using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace WallpaperManager.Models;

public sealed class WallpaperItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public void OnPropertyChanged([CallerMemberName] string? propertyName = null)
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
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); OnPropertyChanged(nameof(HomeActionGlyph)); OnPropertyChanged(nameof(HomeActionTooltip)); } }
    }

    public string HomeActionTooltip => IsSelected ? "Remove from Home" : "Add to Home";

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

    // Dynamic Button Slots
    private string _button1Id = string.Empty; public string Button1Id { get => _button1Id; set { _button1Id = value; OnPropertyChanged(); OnPropertyChanged(nameof(Button1Glyph)); OnPropertyChanged(nameof(Button1Tooltip)); } }
    private Visibility _button1Visibility = Visibility.Collapsed; public Visibility Button1Visibility { get => _button1Visibility; set { _button1Visibility = value; OnPropertyChanged(); } }
    public string Button1Glyph => GetButtonGlyph(Button1Id);
    public string Button1Tooltip => GetButtonTooltip(Button1Id);

    private string _button2Id = string.Empty; public string Button2Id { get => _button2Id; set { _button2Id = value; OnPropertyChanged(); OnPropertyChanged(nameof(Button2Glyph)); OnPropertyChanged(nameof(Button2Tooltip)); } }
    private Visibility _button2Visibility = Visibility.Collapsed; public Visibility Button2Visibility { get => _button2Visibility; set { _button2Visibility = value; OnPropertyChanged(); } }
    public string Button2Glyph => GetButtonGlyph(Button2Id);
    public string Button2Tooltip => GetButtonTooltip(Button2Id);

    private string _button3Id = string.Empty; public string Button3Id { get => _button3Id; set { _button3Id = value; OnPropertyChanged(); OnPropertyChanged(nameof(Button3Glyph)); OnPropertyChanged(nameof(Button3Tooltip)); } }
    private Visibility _button3Visibility = Visibility.Collapsed; public Visibility Button3Visibility { get => _button3Visibility; set { _button3Visibility = value; OnPropertyChanged(); } }
    public string Button3Glyph => GetButtonGlyph(Button3Id);
    public string Button3Tooltip => GetButtonTooltip(Button3Id);

    private string _button4Id = string.Empty; public string Button4Id { get => _button4Id; set { _button4Id = value; OnPropertyChanged(); OnPropertyChanged(nameof(Button4Glyph)); OnPropertyChanged(nameof(Button4Tooltip)); } }
    private Visibility _button4Visibility = Visibility.Collapsed; public Visibility Button4Visibility { get => _button4Visibility; set { _button4Visibility = value; OnPropertyChanged(); } }
    public string Button4Glyph => GetButtonGlyph(Button4Id);
    public string Button4Tooltip => GetButtonTooltip(Button4Id);

    private string _button5Id = string.Empty; public string Button5Id { get => _button5Id; set { _button5Id = value; OnPropertyChanged(); OnPropertyChanged(nameof(Button5Glyph)); OnPropertyChanged(nameof(Button5Tooltip)); } }
    private Visibility _button5Visibility = Visibility.Collapsed; public Visibility Button5Visibility { get => _button5Visibility; set { _button5Visibility = value; OnPropertyChanged(); } }
    public string Button5Glyph => GetButtonGlyph(Button5Id);
    public string Button5Tooltip => GetButtonTooltip(Button5Id);

    private string _button6Id = string.Empty; public string Button6Id { get => _button6Id; set { _button6Id = value; OnPropertyChanged(); OnPropertyChanged(nameof(Button6Glyph)); OnPropertyChanged(nameof(Button6Tooltip)); } }
    private Visibility _button6Visibility = Visibility.Collapsed; public Visibility Button6Visibility { get => _button6Visibility; set { _button6Visibility = value; OnPropertyChanged(); } }
    public string Button6Glyph => GetButtonGlyph(Button6Id);
    public string Button6Tooltip => GetButtonTooltip(Button6Id);

    private string GetButtonGlyph(string id) => id switch
    {
        CardButtonIds.ThreeDot => "\uE712",
        CardButtonIds.AddTag => "\uE8EC", // Tag
        CardButtonIds.AddToHome => HomeActionGlyph,
        CardButtonIds.Delete => "\uE74D", // Delete
        CardButtonIds.Details => "\uE946", // Info
        _ => string.Empty
    };

    private string GetButtonTooltip(string id) => id switch
    {
        CardButtonIds.ThreeDot => "More actions",
        CardButtonIds.AddTag => "Add tags",
        CardButtonIds.AddToHome => HomeActionTooltip,
        CardButtonIds.Delete => "Delete wallpaper",
        CardButtonIds.Details => "Wallpaper details",
        _ => string.Empty
    };

    public void UpdateButtons(List<string> buttons, bool isSelected)
    {
        IsSelected = isSelected; // Ensure state is correct

        Button1Id = buttons.Count > 0 ? buttons[0] : string.Empty;
        Button1Visibility = string.IsNullOrEmpty(Button1Id) ? Visibility.Collapsed : Visibility.Visible;

        Button2Id = buttons.Count > 1 ? buttons[1] : string.Empty;
        Button2Visibility = string.IsNullOrEmpty(Button2Id) ? Visibility.Collapsed : Visibility.Visible;

        Button3Id = buttons.Count > 2 ? buttons[2] : string.Empty;
        Button3Visibility = string.IsNullOrEmpty(Button3Id) ? Visibility.Collapsed : Visibility.Visible;

        Button4Id = buttons.Count > 3 ? buttons[3] : string.Empty;
        Button4Visibility = string.IsNullOrEmpty(Button4Id) ? Visibility.Collapsed : Visibility.Visible;

        Button5Id = buttons.Count > 4 ? buttons[4] : string.Empty;
        Button5Visibility = string.IsNullOrEmpty(Button5Id) ? Visibility.Collapsed : Visibility.Visible;

        Button6Id = buttons.Count > 5 ? buttons[5] : string.Empty;
        Button6Visibility = string.IsNullOrEmpty(Button6Id) ? Visibility.Collapsed : Visibility.Visible;
    }

    public string HomeActionGlyph => IsSelected ? "\uE711" : "\uE710"; // Minus vs Add

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
