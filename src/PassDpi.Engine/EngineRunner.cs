using System.Diagnostics;
using PassDpi.Core;

namespace PassDpi.Engine;

/// <summary>
/// Owns the single goodbyedpi.exe child process. Event driven: no polling, the OS tells us
/// when the process exits.
/// </summary>
public sealed class EngineRunner : IDisposable
{
    private readonly string _engineDir;
    private readonly ILog _log;
    private readonly object _gate = new();
    private readonly JobObject _job = new();
    private Process? _proc;
    private bool _stopping;
    private int _outputLines;

    /// <summary>Raised when the engine exits without us asking it to (exit code).</summary>
    public event Action<int>? Crashed;

    public EngineProfile? Current { get; private set; }
    public bool IsRunning { get { lock (_gate) return _proc is { HasExited: false }; } }

    public EngineRunner(string engineDir, ILog log)
    {
        _engineDir = engineDir;
        _log = log;
    }

    public string ExePath => Path.Combine(_engineDir, EngineManifest.ExeName);

    /// <summary>
    /// Starts the engine and waits until it reports the filter active (or a short grace period
    /// passes with the process still alive). Returns false if it exited or failed verification.
    /// </summary>
    public async Task<bool> StartAsync(EngineProfile profile, CancellationToken ct)
    {
        Stop();

        if (!EngineFiles.Verify(_engineDir, out var problem))
        {
            _log.Error($"Engine files failed verification: {problem}. Refusing to start.");
            return false;
        }

        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var psi = new ProcessStartInfo(ExePath)
        {
            WorkingDirectory = _engineDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in profile.Args) psi.ArgumentList.Add(a);

        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _outputLines = 0;
        p.OutputDataReceived += (_, e) => OnOutput(e.Data, ready);
        p.ErrorDataReceived += (_, e) => OnOutput(e.Data, ready);
        p.Exited += (_, _) => OnExited(p, ready);

        lock (_gate)
        {
            _stopping = false;
            p.Start();
            try { _job.Assign(p.Handle); }
            catch (Exception ex) { _log.Warn($"Could not assign engine to job object: {ex.Message}"); }
            _proc = p;
            Current = profile;
        }
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        _log.Info($"Engine started (pid {p.Id}): {profile.Name} [{profile.ArgsText}]");

        // goodbyedpi's stdout may be block-buffered when redirected, so the "Filter activated"
        // line can arrive late; treat "still alive after the grace period" as started too.
        var grace = Task.Delay(TimeSpan.FromSeconds(2.5), ct);
        await Task.WhenAny(ready.Task, grace).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_proc != p || p.HasExited)
            {
                _log.Warn($"Engine exited during startup (code {SafeExit(p)}).");
                return false;
            }
        }
        return true;
    }

    private void OnOutput(string? line, TaskCompletionSource ready)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        if (line.Contains("Filter activated", StringComparison.OrdinalIgnoreCase)) ready.TrySetResult();
        // Cap per-run output so a verbose flag can't flood the log.
        if (Interlocked.Increment(ref _outputLines) <= 40) _log.Info("[goodbyedpi] " + line.Trim());
    }

    private void OnExited(Process p, TaskCompletionSource ready)
    {
        ready.TrySetResult();
        bool owned, unexpected;
        lock (_gate)
        {
            // If Stop() already detached this process, Stop owns it and disposes it after waiting.
            owned = _proc == p;
            unexpected = owned && !_stopping;
            if (owned) { _proc = null; Current = null; }
        }
        var code = SafeExit(p);
        if (owned) p.Dispose();
        if (unexpected)
        {
            _log.Warn($"Engine exited unexpectedly (code {code}).");
            Crashed?.Invoke(code);
        }
    }

    private static int SafeExit(Process p) { try { return p.ExitCode; } catch { return -1; } }

    public void Stop()
    {
        Process? p;
        lock (_gate)
        {
            p = _proc;
            _stopping = true;
            _proc = null;
            Current = null;
        }
        if (p == null) return;
        try
        {
            if (!p.HasExited)
            {
                p.Kill(entireProcessTree: true);
                p.WaitForExit(3000);
            }
            _log.Info("Engine stopped.");
        }
        catch (Exception ex) { _log.Warn($"Stopping engine: {ex.Message}"); }
        finally { p.Dispose(); }
    }

    /// <summary>Kills goodbyedpi.exe processes left over from our engine folder (e.g. after a crash).</summary>
    public void KillOrphans()
    {
        foreach (var p in Process.GetProcessesByName("goodbyedpi"))
        {
            try
            {
                var path = p.MainModule?.FileName;
                if (path != null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(ExePath), StringComparison.OrdinalIgnoreCase))
                {
                    _log.Warn($"Killing leftover engine process {p.Id}");
                    p.Kill();
                }
                else if (path != null)
                {
                    _log.Warn($"Another GoodbyeDPI is running ({path}). It may conflict with PassDPI.");
                }
            }
            catch { /* access denied / exited */ }
            finally { p.Dispose(); }
        }
    }

    /// <summary>
    /// Paths of goodbyedpi.exe processes that are not ours (e.g. a copy the user runs by hand).
    /// They already rewrite traffic, so they would make every test result meaningless.
    /// </summary>
    public List<string> FindForeignEngines()
    {
        var list = new List<string>();
        foreach (var p in Process.GetProcessesByName("goodbyedpi"))
        {
            try
            {
                var path = p.MainModule?.FileName;
                if (path == null || !string.Equals(Path.GetFullPath(path), Path.GetFullPath(ExePath), StringComparison.OrdinalIgnoreCase))
                    list.Add(path ?? $"PID {p.Id}");
            }
            catch { list.Add($"PID {p.Id}"); }
            finally { p.Dispose(); }
        }
        return list;
    }

    public void Dispose()
    {
        Stop();
        _job.Dispose();
    }
}
