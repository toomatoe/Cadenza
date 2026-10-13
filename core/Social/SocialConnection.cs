using System.Net.WebSockets;
using System.Text.Json;

namespace Cadenza.Core.Social;

/// <summary>
/// The live event socket. Reconnects with backoff (2 s doubling to 60 s) and raises
/// <see cref="EventReceived"/> on the creating thread's synchronization context. Idle cost is one open socket.
/// </summary>
public sealed class SocialConnection : IAsyncDisposable
{
    private const int BufferBytes = 16 * 1024;
    private static readonly TimeSpan s_firstRetry = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan s_maxRetry = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan s_keepAlive = TimeSpan.FromSeconds(30);

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

    private async Task RunAsync(CancellationToken ct)
    {
        var retry = s_firstRetry;
        while (!ct.IsCancellationRequested)
        {
            using (var socket = new ClientWebSocket())
            {
                socket.Options.SetRequestHeader("Authorization", "Bearer " + _token);
                socket.Options.KeepAliveInterval = s_keepAlive;
                try
                {
                    await socket.ConnectAsync(_endpoint, ct);
                    retry = s_firstRetry;
                    Post(() => ConnectionChanged?.Invoke(this, true));
                    await ReceiveAsync(socket, ct);
                }
                catch (Exception error) when (error is WebSocketException or HttpRequestException or JsonException)
                {
                    // Server offline or the connection dropped; try again below.
                }
            }

            if (ct.IsCancellationRequested)
            {
                break;
            }

            Post(() => ConnectionChanged?.Invoke(this, false));
            await Task.Delay(retry, ct);
            retry = TimeSpan.FromTicks(Math.Min(retry.Ticks * 2, s_maxRetry.Ticks));
        }
    }

    private async Task ReceiveAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[BufferBytes];
        using var message = new MemoryStream();
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return;
            }

            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage)
            {
                continue;
            }

            var socialEvent = JsonSerializer.Deserialize<SocialEvent>(message.GetBuffer().AsSpan(0, (int)message.Length), SocialRules.Json);
            message.SetLength(0);
            if (socialEvent is not null)
            {
                Post(() => EventReceived?.Invoke(this, socialEvent));
            }
        }
    }

    private void Post(Action action)
    {
        if (_context is null)
        {
            action();
        }
        else
        {
            _context.Post(_ => action(), null);
        }
    }
}
