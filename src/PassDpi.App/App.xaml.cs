using System.Windows;
using PassDpi.App.Services;
using PassDpi.Setup;

namespace PassDpi.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;

        // Elevated helper modes (launched by the UI with "runas"). No window.
        if (args.Contains("--install") || args.Contains("--uninstall"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(await RunSetupAsync(args));
            return;
        }

        var prefs = UiPrefs.Load();
        if (prefs.Language != null) Loc.I.SetLanguage(prefs.Language);
        ThemeManager.Apply(prefs.Theme);

        var window = new MainWindow(prefs);
        MainWindow = window;
        window.Show();

#if DEBUG
        // --snapshot <png> [tab] [theme] [lang]
        var snap = Array.IndexOf(args, "--snapshot");
        if (snap >= 0 && snap + 1 < args.Length)
        {
            if (snap + 3 < args.Length) ThemeManager.Apply(args[snap + 3]);
            if (snap + 4 < args.Length) Loc.I.SetLanguage(args[snap + 4]);
            var tab = snap + 2 < args.Length && int.TryParse(args[snap + 2], out var t) ? t : 0;
            await window.SnapshotAsync(args[snap + 1], tab, 2500);
            Shutdown();
        }
#endif
    }

    private static async Task<int> RunSetupAsync(string[] args)
    {
        var log = new ListLog();
        try
        {
            if (args.Contains("--uninstall"))
            {
                Installer.Uninstall(log);
                return 0;
            }
            var sid = ArgValue(args, "--owner-sid") ?? throw new ArgumentException("--owner-sid missing");
            var folder = ArgValue(args, "--engine-folder");
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await Installer.InstallAsync(sid, folder, log, cts.Token);
            return 0;
        }
        catch (Exception ex)
        {
            var details = string.Join(Environment.NewLine, log.Lines.TakeLast(12));
            MessageBox.Show($"{ex.Message}{Environment.NewLine}{Environment.NewLine}{details}", "PassDPI setup",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }

    private static string? ArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
