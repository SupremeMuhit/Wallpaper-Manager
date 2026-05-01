using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace WallpaperManager.Models;

public sealed class WallpaperItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public string Key => !string.IsNullOrWhiteSpace(SteamId)
        ? SteamId
        : DirectoryPath;

    public string DirectoryPath { get; set; } = string.Empty;

    public string PreviewPath { get; set; } = string.Empty;

    public string LaunchPath { get; set; } = string.Empty;

    public string LocalName { get; set; } = string.Empty;

    public string SteamId { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

    private bool _isNsfw;
    public bool IsNsfw
    {
        get => _isNsfw;
        set { if (_isNsfw != value) { _isNsfw = value; OnPropertyChanged(); } }
    }

    private bool _isMature;
    public bool IsMature
    {
        get => _isMature;
        set { if (_isMature != value) { _isMature = value; OnPropertyChanged(); } }
    }

    private WorkshopMetadata? _workshopMetadata;
    public WorkshopMetadata? WorkshopMetadata
    {
        get => _workshopMetadata;
        set
        {
            if (_workshopMetadata != value)
            {
                _workshopMetadata = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(TagsText));
            }
        }
    }

    public List<string> Tags { get; set; } = [];

    private bool _prioritizeWorkshopName;
    public bool PrioritizeWorkshopName
    {
        get => _prioritizeWorkshopName;
        set { if (_prioritizeWorkshopName != value) { _prioritizeWorkshopName = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); } }
    }

    private bool _useWorkshopTags;
    public bool UseWorkshopTags
    {
        get => _useWorkshopTags;
        set { if (_useWorkshopTags != value) { _useWorkshopTags = value; OnPropertyChanged(); OnPropertyChanged(nameof(TagsText)); } }
    }

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

    private double _blurOpacity = 1.0;
    /// <summary>Opacity applied to the preview image (controlled by censorship settings).</summary>
    public double BlurOpacity
    {
        get => _blurOpacity;
        set { if (_blurOpacity != value) { _blurOpacity = value; OnPropertyChanged(); } }
    }

    private double _censorshipOverlayOpacity = 0.6;
    public double CensorshipOverlayOpacity
    {
        get => _censorshipOverlayOpacity;
        set { if (_censorshipOverlayOpacity != value) { _censorshipOverlayOpacity = value; OnPropertyChanged(); } }
    }

    private Visibility _nsfwOverlayVisibility = Visibility.Collapsed;
    /// <summary>Overlay badge visibility for NSFW wallpapers.</summary>
    public Visibility NsfwOverlayVisibility
    {
        get => _nsfwOverlayVisibility;
        set { if (_nsfwOverlayVisibility != value) { _nsfwOverlayVisibility = value; OnPropertyChanged(); } }
    }

    private Visibility _matureOverlayVisibility = Visibility.Collapsed;
    /// <summary>Overlay badge visibility for Mature wallpapers (not NSFW).</summary>
    public Visibility MatureOverlayVisibility
    {
        get => _matureOverlayVisibility;
        set { if (_matureOverlayVisibility != value) { _matureOverlayVisibility = value; OnPropertyChanged(); } }
    }

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

    public string DisplayName => PrioritizeWorkshopName && WorkshopMetadata != null && !string.IsNullOrWhiteSpace(WorkshopMetadata.Title)
        ? WorkshopMetadata.Title
        : LocalName;

    public string IdText => string.IsNullOrWhiteSpace(SteamId) ? "Local" : SteamId;

    public string SizeText => FormatSize(SizeBytes);

    public string TagsText
    {
        get
        {
            var displayTags = new List<string>(Tags);
            if (UseWorkshopTags && WorkshopMetadata != null)
            {
                displayTags.AddRange(WorkshopMetadata.Tags);
                displayTags = displayTags.Distinct().ToList();
            }
            return displayTags.Count == 0 ? string.Empty : string.Join(", ", displayTags);
        }
    }

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
