using System.Windows.Media;
using Usage.Core;
using Usage.Core.Link;
using Usage.Core.Providers;

namespace Usage.Windows;

public sealed record WindowVM(string Id, string Label, string ShortLabel, double Percent, string PercentText, Color Accent, string Countdown, string ResetClock, double Elapsed, bool HasElapsed, string ViewStyle = "bars", bool ShowReset = true)
{
    public string BarKind => ViewStyle == "segments" ? "segments" : "bar";
    public Brush AccentBrush { get { var b = new SolidColorBrush(Accent); b.Freeze(); return b; } }
    public string ResetLine => Loc.T("Reseta em ", "Resets in ") + Countdown + (ResetClock.Length > 0 ? " · " + ResetClock : "");
}

public sealed record ModelVM(string Name, string Value);

public sealed record ProviderVM(
    string Id, string Name, string? Plan, Color Accent, string? Notice, bool Stale, string UpdatedText,
    IReadOnlyList<WindowVM> Windows, IReadOnlyList<ModelVM> Models, IReadOnlyList<Detail> Details, IReadOnlyList<HistoryPoint> History,
    string IconId = "generic", bool ShowIcon = true, string ViewStyle = "bars", bool ShowReset = true, bool ShowModels = true, bool ShowUpdated = true, bool Pinned = false)
{
    /// <summary>Rings and big numbers sit side by side inside the card; bars and segments stack.</summary>
    public bool Horizontal => ViewStyle is "rings" or "numbers";
    /// <summary>Densest auto-fit layout: smaller card header.</summary>
    public bool Tight => ViewStyle == "mini";
    public Brush AccentBrush { get { var b = new SolidColorBrush(Accent); b.Freeze(); return b; } }
    public bool HasWindows => Windows.Count > 0;
    /// <summary>Errors without data keep their own row (the card has no windows, so the height is free).</summary>
    public bool HasNotice => Notice is not null && !Stale;
    /// <summary>Stale data is shown inline in the header, so the card height never changes when a reading fails.</summary>
    public bool HasStale => Notice is not null && Stale;
    public string StaleShort => "⚠ " + (Notice is { } n && n.Contains(" · ") ? n[(n.IndexOf(" · ") + 3)..] : Notice ?? "");
    public bool HasModels => ShowModels && Models.Count > 0;
    public bool HasPlan => Plan is not null;
    public string ModelsText => string.Join("  ·  ", Models.Select(m => m.Name + " " + m.Value));
    public string Headline => string.Join("   ", Windows.Select(w => w.ShortLabel + " " + w.PercentText));
}

/// <summary>Single source of truth for all windows: service, devices, relay and the derived view-models.</summary>
public sealed class AppState : IDisposable
{
    public Settings Settings { get; } = Settings.Load();
    public UsageService Service { get; }
    public DeviceStore Devices { get; }
    public RelayHost Relay { get; }
    public IReadOnlyList<ProviderVM> Providers { get; private set; } = [];
    public DateTimeOffset? GeneratedAt { get; private set; }
    public bool Refreshing => Service.IsRefreshing;
    public event Action? Updated;
    /// <summary>Set by the app: false when neither the window nor the widget is on screen (then no view-model is rebuilt).</summary>
    public Func<bool>? UiVisible { get; set; }
    public Dictionary<string, bool> Online { get; } = [];
    readonly System.Windows.Threading.DispatcherTimer tick;

    public AppState()
    {
        Loc.Apply(Settings.Language);
        Service = new UsageService(AppPaths.DefaultProviders(), AppPaths.Data);
        Devices = new DeviceStore(AppPaths.Data);
        Relay = new RelayHost(Service, Devices, () => Settings.RelayUrl);
        Relay.Connection += (id, online) => { lock (Online) Online[id] = online; Raise(); };
        Service.Changed += _ => Raise();
        Service.RefreshingChanged += _ => Raise();
        Devices.Changed += Raise;
        tick = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        tick.Tick += (_, _) => { if (UiVisible?.Invoke() != false) Rebuild(); };
        Service.SkipAutoRefresh = () => Settings.PauseWhenIdle && Power.IdleSeconds() > 600;   // away from the PC for 10 min: do not wake the CLIs
        Rebuild();
    }

    public void Start()
    {
        Relay.Start();
        Service.StartAutoRefresh(TimeSpan.FromMinutes(Math.Clamp(Settings.IntervalMinutes, 1, 60)));
        tick.Start();
    }

    public void Restart()
    {
        Loc.Apply(Settings.Language);
        Theme.Apply(Settings);
        Service.StartAutoRefresh(TimeSpan.FromMinutes(Math.Clamp(Settings.IntervalMinutes, 1, 60)));
        Rebuild();
    }

    void Raise() => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => { if (UiVisible?.Invoke() != false) Rebuild(); });

    /// <summary>Auto-fit level chosen by the window (not saved): 0 full, 1 no secondary lines, 2 one line per window.</summary>
    public int FitLevel { get; set; }

    public void Rebuild(bool raise = true)
    {
        var snap = Service.Current;
        var now = DateTimeOffset.UtcNow;
        GeneratedAt = snap?.GeneratedAt;
        var pinned = Settings.PinnedProviders;
        var list = (snap?.Providers ?? [])
            .Where(p => !(Settings.HideMissingProviders && p.Status == ProviderStatus.Error && p.ErrorCode == "CLI_NOT_FOUND" && !p.HasData))
            .Where(p => !Settings.HiddenProviders.Contains(p.Id));
        // scroll mode: the providers the user pinned come first (stable order otherwise)
        if (Settings.LayoutMode == "scroll") list = list.OrderBy(p => pinned.Contains(p.Id) ? 0 : 1);
        Providers = list.Select(p => Build(p, now)).ToList();
        if (raise) Updated?.Invoke();
    }

    ProviderVM Build(ProviderUsage p, DateTimeOffset now)
    {
        var accent = Theme.Accent(p.Id);
        var windows = p.Windows.Where(w => w.UsedPercent is not null).Select(w =>
        {
            var pct = w.UsedPercent!.Value;
            double elapsed = 0; var has = false;
            if (w.ResetsAt is { } r && w.DurationMinutes is { } d && d > 0)
            {
                elapsed = Math.Clamp(1 - (r - now).TotalMinutes / d, 0, 1); has = true;
            }
            return new WindowVM(w.Id, Loc.WindowName(w.Id, w.Label), Loc.WindowName(w.Id, w.Label, true), Math.Min(pct, 100), Loc.Percent(pct),
                Theme.Level(pct, accent), Loc.Countdown(w.ResetsAt, now), Loc.ResetAt(w.ResetsAt, now), elapsed, has, FitLevel >= 2 ? "mini" : Settings.ViewStyle, Settings.ShowResetLine && FitLevel < 2);
        }).ToList();

        string? notice = p.Status switch
        {
            ProviderStatus.Error when !p.HasData => Loc.Error(p.ErrorCode),
            ProviderStatus.Stale => Loc.T("Dados antigos", "Stale data") + (p.FetchedAt is { } f ? " · " + Loc.Age(f, now) : ""),
            _ => null,
        };
        var models = p.Models.Select(m => new ModelVM(m.Name, m.SpentUsd is { } usd ? $"${usd:0.00}" : Loc.Percent(m.UsedPercent))).ToList();
        var first = p.Windows.FirstOrDefault(w => w.UsedPercent is not null);
        return new ProviderVM(p.Id, p.Name, p.Plan, accent, notice, p.Status == ProviderStatus.Stale,
            p.FetchedAt is { } t ? Loc.T("Lido ", "Read ") + t.ToLocalTime().ToString("HH:mm") + " · " + Loc.Age(t, now) : "",
            windows, models, p.Details.Select(d => new Detail(d.Label == "credits" ? Loc.T("Créditos", "Credits") : d.Label == "extra_usage" ? Loc.T("Uso extra", "Extra usage") : d.Label, d.Value)).ToList(),
            first is null ? [] : Service.History(p.Id, first.Id), IconCatalog.Resolve(p.Id, Settings.Icons), Settings.ShowIcons,
            (FitLevel >= 2 ? "mini" : Settings.ViewStyle), Settings.ShowResetLine && FitLevel < 2, Settings.ShowModels && FitLevel == 0, Settings.ShowUpdated && FitLevel == 0, Settings.LayoutMode == "scroll" && Settings.PinnedProviders.Contains(p.Id));
    }

    public string StatusLine()
    {
        if (Service.IsRefreshing) return Loc.T("Atualizando…", "Updating…");
        if (GeneratedAt is not { } g) return Loc.T("Aguardando a primeira leitura…", "Waiting for the first reading…");
        return Loc.T("Atualizado ", "Updated ") + g.ToLocalTime().ToString("HH:mm") + " · " + Loc.Age(g, DateTimeOffset.UtcNow);
    }

    public void Dispose() { tick.Stop(); Relay.Dispose(); Service.Dispose(); }
}
