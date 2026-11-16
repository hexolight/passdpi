using System.Diagnostics;
using System.Security.Principal;
using PassDpi.Core;
using PassDpi.Core.Native;
using PassDpi.Core.Validation;
using PassDpi.Engine;
using PassDpi.Settings;

namespace PassDpi.Setup;

public sealed class ListLog : ILog
{
    public readonly List<string> Lines = new();
    public void Write(LogLevel level, string message) => Lines.Add($"{level}: {message}");
}

/// <summary>
/// Runs in the elevated copy of PassDPI.exe (started once with UAC). Copies binaries to
/// Program Files, gets the verified engine, creates the LocalSystem service and starts it.
/// </summary>
public static class Installer
{
    public static bool IsElevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <param name="ownerSid">SID of the non-elevated user who clicked Install (only they may use the pipe).</param>
    /// <param name="engineFolder">Optional existing GoodbyeDPI folder (only the pinned version is accepted).</param>
    public static async Task InstallAsync(string ownerSid, string? engineFolder, ILog log, CancellationToken ct)
    {
        if (!IsElevated()) throw new UnauthorizedAccessException("Administrator rights are required.");
        _ = new SecurityIdentifier(ownerSid); // throws on malformed SID

        var sourceDir = Path.GetDirectoryName(Environment.ProcessPath)!;
        var runningFromInstallDir = string.Equals(Path.GetFullPath(sourceDir).TrimEnd('\\'),
            Path.GetFullPath(AppPaths.InstallDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        var serviceBytes = runningFromInstallDir ? null : ReadServiceBinary(sourceDir)
            ?? throw new FileNotFoundException(
                $"{AppPaths.ServiceExeName} is neither embedded in {AppPaths.AppExeName} nor next to it.");

        // 1) Stop an existing service so its exe can be replaced; keep settings.
        var existed = ServiceControl.Exists(AppPaths.ServiceName);
        var wasAuto = existed && ServiceControl.IsAutoStart(AppPaths.ServiceName);
        if (existed)
        {
            log.Info("Stopping existing service");
            ServiceControl.Stop(AppPaths.ServiceName, TimeSpan.FromSeconds(20));
        }

        // 2) Protected folders.
        SecureDirectory.Ensure(AppPaths.InstallDir, usersCanRead: true);
        SecureDirectory.Ensure(AppPaths.DataDir, usersCanRead: false);

        // 3) Binaries (read into memory, then write: the bytes we copy are the bytes we read).
        if (!runningFromInstallDir)
        {
            WriteFile(File.ReadAllBytes(Environment.ProcessPath!), AppPaths.AppExePath);
            WriteFile(serviceBytes!, AppPaths.ServiceExePath);
            log.Info("Copied PassDPI binaries to " + AppPaths.InstallDir);
        }

        // 4) Engine.
        if (!string.IsNullOrWhiteSpace(engineFolder))
            EngineFiles.InstallFromFolder(engineFolder, AppPaths.EngineDir, log);
        else if (!EngineFiles.Verify(AppPaths.EngineDir, out _))
            await EngineFiles.InstallFromGitHubAsync(AppPaths.EngineDir, log, ct).ConfigureAwait(false);
        else
            log.Info("Engine already installed and verified.");

        // 5) Settings: record the owner, keep everything else.
        var store = new SettingsStore();
        var settings = store.LoadSettings();
        settings.OwnerSid = ownerSid;
        if (settings.Sites.Count == 0) settings.Sites = SiteValidator.Defaults();
        store.SaveSettings(settings);

        // 6) Service (LocalSystem). Fresh installs start with Windows by default.
        ServiceControl.InstallOrUpdate(AppPaths.ServiceName, AppPaths.ServiceDisplayName, AppPaths.ServiceDescription,
            AppPaths.ServiceExePath, autoStart: !existed || wasAuto, ownerSid);
        log.Info("Service registered");

        // 7) Start menu shortcut (all users).
        try { Shortcut.Create(AppPaths.StartMenuShortcut, AppPaths.AppExePath, "PassDPI"); }
        catch (Exception ex) { log.Warn($"Shortcut: {ex.Message}"); }

        ServiceControl.Start(AppPaths.ServiceName);
        log.Info("Service started");
    }

    /// <summary>Elevated removal, used when the service can't be asked to uninstall itself.</summary>
    public static void Uninstall(ILog log)
    {
        if (!IsElevated()) throw new UnauthorizedAccessException("Administrator rights are required.");
        try { ServiceControl.Stop(AppPaths.ServiceName, TimeSpan.FromSeconds(20)); } catch (Exception ex) { log.Warn(ex.Message); }

        var engineExe = Path.Combine(AppPaths.EngineDir, EngineManifest.ExeName);
        foreach (var p in Process.GetProcessesByName("goodbyedpi"))
        {
            try
            {
                if (string.Equals(p.MainModule?.FileName, engineExe, StringComparison.OrdinalIgnoreCase)) p.Kill();
            }
            catch { }
            finally { p.Dispose(); }
        }

        Core.Uninstall.RemoveOurWinDivertDriver(log);
        try { ServiceControl.Delete(AppPaths.ServiceName); } catch (Exception ex) { log.Warn(ex.Message); }
        Core.Uninstall.DeleteShortcut(log);
        Core.Uninstall.DeleteDataDir(log);
        Core.Uninstall.ScheduleInstallDirRemoval(log);
    }

    /// <summary>Resource name of the service binary embedded into PassDPI.exe by build/publish.ps1.</summary>
    public const string EmbeddedServiceResource = "PassDpi.Service.exe";

    /// <summary>
    /// The release ships as one PassDPI.exe with the (trimmed) service exe embedded in it, so it is
    /// extracted from our own image: the same trust as the running exe. Dev builds fall back to a sibling file.
    /// </summary>
    private static byte[]? ReadServiceBinary(string sourceDir)
    {
        var asm = System.Reflection.Assembly.GetEntryAssembly();
        using (var s = asm?.GetManifestResourceStream(EmbeddedServiceResource))
        {
            if (s != null)
            {
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }
        var sibling = Path.Combine(sourceDir, AppPaths.ServiceExeName);
        return File.Exists(sibling) ? File.ReadAllBytes(sibling) : null;
    }

    private static void WriteFile(byte[] bytes, string dst)
    {
        var tmp = dst + ".new";
        var old = dst + ".old";
        File.WriteAllBytes(tmp, bytes);
        try { if (File.Exists(old)) File.Delete(old); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        // A running exe can't be overwritten but can be renamed; move it aside first.
        if (File.Exists(dst))
        {
            try { File.Delete(dst); }
            catch (IOException) { File.Move(dst, old, overwrite: true); }
            catch (UnauthorizedAccessException) { File.Move(dst, old, overwrite: true); }
        }
        File.Move(tmp, dst, overwrite: true);
    }
}
