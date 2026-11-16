using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using PassDpi.Core;
using PassDpi.Core.Protocol;

namespace PassDpi.Service;

/// <summary>
/// Control pipe. The DACL allows only the installing user (and SYSTEM); network logons are
/// explicitly denied. Requests are parsed into the closed <see cref="Command"/> set.
/// </summary>
public sealed class PipeServer : IDisposable
{
    private const int MaxClients = 4;

    private readonly Controller _controller;
    private readonly ILog _log;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _slots = new(MaxClients, MaxClients);
    private readonly List<Client> _clients = new();
    private readonly object _gate = new();
    private Task? _acceptLoop;

    private sealed class Client
    {
        public required NamedPipeServerStream Stream;
        public readonly SemaphoreSlim WriteLock = new(1, 1);
    }

    public PipeServer(Controller controller, ILog log)
    {
        _controller = controller;
        _log = log;
        _controller.Broadcast += m => _ = BroadcastAsync(m);
    }

    public void Start() => _acceptLoop = Task.Run(AcceptLoopAsync);

    internal static PipeSecurity BuildSecurity(string ownerSid)
    {
        var sec = new PipeSecurity();
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var network = new SecurityIdentifier(WellKnownSidType.NetworkSid, null);
        sec.AddAccessRule(new PipeAccessRule(network, PipeAccessRights.FullControl, AccessControlType.Deny));
        sec.AddAccessRule(new PipeAccessRule(system, PipeAccessRights.FullControl, AccessControlType.Allow));
        // The creating account needs CreateNewInstance for the 2nd..Nth instances (SYSTEM as a service).
        var self = WindowsIdentity.GetCurrent().User;
        if (self != null && self != system)
            sec.AddAccessRule(new PipeAccessRule(self, PipeAccessRights.FullControl, AccessControlType.Allow));

        SecurityIdentifier owner;
        try { owner = new SecurityIdentifier(ownerSid); }
        catch (Exception) { owner = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null); }
        sec.AddAccessRule(new PipeAccessRule(owner, PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize, AccessControlType.Allow));
        return sec;
    }

    private async Task AcceptLoopAsync()
    {
        var first = true;
        while (!_cts.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                await _slots.WaitAsync(_cts.Token).ConfigureAwait(false);
                var options = PipeOptions.Asynchronous | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None);
                server = NamedPipeServerStreamAcl.Create(AppPaths.PipeName, PipeDirection.InOut, MaxClients,
                    PipeTransmissionMode.Byte, options, 4096, 4096, BuildSecurity(_controller.OwnerSid));
                first = false;
                await server.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);
                var client = new Client { Stream = server };
                server = null;
                _ = Task.Run(() => ServeAsync(client));
            }
            catch (OperationCanceledException) { server?.Dispose(); break; }
            catch (UnauthorizedAccessException) when (first)
            {
                // FirstPipeInstance failed: someone else already owns this pipe name (squatting).
                _log.Error("Another process already created the control pipe. Refusing to share it; retrying in 10 s.");
                server?.Dispose();
                _slots.Release();
                try { await Task.Delay(10000, _cts.Token).ConfigureAwait(false); } catch { break; }
            }
            catch (Exception ex)
            {
                _log.Warn($"Pipe accept error: {ex.Message}");
                server?.Dispose();
                _slots.Release();
                try { await Task.Delay(1000, _cts.Token).ConfigureAwait(false); } catch { break; }
            }
        }
    }

    private async Task ServeAsync(Client c)
    {
        lock (_gate) _clients.Add(c);
        var buffer = new MemoryStream();
        var chunk = new byte[4096];
        try
        {
            while (!_cts.IsCancellationRequested && c.Stream.IsConnected)
            {
                var line = await MessageFraming.ReadLineAsync(c.Stream, buffer, chunk, _cts.Token).ConfigureAwait(false);
                if (line == null) break;
                var req = MessageFraming.Deserialize(line);
                PipeMessage resp;
                if (req == null || req.Kind != MessageKind.Request || req.Command == null)
                    resp = new PipeMessage { Kind = MessageKind.Response, Id = req?.Id ?? 0, Ok = false, Error = "bad request" };
                else
                    resp = await _controller.HandleAsync(req).ConfigureAwait(false);

                await SendAsync(c, resp).ConfigureAwait(false);

                if (req?.Command == Command.Uninstall && resp.Ok) break;
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or InvalidDataException) { }
        finally
        {
            lock (_gate) _clients.Remove(c);
            try { c.Stream.Dispose(); } catch { }
            _slots.Release();
        }
    }

    private static async Task SendAsync(Client c, PipeMessage m)
    {
        await c.WriteLock.WaitAsync().ConfigureAwait(false);
        try { await MessageFraming.WriteAsync(c.Stream, m, CancellationToken.None).ConfigureAwait(false); }
        finally { c.WriteLock.Release(); }
    }

    private async Task BroadcastAsync(PipeMessage m)
    {
        Client[] targets;
        lock (_gate) targets = _clients.ToArray();
        foreach (var c in targets)
        {
            try { await SendAsync(c, m).ConfigureAwait(false); }
            catch { /* client went away; ServeAsync cleans up */ }
        }
    }

    public void PushLog(string line)
    {
        lock (_gate) if (_clients.Count == 0) return;
        _ = BroadcastAsync(new PipeMessage { Kind = MessageKind.Event, Event = EventType.Log, Logs = new() { line } });
    }

    public void Dispose()
    {
        _cts.Cancel();
        lock (_gate)
            foreach (var c in _clients) { try { c.Stream.Dispose(); } catch { } }
        try { _acceptLoop?.Wait(2000); } catch { }
    }
}
