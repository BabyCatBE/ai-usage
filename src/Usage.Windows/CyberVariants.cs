using System.Windows.Media;

namespace Usage.Windows;

/// <summary>
/// One sub-theme of the cyberpunk look. Primary and Secondary draw the neon rim, ticks and glow; the three Bg stops paint the backdrop;
/// Codex/Claude/Open are the provider accents (distinct from each other and from the status colours).
/// </summary>
public sealed record CyberVariant(
    string Id, string Name, bool Dark,
    Color Base, Color Ink, Color Text, Color Dim,
    Color Primary, Color Secondary, Color Warn, Color Crit,
    Color BgA, Color BgB, Color BgC, Color Glow1, Color Glow2,
    Color Codex, Color Claude, Color Open, bool Soft = false)
{
    static Color C(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    public static readonly IReadOnlyList<CyberVariant> All =
    [
        // ---- dark ----
        new("neon-night", "Neon Night", true, C(0x0B0714), C(0x05030B), C(0xEAF6FF), C(0x9FB0E8), C(0x00E5FF), C(0xFF2BD6), C(0xFCEE0A), C(0xFF2E6E),
            C(0x0B0714), C(0x14072A), C(0x05101E), C(0xFF2BD6), C(0x00E5FF), C(0x00FFB2), C(0xFF7A1A), C(0xC76BFF)),
        new("black-yellow", "Black & Yellow", true, C(0x0A0A08), C(0x000000), C(0xFFF8D6), C(0xC9BE8A), C(0xFCEE0A), C(0xF5A300), C(0xFF9F1C), C(0xFF3B30),
            C(0x0A0A08), C(0x15140A), C(0x0A0A08), C(0xFCEE0A), C(0xF5A300), C(0x00F0FF), C(0xFF6A00), C(0xFF3EA5)),
        new("purple-blue", "Purple Haze", true, C(0x0C0824), C(0x05031A), C(0xEEEBFF), C(0xA9A6E8), C(0x8B5CF6), C(0x38BDF8), C(0xFBBF24), C(0xFB4B7B),
            C(0x0C0824), C(0x1A0B4A), C(0x07183A), C(0x8B5CF6), C(0x38BDF8), C(0x34F5C5), C(0xFF8A5B), C(0xE879F9)),
        new("toxic-green", "Toxic Matrix", true, C(0x04100A), C(0x010805), C(0xE6FFEE), C(0x8FD1A6), C(0x39FF14), C(0x00E5A8), C(0xFFD60A), C(0xFF3B5C),
            C(0x03100A), C(0x05200F), C(0x021A18), C(0x39FF14), C(0x00E5A8), C(0x7CFF6B), C(0xFF9A3D), C(0x5CE1FF)),
        new("crimson", "Crimson Blade", true, C(0x0F0508), C(0x070204), C(0xFFEDEF), C(0xD69AA4), C(0xFF1F5A), C(0xB02BFF), C(0xFFC107), C(0xFF6B6B),
            C(0x0F0508), C(0x2A0714), C(0x140A24), C(0xFF1F5A), C(0x7A2BFF), C(0x00F0FF), C(0xFFA94D), C(0xD16BFF)),
        // ---- light ----
        new("pop-light", "Pop Pink & Cyan", false, C(0xF4F0FF), C(0xD8D2F0), C(0x2A1B4D), C(0x6B5B95), C(0xE01EA8), C(0x00A8C6), C(0xD98300), C(0xD6204A),
            C(0xEDE7FF), C(0xFBE7F5), C(0xDDF6FB), C(0xFF7AD9), C(0x7AE7FF), C(0x00A37A), C(0xE8590C), C(0x7C3AED)),
        new("sun-light", "Sunny Gold & Black", false, C(0xFFFCE8), C(0xEBE2A8), C(0x15130A), C(0x6B6330), C(0xD9A400), C(0x111111), C(0xB26A00), C(0xC62828),
            C(0xFFF9CC), C(0xFFFDF0), C(0xFFF1A8), C(0xFFE600), C(0xFFB300), C(0x00796B), C(0xD84300), C(0x6A1B9A)),
        new("lavender-light", "Lavender Circuit", false, C(0xEEF2FF), C(0xD6DBF5), C(0x1B1B3A), C(0x5B5F97), C(0x6D28D9), C(0x2563EB), C(0xC77700), C(0xD91E4A),
            C(0xE8ECFF), C(0xF0E8FF), C(0xE0F0FF), C(0xA78BFA), C(0x60A5FA), C(0x0D9488), C(0xEA580C), C(0x9333EA)),
        new("mint-light", "Mint Neon", false, C(0xECFDF5), C(0xC7EEDC), C(0x06281C), C(0x3F7A62), C(0x059669), C(0x0891B2), C(0xB45309), C(0xDC2626),
            C(0xE4FBF0), C(0xEFFEF8), C(0xDAF5F7), C(0x34D399), C(0x22D3EE), C(0x047857), C(0xEA580C), C(0x7C3AED)),
        // ---- soft (muted, low saturation; text >= 12:1, secondary text >= 5.4:1 on Base) ----
        new("soft-slate", "Slate", true, C(0x1D1F24), C(0x14161A), C(0xE6E7EC), C(0x9EA2AE), C(0x8FA6C9), C(0xA99FCB), C(0xD6B070), C(0xE0908C),
            C(0x1D1F24), C(0x20232A), C(0x1B1E23), C(0x8FA6C9), C(0xA99FCB), C(0x86BDA9), C(0xDBA283), C(0xB0A3D6), true),
        new("soft-dusk", "Dusk", true, C(0x211F26), C(0x17151B), C(0xE8E6EC), C(0xA09DAC), C(0xA6A0D0), C(0x86AFC4), C(0xD6B070), C(0xE0908C),
            C(0x211F26), C(0x25222D), C(0x1E1D24), C(0xA6A0D0), C(0x86AFC4), C(0x86BDA9), C(0xDBA283), C(0xC0A8D6), true),
        new("soft-mist", "Mist", false, C(0xF2F2F6), C(0xDADBE2), C(0x2A2C33), C(0x5F636E), C(0x4F6F9C), C(0x7B70A6), C(0x93660F), C(0xAB4440),
            C(0xF2F2F6), C(0xECEEF3), C(0xF5F3F0), C(0x4F6F9C), C(0x7B70A6), C(0x3F7F6D), C(0xA2653F), C(0x6F5FA3), true),
        new("soft-sand", "Sand", false, C(0xF5F1EA), C(0xDFD8CC), C(0x2E2A25), C(0x665F55), C(0x5F7352), C(0x9A7A5A), C(0x8F6410), C(0xA8453C),
            C(0xF5F1EA), C(0xF0EBE2), C(0xF7F4EE), C(0x5F7352), C(0x9A7A5A), C(0x4A7F68), C(0xA5603A), C(0x71619B), true),
    ];

    public static CyberVariant Get(string? id) => All.FirstOrDefault(v => v.Id == id && !v.Soft) ?? All.First(v => !v.Soft);
    public static CyberVariant GetSoft(string? id) => All.FirstOrDefault(v => v.Id == id && v.Soft) ?? All.First(v => v.Soft);
}
