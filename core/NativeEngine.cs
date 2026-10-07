using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace ApurvaSpotify.Core;

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
        [DllImport("apurva_audio", CallingConvention = CallingConvention.Cdecl)] public static extern uint apurva_abi_version();
        [DllImport("apurva_audio", CallingConvention = CallingConvention.Cdecl)] public static extern ulong apurva_create();
        [DllImport("apurva_audio", CallingConvention = CallingConvention.Cdecl)] public static extern int apurva_submit(ulong id, ulong requestId, uint opcode);
        [DllImport("apurva_audio", CallingConvention = CallingConvention.Cdecl)] public static extern int apurva_poll(ulong id, out NativeEvent value);
        [DllImport("apurva_audio", CallingConvention = CallingConvention.Cdecl)] public static extern int apurva_destroy(ulong id);
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
        if (Marshal.SizeOf<NativeEvent>() != 24 || Native.apurva_abi_version() != 1)
            throw new InvalidOperationException("The native engine ABI does not match this application.");
        handle = Native.apurva_create();
        if (handle == 0) throw new InvalidOperationException("The native worker could not start.");
        pump = Task.Run(PumpAsync);
    }
    public Task<uint> CheckBridgeAsync(CancellationToken ct) => SubmitAsync(0, ct);
    public Task<uint> RequestPlaybackAsync(CancellationToken ct) => SubmitAsync(1, ct);

    private async Task<uint> SubmitAsync(uint opcode, CancellationToken ct)
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
            var status = Native.apurva_submit(handle, id, opcode);
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
                lock (gate)
                {
                    if (handle == 0) return;
                    for (var count = 0; count < 32; count++)
                    {
                        var status = Native.apurva_poll(handle, out var value);
                        if (status == 1) break;
                        if (status != 0) throw new InvalidOperationException("Native event channel failed.");
                        if (pending.TryRemove(value.RequestId, out var source))
                        {
                            if (value.Status == 0) source.TrySetResult(value.Value);
                            else source.TrySetException(new NotSupportedException("Standalone Spotify playback is not connected in this build."));
                        }
                    }
                }
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
        if (old != 0) await Task.Run(() => Native.apurva_destroy(old));
        stopping.Dispose();
        GC.SuppressFinalize(this);
    }
}
