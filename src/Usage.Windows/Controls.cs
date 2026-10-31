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
    protected static Brush Fill(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    protected static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
    protected static Color Lighten(Color c, double f) => Color.FromRgb((byte)(c.R + (255 - c.R) * f), (byte)(c.G + (255 - c.G) * f), (byte)(c.B + (255 - c.B) * f));
}

/// <summary>Sunken track with a glowing, rounded fill.</summary>
public sealed class NeuBar : ThemedElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(NeuBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(nameof(Accent), typeof(Color), typeof(NeuBar),
        new FrameworkPropertyMetadata(Colors.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Color Accent { get => (Color)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    protected override Size MeasureOverride(Size s) => new(double.IsInfinity(s.Width) ? 100 : s.Width, 10);

    protected override void OnRender(DrawingContext dc)
    {
        var r = new Rect(0, 0, ActualWidth, ActualHeight);
        var rad = r.Height / 2;
        var track = new LinearGradientBrush(Theme.Shade.WithAlpha(Theme.Dark ? (byte)255 : (byte)150), Theme.Base, 90);
        track.Freeze();
        dc.DrawRoundedRectangle(track, null, r, rad, rad);
        // soft highlight along the lower edge: reads as a recessed groove
        dc.DrawRoundedRectangle(null, new Pen(Fill(WithAlpha(Theme.Light, 200)), 1), new Rect(0.5, 0.5, r.Width - 1, r.Height - 1), rad, rad);
        var pct = Math.Clamp(Value, 0, 100);
        if (pct <= 0.2) return;
        var w = Math.Max(r.Height - 2, (r.Width - 2) * pct / 100);
        var fill = new Rect(1, 1, Math.Min(w, r.Width - 2), r.Height - 2);
        var glow = new RadialGradientBrush(WithAlpha(Accent, 70), Colors.Transparent);
        dc.DrawRoundedRectangle(glow, null, new Rect(fill.X - 4, fill.Y - 4, fill.Width + 8, fill.Height + 8), rad + 4, rad + 4);
        var g = new LinearGradientBrush(Lighten(Accent, 0.3), Accent, 90); g.Freeze();
        dc.DrawRoundedRectangle(g, null, fill, rad - 1, rad - 1);
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
        var t = Math.Max(3, size / 7);
        var rad = size / 2 - t / 2 - 0.5;
        dc.DrawEllipse(null, new Pen(Fill(WithAlpha(Theme.Shade, Theme.Dark ? (byte)230 : (byte)110)), t), c, rad, rad);
        if (!HasValue) return;
        var pct = Math.Clamp(Value, 0, 100);
        if (pct < 0.5) return;
        var pen = new Pen(Fill(Accent), t) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (pct >= 99.9) { dc.DrawEllipse(null, pen, c, rad, rad); return; }
        var a = -Math.PI / 2; var b = a + pct / 100 * 2 * Math.PI;
        var fig = new PathFigure(new Point(c.X + rad * Math.Cos(a), c.Y + rad * Math.Sin(a)), [new ArcSegment(new Point(c.X + rad * Math.Cos(b), c.Y + rad * Math.Sin(b)), new Size(rad, rad), 0, pct > 50, SweepDirection.Clockwise, true)], false);
        dc.DrawGeometry(null, pen, new PathGeometry([fig]));
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
        var t0 = pts[0].At; var span = Math.Max(1, (pts[^1].At - t0).TotalSeconds);
        Point P(HistoryPoint p) => new(6 + (w - 12) * (p.At - t0).TotalSeconds / span, h - 6 - (h - 12) * Math.Clamp(p.Percent, 0, 100) / 100);
        var fig = new PathFigure(P(pts[0]), pts.Skip(1).Select(p => (PathSegment)new LineSegment(P(p), true)), false);
        dc.DrawGeometry(null, new Pen(Fill(Accent), 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, new PathGeometry([fig]));
    }
}
