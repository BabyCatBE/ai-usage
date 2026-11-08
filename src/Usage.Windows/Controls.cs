using System.Windows;
using System.Windows.Media;
using Usage.Core;

namespace Usage.Windows;

/// <summary>Common base: repaint when the theme changes.</summary>
public abstract class ThemedElement : FrameworkElement
{
    protected ThemedElement()
    {
        Loaded += (_, _) => Theme.Changed += OnTheme;
        Unloaded += (_, _) => Theme.Changed -= OnTheme;
    }
    void OnTheme() => Dispatcher.Invoke(InvalidateVisual);
    static readonly Dictionary<uint, Brush> brushes = [];
    /// <summary>Frozen brushes are shared: no allocation per repaint (UI thread only).</summary>
    protected static Brush Fill(Color c)
    {
        var key = ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
        if (brushes.TryGetValue(key, out var b)) return b;
        if (brushes.Count > 300) brushes.Clear();
        var nb = new SolidColorBrush(c); nb.Freeze();
        return brushes[key] = nb;
    }
    protected static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
    protected static Color Lighten(Color c, double f) => Color.FromRgb((byte)(c.R + (255 - c.R) * f), (byte)(c.G + (255 - c.G) * f), (byte)(c.B + (255 - c.B) * f));
}

/// <summary>Usage bar. Kind "bar": sunken track with a glowing rounded fill (neon HUD track in the cyber look). Kind "segments": LED-style blocks.</summary>
public sealed class NeuBar : ThemedElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(NeuBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(nameof(Accent), typeof(Color), typeof(NeuBar),
        new FrameworkPropertyMetadata(Colors.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(string), typeof(NeuBar),
        new FrameworkPropertyMetadata("bar", FrameworkPropertyMetadataOptions.AffectsRender));
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Color Accent { get => (Color)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }

    protected override Size MeasureOverride(Size s) => new(double.IsInfinity(s.Width) ? 100 : s.Width, 10);

    protected override void OnRender(DrawingContext dc)
    {
        var r = new Rect(0, 0, ActualWidth, ActualHeight);
        if (r.Width < 4 || r.Height < 2) return;
        var pct = Math.Clamp(Value, 0, 100);
        if (Kind == "segments") { Segments(dc, r, pct); return; }
        if (Theme.Cyber) { Neon(dc, r, pct); return; }
        if (Theme.Cupertino) { Capsule(dc, r, pct); return; }

        var rad = r.Height / 2;
        var track = new LinearGradientBrush(Theme.Shade.WithAlpha(Theme.Dark ? (byte)255 : (byte)150), Theme.Base, 90);
        track.Freeze();
        dc.DrawRoundedRectangle(track, null, r, rad, rad);
        // soft highlight along the lower edge: reads as a recessed groove
        dc.DrawRoundedRectangle(null, new Pen(Fill(WithAlpha(Theme.Light, 200)), 1), new Rect(0.5, 0.5, r.Width - 1, r.Height - 1), rad, rad);
        if (pct <= 0.2) return;
        var w = Math.Max(r.Height - 2, (r.Width - 2) * pct / 100);
        var fill = new Rect(1, 1, Math.Min(w, r.Width - 2), r.Height - 2);
        var glow = new RadialGradientBrush(WithAlpha(Accent, 70), Colors.Transparent);
        if (!Theme.Soft) dc.DrawRoundedRectangle(glow, null, new Rect(fill.X - 4, fill.Y - 4, fill.Width + 8, fill.Height + 8), rad + 4, rad + 4);
        var g = new LinearGradientBrush(Lighten(Accent, 0.3), Accent, 90); g.Freeze();
        dc.DrawRoundedRectangle(g, null, fill, rad - 1, rad - 1);
    }

    /// <summary>Cupertino: flat capsule track in the system fill, solid accent capsule (like an iOS progress view).</summary>
    void Capsule(DrawingContext dc, Rect r, double pct)
    {
        var rad = r.Height / 2;
        dc.DrawRoundedRectangle(Fill(Theme.Dark ? Color.FromArgb(0x5C, 0x78, 0x78, 0x80) : Color.FromArgb(0x33, 0x78, 0x78, 0x80)), null, r, rad, rad);
        if (pct <= 0.2) return;
        var w = Math.Max(r.Height, r.Width * pct / 100);
        dc.DrawRoundedRectangle(Fill(Accent), null, new Rect(0, 0, Math.Min(w, r.Width), r.Height), rad, rad);
    }

    /// <summary>Cyberpunk: flat ink track with a neon outline, quarter ticks and a bright glowing fill.</summary>
    void Neon(DrawingContext dc, Rect r, double pct)
    {
        var rad = Math.Min(2.0, r.Height / 2);
        dc.DrawRoundedRectangle(Fill(WithAlpha(Theme.Shade, 215)), new Pen(Fill(WithAlpha(Theme.Light, 80)), 1), new Rect(0.5, 0.5, r.Width - 1, r.Height - 1), rad, rad);
        if (pct > 0.2)
        {
            var w = Math.Max(3, (r.Width - 3) * pct / 100);
            var fill = new Rect(1.5, 1.5, Math.Min(w, r.Width - 3), r.Height - 3);
            dc.DrawRoundedRectangle(new RadialGradientBrush(WithAlpha(Accent, 110), Colors.Transparent), null, new Rect(fill.X - 5, fill.Y - 5, fill.Width + 10, fill.Height + 10), 6, 6);
            var g = new LinearGradientBrush(WithAlpha(Accent, 190), Lighten(Accent, 0.45), 0); g.Freeze();
            dc.DrawRoundedRectangle(g, null, fill, rad, rad);
        }
        if (r.Height >= 8)
            for (var q = 1; q < 4; q++)
            {
                var x = Math.Round(r.Width * q / 4) + 0.5;
                dc.DrawLine(new Pen(Fill(WithAlpha(Theme.Text, 55)), 1), new Point(x, 1), new Point(x, r.Height - 1));
            }
    }

    /// <summary>LED-style blocks: filled ones take the accent, empty ones stay as a faint outline.</summary>
    void Segments(DrawingContext dc, Rect r, double pct)
    {
        var n = Math.Max(8, (int)(r.Width / 11));
        const double gap = 2;
        var w = (r.Width - gap * (n - 1)) / n;
        var on = pct <= 0.2 ? 0 : Math.Clamp((int)Math.Round(pct / 100 * n), 1, n);
        var rad = Theme.Cyber ? 0.5 : Math.Min(2.5, r.Height / 2);
        var empty = Fill(WithAlpha(Theme.Cyber ? Theme.Light : Theme.Shade, Theme.Cyber ? (byte)38 : Theme.Dark ? (byte)190 : (byte)110));
        var lit = Fill(Accent);
        if (on > 0 && Theme.Cyber)
            dc.DrawRoundedRectangle(new RadialGradientBrush(WithAlpha(Accent, 90), Colors.Transparent), null, new Rect(-4, -4, on * (w + gap) + 6, r.Height + 8), 6, 6);
        for (var i = 0; i < n; i++)
            dc.DrawRoundedRectangle(i < on ? lit : empty, null, new Rect(i * (w + gap), 0, w, r.Height), rad, rad);
    }
}
public static class ColorExt
{
    public static Color WithAlpha(this Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
}

/// <summary>Circular gauge used by the collapsed floating widget.</summary>
public sealed class Ring : ThemedElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(Ring),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(nameof(Accent), typeof(Color), typeof(Ring),
        new FrameworkPropertyMetadata(Colors.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty HasValueProperty = DependencyProperty.Register(nameof(HasValue), typeof(bool), typeof(Ring),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Color Accent { get => (Color)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public bool HasValue { get => (bool)GetValue(HasValueProperty); set => SetValue(HasValueProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        var t = size > 44 ? Math.Max(5, size / 11) : Math.Max(3, size / 7);
        var rad = size / 2 - t / 2 - 0.5;
        var trackColor = Theme.Cupertino ? WithAlpha(Accent, Theme.Dark ? (byte)0x40 : (byte)0x30) : WithAlpha(Theme.Shade, Theme.Dark ? (byte)230 : (byte)110);
        dc.DrawEllipse(null, new Pen(Fill(trackColor), t), c, rad, rad);
        if (!HasValue) return;
        var pct = Math.Clamp(Value, 0, 100);
        if (pct < 0.5) return;
        var pen = new Pen(Fill(Accent), t) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var halo = Theme.Cyber && size > 20 ? new Pen(Fill(WithAlpha(Accent, 55)), t + 5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round } : null;
        if (pct >= 99.9) { if (halo is not null) dc.DrawEllipse(null, halo, c, rad, rad); dc.DrawEllipse(null, pen, c, rad, rad); return; }
        var a = -Math.PI / 2; var b = a + pct / 100 * 2 * Math.PI;
        var fig = new PathFigure(new Point(c.X + rad * Math.Cos(a), c.Y + rad * Math.Sin(a)), [new ArcSegment(new Point(c.X + rad * Math.Cos(b), c.Y + rad * Math.Sin(b)), new Size(rad, rad), 0, pct > 50, SweepDirection.Clockwise, true)], false);
        var arc = new PathGeometry([fig]);
        if (halo is not null) dc.DrawGeometry(null, halo, arc);
        dc.DrawGeometry(null, pen, arc);
    }
}

/// <summary>Test option of the retracted widget: one concentric ring per window, outermost = first (shortest) window.</summary>
public sealed class MultiRing : ThemedElement
{
    public IReadOnlyList<(double Percent, Color Accent)> Values { get; set; } = [];

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0 || Values.Count == 0) return;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        var n = Math.Min(Values.Count, 4);
        var t = Math.Max(2.2, size / (n * 4.2));
        const double gap = 1.2;
        for (var i = 0; i < n; i++)
        {
            var rad = size / 2 - t / 2 - 0.5 - i * (t + gap);
            if (rad <= t) break;
            var (pct, accent) = Values[i];
            dc.DrawEllipse(null, new Pen(Fill(WithAlpha(Theme.Cupertino ? accent : Theme.Shade, Theme.Cupertino ? (byte)0x38 : Theme.Dark ? (byte)230 : (byte)110)), t), c, rad, rad);
            pct = Math.Clamp(pct, 0, 100);
            if (pct < 0.5) continue;
            var pen = new Pen(Fill(accent), t) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (pct >= 99.9) { dc.DrawEllipse(null, pen, c, rad, rad); continue; }
            var a = -Math.PI / 2; var b = a + pct / 100 * 2 * Math.PI;
            var fig = new PathFigure(new Point(c.X + rad * Math.Cos(a), c.Y + rad * Math.Sin(a)), [new ArcSegment(new Point(c.X + rad * Math.Cos(b), c.Y + rad * Math.Sin(b)), new Size(rad, rad), 0, pct > 50, SweepDirection.Clockwise, true)], false);
            dc.DrawGeometry(null, pen, new PathGeometry([fig]));
        }
    }
}

/// <summary>24h history line.</summary>
public sealed class Sparkline : ThemedElement
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(nameof(Points), typeof(IReadOnlyList<HistoryPoint>), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(nameof(Accent), typeof(Color), typeof(Sparkline),
        new FrameworkPropertyMetadata(Colors.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<HistoryPoint>? Points { get => (IReadOnlyList<HistoryPoint>?)GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    public Color Accent { get => (Color)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth; var h = ActualHeight;
        dc.DrawRoundedRectangle(Fill(WithAlpha(Theme.Shade, Theme.Dark ? (byte)120 : (byte)45)), null, new Rect(0, 0, w, h), 8, 8);
        var pts = Points;
        if (pts is null || pts.Count < 2) return;
        // fixed 24 h window ending at the latest reading: a short history leaves the left side empty instead of stretching
        // a few minutes across the whole width (the label says 24 h, so the axis must mean 24 h)
        var t1 = pts[^1].At; var t0 = t1 - TimeSpan.FromHours(24); const double span = 24 * 3600;
        Point P(HistoryPoint p) => new(6 + (w - 12) * Math.Clamp((p.At - t0).TotalSeconds / span, 0, 1), h - 6 - (h - 12) * Math.Clamp(p.Percent, 0, 100) / 100);
        var fig = new PathFigure(P(pts[0]), pts.Skip(1).Select(p => (PathSegment)new LineSegment(P(p), true)), false);
        dc.DrawGeometry(null, new Pen(Fill(Accent), 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, new PathGeometry([fig]));
    }
}
