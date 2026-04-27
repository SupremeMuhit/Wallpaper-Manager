namespace WallpaperManager.Models;

public static class ThemeOptions
{
    public const string System = "System";
    public const string Light = "Light";
    public const string Dark = "Dark";

    public static readonly IReadOnlyList<string> All = [System, Light, Dark];
}
