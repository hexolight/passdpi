using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PassDpi.App.Services;
using PassDpi.App.ViewModels;

namespace PassDpi.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(UiPrefs prefs)
    {
        InitializeComponent();
        _vm = new MainViewModel(prefs);
        DataContext = _vm;
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Loaded += async (_, _) => await _vm.InitializeAsync();
        // Closing the window ends the process entirely (no tray icon, nothing left in RAM).
        Closed += (_, _) => _vm.Dispose();
    }

#if DEBUG
    /// <summary>Developer aid: renders the window to a PNG (used for UI checks without screen capture).</summary>
    public async Task SnapshotAsync(string path, int tab, int delayMs)
    {
        _vm.SelectedTab = tab;
        await Task.Delay(delayMs);
        var root = (FrameworkElement)Content;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)(root.ActualWidth * dpi.DpiScaleX), (int)(root.ActualHeight * dpi.DpiScaleY),
            96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, System.Windows.Media.PixelFormats.Pbgra32);
        var dv = new System.Windows.Media.DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Background, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            dc.DrawRectangle(new System.Windows.Media.VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        }
        rtb.Render(dv);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        using var fs = System.IO.File.Create(path);
        enc.Save(fs);
    }
#endif

    private void LogChanged(object sender, TextChangedEventArgs e) => LogBox.ScrollToEnd();

    private void NewSiteKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _vm.AddSiteCommand.CanExecute(null)) _vm.AddSiteCommand.Execute(null);
    }

    private void SiteChanged(object sender, RoutedEventArgs e) => _vm.SitesDirty = true;
}
