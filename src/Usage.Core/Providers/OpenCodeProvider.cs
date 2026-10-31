using System.Text.Json.Nodes;

namespace Usage.Core.Providers;

/// <summary>
/// OpenCode Go limits via the CLI's local API (`opencode api GET /api/experimental/usage/limits`).
/// The endpoint only answers once the OpenCode Console account is connected (`opencode auth login`).
/// </summary>
public sealed class OpenCodeProvider : IUsageProvider
{
    public string Id => "opencode";
    public string Name => "OpenCode";

    public async Task<ProviderReading> CollectAsync(CancellationToken ct)
    {
        var cli = Cli.Find("opencode", "@opencode/cli", "bin/opencode.exe", "bin/opencode") ?? throw new UsageException("CLI_NOT_FOUND");
        var run = await Cli.RunAsync(cli, ["api", "GET", "/api/experimental/usage/limits"], TimeSpan.FromSeconds(45), ct);
        if (!run.Ok) throw new UsageException("CLI_FAILED");
        JsonNode? root;
        try { root = JsonNode.Parse(run.Stdout); } catch { throw new UsageException("INVALID_RESPONSE"); }
        return Parse(root);
    }

    public static ProviderReading Parse(JsonNode? root)
    {
        if (root is not JsonObject obj || !obj.ContainsKey("data")) throw new UsageException("INVALID_RESPONSE");
        if (obj["data"] is not JsonObject data) throw new UsageException("CONSOLE_LOGIN_REQUIRED");

        var windows = new List<UsageWindow>();
        if (data["go"] is JsonObject go)
        {
            void Add(string key, string id, string label, int mins)
            {
                if (go[key] is JsonObject m && m["percent"] is not null)
                    windows.Add(new UsageWindow(id, label, Math.Clamp((double)m["percent"]!, 0, 100000), FromMillis(m["resetsAt"]), mins));
            }
            Add("rolling", "five_hour", "5h", 300);
            Add("weekly", "seven_day", "7d", 10080);
            Add("monthly", "monthly", "30d", 43200);
        }

        var models = new List<ModelUsage>();
        if (data["budgets"] is JsonArray budgets)
            foreach (var budget in budgets.OfType<JsonObject>())
                if (budget["models"] is JsonArray ms)
                    foreach (var m in ms.OfType<JsonObject>())
                        if (m["name"] is not null && m["spent"] is not null)
                            models.Add(new ModelUsage(m["name"]!.GetValue<string>(), null, (double)m["spent"]!));
        var merged = models.GroupBy(m => m.Name).Select(g => new ModelUsage(g.Key, null, g.Sum(x => x.SpentUsd ?? 0)))
            .OrderByDescending(m => m.SpentUsd).Take(12).ToList();

        if (windows.Count == 0 && merged.Count == 0) throw new UsageException("USAGE_UNAVAILABLE");
        return new ProviderReading(windows.Count > 0 ? "Go" : null, windows, merged);
    }

    static DateTimeOffset? FromMillis(JsonNode? n) => n is null ? null : DateTimeOffset.FromUnixTimeMilliseconds((long)(double)n);
}
