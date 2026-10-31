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
        state.Updated += OnUpdated;
        state.Devices.Changed += () => Dispatcher.BeginInvoke(RefreshDevices);
        SizeChanged += (_, _) => UpdateColumns();
        Closing += (_, e) => { e.Cancel = true; Hide(); }; // lives in the tray
        Topmost = state.Settings.WidgetAlwaysOnTop && false;
        Localize();
        OnUpdated();
        UpdateColumns();
    }

    void UpdateColumns()
    {
        var avail = Math.Max(240, ActualWidth - 12 - 18 - 16);
        var cols = Math.Clamp((int)(avail / 360), 1, 3);
        CardWidth = Math.Floor(avail / cols);
        var compact = ActualWidth < 420;
        foreach (var b in new[] { RefreshButton, PinButton, PhoneButton, SettingsButton }) { b.Width = b.Height = compact ? 34 : 40; b.Margin = new Thickness(compact ? 1 : 3); }
        TitleText.FontSize = compact ? 18 : 22;
    }

    void OnUpdated()
    {
        Dispatcher.BeginInvoke(() =>
        {
            Cards.ItemsSource = null;
            Cards.ItemsSource = state.Providers;
            StatusText.Text = state.StatusLine();
            EmptyText.Visibility = state.Providers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyText.Text = Loc.T("Nenhum provedor com dados ainda. Faça login nos CLIs (codex, claude, opencode) e toque em atualizar.", "No provider data yet. Sign in to the CLIs (codex, claude, opencode) and refresh.");
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
    }

    // ---- navigation ----------------------------------------------------------------------------------------

    void Show(string m)
    {
        mode = mode == m ? "dashboard" : m;
        DashboardPanel.Visibility = mode == "dashboard" ? Visibility.Visible : Visibility.Collapsed;
        PhonePanel.Visibility = mode == "phone" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = mode == "settings" ? Visibility.Visible : Visibility.Collapsed;
        PhoneButton.Foreground = mode == "phone" ? new SolidColorBrush(Theme.Accent("codex")) : (Brush)FindResource("TextBrush");
        SettingsButton.Foreground = mode == "settings" ? new SolidColorBrush(Theme.Accent("codex")) : (Brush)FindResource("TextBrush");
    }

    void OnRefresh(object s, RoutedEventArgs e) { _ = state.Service.RefreshAsync(); OnUpdated(); }
    void OnPin(object s, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        PinButton.Foreground = Topmost ? new SolidColorBrush(Theme.Accent("codex")) : (Brush)FindResource("TextBrush");
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
            var dot = new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Fill = online ? new SolidColorBrush(Theme.Accent("codex")) : (Brush)FindResource("DimBrush") };
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
        void Title(string t) => SettingsBody.Children.Add(new TextBlock { Text = t, FontSize = 17, FontWeight = FontWeights.Bold, Style = (Style)FindResource("Label"), Margin = new Thickness(0, 0, 0, 6) });
        void Caption(string t) => SettingsBody.Children.Add(new TextBlock { Text = t, Style = (Style)FindResource("Dim"), Margin = new Thickness(0, 10, 0, 2) });
        void Check(string text, bool value, Action<bool> set)
        {
            var c = new CheckBox { Content = text, IsChecked = value, Style = (Style)FindResource("NeuCheck") };
            c.Click += (_, _) => { set(c.IsChecked == true); Apply(); };
            SettingsBody.Children.Add(c);
        }
        void Choice(string[] values, string[] labels, string current, Action<string> set)
        {
            var panel = new WrapPanel();
            for (var i = 0; i < values.Length; i++)
            {
                var v = values[i];
                var b = new Button { Content = labels[i], Style = (Style)FindResource("NeuButton"), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 4, 6, 4), FontSize = 12 };
                if (v == current) b.Foreground = new SolidColorBrush(Theme.Accent("codex"));
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
        Choice(["neu", "glass"], ["Neumorphism", "Liquid glass"], st.Style, v => st.Style = v);
        Caption(Loc.T("Modo", "Mode"));
        Choice(["auto", "light", "dark"], [Loc.T("Automático", "Automatic"), Loc.T("Claro", "Light"), Loc.T("Escuro", "Dark")], st.Theme, v => st.Theme = v);
        Caption(Loc.T("Ícones dos provedores", "Provider icons"));
        void IconCheck()
        {
            var c = new CheckBox { Content = Loc.T("Mostrar ícones", "Show icons"), IsChecked = st.ShowIcons, Style = (Style)FindResource("NeuCheck") };
            c.Click += (_, _) => { st.ShowIcons = c.IsChecked == true; Apply(false); BuildSettings(); };
            SettingsBody.Children.Add(c);
        }
        IconCheck();
        if (st.ShowIcons)
            foreach (var (pid, pname) in IconCatalog.Providers)
            {
                SettingsBody.Children.Add(new TextBlock { Text = pname, Style = (Style)FindResource("Dim"), Margin = new Thickness(0, 6, 0, 0) });
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
                SettingsBody.Children.Add(wrap);
            }
        Caption(Loc.T("Widget flutuante", "Floating widget"));
        Check(Loc.T("Mostrar o widget no canto da tela", "Show the widget on a screen corner"), st.WidgetEnabled, v => st.WidgetEnabled = v);
        Choice(["Right", "Left"], [Loc.T("Borda direita", "Right edge"), Loc.T("Borda esquerda", "Left edge")], st.WidgetEdge, v => st.WidgetEdge = v);
        Check(Loc.T("Sempre sobre outras janelas", "Always on top"), st.WidgetAlwaysOnTop, v => st.WidgetAlwaysOnTop = v);
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
        SettingsBody.Children.Add(relay);
        SettingsBody.Children.Add(new TextBlock
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
