using System.Text;
using System.Text.Json;
using Usage.Core.Providers;

namespace Usage.Core;

public static class AppPaths
{
    public static string Data { get; } = Environment.GetEnvironmentVariable("USAGE_DATA_DIR") is { Length: > 0 } d ? d
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsageMonitor");
    public static string CodexState => Path.Combine(Data, "codex-state");
    public static string ClaudeRuntime => Path.Combine(Data, "claude-runtime");

    public static List<IUsageProvider> DefaultProviders() =>
        [new CodexProvider(CodexState), new ClaudeProvider(ClaudeRuntime), new OpenCodeProvider()];
}

/// <summary>Public relay used when the user does not run their own.</summary>
public static class Defaults
{
    public const string Relay = "wss://usage-relay.neves-lab.workers.dev";
}

public sealed class Settings
{
    public int IntervalMinutes { get; set; } = 5;
    public string Language { get; set; } = "auto";   // auto | pt | en
    public string Theme { get; set; } = "auto";      // colour mode: auto | light | dark
    public string Style { get; set; } = "neu";       // look: neu (soft neumorphism) | glass (liquid glass)
    public string RelayUrl { get; set; } = Defaults.Relay;
    public string WidgetEdge { get; set; } = "Right";   // Right | Left: docked to the middle of that screen edge
    public bool WidgetEnabled { get; set; } = true;
    public bool WidgetAlwaysOnTop { get; set; } = true;
    public bool HideMissingProviders { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;
    public bool ShowIcons { get; set; } = true;
    public Dictionary<string, string> Icons { get; set; } = [];   // provider id -> icon id

    static string FilePath => Path.Combine(AppPaths.Data, "settings.json");

    public static Settings Load()
    {
        Settings s;
        try { s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath, Encoding.UTF8), Json.Options) ?? new(); }
        catch { s = new(); }
        if (Environment.GetEnvironmentVariable("USAGE_RELAY_URL") is { Length: > 0 } relay) s.RelayUrl = relay;
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Data);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions(Json.Options) { WriteIndented = true }), new UTF8Encoding(false));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch { }
    }
}
