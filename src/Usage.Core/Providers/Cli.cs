using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace Usage.Core.Providers;

/// <summary>Locates the official CLIs. Authentication is always delegated to them; credential files are never read.</summary>
public static class Cli
{
    public sealed record Command(string File, string[] Prefix);

    static readonly bool Win = OperatingSystem.IsWindows();

    static IEnumerable<string> PathDirs() =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

    static string? OnPath(params string[] names)
    {
        foreach (var dir in PathDirs())
            foreach (var n in names)
            {
                var p = Path.Combine(dir, n);
                if (File.Exists(p)) return p;
            }
        return null;
    }

    static string? Existing(string p) => File.Exists(p) ? p : null;

    public static string? Node() => OnPath(Win ? "node.exe" : "node")
        ?? (Win ? Existing(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe")) : null);

    static string NpmRoot()
    {
        var appData = Environment.GetEnvironmentVariable("APPDATA");
        return appData is null ? "" : Path.Combine(appData, "npm", "node_modules");
    }

    /// <param name="npmPackage">e.g. "@openai/codex"</param>
    public static Command? Find(string name, string? npmPackage = null, params string[] npmEntries)
    {
        var over = Environment.GetEnvironmentVariable("USAGE_" + name.ToUpperInvariant() + "_EXE");
        if (!string.IsNullOrEmpty(over)) return File.Exists(over) ? new Command(over, []) : null;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "";
        var native = new List<string?>();
        if (Win)
        {
            native.Add(OnPath(name + ".exe"));
            native.Add(Existing(Path.Combine(home, ".local", "bin", name + ".exe")));
            var codexBin = Path.Combine(local, "OpenAI", "Codex", "bin");
            if (name == "codex" && Directory.Exists(codexBin))
                native.AddRange(Directory.GetDirectories(codexBin).Select(d => Existing(Path.Combine(d, "codex.exe"))));
        }
        else
        {
            native.Add(OnPath(name));
            native.Add(Existing(Path.Combine(home, ".local", "bin", name)));
            native.Add(Existing(Path.Combine(home, "." + name, "bin", name)));
        }
        var exe = native.FirstOrDefault(p => p is not null);
        if (exe is not null) return new Command(exe, []);

        if (npmPackage is not null)
        {
            var root = Path.Combine(NpmRoot(), npmPackage.Replace('/', Path.DirectorySeparatorChar));
            foreach (var entry in npmEntries)
            {
                var p = Path.Combine(root, entry.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(p)) continue;
                if (p.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                {
                    var node = Node();
                    if (node is not null) return new Command(node, [p]);
                }
                else return new Command(p, []);
            }
        }
        return null;
    }

    /// <summary>Drops vendor auth/behaviour variables so the CLIs read only their own configuration.</summary>
    public static void CleanEnvironment(ProcessStartInfo psi)
    {
        foreach (var key in psi.Environment.Keys.ToList())
            if (key.StartsWith("ANTHROPIC_", StringComparison.OrdinalIgnoreCase) || key.StartsWith("CLAUDE_", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("OPENAI_", StringComparison.OrdinalIgnoreCase) || key.StartsWith("CODEX_", StringComparison.OrdinalIgnoreCase)
                || key.Equals("NODE_OPTIONS", StringComparison.OrdinalIgnoreCase))
                psi.Environment.Remove(key);
    }

    public static Process Start(Command cmd, IEnumerable<string> args, string? cwd = null, Action<ProcessStartInfo>? configure = null)
    {
        var psi = new ProcessStartInfo(cmd.File)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (cwd is not null) { Directory.CreateDirectory(cwd); psi.WorkingDirectory = cwd; }
        foreach (var a in cmd.Prefix.Concat(args)) psi.ArgumentList.Add(a);
        CleanEnvironment(psi);
        configure?.Invoke(psi);
        var proc = Process.Start(psi) ?? throw new UsageException("CLI_START_FAILED");
        // the collectors are background work: never let them compete with what the user is doing
        try { proc.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
        return proc;
    }

    public static void Kill(Process p)
    {
        try { p.StandardInput.Close(); } catch { }
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
    }

    /// <summary>Runs a short command and returns stdout. Output is never surfaced to logs.</summary>
    public static async Task<(bool Ok, string Stdout, string Stderr)> RunAsync(Command cmd, IEnumerable<string> args, TimeSpan timeout, CancellationToken ct, string? cwd = null)
    {
        using var p = Start(cmd, args, cwd);
        p.StandardInput.Close();
        var err = p.StandardError.ReadToEndAsync(CancellationToken.None);
        var outTask = p.StandardOutput.ReadToEndAsync(CancellationToken.None);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            Kill(p);
            if (ct.IsCancellationRequested) throw;
            throw new UsageException("CLI_TIMEOUT");
        }
        var stdout = await outTask; var stderr = await err;
        return (p.ExitCode == 0, stdout, stderr);
    }

    /// <summary>Line-delimited JSON request/response over stdio (used by Codex and Claude).</summary>
    public sealed class JsonRpc : IAsyncDisposable
    {
        readonly Process proc;
        readonly Dictionary<string, TaskCompletionSource<JsonNode?>> pending = [];
        readonly Func<JsonObject, (string? Id, JsonNode? Result, bool Error)?> match;
        readonly string prefix;
        int next;

        public JsonRpc(Process p, Func<JsonObject, (string? Id, JsonNode? Result, bool Error)?> matcher, string idPrefix = "")
        {
            proc = p; match = matcher; prefix = idPrefix;
            _ = Task.Run(async () => { try { while (await p.StandardError.ReadLineAsync() is not null) { } } catch { } });
            _ = Task.Run(ReadLoop);
        }

        async Task ReadLoop()
        {
            try
            {
                string? line;
                while ((line = await proc.StandardOutput.ReadLineAsync()) is not null)
                {
                    JsonNode? node; try { node = JsonNode.Parse(line); } catch { continue; }
                    if (node is not JsonObject obj) continue;
                    var m = match(obj);
                    if (m is null || m.Value.Id is null) continue;
                    TaskCompletionSource<JsonNode?>? tcs;
                    lock (pending) { pending.Remove(m.Value.Id, out tcs); }
                    if (tcs is null) continue;
                    if (m.Value.Error) tcs.TrySetException(new UsageException("RPC_FAILED"));
                    else tcs.TrySetResult(m.Value.Result);
                }
            }
            catch { }
            finally
            {
                lock (pending) { foreach (var t in pending.Values) t.TrySetException(new UsageException("CLI_EXITED")); pending.Clear(); }
            }
        }

        public void Send(JsonNode message)
        {
            try { proc.StandardInput.WriteLine(message.ToJsonString()); proc.StandardInput.Flush(); }
            catch { throw new UsageException("CLI_WRITE_FAILED"); }
        }

        public async Task<JsonNode?> CallAsync(Func<string, JsonNode> build, TimeSpan timeout, CancellationToken ct)
        {
            var id = prefix + Interlocked.Increment(ref next);
            var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (pending) pending[id] = tcs;
            Send(build(id));
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            try { return await tcs.Task.WaitAsync(cts.Token); }
            catch (OperationCanceledException)
            {
                lock (pending) pending.Remove(id);
                if (ct.IsCancellationRequested) throw;
                throw new UsageException("CLI_TIMEOUT");
            }
        }

        public ValueTask DisposeAsync() { Kill(proc); proc.Dispose(); return ValueTask.CompletedTask; }
    }
}
