namespace WallpaperManager.Models;

public sealed class AppSettings
{
    public List<WallpaperLibraryRoot> WallpaperDirectories { get; set; } = [];

    public string EngineExecutablePath { get; set; } = string.Empty;

    public string Theme { get; set; } = ThemeOptions.System;

    public List<string> SelectedWallpaperKeys { get; set; } = [];

    public List<string> NsfwWallpaperKeys { get; set; } = [];

    public Dictionary<string, List<string>> WallpaperTags { get; set; } = [];

    public List<WallpaperTag> Tags { get; set; } =
    [
        new() { Name = "Favorite", Color = "#3A7AFE" }
    ];

    public bool ColorRowsByHighestPriorityTag { get; set; } = true;

    public bool ShowNsfwWallpapers { get; set; } = true;

    public List<string> HiddenLibraryColumns { get; set; } = [];

    public string LibraryViewMode { get; set; } = LibraryViewModes.List;

    public string HomeViewMode { get; set; } = LibraryViewModes.Thumbnail;

    public string CardSize { get; set; } = CardSizeOptions.Medium;
}
