namespace PassDpi.Core;

/// <summary>
/// Every on-disk location PassDPI uses. These are fixed and never come from
/// user input or the pipe, so the SYSTEM service can only ever touch these folders.
/// </summary>
public static class AppPaths
{
    public const string ServiceName = "PassDPI";
    public const string ServiceDisplayName = "PassDPI";
    public const string ServiceDescription = "Runs GoodbyeDPI with the profile that works on the current network. Controlled by the PassDPI app.";
    public const string PipeName = "PassDPI.Control.v1";

    public const string ServiceExeName = "PassDpi.Service.exe";
    public const string AppExeName = "PassDPI.exe";

    /// <summary>C:\Program Files\PassDPI — binaries, writable only by Administrators/SYSTEM.</summary>
    public static string InstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PassDPI");

    /// <summary>C:\Program Files\PassDPI\engine — goodbyedpi.exe + WinDivert.</summary>
    public static string EngineDir => Path.Combine(InstallDir, "engine");

    public static string ServiceExePath => Path.Combine(InstallDir, ServiceExeName);
    public static string AppExePath => Path.Combine(InstallDir, AppExeName);

    /// <summary>C:\ProgramData\PassDPI — settings, network memory, logs (protected ACL).</summary>
    public static string DataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PassDPI");

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string NetworksFile => Path.Combine(DataDir, "networks.json");
    public static string SaltFile => Path.Combine(DataDir, "salt.bin");
    public static string LogDir => Path.Combine(DataDir, "logs");

    /// <summary>Per-user UI preferences (language/theme). Not security relevant.</summary>
    public static string UserPrefsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PassDPI");

    public static string StartMenuShortcut =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "PassDPI.lnk");
}
