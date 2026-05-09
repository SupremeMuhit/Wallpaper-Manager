using System.Diagnostics;
using Microsoft.Win32;
using WallpaperManager.Models;

namespace WallpaperManager.Services;

/// <summary>
/// Manages Windows desktop right-click context menu entries via the registry.
/// Entries are created under HKCU\Software\Classes\DesktopBackground\Shell.
/// </summary>
public sealed class ContextMenuService
{
    private const string ShellBase = @"Software\Classes\DesktopBackground\Shell";
    private const string NextKey = "CarbonNextWallpaper";
    private const string SwitchKey = "CarbonSwitchWallpaper";

    private static string GetAppExePath()
    {
        return Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
    }

    // ── Next Desktop Wallpaper ──────────────────────────────────────────

    public static void RegisterNextWallpaper()
    {
        var exePath = GetAppExePath();
        if (string.IsNullOrEmpty(exePath)) return;

        using var shellKey = Registry.CurrentUser.CreateSubKey($@"{ShellBase}\{NextKey}");
        shellKey.SetValue(string.Empty, "Next Desktop Wallpaper");
        shellKey.SetValue("Icon", $"\"{exePath}\",0");

        using var cmdKey = shellKey.CreateSubKey("command");
        cmdKey.SetValue(string.Empty, $"\"{exePath}\" --next-wallpaper");
    }

    public static void UnregisterNextWallpaper()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"{ShellBase}\{NextKey}", false);
        }
        catch { /* key doesn't exist — fine */ }
    }

    // ── Switch Desktop Wallpaper (cascading submenu) ────────────────────

    public static void RegisterSwitchWallpaper(IReadOnlyList<WallpaperItem> wallpapers)
    {
        var exePath = GetAppExePath();
        if (string.IsNullOrEmpty(exePath)) return;

        // Remove old entries first
        UnregisterSwitchWallpaper();

        using var shellKey = Registry.CurrentUser.CreateSubKey($@"{ShellBase}\{SwitchKey}");
        shellKey.SetValue("MUIVerb", "Switch Desktop Wallpaper");
        shellKey.SetValue("Icon", $"\"{exePath}\",0");
        shellKey.SetValue("SubCommands", string.Empty);

        // Create the cascading Shell subkeys
        using var subShell = shellKey.CreateSubKey("Shell");

        for (int i = 0; i < wallpapers.Count; i++)
        {
            var wp = wallpapers[i];
            var safeKey = SanitizeRegistryKeyName(wp.Key, i);

            using var itemKey = subShell.CreateSubKey(safeKey);
            itemKey.SetValue("MUIVerb", wp.DisplayName);

            using var cmdKey = itemKey.CreateSubKey("command");
            cmdKey.SetValue(string.Empty, $"\"{exePath}\" --switch-wallpaper \"{wp.Key}\"");
        }
    }

    public static void UnregisterSwitchWallpaper()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"{ShellBase}\{SwitchKey}", false);
        }
        catch { /* key doesn't exist — fine */ }
    }

    // ── Bulk apply based on current settings ────────────────────────────

    public static void ApplySettings(AppSettings settings, IReadOnlyList<WallpaperItem> allWallpapers)
    {
        // Next wallpaper
        if (settings.ContextMenuNextWallpaper)
            RegisterNextWallpaper();
        else
            UnregisterNextWallpaper();

        // Switch wallpaper
        if (settings.ContextMenuSwitchWallpaper)
        {
            var wallpapers = GetWallpapersForList(settings, allWallpapers);
            RegisterSwitchWallpaper(wallpapers);
        }
        else
        {
            UnregisterSwitchWallpaper();
        }
    }

    public static void RemoveAll()
    {
        UnregisterNextWallpaper();
        UnregisterSwitchWallpaper();
    }

    // ── Command-line handling (called from App.xaml.cs) ──────────────────

    /// <summary>
    /// Processes context menu command-line arguments.
    /// Returns true if an argument was handled (app should exit silently).
    /// </summary>
    public static async Task<bool> HandleCommandLineAsync(string[] args)
    {
        if (args.Length == 0) return false;

        var store = new AppSettingsStore();
        var settings = await store.LoadAsync();

        if (args[0] == "--next-wallpaper")
        {
            await HandleNextWallpaperAsync(settings);
            return true;
        }

        if (args[0] == "--switch-wallpaper" && args.Length >= 2)
        {
            var wallpaperKey = args[1];
            await HandleSwitchWallpaperAsync(settings, wallpaperKey);
            return true;
        }

        return false;
    }

    private static async Task HandleNextWallpaperAsync(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.EngineExecutablePath) ||
            !File.Exists(settings.EngineExecutablePath))
            return;

        // Build the list of wallpapers from disk
        var scanner = new WallpaperScanner();
        var roots = string.IsNullOrWhiteSpace(settings.WallpaperDirectory)
            ? Array.Empty<WallpaperLibraryRoot>()
            : new[] { WallpaperLibraryRoot.FromPath(settings.WallpaperDirectory) };

        var allWallpapers = await scanner.ScanAsync(
            roots,
            settings.SelectedWallpaperKeys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            settings.NsfwWallpaperKeys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            settings.MatureWallpaperKeys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, List<string>>());

        if (allWallpapers.Count == 0) return;

        // Filter to the selected list
        var candidates = GetWallpapersForList(settings, allWallpapers);
        if (candidates.Count == 0) return;

        // Pick a random wallpaper
        var rng = new Random();
        var chosen = candidates[rng.Next(candidates.Count)];

        var engine = new WallpaperEngineService();
        if (!engine.IsRunning(settings.EngineExecutablePath))
            engine.StartEngine(settings.EngineExecutablePath);

        engine.RunWallpaper(settings.EngineExecutablePath, chosen);
    }

    private static async Task HandleSwitchWallpaperAsync(AppSettings settings, string wallpaperKey)
    {
        if (string.IsNullOrWhiteSpace(settings.EngineExecutablePath) ||
            !File.Exists(settings.EngineExecutablePath))
            return;

        var scanner = new WallpaperScanner();
        var roots = string.IsNullOrWhiteSpace(settings.WallpaperDirectory)
            ? Array.Empty<WallpaperLibraryRoot>()
            : new[] { WallpaperLibraryRoot.FromPath(settings.WallpaperDirectory) };

        var allWallpapers = await scanner.ScanAsync(
            roots,
            settings.SelectedWallpaperKeys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            settings.NsfwWallpaperKeys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            settings.MatureWallpaperKeys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, List<string>>());

        var chosen = allWallpapers.FirstOrDefault(w => w.Key == wallpaperKey);
        if (chosen == null) return;

        var engine = new WallpaperEngineService();
        if (!engine.IsRunning(settings.EngineExecutablePath))
            engine.StartEngine(settings.EngineExecutablePath);

        engine.RunWallpaper(settings.EngineExecutablePath, chosen);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static List<WallpaperItem> GetWallpapersForList(
        AppSettings settings, IReadOnlyList<WallpaperItem> allWallpapers)
    {
        // Mark which ones are selected (in Home)
        var selectedKeys = new HashSet<string>(settings.SelectedWallpaperKeys);
        var homeWallpapers = allWallpapers.Where(w => selectedKeys.Contains(w.Key)).ToList();

        if (string.IsNullOrEmpty(settings.ContextMenuListId))
        {
            // "Entire Home"
            return homeWallpapers;
        }

        // Specific list
        var list = settings.WallpaperLists.FirstOrDefault(l => l.Id == settings.ContextMenuListId);
        if (list == null) return homeWallpapers;

        var listKeys = new HashSet<string>(list.WallpaperKeys);
        return homeWallpapers.Where(w => listKeys.Contains(w.Key)).ToList();
    }

    private static string SanitizeRegistryKeyName(string key, int index)
    {
        // Registry subkey names can't contain backslashes
        var safe = key.Replace("\\", "_").Replace("/", "_");
        // Prefix with index to maintain ordering
        return $"{index:D4}_{safe}";
    }
}
