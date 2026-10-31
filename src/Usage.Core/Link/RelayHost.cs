using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace Usage.Core.Link;

/// <summary>
/// PC side of the phone link. Keeps one outbound WebSocket per paired phone to the relay
/// (outbound only: works behind NAT/CGNAT without opening ports) and answers requests with encrypted snapshots.
/// </summary>
public sealed class RelayHost : IDisposable
{
    static readonly int[] Backoff = [1, 2, 5, 10, 30, 60];
    static readonly TimeSpan MinPhoneRefresh = TimeSpan.FromSeconds(20);

    readonly UsageService usage;
    readonly DeviceStore devices;
    readonly Func<string> relayUrl;
    readonly Dictionary<string, (CancellationTokenSource Cts, ClientWebSocket? Socket)> links = [];
    readonly object sync = new();
    DateTimeOffset lastPhoneRefresh = DateTimeOffset.MinValue;

    public event Action<string, bool>? Connection; // deviceId, online

    public RelayHost(UsageService usage, DeviceStore devices, Func<string> relayUrl)
    {
        this.usage = usage; this.devices = devices; this.relayUrl = relayUrl;
        usage.Changed += OnSnapshot;
        devices.Changed += Sync;
    }

    public void Start() => Sync();

    /// <summary>Starts links for new devices and stops links of removed ones.</summary>
    void Sync()
    {
        lock (sync)
        {
            var wanted = devices.All().ToDictionary(d => d.Id);
            foreach (var id in links.Keys.Where(k => !wanted.ContainsKey(k)).ToList())
            {
                links[id].Cts.Cancel(); links.Remove(id);
            }
            foreach (var d in wanted.Values.Where(d => !links.ContainsKey(d.Id)))
            {
                var cts = new CancellationTokenSource();
                links[d.Id] = (cts, null);
                _ = Task.Run(() => RunLink(d, cts.Token));
            }
        }
    }

    async Task RunLink(Device device, CancellationToken ct)
    {
        var secret = Pairing.FromCode(device.Code);
        if (secret is null) return;
        var keys = Pairing.Derive(secret);
        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            using var ws = new ClientWebSocket();
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(25);
            try
            {
                var url = relayUrl().TrimEnd('/') + "/r/" + keys.RoomId + "?role=host";
                await ws.ConnectAsync(new Uri(url), ct);
                lock (sync) if (links.TryGetValue(device.Id, out var l)) links[device.Id] = (l.Cts, ws);
                attempt = 0;
                Connection?.Invoke(device.Id, true);
                await Receive(device, keys, ws, ct);
            }
            catch (OperationCanceledException) { break; }
            catch { /* reconnect below */ }
            finally
            {
                lock (sync) if (links.TryGetValue(device.Id, out var l)) links[device.Id] = (l.Cts, null);
                Connection?.Invoke(device.Id, false);
            }
            try { await Task.Delay(TimeSpan.FromSeconds(Backoff[Math.Min(attempt++, Backoff.Length - 1)]), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    async Task Receive(Device device, Pairing.Keys keys, ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var sb = new StringBuilder();
            WebSocketReceiveResult r;
            do
            {
                r = await ws.ReceiveAsync(buffer, ct);
                if (r.MessageType == WebSocketMessageType.Close) return;
                if (r.MessageType == WebSocketMessageType.Text) sb.Append(Encoding.UTF8.GetString(buffer, 0, r.Count));
                if (sb.Length > 64 * 1024) return;
            } while (!r.EndOfMessage);

            JsonNode? node; try { node = JsonNode.Parse(sb.ToString()); } catch { continue; }
            if (node is not JsonObject obj) continue;
            if (obj["relay"] is { } ctl)
            {
                if (ctl.GetValue<string>() == "client_joined") await Send(device, ws, keys, Snapshot(), ct);
                continue;
            }
            var msg = Pairing.Open(keys, 'c', sb.ToString(), DateTimeOffset.UtcNow);
            if (msg is null) continue;
            devices.MarkSeen(device.Id);
            switch (msg["t"]?.GetValue<string>())
            {
                case "get":
                    await Send(device, ws, keys, Snapshot(), ct);
                    break;
                case "refresh":
                    if (DateTimeOffset.UtcNow - lastPhoneRefresh < MinPhoneRefresh)
                    {
                        await Send(device, ws, keys, Snapshot(), ct);
                        break;
                    }
                    lastPhoneRefresh = DateTimeOffset.UtcNow;
                    _ = usage.RefreshAsync(ct); // OnSnapshot publishes the result
                    break;
            }
        }
    }

    JsonObject Snapshot()
    {
        var s = usage.Current;
        return new JsonObject
        {
            ["t"] = "snapshot",
            ["ts"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["busy"] = usage.IsRefreshing,
            ["d"] = s is null ? null : JsonNode.Parse(Json.Serialize(s)),
        };
    }

    static async Task Send(Device device, ClientWebSocket ws, Pairing.Keys keys, JsonObject message, CancellationToken ct)
    {
        if (ws.State != WebSocketState.Open) return;
        var bytes = Encoding.UTF8.GetBytes(Pairing.Seal(keys, 'h', message));
        try { await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct); } catch { }
    }

    void OnSnapshot(UsageSnapshot snapshot)
    {
        List<(Device, ClientWebSocket)> open = [];
        lock (sync)
            foreach (var d in devices.All())
                if (links.TryGetValue(d.Id, out var l) && l.Socket is { State: WebSocketState.Open } ws) open.Add((d, ws));
        foreach (var (d, ws) in open)
        {
            var secret = Pairing.FromCode(d.Code);
            if (secret is null) continue;
            _ = Send(d, ws, Pairing.Derive(secret), Snapshot(), CancellationToken.None);
        }
    }

    public void Dispose()
    {
        usage.Changed -= OnSnapshot;
        devices.Changed -= Sync;
        lock (sync) { foreach (var l in links.Values) l.Cts.Cancel(); links.Clear(); }
    }
}
