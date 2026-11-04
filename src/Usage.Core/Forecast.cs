namespace Usage.Core;

/// <summary>Where a usage window is heading at the pace of the last hour.</summary>
/// <param name="PerHour">Percentage points per hour over <paramref name="BasisMinutes"/>.</param>
/// <param name="RunsOutAt">When the window would reach 100 % at this pace; null if the reset comes first (or nothing is being used).</param>
/// <param name="AtResetPercent">Expected percentage at the reset when the window does not run out first.</param>
public sealed record ForecastResult(double PerHour, int BasisMinutes, DateTimeOffset? RunsOutAt, double? AtResetPercent);

/// <summary>Beta: "if I keep using it like this, when does it run out?" from the in-memory history of readings.</summary>
public static class Forecast
{
    static readonly TimeSpan Target = TimeSpan.FromHours(1), MinBasis = TimeSpan.FromMinutes(12), MaxBasis = TimeSpan.FromHours(2), MaxAge = TimeSpan.FromMinutes(15);

    /// <summary>Null while there is not enough history (about 12 minutes of readings since the last reset) or the latest reading is stale.</summary>
    public static ForecastResult? Project(IReadOnlyList<HistoryPoint> history, double nowPercent, DateTimeOffset? resetsAt, DateTimeOffset now)
    {
        var sorted = history.Where(h => h.At <= now).OrderBy(h => h.At).ToList();
        if (sorted.Count == 0 || now - sorted[^1].At > MaxAge) return null;
        // only the run since the last reset (the percentage dropped)
        var start = 0;
        for (var i = 1; i < sorted.Count; i++) if (sorted[i].Percent + 0.5 < sorted[i - 1].Percent) start = i;
        var run = sorted.Skip(start).ToList();
        // newest reading at least an hour old but not older than two hours (a PC asleep for hours would dilute the pace)
        var basis = run.LastOrDefault(h => now - h.At >= Target && now - h.At <= MaxBasis) ?? run.FirstOrDefault(h => now - h.At <= MaxBasis);
        if (basis is null || now - basis.At < MinBasis) return null;
        var span = now - basis.At;
        var perHour = Math.Max(0, (nowPercent - basis.Percent) / span.TotalHours);
        var basisMin = (int)span.TotalMinutes;
        if (perHour < 0.05) return new ForecastResult(0, basisMin, null, nowPercent);
        var left = Math.Max(0, 100 - nowPercent);
        var toEmpty = TimeSpan.FromHours(left / perHour);
        var untilReset = resetsAt is { } r && r > now ? r - now : (TimeSpan?)null;
        if (untilReset is { } u && toEmpty >= u) return new ForecastResult(perHour, basisMin, null, Math.Min(100, nowPercent + perHour * u.TotalHours));
        return new ForecastResult(perHour, basisMin, now + toEmpty, null);
    }
}
