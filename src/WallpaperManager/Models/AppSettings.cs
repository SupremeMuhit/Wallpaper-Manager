namespace WallpaperManager.Models;

public enum CensorshipMode
{
    Off,
    Blur,
    Overlay
}

public sealed class AppSettings
{
    /// <summary>Single wallpaper directory path (replaces the old multi-directory list).</summary>
    public string WallpaperDirectory { get; set; } = string.Empty;

    /// <summary>Legacy multi-directory list kept for migration only. Not used at runtime.</summary>
    public List<WallpaperLibraryRoot> WallpaperDirectories { get; set; } = [];

    public string EngineExecutablePath { get; set; } = string.Empty;

    public string Theme { get; set; } = ThemeOptions.System;

    public string ThemeColor { get; set; } = "#3A7AFE";

    public List<string> SelectedWallpaperKeys { get; set; } = [];

    public List<string> NsfwWallpaperKeys { get; set; } = [];

    public List<string> MatureWallpaperKeys { get; set; } = [];

    public Dictionary<string, List<string>> WallpaperTags { get; set; } = [];

    public List<WallpaperTag> Tags { get; set; } =
    [
        new() { Name = "Favorite", Color = "#3A7AFE" }
    ];

    public bool ColorRowsByHighestPriorityTag { get; set; } = true;

    public NsfwTabMode NsfwTabMode { get; set; } = NsfwTabMode.Off;

    public bool UseMicaBackdrop { get; set; } = true;

    public bool RunOnStartup { get; set; }

    public bool QuitToTray { get; set; } = true;

    public string MemoryUsageProfile { get; set; } = "Balanced";

    public bool DontAutoMarkNsfwFromWorkshop { get; set; } = false;

    public CensorshipMode NsfwMode { get; set; } = CensorshipMode.Blur;

    public CensorshipMode MatureMode { get; set; } = CensorshipMode.Overlay;

    public double BlurIntensity { get; set; } = 40.0;

    public double OverlayOpacity { get; set; } = 0.5;
    public bool RemoveCensorOnHover { get; set; } = true;


    public List<string> HiddenLibraryColumns { get; set; } = [];

    public string LibraryViewMode { get; set; } = LibraryViewModes.List;

    public string HomeViewMode { get; set; } = LibraryViewModes.Thumbnail;

    public string CardSize { get; set; } = CardSizeOptions.Medium;

    public string LibrarySortMode { get; set; } = "Name";

    public string HomeSortMode { get; set; } = "Free Movement";
    public LibraryHideMode LibraryHideMode { get; set; } = LibraryHideMode.Off;
    public List<string> CardButtons { get; set; } = ["ThreeDot", "AddToHome", "AddLocalName", "Details"];

    public bool IsDevMode { get; set; }
    
    // ── Tag Settings ───────────────────────────────────────────────────
    public bool UseWorkshopTags { get; set; } = true;
    public bool ConsiderSubdirectoryAsTag { get; set; } = false;

    // ── Wallpaper Lists ────────────────────────────────────────────────
    public List<WallpaperList> WallpaperLists { get; set; } = [];

    // ── Desktop Context Menu Integration ───────────────────────────────
    public bool ContextMenuNextWallpaper { get; set; }
    public bool ContextMenuSwitchWallpaper { get; set; }
    /// <summary>"Random" or "Serial" — controls how "Next Desktop Wallpaper" picks the next one.</summary>
    public string ContextMenuNextMode { get; set; } = "Random";
    /// <summary>Empty string means "Entire Home". Otherwise holds the list Id.</summary>
    public string ContextMenuListId { get; set; } = string.Empty;
    public string GlobalShortcut { get; set; } = "None";
    public string CurrentlyPlayingWallpaperKey { get; set; } = string.Empty;
}

/// <summary>A named subset of wallpapers in the Home view.</summary>
public class WallpaperList
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public List<string> WallpaperKeys { get; set; } = [];
}


public static class CardButtonIds
{
    public const string ThreeDot = "ThreeDot";
    public const string AddTag = "AddTag";
    public const string AddLocalName = "AddLocalName";
    public const string AddToHome = "AddToHome";
    public const string Delete = "Delete";
    public const string Details = "Details";
}

public class CardButtonInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Glyph { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
}

public enum LibraryHideMode
{
    Off,
    OnlyNsfw,
    NsfwAndMature
}

public enum NsfwTabMode
{
    Off,
    OnlyNsfw,
    NsfwAndMature
}
