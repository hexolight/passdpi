using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PassDpi.App;

/// <summary>Swaps the color dictionary and keeps the native title bar in sync (dark/light).</summary>
public static partial class ThemeManager
{
    public static string Current { get; private set; } = "dark";

    public static void Apply(string theme)
    {
        Current = theme == "light" ? "light" : "dark";
        var dicts = Application.Current.Resources.MergedDictionaries;
        var uri = new Uri($"Themes/{(Current == "light" ? "Light" : "Dark")}.xaml", UriKind.Relative);
        var old = dicts.FirstOrDefault(d => d.Source?.OriginalString.StartsWith("Themes/", StringComparison.Ordinal) == true
                                            && !d.Source.OriginalString.EndsWith("Controls.xaml", StringComparison.Ordinal));
        var fresh = new ResourceDictionary { Source = uri };
        if (old != null) dicts[dicts.IndexOf(old)] = fresh; else dicts.Insert(0, fresh);

        foreach (Window w in Application.Current.Windows) ApplyTitleBar(w);
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

    public static void ApplyTitleBar(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == 0) return;
        var dark = Current == "dark" ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20 /*DWMWA_USE_IMMERSIVE_DARK_MODE*/, ref dark, sizeof(int));
    }
}
