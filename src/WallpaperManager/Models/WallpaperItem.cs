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

    public DateTime DateModified { get; set; }

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

    private bool _isHovered;
    public bool IsHovered
    {
        get => _isHovered;
        set { if (_isHovered != value) { _isHovered = value; OnPropertyChanged(); } }
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

    private Brush? _rowBackground;
    public Brush? RowBackground
    {
        get => _rowBackground;
        set { if (_rowBackground != value) { _rowBackground = value; OnPropertyChanged(); } }
    }

    private double _cardWidth = 250;
    public double CardWidth
    {
        get => _cardWidth;
        set { if (_cardWidth != value) { _cardWidth = value; OnPropertyChanged(); } }
    }

    private double _cardPreviewHeight = 132;
    public double CardPreviewHeight
    {
        get => _cardPreviewHeight;
        set { if (_cardPreviewHeight != value) { _cardPreviewHeight = value; OnPropertyChanged(); } }
    }

    private double _listPreviewWidth = 88;
    public double ListPreviewWidth
    {
        get => _listPreviewWidth;
        set { if (_listPreviewWidth != value) { _listPreviewWidth = value; OnPropertyChanged(); } }
    }

    private double _listPreviewHeight = 52;
    public double ListPreviewHeight
    {
        get => _listPreviewHeight;
        set { if (_listPreviewHeight != value) { _listPreviewHeight = value; OnPropertyChanged(); } }
    }

    private double _listRowMinHeight = 72;
    public double ListRowMinHeight
    {
        get => _listRowMinHeight;
        set { if (_listRowMinHeight != value) { _listRowMinHeight = value; OnPropertyChanged(); } }
    }

    private double _listTitleFontSize = 15;
    public double ListTitleFontSize
    {
        get => _listTitleFontSize;
        set { if (_listTitleFontSize != value) { _listTitleFontSize = value; OnPropertyChanged(); } }
    }

    private Thickness _listRowPadding = new(12, 8, 12, 8);
    public Thickness ListRowPadding
    {
        get => _listRowPadding;
        set { if (_listRowPadding != value) { _listRowPadding = value; OnPropertyChanged(); } }
    }

    private Visibility _listPreviewVisibility = Visibility.Visible;
    public Visibility ListPreviewVisibility
    {
        get => _listPreviewVisibility;
        set { if (_listPreviewVisibility != value) { _listPreviewVisibility = value; OnPropertyChanged(); } }
    }

    private Visibility _directHomeActionVisibility = Visibility.Visible;
    public Visibility DirectHomeActionVisibility
    {
        get => _directHomeActionVisibility;
        set { if (_directHomeActionVisibility != value) { _directHomeActionVisibility = value; OnPropertyChanged(); } }
    }

    private Visibility _listDetailsVisibility = Visibility.Visible;
    public Visibility ListDetailsVisibility
    {
        get => _listDetailsVisibility;
        set { if (_listDetailsVisibility != value) { _listDetailsVisibility = value; OnPropertyChanged(); } }
    }

    private Visibility _thumbnailDetailsVisibility = Visibility.Visible;
    public Visibility ThumbnailDetailsVisibility
    {
        get => _thumbnailDetailsVisibility;
        set { if (_thumbnailDetailsVisibility != value) { _thumbnailDetailsVisibility = value; OnPropertyChanged(); } }
    }

    private Visibility _largeListLayoutVisibility = Visibility.Collapsed;
    public Visibility LargeListLayoutVisibility
    {
        get => _largeListLayoutVisibility;
        set { if (_largeListLayoutVisibility != value) { _largeListLayoutVisibility = value; OnPropertyChanged(); } }
    }

    private Visibility _compactListLayoutVisibility = Visibility.Visible;
    public Visibility CompactListLayoutVisibility
    {
        get => _compactListLayoutVisibility;
        set { if (_compactListLayoutVisibility != value) { _compactListLayoutVisibility = value; OnPropertyChanged(); } }
    }

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

    private Visibility _blurOverlayVisibility = Visibility.Collapsed;
    /// <summary>Visibility for the blur overlay.</summary>
    public Visibility BlurOverlayVisibility
    {
        get => _blurOverlayVisibility;
        set { if (_blurOverlayVisibility != value) { _blurOverlayVisibility = value; OnPropertyChanged(); } }
    }

    private Visibility _previewColumnVisibility = Visibility.Visible;
    public Visibility PreviewColumnVisibility
    {
        get => _previewColumnVisibility;
        set { if (_previewColumnVisibility != value) { _previewColumnVisibility = value; OnPropertyChanged(); } }
    }

    private Visibility _localNameColumnVisibility = Visibility.Visible;
    public Visibility LocalNameColumnVisibility
    {
        get => _localNameColumnVisibility;
        set { if (_localNameColumnVisibility != value) { _localNameColumnVisibility = value; OnPropertyChanged(); } }
    }

    private Visibility _idColumnVisibility = Visibility.Visible;
    public Visibility IdColumnVisibility
    {
        get => _idColumnVisibility;
        set { if (_idColumnVisibility != value) { _idColumnVisibility = value; OnPropertyChanged(); } }
    }

    private Visibility _sizeColumnVisibility = Visibility.Visible;
    public Visibility SizeColumnVisibility
    {
        get => _sizeColumnVisibility;
        set { if (_sizeColumnVisibility != value) { _sizeColumnVisibility = value; OnPropertyChanged(); } }
    }

    private Visibility _tagsColumnVisibility = Visibility.Visible;
    public Visibility TagsColumnVisibility
    {
        get => _tagsColumnVisibility;
        set { if (_tagsColumnVisibility != value) { _tagsColumnVisibility = value; OnPropertyChanged(); } }
    }

    private GridLength _previewColumnWidth = new(104);
    public GridLength PreviewColumnWidth
    {
        get => _previewColumnWidth;
        set { if (_previewColumnWidth != value) { _previewColumnWidth = value; OnPropertyChanged(); } }
    }

    private GridLength _localNameColumnWidth = new(2, GridUnitType.Star);
    public GridLength LocalNameColumnWidth
    {
        get => _localNameColumnWidth;
        set { if (_localNameColumnWidth != value) { _localNameColumnWidth = value; OnPropertyChanged(); } }
    }

    private GridLength _idColumnWidth = new(140);
    public GridLength IdColumnWidth
    {
        get => _idColumnWidth;
        set { if (_idColumnWidth != value) { _idColumnWidth = value; OnPropertyChanged(); } }
    }

    private GridLength _sizeColumnWidth = new(110);
    public GridLength SizeColumnWidth
    {
        get => _sizeColumnWidth;
        set { if (_sizeColumnWidth != value) { _sizeColumnWidth = value; OnPropertyChanged(); } }
    }

    private GridLength _tagsColumnWidth = new(160);
    public GridLength TagsColumnWidth
    {
        get => _tagsColumnWidth;
        set { if (_tagsColumnWidth != value) { _tagsColumnWidth = value; OnPropertyChanged(); } }
    }

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
        : new BitmapImage(new Uri("file:///" + PreviewPath.Replace("\\", "/")));

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
