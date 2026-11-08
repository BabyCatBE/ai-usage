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
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    static readonly IntPtr HWND_TOPMOST = new(-1), HWND_NOTOPMOST = new(-2);
    const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;

    readonly AppState state;
    readonly DispatcherTimer collapseTimer = new() { Interval = TimeSpan.FromMilliseconds(380) };
    readonly DispatcherTimer detailTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };
    bool expanded, dirty;
    readonly DispatcherTimer forecastTimer = new() { Interval = TimeSpan.FromMilliseconds(160) };
    long settleUntil;
    FrameworkElement? pendingForecastWrap; ProviderVM? pendingForecastP;
    ProviderVM? detailFor, forecastFor;
    /// <summary>Stage 4: the forecast opens beside the detail popup, which stays visible.</summary>
    readonly Popup Forecast = new() { AllowsTransparency = true, StaysOpen = true, Focusable = false, Placement = PlacementMode.Custom, PopupAnimation = PopupAnimation.None };
    bool OverPopup => (Detail.IsOpen && Detail.Child is { IsMouseOver: true }) || (Forecast.IsOpen && Forecast.Child is { IsMouseOver: true });
    ContentControl? detailCard;
    Border? detailBacking;

    /// <summary>
    /// The popups are placed once, when they open. The widget grows (retracted -> expanded) a moment later, which used to leave an
    /// already-open detail drawn over the expanded panel: every size/position change of the widget places them again.
    /// </summary>
    void RePlacePopups()
    {
        foreach (var pop in new[] { Detail, Forecast })
        {
            if (!pop.IsOpen) continue;
            var h = pop.HorizontalOffset;
            pop.HorizontalOffset = h + 1;   // any offset change makes WPF run the custom placement again
            pop.HorizontalOffset = h;
        }
    }
    void CloseDetail() { forecastTimer.Stop(); pendingForecastP = null; pendingForecastWrap = null; ClosePopup(Detail); ClosePopup(Forecast); detailFor = null; forecastFor = null; }

    /// <summary>Outer-rounded card (the side facing the screen edge is square): the widget, the detail and the forecast read as one connected strip.</summary>
    ContentControl PanelCard(UIElement content, bool outer = true)
    {
        var tpl = outer ? (DockedRight ? "DockRightTemplate" : "DockLeftTemplate") : "FlatTemplate";
        return new ContentControl { Style = (Style)FindResource("Card"), Template = (ControlTemplate)FindResource(tpl), Content = content, Margin = new Thickness(16) };
    }

    /// <summary>Fades a popup out before closing it (an instant close looks abrupt next to the slide-in).</summary>
    static void ClosePopup(Popup pop)
    {
        if (!pop.IsOpen) return;
        if (pop.Child is not FrameworkElement el) { pop.IsOpen = false; return; }
        var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(130)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        a.Completed += (_, _) => { if (ReferenceEquals(pop.Child, el) && el.Opacity <= 0.01) pop.IsOpen = false; };
        el.BeginAnimation(OpacityProperty, a);
    }

    /// <summary>Content eases in while sliding out from under the panel it is attached to.</summary>
    void SlideIn(FrameworkElement el, double distance)
    {
        var from = DockedRight ? distance : -distance;
        var tt = new TranslateTransform(from, 0);
        el.RenderTransform = tt;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
        tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
    }

    public FloatingWidget(AppState state)
    {
        this.state = state;
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            // SetWindowLong(GWL_EXSTYLE) takes the window out of the topmost band (the WS_EX_TOPMOST bit stays):
            // put it back where this stage belongs (collapsed = behind windows, expanded = above them).
            ApplyStageTopmost();
        };
        SizeChanged += (_, _) => { Reposition(); ApplyGlass(); RePlacePopups(); };
        LocationChanged += (_, _) => RePlacePopups();
        Theme.Changed += () => Dispatcher.BeginInvoke(ApplyGlass);
        Loaded += (_, _) => { Fit(); ApplyGlass(); };
        // the pointer may be over the detail / forecast popup (a separate window): keep everything open while it is
        collapseTimer.Tick += (_, _) => { collapseTimer.Stop(); if (OverPopup) return; SetExpanded(false); };
        detailTimer.Tick += (_, _) => { detailTimer.Stop(); if (OverPopup) return; CloseDetail(); };
        forecastTimer.Tick += (_, _) =>
        {
            forecastTimer.Stop();
            if (pendingForecastP is null || pendingForecastWrap is null) return;
            var (p, w) = (pendingForecastP, pendingForecastWrap); pendingForecastP = null; pendingForecastWrap = null;
            // only if this detail is still the one on screen and the pointer is really resting on it
            if (!w.IsMouseOver || !ReferenceEquals(Detail.Child, w) || !ReferenceEquals(detailFor, p)) return;
            if (state.Settings.WidgetForecast) ShowForecast(p);
        };
        Forecast.CustomPopupPlacementCallback = PlaceForecast;
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

    /// <summary>
    /// Collapsed: behind normal windows, so it only shows over the desktop (a maximized window hides it).
    /// Expanded: above the window the pointer came from, because the user reached the widget from outside that window.
    /// </summary>
    void ApplyStageTopmost()
    {
        var on = expanded && state.Settings.WidgetAlwaysOnTop;
        Topmost = on;
        ForceTopmost(on);
    }

    /// <summary>
    /// SetWindowLong(GWL_EXSTYLE) leaves WS_EX_TOPMOST set but drops the window out of the topmost band, and assigning
    /// Topmost = true again does not reapply it (the property value did not change). SetWindowPos is what actually moves
    /// the window into (or out of) that band.
    /// </summary>
    void ForceTopmost(bool on)
    {
        var h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero) return;
        SetWindowPos(h, on ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public void ApplySettings()
    {
        ApplyStageTopmost();
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
        ApplyStageTopmost();   // collapsed: behind windows (desktop only); expanded: above the window the pointer came from
        forecastTimer.Stop(); pendingForecastP = null; pendingForecastWrap = null;
        if (!value) { detailTimer.Stop(); CloseDetail(); }
        // the window resizes and moves a few frames from now: no popup may open until that is over
        settleUntil = Environment.TickCount64 + 280;
        var fade = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        (value ? (UIElement)Expanded : Collapsed).BeginAnimation(OpacityProperty, fade);
        Rebuild();
        Fit();
        UpdateLayout();   // apply the new size/position now, not a frame later
        Fit();
        if (!value) { Detail.IsOpen = false; Forecast.IsOpen = false; }   // never leave a stale popup over the shrunken widget
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
        box.Cursor = Cursors.Hand;   // stage 3 opens on click, not on hover
        box.MouseEnter += (_, _) =>
        {
            detailTimer.Stop(); box.Background = (Brush)FindResource("ShadeBrush");
        };
        box.MouseLeave += (_, _) =>
        {
            box.Background = Brushes.Transparent; detailTimer.Start();
        };
        box.MouseLeftButtonUp += (_, e) =>
        {
            if (e.ClickCount > 1) return;
            e.Handled = true;
            collapseTimer.Stop(); detailTimer.Stop();
            if (ReferenceEquals(detailFor, p) && Detail.IsOpen) { CloseDetail(); return; }   // click again closes
            if (Environment.TickCount64 < settleUntil) return;   // still growing/moving: ignore, click again
            ShowDetail(p, box);
        };
        return box;
    }

    FrameworkElement? detailTarget;

    static double MeasuredWidth(Popup pop)
    {
        if (pop.Child is not FrameworkElement c) return 332;
        c.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return c.DesiredSize.Width > 0 ? c.DesiredSize.Width : 332;
    }

    /// <summary>The popup's own Height can be 0 / stale (its child was just swapped): measure the content instead.</summary>
    static double MeasuredHeight(Popup pop)
    {
        if (pop.Child is not FrameworkElement c) return 0;
        c.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return c.DesiredSize.Height > 0 ? c.DesiredSize.Height : 0;
    }

    /// <summary>Beside the hovered row, clamped to the work area so it never slides over the widget itself.</summary>
    CustomPopupPlacement[] PlaceDetail(Size popup, Size target, Point offset)
    {
        var wa = SystemParameters.WorkArea;
        var src = PresentationSource.FromVisual(Surface);
        var m = src?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var surfTop = Surface.PointToScreen(new Point(0, 0)).Y / m.M22;
        var rowTop = detailTarget is { IsLoaded: true } t ? t.PointToScreen(new Point(0, 0)).Y / m.M22 : surfTop;
        var y = Math.Min(Math.Max(rowTop, wa.Top), Math.Max(wa.Top, wa.Bottom - MeasuredHeight(Detail))) - surfTop;
        // popup.Size can be 0 / stale while the popup is being (re)placed (its child was just swapped): measure the content ourselves
        var pw = popup.Width > 40 ? popup.Width : MeasuredWidth(Detail);
        var x = DockedRight ? -pw + 16 : target.Width - 16;   // card edge flush with the widget
        return [new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.None)];
    }

    /// <summary>Beside the detail popup (one step further from the screen edge), at the same height, clamped to the work area.</summary>
    CustomPopupPlacement[] PlaceForecast(Size popup, Size target, Point offset)
    {
        var wa = SystemParameters.WorkArea;
        var src = PresentationSource.FromVisual(Surface);
        var m = src?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var surfTop = Surface.PointToScreen(new Point(0, 0)).Y / m.M22;
        var surfLeft = Surface.PointToScreen(new Point(0, 0)).X / m.M11;
        // the forecast is shorter than the detail: anchor its top on the detail's real top instead of re-clamping with a
        // popup height that can still be 0 / stale while the child is being swapped (that used to push it upwards)
        var detailTop = Detail.Child is FrameworkElement dc && dc.IsLoaded
            ? dc.PointToScreen(new Point(0, 0)).Y / m.M22
            : detailTarget is { IsLoaded: true } t ? t.PointToScreen(new Point(0, 0)).Y / m.M22
            : surfTop;
        var y = detailTop - surfTop;
        var detailW = MeasuredWidth(Detail);
        var fw = popup.Width > 40 ? popup.Width : MeasuredWidth(Forecast);
        var x = DockedRight ? -(detailW + fw) + 48 : target.Width + detailW - 48;   // forecast card edge flush with the detail card edge (16 px margin each side; the detail itself is shifted 16)
        if (DockedRight) x = Math.Max(x, wa.Left - surfLeft);   // never past the left edge of the screen
        return [new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.None)];
    }

    void ShowDetail(ProviderVM p, FrameworkElement target)
    {
        if (ReferenceEquals(detailFor, p) && Detail.IsOpen) return;   // same provider: the detail (and its forecast) stay as they are
        detailFor = p;
        forecastFor = null; forecastTimer.Stop(); pendingForecastP = null; pendingForecastWrap = null;
        Forecast.IsOpen = false;   // immediately, not through the fade: a half-faded forecast must not linger over the new detail
        Fit(); UpdateLayout();     // fresh geometry for the placement callback
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

        // stage 3 (detail). Pointer over it -> stage 4 (forecast); pointer away -> closes after a short grace period.
        var dcard = PanelCard(s);
        var dback = Backing();
        var wrap = HitArea(dcard, dback);
        wrap.MouseEnter += (_, _) =>
        {
            collapseTimer.Stop(); detailTimer.Stop();
            if (!state.Settings.WidgetForecast || (ReferenceEquals(forecastFor, p) && Forecast.IsOpen)) return;
            pendingForecastP = p; pendingForecastWrap = wrap; forecastTimer.Stop(); forecastTimer.Start();
        };
        wrap.MouseLeave += (_, _) =>
        {
            if (ReferenceEquals(pendingForecastWrap, wrap)) { forecastTimer.Stop(); pendingForecastP = null; pendingForecastWrap = null; }
            if (ReferenceEquals(Detail.Child, wrap)) { detailTimer.Start(); collapseTimer.Start(); }
        };
        detailCard = dcard; detailBacking = dback;
        OpenPopup(Detail, wrap, 26);
    }

    /// <summary>The popup's shadow margin must be hit-testable (a nearly invisible fill) so the pointer can cross from one popup to the next without leaving them.</summary>
    Border HitArea(UIElement card, Border backing)
    {
        var g = new Grid();
        g.Children.Add(backing); g.Children.Add(card);
        return new Border { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), Child = g };
    }

    /// <summary>
    /// Popups are separate windows: nothing blurs what is behind them, so the glass card alone lets the text of the window underneath
    /// show through and fight with the report. A dense backing in the theme colour keeps the glass look and makes it readable anywhere.
    /// </summary>
    Border Backing() => new()
    {
        Margin = new Thickness(16), IsHitTestVisible = false,
        CornerRadius = (CornerRadius)FindResource(DockedRight ? "DockRightRadius" : "DockLeftRadius"),
        Background = new SolidColorBrush(Color.FromArgb(0xE6, Theme.Base.R, Theme.Base.G, Theme.Base.B)),
    };

    void OpenPopup(Popup pop, FrameworkElement content, double slide)
    {
        pop.Child = content;
        pop.PlacementTarget = Surface;
        if (pop.IsOpen) { pop.IsOpen = false; }   // re-evaluate placement for the new content, without a visible gap (no fade)
        pop.IsOpen = true;
        SlideIn(content, slide);
    }

    // ---- stage 4: forecast (beta) -------------------------------------------------------------------------------

    /// <summary>"If I keep using it like this": per window, when it runs out (or what is left at the reset) at the pace of the last hour.</summary>
    void ShowForecast(ProviderVM p)
    {
        if (ReferenceEquals(forecastFor, p) && Forecast.IsOpen) return;
        forecastFor = p;
        if (detailCard is not null) detailCard.Template = (ControlTemplate)FindResource("FlatTemplate");
        if (detailBacking is not null) detailBacking.CornerRadius = new CornerRadius(0);   // the detail now has a panel on each side
        var now = DateTimeOffset.Now;
        var s = new StackPanel { Width = 300 };
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        var beta = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 1, 6, 1), Background = (Brush)FindResource("ShadeBrush"), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "BETA", FontSize = 9.5, FontWeight = FontWeights.Bold, Style = (Style)FindResource("Dim") } };
        DockPanel.SetDock(beta, Dock.Right); head.Children.Add(beta);
        head.Children.Add(new TextBlock { Text = Loc.T("Previsão · ", "Forecast · ") + p.Name, FontSize = 16, FontWeight = FontWeights.Bold, Style = (Style)FindResource("Label") });
        s.Children.Add(head);
        s.Children.Add(new TextBlock { Text = Loc.T("No ritmo da última hora", "At the pace of the last hour"), Style = (Style)FindResource("Dim"), FontSize = 11, Margin = new Thickness(0, 0, 0, 2) });

        if (p.Windows.Count == 0)
            s.Children.Add(new TextBlock { Text = p.Notice ?? "", Style = (Style)FindResource("Dim"), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.None, Margin = new Thickness(0, 8, 0, 0) });
        foreach (var w in p.Windows)
        {
            var f = Usage.Core.Forecast.Project(state.Service.History(p.Id, w.Id), w.Percent, w.ResetsAt, now);
            var row = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            string headline, note = "";
            Brush hb = (Brush)FindResource("TextBrush");
            double projected = w.Percent;
            if (f is null) { headline = Loc.T("Coletando dados…", "Collecting data…"); hb = (Brush)FindResource("DimBrush"); note = Loc.T("Precisa de ~15 min de leituras desde o último reset.", "Needs ~15 min of readings since the last reset."); }
            else if (f.PerHour <= 0) { headline = Loc.T("Sem uso recente", "No recent use"); note = Loc.T($"Nada gasto nos últimos {f.BasisMinutes} min.", $"Nothing used in the last {f.BasisMinutes} min."); }
            else
            {
                var rate = (f.PerHour >= 10 ? $"+{f.PerHour:0}" : $"+{f.PerHour:0.0}") + Loc.T("%/h", "%/h");
                if (f.RunsOutAt is { } out_)
                {
                    headline = Loc.T("Acaba em ~", "Runs out in ~") + Loc.Countdown(out_, now);
                    hb = (Brush)FindResource("CritBrush"); projected = 100;
                    note = Loc.T("às ", "at ") + out_.ToLocalTime().ToString("HH:mm") + " · " + rate;
                }
                else
                {
                    projected = f.AtResetPercent ?? w.Percent;
                    headline = Loc.T($"Sobra ~{100 - projected:0}% no reset", $"~{100 - projected:0}% left at reset");
                    note = Loc.T($"chega a ~{projected:0}% no reset · ", $"reaches ~{projected:0}% by the reset · ") + rate;
                }
            }
            var pct = new TextBlock { Text = headline, FontWeight = FontWeights.Bold, FontSize = 13, Foreground = hb, Style = (Style)FindResource("Label") };
            DockPanel.SetDock(pct, Dock.Right); row.Children.Add(pct);
            row.Children.Add(new TextBlock { Text = w.Label, FontWeight = FontWeights.SemiBold, Style = (Style)FindResource("Label") });
            s.Children.Add(row);
            s.Children.Add(ProjectionBar(w, projected, f is not null));
            if (note.Length > 0) s.Children.Add(new TextBlock { Text = note, Style = (Style)FindResource("Dim"), FontSize = 11, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.None });
        }
        var wrap = HitArea(PanelCard(s), Backing());
        wrap.MouseLeave += (_, _) => { if (ReferenceEquals(Forecast.Child, wrap)) { detailTimer.Start(); collapseTimer.Start(); } };
        wrap.MouseEnter += (_, _) => { collapseTimer.Stop(); detailTimer.Stop(); };
        OpenPopup(Forecast, wrap, 48);
    }

    /// <summary>Track with the current usage filled solid and the projection up to the reset (or to 100 %) as a lighter extension.</summary>
    FrameworkElement ProjectionBar(WindowVM w, double projected, bool has)
    {
        var now = Math.Clamp(w.Percent, 0, 100);
        var end = has ? Math.Clamp(projected, now, 100) : now;
        var track = new Border { Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 4, 0, 3), ClipToBounds = true, Background = (Brush)FindResource("ShadeBrush") };
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(now, 0.0001), GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(end - now, 0.0001), GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(100 - end, 0.0001), GridUnitType.Star) });
        var solid = new Border { Background = w.AccentBrush };
        var c = w.Accent;
        var ext = new Border { Background = new SolidColorBrush(Color.FromArgb(0x70, c.R, c.G, c.B)) };
        Grid.SetColumn(ext, 1);
        g.Children.Add(solid); g.Children.Add(ext);
        track.Child = g;
        return track;
    }
}
