using System.ServiceProcess;
using PassDpi.Core;

namespace PassDpi.Service;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Contains("--console"))
        {
            // Developer mode: run in the foreground (must be started from an elevated prompt).
            var host = new ServiceHost();
            host.Start(console: true);
            Console.WriteLine("PassDPI service running in console mode. Press Enter to stop.");
            Console.ReadLine();
            host.Stop();
            return 0;
        }

        ServiceBase.Run(new PassDpiService());
        return 0;
    }
}

/// <summary>Windows Service wrapper. OnStart returns immediately; the work happens in the background.</summary>
public sealed class PassDpiService : ServiceBase
{
    private readonly ServiceHost _host = new();

    public PassDpiService()
    {
        ServiceName = AppPaths.ServiceName;
        CanStop = true;
        CanShutdown = true;
        CanPauseAndContinue = false;
        AutoLog = false;
        _host.StopRequested += () => Task.Run(Stop);
    }

    protected override void OnStart(string[] args) => _host.Start(console: false);
    protected override void OnStop() => _host.Stop();
    protected override void OnShutdown() => _host.Stop();
}

/// <summary>Wires logger, controller, pipe and network watcher together.</summary>
public sealed class ServiceHost
{
    private FileLog? _log;
    private Controller? _controller;
    private PipeServer? _pipe;
    private NetworkWatcher? _watcher;
    private int _stopped;

    public event Action? StopRequested;

    public void Start(bool console)
    {
        _log = new FileLog(AppPaths.LogDir);
        if (console) _log.LineWritten += Console.WriteLine;
        try
        {
            _controller = new Controller(_log);
            _controller.LoadState();
            _pipe = new PipeServer(_controller, _log);
            _log.LineWritten += line => _pipe?.PushLog(line);
            _controller.UninstallRequested += OnUninstall;
            _pipe.Start();
            _watcher = new NetworkWatcher(_controller.OnNetworkChangedAsync, TimeSpan.FromSeconds(3));
            _ = Task.Run(async () =>
            {
                try { await _controller.StartAsync().ConfigureAwait(false); }
                catch (Exception ex) { _log.Error($"Startup: {ex}"); }
            });
        }
        catch (Exception ex)
        {
            _log.Error($"Fatal during start: {ex}");
            throw;
        }
    }

    private void OnUninstall()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(500).ConfigureAwait(false); // let the response reach the UI
            _watcher?.Dispose();
            _controller?.PerformUninstall();
            StopRequested?.Invoke();
        });
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 1) return;
        _watcher?.Dispose();
        _pipe?.Dispose();
        _controller?.Dispose();
        _log?.Close();
    }
}
