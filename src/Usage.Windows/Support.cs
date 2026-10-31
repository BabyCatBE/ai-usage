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
    public static bool Glass { get; private set; }
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

    public static void Apply(Settings st)
    {
        if (st.Theme == "glass") { st.Theme = "auto"; st.Style = "glass"; } // older settings files
        Glass = st.Style == "glass";
        Dark = st.Theme == "dark" || (st.Theme != "light" && SystemDark());
        if (Glass && Dark) { Base = Color.FromRgb(0x0B, 0x10, 0x24); Light = Color.FromRgb(0x6E, 0x80, 0xB0); Shade = Color.FromRgb(0x05, 0x08, 0x18); Text = Color.FromRgb(0xF2, 0xF6, 0xFF); Dim = Color.FromRgb(0xB8, 0xC6, 0xE4); }
        else if (Glass) { Base = Color.FromRgb(0xE9, 0xEE, 0xFA); Light = Colors.White; Shade = Color.FromRgb(0x8E, 0xA0, 0xC4); Text = Color.FromRgb(0x1B, 0x25, 0x3D); Dim = Color.FromRgb(0x4F, 0x5F, 0x80); }
        else if (Dark) { Base = Color.FromRgb(0x2A, 0x2E, 0x35); Light = Color.FromRgb(0x38, 0x3D, 0x46); Shade = Color.FromRgb(0x1B, 0x1E, 0x23); Text = Color.FromRgb(0xE9, 0xEC, 0xF2); Dim = Color.FromRgb(0x9A, 0xA3, 0xB2); }
        else { Base = Color.FromRgb(0xE4, 0xE8, 0xEE); Light = Colors.White; Shade = Color.FromRgb(0xA9, 0xB4, 0xC6); Text = Color.FromRgb(0x34, 0x3B, 0x48); Dim = Color.FromRgb(0x7A, 0x84, 0x96); }

        var r = Application.Current.Resources;
        static SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        r["BaseBrush"] = B(Base); r["TextBrush"] = B(Text); r["DimBrush"] = B(Dim); r["LightBrush"] = B(Light); r["ShadeBrush"] = B(Shade);
        r["WarnBrush"] = B(Warn); r["CritBrush"] = B(Crit);
        if (Glass)
        {
            r["WindowBrush"] = GlassBackground(Dark);
            // the panes stay mostly opaque so numbers never fight with whatever is behind the widget
            r["CardBack"] = B(Dark ? Color.FromArgb(0xE8, 0x0B, 0x10, 0x24) : Color.FromArgb(0xEE, 0xF4, 0xF7, 0xFF));
            r["CardFill"] = Frozen(new LinearGradientBrush(new GradientStopCollection { new(Dark ? Color.FromArgb(0x38, 255, 255, 255) : Color.FromArgb(0x99, 255, 255, 255), 0), new(Dark ? Color.FromArgb(0x12, 200, 220, 255) : Color.FromArgb(0x33, 200, 215, 255), 1) }, new Point(0, 0), new Point(1, 1)));
            r["CardBorder"] = Frozen(new LinearGradientBrush(new GradientStopCollection { new(Color.FromArgb(Dark ? (byte)0xA0 : (byte)0xFF, 255, 255, 255), 0), new(Dark ? Color.FromArgb(0x1C, 160, 190, 255) : Color.FromArgb(0x70, 150, 170, 220), 0.5), new(Color.FromArgb(Dark ? (byte)0x55 : (byte)0xC0, 255, 255, 255), 1) }, new Point(0, 0), new Point(1, 1)));
            // no blur effects in the glass look: each one costs an offscreen bitmap per element, the rim and fill carry the depth
            r["ShadowLight"] = null; r["ShadowDark"] = null; r["ShadowLightSm"] = null; r["ShadowDarkSm"] = null;
        }
        else
        {
            r["WindowBrush"] = r["BaseBrush"]; r["CardBack"] = r["BaseBrush"]; r["CardFill"] = r["BaseBrush"]; r["CardBorder"] = B(Colors.Transparent);
            r["ShadowLight"] = Shadow(Light, -5, 14); r["ShadowDark"] = Shadow(Shade, 5, 14);
            r["ShadowLightSm"] = Shadow(Light, -3, 8); r["ShadowDarkSm"] = Shadow(Shade, 3, 8);
        }
        Changed?.Invoke();
    }

    static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    /// <summary>Deep night gradient with soft violet and cyan glows behind the translucent cards.</summary>
    static Brush GlassBackground(bool dark)
    {
        static RadialGradientBrush Glow(byte r, byte g, byte b, byte a) => new(Color.FromArgb(a, r, g, b), Color.FromArgb(0, r, g, b));
        var bg = new LinearGradientBrush(new GradientStopCollection { new(dark ? Color.FromRgb(0x0A, 0x0E, 0x22) : Color.FromRgb(0xDD, 0xE7, 0xFB), 0), new(dark ? Color.FromRgb(0x17, 0x12, 0x45) : Color.FromRgb(0xEA, 0xE0, 0xFB), 0.55), new(dark ? Color.FromRgb(0x08, 0x20, 0x35) : Color.FromRgb(0xD6, 0xF0, 0xF6), 1) }, new Point(0, 0), new Point(1, 1));
        var group = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(0, 0, 100, 100)) };
        group.Children.Add(new GeometryDrawing(bg, null, new RectangleGeometry(new Rect(0, 0, 100, 100))));
        group.Children.Add(new GeometryDrawing(Glow(0x8B, 0x5C, 0xF6, dark ? (byte)0x90 : (byte)0x55), null, new EllipseGeometry(new Point(18, 12), 55, 45)));
        group.Children.Add(new GeometryDrawing(Glow(0x22, 0xD3, 0xEE, dark ? (byte)0x70 : (byte)0x50), null, new EllipseGeometry(new Point(88, 92), 55, 50)));
        group.Children.Add(new GeometryDrawing(Glow(0xF4, 0x72, 0xB6, dark ? (byte)0x40 : (byte)0x40), null, new EllipseGeometry(new Point(92, 8), 35, 30)));
        var brush = new DrawingBrush(group) { Stretch = Stretch.Fill, Viewbox = new Rect(0, 0, 100, 100), ViewboxUnits = BrushMappingMode.Absolute };
        brush.Freeze();
        return brush;
    }

    static DropShadowEffect Shadow(Color c, double offset, double blur)
    {
        var e = new DropShadowEffect
        {
            Color = c, BlurRadius = blur, ShadowDepth = Math.Abs(offset) * 1.4142, Opacity = Glass ? 0.55 : Dark ? 0.9 : 0.85,
            Direction = offset < 0 ? 135 : 315, RenderingBias = RenderingBias.Performance,
        };
        e.Freeze();
        return e;
    }
}
