namespace WallpaperManager.Models;

public static class CardSizeOptions
{
    public const string Small = "Small";
    public const string Medium = "Medium";
    public const string Large = "Large";

    public static readonly IReadOnlyList<string> All = [Small, Medium, Large];
}
