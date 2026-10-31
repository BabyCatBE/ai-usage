using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Usage.Windows;

/// <summary>Provider glyph drawn from simple shapes (same designs as the Android app).</summary>
public sealed class ProviderIcon : ThemedElement
{
    public static readonly DependencyProperty IconIdProperty = DependencyProperty.Register(nameof(IconId), typeof(string), typeof(ProviderIcon),
        new FrameworkPropertyMetadata("generic", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(nameof(Accent), typeof(Color), typeof(ProviderIcon),
        new FrameworkPropertyMetadata(Colors.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public string IconId { get => (string)GetValue(IconIdProperty); set => SetValue(IconIdProperty, value); }
    public Color Accent { get => (Color)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    static readonly string[] Mascot = ["..XXXXXXX..", ".XXXXXXXXX.", "XXXeXXXeXXX", "XXXXXXXXXXX", ".XXXXXXXXX.", "..X.X.X.X.."];
    static readonly Dictionary<string, string> Letters = new() { ["kimi"] = "K", ["glm"] = "Z", ["qwen"] = "Q", ["minimax"] = "M", ["deepseek"] = "D" };

    protected override void OnRender(DrawingContext dc)
    {
        var s = Math.Min(ActualWidth, ActualHeight);
        if (s <= 0) return;
        var r = s / 2; var cx = ActualWidth / 2; var cy = ActualHeight / 2;
        var brush = Fill(Accent);
        var pen = new Pen(brush, Math.Max(1.4, r * 0.17)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        switch (IconId)
        {
            case "chatgpt":
                for (var i = 0; i < 3; i++)
                {
                    dc.PushTransform(new RotateTransform(i * 60, cx, cy));
                    dc.DrawRoundedRectangle(null, pen, new Rect(cx - r * 0.56, cy - r * 0.2, r * 1.12, r * 0.4), r * 0.2, r * 0.2);
                    dc.Pop();
                }
                break;
            case "codex":
                dc.DrawRoundedRectangle(null, pen, new Rect(cx - r * 0.64, cy - r * 0.5, r * 1.28, r), r * 0.3, r * 0.3);
                dc.DrawGeometry(null, pen, new PathGeometry([new PathFigure(new Point(cx - r * 0.3, cy - r * 0.2),
                    [new LineSegment(new Point(cx - r * 0.06, cy), true), new LineSegment(new Point(cx - r * 0.3, cy + r * 0.2), true)], false)]));
                dc.DrawLine(pen, new Point(cx + r * 0.06, cy + r * 0.22), new Point(cx + r * 0.32, cy + r * 0.22));
                break;
            case "claude":
                for (var i = 0; i < 8; i++)
                {
                    var a = (i * 45 + 22.5) * Math.PI / 180;
                    var outer = i % 2 == 0 ? r * 0.66 : r * 0.5; var inner = r * 0.12;
                    dc.DrawLine(pen, new Point(cx + inner * Math.Cos(a), cy + inner * Math.Sin(a)), new Point(cx + outer * Math.Cos(a), cy + outer * Math.Sin(a)));
                }
                break;
            case "claude-code":
            {
                var cell = r * 1.7 / 11; var x0 = cx - cell * 5.5; var y0 = cy - cell * 3;
                var eye = Fill(Color.FromRgb(0x2A, 0x2E, 0x35));
                for (var row = 0; row < Mascot.Length; row++)
                    for (var col = 0; col < Mascot[row].Length; col++)
                        if (Mascot[row][col] != '.')
                            dc.DrawRectangle(Mascot[row][col] == 'e' ? eye : brush, null, new Rect(x0 + col * cell, y0 + row * cell, cell + 0.3, cell + 0.3));
                break;
            }
            case "opencode":
                dc.DrawRoundedRectangle(null, pen, new Rect(cx - r * 0.46, cy - r * 0.58, r * 0.92, r * 1.16), r * 0.12, r * 0.12);
                dc.DrawRectangle(brush, null, new Rect(cx - r * 0.2, cy - r * 0.28, r * 0.4, r * 0.56));
                break;
            default:
                if (Letters.TryGetValue(IconId, out var letter))
                {
                    var t = new FormattedText(letter, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), r * 1.3, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                    dc.DrawText(t, new Point(cx - t.Width / 2, cy - t.Height / 2));
                }
                else dc.DrawEllipse(brush, null, new Point(cx, cy), r * 0.32, r * 0.32);
                break;
        }
    }
}
