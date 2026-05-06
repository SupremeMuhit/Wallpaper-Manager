namespace WallpaperManager.Models;

public sealed class LocalWallpaperMetadata
{
    public string? LocalName { get; set; }
    public List<string> LocalTags { get; set; } = [];
}

public sealed class LocalMetadataFile
{
    public Dictionary<string, LocalWallpaperMetadata> Wallpapers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
