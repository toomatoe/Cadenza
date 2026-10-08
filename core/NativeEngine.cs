using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Cadenza.Core;

public enum PlaybackState { Loading = 10, Playing = 11, Paused = 12, Stopped = 13, Ended = 14, Failed = 15 }
public sealed record PlaybackUpdate(PlaybackState State, uint PositionMs, uint TrackStamp, int ErrorCode);

public sealed class NativeEngine : IAsyncDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeEvent
    {
        public ulong RequestId;
        public uint Kind;
        public int Status;
        public uint Value;
        public uint Reserved;
    }
    private static class Native
    {
        [DllImport("cadenza_audio", CallingConvention = CallingConvention.Cdecl)] public static extern uint cadenza_abi_version();
        [DllImport("cadenza_audio", CallingConvention = CallingConvention.Cdecl)] public static extern ulong cadenza_create();
        [DllImport("cadenza_audio", CallingConvention = CallingConvention.Cdecl)] public static extern int cadenza_submit(ulong id, ulong requestId, uint opcode);
        [DllImport("cadenza_audio", CallingConvention = CallingConvention.Cdecl)] public static extern int cadenza_submit_text(ulong id, ulong requestId, uint opcode, [In] byte[] payload, nuint length);
        [DllImport("cadenza_audio", CallingConvention = CallingConvention.Cdecl)] public static extern int cadenza_poll(ulong id, out NativeEvent value);
        [DllImport("cadenza_audio", CallingConvention = CallingConvention.Cdecl)] public static extern int cadenza_destroy(ulong id);
    }
    private readonly object gate = new();
    private readonly ConcurrentDictionary<ulong, TaskCompletionSource<uint>> pending = new();
    private readonly CancellationTokenSource stopping = new();
    private readonly Task pump;
    private ulong handle;
    private long nextRequest;
    private Task? disposal;
    private Exception? terminalFailure;
    public NativeEngine()
    {
        uint abi;
        try { abi = Native.cadenza_abi_version(); }
        catch (DllNotFoundException error)
        {
            var dll = Path.Combine(AppContext.BaseDirectory, "cadenza_audio.dll");
            throw new InvalidOperationException(File.Exists(dll)
                ? "The audio engine could not load a required dependency. Install the Microsoft Visual C++ x64 runtime, then restart Cadenza."
                : "The audio engine is missing. Rebuild with build.ps1 -Publish, then launch artifacts\\windows-x64\\Cadenza.Desktop.exe. Keep all files in that folder together.", error);
        }
        catch (BadImageFormatException error)
        {
            throw new InvalidOperationException("The audio engine has the wrong architecture. Rebuild the Windows x64 application with build.ps1 -Publish.", error);
        }
        if (Marshal.SizeOf<NativeEvent>() != 24 || abi != 2)
            throw new InvalidOperationException("The native engine ABI does not match this application.");
        handle = Native.cadenza_create();
        if (handle == 0) throw new InvalidOperationException("The native worker could not start.");
        pump = Task.Run(PumpAsync);
    }
    private PlaybackUpdate? latest;
    public PlaybackUpdate? LatestPlayback => Volatile.Read(ref latest);
    public event Action<PlaybackUpdate>? PlaybackChanged;
    public Task<uint> AuthenticateAsync(string clientId, string accessToken, CancellationToken ct)
    {
        SpotifyAuth.ValidateClientId(clientId);
        if (string.IsNullOrWhiteSpace(accessToken) || accessToken.IndexOfAny(['\r', '\n', '\0']) >= 0)
            throw new ArgumentException("Invalid playback access token.", nameof(accessToken));
        return SubmitAsync(2, ct, clientId + "\n" + accessToken);
    }
    public Task<uint> LoadTrackAsync(string id, CancellationToken ct)
    {
        if (id.Length != 22 || !id.All(char.IsAsciiLetterOrDigit)) throw new ArgumentException("Invalid Spotify track ID.", nameof(id));
        return SubmitAsync(3, ct, "spotify:track:" + id);
    }
    public Task<uint> ResumeAsync(CancellationToken ct) => SubmitAsync(4, ct);
    public Task<uint> PauseAsync(CancellationToken ct) => SubmitAsync(5, ct);
    public Task<uint> SeekAsync(uint positionMs, CancellationToken ct) => SubmitAsync(6, ct, positionMs.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public Task<uint> SetVolumeAsync(uint percent, CancellationToken ct)
    {
        if (percent > 100) throw new ArgumentOutOfRangeException(nameof(percent));
        return SubmitAsync(7, ct, percent.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
    public Task<uint> StopAsync(CancellationToken ct) => SubmitAsync(8, ct);
    public Task<uint> CheckBridgeAsync(CancellationToken ct) => SubmitAsync(0, ct);
    public Task<uint> RequestPlaybackAsync(CancellationToken ct) => SubmitAsync(1, ct);

    private async Task<uint> SubmitAsync(uint opcode, CancellationToken ct, string? text = null)
    {
        ct.ThrowIfCancellationRequested();
        Task<uint> result;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(handle == 0 || disposal is not null, this);
            if (terminalFailure is not null) throw new InvalidOperationException("Native event channel is closed. Restart the application.", terminalFailure);
            var id = (ulong)Interlocked.Increment(ref nextRequest);
            var source = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending[id] = source;
            int status;
            if (text is null) status = Native.cadenza_submit(handle, id, opcode);
            else
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                try
                {
                    if (bytes.Length is 0 or > 8192) throw new ArgumentException("Native payload exceeds its limit.");
                    status = Native.cadenza_submit_text(handle, id, opcode, bytes, (nuint)bytes.Length);
                }
                catch { pending.TryRemove(id, out _); throw; }
                finally { CryptographicOperations.ZeroMemory(bytes); }
            }
            if (status != 0)
            {
                pending.TryRemove(id, out _);
                throw new InvalidOperationException(status == 3 ? "Native command queue is full. Retry shortly." : "Native command was rejected.");
            }
            result = source.Task;
        }
        // Cancellation stops this wait, not native work. The pump still drains its completion.
        return await result.WaitAsync(ct);
    }
    private async Task PumpAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
            while (await timer.WaitForNextTickAsync(stopping.Token))
            {
                var updates = new List<PlaybackUpdate>();
                lock (gate)
                {
                    if (handle == 0) return;
                    for (var count = 0; count < 32; count++)
                    {
                        var status = Native.cadenza_poll(handle, out var value);
                        if (status == 1) break;
                        if (status != 0) throw new InvalidOperationException("Native event channel failed.");
                        if (value.RequestId == 0 && value.Kind is >= 10 and <= 15)
                        {
                            var update = new PlaybackUpdate((PlaybackState)value.Kind, value.Value, value.Reserved, value.Status);
                            Volatile.Write(ref latest, update);
                            updates.Add(update);
                        }
                        else if (pending.TryRemove(value.RequestId, out var source))
                        {
                            if (value.Status == 0) source.TrySetResult(value.Value);
                            else source.TrySetException(CommandError(value.Status));
                        }
                    }
                }
                foreach (var update in updates) PlaybackChanged?.Invoke(update);
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (Exception error)
        {
            lock (gate)
            {
                terminalFailure = error;
                foreach (var item in pending.Values) item.TrySetException(error);
                pending.Clear();
            }
        }
    }
    private static Exception CommandError(int status) => status switch
    {
        4 => new NotSupportedException("Spotify audio output is unavailable on this platform or in this build."),
        6 => new InvalidOperationException("Playback session disconnected. Play the track again to reconnect."),
        7 => new InvalidOperationException("Spotify rejected the playback session or it timed out. Reconnect Spotify in Settings and check Premium/API access."),
        8 => new InvalidOperationException("Windows audio output could not start. Check your playback device."),
        2 => new ArgumentException("Invalid native playback command."),
        _ => new InvalidOperationException("The native playback command failed.")
    };
    public ValueTask DisposeAsync()
    {
        lock (gate) { disposal ??= DisposeCoreAsync(); return new ValueTask(disposal); }
    }
    private async Task DisposeCoreAsync()
    {
        stopping.Cancel();
        // Do not hold gate while awaiting the pump or joining the native worker.
        await Task.Yield();
        await pump;
        ulong old;
        lock (gate) { old = handle; handle = 0; }
        foreach (var item in pending.Values) item.TrySetCanceled();
        pending.Clear();
        if (old != 0) await Task.Run(() => Native.cadenza_destroy(old));
        stopping.Dispose();
        GC.SuppressFinalize(this);
    }
}
