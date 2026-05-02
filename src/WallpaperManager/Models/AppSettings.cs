namespace WallpaperManager.Models;

public enum CensorshipMode
{
    Off,
    Blur,
    Overlay
}

public sealed class AppSettings
{
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

    public string MemoryUsageProfile { get; set; } = "Balanced";

    public bool PrioritizeWorkshopName { get; set; }

    public bool AutoMarkNsfwFromWorkshop { get; set; } = true;

    public CensorshipMode NsfwMode { get; set; } = CensorshipMode.Blur;

    public CensorshipMode MatureMode { get; set; } = CensorshipMode.Overlay;

    public double BlurIntensity { get; set; } = 40.0;

    public double OverlayOpacity { get; set; } = 0.5;
    public bool RemoveCensorOnHover { get; set; } = true;

    public bool UseWorkshopTags { get; set; }

    public List<string> HiddenLibraryColumns { get; set; } = [];

    public string LibraryViewMode { get; set; } = LibraryViewModes.List;

    public string HomeViewMode { get; set; } = LibraryViewModes.Thumbnail;

    public string CardSize { get; set; } = CardSizeOptions.Medium;

    public string LibrarySortMode { get; set; } = "Name";

    public string HomeSortMode { get; set; } = "Free Movement";
}

public enum NsfwTabMode
{
    Off,
    OnlyNsfw,
    NsfwAndMature
}
