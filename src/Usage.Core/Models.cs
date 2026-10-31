using System.Text.Json;
using System.Text.Json.Serialization;

namespace Usage.Core;

[JsonConverter(typeof(JsonStringEnumConverter<ProviderStatus>))]
public enum ProviderStatus { Ok, Stale, Error }

/// <summary>One quota window, e.g. five_hour, seven_day, monthly.</summary>
public sealed record UsageWindow(string Id, string Label, double? UsedPercent, DateTimeOffset? ResetsAt, int? DurationMinutes);

/// <summary>Per-model (or per-product) breakdown. Either value can be absent.</summary>
public sealed record ModelUsage(string Name, double? UsedPercent, double? SpentUsd);

public sealed record Detail(string Label, string Value);

public sealed record ProviderUsage(
    string Id,
    string Name,
    ProviderStatus Status,
    DateTimeOffset? FetchedAt,
    string? ErrorCode,
    string? Plan,
    IReadOnlyList<UsageWindow> Windows,
    IReadOnlyList<ModelUsage> Models,
    IReadOnlyList<Detail> Details)
{
    [JsonIgnore]
    public bool HasData => Windows.Any(w => w.UsedPercent is not null);
}

public sealed record UsageSnapshot(int SchemaVersion, DateTimeOffset GeneratedAt, IReadOnlyList<ProviderUsage> Providers)
{
    public const int CurrentVersion = 2;
}

/// <summary>What a provider returns on success.</summary>
public sealed record ProviderReading(string? Plan, IReadOnlyList<UsageWindow> Windows, IReadOnlyList<ModelUsage>? Models = null, IReadOnlyList<Detail>? Details = null);

/// <summary>Safe, fixed error codes. Never carries stderr, paths or credentials.</summary>
public sealed class UsageException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}

public interface IUsageProvider
{
    string Id { get; }
    string Name { get; }
    Task<ProviderReading> CollectAsync(CancellationToken ct);
}
