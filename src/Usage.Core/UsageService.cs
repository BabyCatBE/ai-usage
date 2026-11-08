using System.Text;

namespace Usage.Core;

public sealed record HistoryPoint(DateTimeOffset At, double Percent);

/// <summary>
/// Collects every provider in parallel, keeps the last good reading as "stale" when a provider fails,
/// and persists only a small snapshot.json (no database).
/// </summary>
public sealed class UsageService : IDisposable
{
    public static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(75);

    readonly IReadOnlyList<IUsageProvider> providers;
    readonly string file, historyFile;
    readonly object gate = new();
    readonly Dictionary<string, List<HistoryPoint>> history = [];
    readonly object historyLock = new();
    CancellationTokenSource? loop;
    Task<UsageSnapshot>? running;

    public UsageSnapshot? Current { get; private set; }
    public bool IsRefreshing => running is { IsCompleted: false };
    public event Action<UsageSnapshot>? Changed;
    public event Action<bool>? RefreshingChanged;

    public UsageService(IEnumerable<IUsageProvider> providers, string dataDir)
    {
        this.providers = providers.ToList();
        Directory.CreateDirectory(dataDir);
        file = Path.Combine(dataDir, "snapshot.json");
        historyFile = Path.Combine(dataDir, "history.json");
        Current = LoadCached();
        LoadHistory();
    }

    UsageSnapshot? LoadCached()
    {
        try
        {
            var s = Json.Deserialize<UsageSnapshot>(File.ReadAllText(file, Encoding.UTF8));
            if (s is null || s.SchemaVersion != UsageSnapshot.CurrentVersion) return null;
            // Cached readings are by definition not fresh.
            return s with { Providers = s.Providers.Select(p => p.Status == ProviderStatus.Ok ? p with { Status = ProviderStatus.Stale } : p).ToList() };
        }
        catch { return null; }
    }

    /// <summary>Single-flight: concurrent callers share the same collection.</summary>
    public Task<UsageSnapshot> RefreshAsync(CancellationToken ct = default)
    {
        lock (gate)
        {
            if (running is { IsCompleted: false }) return running;
            running = Task.Run(() => CollectAsync(ct), CancellationToken.None);
        }
        RefreshingChanged?.Invoke(true);
        return running;
    }

    async Task<UsageSnapshot> CollectAsync(CancellationToken ct)
    {
        try
        {
            var previous = Current?.Providers.ToDictionary(p => p.Id) ?? [];
            var results = await Task.WhenAll(providers.Select(p => CollectOne(p, previous.GetValueOrDefault(p.Id), ct)));
            var snapshot = new UsageSnapshot(UsageSnapshot.CurrentVersion, DateTimeOffset.UtcNow, results);
            Current = snapshot;
            Record(snapshot);
            Persist(snapshot);
            Changed?.Invoke(snapshot);
            return snapshot;
        }
        finally { RefreshingChanged?.Invoke(false); }
    }

    static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];
    static readonly HashSet<string> Transient = ["USAGE_UNAVAILABLE", "CLI_TIMEOUT", "RPC_FAILED", "CLI_EXITED", "CLI_WRITE_FAILED", "CLI_START_FAILED", "COLLECTION_FAILED", "AUTH_STATUS_INVALID"];

    // Rate-limit protection: the Claude usage endpoint answers 429 when polled too often, and in-cycle retries kept it blocked.
    // Space out successful reads and, on USAGE_UNAVAILABLE, back off exponentially (no in-cycle retries) until the limit clears.
    static readonly Dictionary<string, TimeSpan> MinSpacing = new() { ["claude"] = TimeSpan.FromMinutes(3) };
    static readonly TimeSpan[] RateLimitBackoff = [TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(8), TimeSpan.FromMinutes(15)];
    static readonly Dictionary<string, (int Fails, DateTimeOffset NextAllowed)> cooldown = [];
    static readonly object cooldownLock = new();

    static async Task<ProviderUsage> CollectOne(IUsageProvider provider, ProviderUsage? previous, CancellationToken ct)
    {
        string code = "COLLECTION_FAILED";
        lock (cooldownLock)
            if (previous is { HasData: true } && cooldown.TryGetValue(provider.Id, out var cd) && DateTimeOffset.UtcNow < cd.NextAllowed)
                return previous with { Status = cd.Fails > 0 ? ProviderStatus.Stale : previous.Status, ErrorCode = cd.Fails > 0 ? "USAGE_UNAVAILABLE" : null };
        for (var attempt = 1; attempt <= RetryDelays.Length + 1; attempt++)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ProviderTimeout);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var r = await provider.CollectAsync(cts.Token);
                if (attempt > 1) DiagLog.Write(provider.Id, $"recovered on attempt {attempt} ({sw.ElapsedMilliseconds} ms)");
                lock (cooldownLock)
                    if (MinSpacing.TryGetValue(provider.Id, out var gap)) cooldown[provider.Id] = (0, DateTimeOffset.UtcNow + gap);
                    else cooldown.Remove(provider.Id);
                return new ProviderUsage(provider.Id, provider.Name, ProviderStatus.Ok, DateTimeOffset.UtcNow, null, r.Plan, r.Windows, r.Models ?? [], r.Details ?? []);
            }
            catch (Exception e)
            {
                code = e switch
                {
                    UsageException u => u.Code,
                    OperationCanceledException when !ct.IsCancellationRequested => "CLI_TIMEOUT",
                    OperationCanceledException => "CANCELLED",
                    _ => "COLLECTION_FAILED",
                };
                DiagLog.Write(provider.Id, $"attempt {attempt} failed: {code} after {sw.ElapsedMilliseconds} ms ({e.GetType().Name}){(e is UsageException ? "" : ": " + e.Message)}");
                if (code == "USAGE_UNAVAILABLE")
                {
                    lock (cooldownLock)
                    {
                        var fails = cooldown.TryGetValue(provider.Id, out var c0) ? c0.Fails + 1 : 1;
                        var wait = RateLimitBackoff[Math.Min(fails, RateLimitBackoff.Length) - 1];
                        cooldown[provider.Id] = (fails, DateTimeOffset.UtcNow + wait);
                        DiagLog.Write(provider.Id, $"rate limited; backing off {wait.TotalMinutes:0} min (failure #{fails})");
                    }
                    break;
                }
                if (attempt <= RetryDelays.Length && Transient.Contains(code) && !ct.IsCancellationRequested)
                {
                    try { await Task.Delay(RetryDelays[attempt - 1], ct); } catch (OperationCanceledException) { code = "CANCELLED"; break; }
                    continue;
                }
                break;
            }
        }
        if (previous is { HasData: true })
            return previous with { Status = ProviderStatus.Stale, ErrorCode = code };
        return new ProviderUsage(provider.Id, provider.Name, ProviderStatus.Error, previous?.FetchedAt, code, previous?.Plan, [], [], []);
    }

    void Record(UsageSnapshot s)
    {
        var cutoff = s.GeneratedAt - TimeSpan.FromHours(24);
        lock (historyLock)
            foreach (var p in s.Providers.Where(p => p.Status == ProviderStatus.Ok))
                foreach (var w in p.Windows.Where(w => w.UsedPercent is not null))
                {
                    var key = p.Id + "/" + w.Id;
                    if (!history.TryGetValue(key, out var list)) history[key] = list = [];
                    list.Add(new HistoryPoint(s.GeneratedAt, w.UsedPercent!.Value));
                    list.RemoveAll(x => x.At < cutoff);
                }
        PersistHistory();
    }

    /// <summary>Loads the persisted history so the 24 h chart and the forecast survive an app restart (best-effort).</summary>
    void LoadHistory()
    {
        try
        {
            var loaded = Json.Deserialize<Dictionary<string, List<HistoryPoint>>>(File.ReadAllText(historyFile, Encoding.UTF8));
            if (loaded is null) return;
            var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromHours(24);
            lock (historyLock)
                foreach (var (key, points) in loaded)
                {
                    var kept = points.Where(p => p.At >= cutoff).OrderBy(p => p.At).ToList();
                    if (kept.Count > 0) history[key] = kept;
                }
        }
        catch { /* the history is best-effort */ }
    }

    void PersistHistory()
    {
        try
        {
            string json;
            lock (historyLock) json = Json.Serialize(history);
            var tmp = historyFile + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            File.Move(tmp, historyFile, overwrite: true);
        }
        catch { /* the history is best-effort */ }
    }

    public IReadOnlyList<HistoryPoint> History(string providerId, string windowId)
    {
        lock (historyLock) return history.TryGetValue(providerId + "/" + windowId, out var l) ? l.ToList() : [];
    }

    void Persist(UsageSnapshot s)
    {
        try
        {
            var tmp = file + ".tmp";
            File.WriteAllText(tmp, Json.Serialize(s), new UTF8Encoding(false));
            File.Move(tmp, file, overwrite: true);
        }
        catch { /* the cache is best-effort */ }
    }

    /// <summary>Checked before every automatic tick (not before manual or on-demand refreshes). Return true to skip it.</summary>
    public Func<bool>? SkipAutoRefresh { get; set; }

    public void StartAutoRefresh(TimeSpan interval)
    {
        StopAutoRefresh();
        var cts = loop = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(interval);
            try
            {
                await RefreshAsync(cts.Token);
                while (await timer.WaitForNextTickAsync(cts.Token))
                {
                    bool skip; try { skip = SkipAutoRefresh?.Invoke() == true; } catch { skip = false; }
                    if (!skip) await RefreshAsync(cts.Token);
                }
            }
            catch (OperationCanceledException) { }
        });
    }

    public void StopAutoRefresh() { loop?.Cancel(); loop = null; }
    public void Dispose() => StopAutoRefresh();
}
