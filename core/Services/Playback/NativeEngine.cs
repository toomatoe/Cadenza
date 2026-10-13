using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Cadenza.Core.Models;
using Cadenza.Core.Services.Spotify;

namespace Cadenza.Core.Services.Playback;

/// <summary>
/// Managed side of the Rust audio engine (ABI 3, see native/README.md). Commands are submitted with a
/// request ID and complete when the native worker reports them; playback events arrive separately
/// through <see cref="PlaybackChanged"/> on a background thread.
/// </summary>
public sealed partial class NativeEngine : IAsyncDisposable
{
    private const uint SupportedAbiVersion = 3;
    private const int NativeEventSize = 24;
    private const int MaxPayloadBytes = 8192;
    private const int MaxEventsPerPoll = 32;
    private static readonly TimeSpan s_pollInterval = TimeSpan.FromMilliseconds(50);

    private readonly Lock _gate = new();
    private readonly ConcurrentDictionary<ulong, TaskCompletionSource<uint>> _pending = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _pump;
    private ulong _handle;
    private long _nextRequest;
    private Task? _disposal;
    private Exception? _terminalFailure;
    private PlaybackUpdate? _latest;

    /// <exception cref="InvalidOperationException">The native library is missing, mismatched or failed to start.</exception>
    public NativeEngine()
    {
        uint abi;
        try
        {
            abi = Native.cadenza_abi_version();
        }
        catch (DllNotFoundException error)
        {
            var dll = Path.Combine(AppContext.BaseDirectory, "cadenza_audio.dll");
            throw new InvalidOperationException(
                File.Exists(dll)
                    ? "The audio engine could not load a required dependency. Install the Microsoft Visual C++ x64 runtime, then restart Cadenza."
                    : "The audio engine is missing. Rebuild with build.ps1 -Publish, then launch artifacts\\windows-x64\\Cadenza.Desktop.exe. Keep all files in that folder together.",
                error);
        }
        catch (BadImageFormatException error)
        {
            throw new InvalidOperationException(
                "The audio engine has the wrong architecture. Rebuild the Windows x64 application with build.ps1 -Publish.",
                error);
        }

        if (Unsafe.SizeOf<NativeEvent>() != NativeEventSize || abi != SupportedAbiVersion)
        {
            throw new InvalidOperationException("The native engine ABI does not match this application.");
        }

        _handle = Native.cadenza_create();
        if (_handle == 0)
        {
            throw new InvalidOperationException("The native worker could not start.");
        }

        _pump = Task.Run(PumpAsync);
    }

    /// <summary>Raised on a background thread for each playback event.</summary>
    public event Action<PlaybackUpdate>? PlaybackChanged;

    private enum Opcode : uint
    {
        Diagnostic = 0,
        LegacyPlaybackCheck = 1,
        Authenticate = 2,
        LoadTrack = 3,
        Resume = 4,
        Pause = 5,
        Seek = 6,
        Volume = 7,
        Stop = 8,
        LoadFile = 9
    }

    private enum Status
    {
        Ok = 0,
        Empty = 1,
        Invalid = 2,
        Full = 3,
        Unsupported = 4,
        Internal = 5,
        Disconnected = 6,
        AuthenticationFailed = 7,
        AudioOutputFailed = 8
    }

    /// <summary>The most recent playback event, if any.</summary>
    public PlaybackUpdate? LatestPlayback => Volatile.Read(ref _latest);

    /// <summary>Signs the native session in with a Spotify access token issued to <paramref name="clientId"/>.</summary>
    public Task<uint> AuthenticateAsync(string clientId, string accessToken, CancellationToken ct)
    {
        SpotifyAuth.ValidateClientId(clientId);
        if (string.IsNullOrWhiteSpace(accessToken) || accessToken.AsSpan().IndexOfAny('\r', '\n', '\0') >= 0)
        {
            throw new ArgumentException("Invalid playback access token.", nameof(accessToken));
        }

        return SubmitAsync(Opcode.Authenticate, ct, clientId + "\n" + accessToken);
    }

    /// <summary>Loads and starts a track.</summary>
    /// <returns>A stamp that identifies this load's playback events.</returns>
    public Task<uint> LoadTrackAsync(string id, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (id.Length != 22 || !id.All(char.IsAsciiLetterOrDigit))
        {
            throw new ArgumentException("Invalid Spotify track ID.", nameof(id));
        }

        return SubmitAsync(Opcode.LoadTrack, ct, "spotify:track:" + id);
    }

    /// <summary>Loads and starts a music file from disk (MP3, FLAC or Ogg Vorbis). Needs no Spotify session.</summary>
    /// <returns>A stamp that identifies this load's playback events.</returns>
    public Task<uint> LoadFileAsync(string path, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!Path.IsPathFullyQualified(path) || path.AsSpan().IndexOfAny('\r', '\n', '\0') >= 0)
        {
            throw new ArgumentException("Invalid music file path.", nameof(path));
        }

        return LoadFileCoreAsync(path, ct);
    }

    // The engine's generic status messages talk about Spotify; say what went wrong with the file instead.
    private async Task<uint> LoadFileCoreAsync(string path, CancellationToken ct)
    {
        try
        {
            return await SubmitAsync(Opcode.LoadFile, ct, path);
        }
        catch (ArgumentException error)
        {
            throw new InvalidOperationException("The song file is missing. Try playing it again.", error);
        }
        catch (NotSupportedException error)
        {
            throw new InvalidOperationException("That song's file isn't an MP3, FLAC or Ogg file Cadenza can play.", error);
        }
    }

    public Task<uint> ResumeAsync(CancellationToken ct) => SubmitAsync(Opcode.Resume, ct);

    public Task<uint> PauseAsync(CancellationToken ct) => SubmitAsync(Opcode.Pause, ct);

    public Task<uint> SeekAsync(uint positionMs, CancellationToken ct) =>
        SubmitAsync(Opcode.Seek, ct, positionMs.ToString(CultureInfo.InvariantCulture));

    public Task<uint> SetVolumeAsync(uint percent, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, 100u);
        return SubmitAsync(Opcode.Volume, ct, percent.ToString(CultureInfo.InvariantCulture));
    }

    public Task<uint> StopAsync(CancellationToken ct) => SubmitAsync(Opcode.Stop, ct);

    /// <summary>Round-trips a diagnostic command.</summary>
    /// <returns>The native ABI version.</returns>
    public Task<uint> CheckBridgeAsync(CancellationToken ct) => SubmitAsync(Opcode.Diagnostic, ct);

    /// <summary>Legacy capability check; always reports playback as unsupported.</summary>
    public Task<uint> RequestPlaybackAsync(CancellationToken ct) => SubmitAsync(Opcode.LegacyPlaybackCheck, ct);

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        lock (_gate)
        {
            _disposal ??= DisposeCoreAsync();
            return new ValueTask(_disposal);
        }
    }

    private static Exception CommandError(Status status) => status switch
    {
        Status.Unsupported => new NotSupportedException("Spotify audio output is unavailable on this platform or in this build."),
        Status.Disconnected => new InvalidOperationException("Playback session disconnected. Play the track again to reconnect."),
        Status.AuthenticationFailed => new InvalidOperationException(
            "Spotify rejected the playback session or it timed out. Reconnect Spotify in Settings and check Premium/API access."),
        Status.AudioOutputFailed => new InvalidOperationException("Windows audio output could not start. Check your playback device."),
        Status.Invalid => new ArgumentException("Invalid native playback command."),
        _ => new InvalidOperationException("The native playback command failed.")
    };

    private async Task<uint> SubmitAsync(Opcode opcode, CancellationToken ct, string? text = null)
    {
        ct.ThrowIfCancellationRequested();
        Task<uint> result;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_handle == 0 || _disposal is not null, this);
            if (_terminalFailure is not null)
            {
                throw new InvalidOperationException("Native event channel is closed. Restart the application.", _terminalFailure);
            }

            var id = (ulong)Interlocked.Increment(ref _nextRequest);
            var source = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = source;
            Status status;
            if (text is null)
            {
                status = (Status)Native.cadenza_submit(_handle, id, (uint)opcode);
            }
            else
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                try
                {
                    if (bytes.Length is 0 or > MaxPayloadBytes)
                    {
                        throw new ArgumentException("Native payload exceeds its limit.", nameof(text));
                    }

                    status = (Status)Native.cadenza_submit_text(_handle, id, (uint)opcode, bytes, (nuint)bytes.Length);
                }
                catch
                {
                    _pending.TryRemove(id, out _);
                    throw;
                }
                finally
                {
                    // The payload may contain an access token.
                    CryptographicOperations.ZeroMemory(bytes);
                }
            }

            if (status != Status.Ok)
            {
                _pending.TryRemove(id, out _);
                throw new InvalidOperationException(status == Status.Full
                    ? "Native command queue is full. Retry shortly."
                    : "Native command was rejected.");
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
            using var timer = new PeriodicTimer(s_pollInterval);
            while (await timer.WaitForNextTickAsync(_stopping.Token))
            {
                var updates = new List<PlaybackUpdate>();
                lock (_gate)
                {
                    if (_handle == 0)
                    {
                        return;
                    }

                    for (var count = 0; count < MaxEventsPerPoll; count++)
                    {
                        var status = (Status)Native.cadenza_poll(_handle, out var value);
                        if (status == Status.Empty)
                        {
                            break;
                        }

                        if (status != Status.Ok)
                        {
                            throw new InvalidOperationException("Native event channel failed.");
                        }

                        // Request ID 0 is telemetry; its kind is a PlaybackState value.
                        if (value.RequestId == 0 && value.Kind is >= (uint)PlaybackState.Loading and <= (uint)PlaybackState.Failed)
                        {
                            var update = new PlaybackUpdate((PlaybackState)value.Kind, value.Value, value.Reserved, value.Status);
                            Volatile.Write(ref _latest, update);
                            updates.Add(update);
                        }
                        else if (_pending.TryRemove(value.RequestId, out var source))
                        {
                            if (value.Status == (int)Status.Ok)
                            {
                                source.TrySetResult(value.Value);
                            }
                            else
                            {
                                source.TrySetException(CommandError((Status)value.Status));
                            }
                        }
                    }
                }

                // Raise events outside the lock so handlers can submit commands.
                foreach (var update in updates)
                {
                    PlaybackChanged?.Invoke(update);
                }
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            lock (_gate)
            {
                _terminalFailure = error;
                foreach (var item in _pending.Values)
                {
                    item.TrySetException(error);
                }

                _pending.Clear();
            }
        }
    }

    private async Task DisposeCoreAsync()
    {
        // Runs synchronously up to the first await, while DisposeAsync holds the gate.
#pragma warning disable CA1849 // CancelAsync would yield inside that critical section.
        _stopping.Cancel();
#pragma warning restore CA1849

        // Do not hold the gate while awaiting the pump or joining the native worker.
        await Task.Yield();
        await _pump;
        ulong handle;
        lock (_gate)
        {
            handle = _handle;
            _handle = 0;
        }

        foreach (var item in _pending.Values)
        {
            item.TrySetCanceled();
        }

        _pending.Clear();
        if (handle != 0)
        {
            await Task.Run(() => Native.cadenza_destroy(handle));
        }

        _stopping.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeEvent
    {
        public ulong RequestId;
        public uint Kind;
        public int Status;
        public uint Value;
        public uint Reserved;
    }

    private static partial class Native
    {
        private const string Library = "cadenza_audio";

        [LibraryImport(Library)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial uint cadenza_abi_version();

        [LibraryImport(Library)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial ulong cadenza_create();

        [LibraryImport(Library)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial int cadenza_submit(ulong id, ulong requestId, uint opcode);

        [LibraryImport(Library)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial int cadenza_submit_text(ulong id, ulong requestId, uint opcode, ReadOnlySpan<byte> payload, nuint length);

        [LibraryImport(Library)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial int cadenza_poll(ulong id, out NativeEvent value);

        [LibraryImport(Library)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial int cadenza_destroy(ulong id);
    }
}
