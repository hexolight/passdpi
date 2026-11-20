using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using PassDpi.App.Services;
using PassDpi.Core;
using PassDpi.Core.Native;
using PassDpi.Core.Protocol;
using PassDpi.Core.Validation;

namespace PassDpi.App.ViewModels;

public enum Conn { Connecting, NotInstalled, Stopped, Connected, Working }

public sealed class SiteItem : ObservableObject
{
    private string _url = "";
    private bool _isControl;
    public string Url { get => _url; set => Set(ref _url, value); }
    public bool IsControl { get => _isControl; set => Set(ref _isControl, value); }
}

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private static Loc L => Loc.I;

    private readonly Dispatcher _ui;
    private readonly UiPrefs _prefs;
    private readonly DispatcherTimer _retry;
    private readonly DispatcherTimer _bannerTimer;
    private PipeClient? _client;
    private bool _suppress;
    private readonly StringBuilder _log = new();
    private int _logLines;

    public MainViewModel(UiPrefs prefs)
    {
        _ui = Dispatcher.CurrentDispatcher;
        _prefs = prefs;

        // Only runs while the window is open AND we're not connected (service down / not installed).
        _retry = new DispatcherTimer(DispatcherPriority.Background, _ui) { Interval = TimeSpan.FromSeconds(2) };
        _retry.Tick += async (_, _) => await TryConnectAsync();
        _bannerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
        _bannerTimer.Tick += (_, _) => { _bannerTimer.Stop(); Banner = null; };

        // Two separate actions: connect with the saved profile right away, or run the full test.
        ConnectCommand = new RelayCommand(() => SendAsync(new PipeMessage { Command = Command.Connect }), () => IsConnected && !IsTesting);
        TestCommand = new RelayCommand(StartTestAsync, () => IsConnected && !IsTesting);
        CancelTestCommand = new RelayCommand(() => SendAsync(new PipeMessage { Command = Command.CancelTest }), () => IsTesting);
        InstallCommand = new RelayCommand(() => InstallAsync(null));
        InstallFromFolderCommand = new RelayCommand(InstallFromFolderAsync);
        StartServiceCommand = new RelayCommand(StartServiceAsync);
        ApplyProfileCommand = new RelayCommand(ApplySelectedAsync, () => IsConnected && SelectedProfile != null);
        AutoModeCommand = new RelayCommand(() => SendAsync(new PipeMessage { Command = Command.Apply, ProfileId = null }), () => IsConnected);
        SaveCustomArgsCommand = new RelayCommand(() => SaveCustomArgsAsync(CustomArgs), () => IsConnected);
        ClearCustomArgsCommand = new RelayCommand(() => SaveCustomArgsAsync(null), () => IsConnected);
        AddSiteCommand = new RelayCommand(AddSite);
        RemoveSiteCommand = new RelayCommand(p => { if (p is SiteItem s) { Sites.Remove(s); SitesDirty = true; } return Task.CompletedTask; });
        ResetSitesCommand = new RelayCommand(ResetSites);
        SaveSitesCommand = new RelayCommand(SaveSitesAsync, () => IsConnected);
        CopyLogCommand = new RelayCommand(() => { try { Clipboard.SetText(LogText); } catch { } });
        RefreshLogCommand = new RelayCommand(LoadLogsAsync, () => IsConnected);
        UninstallCommand = new RelayCommand(UninstallAsync);
        SetLanguageCommand = new RelayCommand(p => { SetLanguage(p as string ?? "en"); return Task.CompletedTask; });
        SetThemeCommand = new RelayCommand(p => { SetTheme(p as string ?? "dark"); return Task.CompletedTask; });

        L.PropertyChanged += (_, _) => RefreshTexts();
    }

    // ------------------------------------------------------------------ commands

    public RelayCommand ConnectCommand { get; }
    public RelayCommand TestCommand { get; }
    public RelayCommand CancelTestCommand { get; }
    public RelayCommand InstallCommand { get; }
    public RelayCommand InstallFromFolderCommand { get; }
    public RelayCommand StartServiceCommand { get; }
    public RelayCommand ApplyProfileCommand { get; }
    public RelayCommand AutoModeCommand { get; }
    public RelayCommand SaveCustomArgsCommand { get; }
    public RelayCommand ClearCustomArgsCommand { get; }
    public RelayCommand AddSiteCommand { get; }
    public RelayCommand RemoveSiteCommand { get; }
    public RelayCommand ResetSitesCommand { get; }
    public RelayCommand SaveSitesCommand { get; }
    public RelayCommand CopyLogCommand { get; }
    public RelayCommand RefreshLogCommand { get; }
    public RelayCommand UninstallCommand { get; }
    public RelayCommand SetLanguageCommand { get; }
    public RelayCommand SetThemeCommand { get; }

    // ------------------------------------------------------------------ bindable state

    private Conn _conn = Conn.Connecting;
    public Conn Connection
    {
        get => _conn;
        set
        {
            if (!Set(ref _conn, value)) return;
            Raise(nameof(IsConnected)); Raise(nameof(ShowOverlay)); Raise(nameof(OverlayText));
            Raise(nameof(ShowInstall)); Raise(nameof(ShowStart)); Raise(nameof(ShowWorking));
        }
    }
    public bool IsConnected => _conn == Conn.Connected;
    public bool ShowOverlay => _conn != Conn.Connected;
    public bool ShowInstall => _conn is Conn.NotInstalled or Conn.Stopped;
    public bool ShowStart => _conn == Conn.Stopped;
    public bool ShowWorking => _conn is Conn.Connecting or Conn.Working;
    public string OverlayText => _conn switch
    {
        Conn.NotInstalled => L["Svc.NotInstalled"],
        Conn.Stopped => L["Svc.Stopped"],
        Conn.Working => L["Svc.Installing"],
        _ => L["Svc.Connecting"],
    };

    private ServiceStatus? _status;
    public ServiceStatus? Status { get => _status; private set { _status = value; RefreshTexts(); } }

    public string StateKind => _status?.State.ToString() ?? "Off";
    public string StateText => L["State." + StateKind];
    public string ProfileText => _status?.ActiveProfileName ?? L["Home.None"];
    public string NetworkText => _status?.NetworkLabel ?? L["Home.None"];
    public string ModeText => _status?.ManualProfileId == null ? L["Home.ModeAuto"] : L["Home.ModeManual"];
    public string DetailText
    {
        get
        {
            if (_status == null) return L["Home.Hint"];
            if (_status.LastCode != null) return L["Code." + _status.LastCode];
            return _status.LastMessage ?? L["Home.Hint"];
        }
    }
    public string VersionText => _status?.Version ?? "-";

    public bool IsTesting => _status?.State == EngineState.Testing;
    public bool IsNotTesting => !IsTesting;
    public string BigButtonText =>
        IsTesting ? $"{(int)ProgressValue}%" :
        _status?.State == EngineState.Running ? L["Home.Connected"] : L["Home.Connect"];
    /// <summary>Which profile "Connect" will use, or a hint to run the test first.</summary>
    public string SavedText => _status?.SavedProfileName is { } n ? L.F("Home.Saved", n) : L["Home.NoSaved"];

    private bool _enabled;
    public bool ProtectionEnabled
    {
        get => _enabled;
        set
        {
            if (!Set(ref _enabled, value) || _suppress) return;
            _ = SendAsync(value
                ? new PipeMessage { Command = Command.Apply, ProfileId = _status?.ManualProfileId }
                : new PipeMessage { Command = Command.Stop });
        }
    }

    private bool _autoStart;
    public bool AutoStart
    {
        get => _autoStart;
        set
        {
            if (!Set(ref _autoStart, value) || _suppress) return;
            _ = SendAsync(new PipeMessage { Command = Command.SetAutoStart, Flag = value });
        }
    }

    private double _progress;
    public double ProgressValue { get => _progress; set { Set(ref _progress, value); Raise(nameof(BigButtonText)); } }

    private string _progressText = "";
    public string ProgressText { get => _progressText; set => Set(ref _progressText, value); }

    private string? _banner;
    public string? Banner
    {
        get => _banner;
        set { Set(ref _banner, value); Raise(nameof(HasBanner)); }
    }
    public bool HasBanner => !string.IsNullOrEmpty(_banner);

    private bool _bannerIsError;
    public bool BannerIsError { get => _bannerIsError; set => Set(ref _bannerIsError, value); }

    private int _tab;
    public int SelectedTab { get => _tab; set { Set(ref _tab, value); if (value == 3) _ = LoadLogsAsync(); } }

    public ObservableCollection<ProfileInfo> Profiles { get; } = new();
    private ProfileInfo? _selectedProfile;
    public ProfileInfo? SelectedProfile { get => _selectedProfile; set => Set(ref _selectedProfile, value); }

    private string? _customArgs;
    public string? CustomArgs { get => _customArgs; set => Set(ref _customArgs, value); }

    public ObservableCollection<SiteItem> Sites { get; } = new();
    public bool SitesDirty { get; set; }

    private string _newSite = "";
    public string NewSiteUrl { get => _newSite; set => Set(ref _newSite, value); }

    public string LogText => _log.ToString();

    public string Language => L.Lang;
    public string Theme => _prefs.Theme;

    private void RefreshTexts()
    {
        foreach (var n in new[] { nameof(StateKind), nameof(StateText), nameof(ProfileText), nameof(NetworkText),
                     nameof(ModeText), nameof(DetailText), nameof(VersionText), nameof(IsTesting), nameof(IsNotTesting), nameof(BigButtonText),
                     nameof(SavedText), nameof(OverlayText), nameof(Language) })
            Raise(n);
        CommandManagerRequery();
    }

    private static void CommandManagerRequery() => System.Windows.Input.CommandManager.InvalidateRequerySuggested();

    // ------------------------------------------------------------------ connection

    public async Task InitializeAsync()
    {
        await TryConnectAsync();
        if (!IsConnected) _retry.Start();
    }

    private bool _connecting;

    private async Task TryConnectAsync()
    {
        if (_connecting || IsConnected || _conn == Conn.Working) return;
        _connecting = true;
        try
        {
            var state = await Task.Run(() => { try { return ServiceControl.GetState(AppPaths.ServiceName); } catch { return 0u; } });
            if (state == 0) { Connection = Conn.NotInstalled; return; }
            if (state is 1 or 3) { Connection = Conn.Stopped; return; }
            if (state != 4) { Connection = Conn.Connecting; return; }

            var client = new PipeClient();
            if (!await client.ConnectAsync(1500)) { client.Dispose(); Connection = Conn.Connecting; return; }

            client.EventReceived += m => _ui.BeginInvoke(() => OnEvent(m));
            client.Disconnected += () => _ui.BeginInvoke(OnDisconnected);
            _client = client;
            Connection = Conn.Connected;
            _retry.Stop();

            var hello = await client.SendAsync(new PipeMessage { Command = Command.Hello });
            if (hello.Status != null) ApplyStatus(hello.Status, refreshSites: true);
            await LoadLogsAsync();
        }
        finally { _connecting = false; }
    }

    private void OnDisconnected()
    {
        _client?.Dispose();
        _client = null;
        if (_conn == Conn.Working) return;
        Connection = Conn.Connecting;
        _retry.Start();
    }

    private void OnEvent(PipeMessage m)
    {
        switch (m.Event)
        {
            case EventType.Status when m.Status != null:
                ApplyStatus(m.Status, refreshSites: false);
                break;
            case EventType.TestProgress when m.Progress != null:
                OnProgress(m.Progress);
                break;
            case EventType.Log when m.Logs != null:
                foreach (var l in m.Logs) AppendLog(l);
                Raise(nameof(LogText));
                break;
        }
    }

    private void ApplyStatus(ServiceStatus s, bool refreshSites)
    {
        Status = s;
        _suppress = true;
        try
        {
            ProtectionEnabled = s.Enabled;
            AutoStart = s.AutoStart;
        }
        finally { _suppress = false; }

        if (!IsTesting && s.State != EngineState.Testing) { ProgressValue = 0; ProgressText = ""; }

        // Profiles: rebuild only when the list actually changed (keeps selection stable).
        var ids = string.Join(",", s.Profiles.Select(p => p.Id + p.Arguments));
        if (ids != string.Join(",", Profiles.Select(p => p.Id + p.Arguments)))
        {
            var sel = SelectedProfile?.Id;
            Profiles.Clear();
            foreach (var p in s.Profiles) Profiles.Add(p);
            SelectedProfile = Profiles.FirstOrDefault(p => p.Id == (sel ?? s.ManualProfileId));
        }

        if (CustomArgs == null && s.CustomArgs != null) CustomArgs = s.CustomArgs;

        if (refreshSites || !SitesDirty)
        {
            Sites.Clear();
            foreach (var site in s.Sites) Sites.Add(new SiteItem { Url = site.Url, IsControl = site.IsControl });
            SitesDirty = false;
        }
    }

    private void OnProgress(TestProgress p)
    {
        switch (p.Phase)
        {
            case "Running":
                ProgressValue = p.Total == 0 ? 0 : 100.0 * p.Index / (p.Total + 1);
                ProgressText = p.ProfileId == "baseline" ? L["Progress.Baseline"] : L.F("Progress.Profile", p.Index, p.Total, p.ProfileName);
                break;
            case "Result":
                if (p.Count is > 0)
                    ProgressText = L.F("Progress.Result", p.ProfileId == "baseline" ? L["Progress.Baseline"] : p.ProfileName, p.Ok ?? 0, p.Count ?? 0);
                break;
            case "Done":
            case "Failed":
            case "Cancelled":
                ProgressValue = 0;
                ProgressText = "";
                ShowBanner(p.Code != null ? L["Code." + p.Code] : p.Message ?? "", isError: p.Phase == "Failed");
                break;
        }
    }

    // ------------------------------------------------------------------ actions

    private async Task<PipeMessage?> SendAsync(PipeMessage m)
    {
        var c = _client;
        if (c == null) { ShowBanner(L["Err.NotConnected"], true); return null; }
        try
        {
            var r = await c.SendAsync(m);
            if (!r.Ok)
            {
                ShowBanner(r.Error == "busy" ? L["Err.Busy"] : L.F("Err.Generic", r.Error ?? "?"), true);
                if (r.Status != null) ApplyStatus(r.Status, false);
                return r;
            }
            if (r.Status != null) ApplyStatus(r.Status, false);
            return r;
        }
        catch (Exception ex)
        {
            ShowBanner(L.F("Err.Generic", ex.Message), true);
            return null;
        }
    }

    private async Task StartTestAsync()
    {
        ProgressValue = 0;
        ProgressText = L["Progress.Baseline"];
        await SendAsync(new PipeMessage { Command = Command.StartTest });
    }

    private Task ApplySelectedAsync() =>
        SelectedProfile == null ? Task.CompletedTask
            : SendAsync(new PipeMessage { Command = Command.Apply, ProfileId = SelectedProfile.Id });

    private async Task SaveCustomArgsAsync(string? args)
    {
        var r = await SendAsync(new PipeMessage { Command = Command.SetCustomArgs, CustomArgs = args ?? "" });
        if (r?.Ok == true)
        {
            CustomArgs = r.Status?.CustomArgs;
            if (!string.IsNullOrEmpty(args)) ShowBanner(L["Profiles.CustomSaved"], false);
        }
    }

    private void AddSite()
    {
        if (!SiteValidator.TryNormalize(NewSiteUrl, out var url)) { ShowBanner(L["Sites.Invalid"], true); return; }
        if (Sites.Any(s => string.Equals(s.Url, url, StringComparison.OrdinalIgnoreCase))) { NewSiteUrl = ""; return; }
        Sites.Add(new SiteItem { Url = url });
        NewSiteUrl = "";
        SitesDirty = true;
    }

    private void ResetSites()
    {
        Sites.Clear();
        foreach (var s in SiteValidator.Defaults()) Sites.Add(new SiteItem { Url = s.Url, IsControl = s.IsControl });
        SitesDirty = true;
    }

    private async Task SaveSitesAsync()
    {
        var list = Sites.Select(s => new TestSite { Url = s.Url, IsControl = s.IsControl }).ToList();
        var r = await SendAsync(new PipeMessage { Command = Command.SetTestSites, Sites = list });
        if (r?.Ok == true) { SitesDirty = false; ShowBanner(L["Sites.Saved"], false); }
    }

    private async Task LoadLogsAsync()
    {
        if (_client == null) return;
        var r = await SendAsync(new PipeMessage { Command = Command.GetLogs });
        if (r?.Logs == null) return;
        _log.Clear(); _logLines = 0;
        foreach (var l in r.Logs) AppendLog(l);
        Raise(nameof(LogText));
    }

    private void AppendLog(string line)
    {
        _log.AppendLine(line);
        if (++_logLines > 600)
        {
            var text = _log.ToString();
            var cut = text.IndexOf('\n', text.Length / 3);
            _log.Clear().Append(text.AsSpan(cut + 1));
            _logLines = _log.ToString().Count(c => c == '\n');
        }
    }

    // ------------------------------------------------------------------ install / start / uninstall

    private async Task StartServiceAsync()
    {
        try
        {
            await Task.Run(() => ServiceControl.Start(AppPaths.ServiceName));
            Connection = Conn.Connecting;
            for (var i = 0; i < 20 && !IsConnected; i++) { await Task.Delay(500); await TryConnectAsync(); }
        }
        catch (Exception ex) { ShowBanner(L.F("Svc.StartFailed", ex.Message), true); }
    }

    private async Task InstallFromFolderAsync()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = L["Svc.PickFolderTitle"] };
        if (dlg.ShowDialog() == true) await InstallAsync(dlg.FolderName);
    }

    private async Task InstallAsync(string? engineFolder)
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value;
        if (sid == null) return;
        var args = $"--install --owner-sid {sid}";
        if (engineFolder != null) args += $" --engine-folder \"{engineFolder.TrimEnd('\\')}\"";

        _client?.Dispose(); _client = null;
        _retry.Stop();
        Connection = Conn.Working;
        var code = await RunElevatedAsync(args);
        Connection = Conn.Connecting;
        if (code == -1223) ShowBanner(L["Svc.InstallCancelled"], true);
        else if (code != 0) ShowBanner(L.F("Svc.InstallFailed", code), true);

        for (var i = 0; i < 20 && !IsConnected; i++) { await TryConnectAsync(); if (!IsConnected) await Task.Delay(500); }
        if (!IsConnected) _retry.Start();
    }

    private static async Task<int> RunElevatedAsync(string args)
    {
        try
        {
            var psi = new ProcessStartInfo(Environment.ProcessPath!, args) { UseShellExecute = true, Verb = "runas" };
            using var p = Process.Start(psi);
            if (p == null) return -1;
            await p.WaitForExitAsync();
            return p.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return -1223; } // user said no
    }

    private async Task UninstallAsync()
    {
        if (MessageBox.Show(L["Settings.UninstallConfirm"], "PassDPI", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        bool ok;
        if (_client != null)
        {
            var r = await SendAsync(new PipeMessage { Command = Command.Uninstall });
            ok = r?.Ok == true;
        }
        else
        {
            _retry.Stop();
            Connection = Conn.Working;
            ok = await RunElevatedAsync("--uninstall") == 0;
            if (!ok) { Connection = Conn.Connecting; _retry.Start(); }
        }
        if (!ok) return;

        UiPrefs.DeleteAll();
        MessageBox.Show(L["Settings.Uninstalled"], "PassDPI", MessageBoxButton.OK, MessageBoxImage.Information);
        Application.Current.Shutdown();
    }

    // ------------------------------------------------------------------ language / theme

    public void SetLanguage(string lang)
    {
        L.SetLanguage(lang);
        _prefs.Language = L.Lang;
        _prefs.Save();
    }

    public void SetTheme(string theme)
    {
        _prefs.Theme = theme == "light" ? "light" : "dark";
        _prefs.Save();
        ThemeManager.Apply(_prefs.Theme);
        Raise(nameof(Theme));
    }

    private void ShowBanner(string text, bool isError)
    {
        BannerIsError = isError;
        Banner = text;
        _bannerTimer.Stop();
        _bannerTimer.Start();
    }

    public void Dispose()
    {
        _retry.Stop();
        _client?.Dispose();
    }
}
