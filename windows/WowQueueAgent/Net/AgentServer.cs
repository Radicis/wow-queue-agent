using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Makaretu.Dns;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WowQueueAgent.Analysis;

namespace WowQueueAgent.Net;

public sealed class AgentServer : IAsyncDisposable
{
    const int MaxClients = 4;

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    readonly AgentConfig _cfg;
    readonly StateTracker _tracker;
    readonly List<WebSocket> _clients = new();
    readonly SemaphoreSlim _clientSlots = new(MaxClients, MaxClients);
    WebApplication? _app;
    ServiceDiscovery? _mdns;

    public AgentServer(AgentConfig cfg, StateTracker tracker)
    {
        _cfg = cfg;
        _tracker = tracker;
    }

    public int ClientCount { get { lock (_clients) return _clients.Count; } }

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://0.0.0.0:{_cfg.Port}");
        _app = builder.Build();
        _app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });
        _app.Map("/ws", HandleAsync);
        await _app.StartAsync();

        try
        {
            _mdns = new ServiceDiscovery();
            _mdns.Advertise(new ServiceProfile(Environment.MachineName, "_wowqueue._tcp", (ushort)_cfg.Port));
        }
        catch (Exception ex) { Log.Write("mDNS unavailable: " + ex.Message); }
    }

    async Task HandleAsync(HttpContext ctx)
    {
        if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
        if (!_clientSlots.Wait(0))
        {
            ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        WebSocket? ws = null;
        try
        {
            ws = await ctx.WebSockets.AcceptWebSocketAsync();
            lock (_clients) _clients.Add(ws);

            await SendAsync(ws, new
            {
                type = "hello", v = 1, agent = Environment.MachineName, bootId = _tracker.BootId,
            });
            foreach (var e in _tracker.RecentEvents(TimeSpan.FromMinutes(10))) await SendAsync(ws, EventMessage(e));
            await SendAsync(ws, StateMessage());

            var buf = new byte[1];
            while (ws.State == WebSocketState.Open)
            {
                var r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ctx.RequestAborted);
                if (r.MessageType == WebSocketMessageType.Close)
                {
                    if (ws.State == WebSocketState.CloseReceived)
                        await ws.CloseOutputAsync(
                            r.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                            r.CloseStatusDescription,
                            ctx.RequestAborted);
                    break;
                }

                await ws.CloseOutputAsync(
                    WebSocketCloseStatus.PolicyViolation,
                    "Agent is read-only.",
                    ctx.RequestAborted);
                break;
            }
        }
        catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested) { }
        catch (WebSocketException ex) { Log.Write("WebSocket client disconnected: " + ex.Message); }
        finally
        {
            if (ws != null)
            {
                lock (_clients) _clients.Remove(ws);
                ws.Dispose();
            }
            _clientSlots.Release();
        }
    }

    object StateMessage()
    {
        var s = _tracker.Snapshot;
        return new
        {
            type = "state", v = 1,
            state = s.State.ToWire(),
            position = s.Position,
            etaSeconds = s.EtaSeconds,
            etaUpdatedAt = s.EtaUpdatedAt,
            soon = s.Soon,
            since = s.Since,
            observedAt = s.ObservedAt,
        };
    }

    static object EventMessage(AgentEvent e) =>
        new { type = "event", v = 1, id = e.Id, kind = e.Kind, message = e.Message, at = e.At };

    static Task SendAsync(WebSocket ws, object message, CancellationToken ct = default) =>
        ws.SendAsync(JsonSerializer.SerializeToUtf8Bytes(message, Json), WebSocketMessageType.Text, true, ct);

    public Task BroadcastStateAsync() => BroadcastAsync(StateMessage());

    public Task BroadcastEventAsync(AgentEvent e) => BroadcastAsync(EventMessage(e));

    async Task BroadcastAsync(object message)
    {
        WebSocket[] snapshot;
        lock (_clients) snapshot = _clients.ToArray();
        foreach (var ws in snapshot)
        {
            try
            {
                if (ws.State == WebSocketState.Open)
                    await SendAsync(ws, message).WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch { }
        }
    }

    public int Port => _cfg.Port;

    public string[] GetHosts() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up
                    && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
        .SelectMany(n => n.GetIPProperties().UnicastAddresses)
        .Select(a => a.Address)
        .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !a.ToString().StartsWith("169.254"))
        .Select(a => a.ToString())
        .OrderBy(a => a.StartsWith("192.168") ? 0 : a.StartsWith("10.") ? 1 : 2)
        .ToArray();

    public string PairingPayload() =>
        JsonSerializer.Serialize(new { v = 1, hosts = GetHosts(), port = _cfg.Port, name = Environment.MachineName }, Json);

    public async ValueTask DisposeAsync()
    {
        _mdns?.Dispose();
        if (_app != null) await _app.StopAsync();
        _clientSlots.Dispose();
    }
}
