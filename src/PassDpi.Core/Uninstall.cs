using System.Diagnostics;
using PassDpi.Core.Native;

namespace PassDpi.Core;

/// <summary>
/// Removal steps shared by the service ("Uninstall" pipe command) and the elevated setup
/// (when the service isn't running). Only touches PassDPI's fixed paths.
/// </summary>
public static class Uninstall
{
    /// <summary>Removes the WinDivert kernel driver service, but only if it was loaded from our folder.</summary>
    public static void RemoveOurWinDivertDriver(ILog log)
    {
        foreach (var name in new[] { "WinDivert", "WinDivert14" })
        {
            try
            {
                var bin = ServiceControl.GetBinaryPath(name);
                if (bin == null) continue;
                // Kernel driver paths look like "\??\C:\Program Files\PassDPI\engine\WinDivert64.sys".
                if (bin.Contains(AppPaths.InstallDir + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    ServiceControl.Stop(name, TimeSpan.FromSeconds(5));
                    ServiceControl.Delete(name);
                    log.Info($"Removed driver service {name}");
                }
                else log.Info($"Driver service {name} belongs to another program ({bin}); left untouched.");
            }
            catch (Exception ex) { log.Warn($"Driver cleanup ({name}): {ex.Message}"); }
        }
    }

    public static void DeleteDataDir(ILog log)
    {
        TryDeleteDir(AppPaths.DataDir, log);
    }

    public static void DeleteShortcut(ILog log)
    {
        try { if (File.Exists(AppPaths.StartMenuShortcut)) File.Delete(AppPaths.StartMenuShortcut); }
        catch (Exception ex) { log.Warn($"Shortcut: {ex.Message}"); }
    }

    /// <summary>
    /// The install folder holds the running exe(s), so it is removed by a detached cmd.exe
    /// after we exit. The path is a compile-time constant location, never user input.
    /// </summary>
    public static void ScheduleInstallDirRemoval(ILog log)
    {
        var dir = Path.GetFullPath(AppPaths.InstallDir);
        // Safety net: only ever delete "<Program Files>\PassDPI".
        if (!dir.EndsWith(@"\PassDPI", StringComparison.OrdinalIgnoreCase) ||
            !dir.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase))
        {
            log.Error($"Refusing to delete unexpected path {dir}");
            return;
        }
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var ping = Path.Combine(Environment.SystemDirectory, "PING.EXE");
        var script = $"for /l %i in (1,1,15) do (rmdir /s /q \"{dir}\" 2>nul & if not exist \"{dir}\" exit /b 0 & \"{ping}\" -n 2 127.0.0.1 >nul)";
        // cmd.exe does not follow the MSVCRT quoting rules ArgumentList produces, so build the raw
        // command line: /c "<script>" — cmd strips exactly the first and last quote.
        var psi = new ProcessStartInfo(cmd, "/d /c \"" + script + "\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Environment.SystemDirectory,
        };
        Process.Start(psi)?.Dispose();
        log.Info("Scheduled removal of the install folder.");
    }

    public static void TryDeleteDir(string dir, ILog log)
    {
        for (var i = 0; i < 5; i++)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (i == 4) log.Warn($"Could not delete {dir}: {ex.Message}");
                Thread.Sleep(300);
            }
        }
    }
}
