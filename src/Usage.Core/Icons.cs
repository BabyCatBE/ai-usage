namespace Usage.Core;

/// <summary>Which glyphs each provider can use. The drawing itself lives in each UI (Windows, Android).</summary>
public static class IconCatalog
{
    public sealed record Option(string Id, string Label);

    public static readonly IReadOnlyList<(string Provider, string Name)> Providers =
        [("codex", "ChatGPT"), ("claude", "Claude"), ("opencode", "OpenCode")];

    public static IReadOnlyList<Option> Options(string provider) => provider switch
    {
        "codex" => [new("chatgpt", "ChatGPT"), new("codex", "Codex")],
        "claude" => [new("claude", "Claude"), new("claude-code", "Claude Code")],
        "opencode" => [new("opencode", "OpenCode"), new("kimi", "Kimi"), new("glm", "GLM"), new("qwen", "Qwen"), new("minimax", "MiniMax"), new("deepseek", "DeepSeek")],
        _ => [new("generic", provider)],
    };

    public static string Default(string provider) => Options(provider)[0].Id;

    /// <summary>The user's choice if it is valid for the provider, otherwise the default.</summary>
    public static string Resolve(string provider, IReadOnlyDictionary<string, string> chosen) =>
        chosen.TryGetValue(provider, out var id) && Options(provider).Any(o => o.Id == id) ? id : Default(provider);
}
