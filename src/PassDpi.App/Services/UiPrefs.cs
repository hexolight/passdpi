using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PassDpi.Core;

namespace PassDpi.App.Services;

/// <summary>Per-user UI preferences (%LocalAppData%\PassDPI\ui.json). Not security relevant.</summary>
public sealed class UiPrefs
{
    public string? Language { get; set; }
    public string Theme { get; set; } = "dark";

    private static string FilePath => Path.Combine(AppPaths.UserPrefsDir, "ui.json");

    public static UiPrefs Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize(File.ReadAllText(FilePath), UiPrefsJson.Default.UiPrefs) ?? new UiPrefs();
        }
        catch { }
        return new UiPrefs();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.UserPrefsDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, UiPrefsJson.Default.UiPrefs));
        }
        catch { }
    }

    public static void DeleteAll()
    {
        try { if (Directory.Exists(AppPaths.UserPrefsDir)) Directory.Delete(AppPaths.UserPrefsDir, true); } catch { }
    }
}

[JsonSerializable(typeof(UiPrefs))]
internal partial class UiPrefsJson : JsonSerializerContext { }
