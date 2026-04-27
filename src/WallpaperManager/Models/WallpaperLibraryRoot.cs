namespace WallpaperManager.Models;

public sealed class WallpaperLibraryRoot
{
    public WallpaperLibraryRoot()
    {
    }

    public WallpaperLibraryRoot(string path)
    {
        Path = path;
    }

    public string Path { get; set; } = string.Empty;

    public string DisplayName => System.IO.Path.GetFileName(Path.TrimEnd(System.IO.Path.DirectorySeparatorChar)) is { Length: > 0 } name
        ? name
        : Path;

    public static WallpaperLibraryRoot FromPath(string path)
    {
        return new WallpaperLibraryRoot(System.IO.Path.GetFullPath(path));
    }
}
