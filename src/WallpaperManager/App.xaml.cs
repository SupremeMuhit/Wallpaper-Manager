using Microsoft.UI.Xaml;
using WallpaperManager.Services;

namespace WallpaperManager;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        UnhandledException += App_UnhandledException;

        // Handle context-menu command-line actions (--next-wallpaper, --switch-wallpaper)
        var cmdArgs = Environment.GetCommandLineArgs();
        // Skip the first element (exe path) to get the actual arguments
        var actualArgs = cmdArgs.Length > 1 ? cmdArgs[1..] : [];

        if (actualArgs.Length > 0)
        {
            try
            {
                var handled = await ContextMenuService.HandleCommandLineAsync(actualArgs);
                if (handled)
                {
                    // Action completed — exit without showing the window
                    Exit();
                    return;
                }
            }
            catch (Exception ex)
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var logPath = System.IO.Path.Combine(appData, "WallpaperManager", "contextmenu-error.txt");
                System.IO.File.AppendAllText(logPath, $"{DateTime.Now}: {ex}\n\n");
            }
        }

        _window = new MainWindow();
        _window.Activate();
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var logPath = System.IO.Path.Combine(appData, "WallpaperManager", "crash.txt");
        System.IO.File.AppendAllText(logPath, $"{DateTime.Now}: {e.Exception}\n\n");
    }
}
