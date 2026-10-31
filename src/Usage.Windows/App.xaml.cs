using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Usage.Core;
using Forms = System.Windows.Forms;

namespace Usage.Windows;

public static class Autostart
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static void Set(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key, true);
            if (k is null) return;
            if (on) k.SetValue("UsageMonitor", $"\"{Environment.ProcessPath}\" --background");
            else k.DeleteValue("UsageMonitor", false);
        }
        catch { }
    }
}

public partial class App : Application
{
    Mutex? single;
    EventWaitHandle? showSignal;
    AppState? state;
    MainWindow? main;
    FloatingWidget? widget;
    Forms.NotifyIcon? tray;
    bool quitting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        single = new Mutex(true, @"Local\UsageMonitor.Single", out var first);
        showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\UsageMonitor.Show");
        if (!first) { showSignal.Set(); Shutdown(); return; }

        var settings = Settings.Load();
        Theme.Apply(settings.Theme);
        state = new AppState();
        Theme.Apply(state.Settings.Theme);
        main = new MainWindow(state) { Icon = IconSource() };
        if (state.Settings.StartWithWindows) Autostart.Set(true);

        BuildTray();
        OnSettingsChanged();
        state.Start();

        // second launches ask the first one to show its window
        new Thread(() => { while (showSignal.WaitOne()) Dispatcher.BeginInvoke(ShowMain); }) { IsBackground = true }.Start();
        SystemEvents.UserPreferenceChanged += (_, _) => Dispatcher.BeginInvoke(() => { if (state.Settings.Theme == "auto") { Theme.Apply("auto"); state.Rebuild(); } });
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(() => widget?.ApplySettings());

        if (!e.Args.Contains("--background")) ShowMain();
    }

    public void ShowMain()
    {
        if (main is null) return;
        main.Show();
        if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal;
        main.Activate();
    }

    /// <summary>Applies widget visibility/position and refreshes localized tray text.</summary>
    public void OnSettingsChanged()
    {
        if (state is null) return;
        if (state.Settings.WidgetEnabled)
        {
            if (widget is null) { widget = new FloatingWidget(state); widget.Show(); }
            widget.ApplySettings();
        }
        else if (widget is not null) { widget.Close(); widget = null; }
        if (tray is not null) tray.ContextMenuStrip = TrayMenu();
    }

    Forms.ContextMenuStrip TrayMenu()
    {
        var m = new Forms.ContextMenuStrip();
        m.Items.Add(Loc.T("Abrir", "Open"), null, (_, _) => ShowMain());
        m.Items.Add(Loc.T("Atualizar agora", "Refresh now"), null, (_, _) => _ = state?.Service.RefreshAsync());
        m.Items.Add(new Forms.ToolStripSeparator());
        m.Items.Add(Loc.T("Mostrar/ocultar widget", "Show/hide widget"), null, (_, _) =>
        {
            if (state is null) return;
            state.Settings.WidgetEnabled = !state.Settings.WidgetEnabled; state.Settings.Save(); OnSettingsChanged();
        });
        m.Items.Add(new Forms.ToolStripSeparator());
        m.Items.Add(Loc.T("Sair", "Quit"), null, (_, _) => Quit());
        return m;
    }

    void BuildTray()
    {
        tray = new Forms.NotifyIcon { Icon = MakeIcon(), Visible = true, Text = Loc.T("Uso de IA", "AI Usage"), ContextMenuStrip = TrayMenu() };
        tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) ShowMain(); };
    }

    public void Quit()
    {
        if (quitting) return;
        quitting = true;
        tray?.Dispose();
        widget?.Close();
        state?.Dispose();
        single?.ReleaseMutex();
        Shutdown();
    }

    // ---- icon drawn in code: three concentric gauge arcs --------------------------------------------------

    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

    static Icon MakeIcon()
    {
        using var bmp = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            void Arc(Color c, int inset, float width, float sweep)
            {
                using var pen = new Pen(c, width) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawArc(pen, inset, inset, 64 - inset * 2, 64 - inset * 2, 135, sweep);
            }
            Arc(Color.FromArgb(0x2F, 0xCB, 0xA0), 6, 8, 270);
            Arc(Color.FromArgb(0xE0, 0x87, 0x5A), 17, 8, 270);
            Arc(Color.FromArgb(0x9B, 0x87, 0xF5), 28, 8, 270);
        }
        var h = bmp.GetHicon();
        using var tmp = Icon.FromHandle(h);
        var icon = (Icon)tmp.Clone();
        DestroyIcon(h);
        return icon;
    }

    static BitmapSource IconSource()
    {
        using var icon = MakeIcon();
        return Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
    }
}
