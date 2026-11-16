using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using PassDpi.Core;
using PassDpi.Core.Validation;

namespace PassDpi.Settings;

/// <summary>
/// Loads/saves settings and per-network memory as JSON under ProgramData\PassDPI.
/// Writes are atomic (temp file + replace) so a crash never leaves a half-written file.
/// </summary>
public sealed class SettingsStore
{
    private readonly string _settingsPath;
    private readonly string _networksPath;
    private readonly object _gate = new();

    public SettingsStore(string? settingsPath = null, string? networksPath = null)
    {
        _settingsPath = settingsPath ?? AppPaths.SettingsFile;
        _networksPath = networksPath ?? AppPaths.NetworksFile;
    }

    public ServiceSettings LoadSettings()
    {
        lock (_gate)
        {
            var s = Read(_settingsPath, SettingsJsonContext.Default.ServiceSettings) ?? new ServiceSettings();
            // Never trust what's on disk blindly: re-validate sites, fall back to defaults.
            s.Sites = SiteValidator.Normalize(s.Sites, out _) ?? SiteValidator.Defaults();
            return s;
        }
    }

    public void SaveSettings(ServiceSettings s)
    {
        lock (_gate) Write(_settingsPath, s, SettingsJsonContext.Default.ServiceSettings);
    }

    public NetworkMemoryFile LoadNetworks()
    {
        lock (_gate) return Read(_networksPath, SettingsJsonContext.Default.NetworkMemoryFile) ?? new NetworkMemoryFile();
    }

    public void SaveNetworks(NetworkMemoryFile n)
    {
        lock (_gate)
        {
            // Keep the file small: remember the 50 most recently tested networks.
            if (n.Networks.Count > 50)
                n.Networks = n.Networks.OrderByDescending(kv => kv.Value.TestedUtc).Take(50)
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
            Write(_networksPath, n, SettingsJsonContext.Default.NetworkMemoryFile);
        }
    }

    private static T? Read<T>(string path, JsonTypeInfo<T> info) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            var fi = new FileInfo(path);
            if (fi.Length > 1024 * 1024) return null;
            return JsonSerializer.Deserialize(File.ReadAllText(path), info);
        }
        catch (Exception) { return null; }
    }

    private static void Write<T>(string path, T value, JsonTypeInfo<T> info)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, info));
        File.Move(tmp, path, overwrite: true);
    }
}
