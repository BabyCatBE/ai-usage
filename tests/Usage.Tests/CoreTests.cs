using System.Text.Json.Nodes;
using Usage.Core;
using Usage.Core.Link;
using Usage.Core.Providers;

namespace Usage.Tests;

public class ParserTests
{
    [Fact]
    public void Codex_maps_5h_and_weekly_and_plan()
    {
        var r = CodexProvider.Parse(JsonNode.Parse("""
        {"rateLimitsByLimitId":{"codex":{"limitId":"codex","planType":"plus",
          "primary":{"usedPercent":12,"windowDurationMins":300,"resetsAt":1793426161},
          "secondary":{"usedPercent":81,"windowDurationMins":10080,"resetsAt":1793795092},
          "credits":{"hasCredits":false,"unlimited":false,"balance":"0"}}}}
        """));
        Assert.Equal("Plus", r.Plan);
        Assert.Equal(["five_hour", "seven_day"], r.Windows.Select(w => w.Id));
        Assert.Equal(81, r.Windows[1].UsedPercent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1793426161), r.Windows[0].ResetsAt);
    }

    [Fact]
    public void Codex_keeps_unknown_windows_and_values_above_100()
    {
        var r = CodexProvider.Parse(JsonNode.Parse("""{"rateLimits":{"primary":{"usedPercent":130,"windowDurationMins":60,"resetsAt":1793426161}}}"""));
        Assert.Equal("w60", r.Windows[0].Id);
        Assert.Equal(130, r.Windows[0].UsedPercent);
    }

    [Fact]
    public void Codex_without_windows_is_unavailable() =>
        Assert.Equal("USAGE_UNAVAILABLE", Assert.Throws<UsageException>(() => CodexProvider.Parse(JsonNode.Parse("""{"rateLimits":{}}"""))).Code);

    [Fact]
    public void Claude_maps_windows_breakdown_and_plan()
    {
        var r = ClaudeProvider.Parse(JsonNode.Parse("""
        {"subscription_type":"pro","rate_limits_available":true,"rate_limits":{
          "five_hour":{"utilization":13,"resets_at":"2026-10-31T04:50:00.401853+00:00"},
          "seven_day":{"utilization":13.5,"resets_at":"2026-11-04T07:00:00+00:00"},
          "seven_day_opus":null,
          "seven_day_breakdown":{"rows":[{"key":"claude_code","display_name":"Claude Code","percent":100},{"key":"chat","display_name":"Chats","percent":0}]}}}
        """));
        Assert.Equal("Pro", r.Plan);
        Assert.Equal(2, r.Windows.Count);
        Assert.Equal(13.5, r.Windows[1].UsedPercent);
        Assert.Equal(["Claude Code", "Chats"], r.Models!.Select(m => m.Name));
    }

    [Fact]
    public void Idle_five_hour_window_is_zero_not_missing()
    {
        var claude = ClaudeProvider.Parse(JsonNode.Parse("""
          {"rate_limits_available":true,"rate_limits":{"five_hour":null,"seven_day":{"utilization":46,"resets_at":"2026-11-05T07:00:00Z"}}}
          """));
        Assert.Equal(["five_hour", "seven_day"], claude.Windows.Select(w => w.Id));
        Assert.Equal(0, claude.Windows[0].UsedPercent);
        var codex = CodexProvider.Parse(JsonNode.Parse("""{"rateLimits":{"primary":null,"secondary":{"usedPercent":95,"windowDurationMins":10080,"resetsAt":1793426161}}}"""));
        Assert.Equal(["five_hour", "seven_day"], codex.Windows.Select(w => w.Id));
        Assert.Equal(0, codex.Windows[0].UsedPercent);
    }

    [Fact]
    public void Claude_unavailable_when_limits_missing() =>
        Assert.Throws<UsageException>(() => ClaudeProvider.Parse(JsonNode.Parse("""{"rate_limits_available":false}""")));

    [Fact]
    public void OpenCode_null_data_requires_console_login() =>
        Assert.Equal("CONSOLE_LOGIN_REQUIRED", Assert.Throws<UsageException>(() => OpenCodeProvider.Parse(JsonNode.Parse("""{"location":{},"data":null}"""))).Code);

    [Fact]
    public void OpenCode_maps_go_meters_and_models_by_spend()
    {
        var r = OpenCodeProvider.Parse(JsonNode.Parse("""
        {"data":{"workspace":{"id":"w","usageURL":"u"},
          "go":{"rolling":{"percent":23,"limited":false,"resetsAt":1793426161000},
                "weekly":{"percent":48,"limited":false,"resetsAt":1793795092000},
                "monthly":{"percent":15,"limited":false,"resetsAt":1795000000000}},
          "budgets":[{"models":[{"name":"kimi","spent":3.1},{"name":"glm","spent":1.2},{"name":"kimi","spent":1}]}]}}
        """));
        Assert.Equal("Go", r.Plan);
        Assert.Equal(["five_hour", "seven_day", "monthly"], r.Windows.Select(w => w.Id));
        Assert.Equal("kimi", r.Models![0].Name);
        Assert.Equal(4.1, r.Models[0].SpentUsd);
    }
}

public class LinkTests
{
    [Fact]
    public void Code_roundtrip_and_grouping()
    {
        var secret = Pairing.NewSecret();
        var code = Pairing.ToCode(secret);
        Assert.Equal(26, code.Length);
        Assert.Equal(secret, Pairing.FromCode(Pairing.Group(code).ToLowerInvariant()));
    }

    [Fact]
    public void Bad_codes_are_rejected()
    {
        Assert.Null(Pairing.FromCode("ABC"));
        Assert.Null(Pairing.FromCode(""));
    }

    static JsonObject Msg(DateTimeOffset at) => new() { ["t"] = "get", ["ts"] = at.ToUnixTimeMilliseconds() };

    [Fact]
    public void Seal_open_roundtrip_and_direction_binding()
    {
        var keys = Pairing.Derive(Pairing.NewSecret());
        var frame = Pairing.Seal(keys, 'c', Msg(DateTimeOffset.UtcNow));
        Assert.Equal("get", Pairing.Open(keys, 'c', frame, DateTimeOffset.UtcNow)?["t"]?.GetValue<string>());
        Assert.Null(Pairing.Open(keys, 'h', frame, DateTimeOffset.UtcNow)); // reflected message
    }

    [Fact]
    public void Tampering_wrong_key_and_replay_are_rejected()
    {
        var keys = Pairing.Derive(Pairing.NewSecret());
        var frame = Pairing.Seal(keys, 'c', Msg(DateTimeOffset.UtcNow));
        var obj = JsonNode.Parse(frame)!.AsObject();
        var bytes = Convert.FromBase64String(obj["c"]!.GetValue<string>()); bytes[0] ^= 1;
        obj["c"] = Convert.ToBase64String(bytes);
        Assert.Null(Pairing.Open(keys, 'c', obj.ToJsonString(), DateTimeOffset.UtcNow));
        Assert.Null(Pairing.Open(Pairing.Derive(Pairing.NewSecret()), 'c', frame, DateTimeOffset.UtcNow));
        Assert.Null(Pairing.Open(keys, 'c', frame, DateTimeOffset.UtcNow.AddMinutes(11)));
        Assert.Null(Pairing.Open(keys, 'c', "not json", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Ciphertext_does_not_leak_plaintext_and_room_is_not_the_secret()
    {
        var secret = Pairing.NewSecret();
        var keys = Pairing.Derive(secret);
        var frame = Pairing.Seal(keys, 'h', new JsonObject { ["t"] = "snapshot", ["ts"] = 1, ["d"] = "usedPercent-secret-marker" });
        Assert.DoesNotContain("marker", frame);
        Assert.DoesNotContain(Pairing.ToCode(secret), keys.RoomId, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(32, keys.RoomId.Length);
    }

    [Fact]
    public void Device_store_adds_confirms_and_revokes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "usage-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new DeviceStore(dir);
            var d = store.Add("Pixel");
            Assert.Single(store.All());
            store.MarkSeen(d.Id);
            Assert.True(new DeviceStore(dir).All()[0].Confirmed);
            store.Remove(d.Id);
            Assert.Empty(new DeviceStore(dir).All());
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class ServiceTests
{
    sealed class Fake(Func<ProviderReading> f) : IUsageProvider
    {
        public string Id => "fake"; public string Name => "Fake";
        public Task<ProviderReading> CollectAsync(CancellationToken ct) => Task.FromResult(f());
    }

    [Fact]
    public async Task Failure_after_success_keeps_data_as_stale()
    {
        var dir = Path.Combine(Path.GetTempPath(), "usage-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var fail = false;
            using var svc = new UsageService([new Fake(() => fail ? throw new UsageException("AUTH_REQUIRED") : new ProviderReading("X", [new UsageWindow("five_hour", "5h", 40, null, 300)]))], dir);
            var a = await svc.RefreshAsync();
            Assert.Equal(ProviderStatus.Ok, a.Providers[0].Status);
            fail = true;
            var b = await svc.RefreshAsync();
            Assert.Equal(ProviderStatus.Stale, b.Providers[0].Status);
            Assert.Equal("AUTH_REQUIRED", b.Providers[0].ErrorCode);
            Assert.Equal(40, b.Providers[0].Windows[0].UsedPercent);
            Assert.Single(svc.History("fake", "five_hour"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Concurrent_refreshes_share_one_collection()
    {
        var dir = Path.Combine(Path.GetTempPath(), "usage-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var calls = 0;
            using var svc = new UsageService([new Fake(() => { Interlocked.Increment(ref calls); Thread.Sleep(200); return new ProviderReading(null, [new UsageWindow("five_hour", "5h", 1, null, 300)]); })], dir);
            await Task.WhenAll(svc.RefreshAsync(), svc.RefreshAsync(), svc.RefreshAsync());
            Assert.Equal(1, calls);
        }
        finally { Directory.Delete(dir, true); }
    }

    static HistoryPoint Pt(DateTimeOffset now, int minAgo, double pct) => new(now - TimeSpan.FromMinutes(minAgo), pct);

    [Fact]
    public void Forecast_projects_run_out_at_the_pace_of_the_last_hour()
    {
        var now = new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
        // 20 % an hour ago, 50 % now: 30 %/h, the remaining 50 % lasts 100 minutes (reset in 3 h)
        var r = Forecast.Project([Pt(now, 70, 15), Pt(now, 60, 20), Pt(now, 30, 35), Pt(now, 0, 50)], 50, now.AddHours(3), now)!;
        Assert.Equal(30, r.PerHour, 2);
        Assert.Equal(now.AddMinutes(100), r.RunsOutAt);
        Assert.Null(r.AtResetPercent);
    }

    [Fact]
    public void Forecast_reports_percent_at_reset_when_it_lasts_and_ignores_pre_reset_samples()
    {
        var now = new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
        var lasts = Forecast.Project([Pt(now, 60, 40), Pt(now, 0, 45)], 45, now.AddHours(2), now)!;
        Assert.Null(lasts.RunsOutAt);
        Assert.Equal(55, lasts.AtResetPercent!.Value, 2);
        var afterReset = Forecast.Project([Pt(now, 60, 90), Pt(now, 20, 2), Pt(now, 0, 6)], 6, now.AddHours(5), now)!;
        Assert.Equal(20, afterReset.BasisMinutes);
        Assert.Equal(12, afterReset.PerHour, 2);
    }

    [Fact]
    public void Forecast_needs_history_and_fresh_readings()
    {
        var now = new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
        Assert.Null(Forecast.Project([Pt(now, 5, 3), Pt(now, 0, 4)], 4, now.AddHours(1), now));
        Assert.Null(Forecast.Project([Pt(now, 90, 3), Pt(now, 40, 4)], 4, now.AddHours(1), now));   // latest reading is 40 min old
        Assert.Equal(0, Forecast.Project([Pt(now, 60, 4), Pt(now, 0, 4)], 4, now.AddHours(1), now)!.PerHour);
    }
}
