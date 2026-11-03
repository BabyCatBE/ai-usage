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
    public string Style { get; set; } = "neu";       // look: neu (neumorphism) | glass (liquid glass) | cyber (neon cyberpunk) | soft (muted, low-saturation) | cupertino (Apple design, real acrylic glass) | mono (monochrome neumorphism, vibrant black or white)
    public string RelayUrl { get; set; } = Defaults.Relay;
    public string WidgetEdge { get; set; } = "Right";   // Right | Left: docked to the middle of that screen edge
    public bool WidgetEnabled { get; set; } = true;
    public bool WidgetAlwaysOnTop { get; set; } = true;
    /// <summary>Test option: the retracted widget shows every window as concentric rings (outer = shortest window).</summary>
    public bool WidgetAllRings { get; set; } = false;
    public bool HideMissingProviders { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;
    public bool ShowIcons { get; set; } = true;
    public Dictionary<string, string> Icons { get; set; } = [];   // provider id -> icon id

    // ---- window layout and visualization -------------------------------------------------------------------
    public string LayoutMode { get; set; } = "fit";              // fit: everything always visible, scales with the window | scroll: pinned first, scroll for the rest
    public List<string> HiddenProviders { get; set; } = [];      // provider ids the user does not want to see
    public List<string> PinnedProviders { get; set; } = [];      // scroll mode: shown first and highlighted
    public string ViewStyle { get; set; } = "bars";              // bars | segments | rings | numbers
    public string Density { get; set; } = "comfortable";         // comfortable | compact
    public bool ShowResetLine { get; set; } = true;
    public bool ShowModels { get; set; } = true;
    public bool ShowUpdated { get; set; } = true;
    public bool ShowProviderBar { get; set; } = true;            // quick on/off chips under the header
    public string SoftVariant { get; set; } = "soft-slate";      // sub-theme of the soft look (2 dark, 2 light)
    public string CyberVariant { get; set; } = "neon-night";     // sub-theme of the cyberpunk look (several dark, several light)

    // ---- performance (the app must stay invisible on a weak PC) --------------------------------------------
    public bool PowerSaver { get; set; } = true;                 // no blur/shadow effects, lower frame rate, low priority
    public bool PauseWhenIdle { get; set; } = true;              // skip automatic refreshes while you are away from the PC

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
