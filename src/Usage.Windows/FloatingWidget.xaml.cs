using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Usage.Windows;

/// <summary>
/// Pill pinned to a screen corner. Hover: expands with every window of every provider.
/// Hover on a provider: a detailed report (all windows, models, details, 24h history).
/// </summary>
public partial class FloatingWidget : Window
{
    const int GWL_EXSTYLE = -20, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80;
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    readonly AppState state;
    readonly DispatcherTimer collapseTimer = new() { Interval = TimeSpan.FromMilliseconds(380) };
    readonly DispatcherTimer detailTimer = new() { Interval = TimeSpan.FromMilliseconds(140) };
    bool expanded, dirty;
    ProviderVM? detailFor;

    public FloatingWidget(AppState state)
    {
        this.state = state;
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };
        SizeChanged += (_, _) => { Reposition(); ApplyGlass(); };
        Theme.Changed += () => Dispatcher.BeginInvoke(ApplyGlass);
        Loaded += (_, _) => { Fit(); ApplyGlass(); };
        collapseTimer.Tick += (_, _) => { collapseTimer.Stop(); SetExpanded(false); };
        detailTimer.Tick += (_, _) => { detailTimer.Stop(); Detail.IsOpen = false; detailFor = null; };
        Detail.Placement = PlacementMode.Custom;
        Detail.PopupAnimation = PopupAnimation.None;
        Detail.CustomPopupPlacementCallback = PlaceDetail;
        Root.MouseEnter += (_, _) => { collapseTimer.Stop(); SetExpanded(true); };
        Root.MouseLeave += (_, _) => { collapseTimer.Start(); if (dirty) Dispatcher.BeginInvoke(Rebuild, DispatcherPriority.Background); };
        MouseDoubleClick += (_, _) => ((App)Application.Current).ShowMain();
        ContextMenu = BuildMenu();
        state.Updated += () => Dispatcher.BeginInvoke(Rebuild);
        ApplySettings();
        Rebuild();
    }

    ContextMenu BuildMenu()
    {
        var m = new ContextMenu();
        MenuItem Item(string t, Action a) { var i = new MenuItem { Header = t }; i.Click += (_, _) => a(); return i; }
        var app = (App)Application.Current;
        m.Items.Add(Item(Loc.T("Abrir", "Open"), app.ShowMain));
        m.Items.Add(Item(Loc.T("Atualizar agora", "Refresh now"), () => _ = state.Service.RefreshAsync()));
        m.Items.Add(Item(Loc.T("Ocultar widget", "Hide widget"), () => { state.Settings.WidgetEnabled = false; state.Settings.Save(); app.OnSettingsChanged(); }));
        m.Items.Add(new Separator());
        m.Items.Add(Item(Loc.T("Sair", "Quit"), app.Quit));
        return m;
    }

    bool DockedRight => state.Settings.WidgetEdge != "Left";

    /// <summary>Cupertino: real acrylic blur behind the glass surface (clipped to it; the side against the screen edge stays square).</summary>
    void ApplyGlass()
    {
        if (!IsLoaded || ActualWidth <= 0) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var m = Root.Margin;
        const double radius = 22;
        // extend past the docked edge so only the corners facing the screen interior are rounded
        var left = DockedRight ? m.Left : -radius;
        var right = DockedRight ? ActualWidth + radius : ActualWidth - m.Right;
        var rect = new Rect(left * dpi.DpiScaleX, m.Top * dpi.DpiScaleY, (right - left) * dpi.DpiScaleX, (ActualHeight - m.Top - m.Bottom) * dpi.DpiScaleY);
        Theme.ApplyLayeredBlur(this, rect, radius * dpi.DpiScaleX);
    }

    public void ApplySettings()
    {
        Topmost = state.Settings.WidgetAlwaysOnTop;
        ContextMenu = BuildMenu();
        Surface.Template = (ControlTemplate)FindResource(DockedRight ? "DockRightTemplate" : "DockLeftTemplate");
        Root.Margin = DockedRight ? new Thickness(16, 16, 0, 16) : new Thickness(0, 16, 16, 16);
        Reposition();
    }

    // ---- layout --------------------------------------------------------------------------------------------

    /// <summary>Flush against the screen edge, vertically centred: it never covers the title-bar buttons.</summary>
    /// <summary>
    /// Sizes the window to its content by hand. SizeToContent sometimes keeps the expanded width after the panel
    /// collapses (the window then looks wide until the next hover), so the size is always set explicitly.
    /// </summary>
    void Fit()
    {
        Root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var d = Root.DesiredSize;
        if (d.Width <= 0 || d.Height <= 0) return;
        if (Math.Abs(Width - d.Width) > 0.5 || double.IsNaN(Width)) Width = d.Width;
        if (Math.Abs(Height - d.Height) > 0.5 || double.IsNaN(Height)) Height = d.Height;
        Reposition();
    }

    void Reposition()
    {
        if (ActualWidth <= 0) return;
        var wa = SystemParameters.WorkArea;
        Left = DockedRight ? wa.Right - ActualWidth : wa.Left;
        Top = wa.Top + (wa.Height - ActualHeight) / 2;
    }

    void SetExpanded(bool value)
    {
        if (expanded == value) return;
        expanded = value;
        Collapsed.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        Expanded.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        if (!value) { detailTimer.Stop(); Detail.IsOpen = false; detailFor = null; }
        var fade = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        (value ? (UIElement)Expanded : Collapsed).BeginAnimation(OpacityProperty, fade);
        Rebuild();
        Fit();
    }

    // ---- content -------------------------------------------------------------------------------------------

    void Rebuild()
    {
        var ps = state.Providers;
        Collapsed.Children.Clear();
        if (ps.Count == 0) Collapsed.Children.Add(new TextBlock { Text = state.Service.IsRefreshing ? "…" : "AI", Style = (Style)FindResource("Label"), FontWeight = FontWeights.Bold, Margin = new Thickness(8, 6, 8, 6) });
        foreach (var p in ps)
        {
            // The ring is the 5-hour window when there is one (an idle 5 h window is 0 %, never replaced by the weekly one),
            // coloured by that window's own level, not by the provider's worst window.
            var shown = p.Windows.FirstOrDefault(w => w.Id == "five_hour") ?? p.Windows.FirstOrDefault();
            var all = state.Settings.WidgetAllRings && p.Windows.Count > 1;
            var size = all ? 30.0 : 24.0;
            var cell = new Grid { Width = size, Height = size, Margin = new Thickness(0, 3, 0, 3) };
            if (all) cell.Children.Add(new MultiRing { Values = p.Windows.Select(w => (w.Percent, w.Accent)).ToList() });
            else cell.Children.Add(new Ring { Value = shown?.Percent ?? 0, HasValue = shown is not null, Accent = shown?.Accent ?? p.Accent });
            var iconSize = all ? 10.0 : 12.0;
            if (p.ShowIcon) cell.Children.Add(new ProviderIcon { IconId = p.IconId, Accent = p.Accent, Width = iconSize, Height = iconSize, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            else cell.Children.Add(new System.Windows.Shapes.Ellipse { Width = 6, Height = 6, Fill = p.AccentBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            Collapsed.Children.Add(cell);
        }

        Dispatcher.BeginInvoke(Fit, DispatcherPriority.Loaded);
        if (!expanded) return;
        // Rebuilding while the pointer is over the panel destroys the element under it, which fires
        // MouseLeave/MouseEnter again and makes the details flicker: postpone until the pointer leaves.
        if (Root.IsMouseOver && Expanded.Children.Count > 0) { dirty = true; return; }
        dirty = false;
        Expanded.Children.Clear();
        Expanded.Children.Add(new TextBlock { Text = state.StatusLine(), Style = (Style)FindResource("Dim"), FontSize = 11, Margin = new Thickness(2, 0, 0, 6) });
        foreach (var p in ps) Expanded.Children.Add(ProviderBlock(p));
        if (ps.Count == 0) Expanded.Children.Add(new TextBlock { Text = Loc.T("Aguardando dados…", "Waiting for data…"), Style = (Style)FindResource("Dim") });
        Dispatcher.BeginInvoke(Fit, DispatcherPriority.Loaded);
    }

    FrameworkElement ProviderBlock(ProviderVM p)
    {
        var box = new Border { Padding = new Thickness(6, 6, 6, 4), Margin = new Thickness(0, 2, 0, 2), CornerRadius = new CornerRadius(12), Background = Brushes.Transparent };
        var stack = new StackPanel();
        var head = new DockPanel { LastChildFill = true };
        if (p.ShowIcon) head.Children.Add(new ProviderIcon { IconId = p.IconId, Accent = p.Accent, Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        if (p.Plan is not null)
        {
            var chip = new TextBlock { Text = p.Plan, Style = (Style)FindResource("Dim"), FontSize = 10.5, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(chip, Dock.Right); head.Children.Add(chip);
        }
        head.Children.Add(new TextBlock { Text = p.Name, FontWeight = FontWeights.Bold, FontSize = 14, Style = (Style)FindResource("Label") });
        stack.Children.Add(head);

        if (p.Windows.Count == 0)
            stack.Children.Add(new TextBlock { Text = p.Notice ?? "", Style = (Style)FindResource("Dim"), FontSize = 11.5, Margin = new Thickness(17, 4, 0, 2), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.None, MaxWidth = 270 });
        foreach (var w in p.Windows)
        {
            var g = new Grid { Margin = new Thickness(17, 5, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
            var l = new TextBlock { Text = w.ShortLabel, Style = (Style)FindResource("Dim"), VerticalAlignment = VerticalAlignment.Center };
            var bar = new NeuBar { Value = w.Percent, Accent = w.Accent, Height = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var pct = new TextBlock { Text = w.PercentText, FontWeight = FontWeights.Bold, FontSize = 13, Foreground = w.AccentBrush, TextAlignment = TextAlignment.Right, Style = (Style)FindResource("Label"), VerticalAlignment = VerticalAlignment.Center };
            var cd = new TextBlock { Text = w.Countdown, Style = (Style)FindResource("Dim"), FontSize = 11, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(bar, 1); Grid.SetColumn(pct, 2); Grid.SetColumn(cd, 3);
            g.Children.Add(l); g.Children.Add(bar); g.Children.Add(pct); g.Children.Add(cd);
            stack.Children.Add(g);
        }
        box.Child = stack;
        box.MouseEnter += (_, _) => { detailTimer.Stop(); box.Background = (Brush)FindResource("ShadeBrush"); ShowDetail(p, box); };
        box.MouseLeave += (_, _) => { box.Background = Brushes.Transparent; detailTimer.Start(); };
        return box;
    }

    FrameworkElement? detailTarget;

    /// <summary>Beside the hovered row, clamped to the work area so it never slides over the widget itself.</summary>
    CustomPopupPlacement[] PlaceDetail(Size popup, Size target, Point offset)
    {
        var wa = SystemParameters.WorkArea;
        var src = PresentationSource.FromVisual(Surface);
        var m = src?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var surfTop = Surface.PointToScreen(new Point(0, 0)).Y / m.M22;
        var rowTop = detailTarget is { IsLoaded: true } t ? t.PointToScreen(new Point(0, 0)).Y / m.M22 : surfTop;
        var y = Math.Min(Math.Max(rowTop, wa.Top), Math.Max(wa.Top, wa.Bottom - popup.Height)) - surfTop;
        var x = DockedRight ? -popup.Width : target.Width;
        return [new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.None)];
    }

    void ShowDetail(ProviderVM p, FrameworkElement target)
    {
        if (ReferenceEquals(detailFor, p) && Detail.IsOpen) return;
        detailFor = p;
        detailTarget = target;
        var s = new StackPanel { Width = 300 };
        s.Children.Add(new TextBlock { Text = p.Name + (p.Plan is null ? "" : " · " + p.Plan), FontSize = 16, FontWeight = FontWeights.Bold, Style = (Style)FindResource("Label"), Margin = new Thickness(0, 0, 0, 4) });
        if (p.Notice is not null) s.Children.Add(new TextBlock { Text = p.Notice, Style = (Style)FindResource("Dim"), Foreground = (Brush)FindResource("WarnBrush"), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.None, Margin = new Thickness(0, 0, 0, 4) });
        foreach (var w in p.Windows)
        {
            var head = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var pct = new TextBlock { Text = w.PercentText, FontSize = 17, FontWeight = FontWeights.Bold, Foreground = w.AccentBrush, Style = (Style)FindResource("Label") };
            DockPanel.SetDock(pct, Dock.Right); head.Children.Add(pct);
            head.Children.Add(new TextBlock { Text = w.Label, FontWeight = FontWeights.SemiBold, Style = (Style)FindResource("Label"), VerticalAlignment = VerticalAlignment.Bottom });
            s.Children.Add(head);
            s.Children.Add(new NeuBar { Value = w.Percent, Accent = w.Accent, Height = 10, Margin = new Thickness(0, 4, 0, 3) });
            s.Children.Add(new TextBlock { Text = w.ResetLine + (w.HasElapsed ? "  ·  " + Loc.T($"{w.Elapsed:P0} da janela decorrida", $"{w.Elapsed:P0} of window elapsed") : ""), Style = (Style)FindResource("Dim"), FontSize = 11, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.None });
        }
        if (p.Models.Count > 0)
        {
            s.Children.Add(new TextBlock { Text = Loc.T("Por modelo / produto", "By model / product"), FontWeight = FontWeights.SemiBold, Style = (Style)FindResource("Label"), FontSize = 12, Margin = new Thickness(0, 12, 0, 2) });
            foreach (var m in p.Models.Take(8))
            {
                var row = new DockPanel();
                var v = new TextBlock { Text = m.Value, Style = (Style)FindResource("Label"), FontSize = 12, FontWeight = FontWeights.SemiBold };
                DockPanel.SetDock(v, Dock.Right); row.Children.Add(v);
                row.Children.Add(new TextBlock { Text = m.Name, Style = (Style)FindResource("Dim") });
                s.Children.Add(row);
            }
        }
        foreach (var d in p.Details)
        {
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var v = new TextBlock { Text = d.Value, Style = (Style)FindResource("Label"), FontSize = 12, FontWeight = FontWeights.SemiBold };
            DockPanel.SetDock(v, Dock.Right); row.Children.Add(v);
            row.Children.Add(new TextBlock { Text = d.Label, Style = (Style)FindResource("Dim") });
            s.Children.Add(row);
        }
        if (p.History.Count > 1)
        {
            s.Children.Add(new TextBlock { Text = Loc.T("Últimas 24 h", "Last 24 h"), FontWeight = FontWeights.SemiBold, Style = (Style)FindResource("Label"), FontSize = 12, Margin = new Thickness(0, 12, 0, 4) });
            s.Children.Add(new Sparkline { Points = p.History, Accent = p.Accent, Height = 44 });
        }
        s.Children.Add(new TextBlock { Text = p.UpdatedText, Style = (Style)FindResource("Dim"), FontSize = 10.5, Margin = new Thickness(0, 10, 0, 0) });

        var card = new ContentControl { Style = (Style)FindResource("Card"), Content = s, Margin = new Thickness(16) };
        Detail.Child = card;
        Detail.PlacementTarget = Surface;
        if (Detail.IsOpen) { Detail.IsOpen = false; }   // re-evaluate placement for the new row, without a visible gap (no fade)
        Detail.IsOpen = true;
    }
}
