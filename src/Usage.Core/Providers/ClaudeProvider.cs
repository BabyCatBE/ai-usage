using System.Text.Json.Nodes;

namespace Usage.Core.Providers;

/// <summary>Claude subscription limits via the official CLI's `get_usage` control request. No prompt is ever sent.</summary>
public sealed class ClaudeProvider : IUsageProvider
{
    public string Id => "claude";
    public string Name => "Claude";
    readonly string runtimeDir;
    public ClaudeProvider(string runtimeDir) => this.runtimeDir = runtimeDir;

    public async Task<ProviderReading> CollectAsync(CancellationToken ct)
    {
        var cli = Cli.Find("claude", "@anthropic-ai/claude-code", "bin/claude.exe", "cli.js") ?? throw new UsageException("CLI_NOT_FOUND");
        var auth = await Cli.RunAsync(cli, ["auth", "status"], TimeSpan.FromSeconds(15), ct);
        JsonNode? account = null;
        try { account = JsonNode.Parse(auth.Stdout); } catch { }
        if (account is null) throw new UsageException("AUTH_STATUS_INVALID");
        if (!auth.Ok || account["loggedIn"]?.GetValue<bool>() != true || account["authMethod"]?.GetValue<string>() != "claude.ai")
            throw new UsageException("AUTH_REQUIRED");

        var proc = Cli.Start(cli, ["--safe-mode", "--strict-mcp-config", "--no-session-persistence", "--print",
            "--input-format", "stream-json", "--output-format", "stream-json", "--verbose"], runtimeDir);
        await using var rpc = new Cli.JsonRpc(proc, Match, "usage-");
        JsonNode Req(string id, string subtype, JsonObject? extra = null)
        {
            var req = new JsonObject { ["subtype"] = subtype };
            if (extra is not null) foreach (var kv in extra) req[kv.Key] = kv.Value?.DeepClone();
            return new JsonObject { ["type"] = "control_request", ["request_id"] = id, ["request"] = req };
        }
        await rpc.CallAsync(id => Req(id, "initialize"), TimeSpan.FromSeconds(30), ct);
        var usage = await rpc.CallAsync(id => Req(id, "get_usage", new JsonObject { ["skip_behaviors"] = true }), TimeSpan.FromSeconds(30), ct);
        return Parse(usage, account["subscriptionType"]?.GetValue<string>());
    }

    static (string? Id, JsonNode? Result, bool Error)? Match(JsonObject o)
    {
        if (o["type"]?.GetValue<string>() != "control_response" || o["response"] is not JsonObject r) return null;
        return (r["request_id"]?.GetValue<string>(), r["response"], r["subtype"]?.GetValue<string>() != "success");
    }

    public static ProviderReading Parse(JsonNode? usage, string? fallbackPlan = null)
    {
        if (usage?["rate_limits_available"]?.GetValue<bool>() != true || usage["rate_limits"] is not JsonObject limits)
            throw new UsageException("USAGE_UNAVAILABLE");
        var windows = new List<UsageWindow>();
        void Add(string key, string id, string label, int mins)
        {
            if (limits[key] is JsonObject w && w["utilization"] is not null)
                windows.Add(new UsageWindow(id, label, (double)w["utilization"]!, ParseDate(w["resets_at"]), mins));
        }
        Add("five_hour", "five_hour", "5h", 300);
        Add("seven_day", "seven_day", "7d", 10080);
        Add("seven_day_opus", "seven_day_opus", "Opus 7d", 10080);
        Add("seven_day_sonnet", "seven_day_sonnet", "Sonnet 7d", 10080);
        if (windows.Count == 0) throw new UsageException("USAGE_UNAVAILABLE");

        var models = new List<ModelUsage>();
        if (limits["seven_day_breakdown"]?["rows"] is JsonArray rows)
            foreach (var row in rows.OfType<JsonObject>())
                if (row["display_name"] is not null && row["percent"] is not null)
                    models.Add(new ModelUsage(row["display_name"]!.GetValue<string>(), (double)row["percent"]!, null));

        var details = new List<Detail>();
        if (limits["spend"] is JsonObject spend && spend["enabled"]?.GetValue<bool>() == true && spend["used"] is JsonObject used)
        {
            var amount = (double?)used["amount_minor"] ?? 0;
            var exp = (int?)used["exponent"] ?? 2;
            details.Add(new Detail("extra_usage", (amount / Math.Pow(10, exp)).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " " + used["currency"]));
        }
        var plan = usage["subscription_type"]?.GetValue<string>() ?? fallbackPlan;
        return new ProviderReading(plan is null or "" ? null : char.ToUpperInvariant(plan[0]) + plan[1..], windows, models, details);
    }

    static DateTimeOffset? ParseDate(JsonNode? n) =>
        n is not null && DateTimeOffset.TryParse(n.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d.ToUniversalTime() : null;
}
