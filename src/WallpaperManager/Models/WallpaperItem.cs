using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;

namespace WallpaperManager.Models;

public sealed class WallpaperItem
{
    public string Key => !string.IsNullOrWhiteSpace(SteamId)
        ? SteamId
        : DirectoryPath;

    public string DirectoryPath { get; set; } = string.Empty;

    public string PreviewPath { get; set; } = string.Empty;

    public string LaunchPath { get; set; } = string.Empty;

    public string LocalName { get; set; } = string.Empty;

    public string SteamId { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public bool IsSelected { get; set; }

    public bool IsHidden { get; set; }

    public bool IsNsfw { get; set; }

    public List<string> Tags { get; set; } = [];

    public Brush? RowBackground { get; set; }

    public double CardWidth { get; set; } = 250;

    public double CardPreviewHeight { get; set; } = 132;

    public double ListPreviewWidth { get; set; } = 88;

    public double ListPreviewHeight { get; set; } = 52;

    public double ListRowMinHeight { get; set; } = 72;

    public double ListTitleFontSize { get; set; } = 15;

    public Thickness ListRowPadding { get; set; } = new(12, 8, 12, 8);

    public Visibility ListPreviewVisibility { get; set; } = Visibility.Visible;

    public Visibility DirectHomeActionVisibility { get; set; } = Visibility.Visible;

    public Visibility ListDetailsVisibility { get; set; } = Visibility.Visible;

    public Visibility ThumbnailDetailsVisibility { get; set; } = Visibility.Visible;

    public Visibility LargeListLayoutVisibility { get; set; } = Visibility.Collapsed;

    public Visibility CompactListLayoutVisibility { get; set; } = Visibility.Visible;

    public double PreviewOpacity => IsNsfw ? 0.16 : 1;

    public Visibility NsfwOverlayVisibility => IsNsfw ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PreviewColumnVisibility { get; set; } = Visibility.Visible;

    public Visibility LocalNameColumnVisibility { get; set; } = Visibility.Visible;

    public Visibility IdColumnVisibility { get; set; } = Visibility.Visible;

    public Visibility SizeColumnVisibility { get; set; } = Visibility.Visible;

    public Visibility TagsColumnVisibility { get; set; } = Visibility.Visible;

    public GridLength PreviewColumnWidth { get; set; } = new(104);

    public GridLength LocalNameColumnWidth { get; set; } = new(2, GridUnitType.Star);

    public GridLength IdColumnWidth { get; set; } = new(140);

    public GridLength SizeColumnWidth { get; set; } = new(110);

    public GridLength TagsColumnWidth { get; set; } = new(160);

    public string DisplayName => LocalName;

    public string IdText => string.IsNullOrWhiteSpace(SteamId) ? "Local" : SteamId;

    public string SizeText => FormatSize(SizeBytes);

    public string TagsText => Tags.Count == 0 ? string.Empty : string.Join(", ", Tags);

    public string HomeActionGlyph => IsSelected ? "\uE738" : "\uE710";

    public BitmapImage? PreviewImage => string.IsNullOrWhiteSpace(PreviewPath) || !File.Exists(PreviewPath)
        ? null
        : new BitmapImage(new Uri(PreviewPath));

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)bytes;
        var unit = 0;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} {units[unit]}" : $"{size:0.##} {units[unit]}";
    }
}
