using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WallpaperManager.Models;
using WallpaperManager.Services;

namespace WallpaperManager;

public static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var cmdArgs = Environment.GetCommandLineArgs();
        var actualArgs = cmdArgs.Length > 1 ? cmdArgs[1..] : [];

        // If launched with context-menu arguments, handle them directly
        // without loading the full WinUI framework (much faster).
        if (actualArgs.Length > 0 && (actualArgs[0] == "--next-wallpaper" || actualArgs[0] == "--switch-wallpaper"))
        {
            HandleContextMenuAction(actualArgs);
            return;
        }

        // Normal WinUI app launch
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }

    private static void Log(string message)
    {
        try
        {
            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WallpaperManager", "contextmenu-log.txt");
            File.AppendAllText(logPath, $"{DateTime.Now:HH:mm:ss.fff}: {message}\n");
        }
        catch { }
    }

    private static void HandleContextMenuAction(string[] args)
    {
        try
        {
            Log($"Starting action: {args[0]}");

            var store = new AppSettingsStore();
            var settings = store.LoadAsync().GetAwaiter().GetResult();

            if (string.IsNullOrWhiteSpace(settings.EngineExecutablePath))
            {
                Log("Error: Engine path is empty");
                return;
            }
            if (!File.Exists(settings.EngineExecutablePath))
            {
                Log($"Error: Engine does not exist at {settings.EngineExecutablePath}");
                return;
            }

            Log($"Engine path ok: {settings.EngineExecutablePath}");

            if (args[0] == "--next-wallpaper")
            {
                HandleNextWallpaper(settings);
            }
            else if (args[0] == "--switch-wallpaper" && args.Length >= 2)
            {
                HandleSwitchWallpaper(settings, args[1]);
            }
        }
        catch (Exception ex)
        {
            Log($"Exception in HandleContextMenuAction: {ex}");
        }
    }

    private static void HandleNextWallpaper(AppSettings settings)
    {
        Log("Scanning library for Next Wallpaper...");
        var scanner = new WallpaperScanner();
        var roots = string.IsNullOrWhiteSpace(settings.WallpaperDirectory)
            ? Array.Empty<WallpaperLibraryRoot>()
            : new[] { WallpaperLibraryRoot.FromPath(settings.WallpaperDirectory) };

        var allWallpapers = scanner.ScanAsync(
            roots,
            settings.SelectedWallpaperKeys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            settings.NsfwWallpaperKeys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            settings.MatureWallpaperKeys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, List<string>>()).GetAwaiter().GetResult();

        Log($"Found {allWallpapers.Count} total wallpapers. Filtering for list...");

        // Filter to selected list
        var candidates = GetWallpapersForList(settings, allWallpapers);
        if (candidates.Count == 0)
        {
            Log("Error: Candidates count is 0");
            return;
        }

        Log($"List has {candidates.Count} candidates. Mode is {settings.ContextMenuNextMode}");

        WallpaperItem chosen;

        if (settings.ContextMenuNextMode == "Serial")
        {
            // Serial: use a persisted index
            var indexPath = GetNextIndexPath();
            var currentIndex = 0;
            if (File.Exists(indexPath) && int.TryParse(File.ReadAllText(indexPath).Trim(), out var saved))
                currentIndex = saved;

            if (currentIndex >= candidates.Count) currentIndex = 0;
            chosen = candidates[currentIndex];
            File.WriteAllText(indexPath, (currentIndex + 1).ToString());
        }
        else
        {
            // Random (default)
            chosen = candidates[Random.Shared.Next(candidates.Count)];
        }

        Log($"Chosen wallpaper: {chosen.DisplayName} ({chosen.Key}) at {chosen.LaunchPath}");

        var engine = new WallpaperEngineService();
        if (!engine.IsRunning(settings.EngineExecutablePath))
        {
            Log("Engine is not running. Starting engine...");
            engine.StartEngine(settings.EngineExecutablePath);
            // Give the engine a moment to start
            Thread.Sleep(2000);
        }

        Log("Running wallpaper via engine...");
        bool success = engine.RunWallpaper(settings.EngineExecutablePath, chosen);
        Log($"Engine RunWallpaper returned {success}");
    }

    private static void HandleSwitchWallpaper(AppSettings settings, string wallpaperKey)
    {
        Log($"Scanning library for Switch Wallpaper to {wallpaperKey}...");
        var scanner = new WallpaperScanner();
        var roots = string.IsNullOrWhiteSpace(settings.WallpaperDirectory)
            ? Array.Empty<WallpaperLibraryRoot>()
            : new[] { WallpaperLibraryRoot.FromPath(settings.WallpaperDirectory) };

        var allWallpapers = scanner.ScanAsync(
            roots,
            settings.SelectedWallpaperKeys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            settings.NsfwWallpaperKeys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            settings.MatureWallpaperKeys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, List<string>>()).GetAwaiter().GetResult();

        var chosen = allWallpapers.FirstOrDefault(w => w.Key == wallpaperKey);
        if (chosen == null)
        {
            Log($"Error: Could not find wallpaper with key {wallpaperKey}");
            return;
        }

        Log($"Found wallpaper: {chosen.DisplayName} at {chosen.LaunchPath}");

        var engine = new WallpaperEngineService();
        if (!engine.IsRunning(settings.EngineExecutablePath))
        {
            Log("Engine is not running. Starting engine...");
            engine.StartEngine(settings.EngineExecutablePath);
            Thread.Sleep(2000);
        }

        Log("Running wallpaper via engine...");
        bool success = engine.RunWallpaper(settings.EngineExecutablePath, chosen);
        Log($"Engine RunWallpaper returned {success}");
    }

    private static List<WallpaperItem> GetWallpapersForList(
        AppSettings settings, IReadOnlyList<WallpaperItem> allWallpapers)
    {
        var selectedKeys = settings.SelectedWallpaperKeys;
        var selectedKeysSet = new HashSet<string>(selectedKeys);
        var homeWallpapers = allWallpapers.Where(w => selectedKeysSet.Contains(w.Key)).ToList();

        List<WallpaperItem> candidates;
        if (string.IsNullOrEmpty(settings.ContextMenuListId))
        {
            candidates = homeWallpapers;
            candidates = SortWallpapers(candidates, settings.HomeSortMode, selectedKeys);
        }
        else
        {
            var list = settings.WallpaperLists.FirstOrDefault(l => l.Id == settings.ContextMenuListId);
            if (list == null) return homeWallpapers;

            var listKeysSet = new HashSet<string>(list.WallpaperKeys);
            candidates = homeWallpapers.Where(w => listKeysSet.Contains(w.Key)).ToList();
            candidates = SortWallpapers(candidates, settings.HomeSortMode, list.WallpaperKeys);
        }

        return candidates;
    }

    private static List<WallpaperItem> SortWallpapers(List<WallpaperItem> items, string mode, List<string> freeMovementKeys)
    {
        return mode switch
        {
            "Free Movement" => items.OrderBy(w => freeMovementKeys.IndexOf(w.Key)).ToList(),
            "Date Added" => items.OrderByDescending(w => w.DateModified).ThenBy(w => w.DisplayName).ToList(),
            "Workshop Updated" => items.OrderByDescending(w => w.WorkshopMetadata?.TimeUpdated ?? DateTime.MinValue).ThenBy(w => w.DisplayName).ToList(),
            "Subscribers" => items.OrderByDescending(w => w.WorkshopMetadata?.SubscriptionCount ?? 0).ThenBy(w => w.DisplayName).ToList(),
            "Size" => items.OrderByDescending(w => w.SizeBytes).ThenBy(w => w.DisplayName).ToList(),
            _ => items.OrderBy(w => w.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList()
        };
    }

    private static string GetNextIndexPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "WallpaperManager", "next-index.txt");
    }
}
