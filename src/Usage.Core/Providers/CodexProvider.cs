using System.Text.Json.Nodes;

namespace Usage.Core.Providers;

/// <summary>ChatGPT / Codex limits via `codex app-server` (JSON-RPC on stdio).</summary>
public sealed class CodexProvider : IUsageProvider
{
    public string Id => "codex";
    public string Name => "ChatGPT";
    readonly string stateDir;
    public CodexProvider(string stateDir) => this.stateDir = stateDir;

    public async Task<ProviderReading> CollectAsync(CancellationToken ct)
    {
        var cli = Cli.Find("codex", "@openai/codex", "bin/codex.js") ?? throw new UsageException("CLI_NOT_FOUND");
        var status = await Cli.RunAsync(cli, ["login", "status"], TimeSpan.FromSeconds(15), ct);
        if (!status.Ok || !(status.Stdout + status.Stderr).Contains("Logged in", StringComparison.OrdinalIgnoreCase)) throw new UsageException("AUTH_REQUIRED");

        var logs = Path.Combine(stateDir, "logs");
        Directory.CreateDirectory(logs);
        static string Toml(string p) => System.Text.Json.JsonSerializer.Serialize(p.Replace('\\', '/'));
        var proc = Cli.Start(cli, ["app-server", "--listen", "stdio://", "-c", "sqlite_home=" + Toml(stateDir), "-c", "log_dir=" + Toml(logs), "-c", "analytics.enabled=false"],
            stateDir, psi => psi.Environment["CODEX_HOME"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"));
        await using var rpc = new Cli.JsonRpc(proc, Match);
        await rpc.CallAsync(id => new JsonObject { ["id"] = int.Parse(id), ["method"] = "initialize",
            ["params"] = new JsonObject { ["clientInfo"] = new JsonObject { ["name"] = "usage_monitor", ["title"] = "Usage", ["version"] = "1.0.0" } } }, TimeSpan.FromSeconds(30), ct);
        rpc.Send(new JsonObject { ["method"] = "initialized", ["params"] = new JsonObject() });
        JsonNode? result;
        try { result = await rpc.CallAsync(id => Read(id, true), TimeSpan.FromSeconds(30), ct); }
        catch (UsageException) { result = await rpc.CallAsync(id => Read(id, false), TimeSpan.FromSeconds(30), ct); }
        return Parse(result);
    }

    static JsonNode Read(string id, bool exclude) => new JsonObject
    {
        ["id"] = int.Parse(id),
        ["method"] = "account/rateLimits/read",
        ["params"] = exclude ? new JsonObject { ["excludeResetCreditDetails"] = true } : new JsonObject(),
    };

    static (string? Id, JsonNode? Result, bool Error)? Match(JsonObject o)
    {
        if (o.ContainsKey("method") || !o.ContainsKey("id")) return null;
        if (!o.ContainsKey("result") && !o.ContainsKey("error")) return null;
        return (o["id"]?.ToString(), o["result"], o.ContainsKey("error"));
    }

    public static ProviderReading Parse(JsonNode? result)
    {
        var map = result?["rateLimitsByLimitId"] as JsonObject;
        var bucket = (map is { Count: > 0 } ? map["codex"] : result?["rateLimits"]) as JsonObject
            ?? throw new UsageException("USAGE_UNAVAILABLE");
        var windows = new List<UsageWindow>();
        foreach (var key in new[] { "primary", "secondary" })
        {
            if (bucket[key] is not JsonObject w || w["usedPercent"] is null) continue;
            var mins = (int?)w["windowDurationMins"];
            var (id, label) = mins switch
            {
                300 => ("five_hour", "5h"),
                10080 => ("seven_day", "7d"),
                null => (key, key),
                _ => ("w" + mins, Humanize(mins.Value)),
            };
            windows.Add(new UsageWindow(id, label, (double)w["usedPercent"]!, FromEpoch(w["resetsAt"]), mins));
        }
        if (windows.Count == 0) throw new UsageException("USAGE_UNAVAILABLE");
        // The 5-hour window is reported as null until it starts (nothing used since the reset): it is at 0 %, not missing.
        if (bucket.ContainsKey("primary") && (bucket["primary"] is not JsonObject p0 || p0["usedPercent"] is null) && windows.All(w => w.Id != "five_hour") && windows.Any(w => w.Id == "seven_day"))
            windows.Insert(0, new UsageWindow("five_hour", "5h", 0, null, 300));
        var details = new List<Detail>();
        if (bucket["credits"] is JsonObject c && c["hasCredits"]?.GetValue<bool>() == true)
            details.Add(new Detail("credits", c["unlimited"]?.GetValue<bool>() == true ? "unlimited" : c["balance"]?.ToString() ?? "0"));
        var plan = bucket["planType"]?.GetValue<string>();
        return new ProviderReading(plan is null ? null : Capitalize(plan), windows, null, details);
    }

    static string Humanize(int mins) => mins % 1440 == 0 ? mins / 1440 + "d" : mins % 60 == 0 ? mins / 60 + "h" : mins + "m";
    static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    static DateTimeOffset? FromEpoch(JsonNode? n) => n is null ? null : DateTimeOffset.FromUnixTimeSeconds((long)n);
}
