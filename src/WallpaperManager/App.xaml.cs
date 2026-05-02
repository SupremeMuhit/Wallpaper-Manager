using Microsoft.UI.Xaml;

namespace WallpaperManager;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        UnhandledException += App_UnhandledException;
        _window = new MainWindow();
        _window.Activate();
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        var appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
        var logPath = System.IO.Path.Combine(appData, "WallpaperManager", "crash.txt");
        System.IO.File.AppendAllText(logPath, $"{System.DateTime.Now}: {e.Exception}\n\n");
    }
}
