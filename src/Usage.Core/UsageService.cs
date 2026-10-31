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
    readonly string file;
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
        Current = LoadCached();
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

    static async Task<ProviderUsage> CollectOne(IUsageProvider provider, ProviderUsage? previous, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ProviderTimeout);
        try
        {
            var r = await provider.CollectAsync(cts.Token);
            return new ProviderUsage(provider.Id, provider.Name, ProviderStatus.Ok, DateTimeOffset.UtcNow, null, r.Plan, r.Windows, r.Models ?? [], r.Details ?? []);
        }
        catch (Exception e)
        {
            var code = e switch
            {
                UsageException u => u.Code,
                OperationCanceledException when !ct.IsCancellationRequested => "CLI_TIMEOUT",
                OperationCanceledException => "CANCELLED",
                _ => "COLLECTION_FAILED",
            };
            if (previous is { HasData: true })
                return previous with { Status = ProviderStatus.Stale, ErrorCode = code };
            return new ProviderUsage(provider.Id, provider.Name, ProviderStatus.Error, previous?.FetchedAt, code, previous?.Plan, [], [], []);
        }
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
                while (await timer.WaitForNextTickAsync(cts.Token)) await RefreshAsync(cts.Token);
            }
            catch (OperationCanceledException) { }
        });
    }

    public void StopAutoRefresh() { loop?.Cancel(); loop = null; }
    public void Dispose() => StopAutoRefresh();
}
