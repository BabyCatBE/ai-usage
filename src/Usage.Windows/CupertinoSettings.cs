using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Usage.Windows;

/// <summary>
/// iOS-style settings building blocks for the Cupertino look: inset-grouped sections (uppercase header, rounded card of rows
/// separated by hairlines, footer note), switches, segmented controls and checkmark rows. Same pattern as the phone app.
/// </summary>
public sealed class SettingsList
{
    readonly Panel host;
    StackPanel? rows;
    public SettingsList(Panel host) => this.host = host;

    static SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    static Color A(byte a, Color c) => Color.FromArgb(a, c.R, c.G, c.B);
    static Brush Text => B(Theme.Text);
    static Brush Dim => B(Theme.Dim);
    static FontFamily Font => (FontFamily)Application.Current.FindResource("UiFont");

    public void Title(string t) => host.Children.Add(new TextBlock { Text = t, FontFamily = Font, FontSize = 30, FontWeight = FontWeights.Bold, Foreground = Text, Margin = new Thickness(4, 0, 0, 2) });

    /// <summary>Starts a section: header above, rows go into one rounded card.</summary>
    public void Section(string header)
    {
        host.Children.Add(new TextBlock { Text = header.ToUpperInvariant(), FontFamily = Font, FontSize = 11.5, Foreground = Dim, Margin = new Thickness(16, 18, 0, 6) });
        rows = new StackPanel();
        host.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(16), ClipToBounds = true, Child = rows,
            Background = B(Theme.Dark ? Color.FromArgb(0xE6, 0x1C, 0x1C, 0x1E) : Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)),
            BorderBrush = B(A(Theme.Dark ? (byte)0x30 : (byte)0x50, Theme.Dim)), BorderThickness = new Thickness(1),
        });
    }

    public void Add(UIElement row)
    {
        if (rows is null) Section("");
        if (rows!.Children.Count > 0)
            rows.Children.Add(new Border { Height = 1, Margin = new Thickness(16, 0, 0, 0), Background = B(A(Theme.Dark ? (byte)0x50 : (byte)0x40, Theme.Dim)) });
        rows.Children.Add(row);
    }

    /// <summary>Wraps any control as a row (padding like a list cell).</summary>
    public void AddPadded(UIElement e, double v = 10) => Add(new Border { Padding = new Thickness(12, v, 12, v), Child = e });

    public void Footer(string text) => host.Children.Add(new TextBlock { Text = text, FontFamily = Font, FontSize = 11.5, Foreground = Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 6, 16, 0) });

    // ---- rows ------------------------------------------------------------------------------------------------

    public void SwitchRow(string text, bool on, Action<bool> set)
    {
        var g = new Grid { Margin = new Thickness(16, 10, 14, 10) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new TextBlock { Text = text, FontFamily = Font, FontSize = 14.5, Foreground = Text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        var sw = new IosSwitch(on, set);
        Grid.SetColumn(sw, 1);
        g.Children.Add(sw);
        // the whole row toggles, like a list cell
        g.Background = Brushes.Transparent;
        g.MouseLeftButtonUp += (_, e) => { if (!sw.IsMouseOver) sw.Toggle(); };
        Add(g);
    }

    public void CheckRow(string text, bool selected, Action tap)
    {
        var g = new Grid { Margin = new Thickness(16, 11, 14, 11), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new TextBlock { Text = text, FontFamily = Font, FontSize = 14.5, Foreground = Text, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        if (selected)
        {
            var tick = new Path
            {
                Data = Geometry.Parse("M 1,7 L 5.5,11.5 L 14,1.5"), Stroke = B(Theme.Highlight), StrokeThickness = 2.4,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Width = 16, Height = 13, Stretch = Stretch.None,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(tick, 1);
            g.Children.Add(tick);
        }
        g.MouseLeftButtonUp += (_, _) => tap();
        Add(g);
    }

    public void SegmentedRow(string[] labels, int selected, Action<int> pick) => AddPadded(new IosSegmented(labels, selected, pick));
}

/// <summary>iOS switch: 51x31 track, white knob with a soft shadow, green when on. The knob glides on a cubic ease.</summary>
public sealed class IosSwitch : Border
{
    readonly Border knob = new() { Width = 27, Height = 27, CornerRadius = new CornerRadius(13.5), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
    readonly TranslateTransform move = new();
    readonly Action<bool> changed;
    public bool On { get; private set; }

    public IosSwitch(bool on, Action<bool> changed)
    {
        this.changed = changed; On = on;
        Width = 51; Height = 31; CornerRadius = new CornerRadius(15.5); Cursor = Cursors.Hand; VerticalAlignment = VerticalAlignment.Center;
        knob.RenderTransform = move;
        knob.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 1.5, Direction = 270, Opacity = 0.3, RenderingBias = RenderingBias.Performance };
        Child = knob;
        Paint(false);
        MouseLeftButtonUp += (_, _) => Toggle();
    }

    void Paint(bool animate)
    {
        var off = Theme.Dark ? Color.FromRgb(0x39, 0x39, 0x3D) : Color.FromRgb(0xE9, 0xE9, 0xEA);
        var green = Theme.Dark ? Color.FromRgb(0x30, 0xD1, 0x58) : Color.FromRgb(0x34, 0xC7, 0x59);
        Background = new SolidColorBrush(On ? green : off);
        var to = On ? 20.0 : 0.0;
        if (animate) move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        else move.X = to;
    }

    public void Toggle() { On = !On; Paint(true); changed(On); }
}

/// <summary>iOS segmented control: capsule track, a thumb that slides to the chosen segment.</summary>
public sealed class IosSegmented : Grid
{
    readonly Border thumb = new() { CornerRadius = new CornerRadius(14), Margin = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Left };
    readonly TranslateTransform move = new();
    readonly string[] labels;
    readonly Action<int> pick;
    int index;

    public IosSegmented(string[] labels, int selected, Action<int> pick)
    {
        this.labels = labels; this.pick = pick; index = Math.Clamp(selected, 0, labels.Length - 1);
        Height = 32; Cursor = Cursors.Hand; Background = Brushes.Transparent;
        var track = new Border { CornerRadius = new CornerRadius(16), Background = new SolidColorBrush(Theme.Dark ? Color.FromArgb(0x3D, 0x76, 0x76, 0x80) : Color.FromArgb(0x1F, 0x76, 0x76, 0x80)) };
        Children.Add(track);
        thumb.Background = new SolidColorBrush(Theme.Dark ? Color.FromRgb(0x63, 0x63, 0x66) : Colors.White);
        thumb.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 1, Direction = 270, Opacity = Theme.Dark ? 0.4 : 0.15, RenderingBias = RenderingBias.Performance };
        thumb.RenderTransform = move;
        Children.Add(thumb);
        var labelsGrid = new UniformGrid { Rows = 1, Columns = labels.Length };
        for (var i = 0; i < labels.Length; i++)
            labelsGrid.Children.Add(new TextBlock { Text = labels[i], FontFamily = (FontFamily)Application.Current.FindResource("UiFont"), FontSize = 12.5, FontWeight = i == index ? FontWeights.SemiBold : FontWeights.Medium, Foreground = new SolidColorBrush(Theme.Text), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        Children.Add(labelsGrid);
        SizeChanged += (_, _) => Place(false);
        MouseLeftButtonUp += (_, e) =>
        {
            var i = (int)Math.Clamp(e.GetPosition(this).X / (ActualWidth / labels.Length), 0, labels.Length - 1);
            if (i == index) return;
            index = i; Place(true);
            for (var k = 0; k < labelsGrid.Children.Count; k++) ((TextBlock)labelsGrid.Children[k]).FontWeight = k == index ? FontWeights.SemiBold : FontWeights.Medium;
            this.pick(i);
        };
    }

    void Place(bool animate)
    {
        if (ActualWidth <= 4) return;
        var w = ActualWidth / labels.Length;
        thumb.Width = Math.Max(0, w - 4);
        var to = index * w;
        if (animate) move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        else move.X = to;
    }
}

/// <summary>Equal-width columns (System.Windows.Controls.Primitives.UniformGrid needs its own using; kept local for clarity).</summary>
sealed class UniformGrid : System.Windows.Controls.Primitives.UniformGrid { }
