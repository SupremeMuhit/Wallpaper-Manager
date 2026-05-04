using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using WallpaperManager.Models;
using WallpaperManager.Services;
using Windows.Storage.Pickers;
using Microsoft.UI;
using Windows.UI;
using System.Diagnostics;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.Graphics.Canvas.Effects;
using Windows.Graphics.Effects;

namespace WallpaperManager;

public class SettingsCategory
{
    public string Tag { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
}

public sealed partial class MainWindow : Window
{
    private const string ContactWebhookKey = "DISCORD_CONTACT_WEBHOOK_URL";
    private const string PreviewColumn = "Preview";
    private const string LocalNameColumn = "LocalName";
    private const string IdColumn = "Id";
    private const string SizeColumn = "Size";
    private const string TagsColumn = "Tags";

    private static readonly HttpClient ContactHttpClient = new();
    private readonly AppSettingsStore _settingsStore = new();
    private readonly WallpaperEngineService _engineService = new();
    private readonly WallpaperScanner _wallpaperScanner = new();
    private readonly SteamWorkshopService _workshopService = new();
    private readonly WorkshopDownloadService _downloadService = new();
    private readonly DispatcherTimer _engineStatusTimer = new();
    private MicaBackdrop? _micaBackdrop;
    private bool _isLoadingSettings;

    public ObservableCollection<WallpaperLibraryRoot> LibraryRoots { get; } = [];

    public ObservableCollection<WallpaperItem> Wallpapers { get; } = [];

    public ObservableCollection<WallpaperItem> VisibleWallpapers { get; } = [];

    public ObservableCollection<WallpaperItem> SelectedWallpapers { get; } = [];

    public ObservableCollection<WallpaperTag> Tags { get; } = [];

    public ObservableCollection<WallpaperTag> VisibleTags { get; } = [];
    
    public ObservableCollection<CardButtonInfo> CardButtonsList { get; } = [];

    public IReadOnlyList<string> ThemeChoices { get; } = ThemeOptions.All;

    public IReadOnlyList<string> LibraryViewChoices { get; } = LibraryViewModes.All;

    public IReadOnlyList<string> CardSizeChoices { get; } = CardSizeOptions.All;

    public AppSettings CurrentSettings { get; private set; } = new();

    public WallpaperItem NsfwPreviewItem { get; } = new() { IsNsfw = true, LocalName = "NSFW Preview" };
    public WallpaperItem MaturePreviewItem { get; } = new() { IsMature = true, LocalName = "Mature Preview" };

    public IReadOnlyList<string> LibrarySortChoices { get; } = ["Name", "Date Added", "Workshop Updated", "Subscribers", "Size"];
    public IReadOnlyList<string> HomeSortChoices { get; } = ["Free Movement", "Name", "Date Added", "Workshop Updated", "Subscribers", "Size"];

    public IReadOnlyList<string> NsfwTabChoices { get; } = ["Off", "Only NSFW", "NSFW and Mature"];

    public ObservableCollection<SettingsCategory> SettingsCategories { get; } =
    [
        new() { Tag = "EngineWallpaper", Title = "Engine Setting", Icon = "\uE8A7" },
        new() { Tag = "Appearance", Title = "Appearance", Icon = "\uE771" },
        new() { Tag = "Library", Title = "Library", Icon = "\uE8B7" },
        new() { Tag = "NsfwMature", Title = "NSFW / Mature", Icon = "\uE8D4" },
        new() { Tag = "Tags", Title = "Tags", Icon = "\uE8EC" },
        new() { Tag = "About", Title = "About", Icon = "\uE946" },
        new() { Tag = "Contact", Title = "Contact", Icon = "\uE715" }
    ];

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;

        _engineStatusTimer.Interval = TimeSpan.FromSeconds(3);
        _engineStatusTimer.Tick += (_, _) => UpdateEngineStatus();
        _engineStatusTimer.Start();

        _sizeChangedTimer.Interval = TimeSpan.FromMilliseconds(200);
        _sizeChangedTimer.Tick += (_, _) =>
        {
            _sizeChangedTimer.Stop();
            ApplyWallpaperPresentation();
        };

        RootGrid.SizeChanged += (s, e) =>
        {
            if (CurrentSettings.CardSize == CardSizeOptions.Large)
            {
                _sizeChangedTimer.Stop();
                _sizeChangedTimer.Start();
            }
        };

        LoadSettings();
    }

    private readonly DispatcherTimer _sizeChangedTimer = new();

    private void ApplyBackdrop(bool useMica)
    {
        try
        {
            if (!useMica)
            {
                SystemBackdrop = null;
                _micaBackdrop = null;
                return;
            }

            _micaBackdrop = new MicaBackdrop();
            SystemBackdrop = _micaBackdrop;
            ApplyThemeColor(CurrentSettings.ThemeColor);
        }
        catch
        {
            // Keep default app background if Mica is unavailable.
            SystemBackdrop = null;
            _micaBackdrop = null;
        }
    }

    private async void LoadSettings()
    {
        _isLoadingSettings = true;
        CurrentSettings = await _settingsStore.LoadAsync();

        LibraryRoots.Clear();
        foreach (var root in CurrentSettings.WallpaperDirectories)
        {
            LibraryRoots.Add(root);
        }

        Tags.Clear();
        foreach (var tag in CurrentSettings.Tags)
        {
            Tags.Add(tag);
        }

        EnginePathTextBox.Text = CurrentSettings.EngineExecutablePath;
        ThemeComboBox.SelectedItem = ToThemeMode(CurrentSettings.Theme);
        ThemeColorTextBox.Text = CurrentSettings.ThemeColor;
        ThemeColorPresetComboBox.SelectedItem = ToThemeColorPreset(CurrentSettings.ThemeColor);
        LibraryViewComboBox.SelectedItem = CurrentSettings.LibraryViewMode;
        HomeViewComboBox.SelectedItem = CurrentSettings.HomeViewMode;
        LibrarySortComboBox.SelectedItem = CurrentSettings.LibrarySortMode;
        HomeSortComboBox.SelectedItem = CurrentSettings.HomeSortMode;
        CardSizeComboBox.SelectedItem = CurrentSettings.CardSize;
        HomeCardSizeComboBox.SelectedItem = CurrentSettings.CardSize;
        ColorRowsToggle.IsOn = CurrentSettings.ColorRowsByHighestPriorityTag;
        NsfwTabComboBox.SelectedIndex = (int)CurrentSettings.NsfwTabMode;
        RunOnStartupToggle.IsOn = CurrentSettings.RunOnStartup;
        MemoryUsageComboBox.SelectedItem = CurrentSettings.MemoryUsageProfile;
        PrioritizeWorkshopNameToggle.IsOn = CurrentSettings.PrioritizeWorkshopName;
        AutoMarkNsfwToggle.IsOn = CurrentSettings.AutoMarkNsfwFromWorkshop;
        RemoveCensorOnHoverToggle.IsOn = CurrentSettings.RemoveCensorOnHover;
        NsfwModeComboBox.SelectedIndex = (int)CurrentSettings.NsfwMode;
        MatureModeComboBox.SelectedIndex = (int)CurrentSettings.MatureMode;
        LibraryHideComboBox.SelectedIndex = (int)CurrentSettings.LibraryHideMode;
        
        InitializeCardButtonsList();

        BlurIntensitySlider.Value = CurrentSettings.BlurIntensity;
        OverlayOpacitySlider.Value = CurrentSettings.OverlayOpacity;
        UseWorkshopTagsToggle.IsOn = CurrentSettings.UseWorkshopTags;

        ApplyTheme(CurrentSettings.Theme);
        CurrentSettings.UseMicaBackdrop = true;
        ApplyBackdrop(true);
        ApplyThemeColor(CurrentSettings.ThemeColor);
        ApplyColumnToggleState();
        ApplyLibraryViewMode(CurrentSettings.LibraryViewMode);
        ApplyHomeViewMode(CurrentSettings.HomeViewMode);
        ApplyHomeSortMode();
        RefreshVisibleTags();
        UpdateEngineStatus();
        _isLoadingSettings = false;

        await ScanLibraryAsync();
    }

    private async void AddDirectory_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder
        };

        picker.FileTypeFilter.Add("*");
        InitializePicker(picker);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return;
        }

        if (LibraryRoots.Any(root => string.Equals(root.Path, folder.Path, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        LibraryRoots.Add(WallpaperLibraryRoot.FromPath(folder.Path));
        TriggerSaveSettings();
        await ScanLibraryAsync();
    }

    private async void RemoveDirectory_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not string path)
        {
            return;
        }

        var root = LibraryRoots.FirstOrDefault(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
        if (root is null)
        {
            return;
        }

        LibraryRoots.Remove(root);
        TriggerSaveSettings();
        await ScanLibraryAsync();
    }

    private async void BrowseEngineExecutable_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder
        };

        picker.FileTypeFilter.Add(".exe");
        InitializePicker(picker);

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        EnginePathTextBox.Text = file.Path;
        TriggerSaveSettings();
        UpdateEngineStatus();
    }

    private void EnginePathTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        CurrentSettings.EngineExecutablePath = EnginePathTextBox.Text;
        TriggerSaveSettings();
        UpdateEngineStatus();
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || ThemeComboBox.SelectedItem is not string themeMode)
        {
            return;
        }

        CurrentSettings.Theme = FromThemeMode(themeMode);
        ApplyTheme(CurrentSettings.Theme);
        TriggerSaveSettings();
    }

    private void LibraryViewComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || LibraryViewComboBox.SelectedItem is not string viewMode)
        {
            return;
        }

        CurrentSettings.LibraryViewMode = viewMode;
        ApplyLibraryViewMode(viewMode);
        TriggerSaveSettings();
    }

    private void HomeViewComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || HomeViewComboBox.SelectedItem is not string viewMode)
        {
            return;
        }

        CurrentSettings.HomeViewMode = viewMode;
        ApplyHomeViewMode(viewMode);
        TriggerSaveSettings();
    }

    private void CardSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || (sender as ComboBox)?.SelectedItem is not string cardSize)
        {
            return;
        }

        CurrentSettings.CardSize = cardSize;
        SyncCardSizeSelectors(cardSize);
        ApplyWallpaperPresentation();
        TriggerSaveSettings();
    }

    private void ColorRowsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        CurrentSettings.ColorRowsByHighestPriorityTag = ColorRowsToggle.IsOn;
        ApplyWallpaperPresentation();
        RefreshVisibleWallpapers();
        RefreshSelectedWallpapers();
        TriggerSaveSettings();
    }

    private void NsfwTabComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        CurrentSettings.NsfwTabMode = (NsfwTabMode)NsfwTabComboBox.SelectedIndex;
        NsfwNavTab.Visibility = CurrentSettings.NsfwTabMode != NsfwTabMode.Off ? Visibility.Visible : Visibility.Collapsed;
        RefreshVisibleWallpapers();
        TriggerSaveSettings();
    }

    private void RunOnStartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        CurrentSettings.RunOnStartup = RunOnStartupToggle.IsOn;
        TriggerSaveSettings();
    }

    private void MemoryUsageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || MemoryUsageComboBox.SelectedItem is not string memoryProfile)
        {
            return;
        }

        CurrentSettings.MemoryUsageProfile = memoryProfile;
        TriggerSaveSettings();
    }

    private void ThemeColorPresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || ThemeColorPresetComboBox.SelectedItem is not string preset)
        {
            return;
        }

        if (preset != "Custom")
        {
            CurrentSettings.ThemeColor = PresetToHex(preset);
            ThemeColorTextBox.Text = CurrentSettings.ThemeColor;
            ApplyThemeColor(CurrentSettings.ThemeColor);
        }

        TriggerSaveSettings();
    }

    private void ThemeColorTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        if (!TryNormalizeHexColor(ThemeColorTextBox.Text, out var normalized))
        {
            return;
        }

        CurrentSettings.ThemeColor = normalized;
        ThemeColorPresetComboBox.SelectedItem = ToThemeColorPreset(normalized);
        ApplyThemeColor(normalized);
        TriggerSaveSettings();
    }

    private async void PickThemeColor_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ColorPicker
        {
            Color = ParseHexColor(CurrentSettings.ThemeColor),
            IsAlphaEnabled = false,
            IsAlphaSliderVisible = false,
            IsAlphaTextInputVisible = false
        };

        var dialog = new ContentDialog
        {
            Title = "Select theme color",
            Content = picker,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = RootGrid.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        CurrentSettings.ThemeColor = FormatHexColor(picker.Color);
        ThemeColorTextBox.Text = CurrentSettings.ThemeColor;
        ThemeColorPresetComboBox.SelectedItem = ToThemeColorPreset(CurrentSettings.ThemeColor);
        ApplyThemeColor(CurrentSettings.ThemeColor);
        TriggerSaveSettings();
    }

    private void PrioritizeWorkshopNameToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings) return;
        CurrentSettings.PrioritizeWorkshopName = PrioritizeWorkshopNameToggle.IsOn;
        ApplyWallpaperPresentation();
        RefreshVisibleWallpapers();
        TriggerSaveSettings();
    }

    private void LibraryHideComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || LibraryHideComboBox.SelectedItem is not string selection) return;
        
        CurrentSettings.LibraryHideMode = selection switch
        {
            "Only NSFW" => LibraryHideMode.OnlyNsfw,
            "NSFW and Mature" => LibraryHideMode.NsfwAndMature,
            _ => LibraryHideMode.Off
        };
        
        RefreshVisibleWallpapers();
        TriggerSaveSettings();
    }

    private void AutoMarkNsfwToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings) return;
        CurrentSettings.AutoMarkNsfwFromWorkshop = AutoMarkNsfwToggle.IsOn;
        TriggerSaveSettings();
    }

    private void RemoveCensorOnHoverToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings) return;
        CurrentSettings.RemoveCensorOnHover = RemoveCensorOnHoverToggle.IsOn;
        ApplyWallpaperPresentation();
        TriggerSaveSettings();
    }

    private void NsfwModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings) return;
        CurrentSettings.NsfwMode = (CensorshipMode)NsfwModeComboBox.SelectedIndex;
        ApplyWallpaperPresentation();
        TriggerSaveSettings();
    }

    private void MatureModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings) return;
        CurrentSettings.MatureMode = (CensorshipMode)MatureModeComboBox.SelectedIndex;
        ApplyWallpaperPresentation();
        TriggerSaveSettings();
    }

    private void BlurIntensitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoadingSettings) return;
        CurrentSettings.BlurIntensity = BlurIntensitySlider.Value;
        ApplyWallpaperPresentation();
        UpdateAllBlurEffects();
        TriggerSaveSettings();
    }

    private readonly HashSet<FrameworkElement> _activeBlurOverlays = new();

    private void BlurOverlay_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            _activeBlurOverlays.Add(element);
            UpdateBlurEffect(element);
        }
    }

    private void BlurOverlay_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            _activeBlurOverlays.Remove(element);
        }
    }

    private void UpdateAllBlurEffects()
    {
        foreach (var element in _activeBlurOverlays.ToList())
        {
            UpdateBlurEffect(element);
        }
    }

    private void UpdateBlurEffect(FrameworkElement element)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;

        var childVisual = ElementCompositionPreview.GetElementChildVisual(element) as SpriteVisual;
        if (childVisual == null)
        {
            var blurEffect = new GaussianBlurEffect
            {
                Name = "Blur",
                BlurAmount = (float)(CurrentSettings.BlurIntensity / 3.0), // Scale intensity to a reasonable radius (0 to 33.3)
                BorderMode = EffectBorderMode.Hard,
                Source = new CompositionEffectSourceParameter("source")
            };

            var factory = compositor.CreateEffectFactory(blurEffect, new[] { "Blur.BlurAmount" });
            var brush = factory.CreateBrush();
            brush.SetSourceParameter("source", compositor.CreateBackdropBrush());

            childVisual = compositor.CreateSpriteVisual();
            childVisual.Brush = brush;

            ElementCompositionPreview.SetElementChildVisual(element, childVisual);

            // Bind size
            var bind = compositor.CreateExpressionAnimation("visual.Size");
            bind.SetReferenceParameter("visual", visual);
            childVisual.StartAnimation("Size", bind);
        }

        // Update intensity
        if (childVisual.Brush is CompositionEffectBrush effectBrush)
        {
            effectBrush.Properties.InsertScalar("Blur.BlurAmount", (float)(CurrentSettings.BlurIntensity / 3.0));
        }
    }

    private void UpdatePreviewWallpapers()
    {
        if (Wallpapers.Count == 0) return;

        var random = new Random();
        var nsfwOptions = Wallpapers.Where(w => w.IsNsfw).ToList();
        var matureOptions = Wallpapers.Where(w => w.IsMature).ToList();
        var allOptions = Wallpapers.ToList();

        if (nsfwOptions.Count > 0)
        {
            var r = nsfwOptions[random.Next(nsfwOptions.Count)];
            NsfwPreviewItem.PreviewPath = r.PreviewPath;
            NsfwPreviewItem.SteamId = r.SteamId;
            NsfwPreviewItem.DirectoryPath = r.DirectoryPath;
        }
        else if (allOptions.Count > 0)
        {
            var r = allOptions[random.Next(allOptions.Count)];
            NsfwPreviewItem.PreviewPath = r.PreviewPath;
        }

        if (matureOptions.Count > 0)
        {
            var r = matureOptions[random.Next(matureOptions.Count)];
            MaturePreviewItem.PreviewPath = r.PreviewPath;
            MaturePreviewItem.SteamId = r.SteamId;
            MaturePreviewItem.DirectoryPath = r.DirectoryPath;
        }
        else if (allOptions.Count > 0)
        {
            var r = allOptions[random.Next(allOptions.Count)];
            MaturePreviewItem.PreviewPath = r.PreviewPath;
        }

        NsfwPreviewItem.OnPropertyChanged(nameof(WallpaperItem.PreviewImage));
        MaturePreviewItem.OnPropertyChanged(nameof(WallpaperItem.PreviewImage));
    }

    private void OverlayOpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoadingSettings) return;
        CurrentSettings.OverlayOpacity = OverlayOpacitySlider.Value;
        ApplyWallpaperPresentation();
        TriggerSaveSettings();
    }

    private void UseWorkshopTagsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings) return;
        CurrentSettings.UseWorkshopTags = UseWorkshopTagsToggle.IsOn;
        ApplyWallpaperPresentation();
        RefreshVisibleWallpapers();
        TriggerSaveSettings();
    }

    private void ColumnToggle_Click(object sender, RoutedEventArgs e)
    {
        var toggle = sender as ToggleButton;
        if (toggle?.Tag is not string column)
        {
            return;
        }

        var hiddenColumns = CurrentSettings.HiddenLibraryColumns;
        if (toggle.IsChecked == true)
        {
            hiddenColumns.RemoveAll(item => string.Equals(item, column, StringComparison.OrdinalIgnoreCase));
        }
        else if (!hiddenColumns.Any(item => string.Equals(item, column, StringComparison.OrdinalIgnoreCase)))
        {
            hiddenColumns.Add(column);
        }

        ApplyColumnVisibility();
        ApplyWallpaperPresentation();
        RefreshVisibleWallpapers();
        TriggerSaveSettings();
    }

    private async void CreateTag_Click(object sender, RoutedEventArgs e)
    {
        await OpenTagEditorAsync(null);
    }

    private async void EditTagButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not WallpaperTag tag)
        {
            return;
        }

        await OpenTagEditorAsync(tag);
    }

    private void TagSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshVisibleTags();
    }

    private async void EditTagColor_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not WallpaperTag tag)
        {
            return;
        }

        var picker = new ColorPicker
        {
            Color = ParseHexColor(tag.Color),
            IsAlphaEnabled = false,
            IsAlphaSliderVisible = false,
            IsAlphaTextInputVisible = false
        };

        var dialog = new ContentDialog
        {
            Title = $"Color for {tag.Name}",
            Content = picker,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = RootGrid.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        tag.Color = FormatHexColor(picker.Color);
        RefreshTagRow(tag);
        RefreshVisibleTags();
        ApplyWallpaperPresentation();
        RefreshVisibleWallpapers();
        RefreshSelectedWallpapers();
        TriggerSaveSettings();
    }

    private void MoveTagUp_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not WallpaperTag tag)
        {
            return;
        }

        var index = Tags.IndexOf(tag);
        if (index <= 0)
        {
            return;
        }

        Tags.Move(index, index - 1);
        RefreshVisibleTags();
        TriggerSaveSettings();
        ApplyWallpaperPresentation();
        RefreshVisibleWallpapers();
        RefreshSelectedWallpapers();
    }

    private void TagsListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (!string.IsNullOrWhiteSpace(TagSearchTextBox.Text))
        {
            return;
        }

        Tags.Clear();
        foreach (var tag in VisibleTags)
        {
            Tags.Add(tag);
        }

        TriggerSaveSettings();
        ApplyWallpaperPresentation();
        RefreshVisibleWallpapers();
        RefreshSelectedWallpapers();
    }

    private void MoveTagDown_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not WallpaperTag tag)
        {
            return;
        }

        var index = Tags.IndexOf(tag);
        if (index < 0 || index >= Tags.Count - 1)
        {
            return;
        }

        Tags.Move(index, index + 1);
        RefreshVisibleTags();
        TriggerSaveSettings();
        ApplyWallpaperPresentation();
        RefreshVisibleWallpapers();
        RefreshSelectedWallpapers();
    }

    private void RemoveTag_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not WallpaperTag tag)
        {
            return;
        }

        Tags.Remove(tag);
        RefreshVisibleTags();
        foreach (var wallpaper in Wallpapers)
        {
            wallpaper.Tags.RemoveAll(item => string.Equals(item, tag.Name, StringComparison.OrdinalIgnoreCase));
        }

        TriggerSaveSettings();
        ApplyWallpaperPresentation();
        RefreshVisibleWallpapers();
        RefreshSelectedWallpapers();
    }

    private async void ExecuteDownload_Click(object sender, RoutedEventArgs e)
    {
        DownloadSuccessPanel.Visibility = Visibility.Collapsed;
        ActiveDownloadPanel.Visibility = Visibility.Collapsed;
        WorkshopPreviewPanel.Visibility = Visibility.Collapsed;
        ActiveDownloadProgressBar.Value = 0;
        ActiveDownloadStatusText.Text = string.Empty;

        var input = WorkshopUrlInput.Text;
        var workshopId = _downloadService.ExtractWorkshopId(input);

        if (string.IsNullOrEmpty(workshopId))
        {
            ShowDownloadInfo("Invalid input", "Please enter a valid Steam Workshop URL or ID.", InfoBarSeverity.Error);
            return;
        }

        var selectedRoot = DownloadPathSelector.SelectedItem as WallpaperLibraryRoot;
        var downloadDir = selectedRoot?.Path;

        if (string.IsNullOrEmpty(downloadDir))
        {
            ShowDownloadInfo("Path Error", "Please select a download directory.", InfoBarSeverity.Error);
            return;
        }

        ExecuteDownloadButton.IsEnabled = false;
        ActiveDownloadPanel.Visibility = Visibility.Visible;
        ActiveDownloadStatusText.Text = "Starting...";
        ShowDownloadInfo("Starting Download", $"Downloading Workshop ID {workshopId}...", InfoBarSeverity.Informational);

        var success = await Task.Run(async () =>
        {
            return await _downloadService.DownloadAsync(workshopId, downloadDir, (progress, status) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    ActiveDownloadProgressBar.IsIndeterminate = progress <= 0;
                    ActiveDownloadProgressBar.Value = progress;
                    ActiveDownloadStatusText.Text = status;
                });
            });
        });

        ExecuteDownloadButton.IsEnabled = true;

        if (success)
        {
            try
            {
                var metadata = await _workshopService.FetchAsync(workshopId);
                if (metadata != null)
                {
                    var metaPath = Path.Combine(downloadDir, workshopId, "meta.json");
                    var json = System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(metaPath, json);
                }
            }
            catch { /* Ignore metadata errors */ }

            DownloadSuccessPanel.Visibility = Visibility.Visible;
            ActiveDownloadPanel.Visibility = Visibility.Collapsed;
            WorkshopUrlInput.Text = string.Empty;
            ShowDownloadInfo("Success", $"Wallpaper {workshopId} downloaded successfully.", InfoBarSeverity.Success);
            await ScanLibraryAsync();
        }
        else
        {
            ShowDownloadInfo("Download Failed", "There was an error downloading the wallpaper. Check the logs or try again.", InfoBarSeverity.Error);
        }
    }

    private string _lastPreviewId = string.Empty;
    private async void WorkshopUrlInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        var input = WorkshopUrlInput.Text;
        var workshopId = _downloadService.ExtractWorkshopId(input);

        if (string.IsNullOrEmpty(workshopId))
        {
            WorkshopPreviewPanel.Visibility = Visibility.Collapsed;
            WorkshopLoadingPanel.Visibility = Visibility.Collapsed;
            _lastPreviewId = string.Empty;
            return;
        }

        if (workshopId == _lastPreviewId) return;
        _lastPreviewId = workshopId;

        WorkshopPreviewPanel.Visibility = Visibility.Collapsed;
        WorkshopLoadingPanel.Visibility = Visibility.Visible;

        try
        {
            var metadata = await _workshopService.FetchAsync(workshopId);
            if (metadata != null && workshopId == _lastPreviewId)
            {
                WorkshopPreviewTitle.Text = metadata.Title;
                WorkshopPreviewDescription.Text = metadata.Description;
                WorkshopPreviewTags.ItemsSource = metadata.Tags;
                
                if (!string.IsNullOrEmpty(metadata.PreviewUrl))
                {
                    WorkshopPreviewImage.Source = new BitmapImage(new Uri(metadata.PreviewUrl));
                }

                WorkshopPreviewPanel.Visibility = Visibility.Visible;
            }
            else if (workshopId == _lastPreviewId)
            {
                WorkshopPreviewPanel.Visibility = Visibility.Collapsed;
            }
        }
        catch 
        { 
            if (workshopId == _lastPreviewId) WorkshopPreviewPanel.Visibility = Visibility.Collapsed;
        }
        finally
        {
            if (workshopId == _lastPreviewId)
            {
                WorkshopLoadingPanel.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void ShowDownloadInfo(string title, string message, InfoBarSeverity severity)
    {
        DownloadInfoBar.Title = title;
        DownloadInfoBar.Message = message;
        DownloadInfoBar.Severity = severity;
        DownloadInfoBar.IsOpen = true;
    }

    private void ShowInfoBar(string title, string message, InfoBarSeverity severity)
    {
        // We'll reuse the EmptyLibraryInfo for notifications if needed, or better, add a dedicated one
        // For now let's just use EmptyLibraryInfo since it's already there
        EmptyLibraryInfo.Title = title;
        EmptyLibraryInfo.Message = message;
        EmptyLibraryInfo.Severity = severity;
        EmptyLibraryInfo.IsOpen = true;
    }

    private async void ScanLibrary_Click(object sender, RoutedEventArgs e)
    {
        await ScanLibraryAsync();
    }

    private void WallpaperActions_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not WallpaperItem wallpaper || sender is not FrameworkElement anchor)
        {
            return;
        }

        var flyout = new MenuFlyout();

        var homeItem = new MenuFlyoutItem
        {
            Text = wallpaper.IsSelected ? "Remove from Home" : "Add to Home",
            Tag = wallpaper
        };
        homeItem.Click += ToggleHomeMenuItem_Click;
        flyout.Items.Add(homeItem);

        var nsfwItem = new MenuFlyoutItem
        {
            Text = wallpaper.IsNsfw ? "Unmark NSFW" : "Mark as NSFW",
            Tag = wallpaper
        };
        nsfwItem.Click += ToggleNsfwMenuItem_Click;
        flyout.Items.Add(nsfwItem);

        var matureItem = new MenuFlyoutItem
        {
            Text = wallpaper.IsMature ? "Unmark Mature" : "Mark as Mature",
            Tag = wallpaper
        };
        matureItem.Click += ToggleMatureMenuItem_Click;
        flyout.Items.Add(matureItem);

        flyout.Items.Add(new MenuFlyoutSeparator());

        var detailsItem = new MenuFlyoutItem
        {
            Text = "Wallpaper Details",
            Tag = wallpaper
        };
        detailsItem.Click += WallpaperDetails_Click;
        flyout.Items.Add(detailsItem);

        if (Tags.Count > 0)
        {
            flyout.Items.Add(new MenuFlyoutSeparator());
            var tagsMenu = new MenuFlyoutSubItem { Text = "Tags" };
            foreach (var tag in Tags)
            {
                var tagItem = new ToggleMenuFlyoutItem
                {
                    Text = tag.Name,
                    IsChecked = wallpaper.Tags.Any(item => string.Equals(item, tag.Name, StringComparison.OrdinalIgnoreCase)),
                    Tag = new WallpaperTagAction(wallpaper, tag.Name)
                };
                tagItem.Click += ToggleWallpaperTagMenuItem_Click;
                tagsMenu.Items.Add(tagItem);
            }

            flyout.Items.Add(tagsMenu);
        }

        flyout.ShowAt(anchor);
    }

    private void ToggleHomeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuFlyoutItem)?.Tag is not WallpaperItem wallpaper)
        {
            return;
        }

        wallpaper.IsSelected = !wallpaper.IsSelected;
        RefreshSelectedWallpapers();
        TriggerSaveSettings();
    }

    private void ToggleHomeButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not WallpaperItem wallpaper)
        {
            return;
        }

        wallpaper.IsSelected = !wallpaper.IsSelected;
        // RefreshVisibleWallpapers(); // REMOVED: This causes the list to jump to top
        RefreshSelectedWallpapers();
        TriggerSaveSettings();
    }



    private void ToggleNsfwMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuFlyoutItem)?.Tag is not WallpaperItem wallpaper)
        {
            return;
        }

        wallpaper.IsNsfw = !wallpaper.IsNsfw;

        ApplyWallpaperPresentation();
        RefreshVisibleWallpapers();
        RefreshSelectedWallpapers();
        TriggerSaveSettings();
    }

    private void ToggleWallpaperTagMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as ToggleMenuFlyoutItem)?.Tag is not WallpaperTagAction action)
        {
            return;
        }

        if (action.Wallpaper.Tags.Any(tag => string.Equals(tag, action.TagName, StringComparison.OrdinalIgnoreCase)))
        {
            action.Wallpaper.Tags.RemoveAll(tag => string.Equals(tag, action.TagName, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            action.Wallpaper.Tags.Add(action.TagName);
        }

        ApplyWallpaperPresentation();
        RefreshVisibleWallpapers();
        RefreshSelectedWallpapers();
        TriggerSaveSettings();
    }

    private void StartEngine_Click(object sender, RoutedEventArgs e)
    {
        _engineService.StartEngine(CurrentSettings.EngineExecutablePath);
        UpdateEngineStatus();
    }

    private void SettingsNavigationList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SettingsNavigationList.SelectedItem is SettingsCategory category)
        {
            ShowSettingsSection(category.Tag);
        }
    }

    private async void SendContact_Click(object sender, RoutedEventArgs e)
    {
        var mail = ContactMailTextBox.Text.Trim();
        var discord = ContactDiscordTextBox.Text.Trim();
        var message = ContactMessageTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(message))
        {
            ShowContactStatus("Message required", "Please write a message before sending.", InfoBarSeverity.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(mail) && string.IsNullOrWhiteSpace(discord))
        {
            ShowContactStatus("Contact required", "Add either a mail address or Discord username.", InfoBarSeverity.Warning);
            return;
        }

        SendContactButton.IsEnabled = false;
        ContactProgressRing.IsActive = true;
        ContactProgressRing.Visibility = Visibility.Visible;
        ContactInfoBar.IsOpen = false;

        try
        {
            var webhookUrl = GetContactWebhookUrl();
            if (string.IsNullOrWhiteSpace(webhookUrl))
            {
                ShowContactStatus("Contact is not configured", "Add DISCORD_CONTACT_WEBHOOK_URL to the local .env file.", InfoBarSeverity.Error);
                return;
            }

            var content = BuildContactMessage(mail, discord, message);
            var payload = JsonSerializer.Serialize(new
            {
                username = "Carbon Wallpaper",
                content,
                allowed_mentions = new
                {
                    parse = Array.Empty<string>()
                }
            });

            using var requestContent = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await ContactHttpClient.PostAsync(webhookUrl, requestContent);
            if (!response.IsSuccessStatusCode)
            {
                ShowContactStatus("Could not send", "Discord did not accept the message. Please try again later.", InfoBarSeverity.Error);
                return;
            }

            ContactMailTextBox.Text = string.Empty;
            ContactDiscordTextBox.Text = string.Empty;
            ContactMessageTextBox.Text = string.Empty;
            ShowContactStatus("Sent", "Your message was sent.", InfoBarSeverity.Success);
        }
        catch (HttpRequestException)
        {
            ShowContactStatus("Could not send", "Check your internet connection and try again.", InfoBarSeverity.Error);
        }
        finally
        {
            SendContactButton.IsEnabled = true;
            ContactProgressRing.IsActive = false;
            ContactProgressRing.Visibility = Visibility.Collapsed;
        }
    }

    private void RunWallpaper_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not WallpaperItem wallpaper)
        {
            return;
        }

        RunWallpaper(wallpaper);
    }

    private void RunWallpaper(WallpaperItem wallpaper)
    {
        if (!_engineService.IsRunning(CurrentSettings.EngineExecutablePath))
        {
            _engineService.StartEngine(CurrentSettings.EngineExecutablePath);
        }

        _engineService.RunWallpaper(CurrentSettings.EngineExecutablePath, wallpaper);
        UpdateEngineStatus();
    }

    private void OpenExternalLink_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string url || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private void OpenWallpaperFolder(WallpaperItem item)
    {
        if (string.IsNullOrWhiteSpace(item.DirectoryPath) || !Directory.Exists(item.DirectoryPath))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = item.DirectoryPath,
            UseShellExecute = true
        });
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var page = args.IsSettingsSelected
            ? "Settings"
            : (args.SelectedItem as NavigationViewItem)?.Tag as string ?? "Home";

        if (page == "NsfwTab")
        {
            page = "Library";
            _showingNsfwTab = true;
        }
        else
        {
            _showingNsfwTab = false;
        }

        if (page == "Downloader")
        {
            DownloadPathSelector.ItemsSource = LibraryRoots;
            if (LibraryRoots.Count > 0 && DownloadPathSelector.SelectedIndex == -1)
            {
                DownloadPathSelector.SelectedIndex = 0;
            }
        }

        ShowPage(page);
        RefreshVisibleWallpapers();
        UpdatePreviewWallpapers();
    }

    private bool _showingNsfwTab = false;

    private bool _isScanning;
    private async Task ScanLibraryAsync()
    {
        if (_isScanning) return;
        _isScanning = true;
        try
        {
        ScanLibraryFromSettings();

        var scannedWallpapers = await _wallpaperScanner.ScanAsync(
            LibraryRoots,
            CurrentSettings.SelectedWallpaperKeys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            CurrentSettings.NsfwWallpaperKeys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            CurrentSettings.MatureWallpaperKeys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            CurrentSettings.WallpaperTags);

        Wallpapers.Clear();
        foreach (var wallpaper in scannedWallpapers)
        {
            Wallpapers.Add(wallpaper);
        }

        ApplyWallpaperPresentation();
        RefreshVisibleWallpapers();
        RefreshSelectedWallpapers();
        UpdateEmptyStates();

        _ = FetchWorkshopMetadataAsync();
        }
        finally
        {
            _isScanning = false;
        }
    }

    private async Task FetchWorkshopMetadataAsync()
    {
        var workshopWallpapers = Wallpapers.Where(w => !string.IsNullOrWhiteSpace(w.SteamId)).ToList();
        if (workshopWallpapers.Count == 0) return;

        var ids = workshopWallpapers.Select(w => w.SteamId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var metadataMap = await _workshopService.FetchBatchAsync(ids);

        var needsSave = false;
        foreach (var wallpaper in workshopWallpapers)
        {
            if (!metadataMap.TryGetValue(wallpaper.SteamId, out var meta)) continue;

            wallpaper.WorkshopMetadata = meta;

            if (CurrentSettings.AutoMarkNsfwFromWorkshop)
            {
                if (meta.IsMature && !wallpaper.IsMature)
                {
                    wallpaper.IsMature = true;
                    needsSave = true;
                }
                if (meta.IsAdult && !wallpaper.IsNsfw)
                {
                    wallpaper.IsNsfw = true;
                    needsSave = true;
                }
            }
        }

        ApplyWallpaperPresentation();
        RefreshVisibleWallpapers();
        RefreshSelectedWallpapers();

        if (needsSave)
        {
            TriggerSaveSettings();
        }
    }

    private void RefreshVisibleWallpapers()
    {
        var sorted = SortWallpapers(Wallpapers.Where(ShouldShowWallpaper), CurrentSettings.LibrarySortMode).ToList();
        
        // Incremental update to preserve scroll position
        for (int i = 0; i < sorted.Count; i++)
        {
            if (i < VisibleWallpapers.Count)
            {
                if (VisibleWallpapers[i] != sorted[i])
                {
                    VisibleWallpapers[i] = sorted[i];
                }
            }
            else
            {
                VisibleWallpapers.Add(sorted[i]);
            }
        }
        
        while (VisibleWallpapers.Count > sorted.Count)
        {
            VisibleWallpapers.RemoveAt(VisibleWallpapers.Count - 1);
        }

        UpdateEmptyStates();
    }

    private void RefreshSelectedWallpapers()
    {
        SelectedWallpapers.Clear();
        var selected = Wallpapers.Where(item => item.IsSelected);
        if (CurrentSettings.HomeSortMode == "Free Movement")
        {
            // Preserve the order defined in SelectedWallpaperKeys
            var keyOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < CurrentSettings.SelectedWallpaperKeys.Count; i++)
            {
                var k = CurrentSettings.SelectedWallpaperKeys[i];
                if (!keyOrder.ContainsKey(k))
                    keyOrder[k] = i;
            }

            selected = selected.OrderBy(w => keyOrder.TryGetValue(w.Key, out var idx) ? idx : int.MaxValue);
        }
        else
        {
            selected = SortWallpapers(selected, CurrentSettings.HomeSortMode);
            
            // Sync the keys when a specific sort is active so the order persists
            CurrentSettings.SelectedWallpaperKeys = selected
                .Select(wallpaper => wallpaper.Key)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        foreach (var wallpaper in selected)
        {
            SelectedWallpapers.Add(wallpaper);
        }

        UpdateEmptyStates();
    }

    private IEnumerable<WallpaperItem> SortWallpapers(IEnumerable<WallpaperItem> items, string mode)
    {
        return mode switch
        {
            "Date Added" => items.OrderByDescending(w => w.DateModified).ThenBy(w => w.DisplayName),
            "Workshop Updated" => items.OrderByDescending(w => w.WorkshopMetadata?.TimeUpdated ?? DateTime.MinValue).ThenBy(w => w.DisplayName),
            "Subscribers" => items.OrderByDescending(w => w.WorkshopMetadata?.SubscriptionCount ?? 0).ThenBy(w => w.DisplayName),
            "Size" => items.OrderByDescending(w => w.SizeBytes).ThenBy(w => w.DisplayName),
            _ => items.OrderBy(w => w.DisplayName, StringComparer.CurrentCultureIgnoreCase)
        };
    }

    private IEnumerable<WallpaperItem> SortWallpapersDescending(IEnumerable<WallpaperItem> items, string mode)
    {
        return SortWallpapers(items, mode); // Already descending for relevant fields
    }

    private IEnumerable<WallpaperItem> SortWallpapersAscending(IEnumerable<WallpaperItem> items, string mode)
    {
        return mode switch
        {
            "Date Added" => items.OrderBy(w => w.DateModified).ThenBy(w => w.DisplayName),
            "Workshop Updated" => items.OrderBy(w => w.WorkshopMetadata?.TimeUpdated ?? DateTime.MinValue).ThenBy(w => w.DisplayName),
            "Subscribers" => items.OrderBy(w => w.WorkshopMetadata?.SubscriptionCount ?? 0).ThenBy(w => w.DisplayName),
            "Size" => items.OrderBy(w => w.SizeBytes).ThenBy(w => w.DisplayName),
            _ => items.OrderByDescending(w => w.DisplayName, StringComparer.CurrentCultureIgnoreCase)
        };
    }

    private void UpdateWallpaperButtons(WallpaperItem item, IReadOnlyList<string> buttons, IReadOnlySet<string> selectedKeys)
    {
        var isSelected = selectedKeys.Contains(item.Key);
        var maxVisible = CurrentSettings.CardSize == CardSizeOptions.Large ? 6 : 3;
        item.UpdateButtons(buttons, isSelected, maxVisible);
    }

    private void ApplyWallpaperPresentation()
    {
        var hiddenColumns = CurrentSettings.HiddenLibraryColumns.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var buttons = CurrentSettings.CardButtons;
        var selectedKeys = CurrentSettings.SelectedWallpaperKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var wallpaper in Wallpapers)
        {
            wallpaper.RowBackground = GetWallpaperRowBrush(wallpaper);
            wallpaper.PreviewColumnVisibility = GetColumnVisibility(hiddenColumns, PreviewColumn);
            wallpaper.LocalNameColumnVisibility = GetColumnVisibility(hiddenColumns, LocalNameColumn);
            wallpaper.IdColumnVisibility = GetColumnVisibility(hiddenColumns, IdColumn);
            wallpaper.SizeColumnVisibility = GetColumnVisibility(hiddenColumns, SizeColumn);
            wallpaper.TagsColumnVisibility = GetColumnVisibility(hiddenColumns, TagsColumn);
            wallpaper.PreviewColumnWidth = GetColumnWidth(hiddenColumns, PreviewColumn, new GridLength(104));
            wallpaper.LocalNameColumnWidth = GetColumnWidth(hiddenColumns, LocalNameColumn, new GridLength(2, GridUnitType.Star));
            wallpaper.IdColumnWidth = GetColumnWidth(hiddenColumns, IdColumn, new GridLength(140));
            wallpaper.SizeColumnWidth = GetColumnWidth(hiddenColumns, SizeColumn, new GridLength(110));
            wallpaper.TagsColumnWidth = GetColumnWidth(hiddenColumns, TagsColumn, new GridLength(180));

            wallpaper.PrioritizeWorkshopName = CurrentSettings.PrioritizeWorkshopName;
            wallpaper.UseWorkshopTags = CurrentSettings.UseWorkshopTags;

            ApplyCensorship(wallpaper);
            ApplySizePresentation(wallpaper, hiddenColumns);
            UpdateWallpaperButtons(wallpaper, buttons, selectedKeys);
        }

        ApplyCensorship(NsfwPreviewItem);
        ApplyCensorship(MaturePreviewItem);

        ApplyColumnVisibility();
        ApplyCompactHeaderVisibility();
    }

    private void ApplyCensorship(WallpaperItem item)
    {
        var mode = item.IsNsfw ? CurrentSettings.NsfwMode : (item.IsMature ? CurrentSettings.MatureMode : CensorshipMode.Off);

        // Remove censorship on hover
        if (CurrentSettings.RemoveCensorOnHover && item.IsHovered)
        {
            item.BlurOpacity = 1.0;
            item.CensorshipOverlayOpacity = 0;
            item.NsfwOverlayVisibility = Visibility.Collapsed;
            item.MatureOverlayVisibility = Visibility.Collapsed;
            item.BlurOverlayVisibility = Visibility.Collapsed;
            return;
        }

        switch (mode)
        {
            case CensorshipMode.Off:
                item.BlurOpacity = 1.0;
                item.CensorshipOverlayOpacity = 0;
                item.NsfwOverlayVisibility = Visibility.Collapsed;
                item.MatureOverlayVisibility = Visibility.Collapsed;
                item.BlurOverlayVisibility = Visibility.Collapsed;
                break;
            case CensorshipMode.Blur:
                // Pure blur: Keep overlay opacity at 1.0 so the blur effect is fully opaque, 
                // but let the BlurAmount (intensity) handle the thickness.
                item.BlurOpacity = 1.0; 
                item.CensorshipOverlayOpacity = 1.0; 
                item.NsfwOverlayVisibility = Visibility.Collapsed;
                item.MatureOverlayVisibility = Visibility.Collapsed;
                item.BlurOverlayVisibility = Visibility.Visible;
                break;
            case CensorshipMode.Overlay:
                item.BlurOpacity = 1.0;
                item.CensorshipOverlayOpacity = CurrentSettings.OverlayOpacity;
                item.NsfwOverlayVisibility = item.IsNsfw ? Visibility.Visible : Visibility.Collapsed;
                item.MatureOverlayVisibility = (item.IsMature && !item.IsNsfw) ? Visibility.Visible : Visibility.Collapsed;
                item.BlurOverlayVisibility = Visibility.Collapsed;
                break;
        }
    }

    private void PreviewImage_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is WallpaperItem item)
        {
            item.IsHovered = true;
            ApplyCensorship(item);
        }
    }

    private void PreviewImage_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is WallpaperItem item)
        {
            item.IsHovered = false;
            ApplyCensorship(item);
        }
    }

    private void ApplyColumnVisibility()
    {
        var hiddenColumns = CurrentSettings.HiddenLibraryColumns.ToHashSet(StringComparer.OrdinalIgnoreCase);

        PreviewHeader.Visibility = GetColumnVisibility(hiddenColumns, PreviewColumn);
        LocalNameHeader.Visibility = GetColumnVisibility(hiddenColumns, LocalNameColumn);
        IdHeader.Visibility = GetColumnVisibility(hiddenColumns, IdColumn);
        SizeHeader.Visibility = GetColumnVisibility(hiddenColumns, SizeColumn);
        TagsHeader.Visibility = GetColumnVisibility(hiddenColumns, TagsColumn);
        PreviewHeaderColumn.Width = GetColumnWidth(hiddenColumns, PreviewColumn, new GridLength(104));
        LocalNameHeaderColumn.Width = GetColumnWidth(hiddenColumns, LocalNameColumn, new GridLength(2, GridUnitType.Star));
        IdHeaderColumn.Width = GetColumnWidth(hiddenColumns, IdColumn, new GridLength(140));
        SizeHeaderColumn.Width = GetColumnWidth(hiddenColumns, SizeColumn, new GridLength(110));
        TagsHeaderColumn.Width = GetColumnWidth(hiddenColumns, TagsColumn, new GridLength(180));
    }

    private void InitializeCardButtonsList()
    {
        CardButtonsList.Clear();
        var allButtons = new List<CardButtonInfo>
        {
            new() { Id = CardButtonIds.ThreeDot, Name = "More actions (Three Dot)", Glyph = "\uE712", IsEnabled = true },
            new() { Id = CardButtonIds.AddTag, Name = "Add Tag / Mark", Glyph = "\uE8EC" },
            new() { Id = CardButtonIds.AddToHome, Name = "Add to Home", Glyph = "\uE710" },
            new() { Id = CardButtonIds.Delete, Name = "Delete Wallpaper", Glyph = "\uE74D" },
            new() { Id = CardButtonIds.Details, Name = "Wallpaper Details", Glyph = "\uE946" }
        };

        // Ensure ThreeDot is in the top 3 and enabled
        var enabledButtons = CurrentSettings.CardButtons.ToList();
        if (!enabledButtons.Contains(CardButtonIds.ThreeDot))
        {
            enabledButtons.Insert(0, CardButtonIds.ThreeDot);
        }

        // Reorder list to ensure ThreeDot is at index 0-2 if it's further down
        var threeDotIdx = enabledButtons.IndexOf(CardButtonIds.ThreeDot);
        if (threeDotIdx > 2)
        {
            enabledButtons.RemoveAt(threeDotIdx);
            enabledButtons.Insert(2, CardButtonIds.ThreeDot);
        }

        foreach (var id in enabledButtons)
        {
            var btn = allButtons.FirstOrDefault(b => b.Id == id);
            if (btn != null)
            {
                btn.IsEnabled = true;
                if (btn.Id == CardButtonIds.ThreeDot) btn.IsEnabled = true; // Hard force
                CardButtonsList.Add(btn);
                allButtons.Remove(btn);
            }
        }

        foreach (var btn in allButtons)
        {
            btn.IsEnabled = false;
            CardButtonsList.Add(btn);
        }
        
        // Final check on ThreeDot position in the UI list
        EnsureThreeDotPosition();
    }

    private void EnsureThreeDotPosition()
    {
        var threeDot = CardButtonsList.FirstOrDefault(b => b.Id == CardButtonIds.ThreeDot);
        if (threeDot != null)
        {
            threeDot.IsEnabled = true; // Cannot disable ThreeDot
            var idx = CardButtonsList.IndexOf(threeDot);
            if (idx > 2)
            {
                CardButtonsList.Move(idx, 2);
            }
        }
    }

    private void CardButtonEnabled_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch ts && ts.DataContext is CardButtonInfo btn && btn.Id == CardButtonIds.ThreeDot)
        {
            ts.IsOn = true; // Force stay on
            btn.IsEnabled = true;
            return;
        }
        SaveCardButtonsFromList();
    }

    private void CardButtons_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        EnsureThreeDotPosition();
        SaveCardButtonsFromList();
    }

    private void SaveCardButtonsFromList()
    {
        CurrentSettings.CardButtons = CardButtonsList
            .Where(b => b.IsEnabled)
            .Select(b => b.Id)
            .ToList();
            
        ApplyWallpaperPresentation();
        TriggerSaveSettings();
    }

    private void ApplyCompactHeaderVisibility()
    {
        var visible = CurrentSettings.CardSize == CardSizeOptions.Large
            ? Visibility.Collapsed
            : Visibility.Visible;
        HomeCompactListHeader.Visibility = visible;
        LibraryCompactListHeader.Visibility = visible;
    }

    private void ApplyColumnToggleState()
    {
        var hiddenColumns = CurrentSettings.HiddenLibraryColumns.ToHashSet(StringComparer.OrdinalIgnoreCase);

        PreviewColumnToggle.IsChecked = !hiddenColumns.Contains(PreviewColumn);
        LocalNameColumnToggle.IsChecked = !hiddenColumns.Contains(LocalNameColumn);
        IdColumnToggle.IsChecked = !hiddenColumns.Contains(IdColumn);
        SizeColumnToggle.IsChecked = !hiddenColumns.Contains(SizeColumn);
        TagsColumnToggle.IsChecked = !hiddenColumns.Contains(TagsColumn);
        ApplyColumnVisibility();
    }

    private void ApplyLibraryViewMode(string viewMode)
    {
        var isThumbnail = viewMode == LibraryViewModes.Thumbnail;
        LibraryListView.Visibility = isThumbnail ? Visibility.Collapsed : Visibility.Visible;
        LibraryThumbnailView.Visibility = isThumbnail ? Visibility.Visible : Visibility.Collapsed;
        ApplyWallpaperPresentation();
    }

    private void ApplyHomeViewMode(string viewMode)
    {
        var isThumbnail = viewMode == LibraryViewModes.Thumbnail;
        HomeListView.Visibility = isThumbnail ? Visibility.Collapsed : Visibility.Visible;
        HomeThumbnailView.Visibility = isThumbnail ? Visibility.Visible : Visibility.Collapsed;
        ApplyWallpaperPresentation();
    }

    private void ApplyHomeSortMode()
    {
        if (HomeListView == null || HomeThumbnailView == null) return;

        var canReorder = CurrentSettings.HomeSortMode == "Free Movement";
        HomeListView.CanDragItems = canReorder;
        HomeListView.CanReorderItems = canReorder;
        HomeListView.AllowDrop = canReorder;
        
        HomeThumbnailView.CanDragItems = canReorder;
        HomeThumbnailView.CanReorderItems = canReorder;
        HomeThumbnailView.AllowDrop = canReorder;
    }

    private Brush GetWallpaperRowBrush(WallpaperItem wallpaper)
    {
        if (!CurrentSettings.ColorRowsByHighestPriorityTag)
        {
            return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }

        foreach (var tag in Tags)
        {
            if (!wallpaper.Tags.Any(item => string.Equals(item, tag.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var color = ParseHexColor(tag.Color);
            return new SolidColorBrush(Color.FromArgb(34, color.R, color.G, color.B));
        }

        return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    private void ShowPage(string page)
    {
        HomePage.Visibility = page == "Home" ? Visibility.Visible : Visibility.Collapsed;
        LibraryPage.Visibility = page == "Library" ? Visibility.Visible : Visibility.Collapsed;
        DownloaderPage.Visibility = page == "Downloader" ? Visibility.Visible : Visibility.Collapsed;
        GuidePage.Visibility = page == "Guide" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        if (page == "Settings")
        {
            if (SettingsNavigationList.SelectedItem == null && SettingsCategories.Count > 0)
            {
                SettingsNavigationList.SelectedIndex = 0;
            }
        }

        PageTitle.Text = _showingNsfwTab ? "NSFW / Mature" : page;
        PageSubtitle.Text = page switch
        {
            "Library" => _showingNsfwTab ? "Your adult and mature wallpapers." : "All detected wallpapers within every configured directory.",
            "Downloader" => "Directly download Steam Workshop wallpapers into your library.",
            "Guide" => "Folder naming, scanning rules, and practical usage notes.",
            "Settings" => "Engine and wallpaper, appearance, library, and tags.",
            _ => "Selected wallpapers from your local Wallpaper Engine library."
        };
    }

    private void ShowSettingsSection(string? section)
    {
        EngineWallpaperSettingsPanel.Visibility = section == "EngineWallpaper" ? Visibility.Visible : Visibility.Collapsed;
        AppearanceSettingsPanel.Visibility = section == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
        LibrarySettingsPanel.Visibility = section == "Library" ? Visibility.Visible : Visibility.Collapsed;
        NsfwMatureSettingsPanel.Visibility = section == "NsfwMature" ? Visibility.Visible : Visibility.Collapsed;
        TagSettingsPanel.Visibility = section == "Tags" ? Visibility.Visible : Visibility.Collapsed;
        AboutSettingsPanel.Visibility = section == "About" ? Visibility.Visible : Visibility.Collapsed;
        ContactSettingsPanel.Visibility = section == "Contact" ? Visibility.Visible : Visibility.Collapsed;

        if (section == "NsfwMature")
        {
            UpdatePreviewWallpapers();
            ApplyWallpaperPresentation();
        }
    }

    private DispatcherTimer? _saveSettingsTimer;

    private void TriggerSaveSettings()
    {
        if (_saveSettingsTimer == null)
        {
            _saveSettingsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _saveSettingsTimer.Tick += async (_, _) =>
            {
                _saveSettingsTimer.Stop();
                try
                {
                    // Update collection-based settings that aren't easily tracked by single handlers
                    CurrentSettings.Tags = Tags.ToList();
                    CurrentSettings.WallpaperDirectories = LibraryRoots.ToList();

                    await _settingsStore.SaveAsync(CurrentSettings);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Save failed: {ex.Message}");
                }
            };
        }

        _saveSettingsTimer.Stop();
        _saveSettingsTimer.Start();
    }

    private void ScanLibraryFromSettings()
    {
        // This method is now only for refreshing state from the CurrentSettings object
        // No longer scraping UI as it caused race conditions/toggle issues.
        ApplyBackdrop(true);
        ApplyTheme(CurrentSettings.Theme);
        ApplyWallpaperPresentation();
        RefreshVisibleWallpapers();
    }
    private void UpdateEmptyStates()
    {
        EmptyHomeInfo.IsOpen = SelectedWallpapers.Count == 0;
        EmptyLibraryInfo.IsOpen = VisibleWallpapers.Count == 0;
        HomeCountText.Text = $"{SelectedWallpapers.Count} selected";
        LibraryCountText.Text = $"{VisibleWallpapers.Count} wallpapers";
    }

    private bool ShouldShowWallpaper(WallpaperItem wallpaper)
    {
        if (_showingNsfwTab)
        {
            if (CurrentSettings.NsfwTabMode == NsfwTabMode.OnlyNsfw && wallpaper.IsNsfw && !wallpaper.IsMature) return true;
            if (CurrentSettings.NsfwTabMode == NsfwTabMode.NsfwAndMature && (wallpaper.IsNsfw || wallpaper.IsMature)) return true;
            return false;
        }
        else
        {
            // Respect Library Hide settings
            if (CurrentSettings.LibraryHideMode == LibraryHideMode.OnlyNsfw && wallpaper.IsNsfw) return false;
            if (CurrentSettings.LibraryHideMode == LibraryHideMode.NsfwAndMature && (wallpaper.IsNsfw || wallpaper.IsMature)) return false;

            if (CurrentSettings.NsfwTabMode == NsfwTabMode.OnlyNsfw && wallpaper.IsNsfw && !wallpaper.IsMature) return false;
            if (CurrentSettings.NsfwTabMode == NsfwTabMode.NsfwAndMature && (wallpaper.IsNsfw || wallpaper.IsMature)) return false;
            return true;
        }
    }

    private void UpdateEngineStatus()
    {
        var hasExecutable = File.Exists(CurrentSettings.EngineExecutablePath);
        var isRunning = hasExecutable && _engineService.IsRunning(CurrentSettings.EngineExecutablePath);

        EngineStatusGlyph.Text = isRunning ? "\u2713" : "!";
        EngineStatusText.Text = isRunning
            ? "Engine running"
            : hasExecutable ? "Engine not running" : "Engine not configured";
        StartEngineButton.Visibility = hasExecutable && !isRunning ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyTheme(string theme)
    {
        RootGrid.RequestedTheme = theme switch
        {
            ThemeOptions.Light => ElementTheme.Light,
            ThemeOptions.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }

    private void ApplyThemeColor(string colorHex)
    {
        var color = ParseHexColor(colorHex);
        var accentBrush = new SolidColorBrush(color);
        Navigation.Resources["SystemAccentColor"] = color;
        Navigation.Resources["SystemAccentColorLight1"] = color;
        Navigation.Resources["SystemAccentColorLight2"] = color;
        Navigation.Resources["SystemAccentColorLight3"] = color;
        Navigation.Resources["SystemAccentColorDark1"] = color;
        Navigation.Resources["SystemAccentColorDark2"] = color;
        Navigation.Resources["SystemAccentColorDark3"] = color;
        Navigation.Resources["SystemAccentColorBrush"] = accentBrush;
        RootGrid.Resources["SystemAccentColor"] = color;
        RootGrid.Resources["SystemAccentColorBrush"] = accentBrush;

        MicaTintOverlay.Background = new SolidColorBrush(Color.FromArgb(36, color.R, color.G, color.B));
    }

    private void InitializePicker(object picker)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (hwnd == System.IntPtr.Zero)
        {
            System.Diagnostics.Debug.WriteLine("Failed to get window handle for picker.");
            return;
        }
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
    }

    private void ShowContactStatus(string title, string message, InfoBarSeverity severity)
    {
        ContactInfoBar.Title = title;
        ContactInfoBar.Message = message;
        ContactInfoBar.Severity = severity;
        ContactInfoBar.IsOpen = true;
    }

    private static string BuildContactMessage(string mail, string discord, string message)
    {
        var builder = new StringBuilder();
        builder.AppendLine("**Wallpaper Manager contact**");
        builder.AppendLine($"Mail: {(string.IsNullOrWhiteSpace(mail) ? "Not provided" : mail)}");
        builder.AppendLine($"Discord: {(string.IsNullOrWhiteSpace(discord) ? "Not provided" : discord)}");
        builder.AppendLine();
        builder.AppendLine(message.Length > 1500 ? message[..1500] : message);
        return builder.ToString();
    }

    private bool IsPageSelected(string page)
    {
        return (Navigation.SelectedItem as NavigationViewItem)?.Tag as string == page;
    }

    private void SelectNavigationPage(string page)
    {
        foreach (var item in Navigation.MenuItems.OfType<NavigationViewItem>())
        {
            if ((item.Tag as string) == page)
            {
                Navigation.SelectedItem = item;
                return;
            }
        }
    }

    private void SyncCardSizeSelectors(string cardSize)
    {
        if (CardSizeComboBox.SelectedItem as string != cardSize)
        {
            CardSizeComboBox.SelectedItem = cardSize;
        }

        if (HomeCardSizeComboBox.SelectedItem as string != cardSize)
        {
            HomeCardSizeComboBox.SelectedItem = cardSize;
        }

    }

    private static Visibility GetColumnVisibility(IReadOnlySet<string> hiddenColumns, string column)
    {
        return hiddenColumns.Contains(column) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static GridLength GetColumnWidth(IReadOnlySet<string> hiddenColumns, string column, GridLength visibleWidth)
    {
        return hiddenColumns.Contains(column) ? new GridLength(0) : visibleWidth;
    }

    private static string ToThemeMode(string theme)
    {
        return theme switch
        {
            ThemeOptions.Light => "Light",
            ThemeOptions.Dark => "Dark",
            _ => "Auto"
        };
    }

    private static string FromThemeMode(string themeMode)
    {
        return themeMode switch
        {
            "Light" => ThemeOptions.Light,
            "Dark" => ThemeOptions.Dark,
            _ => ThemeOptions.System
        };
    }

    private static string PresetToHex(string preset)
    {
        return preset switch
        {
            "Red" => "#C43B3B",
            "Green" => "#2E9F62",
            "Yellow" => "#CC9A1F",
            "Purple" => "#6A5ACD",
            "Blue" => "#3A7AFE",
            "Orange" => "#F7630C",
            "Pink" => "#E3008C",
            "Teal" => "#00B294",
            "Gray" => "#69797E",
            _ => "#3A7AFE"
        };
    }

    private static string ToThemeColorPreset(string color)
    {
        var normalized = NormalizeHexColor(color);
        return normalized switch
        {
            "#C43B3B" => "Red",
            "#2E9F62" => "Green",
            "#CC9A1F" => "Yellow",
            "#6A5ACD" => "Purple",
            "#3A7AFE" => "Blue",
            "#F7630C" => "Orange",
            "#E3008C" => "Pink",
            "#00B294" => "Teal",
            "#69797E" => "Gray",
            _ => "Custom"
        };
    }

    private static bool TryNormalizeHexColor(string value, out string normalized)
    {
        normalized = value.Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        if (!normalized.StartsWith('#'))
        {
            normalized = $"#{normalized}";
        }

        if (normalized.Length != 7)
        {
            return false;
        }

        var hex = normalized[1..];
        if (!hex.All(Uri.IsHexDigit))
        {
            return false;
        }

        normalized = normalized.ToUpperInvariant();
        return true;
    }

    private static string NormalizeHexColor(string value)
    {
        if (TryNormalizeHexColor(value, out var normalized))
        {
            return normalized;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return "#3A7AFE";
        }

        return "#3A7AFE";
    }

    private void RefreshVisibleTags()
    {
        var search = TagSearchTextBox.Text.Trim();
        IEnumerable<WallpaperTag> filteredTags = string.IsNullOrWhiteSpace(search)
            ? Tags
            : Tags.Where(tag => tag.Name.Contains(search, StringComparison.OrdinalIgnoreCase));

        VisibleTags.Clear();
        foreach (var tag in filteredTags)
        {
            VisibleTags.Add(tag);
        }

        var canReorder = string.IsNullOrWhiteSpace(search);
        TagsListView.CanDragItems = canReorder;
        TagsListView.CanReorderItems = canReorder;
    }

    private async Task OpenTagEditorAsync(WallpaperTag? tag)
    {
        var editingExisting = tag is not null;
        var nameBox = new TextBox
        {
            Header = editingExisting ? "Change tag name" : "New tag name",
            Text = tag?.Name ?? string.Empty
        };

        var colorBox = new TextBox
        {
            Header = editingExisting ? "Change tag color" : "New tag color",
            Text = tag?.Color ?? "#3A7AFE",
            Width = 200
        };

        var colorPicker = new ColorPicker
        {
            Color = ParseHexColor(colorBox.Text),
            IsAlphaEnabled = false,
            IsAlphaSliderVisible = false,
            IsAlphaTextInputVisible = false
        };

        var colorButtonRect = new Border { Background = new SolidColorBrush(ParseHexColor(colorBox.Text)), Width = 28, Height = 28, CornerRadius = new CornerRadius(4) };
        var colorButton = new Button
        {
            Content = colorButtonRect,
            Flyout = new Flyout { Content = colorPicker },
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
            Height = 32,
            Padding = new Thickness(0)
        };

        var colorPanel = new StackPanel { Orientation = Orientation.Horizontal };
        colorPanel.Children.Add(colorBox);
        colorPanel.Children.Add(colorButton);

        var previewCards = new StackPanel { Spacing = 8 };
        var previewSamples = Wallpapers
            .OrderBy(_ => Guid.NewGuid())
            .Take(3)
            .ToList();
        while (previewSamples.Count < 3)
        {
            previewSamples.Add(new WallpaperItem
            {
                LocalName = "Preview wallpaper",
                SteamId = "Local",
                SizeBytes = 0
            });
        }
        foreach (var sample in previewSamples)
        {
            var row = new Border
            {
                MinHeight = 168,
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 160, 160, 160)),
                Padding = new Thickness(16, 12, 16, 12)
            };

            var rowGrid = new Grid { ColumnSpacing = 12 };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var previewBox = new Grid { Width = 240, Height = 132 };
            previewBox.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromArgb(44, 120, 120, 120))
            });
            if (sample.PreviewImage is not null)
            {
                previewBox.Children.Add(new Image
                {
                    Source = sample.PreviewImage,
                    Stretch = Stretch.UniformToFill
                });
            }

            var details = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            details.Children.Add(new TextBlock
            {
                Text = sample.DisplayName,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 26,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            details.Children.Add(new TextBlock
            {
                Text = $"ID: {sample.IdText}",
                FontSize = 20,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });
            details.Children.Add(new TextBlock
            {
                Text = $"Size: {sample.SizeText}",
                FontSize = 20,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });

            rowGrid.Children.Add(previewBox);
            Grid.SetColumn(details, 1);
            rowGrid.Children.Add(details);
            row.Child = rowGrid;
            previewCards.Children.Add(row);
        }

        bool updatingColor = false;
        void UpdatePreview()
        {
            if (updatingColor) return;
            updatingColor = true;

            var color = ParseHexColor(colorBox.Text);
            var brush = new SolidColorBrush(Color.FromArgb(40, color.R, color.G, color.B));
            foreach (var child in previewCards.Children.OfType<Border>())
            {
                child.Background = brush;
            }
            colorButtonRect.Background = new SolidColorBrush(color);
            colorPicker.Color = color;
            
            updatingColor = false;
        }

        colorBox.TextChanged += (_, _) =>
        {
            if (TryNormalizeHexColor(colorBox.Text, out _))
            {
                UpdatePreview();
            }
        };
        colorPicker.ColorChanged += (_, args) =>
        {
            if (updatingColor) return;
            colorBox.Text = FormatHexColor(args.NewColor);
        };

        var editorLayout = new Grid
        {
            ColumnSpacing = 32
        };
        editorLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.40, GridUnitType.Star) });
        editorLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.60, GridUnitType.Star) });

        var leftPanel = new StackPanel
        {
            Spacing = 16,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        leftPanel.Children.Add(nameBox);
        leftPanel.Children.Add(colorPanel);

        var rightPanel = new StackPanel { Spacing = 16, HorizontalAlignment = HorizontalAlignment.Stretch };
        rightPanel.Children.Add(new TextBlock { Text = "Preview", FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        rightPanel.Children.Add(previewCards);

        editorLayout.Children.Add(leftPanel);
        Grid.SetColumn(rightPanel, 1);
        editorLayout.Children.Add(rightPanel);
        UpdatePreview();

        var dialog = new ContentDialog
        {
            Title = editingExisting ? "Modify tag" : "New tag",
            Content = new ScrollViewer { Content = editorLayout, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        if (RootGrid.XamlRoot != null)
        {
            dialog.XamlRoot = RootGrid.XamlRoot;
        }

        dialog.Resources["ContentDialogMaxWidth"] = 1000.0;

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var newName = nameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        var newColor = NormalizeHexColor(colorBox.Text);
        if (editingExisting && tag is not null)
        {
            var oldName = tag.Name;
            if (!string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase)
                && Tags.Any(item => !ReferenceEquals(item, tag) && string.Equals(item.Name, newName, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            tag.Name = newName;
            tag.Color = newColor;
            RefreshTagRow(tag);
            if (!string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var wallpaper in Wallpapers)
                {
                    for (var index = 0; index < wallpaper.Tags.Count; index++)
                    {
                        if (string.Equals(wallpaper.Tags[index], oldName, StringComparison.OrdinalIgnoreCase))
                        {
                            wallpaper.Tags[index] = newName;
                        }
                    }
                }
            }
        }
        else
        {
            if (Tags.Any(item => string.Equals(item.Name, newName, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            Tags.Add(new WallpaperTag
            {
                Name = newName,
                Color = newColor
            });
        }

        RefreshVisibleTags();
        ApplyWallpaperPresentation();
        RefreshVisibleWallpapers();
        RefreshSelectedWallpapers();
        TriggerSaveSettings();
    }

    private void RefreshTagRow(WallpaperTag tag)
    {
        var index = Tags.IndexOf(tag);
        if (index < 0)
        {
            return;
        }

        Tags.RemoveAt(index);
        Tags.Insert(index, tag);
    }

    private static (double Width, double PreviewHeight) GetCardSize(string cardSize)
    {
        return cardSize switch
        {
            CardSizeOptions.Small => (180, 96),
            CardSizeOptions.Large => (300, 170),
            _ => (250, 132)
        };
    }

    private void ApplySizePresentation(WallpaperItem wallpaper, IReadOnlySet<string> hiddenColumns)
    {
        (wallpaper.CardWidth, wallpaper.CardPreviewHeight) = GetCardSize(CurrentSettings.CardSize);
        if (CurrentSettings.CardSize == CardSizeOptions.Large)
        {
            var availableWidth = Math.Max(LibraryThumbnailView.ActualWidth, HomeThumbnailView.ActualWidth);
            if (availableWidth > 900)
            {
                const double cardGap = 12;
                var columns = Math.Max(3, (int)Math.Floor((availableWidth + cardGap) / (wallpaper.CardWidth + cardGap)));
                var targetWidth = Math.Floor((availableWidth - ((columns + 1) * cardGap)) / columns);
                wallpaper.CardWidth = Math.Clamp(targetWidth, 260, 360);
            }
        }

        var previewAllowed = !hiddenColumns.Contains(PreviewColumn);
        wallpaper.ListPreviewVisibility = CurrentSettings.CardSize == CardSizeOptions.Small || !previewAllowed
            ? Visibility.Collapsed
            : Visibility.Visible;
        wallpaper.DirectHomeActionVisibility = CurrentSettings.CardSize == CardSizeOptions.Small
            ? Visibility.Collapsed
            : Visibility.Visible;
        wallpaper.ListDetailsVisibility = Visibility.Visible;
        wallpaper.ThumbnailDetailsVisibility = CurrentSettings.CardSize == CardSizeOptions.Small
            ? Visibility.Collapsed
            : Visibility.Visible;
        wallpaper.LargeListLayoutVisibility = CurrentSettings.CardSize == CardSizeOptions.Large
            ? Visibility.Visible
            : Visibility.Collapsed;
        wallpaper.CompactListLayoutVisibility = CurrentSettings.CardSize == CardSizeOptions.Large
            ? Visibility.Collapsed
            : Visibility.Visible;

        switch (CurrentSettings.CardSize)
        {
            case CardSizeOptions.Large:
                wallpaper.ListPreviewWidth = 170;
                wallpaper.ListPreviewHeight = 130;
                wallpaper.ListRowMinHeight = 164;
                wallpaper.ListTitleFontSize = 22;
                wallpaper.ListRowPadding = new Thickness(20, 16, 16, 16);
                break;
            case CardSizeOptions.Small:
                wallpaper.ListPreviewWidth = 0;
                wallpaper.ListPreviewHeight = 0;
                wallpaper.ListRowMinHeight = 48;
                wallpaper.ListTitleFontSize = 15;
                wallpaper.ListRowPadding = new Thickness(12, 6, 12, 6);
                break;
            default:
                wallpaper.ListPreviewWidth = 96;
                wallpaper.ListPreviewHeight = 56;
                wallpaper.ListRowMinHeight = 82;
                wallpaper.ListTitleFontSize = 18;
                wallpaper.ListRowPadding = new Thickness(14, 10, 12, 10);
                break;
        }
    }

    private static string FormatHexColor(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static Color ParseHexColor(string value)
    {
        value = NormalizeHexColor(value).TrimStart('#');
        return Color.FromArgb(
            255,
            Convert.ToByte(value[..2], 16),
            Convert.ToByte(value.Substring(2, 2), 16),
            Convert.ToByte(value.Substring(4, 2), 16));
    }

    private sealed record WallpaperTagAction(WallpaperItem Wallpaper, string TagName);

    private static string GetContactWebhookUrl()
    {
        var environmentValue = Environment.GetEnvironmentVariable(ContactWebhookKey);
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return environmentValue.Trim();
        }

        foreach (var directory in GetEnvSearchDirectories())
        {
            var envPath = Path.Combine(directory, ".env");
            if (!File.Exists(envPath))
            {
                continue;
            }

            foreach (var line in File.ReadLines(envPath))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                {
                    continue;
                }

                var separatorIndex = trimmed.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var key = trimmed[..separatorIndex].Trim();
                if (!string.Equals(key, ContactWebhookKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return trimmed[(separatorIndex + 1)..].Trim().Trim('"');
            }
        }

        return string.Empty;
    }

    private static IEnumerable<string> GetEnvSearchDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (seen.Add(directory.FullName))
                {
                    yield return directory.FullName;
                }

                directory = directory.Parent;
            }
        }
    }

    private void ToggleMatureMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuFlyoutItem)?.Tag is not WallpaperItem wallpaper)
        {
            return;
        }

        wallpaper.IsMature = !wallpaper.IsMature;

        ApplyWallpaperPresentation();
        RefreshVisibleWallpapers();
        RefreshSelectedWallpapers();
        TriggerSaveSettings();
    }

    private async void WallpaperDetails_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not WallpaperItem wallpaper)
        {
            return;
        }

        if (wallpaper.WorkshopMetadata == null && !string.IsNullOrWhiteSpace(wallpaper.SteamId))
        {
            var fetchedMeta = await _workshopService.FetchAsync(wallpaper.SteamId);
            if (fetchedMeta != null)
            {
                wallpaper.WorkshopMetadata = fetchedMeta;
            }
        }

        var meta = wallpaper.WorkshopMetadata;
        
        var rootGrid = new Grid 
        { 
            ColumnSpacing = 24, 
            MinWidth = 800, 
            MinHeight = 450,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400) });
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
 
        // Left Side: Image + Basic Info
        var leftStack = new StackPanel { Spacing = 12 };
        
        var previewCard = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"],
            Height = 225,
            Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, 400, 225) }
        };
        
        var previewUri = string.IsNullOrWhiteSpace(wallpaper.PreviewPath) 
            ? new Uri("ms-appx:///Assets/StoreLogo.png")
            : new Uri("file:///" + wallpaper.PreviewPath.Replace("\\", "/"));

        previewCard.Child = new Image
        {
            Source = new BitmapImage(previewUri),
            Stretch = Stretch.UniformToFill
        };
        leftStack.Children.Add(previewCard);

        var infoCard = new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16)
        };
        var infoInner = new StackPanel { Spacing = 8 };
        infoInner.Children.Add(new TextBlock
        {
            Text = wallpaper.DisplayName,
            Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
            TextWrapping = TextWrapping.Wrap
        });
        
        var detailsGrid = new Grid { ColumnSpacing = 16 };
        detailsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        detailsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var leftDetails = new StackPanel { Spacing = 4 };
        leftDetails.Children.Add(new TextBlock { Text = $"ID: {wallpaper.IdText}", Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        leftDetails.Children.Add(new TextBlock { Text = $"Size: {wallpaper.SizeText}", Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });

        var rightDetails = new StackPanel { Spacing = 4 };
        if (wallpaper.IsNsfw) rightDetails.Children.Add(new TextBlock { Text = "NSFW", Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red) });
        if (wallpaper.IsMature) rightDetails.Children.Add(new TextBlock { Text = "Mature", Foreground = new SolidColorBrush(Microsoft.UI.Colors.Orange) });
        if (wallpaper.Tags.Count > 0) rightDetails.Children.Add(new TextBlock { Text = $"Tags: {wallpaper.TagsText}", Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"], TextWrapping = TextWrapping.Wrap });

        detailsGrid.Children.Add(leftDetails);
        Grid.SetColumn(leftDetails, 0);
        detailsGrid.Children.Add(rightDetails);
        Grid.SetColumn(rightDetails, 1);

        infoInner.Children.Add(detailsGrid);
        infoCard.Child = infoInner;
        leftStack.Children.Add(infoCard);
        
        rootGrid.Children.Add(leftStack);
        Grid.SetColumn(leftStack, 0);

        // Right Side: Workshop Metadata
        var rightStack = new StackPanel { Spacing = 12 };
        if (meta != null)
        {
            var statsCard = new Border
            {
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16)
            };

            var statsGrid = new Grid { RowSpacing = 8, ColumnSpacing = 16 };
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            statsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var titleBlock = new TextBlock
            {
                Text = "Workshop Info",
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                Margin = new Thickness(0, 0, 0, 8)
            };
            
            var s1 = new StackPanel { Spacing = 4 };
            s1.Children.Add(new TextBlock { Text = $"Subscribers: {meta.SubscriptionCount:N0}" });
            s1.Children.Add(new TextBlock { Text = $"Favorites: {meta.FavoriteCount:N0}" });
            
            var s2 = new StackPanel { Spacing = 4 };
            s2.Children.Add(new TextBlock { Text = $"Views: {meta.ViewCount:N0}" });
            if (!string.IsNullOrWhiteSpace(meta.ContentRating))
                s2.Children.Add(new TextBlock { Text = $"Rating: {meta.ContentRating}" });

            statsGrid.Children.Add(s1);
            Grid.SetColumn(s2, 1);
            statsGrid.Children.Add(s2);
            Grid.SetRow(s1, 1);
            Grid.SetRow(s2, 1);

            var statsOuter = new StackPanel();
            statsOuter.Children.Add(titleBlock);
            if (!string.IsNullOrWhiteSpace(meta.Title))
                statsOuter.Children.Add(new TextBlock { Text = $"Title: {meta.Title}", Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap });
            statsOuter.Children.Add(statsGrid);
            if (meta.Tags.Count > 0)
                statsOuter.Children.Add(new TextBlock { Text = $"Tags: {string.Join(", ", meta.Tags)}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
            
            statsCard.Child = statsOuter;
            rightStack.Children.Add(statsCard);

            if (meta.Description is { Length: > 0 } desc)
            {
                var descCard = new Border
                {
                    Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(16),
                    VerticalAlignment = VerticalAlignment.Stretch
                };

                var descInner = new StackPanel { Spacing = 8 };
                descInner.Children.Add(new TextBlock
                {
                    Text = "Description",
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                });

                var truncated = desc.Length > 1000 ? desc[..1000] + "…" : desc;
                descInner.Children.Add(new TextBlock
                {
                    Text = truncated,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 13
                });

                descCard.Child = descInner;
                rightStack.Children.Add(descCard);
            }
        }
        else if (!string.IsNullOrWhiteSpace(wallpaper.SteamId))
        {
            rightStack.Children.Add(new InfoBar
            {
                IsOpen = true,
                Severity = InfoBarSeverity.Warning,
                Title = "Workshop metadata unavailable",
                Message = "Could not fetch details from Steam. The item might be private, deleted, or you might be offline."
            });
        }

        Grid.SetColumn(rightStack, 1);
        rootGrid.Children.Add(rightStack);


        var dialog = new ContentDialog
        {
            Title = "Wallpaper Details",
            Content = new ScrollViewer 
            { 
                Content = rootGrid, 
                MaxHeight = 600,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            },
            CloseButtonText = "Close",
            XamlRoot = RootGrid.XamlRoot
        };

        // WinUI 3 ContentDialog has a built-in max width of 548px via theme resource.
        // The only way to widen it is to override this resource on the dialog instance.
        dialog.Resources["ContentDialogMaxWidth"] = 900.0;

        await dialog.ShowAsync();
    }

    private async void RemoveHomeCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: WallpaperItem wallpaper })
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Remove Wallpaper",
            Content = $"Are you sure you want to remove '{wallpaper.DisplayName}' from your home page?",
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            CurrentSettings.SelectedWallpaperKeys.Remove(wallpaper.Key);
            wallpaper.IsSelected = false;
            TriggerSaveSettings();
            RefreshSelectedWallpapers();
            RefreshVisibleWallpapers();
        }
    }

    private void HomeSortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || sender is not ComboBox comboBox || comboBox.SelectedItem is not string sortMode)
            return;

        CurrentSettings.HomeSortMode = sortMode;
        ApplyHomeSortMode();
        RefreshSelectedWallpapers();
        TriggerSaveSettings();
    }

    private void LibrarySortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || sender is not ComboBox comboBox || comboBox.SelectedItem is not string sortMode)
            return;

        CurrentSettings.LibrarySortMode = sortMode;
        RefreshVisibleWallpapers();
        TriggerSaveSettings();
    }

    private void HomeView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (CurrentSettings.HomeSortMode != "Free Movement")
            return;

        // Save the new order
        CurrentSettings.SelectedWallpaperKeys = SelectedWallpapers
            .Select(wallpaper => wallpaper.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        TriggerSaveSettings();
    }

    private async void DynamicCardButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is WallpaperItem item)
        {
            var buttonId = btn.Tag?.ToString();
            if (string.IsNullOrEmpty(buttonId)) return;

            switch (buttonId)
            {
                case CardButtonIds.ThreeDot:
                    ShowWallpaperActions(btn, item);
                    break;
                case CardButtonIds.AddTag:
                    ShowAddTagFlyout(btn, item);
                    break;
                case CardButtonIds.AddToHome:
                    ToggleHomeStatus(item);
                    break;
                case CardButtonIds.Delete:
                    await DeleteWallpaperAsync(item);
                    break;
                case CardButtonIds.Details:
                    ShowWallpaperDetails(item);
                    break;
            }
        }
    }

    private void ShowWallpaperActions(FrameworkElement anchor, WallpaperItem item)
    {
        var menu = new MenuFlyout();
        
        // Always include basic actions if they aren't already visible on the card
        AddDynamicMenuItem(menu, CardButtonIds.AddToHome, item);
        AddDynamicMenuItem(menu, CardButtonIds.Details, item);
        AddDynamicMenuItem(menu, CardButtonIds.AddTag, item);

        if (item.OverflowButtons.Count > 0)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            foreach (var btnId in item.OverflowButtons)
            {
                // Avoid duplicates if we already added them above as basic actions
                if (btnId == CardButtonIds.ThreeDot || btnId == CardButtonIds.AddToHome || btnId == CardButtonIds.Details || btnId == CardButtonIds.AddTag)
                    continue;

                AddDynamicMenuItem(menu, btnId, item);
            }
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        
        var runItem = new MenuFlyoutItem { Text = "Run Wallpaper", Icon = new SymbolIcon(Symbol.Play) };
        runItem.Click += (_, _) => RunWallpaper(item);
        menu.Items.Add(runItem);

        var openFolderItem = new MenuFlyoutItem { Text = "Open Folder", Icon = new SymbolIcon(Symbol.Folder) };
        openFolderItem.Click += (_, _) => OpenWallpaperFolder(item);
        menu.Items.Add(openFolderItem);

        // Delete is special and usually at the bottom
        if (item.OverflowButtons.Contains(CardButtonIds.Delete) || !CurrentSettings.CardButtons.Contains(CardButtonIds.Delete))
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            AddDynamicMenuItem(menu, CardButtonIds.Delete, item);
        }

        menu.ShowAt(anchor);
    }

    private void AddDynamicMenuItem(MenuFlyout menu, string buttonId, WallpaperItem item)
    {
        var text = buttonId switch
        {
            CardButtonIds.AddToHome => item.IsSelected ? "Remove from Home" : "Add to Home",
            CardButtonIds.AddTag => "Mark / Add Tags",
            CardButtonIds.Delete => "Delete Wallpaper",
            CardButtonIds.Details => "Wallpaper Details",
            _ => string.Empty
        };

        if (string.IsNullOrEmpty(text)) return;

        IconElement? icon = buttonId switch
        {
            CardButtonIds.AddToHome => new FontIcon { Glyph = item.HomeActionGlyph },
            CardButtonIds.AddTag => new SymbolIcon(Symbol.Tag),
            CardButtonIds.Delete => new SymbolIcon(Symbol.Delete),
            CardButtonIds.Details => new SymbolIcon(Symbol.List),
            _ => null
        };

        var menuItem = new MenuFlyoutItem { Text = text, Icon = icon };
        if (buttonId == CardButtonIds.Delete) menuItem.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);

        menuItem.Click += async (s, e) =>
        {
            switch (buttonId)
            {
                case CardButtonIds.AddTag: ShowAddTagFlyout(anchor: null!, item); break; // Anchor is used for flyout positioning, might need care
                case CardButtonIds.AddToHome: ToggleHomeStatus(item); break;
                case CardButtonIds.Delete: await DeleteWallpaperAsync(item); break;
                case CardButtonIds.Details: ShowWallpaperDetails(item); break;
            }
        };

        // If we don't have an anchor for AddTag flyout when called from menu, we might need to adjust ShowAddTagFlyout
        if (buttonId == CardButtonIds.AddTag)
        {
            menuItem.Click -= (s, e) => { }; // Clear previous and re-add with proper logic
            menuItem.Click += (s, e) => ShowAddTagFlyout(menu.Target, item);
        }

        menu.Items.Add(menuItem);
    }

    private void ShowAddTagFlyout(FrameworkElement anchor, WallpaperItem item)
    {
        var flyout = new Flyout();
        var panel = new StackPanel { Spacing = 10, Width = 200, Padding = new Thickness(5) };

        panel.Children.Add(new TextBlock { Text = "Mark Content", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        
        var markNsfwBtn = new Button { Content = item.IsNsfw ? "Unmark NSFW" : "Mark NSFW", HorizontalAlignment = HorizontalAlignment.Stretch };
        markNsfwBtn.Click += (_, _) => { item.IsNsfw = !item.IsNsfw; TriggerSaveSettings(); ApplyWallpaperPresentation(); flyout.Hide(); };
        
        var markMatureBtn = new Button { Content = item.IsMature ? "Unmark Mature" : "Mark Mature", HorizontalAlignment = HorizontalAlignment.Stretch };
        markMatureBtn.Click += (_, _) => { item.IsMature = !item.IsMature; TriggerSaveSettings(); ApplyWallpaperPresentation(); flyout.Hide(); };

        panel.Children.Add(markNsfwBtn);
        panel.Children.Add(markMatureBtn);
        
        panel.Children.Add(new MenuFlyoutSeparator());
        panel.Children.Add(new TextBlock { Text = "Add Local Tag", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });

        var tagSearch = new AutoSuggestBox { PlaceholderText = "Search/Add tag", QueryIcon = new SymbolIcon(Symbol.Find) };
        tagSearch.ItemsSource = Tags.Select(t => t.Name).ToList();
        tagSearch.SuggestionChosen += (s, a) => { 
            var tagName = a.SelectedItem?.ToString();
            if (!string.IsNullOrEmpty(tagName) && !item.Tags.Contains(tagName)) { item.Tags.Add(tagName); TriggerSaveSettings(); ApplyWallpaperPresentation(); }
            flyout.Hide();
        };

        panel.Children.Add(tagSearch);

        flyout.Content = panel;
        flyout.ShowAt(anchor);
    }

    private void ShowWallpaperDetails(WallpaperItem item)
    {
        WallpaperDetails_Click(new Button { Tag = item }, new RoutedEventArgs());
    }

    private async Task DeleteWallpaperAsync(WallpaperItem item)
    {
        var dialog = new ContentDialog
        {
            Title = "Delete Wallpaper",
            Content = $"Are you sure you want to permanently delete '{item.DisplayName}'?\nThis will remove the files from your computer.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            try
            {
                if (Directory.Exists(item.DirectoryPath))
                {
                    Directory.Delete(item.DirectoryPath, true);
                }
                
                VisibleWallpapers.Remove(item);
                SelectedWallpapers.Remove(item);
                CurrentSettings.SelectedWallpaperKeys.Remove(item.Key);
                
                TriggerSaveSettings();
            }
            catch (Exception ex)
            {
                var errorDialog = new ContentDialog
                {
                    Title = "Delete Failed",
                    Content = $"Could not delete wallpaper: {ex.Message}",
                    CloseButtonText = "OK",
                    XamlRoot = RootGrid.XamlRoot
                };
                await errorDialog.ShowAsync();
            }
        }
    }

    private void ToggleHomeStatus(WallpaperItem item)
    {
        item.IsSelected = !item.IsSelected;
        
        if (item.IsSelected)
        {
            if (!CurrentSettings.SelectedWallpaperKeys.Contains(item.Key))
            {
                CurrentSettings.SelectedWallpaperKeys.Add(item.Key);
            }
        }
        else
        {
            CurrentSettings.SelectedWallpaperKeys.Remove(item.Key);
        }

        RefreshSelectedWallpapers();
        TriggerSaveSettings();
    }
}
