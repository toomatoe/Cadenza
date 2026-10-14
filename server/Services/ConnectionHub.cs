using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using Cadenza.Core.Social;
using Cadenza.Server.Interfaces;

namespace Cadenza.Server.Services;

/// <summary>Open WebSockets by user, for pushing <see cref="SocialEvent"/>s. A user may be connected from several devices.</summary>
public sealed class ConnectionHub : IConnectionHub
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Connection>> _connections = new();

    /// <summary>Holds the socket open until the client closes it or the server stops.</summary>
    public async Task ServeAsync(string userId, WebSocket socket, CancellationToken cancellationToken)
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
                var result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);
            }
        }
        catch (Exception error) when (error is WebSocketException or OperationCanceledException)
        {
            // The client went away or the server is stopping.
        }
        finally
        {
            if (_connections.TryGetValue(userId, out var devices))
                devices.TryRemove(id, out _);
        }
    }

    public bool IsOnline(string userId) => _connections.TryGetValue(userId, out var devices) && !devices.IsEmpty;

    public async Task SendAsync(string userId, SocialEvent socialEvent, CancellationToken cancellationToken)
    {
        if (!_connections.TryGetValue(userId, out var devices))
            return;

        var payload = JsonSerializer.SerializeToUtf8Bytes(socialEvent, SocialRules.Json);
        foreach (var connection in devices.Values)
        {
            await connection.SendAsync(payload, cancellationToken);
        }
    }

    /// <summary>One socket with a send lock: WebSocket allows only one send at a time.</summary>
    private sealed class Connection : IDisposable
    {
        private readonly SemaphoreSlim _sendGate = new(1, 1);
        private readonly WebSocket _socket;

        public Connection(WebSocket socket)
        {
            _socket = socket;
        }

        public async Task SendAsync(byte[] payload, CancellationToken cancellationToken)
        {
            await _sendGate.WaitAsync(cancellationToken);
            try
            {
                if (_socket.State == WebSocketState.Open)
                    await _socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
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
