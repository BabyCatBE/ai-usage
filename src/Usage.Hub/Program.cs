using Usage.Core;
using Usage.Core.Link;

// Headless hub: collects usage and serves paired phones through the relay. Runs on a PC or a VPS (24h).
var command = args.FirstOrDefault() ?? "serve";
var settings = Settings.Load();
var devices = new DeviceStore(AppPaths.Data);

switch (command)
{
    case "pair":
    {
        var d = devices.Add(args.Length > 1 ? string.Join(' ', args.Skip(1)) : "Android");
        var secret = Pairing.FromCode(d.Code)!;
        Console.WriteLine("Pairing code (paste in the phone app):");
        Console.WriteLine("  " + Pairing.Group(d.Code));
        Console.WriteLine("Link:");
        Console.WriteLine("  " + Pairing.BuildLink(secret, settings.RelayUrl, Environment.MachineName));
        Console.WriteLine("Run `usage-hub serve` to keep the link online. Unused pairings expire after 15 minutes.");
        return 0;
    }
    case "devices":
        foreach (var d in devices.All()) Console.WriteLine($"{d.Id}  {d.Name}  {(d.Confirmed ? "paired" : "waiting")}  last seen: {d.LastSeen?.ToString("u") ?? "-"}");
        return 0;
    case "revoke" when args.Length > 1:
        devices.Remove(args[1]);
        Console.WriteLine("Removed " + args[1]);
        return 0;
    case "once":
    {
        using var s = new UsageService(AppPaths.DefaultProviders(), AppPaths.Data);
        Console.WriteLine(Json.Serialize(await s.RefreshAsync()));
        return 0;
    }
    case "serve":
    {
        using var service = new UsageService(AppPaths.DefaultProviders(), AppPaths.Data);
        using var host = new RelayHost(service, devices, () => settings.RelayUrl);
        service.Changed += s => Console.WriteLine($"{s.GeneratedAt:u} refreshed: " + string.Join(", ", s.Providers.Select(p => $"{p.Id}={p.Status}")));
        host.Start();
        service.StartAutoRefresh(TimeSpan.FromMinutes(Math.Clamp(settings.IntervalMinutes, 1, 60)));
        Console.WriteLine($"usage-hub running; relay {settings.RelayUrl}; {devices.All().Count} device(s). Ctrl+C to stop.");
        var stop = new TaskCompletionSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.TrySetResult(); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.TrySetResult();
        await stop.Task;
        return 0;
    }
    default:
        Console.WriteLine("usage-hub [serve|pair [name]|devices|revoke <id>|once]");
        return 1;
}
