using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;
using Usage.Core;
using Usage.Core.Link;

namespace Usage.Windows;

public sealed class Conv : IValueConverter
{
    public static readonly Conv BoolToVisible = new();
    public object Convert(object value, Type t, object p, CultureInfo c) => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public partial class MainWindow : Window
{
    public static readonly DependencyProperty CardWidthProperty = DependencyProperty.Register(nameof(CardWidth), typeof(double), typeof(MainWindow), new PropertyMetadata(380.0));
    public double CardWidth { get => (double)GetValue(CardWidthProperty); set => SetValue(CardWidthProperty, value); }

    readonly AppState state;
    readonly System.Windows.Threading.DispatcherTimer resizeTimer = new() { Interval = TimeSpan.FromMilliseconds(110) };
    bool dirty;                       // data changed while the window was hidden: refresh when it is shown
    string? fitKey;                   // last layout inputs: identical inputs reuse the previous result
    Device? pairing;
    string mode = "dashboard";
    static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public MainWindow(AppState state)
    {
        this.state = state;
        InitializeComponent();
        foreach (var (b, g) in new[] { (RefreshButton, ""), (PinButton, ""), (PhoneButton, ""), (SettingsButton, "") })
        {
            b.FontFamily = Icons; b.Content = g; b.FontSize = 15;
        }
        LayoutButton.FontFamily = Icons; LayoutButton.FontSize = 15;
        SourceInitialized += (_, _) => Theme.ApplyChrome(this);
        IsVisibleChanged += (_, e) => { if (e.NewValue is true) Theme.ApplyChrome(this); };
        Theme.Changed += () => Dispatcher.BeginInvoke(() => { Theme.ApplyChrome(this); UpdateLayoutButton(); fitKey = null; resizeTimer.Stop(); resizeTimer.Start(); });
        // while the window is being dragged the Viewbox rescales for free; the (costly) re-layout runs once, when the user stops
        resizeTimer.Tick += (_, _) => { resizeTimer.Stop(); UpdateColumns(); };
        DashboardPanel.SizeChanged += (_, _) => { resizeTimer.Stop(); resizeTimer.Start(); };
        IsVisibleChanged += (_, e) => { if (e.NewValue is true && dirty) { dirty = false; state.Rebuild(); } };
        state.Updated += OnUpdated;
        state.Devices.Changed += () => Dispatcher.BeginInvoke(RefreshDevices);
        SizeChanged += (_, _) => UpdateColumns();
        Closing += (_, e) => { e.Cancel = true; Hide(); }; // lives in the tray
        Topmost = state.Settings.WidgetAlwaysOnTop && false;
        Localize();
        OnUpdated();
        UpdateColumns();
    }

    /// <summary>Header buttons and title shrink with the window so nothing gets cut off.</summary>
    void UpdateHeader()
    {
        var w = ActualWidth;
        var size = w < 340 ? 30 : w < 420 ? 34 : 40;
        foreach (var b in new[] { LayoutButton, RefreshButton, PinButton, PhoneButton, SettingsButton })
        {
            b.Width = b.Height = size; b.Margin = new Thickness(size < 40 ? 1 : 3); b.FontSize = size < 34 ? 13 : 15;
        }
        TitleText.FontSize = w < 340 ? 15 : w < 420 ? 18 : 22;
        Header.Margin = w < 340 ? new Thickness(12, 10, 8, 4) : new Thickness(20, 16, 14, 6);
        ProviderBar.Margin = w < 340 ? new Thickness(8, 0, 6, 2) : new Thickness(16, 0, 12, 4);
        UpdateBarVisibility();
    }

    void UpdateBarVisibility() =>
        ProviderBar.Visibility = state.Settings.ShowProviderBar && mode == "dashboard" && ActualHeight >= 400 ? Visibility.Visible : Visibility.Collapsed;

    const double MinCardWidth = 230;
    /// <summary>Content is never scaled below this: below it the layout gets denser or starts to scroll, so numbers stay readable.</summary>
    const double GoodScale = 0.92, MinScale = 0.8;

    double MeasureCards(int cols, double cardW)
    {
        CardWidth = cardW; Cards.Width = cols * cardW;
        Cards.InvalidateMeasure();
        Cards.Measure(new Size(Cards.Width, double.PositiveInfinity));
        return Cards.DesiredSize.Height;
    }

    /// <summary>Best column count for the current content: tries one to three columns and returns the largest scale that fits the height.</summary>
    (int cols, double scale, double width) SearchColumns(double availW, double availH, int count)
    {
        var best = (cols: 1, scale: -1.0, width: availW);
        for (var c = 1; c <= Math.Min(3, count); c++)
        {
            double scale = 1, width = availW; var ok = true;
            for (var it = 0; it < 3; it++)
            {
                width = availW / Math.Max(0.25, scale);
                var cw = Math.Floor(width / c);
                if (c > 1 && cw < MinCardWidth) { ok = false; break; }
                scale = Math.Min(1, availH / Math.Max(1, MeasureCards(c, cw)));
            }
            if (ok && scale > best.scale + 0.015) best = (c, scale, width);
            if (ok && scale >= 0.999) break;   // fits at natural size with this many columns: no need to look further
        }
        return best;
    }

    /// <summary>Switches the card detail level (0 full, 1 no secondary lines, 2 one line per window) and rebuilds the cards if it changed.</summary>
    void SetFitLevel(int level)
    {
        Theme.SetDensity(level);
        if (state.FitLevel == level) return;
        state.FitLevel = level;
        state.Rebuild(false);
        Cards.ItemsSource = null;
        Cards.ItemsSource = state.Providers;
        Cards.UpdateLayout();   // generate containers + templates now, or the next measure sees the previous level
    }

    /// <summary>
    /// "fit" layout: every provider stays visible AND readable. It tries the full card first, then denser ones (no secondary lines, then one
    /// line per window), choosing columns that make the content scale as little as possible. If even the densest layout would need to be
    /// scaled below a readable size, the window scrolls instead of shrinking the numbers.
    /// "scroll" layout: normal size, columns by width, vertical scrolling.
    /// </summary>
    bool fitBusy, fitAgain;
    void UpdateColumns()
    {
        if (fitBusy) { fitAgain = true; return; }
        fitBusy = true;
        try { FitLayout(); }
        finally { fitBusy = false; }
        if (fitAgain) { fitAgain = false; Dispatcher.BeginInvoke(UpdateColumns, System.Windows.Threading.DispatcherPriority.Loaded); }
    }

    void FitLayout()
    {
        UpdateHeader();
        if (DashboardPanel.ActualWidth < 50 || DashboardPanel.ActualHeight < 30) return;
        var fit = state.Settings.LayoutMode != "scroll";
        var count = Math.Max(1, state.Providers.Count);
        var availW = DashboardPanel.ActualWidth - 12 - 8;
        var availH = DashboardPanel.ActualHeight - 4;
        var key = $"{availW:0}|{availH:0}|{fit}|{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(state.Providers)}|{state.Settings.ViewStyle}|{state.Settings.Density}";
        if (key == fitKey) return;
        fitKey = key;

        int cols; double cardW; var scales = false;
        if (fit)
        {
            availW = DashboardPanel.ActualWidth - 12;
            var pick = (level: 0, cols: 1, scale: -1.0, width: availW);
            for (var level = 0; level <= 2; level++)
            {
                SetFitLevel(level);
                var r = SearchColumns(availW, availH, count);
                if (r.scale > pick.scale + 0.001) pick = (level, r.cols, r.scale, r.width);
                if (r.scale >= GoodScale) { pick = (level, r.cols, r.scale, r.width); break; }
            }
            if (pick.scale >= MinScale)
            {
                SetFitLevel(pick.level);
                cols = pick.cols; cardW = Math.Floor(pick.width / cols); scales = true;
            }
            else
            {
                // would be unreadable: densest layout at natural size, scrolling
                SetFitLevel(2);
                availW = DashboardPanel.ActualWidth - 12 - 8;
                cols = Math.Clamp((int)(availW / 300), 1, 3);
                cardW = Math.Floor(availW / cols);
            }
        }
        else
        {
            SetFitLevel(0);
            cols = Math.Clamp((int)(availW / 360), 1, 3);
            cardW = Math.Floor(availW / cols);
        }

        FitBox.Stretch = scales ? Stretch.Uniform : Stretch.None;
        FitBox.HorizontalAlignment = scales ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        DashboardPanel.VerticalScrollBarVisibility = scales ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        CardWidth = cardW; Cards.Width = cols * cardW;
        EmptyText.Width = Math.Max(120, Cards.Width - 48);
    }
    // ---- provider chips + layout mode -------------------------------------------------------------------------

    void BuildProviderBar()
    {
        var st = state.Settings;
        ProviderBar.Children.Clear();
        UpdateBarVisibility();
        foreach (var (pid, pname) in IconCatalog.Providers)
        {
            var on = !st.HiddenProviders.Contains(pid);
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            if (st.ShowIcons) content.Children.Add(new ProviderIcon { IconId = IconCatalog.Resolve(pid, st.Icons), Accent = on ? Theme.Accent(pid) : Theme.Dim, Width = 15, Height = 15, Margin = new Thickness(0, 0, 6, 0) });
            content.Children.Add(new TextBlock { Text = pname, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource(on ? "TextBrush" : "DimBrush") });
            var b = new Button
            {
                Content = content, Style = (Style)FindResource("NeuButton"), Padding = new Thickness(9, 4, 11, 4), Margin = new Thickness(3, 2, 3, 2), Opacity = on ? 1 : 0.55,
                ToolTip = on ? Loc.T("Ocultar ", "Hide ") + pname : Loc.T("Mostrar ", "Show ") + pname,
            };
            var id = pid;
            b.Click += (_, _) =>
            {
                if (!st.HiddenProviders.Remove(id)) st.HiddenProviders.Add(id);
                st.Save(); state.Rebuild();
            };
            ProviderBar.Children.Add(b);
        }
    }

    void UpdateLayoutButton()
    {
        var fit = state.Settings.LayoutMode != "scroll";
        LayoutButton.Content = fit ? "\uE80A" : "\uE8FD";
        LayoutButton.ToolTip = fit
            ? Loc.T("Ver todos: ajusta ao tamanho da janela. Clique para destaques com rolagem", "See all: fits the window. Click for highlights with scrolling")
            : Loc.T("Destaques com rolagem. Clique para ver todos ajustados à janela", "Highlights with scrolling. Click to fit everything in the window");
    }

    void OnLayoutMode(object s, RoutedEventArgs e)
    {
        state.Settings.LayoutMode = state.Settings.LayoutMode == "scroll" ? "fit" : "scroll";
        ApplyView();
    }

    /// <summary>View-only settings: no service restart, just repaint with the new look and layout.</summary>
    void ApplyView()
    {
        state.Settings.Save();
        Theme.Apply(state.Settings);
        ((App)Application.Current).ApplyPerformance();
        fitKey = null;
        state.Rebuild();
        BuildSettings();
        UpdateLayoutButton();
    }
    void OnUpdated()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!IsVisible) { dirty = true; return; }   // nothing is drawn for a hidden window
            Cards.ItemsSource = null;
            Cards.ItemsSource = state.Providers;
            StatusText.Text = state.StatusLine();
            EmptyText.Visibility = state.Providers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyText.Text = state.Settings.HiddenProviders.Count > 0 && state.Service.Current is { Providers.Count: > 0 }
                ? Loc.T("Todos os provedores estão ocultos. Use os botões acima para mostrar.", "All providers are hidden. Use the buttons above to show them.")
                : Loc.T("Nenhum provedor com dados ainda. Faça login nos CLIs (codex, claude, opencode) e toque em atualizar.", "No provider data yet. Sign in to the CLIs (codex, claude, opencode) and refresh.");
            BuildProviderBar();
            UpdateLayoutButton();
            Dispatcher.BeginInvoke(UpdateColumns, System.Windows.Threading.DispatcherPriority.Loaded);
            RefreshButton.IsEnabled = !state.Refreshing;
            RefreshButton.Opacity = state.Refreshing ? 0.5 : 1;
        });
    }

    public void Localize()
    {
        Title = Loc.T("Uso de IA", "AI Usage");
        TitleText.Text = Title;
        RefreshButton.ToolTip = Loc.T("Atualizar agora", "Refresh now");
        PinButton.ToolTip = Loc.T("Manter sobre outras janelas", "Keep on top");
        PhoneButton.ToolTip = Loc.T("Celular", "Phone");
        SettingsButton.ToolTip = Loc.T("Configurações", "Settings");
        PhoneTitle.Text = Loc.T("Parear celular", "Pair phone");
        PhoneHelp.Text = Loc.T("Veja seus limites no Android, em casa ou fora dela. O PC precisa estar ligado com este app aberto. Tudo viaja criptografado de ponta a ponta; o relay só repassa dados ilegíveis.",
            "See your limits on Android, at home or away. Your PC must be on with this app running. Everything is end-to-end encrypted; the relay only forwards unreadable data.");
        PairButton.Content = Loc.T("Gerar código de pareamento", "Generate pairing code");
        CopyCodeButton.Content = Loc.T("Copiar código", "Copy code");
        CopyLinkButton.Content = Loc.T("Copiar link", "Copy link");
        PairHint.Text = Loc.T("No app do celular: Escanear QR, ou cole o código. O código expira em 15 minutos se não for usado.", "In the phone app: Scan QR, or paste the code. Unused codes expire after 15 minutes.");
        DevicesTitle.Text = Loc.T("Celulares pareados", "Paired phones");
        NoDevices.Text = Loc.T("Nenhum celular pareado.", "No paired phones.");
        RefreshDevices();
        BuildSettings();
        UpdateLayoutButton();
    }

    // ---- navigation ----------------------------------------------------------------------------------------

    void Show(string m)
    {
        mode = mode == m ? "dashboard" : m;
        DashboardPanel.Visibility = mode == "dashboard" ? Visibility.Visible : Visibility.Collapsed;
        PhonePanel.Visibility = mode == "phone" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = mode == "settings" ? Visibility.Visible : Visibility.Collapsed;
        ProviderBar.Visibility = state.Settings.ShowProviderBar && mode == "dashboard" ? Visibility.Visible : Visibility.Collapsed;
        PhoneButton.Foreground = mode == "phone" ? new SolidColorBrush(Theme.Highlight) : (Brush)FindResource("TextBrush");
        SettingsButton.Foreground = mode == "settings" ? new SolidColorBrush(Theme.Highlight) : (Brush)FindResource("TextBrush");
    }

    void OnRefresh(object s, RoutedEventArgs e) { _ = state.Service.RefreshAsync(); OnUpdated(); }
    void OnPin(object s, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        PinButton.Foreground = Topmost ? new SolidColorBrush(Theme.Highlight) : (Brush)FindResource("TextBrush");
    }
    void OnPhone(object s, RoutedEventArgs e) => Show("phone");
    void OnSettings(object s, RoutedEventArgs e) => Show("settings");

    // ---- pairing -------------------------------------------------------------------------------------------

    void OnPairNew(object s, RoutedEventArgs e)
    {
        pairing = state.Devices.Add("Android");
        var secret = Pairing.FromCode(pairing.Code)!;
        var link = Pairing.BuildLink(secret, state.Settings.RelayUrl, Environment.MachineName);
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(link, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(8);
        var bmp = new BitmapImage();
        bmp.BeginInit(); bmp.StreamSource = new MemoryStream(png); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.EndInit(); bmp.Freeze();
        QrImage.Source = bmp;
        CodeText.Text = Pairing.Group(pairing.Code);
        PairBox.Tag = link;
        PairBox.Visibility = Visibility.Visible;
    }

    void OnCopyCode(object s, RoutedEventArgs e) { if (pairing is not null) Clipboard.SetText(Pairing.Group(pairing.Code)); }
    void OnCopyLink(object s, RoutedEventArgs e) { if (PairBox.Tag is string l) Clipboard.SetText(l); }

    void RefreshDevices()
    {
        var list = state.Devices.All();
        if (pairing is not null && list.FirstOrDefault(d => d.Id == pairing.Id) is { Confirmed: true }) { PairBox.Visibility = Visibility.Collapsed; pairing = null; }
        NoDevices.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DeviceList.Items.Clear();
        foreach (var d in list)
        {
            bool online; lock (state.Online) online = state.Online.GetValueOrDefault(d.Id);
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4), LastChildFill = true };
            var revoke = new Button { Content = Loc.T("Remover", "Remove"), Style = (Style)FindResource("NeuButton"), Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 5, 10, 5), FontSize = 12 };
            var id = d.Id;
            revoke.Click += (_, _) => state.Devices.Remove(id);
            DockPanel.SetDock(revoke, Dock.Right);
            row.Children.Add(revoke);
            var rename = new Button { Content = Loc.T("Renomear", "Rename"), Style = (Style)FindResource("NeuButton"), Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 5, 10, 5), FontSize = 12 };
            DockPanel.SetDock(rename, Dock.Right);
            row.Children.Add(rename);
            var dot = new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Fill = online ? new SolidColorBrush(Theme.Highlight) : (Brush)FindResource("DimBrush") };
            DockPanel.SetDock(dot, Dock.Left);
            row.Children.Add(dot);
            var text = new StackPanel();
            var nameText = new TextBlock { Text = d.Name, Style = (Style)FindResource("Label"), FontWeight = FontWeights.SemiBold };
            text.Children.Add(nameText);
            rename.Click += (_, _) =>
            {
                var box = new TextBox { Text = d.Name, Style = (Style)FindResource("NeuText"), Padding = new Thickness(8, 3, 8, 3), MaxLength = 40 };
                var done = false;
                void Commit() { if (done) return; done = true; state.Devices.Rename(id, box.Text); RefreshDevices(); }
                box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); else if (e.Key == Key.Escape) { done = true; RefreshDevices(); } };
                box.LostFocus += (_, _) => Commit();
                var i = text.Children.IndexOf(nameText); text.Children.RemoveAt(i); text.Children.Insert(i, box);
                box.Focus(); box.SelectAll();
            };
            text.Children.Add(new TextBlock
            {
                Style = (Style)FindResource("Dim"),
                Text = (d.Confirmed ? Loc.T("Pareado", "Paired") : Loc.T("Aguardando o celular…", "Waiting for the phone…")) + (d.LastSeen is { } ls ? " · " + Loc.T("visto ", "seen ") + ls.ToLocalTime().ToString("dd/MM HH:mm") : ""),
            });
            row.Children.Add(text);
            DeviceList.Items.Add(row);
        }
    }

    // ---- settings ------------------------------------------------------------------------------------------

    void BuildSettings()
    {
        var st = state.Settings;
        SettingsBody.Children.Clear();
        // Every look uses the same iOS-style grouped list as the phone app (sections, switches, segmented controls, checkmarks); only the colours follow the theme
        const bool cu = true;
        var list = new SettingsList(SettingsBody);
        void Title(string t) { if (cu) list.Title(t); else SettingsBody.Children.Add(new TextBlock { Text = t, FontSize = 17, FontWeight = FontWeights.Bold, Style = (Style)FindResource("Label"), Margin = new Thickness(0, 0, 0, 6) }); }
        void Caption(string t) { if (cu) list.Section(t); else SettingsBody.Children.Add(new TextBlock { Text = t, Style = (Style)FindResource("Dim"), Margin = new Thickness(0, 10, 0, 2) }); }
        void Note(TextBlock t) { if (cu) list.Footer(t.Text); else SettingsBody.Children.Add(t); }
        void Put(UIElement e, double pad = 10) { if (cu) list.AddPadded(e, pad); else SettingsBody.Children.Add(e); }
        void Pick(string[] values, string[] labels, string current, Action<string> set, Action after)
        {
            // segmented only when every label fits on one line of a ~380 px row (Portuguese labels are longer): otherwise a list with checkmarks
            if (labels.Sum(l => l.Length) <= 40 && labels.All(l => l.Length <= 14))
                list.SegmentedRow(labels, Math.Max(0, Array.IndexOf(values, current)), i => { set(values[i]); after(); });
            else
                for (var i = 0; i < values.Length; i++) { var v = values[i]; list.CheckRow(labels[i], v == current, () => { set(v); after(); }); }
        }
        void Check(string text, bool value, Action<bool> set)
        {
            if (cu) { list.SwitchRow(text, value, v => { set(v); Apply(); }); return; }
            var c = new CheckBox { Content = text, IsChecked = value, Style = (Style)FindResource("NeuCheck") };
            c.Click += (_, _) => { set(c.IsChecked == true); Apply(); };
            SettingsBody.Children.Add(c);
        }
        void Choice(string[] values, string[] labels, string current, Action<string> set)
        {
            if (cu) { Pick(values, labels, current, set, () => { Apply(); BuildSettings(); }); return; }
            var panel = new WrapPanel();
            for (var i = 0; i < values.Length; i++)
            {
                var v = values[i];
                var b = new Button { Content = labels[i], Style = (Style)FindResource("NeuButton"), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 4, 6, 4), FontSize = 12 };
                if (v == current) b.Foreground = new SolidColorBrush(Theme.Highlight);
                b.Click += (_, _) => { set(v); Apply(); BuildSettings(); };
                panel.Children.Add(b);
            }
            SettingsBody.Children.Add(panel);
        }

        Title(Loc.T("Configurações", "Settings"));
        Caption(Loc.T("Atualizar a cada", "Refresh every"));
        Choice(["1", "2", "5", "10", "15", "30"], ["1 min", "2 min", "5 min", "10 min", "15 min", "30 min"], st.IntervalMinutes.ToString(), v => st.IntervalMinutes = int.Parse(v));
        Caption(Loc.T("Idioma", "Language"));
        Choice(["auto", "pt", "en"], [Loc.T("Automático", "Automatic"), "Português", "English"], st.Language, v => st.Language = v);
        Caption(Loc.T("Tema", "Theme"));
        Choice(["neu", "mono", "glass", "cyber", "soft", "cupertino"], ["Neumorphism", "Mono", "Liquid glass", "Cyberpunk", Loc.T("Suave", "Soft"), "Cupertino"], st.Style, v => st.Style = v);
        if (st.Style is not ("cyber" or "soft")) Caption(Loc.T("Modo", "Mode"));
        if (st.Style is not ("cyber" or "soft")) Choice(["auto", "light", "dark"], [Loc.T("Automático", "Automatic"), Loc.T("Claro", "Light"), Loc.T("Escuro", "Dark")], st.Theme, v => st.Theme = v);

        // ---- visualization: layout, providers, indicators ----
        void ViewChoice(string[] values, string[] labels, string current, Action<string> set)
        {
            if (cu) { Pick(values, labels, current, set, ApplyView); return; }
            var panel = new WrapPanel();
            for (var i = 0; i < values.Length; i++)
            {
                var v = values[i];
                var b = new Button { Content = labels[i], Style = (Style)FindResource("NeuButton"), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 4, 6, 4), FontSize = 12 };
                if (v == current) b.Foreground = new SolidColorBrush(Theme.Highlight);
                b.Click += (_, _) => { set(v); ApplyView(); };
                panel.Children.Add(b);
            }
            SettingsBody.Children.Add(panel);
        }
        void ViewCheck(string text, bool value, Action<bool> set)
        {
            if (cu) { list.SwitchRow(text, value, v => { set(v); ApplyView(); }); return; }
            var c = new CheckBox { Content = text, IsChecked = value, Style = (Style)FindResource("NeuCheck") };
            c.Click += (_, _) => { set(c.IsChecked == true); ApplyView(); };
            SettingsBody.Children.Add(c);
        }
        if (st.Style is "cyber" or "soft")
        {
            var isSoft = st.Style == "soft";
            Caption(isSoft ? Loc.T("Suave: escolha a paleta", "Soft: pick a palette") : Loc.T("Cyberpunk: escolha o submodo", "Cyberpunk: pick a sub-theme"));
            foreach (var dark in new[] { true, false })
            {
                Put(new TextBlock { Text = dark ? Loc.T("Escuros", "Dark") : Loc.T("Claros", "Light"), Style = (Style)FindResource("Dim"), FontSize = 11 }, 6);
                var wrap = new WrapPanel();
                foreach (var v in CyberVariant.All.Where(x => x.Dark == dark && x.Soft == isSoft))
                {
                    var sel = v.Id == (isSoft ? st.SoftVariant : st.CyberVariant);
                    var content = new StackPanel { Orientation = Orientation.Horizontal };
                    var dot = new Grid { Width = 16, Height = 16, Margin = new Thickness(0, 0, 7, 0) };
                    dot.Children.Add(new System.Windows.Shapes.Ellipse { Fill = new LinearGradientBrush(v.Primary, v.Secondary, 45), Stroke = new SolidColorBrush(v.Dark ? Colors.White : Colors.Black) { Opacity = 0.35 }, StrokeThickness = 1 });
                    content.Children.Add(dot);
                    content.Children.Add(new TextBlock { Text = v.Name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = sel ? new SolidColorBrush(Theme.Highlight) : (Brush)FindResource("TextBrush") });
                    var b = new Button { Content = content, Style = (Style)FindResource("NeuButton"), Padding = new Thickness(10, 6, 12, 6), Margin = new Thickness(0, 4, 6, 4) };
                    var vid = v.Id;
                    b.Click += (_, _) => { if (isSoft) st.SoftVariant = vid; else st.CyberVariant = vid; ApplyView(); };
                    wrap.Children.Add(b);
                }
                Put(wrap, 4);
            }
        }
        Caption(Loc.T("Layout da janela", "Window layout"));
        ViewChoice(["fit", "scroll"], [Loc.T("Ver todos (ajusta ao tamanho)", "See all (fits the window)"), Loc.T("Destaques + rolagem", "Highlights + scrolling")], st.LayoutMode, v => st.LayoutMode = v);
        Note(new TextBlock
        {
            Style = (Style)FindResource("Dim"), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.None, FontSize = 11, Margin = new Thickness(0, 0, 0, 2),
            Text = st.LayoutMode == "scroll"
                ? Loc.T("Os provedores em destaque aparecem primeiro; role para ver o restante.", "Highlighted providers come first; scroll to see the rest.")
                : Loc.T("Tudo continua visível: ao redimensionar a janela, o conteúdo se reorganiza e reduz para caber.", "Everything stays visible: when you resize the window the content reflows and scales to fit."),
        });
        Caption(Loc.T("Provedores exibidos", "Providers shown"));
        foreach (var (pid, pname) in IconCatalog.Providers)
        {
            var id = pid;
            ViewCheck(pname, !st.HiddenProviders.Contains(id), on => { if (on) st.HiddenProviders.Remove(id); else if (!st.HiddenProviders.Contains(id)) st.HiddenProviders.Add(id); });
        }
        ViewCheck(Loc.T("Botões de atalho para mostrar/ocultar na janela", "Quick show/hide buttons in the window"), st.ShowProviderBar, v => st.ShowProviderBar = v);
        if (st.LayoutMode == "scroll")
        {
            Caption(Loc.T("Em destaque (aparecem primeiro)", "Highlighted (shown first)"));
            foreach (var (pid, pname) in IconCatalog.Providers)
            {
                var id = pid;
                ViewCheck("★ " + pname, st.PinnedProviders.Contains(id), on => { if (on) { if (!st.PinnedProviders.Contains(id)) st.PinnedProviders.Add(id); } else st.PinnedProviders.Remove(id); });
            }
        }
        Caption(Loc.T("Indicadores", "Indicators"));
        ViewChoice(["bars", "segments", "rings", "numbers"], [Loc.T("Barras", "Bars"), Loc.T("Segmentos", "Segments"), Loc.T("Anéis", "Rings"), Loc.T("Números", "Numbers")], st.ViewStyle, v => st.ViewStyle = v);
        Caption(Loc.T("Densidade", "Density"));
        ViewChoice(["comfortable", "compact"], [Loc.T("Confortável", "Comfortable"), Loc.T("Compacta", "Compact")], st.Density, v => st.Density = v);
        Caption(Loc.T("Mostrar nos cartões", "Show on cards"));
        ViewCheck(Loc.T("Quando reseta", "Reset time"), st.ShowResetLine, v => st.ShowResetLine = v);
        ViewCheck(Loc.T("Uso por modelo / produto", "Per model / product usage"), st.ShowModels, v => st.ShowModels = v);
        ViewCheck(Loc.T("Hora da última leitura", "Last reading time"), st.ShowUpdated, v => st.ShowUpdated = v);
        Caption(Loc.T("Ícones dos provedores", "Provider icons"));
        void IconCheck()
        {
            if (cu) { list.SwitchRow(Loc.T("Mostrar ícones", "Show icons"), st.ShowIcons, v => { st.ShowIcons = v; Apply(false); BuildSettings(); }); return; }
            var c = new CheckBox { Content = Loc.T("Mostrar ícones", "Show icons"), IsChecked = st.ShowIcons, Style = (Style)FindResource("NeuCheck") };
            c.Click += (_, _) => { st.ShowIcons = c.IsChecked == true; Apply(false); BuildSettings(); };
            SettingsBody.Children.Add(c);
        }
        IconCheck();
        if (st.ShowIcons)
            foreach (var (pid, pname) in IconCatalog.Providers)
            {
                if (cu) list.Add(new TextBlock { Text = pname, FontFamily = (FontFamily)FindResource("UiFont"), FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextBrush"), Margin = new Thickness(16, 10, 0, 0) });
                else SettingsBody.Children.Add(new TextBlock { Text = pname, Style = (Style)FindResource("Dim"), Margin = new Thickness(0, 6, 0, 0) });
                var wrap = new WrapPanel();
                var current = IconCatalog.Resolve(pid, st.Icons);
                foreach (var opt in IconCatalog.Options(pid))
                {
                    var sel = opt.Id == current;
                    var content = new StackPanel { Orientation = Orientation.Horizontal };
                    content.Children.Add(new ProviderIcon { IconId = opt.Id, Accent = sel ? Theme.Accent(pid) : Theme.Dim, Width = 22, Height = 22, Margin = new Thickness(0, 0, 6, 0) });
                    content.Children.Add(new TextBlock { Text = opt.Label, VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Foreground = sel ? new SolidColorBrush(Theme.Accent(pid)) : (Brush)FindResource("TextBrush") });
                    var b = new Button { Content = content, Style = (Style)FindResource("NeuButton"), Padding = new Thickness(10, 6, 12, 6), Margin = new Thickness(0, 4, 6, 4) };
                    var oid = opt.Id; var prov = pid;
                    b.Click += (_, _) => { st.Icons[prov] = oid; Apply(false); BuildSettings(); };
                    wrap.Children.Add(b);
                }
                Put(wrap, 4);
            }
        Caption(Loc.T("Widget flutuante", "Floating widget"));
        Check(Loc.T("Mostrar o widget no canto da tela", "Show the widget on a screen corner"), st.WidgetEnabled, v => st.WidgetEnabled = v);
        Choice(["Right", "Left"], [Loc.T("Borda direita", "Right edge"), Loc.T("Borda esquerda", "Left edge")], st.WidgetEdge, v => st.WidgetEdge = v);
        Check(Loc.T("Sempre sobre outras janelas", "Always on top"), st.WidgetAlwaysOnTop, v => st.WidgetAlwaysOnTop = v);
        Check(Loc.T("Beta: previsão ao passar o mouse sobre o relatório detalhado", "Beta: forecast when hovering the detail report"), st.WidgetForecast, v => st.WidgetForecast = v);
        Check(Loc.T("Teste: todos os usos em anéis no widget retraído", "Test: every window as rings on the retracted widget"), st.WidgetAllRings, v => st.WidgetAllRings = v);
        Caption(Loc.T("Desempenho", "Performance"));
        ViewCheck(Loc.T("Modo economia: sem sombras/efeitos pesados, prioridade baixa", "Power saver: no heavy shadows/effects, low priority"), st.PowerSaver, v => st.PowerSaver = v);
        ViewCheck(Loc.T("Pausar atualização automática quando eu estiver ausente (10 min)", "Pause automatic refresh while I am away (10 min)"), st.PauseWhenIdle, v => st.PauseWhenIdle = v);
        Caption(Loc.T("Geral", "General"));
        Check(Loc.T("Ocultar provedores não instalados", "Hide providers that are not installed"), st.HideMissingProviders, v => st.HideMissingProviders = v);
        Check(Loc.T("Iniciar com o Windows", "Start with Windows"), st.StartWithWindows, v => st.StartWithWindows = v);
        Caption(Loc.T("Relay (conexão com o celular fora de casa)", "Relay (phone connection away from home)"));
        var relay = new TextBox { Text = st.RelayUrl, Style = (Style)FindResource("NeuText") };
        relay.LostFocus += (_, _) =>
        {
            var t = relay.Text.Trim();
            if (t.StartsWith("wss://") || t.StartsWith("ws://")) { st.RelayUrl = t.TrimEnd('/'); st.Save(); }
            else relay.Text = st.RelayUrl;
        };
        Put(relay, 8);
        Note(new TextBlock
        {
            Style = (Style)FindResource("Dim"), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.None, Margin = new Thickness(0, 6, 0, 0),
            Text = Loc.T("Altere só se você hospeda o próprio relay. Reinicie o app para reconectar os celulares.", "Change only if you host your own relay. Restart the app to reconnect phones."),
        });
    }

    void Apply(bool full = true)
    {
        state.Settings.Save();
        if (!full) { state.Rebuild(); return; }
        Autostart.Set(state.Settings.StartWithWindows);
        state.Restart();
        Localize();
        ((App)Application.Current).OnSettingsChanged();
    }
}
