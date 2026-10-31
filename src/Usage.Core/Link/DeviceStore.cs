using System.Text;

namespace Usage.Core.Link;

public sealed class Device
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";          // pairing secret (base32); local file only
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSeen { get; set; }
    public bool Confirmed { get; set; }              // true once a phone has actually connected
}

/// <summary>Paired phones. Each one has its own secret and therefore its own room, so revoking is just deleting the entry.</summary>
public sealed class DeviceStore
{
    readonly string file;
    readonly object sync = new();
    List<Device> devices;
    public event Action? Changed;

    public DeviceStore(string dir)
    {
        Directory.CreateDirectory(dir);
        file = Path.Combine(dir, "devices.json");
        devices = Load();
        Prune();
    }

    List<Device> Load()
    {
        try { return Json.Deserialize<List<Device>>(File.ReadAllText(file, Encoding.UTF8)) ?? []; }
        catch { return []; }
    }

    void Save()
    {
        try
        {
            var tmp = file + ".tmp";
            File.WriteAllText(tmp, Json.Serialize(devices), new UTF8Encoding(false));
            File.Move(tmp, file, overwrite: true);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch { }
    }

    public IReadOnlyList<Device> All() { lock (sync) return [.. devices]; }

    public Device Add(string name)
    {
        var d = new Device { Name = string.IsNullOrWhiteSpace(name) ? "Android" : name.Trim(), Code = Pairing.ToCode(Pairing.NewSecret()) };
        lock (sync) { devices.Add(d); Save(); }
        Changed?.Invoke();
        return d;
    }

    public void Remove(string id)
    {
        lock (sync) { devices.RemoveAll(d => d.Id == id); Save(); }
        Changed?.Invoke();
    }

    public void MarkSeen(string id)
    {
        lock (sync)
        {
            var d = devices.FirstOrDefault(x => x.Id == id);
            if (d is null) return;
            var first = !d.Confirmed;
            d.Confirmed = true; d.LastSeen = DateTimeOffset.UtcNow;
            if (first) Save();
            else if (Random.Shared.Next(20) == 0) Save();
        }
        Changed?.Invoke();
    }

    /// <summary>Pairings nobody used within 15 minutes are dropped.</summary>
    void Prune()
    {
        lock (sync)
            if (devices.RemoveAll(d => !d.Confirmed && DateTimeOffset.UtcNow - d.CreatedAt > TimeSpan.FromMinutes(15)) > 0) Save();
    }
}
