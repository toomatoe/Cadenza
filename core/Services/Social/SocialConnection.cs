using System.Net.WebSockets;
using System.Text.Json;
using Cadenza.Core.Interfaces;
using Cadenza.Core.Models.Social;

namespace Cadenza.Core.Services.Social;

/// <summary>
/// The live event socket. Reconnects with backoff (2 s doubling to 60 s) and raises
/// <see cref="EventReceived"/> on the creating thread's synchronization context. Idle cost is one open socket.
/// </summary>
public sealed class SocialConnection : ISocialConnection
{
    private const int BufferBytes = 16 * 1024;
    private static readonly TimeSpan _firstRetry = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan _maxRetry = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan _keepAlive = TimeSpan.FromSeconds(30);

    private readonly Uri _endpoint;
    private readonly string _token;
    private readonly CancellationTokenSource _stop = new();
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private Task? _loop;

    public SocialConnection(Uri server, string token)
    {
        ArgumentNullException.ThrowIfNull(server);
        _endpoint = new UriBuilder(new Uri(server, "api/live")) { Scheme = server.Scheme == Uri.UriSchemeHttps ? "wss" : "ws" }.Uri;
        _token = token;
    }

    public event EventHandler<SocialEvent>? EventReceived;

    /// <summary>Raised with true when connected and false when the connection drops.</summary>
    public event EventHandler<bool>? ConnectionChanged;

    public void Start() => _loop ??= Task.Run(() => RunAsync(_stop.Token));

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_loop is not null)
        {
            try
            {
                await _loop;
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping.
            }
        }

        _stop.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var retry = _firstRetry;
        while (!cancellationToken.IsCancellationRequested)
        {
            using (var socket = new ClientWebSocket())
            {
                socket.Options.SetRequestHeader("Authorization", "Bearer " + _token);
                socket.Options.KeepAliveInterval = _keepAlive;
                try
                {
                    await socket.ConnectAsync(_endpoint, cancellationToken);
                    retry = _firstRetry;
                    Post(() => ConnectionChanged?.Invoke(this, true));
                    await ReceiveAsync(socket, cancellationToken);
                }
                catch (Exception error) when (error is WebSocketException or HttpRequestException or JsonException)
                {
                    // Server offline or the connection dropped; try again below.
                }
            }

            if (cancellationToken.IsCancellationRequested)
                break;

            Post(() => ConnectionChanged?.Invoke(this, false));
            await Task.Delay(retry, cancellationToken);
            retry = TimeSpan.FromTicks(Math.Min(retry.Ticks * 2, _maxRetry.Ticks));
        }
    }

    private async Task ReceiveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[BufferBytes];
        using var message = new MemoryStream();
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                return;

            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage)
                continue;

            var socialEvent = JsonSerializer.Deserialize<SocialEvent>(message.GetBuffer().AsSpan(0, (int)message.Length), SocialRules.Json);
            message.SetLength(0);
            if (socialEvent is not null)
                Post(() => EventReceived?.Invoke(this, socialEvent));
        }
    }

    private void Post(Action action)
    {
        if (_context is null)
            action();
        else
            _context.Post(_ => action(), null);
    }
}
