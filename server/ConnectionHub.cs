using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using Cadenza.Core.Social;

namespace Cadenza.Server;

/// <summary>Open WebSockets by user, for pushing <see cref="SocialEvent"/>s. A user may be connected from several devices.</summary>
public sealed class ConnectionHub
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Connection>> _connections = new();

    /// <summary>Holds the socket open until the client closes it or the server stops.</summary>
    public async Task ServeAsync(string userId, WebSocket socket, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(socket);
        var id = Guid.NewGuid();
        using var connection = new Connection(socket);
        _connections.GetOrAdd(userId, _ => new()).TryAdd(id, connection);
        try
        {
            // Clients only listen; reading is how a close (or a dead connection) is noticed.
            var buffer = new byte[256];
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, ct);
                }
            }
        }
        catch (Exception error) when (error is WebSocketException or OperationCanceledException)
        {
            // The client went away or the server is stopping.
        }
        finally
        {
            if (_connections.TryGetValue(userId, out var devices))
            {
                devices.TryRemove(id, out _);
            }
        }
    }

    public bool IsOnline(string userId) => _connections.TryGetValue(userId, out var devices) && !devices.IsEmpty;

    public async Task SendAsync(string userId, SocialEvent socialEvent, CancellationToken ct)
    {
        if (!_connections.TryGetValue(userId, out var devices))
        {
            return;
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(socialEvent, SocialRules.Json);
        foreach (var connection in devices.Values)
        {
            await connection.SendAsync(payload, ct);
        }
    }

    /// <summary>One socket with a send lock: WebSocket allows only one send at a time.</summary>
    private sealed class Connection(WebSocket socket) : IDisposable
    {
        private readonly SemaphoreSlim _sendGate = new(1, 1);

        public async Task SendAsync(byte[] payload, CancellationToken ct)
        {
            await _sendGate.WaitAsync(ct);
            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, ct);
                }
            }
            catch (WebSocketException)
            {
                // A dead socket is removed when its receive loop ends.
            }
            finally
            {
                _sendGate.Release();
            }
        }

        public void Dispose() => _sendGate.Dispose();
    }
}
