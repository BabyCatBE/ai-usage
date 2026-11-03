using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Runtime.InteropServices;
using System.Windows.Interop;
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

/// <summary>Keeps the app invisible on a weak PC: process priority, Windows efficiency mode (EcoQoS) and user-idle detection.</summary>
public static class Power
{
    [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
    [StructLayout(LayoutKind.Sequential)] struct PROCESS_POWER_THROTTLING_STATE { public uint Version, ControlMask, StateMask; }
    [DllImport("kernel32.dll")] static extern bool SetProcessInformation(IntPtr process, int cls, ref PROCESS_POWER_THROTTLING_STATE info, int size);
    const int ProcessPowerThrottling = 4; const uint ExecutionSpeed = 1;

    /// <summary>Seconds since the last keyboard or mouse input.</summary>
    public static double IdleSeconds()
    {
        var i = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref i)) return 0;
        return unchecked((uint)Environment.TickCount - i.dwTime) / 1000.0;
    }

    /// <summary>saver on: below-normal priority; eco = also ask Windows to run the process on efficient cores / low clocks.</summary>
    public static void Apply(bool saver, bool eco)
    {
        try
        {
            using var p = System.Diagnostics.Process.GetCurrentProcess();
            p.PriorityClass = saver ? System.Diagnostics.ProcessPriorityClass.BelowNormal : System.Diagnostics.ProcessPriorityClass.Normal;
            var st = new PROCESS_POWER_THROTTLING_STATE { Version = 1, ControlMask = ExecutionSpeed, StateMask = saver && eco ? ExecutionSpeed : 0 };
            SetProcessInformation(p.Handle, ProcessPowerThrottling, ref st, Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
        }
        catch { }
    }
}

/// <summary>
/// Palettes: soft neumorphism, liquid glass (Apple-style: translucent rim-lit panes, regular variant, opaque fallbacks)
/// and neon cyberpunk. Applied to application resources so every DynamicResource follows it.
/// </summary>
public static class Theme
{
    public static bool Dark { get; private set; }
    public static bool Glass { get; private set; }
    public static bool Cyber { get; private set; }
    public static bool Soft { get; private set; }
    /// <summary>Apple-design look: system colours, Inter, glass cards; the only look that turns on real (DWM acrylic) blur.</summary>
    public static bool Cupertino { get; private set; }
    /// <summary>Cupertino with the desktop blurred behind the windows (off with power saver or Windows transparency off).</summary>
    public static bool Acrylic { get; private set; }
    /// <summary>Monochrome neumorphism: the soft look in pure greys, vibrant black (dark) or vibrant white (light).</summary>
    public static bool Mono { get; private set; }
    public static bool PowerSaver { get; private set; }
    public static CyberVariant? Variant { get; private set; }
    public static Color Base, Light, Shade, Text, Dim, Highlight, Warn = Color.FromRgb(0xE6, 0xA2, 0x3C), Crit = Color.FromRgb(0xE5, 0x53, 0x4B);
    public static event Action? Changed;

    public static Color Accent(string id) => Mono ? Text : Cupertino
        ? id switch
        {
            "codex" => Dark ? Color.FromRgb(0x63, 0xE6, 0xE2) : Color.FromRgb(0x00, 0xC7, 0xBE),
            "claude" => Dark ? Color.FromRgb(0xFF, 0x9F, 0x0A) : Color.FromRgb(0xFF, 0x95, 0x00),
            "opencode" => Dark ? Color.FromRgb(0x5E, 0x5C, 0xE6) : Color.FromRgb(0x58, 0x56, 0xD6),
            _ => Dark ? Color.FromRgb(0x0A, 0x84, 0xFF) : Color.FromRgb(0x00, 0x7A, 0xFF),
        }
        : Variant is { } v
        ? id switch
        {
            "codex" => v.Codex,
            "claude" => v.Claude,
            "opencode" => v.Open,
            _ => v.Primary,
        }
        : id switch
        {
            "codex" => Color.FromRgb(0x2F, 0xCB, 0xA0),
            "claude" => Color.FromRgb(0xE0, 0x87, 0x5A),
            "opencode" => Color.FromRgb(0x9B, 0x87, 0xF5),
            _ => Color.FromRgb(0x5A, 0xA9, 0xE6),
        };

    public static Color Level(double? percent, Color accent) => percent switch { >= 85 => Crit, >= 60 => Warn, _ => accent };

    const string Personalize = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool SystemDark()
    {
        try { return Registry.GetValue(Personalize, "AppsUseLightTheme", 1) is int v && v == 0; }
        catch { return false; }
    }

    /// <summary>Windows "Transparency effects" off, or high contrast on: glass falls back to opaque surfaces and stronger rims.</summary>
    static bool ReduceTransparency()
    {
        try { if (Registry.GetValue(Personalize, "EnableTransparency", 1) is int v && v == 0) return true; }
        catch { }
        return SystemParameters.HighContrast;
    }

    static SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    static Color A(byte a, Color c) => Color.FromArgb(a, c.R, c.G, c.B);
    static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
    static Color Mix(Color a, Color b, double t) => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    static LinearGradientBrush Grad(Point from, Point to, params (Color c, double at)[] stops) =>
        Frozen(new LinearGradientBrush(new GradientStopCollection(stops.Select(s => new GradientStop(s.c, s.at))), from, to));

    public static void Apply(Settings st)
    {
        if (st.Theme == "glass") { st.Theme = "auto"; st.Style = "glass"; } // older settings files
        Cyber = st.Style == "cyber";
        Soft = st.Style == "soft";
        Variant = Cyber ? CyberVariant.Get(st.CyberVariant) : Soft ? CyberVariant.GetSoft(st.SoftVariant) : null;
        PowerSaver = st.PowerSaver;
        Glass = st.Style == "glass";
        Cupertino = st.Style == "cupertino";
        Mono = st.Style == "mono";
        Dark = Variant is not null ? Variant.Dark : st.Theme == "dark" || (st.Theme != "light" && SystemDark());   // each cyber sub-theme is either dark or light
        var opaque = Glass && ReduceTransparency();
        Acrylic = Cupertino && !PowerSaver && !ReduceTransparency();

        if (Mono) Text = Dark ? Colors.White : Colors.Black;   // Accent() returns the text colour in Mono
        Highlight = Accent("codex");
        Warn = Color.FromRgb(0xE6, 0xA2, 0x3C); Crit = Color.FromRgb(0xE5, 0x53, 0x4B);
        if (Soft) { var v = Variant!; Base = v.Base; Light = Mix(v.Base, Colors.White, v.Dark ? 0.06 : 0.55); Shade = v.Ink; Text = v.Text; Dim = v.Dim; Warn = v.Warn; Crit = v.Crit; Highlight = v.Primary; }
        else if (Cyber) { var v = Variant!; Base = v.Base; Light = v.Primary; Shade = v.Ink; Text = v.Text; Dim = v.Dim; Warn = v.Warn; Crit = v.Crit; Highlight = v.Primary; }
        else if (Mono && Dark) { Base = Color.FromRgb(0x10, 0x10, 0x10); Light = Color.FromRgb(0x24, 0x24, 0x24); Shade = Colors.Black; Text = Colors.White; Dim = Color.FromRgb(0x9A, 0x9A, 0x9A); Highlight = Colors.White; Warn = Color.FromRgb(0xC8, 0xC8, 0xC8); Crit = Color.FromRgb(0xFF, 0x45, 0x3A); }
        else if (Mono) { Base = Color.FromRgb(0xF2, 0xF2, 0xF2); Light = Colors.White; Shade = Color.FromRgb(0xC4, 0xC4, 0xC4); Text = Colors.Black; Dim = Color.FromRgb(0x5E, 0x5E, 0x5E); Highlight = Colors.Black; Warn = Color.FromRgb(0x44, 0x44, 0x44); Crit = Color.FromRgb(0xE0, 0x24, 0x1B); }
        else if (Cupertino && Dark) { Base = Color.FromRgb(0x1C, 0x1C, 0x1E); Light = Color.FromRgb(0x3A, 0x3A, 0x3C); Shade = Colors.Black; Text = Colors.White; Dim = Color.FromRgb(0x98, 0x98, 0x9F); Highlight = Color.FromRgb(0x0A, 0x84, 0xFF); Warn = Color.FromRgb(0xFF, 0x9F, 0x0A); Crit = Color.FromRgb(0xFF, 0x45, 0x3A); }
        else if (Cupertino) { Base = Color.FromRgb(0xF2, 0xF2, 0xF7); Light = Colors.White; Shade = Color.FromRgb(0xC7, 0xC7, 0xCC); Text = Colors.Black; Dim = Color.FromRgb(0x6C, 0x6C, 0x70); Highlight = Color.FromRgb(0x00, 0x7A, 0xFF); Warn = Color.FromRgb(0xFF, 0x95, 0x00); Crit = Color.FromRgb(0xFF, 0x3B, 0x30); }
        else if (Glass && Dark) { Base = Color.FromRgb(0x0B, 0x10, 0x24); Light = Color.FromRgb(0x6E, 0x80, 0xB0); Shade = Color.FromRgb(0x05, 0x08, 0x18); Text = Color.FromRgb(0xF2, 0xF6, 0xFF); Dim = Color.FromRgb(0xB8, 0xC6, 0xE4); }
        else if (Glass) { Base = Color.FromRgb(0xE9, 0xEE, 0xFA); Light = Colors.White; Shade = Color.FromRgb(0x8E, 0xA0, 0xC4); Text = Color.FromRgb(0x1B, 0x25, 0x3D); Dim = Color.FromRgb(0x4F, 0x5F, 0x80); }
        else if (Dark) { Base = Color.FromRgb(0x2A, 0x2E, 0x35); Light = Color.FromRgb(0x38, 0x3D, 0x46); Shade = Color.FromRgb(0x1B, 0x1E, 0x23); Text = Color.FromRgb(0xE9, 0xEC, 0xF2); Dim = Color.FromRgb(0x9A, 0xA3, 0xB2); }
        else { Base = Color.FromRgb(0xE4, 0xE8, 0xEE); Light = Colors.White; Shade = Color.FromRgb(0xA9, 0xB4, 0xC6); Text = Color.FromRgb(0x34, 0x3B, 0x48); Dim = Color.FromRgb(0x7A, 0x84, 0x96); }

        var r = Application.Current.Resources;
        r["BaseBrush"] = B(Base); r["TextBrush"] = B(Text); r["DimBrush"] = B(Dim); r["LightBrush"] = B(Light); r["ShadeBrush"] = B(Shade);
        r["WarnBrush"] = B(Warn); r["CritBrush"] = B(Crit); r["HighlightBrush"] = B(Highlight);
        r["CardSheen"] = null;
        r["UiFont"] = Cupertino ? InterFont : SegoeFont;
        r["CardBorderThickness"] = new Thickness(1);
        r["CardRadius"] = new CornerRadius(20); r["DockRightRadius"] = new CornerRadius(20, 0, 0, 20); r["DockLeftRadius"] = new CornerRadius(0, 20, 20, 0);
        r["ButtonRadius"] = new CornerRadius(14); r["RoundRadius"] = new CornerRadius(20);
        // slim, discreet scrollbar that follows the palette
        r["ScrollThumb"] = B(A(Dark ? (byte)0x50 : (byte)0x70, Cyber ? Highlight : Dim));
        r["ScrollThumbHot"] = B(A(0xC0, Cyber ? Highlight : Dim));

        if (Cyber)
        {
            var v = Variant!;
            r["WindowBrush"] = CyberBackground(v);
            r["CardBack"] = B(A(Dark ? (byte)0xF0 : (byte)0xEC, Base));
            var fa = Dark ? (byte)0x26 : (byte)0x1C;
            r["CardFill"] = Grad(new Point(0, 0), new Point(1, 1), (A(fa, v.Primary), 0), (A((byte)(fa / 2), Mix(v.Primary, v.Secondary, 0.5)), 0.55), (A((byte)(fa * 0.9), v.Secondary), 1));
            r["CardBorder"] = Grad(new Point(0, 0), new Point(1, 1), (v.Primary, 0), (A(0x70, Mix(v.Primary, v.Secondary, 0.5)), 0.5), (v.Secondary, 1));
            r["CardBorderThickness"] = new Thickness(1.5);
            r["CardRadius"] = new CornerRadius(5); r["DockRightRadius"] = new CornerRadius(5, 0, 0, 5); r["DockLeftRadius"] = new CornerRadius(0, 5, 5, 0);
            r["ButtonRadius"] = new CornerRadius(4); r["RoundRadius"] = new CornerRadius(4);
            r["ShadowLight"] = null; r["ShadowLightSm"] = null;
            // blurred glow = an offscreen bitmap per element: only when the user turned the power saver off
            r["ShadowDark"] = PowerSaver ? null : Glow(v.Primary, 16, Dark ? 0.38 : 0.28);
            r["ShadowDarkSm"] = PowerSaver ? null : Glow(v.Secondary, 8, Dark ? 0.4 : 0.3);
        }
        else if (Soft)
        {
            // flat, quiet surfaces: a hairline border, no glow, no blur, no shadow; colour is kept for the bars and status only
            var v = Variant!;
            r["WindowBrush"] = SoftBackground(v);
            r["CardBack"] = B(Mix(v.Base, Colors.White, v.Dark ? 0.045 : 0.5));
            r["CardFill"] = B(Colors.Transparent);
            r["CardBorder"] = B(A(Dark ? (byte)0x30 : (byte)0x38, v.Dim));
            r["CardBorderThickness"] = new Thickness(1);
            r["CardRadius"] = new CornerRadius(16); r["DockRightRadius"] = new CornerRadius(16, 0, 0, 16); r["DockLeftRadius"] = new CornerRadius(0, 16, 16, 0);
            r["ButtonRadius"] = new CornerRadius(11); r["RoundRadius"] = new CornerRadius(16);
            r["ShadowLight"] = null; r["ShadowDark"] = null; r["ShadowLightSm"] = null; r["ShadowDarkSm"] = null;
        }
        else if (Cupertino)
        {
            // Apple-style glass: the window shows the desktop through DWM acrylic under a faint aurora veil; each card is a
            // translucent pane with a rim that catches the light and a sheen along its top edge. Without acrylic the aurora is opaque.
            r["WindowBrush"] = CupertinoBackground(Dark, Acrylic);
            r["CardBack"] = B(Dark ? Color.FromArgb(0x8C, 0x1C, 0x1C, 0x1E) : Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));
            r["CardFill"] = Grad(new Point(0, 0), new Point(1, 1), (Color.FromArgb(Dark ? (byte)0x1C : (byte)0x40, 255, 255, 255), 0), (Color.FromArgb(Dark ? (byte)0x06 : (byte)0x10, 255, 255, 255), 0.6), (Colors.Transparent, 1));
            r["CardBorder"] = Grad(new Point(0, 0), new Point(1, 1), (Color.FromArgb(Dark ? (byte)0x8C : (byte)0xFF, 255, 255, 255), 0), (Color.FromArgb(Dark ? (byte)0x14 : (byte)0x50, 255, 255, 255), 0.45), (Color.FromArgb(Dark ? (byte)0x48 : (byte)0xC0, 255, 255, 255), 1));
            r["CardSheen"] = Grad(new Point(0.5, 0), new Point(0.5, 1), (Color.FromArgb(Dark ? (byte)0x22 : (byte)0x55, 255, 255, 255), 0), (Colors.Transparent, 0.32));
            r["CardBorderThickness"] = new Thickness(1);
            r["CardRadius"] = new CornerRadius(22); r["DockRightRadius"] = new CornerRadius(22, 0, 0, 22); r["DockLeftRadius"] = new CornerRadius(0, 22, 22, 0);
            r["ButtonRadius"] = new CornerRadius(100); r["RoundRadius"] = new CornerRadius(20);
            r["ScrollThumb"] = B(A(Dark ? (byte)0x60 : (byte)0x50, Dim));
            r["ShadowLight"] = null; r["ShadowLightSm"] = null; r["ShadowDarkSm"] = null;
            r["ShadowDark"] = PowerSaver ? null : SoftShadow(Dark ? 0.45 : 0.14);
        }
        else if (Glass)
        {
            r["WindowBrush"] = GlassBackground(Dark);
            // Apple's rule: text-heavy surfaces use the regular variant, never clear glass. Panes stay mostly opaque so numbers never fight
            // with what is behind them; the rim and a specular sheen cast from the top edge carry the material. Reduce Transparency = opaque.
            r["CardBack"] = B(opaque ? Base : Dark ? Color.FromArgb(0xE8, 0x0B, 0x10, 0x24) : Color.FromArgb(0xEE, 0xF4, 0xF7, 0xFF));
            r["CardFill"] = opaque
                ? B(Dark ? Color.FromArgb(0x30, 255, 255, 255) : Color.FromArgb(0x60, 255, 255, 255))
                : Grad(new Point(0, 0), new Point(1, 1), (Dark ? Color.FromArgb(0x38, 255, 255, 255) : Color.FromArgb(0x99, 255, 255, 255), 0), (Dark ? Color.FromArgb(0x12, 200, 220, 255) : Color.FromArgb(0x33, 200, 215, 255), 1));
            r["CardBorder"] = opaque
                ? B(Dark ? Color.FromRgb(0xC8, 0xD2, 0xF0) : Color.FromRgb(0x4F, 0x5F, 0x80))
                : Grad(new Point(0, 0), new Point(1, 1), (Color.FromArgb(Dark ? (byte)0xB4 : (byte)0xFF, 255, 255, 255), 0), (Dark ? Color.FromArgb(0x1C, 160, 190, 255) : Color.FromArgb(0x70, 150, 170, 220), 0.5), (Color.FromArgb(Dark ? (byte)0x5A : (byte)0xC0, 255, 255, 255), 1));
            if (!opaque)
                r["CardSheen"] = Grad(new Point(0.5, 0), new Point(0.5, 1), (Color.FromArgb(Dark ? (byte)0x30 : (byte)0x80, 255, 255, 255), 0), (Color.FromArgb(Dark ? (byte)0x0A : (byte)0x20, 255, 255, 255), 0.35), (Colors.Transparent, 0.6));
            // no blur effects in the glass look: each one costs an offscreen bitmap per element
            r["ShadowLight"] = null; r["ShadowDark"] = null; r["ShadowLightSm"] = null; r["ShadowDarkSm"] = null;
        }
        else
        {
            r["WindowBrush"] = r["BaseBrush"]; r["CardBack"] = r["BaseBrush"]; r["CardFill"] = r["BaseBrush"]; r["CardBorder"] = B(Colors.Transparent);
            if (PowerSaver)
            {
                // same soft look without blurred drop shadows (each one is an offscreen bitmap): light rim top-left, shade rim bottom-right
                r["CardBorder"] = Grad(new Point(0, 0), new Point(1, 1), (A(Dark ? (byte)0xB0 : (byte)0xFF, Light), 0), (A(0x10, Light), 0.45), (A(0xC0, Shade), 1));
                r["CardBorderThickness"] = new Thickness(1.4);
                r["ShadowLight"] = null; r["ShadowDark"] = null; r["ShadowLightSm"] = null; r["ShadowDarkSm"] = null;
            }
            else
            {
                r["ShadowLight"] = Shadow(Light, -5, 14); r["ShadowDark"] = Shadow(Shade, 5, 14);
                r["ShadowLightSm"] = Shadow(Light, -3, 8); r["ShadowDarkSm"] = Shadow(Shade, 3, 8);
            }
        }

        userCompact = st.Density == "compact";
        lastDensity = null;
        SetDensity(0);
        Changed?.Invoke();
    }

    static bool userCompact;
    static int? lastDensity;

    /// <summary>Spacing tokens. level 0 = the user's density, 1 = compact, 2 = tight (used by the auto-fit when the window is small).</summary>
    public static void SetDensity(int level)
    {
        var lv = level >= 2 ? 2 : level == 1 || userCompact ? 1 : 0;
        if (lastDensity == lv) return;
        lastDensity = lv;
        var r = Application.Current.Resources;
        r["CardPadding"] = lv == 2 ? new Thickness(10, 7, 10, 7) : lv == 1 ? new Thickness(12, 9, 12, 9) : new Thickness(16, 14, 16, 14);
        r["CardMargin"] = new Thickness(lv == 2 ? 3 : lv == 1 ? 5 : 10);
        r["RowGap"] = new Thickness(0, lv == 2 ? 3 : lv == 1 ? 4 : 8, 0, 0);
    }

    // ---- window chrome (title bar + border) so a dark theme never shows a white frame ---------------------------

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
    [StructLayout(LayoutKind.Sequential)] struct Margins { public int Left, Right, Top, Bottom; }
    const int DWMWA_SYSTEMBACKDROP_TYPE = 38, DWMSBT_NONE = 1, DWMSBT_TRANSIENTWINDOW = 3;
    const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);

    static readonly FontFamily SegoeFont = new("Segoe UI Variable Text, Segoe UI");
    static readonly FontFamily InterFont = new(new Uri("pack://application:,,,/"), "./Fonts/#Inter, Segoe UI Variable Text, Segoe UI");

    static DropShadowEffect SoftShadow(double opacity)
    {
        var e = new DropShadowEffect { Color = Colors.Black, BlurRadius = 26, ShadowDepth = 6, Direction = 270, Opacity = opacity, RenderingBias = RenderingBias.Performance };
        e.Freeze();
        return e;
    }

    /// <summary>
    /// Cupertino: real blur of what is behind the window (DWM acrylic system backdrop, Windows 11 22H2+). Other looks get the
    /// plain backdrop back. Older systems ignore the attribute and keep the opaque aurora.
    /// </summary>
    static void ApplyBackdrop(Window w, IntPtr h)
    {
        var src = HwndSource.FromHwnd(h);
        if (Acrylic)
        {
            if (src?.CompositionTarget is { } ct) ct.BackgroundColor = Colors.Transparent;
            var m = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(h, ref m);
            var t = DWMSBT_TRANSIENTWINDOW;
            DwmSetWindowAttribute(h, DWMWA_SYSTEMBACKDROP_TYPE, ref t, 4);
            var none = DWMWA_COLOR_NONE;
            DwmSetWindowAttribute(h, DWMWA_CAPTION_COLOR, ref none, 4);
        }
        else
        {
            var t = DWMSBT_NONE;
            DwmSetWindowAttribute(h, DWMWA_SYSTEMBACKDROP_TYPE, ref t, 4);
            var m = new Margins();
            DwmExtendFrameIntoClientArea(h, ref m);
            if (src?.CompositionTarget is { } ct) ct.BackgroundColor = SystemColors.WindowColor;
        }
    }

    [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionData data);
    [DllImport("user32.dll")] static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
    [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);
    [StructLayout(LayoutKind.Sequential)] struct AccentPolicy { public int AccentState, AccentFlags; public uint GradientColor; public int AnimationId; }
    [StructLayout(LayoutKind.Sequential)] struct WindowCompositionData { public int Attribute; public IntPtr Data; public int SizeOfData; }
    const int WCA_ACCENT_POLICY = 19, ACCENT_DISABLED = 0, ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

    /// <summary>
    /// Acrylic blur behind a layered (transparent) window such as the floating widget, clipped to its glass surface
    /// [surface] in device pixels with [radius] corners. Pass Acrylic = false (other looks) to switch it off.
    /// </summary>
    public static void ApplyLayeredBlur(Window w, Rect surface, double radius)
    {
        var h = new WindowInteropHelper(w).Handle;
        if (h == IntPtr.Zero) return;
        try
        {
            var on = Acrylic;
            var policy = new AccentPolicy { AccentState = on ? ACCENT_ENABLE_ACRYLICBLURBEHIND : ACCENT_DISABLED, GradientColor = on ? (Dark ? 0x30000000u : 0x30FFFFFFu) : 0 };
            var size = Marshal.SizeOf<AccentPolicy>();
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, ptr, false);
                var data = new WindowCompositionData { Attribute = WCA_ACCENT_POLICY, Data = ptr, SizeOfData = size };
                SetWindowCompositionAttribute(h, ref data);
            }
            finally { Marshal.FreeHGlobal(ptr); }
            if (on)
            {
                var d = (int)Math.Round(radius * 2);
                SetWindowRgn(h, CreateRoundRectRgn((int)surface.Left, (int)surface.Top, (int)surface.Right + 1, (int)surface.Bottom + 1, d, d), true);
            }
            else SetWindowRgn(h, IntPtr.Zero, true);
        }
        catch { }
    }

    /// <summary>iOS 26-style colour fields. Over acrylic it is a faint veil (the blurred desktop shows through); otherwise opaque.</summary>
    static Brush CupertinoBackground(bool dark, bool overAcrylic)
    {
        static RadialGradientBrush Glow(Color c, byte a) => new(Color.FromArgb(a, c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B));
        byte k(byte a) => overAcrylic ? (byte)(a * 0.55) : a;
        var top = dark ? Color.FromRgb(0x06, 0x07, 0x0D) : Color.FromRgb(0xF5, 0xF2, 0xFA);
        var bottom = dark ? Color.FromRgb(0x0A, 0x0C, 0x17) : Color.FromRgb(0xE9, 0xF1, 0xFB);
        var baseA = overAcrylic ? (dark ? (byte)0x70 : (byte)0x60) : (byte)0xFF;
        var bg = new LinearGradientBrush(A(baseA, top), A(baseA, bottom), 90);
        var group = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(0, 0, 100, 100)) };
        group.Children.Add(new GeometryDrawing(bg, null, new RectangleGeometry(new Rect(0, 0, 100, 100))));
        group.Children.Add(new GeometryDrawing(Glow(dark ? Color.FromRgb(0x1E, 0x3A, 0x8A) : Color.FromRgb(0x8E, 0xC5, 0xFF), k(dark ? (byte)0xE0 : (byte)0x99)), null, new EllipseGeometry(new Point(18, 12), 62, 50)));
        group.Children.Add(new GeometryDrawing(Glow(dark ? Color.FromRgb(0x6D, 0x28, 0xD9) : Color.FromRgb(0xC9, 0xA7, 0xFF), k(dark ? (byte)0xB0 : (byte)0x80)), null, new EllipseGeometry(new Point(92, 30), 55, 45)));
        group.Children.Add(new GeometryDrawing(Glow(dark ? Color.FromRgb(0xBE, 0x18, 0x5D) : Color.FromRgb(0xFF, 0xB3, 0xC7), k(dark ? (byte)0x70 : (byte)0x66)), null, new EllipseGeometry(new Point(70, 78), 50, 40)));
        group.Children.Add(new GeometryDrawing(Glow(dark ? Color.FromRgb(0x0E, 0x74, 0x90) : Color.FromRgb(0x9D, 0xF0, 0xE0), k(dark ? (byte)0x90 : (byte)0x7A)), null, new EllipseGeometry(new Point(10, 70), 58, 48)));
        var brush = new DrawingBrush(group) { Stretch = Stretch.Fill, Viewbox = new Rect(0, 0, 100, 100), ViewboxUnits = BrushMappingMode.Absolute };
        RenderOptions.SetCachingHint(brush, CachingHint.Cache);
        brush.Freeze();
        return brush;
    }
    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_BORDER_COLOR = 34, DWMWA_CAPTION_COLOR = 35, DWMWA_TEXT_COLOR = 36;
    const int DWMWA_COLOR_DEFAULT = unchecked((int)0xFFFFFFFF);

    static int Rgb(Color c) => c.R | (c.G << 8) | (c.B << 16);

    /// <summary>Paints the native title bar and window border with the palette (Windows 11; older systems ignore it).</summary>
    public static void ApplyChrome(Window w)
    {
        var h = new WindowInteropHelper(w).Handle;
        if (h == IntPtr.Zero) return;
        try
        {
            var dark = Dark ? 1 : 0;
            DwmSetWindowAttribute(h, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
            var cap = Dark ? Rgb(Base) : DWMWA_COLOR_DEFAULT;
            var txt = Dark ? Rgb(Text) : DWMWA_COLOR_DEFAULT;
            var border = Soft && Variant is { } sv ? Rgb(Mix(sv.Base, sv.Dim, 0.3)) : Cyber && Variant is { } cv ? Rgb(Mix(cv.Base, cv.Primary, Dark ? 0.55 : 0.7)) : Dark ? Rgb(Glass ? Color.FromRgb(0x2E, 0x39, 0x66) : Color.FromRgb(0x3A, 0x3F, 0x4A)) : DWMWA_COLOR_DEFAULT;
            DwmSetWindowAttribute(h, DWMWA_CAPTION_COLOR, ref cap, 4);
            DwmSetWindowAttribute(h, DWMWA_TEXT_COLOR, ref txt, 4);
            DwmSetWindowAttribute(h, DWMWA_BORDER_COLOR, ref border, 4);
            ApplyBackdrop(w, h);
        }
        catch { }
    }

    /// <summary>Deep night gradient with soft violet and cyan glows behind the translucent cards.</summary>
    static Brush GlassBackground(bool dark)
    {
        static RadialGradientBrush Glow(byte r, byte g, byte b, byte a) => new(Color.FromArgb(a, r, g, b), Color.FromArgb(0, r, g, b));
        var bg = new LinearGradientBrush(new GradientStopCollection { new(dark ? Color.FromRgb(0x0A, 0x0E, 0x22) : Color.FromRgb(0xDD, 0xE7, 0xFB), 0), new(dark ? Color.FromRgb(0x17, 0x12, 0x45) : Color.FromRgb(0xEA, 0xE0, 0xFB), 0.55), new(dark ? Color.FromRgb(0x08, 0x20, 0x35) : Color.FromRgb(0xD6, 0xF0, 0xF6), 1) }, new Point(0, 0), new Point(1, 1));
        var group = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(0, 0, 100, 100)) };
        group.Children.Add(new GeometryDrawing(bg, null, new RectangleGeometry(new Rect(0, 0, 100, 100))));
        group.Children.Add(new GeometryDrawing(Glow(0x8B, 0x5C, 0xF6, dark ? (byte)0x90 : (byte)0x55), null, new EllipseGeometry(new Point(18, 12), 55, 45)));
        group.Children.Add(new GeometryDrawing(Glow(0x22, 0xD3, 0xEE, dark ? (byte)0x70 : (byte)0x50), null, new EllipseGeometry(new Point(88, 92), 55, 50)));
        group.Children.Add(new GeometryDrawing(Glow(0xF4, 0x72, 0xB6, (byte)0x40), null, new EllipseGeometry(new Point(92, 8), 35, 30)));
        var brush = new DrawingBrush(group) { Stretch = Stretch.Fill, Viewbox = new Rect(0, 0, 100, 100), ViewboxUnits = BrushMappingMode.Absolute };
        RenderOptions.SetCachingHint(brush, CachingHint.Cache);
        brush.Freeze();
        return brush;
    }

    /// <summary>Backdrop of a soft sub-theme: a barely-there three-stop gradient, nothing else.</summary>
    static Brush SoftBackground(CyberVariant v)
    {
        var bg = Grad(new Point(0, 0), new Point(1, 1), (v.BgA, 0), (v.BgB, 0.55), (v.BgC, 1));
        return bg;
    }

    /// <summary>Backdrop of a cyberpunk sub-theme: three-stop gradient, a faint grid of the primary colour and two corner glows. Cached as a bitmap.</summary>
    static Brush CyberBackground(CyberVariant v)
    {
        static RadialGradientBrush Glow(Color c, byte a) => new(Color.FromArgb(a, c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B));
        var bg = new LinearGradientBrush(new GradientStopCollection { new(v.BgA, 0), new(v.BgB, 0.55), new(v.BgC, 1) }, new Point(0, 0), new Point(1, 1));
        var group = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(0, 0, 100, 100)) };
        group.Children.Add(new GeometryDrawing(bg, null, new RectangleGeometry(new Rect(0, 0, 100, 100))));
        group.Children.Add(new GeometryDrawing(Glow(v.Glow1, v.Dark ? (byte)0x70 : (byte)0x60), null, new EllipseGeometry(new Point(90, 8), 55, 40)));
        group.Children.Add(new GeometryDrawing(Glow(v.Glow2, v.Dark ? (byte)0x60 : (byte)0x55), null, new EllipseGeometry(new Point(8, 94), 60, 45)));
        var grid = new GeometryGroup();
        for (var i = 1; i < 10; i++) { grid.Children.Add(new LineGeometry(new Point(i * 10, 0), new Point(i * 10, 100))); grid.Children.Add(new LineGeometry(new Point(0, i * 10), new Point(100, i * 10))); }
        group.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(A(v.Dark ? (byte)0x16 : (byte)0x1C, v.Primary)), 0.12), grid));
        var brush = new DrawingBrush(group) { Stretch = Stretch.Fill, Viewbox = new Rect(0, 0, 100, 100), ViewboxUnits = BrushMappingMode.Absolute };
        RenderOptions.SetCachingHint(brush, CachingHint.Cache);   // rasterised once instead of re-drawn on every repaint
        brush.Freeze();
        return brush;
    }
    static DropShadowEffect Glow(Color c, double blur, double opacity)
    {
        var e = new DropShadowEffect { Color = c, BlurRadius = blur, ShadowDepth = 0, Opacity = opacity, RenderingBias = RenderingBias.Performance };
        e.Freeze();
        return e;
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
