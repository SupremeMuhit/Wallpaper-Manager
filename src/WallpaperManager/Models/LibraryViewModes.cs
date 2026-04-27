namespace WallpaperManager.Models;

public static class LibraryViewModes
{
    public const string List = "List";
    public const string Thumbnail = "Thumbnail";

    public static readonly IReadOnlyList<string> All = [List, Thumbnail];
}
