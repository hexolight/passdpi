using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using PassDpi.Core;
using PassDpi.Core.Native;
using PassDpi.Core.Protocol;

namespace PassDpi.App.Services;

/// <summary>
/// Client side of the control pipe. Verifies the pipe really belongs to the PassDPI service
/// process (guards against another program squatting the pipe name).
/// </summary>
public sealed partial class PipeClient : IDisposable
{
    private NamedPipeClientStream? _pipe;
    private readonly Dictionary<int, TaskCompletionSource<PipeMessage>> _pending = new();
    private readonly object _gate = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private int _nextId;

    public event Action<PipeMessage>? EventReceived;
    public event Action? Disconnected;

    public bool IsConnected => _pipe?.IsConnected == true;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeServerProcessId(nint pipe, out uint pid);

    public async Task<bool> ConnectAsync(int timeoutMs)
    {
        var pipe = new NamedPipeClientStream(".", AppPaths.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(timeoutMs, _cts.Token).ConfigureAwait(false);

            // The server must be the PassDPI service process, not something that grabbed the name.
            var servicePid = ServiceControl.GetProcessId(AppPaths.ServiceName);
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var serverPid) ||
                servicePid == 0 || serverPid != servicePid)
            {
                pipe.Dispose();
                return false;
            }
        }
        catch (Exception)
        {
            pipe.Dispose();
            return false;
        }

        _pipe = pipe;
        _ = Task.Run(ReadLoopAsync);
        return true;
    }

    private async Task ReadLoopAsync()
    {
        var pipe = _pipe!;
        var buffer = new MemoryStream();
        var chunk = new byte[8192];
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var line = await MessageFraming.ReadLineAsync(pipe, buffer, chunk, _cts.Token).ConfigureAwait(false);
                if (line == null) break;
                var msg = MessageFraming.Deserialize(line);
                if (msg == null) continue;
                if (msg.Kind == MessageKind.Response)
                {
                    TaskCompletionSource<PipeMessage>? tcs;
                    lock (_gate) { _pending.Remove(msg.Id, out tcs); }
                    tcs?.TrySetResult(msg);
                }
                else if (msg.Kind == MessageKind.Event)
                {
                    EventReceived?.Invoke(msg);
                }
            }
        }
        catch { }
        finally
        {
            lock (_gate)
            {
                foreach (var t in _pending.Values) t.TrySetException(new IOException("disconnected"));
                _pending.Clear();
            }
            try { pipe.Dispose(); } catch { }
            if (!_cts.IsCancellationRequested) Disconnected?.Invoke();
        }
    }

    public async Task<PipeMessage> SendAsync(PipeMessage req, int timeoutMs = 15000)
    {
        var pipe = _pipe;
        if (pipe == null || !pipe.IsConnected) throw new IOException("not connected");
        req.Kind = MessageKind.Request;
        req.Id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<PipeMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate) _pending[req.Id] = tcs;

        await _writeLock.WaitAsync().ConfigureAwait(false);
        try { await MessageFraming.WriteAsync(pipe, req, _cts.Token).ConfigureAwait(false); }
        finally { _writeLock.Release(); }

        var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs)).ConfigureAwait(false);
        if (done != tcs.Task)
        {
            lock (_gate) _pending.Remove(req.Id);
            throw new TimeoutException();
        }
        return await tcs.Task.ConfigureAwait(false);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _pipe?.Dispose(); } catch { }
    }
}
