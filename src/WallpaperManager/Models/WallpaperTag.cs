namespace WallpaperManager.Models;

public sealed class WallpaperTag
{
    public string Name { get; set; } = string.Empty;

    public string Color { get; set; } = "#3A7AFE";

    public Microsoft.UI.Xaml.Media.Brush ColorBrush => new Microsoft.UI.Xaml.Media.SolidColorBrush(ParseColor(Color));

    private static Windows.UI.Color ParseColor(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            value = "#3A7AFE";
        }

        value = value.Trim();
        if (value.StartsWith('#'))
        {
            value = value[1..];
        }

        if (value.Length != 6)
        {
            value = "3A7AFE";
        }

        return Windows.UI.Color.FromArgb(
            255,
            Convert.ToByte(value[..2], 16),
            Convert.ToByte(value.Substring(2, 2), 16),
            Convert.ToByte(value.Substring(4, 2), 16));
    }
}
