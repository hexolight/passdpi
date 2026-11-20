using System.Reflection;
using PassDpi.Core;
using PassDpi.Core.Native;
using PassDpi.Core.Protocol;
using PassDpi.Core.Validation;
using PassDpi.Engine;
using PassDpi.Probe;
using PassDpi.Settings;

namespace PassDpi.Service;

/// <summary>
/// The service's state machine. Every operation that touches the engine runs under one
/// async lock (<see cref="_op"/>), so a network change, a crash restart and a UI command can
/// never fight over goodbyedpi.exe.
/// </summary>
public sealed class Controller : IDisposable
{
    private readonly FileLog _log;
    private readonly SettingsStore _store = new();
    private readonly EngineRunner _engine;
    private readonly ProfileTester _tester;
    private readonly SemaphoreSlim _op = new(1, 1);
    private readonly object _stateGate = new();

    private ServiceSettings _settings = new();
    private NetworkMemoryFile _memory = new();
    private byte[] _salt = Array.Empty<byte>();
    private NetworkIdentity? _network;
    private EngineState _state = EngineState.Off;
    private string? _lastMessage;
    private bool _engineInstalled;
    private CancellationTokenSource? _testCts;
    private Task? _testTask;

    // Crash restart with backoff
    private int _crashCount;
    private DateTime _lastStartUtc;
    private Timer? _restartTimer;

    private readonly CancellationTokenSource _shutdown = new();

    /// <summary>Raised for every message that should be pushed to connected UIs.</summary>
    public event Action<PipeMessage>? Broadcast;

    /// <summary>Raised when the UI asked to uninstall; the host stops the service afterwards.</summary>
    public event Action? UninstallRequested;

    public static string Version =>
        typeof(Controller).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "0.0.0";

    public string OwnerSid => _settings.OwnerSid;

    public Controller(FileLog log)
    {
        _log = log;
        _engine = new EngineRunner(AppPaths.EngineDir, log);
        _engine.Crashed += OnEngineCrashed;
        _tester = new ProfileTester(_engine, log);
    }

    // ------------------------------------------------------------------ lifecycle

    public void LoadState()
    {
        SecureDirectory.Ensure(AppPaths.DataDir, usersCanRead: false);
        if (Directory.Exists(AppPaths.InstallDir) && !SecureDirectory.IsProtected(AppPaths.InstallDir))
        {
            _log.Warn("Install folder ACL allowed non-admin writes; restoring the protected ACL.");
            SecureDirectory.Ensure(AppPaths.InstallDir, usersCanRead: true);
        }
        _settings = _store.LoadSettings();
        _memory = _store.LoadNetworks();
        _salt = NetworkFingerprint.LoadOrCreateSalt(AppPaths.SaltFile);
        _engineInstalled = EngineFiles.Verify(AppPaths.EngineDir, out var problem);
        if (!_engineInstalled) _log.Warn($"Engine not ready: {problem}");
        _log.Info($"PassDPI service {Version} started. Enabled={_settings.Enabled}, mode={(_settings.ManualProfileId ?? "auto")}");
    }

    public async Task StartAsync()
    {
        _engine.KillOrphans();
        if (_settings.Enabled) await EnsureForCurrentNetworkAsync("service start").ConfigureAwait(false);
        else SetState(EngineState.Off, "Disabled", "Disabled");
    }

    public void Shutdown()
    {
        _shutdown.Cancel();
        _testCts?.Cancel();
        _restartTimer?.Dispose();
        _engine.Stop();
        _log.Info("Service stopping.");
    }

    public void Dispose()
    {
        Shutdown();
        _engine.Dispose();
    }

    // ------------------------------------------------------------------ network handling

    /// <summary>Called (debounced) by the network watcher.</summary>
    public async Task OnNetworkChangedAsync()
    {
        if (_shutdown.IsCancellationRequested || !_settings.Enabled) return;
        var id = NetworkFingerprint.GetCurrent(_salt);
        if (id?.Hash == _network?.Hash) return; // same network, nothing to do
        _log.Info(id == null ? "Network lost." : $"Network changed: {id.Label}");

        // A test for the old network is meaningless now.
        if (_testCts != null) { _testCts.Cancel(); try { await (_testTask ?? Task.CompletedTask).ConfigureAwait(false); } catch { } }
        await EnsureForCurrentNetworkAsync("network change").ConfigureAwait(false);
    }

    /// <summary>
    /// Brings the engine to the right state for the current network:
    /// manual profile → just run it; remembered profile → run + quick verify; otherwise → full test.
    /// </summary>
    private async Task EnsureForCurrentNetworkAsync(string reason)
    {
        bool needTest = false;
        await _op.WaitAsync(_shutdown.Token).ConfigureAwait(false);
        try
        {
            _network = NetworkFingerprint.GetCurrent(_salt);
            if (!_settings.Enabled) { _engine.Stop(); SetState(EngineState.Off, "Disabled", "Disabled"); return; }
            if (!_engineInstalled) { SetState(EngineState.Error, "GoodbyeDPI files are missing or invalid. Reinstall PassDPI.", "EngineMissing"); return; }

            if (_settings.ManualProfileId != null)
            {
                var p = ProfileCatalog.Find(_settings.ManualProfileId, _settings.CustomArgs);
                if (p == null) { SetState(EngineState.Error, "Selected profile is invalid.", "ProfileInvalid"); return; }
                await StartProfileLocked(p, $"manual profile ({reason})").ConfigureAwait(false);
                return;
            }

            if (_network == null)
            {
                _log.Info("No network with a default gateway; waiting for one.");
                SetState(_engine.IsRunning ? EngineState.Running : EngineState.Off, "No network", "NoNetwork");
                return;
            }

            if (_memory.Networks.TryGetValue(_network.Hash, out var rec))
            {
                if (rec.ProfileId.Length == 0 && _engine.FindForeignEngines().Count > 0)
                {
                    // "Sites open with the engine off" proves nothing while another GoodbyeDPI is active.
                    SetState(EngineState.Error, "Another GoodbyeDPI is running. Close it and run the test again.", "OtherEngine");
                    return;
                }
                if (rec.ProfileId.Length == 0)
                {
                    // Remembered "no blocking here": confirm with the engine off.
                    _engine.Stop();
                    var r = await _tester.VerifyAsync(_settings.Sites, _shutdown.Token).ConfigureAwait(false);
                    if (r.ControlOk && r.Score >= 1.0) { SetState(EngineState.Off, "No blocking detected on this network", "NoBlocking"); return; }
                    needTest = true;
                }
                else
                {
                    var p = ProfileCatalog.Find(rec.ProfileId, _settings.CustomArgs);
                    if (p != null && await StartProfileLocked(p, $"remembered for this network ({reason})").ConfigureAwait(false))
                    {
                        var r = await _tester.VerifyAsync(_settings.Sites, _shutdown.Token).ConfigureAwait(false);
                        if (r.ControlOk && r.Score > 0 && r.Score >= rec.Score - 0.34)
                        {
                            SetState(EngineState.Running, $"Verified: {r.TargetOk}/{r.TargetCount} sites open", "Verified");
                            return;
                        }
                        _log.Info("Remembered profile no longer works well here; re-testing.");
                    }
                    needTest = true;
                }
            }
            else needTest = true;
        }
        catch (OperationCanceledException) { return; }
        finally { _op.Release(); }

        if (needTest) await RunTestAsync().ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ test

    private Task StartTestInBackground()
    {
        _testTask = Task.Run(RunTestAsync);
        return Task.CompletedTask;
    }

    private int _testRunning;
    public bool IsTesting => Volatile.Read(ref _testRunning) == 1;

    private async Task RunTestAsync()
    {
        if (Interlocked.CompareExchange(ref _testRunning, 1, 0) != 0) return;
        try { await _op.WaitAsync(_shutdown.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { Volatile.Write(ref _testRunning, 0); return; }
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        _testCts = cts;
        try
        {
            _network = NetworkFingerprint.GetCurrent(_salt);

            var foreign = _engine.FindForeignEngines();
            if (foreign.Count > 0)
            {
                // Another GoodbyeDPI already rewrites traffic: the baseline would look unblocked and every
                // profile would be measured on top of it. Refuse rather than remember a wrong result.
                var msg = $"Another GoodbyeDPI is running ({string.Join(", ", foreign)}). Close it and run the test again.";
                _log.Warn(msg);
                Push(new PipeMessage
                {
                    Kind = MessageKind.Event, Event = EventType.TestProgress,
                    Progress = new TestProgress { Phase = "Failed", Message = msg, Code = "OtherEngine" },
                });
                SetState(EngineState.Error, msg, "OtherEngine");
                return;
            }

            SetState(EngineState.Testing, "Testing profiles…", "Testing");
            _log.Info($"Starting automatic test on {(_network?.Label ?? "unknown network")}");

            var profiles = ProfileCatalog.BuiltIn.ToList();
            var custom = ProfileCatalog.Find(ProfileCatalog.CustomId, _settings.CustomArgs);
            if (custom != null) profiles.Add(custom);

            var report = await _tester.RunAsync(profiles, _settings.Sites,
                p => Push(new PipeMessage { Kind = MessageKind.Event, Event = EventType.TestProgress, Progress = p }),
                cts.Token).ConfigureAwait(false);

            _log.Info($"Test finished: {report.Outcome}. {report.Message}");
            Push(new PipeMessage
            {
                Kind = MessageKind.Event, Event = EventType.TestProgress,
                Progress = new TestProgress
                {
                    Phase = report.Outcome switch { TestOutcome.Found or TestOutcome.NoBlocking => "Done", TestOutcome.Cancelled => "Cancelled", _ => "Failed" },
                    ProfileId = report.Best?.Id ?? "", ProfileName = report.Best?.Name ?? "",
                    Score = report.Score, Message = report.Message, Code = report.Outcome.ToString(),
                }
            });

            switch (report.Outcome)
            {
                case TestOutcome.Found:
                    Remember(report.Best!.Id, report.Score);
                    if (await StartProfileLocked(report.Best!, "test winner").ConfigureAwait(false))
                        SetState(EngineState.Running, report.Message, "Found");
                    break;
                case TestOutcome.NoBlocking:
                    Remember("", 1);
                    SetState(EngineState.Off, report.Message, "NoBlocking");
                    break;
                case TestOutcome.Cancelled:
                    SetState(EngineState.Off, report.Message, "Cancelled");
                    break;
                default:
                    SetState(EngineState.Error, report.Message, report.Outcome.ToString());
                    break;
            }
        }
        catch (OperationCanceledException) { SetState(EngineState.Off, "Test cancelled.", "Cancelled"); }
        catch (Exception ex)
        {
            _engine.Stop();
            _log.Error($"Test failed: {ex}");
            SetState(EngineState.Error, "Test failed: " + ex.Message, "TestError");
        }
        finally
        {
            _testCts = null;
            cts.Dispose();
            _op.Release();
            Volatile.Write(ref _testRunning, 0);
        }
    }

    private void Remember(string profileId, double score)
    {
        if (_network == null) return;
        _memory.Networks[_network.Hash] = new NetworkRecord { ProfileId = profileId, Score = score, TestedUtc = DateTime.UtcNow };
        try { _store.SaveNetworks(_memory); } catch (Exception ex) { _log.Warn($"Saving network memory: {ex.Message}"); }
    }

    // ------------------------------------------------------------------ engine

    /// <summary>Profile that "Connect" uses: pinned manual profile, else this network's memory, else the last one that worked.</summary>
    private EngineProfile? SavedProfile(out bool noBlockingHere)
    {
        noBlockingHere = false;
        if (_settings.ManualProfileId != null) return ProfileCatalog.Find(_settings.ManualProfileId, _settings.CustomArgs);
        if (_network != null && _memory.Networks.TryGetValue(_network.Hash, out var rec))
        {
            if (rec.ProfileId.Length == 0) { noBlockingHere = true; return null; }
            var p = ProfileCatalog.Find(rec.ProfileId, _settings.CustomArgs);
            if (p != null) return p;
        }
        return _settings.LastProfileId != null ? ProfileCatalog.Find(_settings.LastProfileId, _settings.CustomArgs) : null;
    }

    /// <summary>"Connect": start the saved profile now. No test and no verify-then-retest, by design.</summary>
    private async Task ConnectSavedAsync()
    {
        await _op.WaitAsync(_shutdown.Token).ConfigureAwait(false);
        try
        {
            _network = NetworkFingerprint.GetCurrent(_salt);
            if (!_engineInstalled) { SetState(EngineState.Error, "GoodbyeDPI files are missing or invalid. Reinstall PassDPI.", "EngineMissing"); return; }
            var p = SavedProfile(out var noBlocking);
            if (noBlocking) { _engine.Stop(); SetState(EngineState.Off, "No blocking detected on this network", "NoBlocking"); return; }
            if (p == null) { SetState(EngineState.Off, "No saved profile yet. Run the test first.", "NoSavedProfile"); return; }
            if (await StartProfileLocked(p, "saved profile (connect)").ConfigureAwait(false))
                SetState(EngineState.Running, $"Connected: {p.Name}", "Connected");
        }
        catch (OperationCanceledException) { }
        finally { _op.Release(); }
    }

    private async Task<bool> StartProfileLocked(EngineProfile p, string why)
    {
        // Already running exactly this profile: don't restart it (a restart drops live connections).
        if (_engine.IsRunning && _engine.Current is { } cur && cur.Id == p.Id && cur.ArgsText == p.ArgsText)
        {
            SetState(EngineState.Running, $"Active: {p.Name}", "Active");
            return true;
        }
        SetState(EngineState.Starting, $"Starting {p.Name}", "Starting");
        _lastStartUtc = DateTime.UtcNow;
        var ok = await _engine.StartAsync(p, _shutdown.Token).ConfigureAwait(false);
        if (ok)
        {
            _log.Info($"Applied {p.Name} — {why}");
            if (_settings.LastProfileId != p.Id) { _settings.LastProfileId = p.Id; Save(); }
            _crashCount = why.StartsWith("crash", StringComparison.Ordinal) ? _crashCount : 0;
            SetState(EngineState.Running, $"Active: {p.Name}", "Active");
        }
        else SetState(EngineState.Error, $"GoodbyeDPI failed to start with {p.Name}. See the log.", "StartFailed");
        return ok;
    }

    private void OnEngineCrashed(int exitCode)
    {
        // Engine stops during tests are expected; and only restart if the user wants it on.
        if (_state == EngineState.Testing || !_settings.Enabled || _shutdown.IsCancellationRequested) return;

        if (DateTime.UtcNow - _lastStartUtc > TimeSpan.FromMinutes(5)) _crashCount = 0;
        _crashCount++;
        if (_crashCount > 5)
        {
            SetState(EngineState.Error, "GoodbyeDPI keeps crashing; stopped retrying. Run the test again.", "CrashGiveUp");
            return;
        }
        var delay = TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, _crashCount)));
        SetState(EngineState.Starting, $"Engine crashed (code {exitCode}); restarting in {delay.TotalSeconds:0}s", "CrashRestart");
        _restartTimer?.Dispose();
        _restartTimer = new Timer(_ => _ = EnsureForCurrentNetworkAsync("crash restart"), null, delay, Timeout.InfiniteTimeSpan);
    }

    // ------------------------------------------------------------------ commands

    public async Task<PipeMessage> HandleAsync(PipeMessage req)
    {
        var resp = new PipeMessage { Kind = MessageKind.Response, Id = req.Id, Ok = true };
        try
        {
            switch (req.Command)
            {
                case Command.Hello:
                case Command.GetStatus:
                    resp.Status = BuildStatus();
                    break;

                case Command.GetLogs:
                    resp.Logs = _log.Snapshot();
                    break;

                case Command.StartTest:
                    if (IsTesting) return Fail(resp, "busy");
                    if (!_engineInstalled) return Fail(resp, "engine-missing");
                    _settings.Enabled = true;
                    _settings.ManualProfileId = null;
                    Save();
                    await StartTestInBackground().ConfigureAwait(false);
                    break;

                case Command.CancelTest:
                    _testCts?.Cancel();
                    break;

                case Command.Apply:
                    if (req.ProfileId != null && ProfileCatalog.Find(req.ProfileId, _settings.CustomArgs) == null)
                        return Fail(resp, "unknown-profile");
                    _testCts?.Cancel();
                    _settings.Enabled = true;
                    _settings.ManualProfileId = req.ProfileId; // null = automatic
                    Save();
                    _ = Task.Run(() => EnsureForCurrentNetworkAsync("user request"));
                    break;

                case Command.Connect:
                    _testCts?.Cancel();
                    _settings.Enabled = true;
                    Save();
                    _ = Task.Run(ConnectSavedAsync);
                    break;

                case Command.Stop:
                    _settings.Enabled = false;
                    Save();
                    _testCts?.Cancel();
                    await _op.WaitAsync().ConfigureAwait(false);
                    try { _engine.Stop(); SetState(EngineState.Off, "Turned off", "Disabled"); }
                    finally { _op.Release(); }
                    break;

                case Command.SetAutoStart:
                    if (req.Flag == null) return Fail(resp, "missing flag");
                    ServiceControl.SetAutoStart(AppPaths.ServiceName, req.Flag.Value);
                    _log.Info($"Start with Windows: {(req.Flag.Value ? "on (Automatic)" : "off (Manual)")}");
                    break;

                case Command.SetTestSites:
                    var sites = SiteValidator.Normalize(req.Sites, out var siteErr);
                    if (sites == null) return Fail(resp, "sites: " + siteErr);
                    _settings.Sites = sites;
                    Save();
                    _log.Info($"Test sites updated ({sites.Count}).");
                    break;

                case Command.SetCustomArgs:
                    if (string.IsNullOrWhiteSpace(req.CustomArgs))
                    {
                        _settings.CustomArgs = null;
                        if (_settings.ManualProfileId == ProfileCatalog.CustomId) _settings.ManualProfileId = null;
                    }
                    else
                    {
                        var parsed = ArgumentWhitelist.Validate(req.CustomArgs, out var argErr);
                        if (parsed == null) return Fail(resp, "args: " + argErr);
                        _settings.CustomArgs = string.Join(' ', parsed);
                    }
                    Save();
                    break;

                case Command.Uninstall:
                    _log.Info("Uninstall requested by the user.");
                    UninstallRequested?.Invoke();
                    break;

                default:
                    return Fail(resp, "unknown command");
            }
            resp.Status ??= BuildStatus();
        }
        catch (Exception ex)
        {
            _log.Error($"{req.Command} failed: {ex.Message}");
            return Fail(resp, ex.Message);
        }
        return resp;
    }

    private static PipeMessage Fail(PipeMessage resp, string error)
    {
        resp.Ok = false;
        resp.Error = error;
        return resp;
    }

    private void Save()
    {
        try { _store.SaveSettings(_settings); }
        catch (Exception ex) { _log.Warn($"Saving settings: {ex.Message}"); }
    }

    // ------------------------------------------------------------------ uninstall

    /// <summary>Stops everything and removes PassDPI. The caller stops the service afterwards.</summary>
    public void PerformUninstall()
    {
        _testCts?.Cancel();
        _engine.Stop();
        Uninstall.RemoveOurWinDivertDriver(_log);
        try { ServiceControl.Delete(AppPaths.ServiceName); _log.Info("Service marked for deletion."); }
        catch (Exception ex) { _log.Warn($"Deleting service: {ex.Message}"); }
        Uninstall.DeleteShortcut(_log);
        _log.Close();
        Uninstall.DeleteDataDir(_log);
        Uninstall.ScheduleInstallDirRemoval(_log);
    }

    // ------------------------------------------------------------------ status

    private string? _lastCode;

    private void SetState(EngineState s, string? message, string? code = null)
    {
        lock (_stateGate)
        {
            _state = s;
            if (message != null) { _lastMessage = message; _lastCode = code; }
        }
        Push(new PipeMessage { Kind = MessageKind.Event, Event = EventType.Status, Status = BuildStatus() });
    }

    private void Push(PipeMessage m)
    {
        try { Broadcast?.Invoke(m); } catch { }
    }

    public ServiceStatus BuildStatus()
    {
        var active = _engine.Current;
        bool auto;
        try { auto = ServiceControl.IsAutoStart(AppPaths.ServiceName); } catch { auto = false; }
        var profiles = ProfileCatalog.BuiltIn.Select(p => new ProfileInfo { Id = p.Id, Name = p.Name, Arguments = p.ArgsText }).ToList();
        if (_settings.CustomArgs != null)
            profiles.Add(new ProfileInfo { Id = ProfileCatalog.CustomId, Name = "Custom", Arguments = _settings.CustomArgs });
        EngineProfile? saved = null;
        try { saved = SavedProfile(out _); } catch { /* memory being rewritten; status is best effort */ }

        lock (_stateGate)
        {
            return new ServiceStatus
            {
                Version = Version,
                State = _state,
                Enabled = _settings.Enabled,
                AutoStart = auto,
                EngineInstalled = _engineInstalled,
                ActiveProfileId = active?.Id,
                ActiveProfileName = active?.Name,
                ManualProfileId = _settings.ManualProfileId,
                SavedProfileId = saved?.Id,
                SavedProfileName = saved?.Name,
                NetworkLabel = _network?.Label,
                NetworkKnown = _network != null && _memory.Networks.ContainsKey(_network.Hash),
                LastMessage = _lastMessage,
                LastCode = _lastCode,
                CustomArgs = _settings.CustomArgs,
                Sites = _settings.Sites,
                Profiles = profiles,
            };
        }
    }
}
