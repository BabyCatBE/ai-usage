using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Win32;
using Usage.Core;

namespace Usage.Windows;

/// <summary>Language: Portuguese when selected (or when the system is Portuguese), English otherwise.</summary>
public static class Loc
{
    public static bool Pt { get; private set; }
    public static void Apply(string setting) =>
        Pt = setting == "pt" || (setting != "en" && CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pt");
    public static string T(string pt, string en) => Pt ? pt : en;

    public static string WindowName(string id, string label, bool shortName = false) => id switch
    {
        "five_hour" => shortName ? "5h" : T("5 horas", "5 hours"),
        "seven_day" => shortName ? T("Sem", "7d") : T("Semana", "Week"),
        "monthly" => T("Mês", shortName ? "30d" : "Month"),
        _ => string.IsNullOrEmpty(label) ? id : label,
    };

    public static string Error(string? code) => code switch
    {
        "AUTH_REQUIRED" => T("Faça login no CLI deste serviço", "Sign in to this service's CLI"),
        "CONSOLE_LOGIN_REQUIRED" => T("Conecte a conta do OpenCode Console (opencode auth login)", "Connect your OpenCode Console account (opencode auth login)"),
        "CLI_NOT_FOUND" => T("CLI não encontrado", "CLI not found"),
        "CLI_TIMEOUT" => T("Sem resposta do CLI", "CLI did not answer"),
        "USAGE_UNAVAILABLE" => T("Limites indisponíveis", "Limits unavailable"),
        _ => T("Falha na leitura", "Could not read"),
    };

    public static string Countdown(DateTimeOffset? reset, DateTimeOffset now)
    {
        if (reset is null) return "—";
        var mins = (long)Math.Ceiling((reset.Value - now).TotalMinutes);
        if (mins <= 0) return T("agora", "now");
        var d = mins / 1440; var h = mins % 1440 / 60; var m = mins % 60;
        return d > 0 ? $"{d}d {h}h" : h > 0 ? $"{h}h {m}min" : $"{m}min";
    }

    public static string ResetAt(DateTimeOffset? reset, DateTimeOffset now)
    {
        if (reset is null) return "";
        var l = reset.Value.ToLocalTime(); var n = now.ToLocalTime();
        var culture = Pt ? new CultureInfo("pt-BR") : new CultureInfo("en-US");
        if (l.Date == n.Date) return T("hoje ", "today ") + l.ToString("HH:mm", culture);
        return (l - n).TotalDays < 6 ? l.ToString("ddd HH:mm", culture) : l.ToString("dd/MM HH:mm", culture);
    }

    public static string Age(DateTimeOffset at, DateTimeOffset now)
    {
        var m = (long)Math.Max(0, (now - at).TotalMinutes);
        return m < 1 ? T("agora", "now") : m < 60 ? T($"há {m}min", $"{m}m ago") : m < 1440 ? T($"há {m / 60}h", $"{m / 60}h ago") : T($"há {m / 1440}d", $"{m / 1440}d ago");
    }

    public static string Percent(double? v) => v is null ? "—" : v % 1 == 0 ? $"{v:0}%" : $"{v:0.0}%";
}

/// <summary>Soft neumorphism palette. Applied to application resources so every DynamicResource follows it.</summary>
public static class Theme
{
    public static bool Dark { get; private set; }
    public static Color Base, Light, Shade, Text, Dim, Warn = Color.FromRgb(0xE6, 0xA2, 0x3C), Crit = Color.FromRgb(0xE5, 0x53, 0x4B);
    public static event Action? Changed;

    public static Color Accent(string id) => id switch
    {
        "codex" => Color.FromRgb(0x2F, 0xCB, 0xA0),
        "claude" => Color.FromRgb(0xE0, 0x87, 0x5A),
        "opencode" => Color.FromRgb(0x9B, 0x87, 0xF5),
        _ => Color.FromRgb(0x5A, 0xA9, 0xE6),
    };

    public static Color Level(double? percent, Color accent) => percent switch { >= 85 => Crit, >= 60 => Warn, _ => accent };

    public static bool SystemDark()
    {
        try { return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int v && v == 0; }
        catch { return false; }
    }

    public static void Apply(string setting)
    {
        Dark = setting == "dark" || (setting != "light" && SystemDark());
        Base = Dark ? Color.FromRgb(0x2A, 0x2E, 0x35) : Color.FromRgb(0xE4, 0xE8, 0xEE);
        Light = Dark ? Color.FromRgb(0x38, 0x3D, 0x46) : Colors.White;
        Shade = Dark ? Color.FromRgb(0x1B, 0x1E, 0x23) : Color.FromRgb(0xA9, 0xB4, 0xC6);
        Text = Dark ? Color.FromRgb(0xE9, 0xEC, 0xF2) : Color.FromRgb(0x34, 0x3B, 0x48);
        Dim = Dark ? Color.FromRgb(0x9A, 0xA3, 0xB2) : Color.FromRgb(0x7A, 0x84, 0x96);

        var r = Application.Current.Resources;
        static SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        r["BaseBrush"] = B(Base); r["TextBrush"] = B(Text); r["DimBrush"] = B(Dim); r["LightBrush"] = B(Light); r["ShadeBrush"] = B(Shade);
        r["WarnBrush"] = B(Warn); r["CritBrush"] = B(Crit);
        r["ShadowLight"] = Shadow(Light, -5, 14); r["ShadowDark"] = Shadow(Shade, 5, 14);
        r["ShadowLightSm"] = Shadow(Light, -3, 8); r["ShadowDarkSm"] = Shadow(Shade, 3, 8);
        Changed?.Invoke();
    }

    static DropShadowEffect Shadow(Color c, double offset, double blur)
    {
        var e = new DropShadowEffect
        {
            Color = c, BlurRadius = blur, ShadowDepth = Math.Abs(offset) * 1.4142, Opacity = Dark ? 0.9 : 0.85,
            Direction = offset < 0 ? 135 : 315, RenderingBias = RenderingBias.Performance,
        };
        e.Freeze();
        return e;
    }
}
