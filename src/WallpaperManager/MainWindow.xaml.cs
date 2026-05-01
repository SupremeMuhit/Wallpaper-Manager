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
    private readonly DispatcherTimer _engineStatusTimer = new();
    private MicaBackdrop? _micaBackdrop;
    private bool _isLoadingSettings;

    public ObservableCollection<WallpaperLibraryRoot> LibraryRoots { get; } = [];

    public ObservableCollection<WallpaperItem> Wallpapers { get; } = [];

    public ObservableCollection<WallpaperItem> VisibleWallpapers { get; } = [];

    public ObservableCollection<WallpaperItem> SelectedWallpapers { get; } = [];

    public ObservableCollection<WallpaperTag> Tags { get; } = [];

    public ObservableCollection<WallpaperTag> VisibleTags { get; } = [];

    public IReadOnlyList<string> ThemeChoices { get; } = ThemeOptions.All;

    public IReadOnlyList<string> LibraryViewChoices { get; } = LibraryViewModes.All;

    public IReadOnlyList<string> CardSizeChoices { get; } = CardSizeOptions.All;

    public AppSettings CurrentSettings { get; private set; } = new();

    public WallpaperItem NsfwPreviewItem { get; } = new() { IsNsfw = true, LocalName = "NSFW Preview" };
    public WallpaperItem MaturePreviewItem { get; } = new() { IsMature = true, LocalName = "Mature Preview" };

    public IReadOnlyList<string> LibrarySortChoices { get; } = ["Name", "Date Added", "Workshop Updated"];
    public IReadOnlyList<string> HomeSortChoices { get; } = ["Free Movement", "Name", "Date Added", "Workshop Updated"];

    public IReadOnlyList<string> NsfwTabChoices { get; } = ["Off", "Only NSFW", "NSFW and Mature"];

    public ObservableCollection<SettingsCategory> SettingsCategories { get; } =
    [
        new() { Tag = "EngineWallpaper", Title = "Engine and Wallpaper", Icon = "\uE8A7" },
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

        LoadSettings();
    }

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
        CensorshipIntensitySlider.Value = CurrentSettings.CensorshipIntensity;
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
        await SaveSettingsAsync();
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
        await SaveSettingsAsync();
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
        await SaveSettingsAsync();
        UpdateEngineStatus();
    }

    private async void EnginePathTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        CurrentSettings.EngineExecutablePath = EnginePathTextBox.Text;
        await SaveSettingsAsync();
        UpdateEngineStatus();
    }

    private async void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || ThemeComboBox.SelectedItem is not string themeMode)
        {
            return;
        }

        CurrentSettings.Theme = FromThemeMode(themeMode);
        ApplyTheme(CurrentSettings.Theme);
        await SaveSettingsAsync();
    }

    private async void LibraryViewComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || LibraryViewComboBox.SelectedItem is not string viewMode)
        {
            return;
        }

        CurrentSettings.LibraryViewMode = viewMode;
        ApplyLibraryViewMode(viewMode);
        await SaveSettingsAsync();
    }

    private async void HomeViewComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || HomeViewComboBox.SelectedItem is not string viewMode)
        {
            return;
        }

        CurrentSettings.HomeViewMode = viewMode;
        ApplyHomeViewMode(viewMode);
        await SaveSettingsAsync();
    }

    private async void CardSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || (sender as ComboBox)?.SelectedItem is not string cardSize)
        {
            return;
        }

        CurrentSettings.CardSize = cardSize;
        SyncCardSizeSelectors(cardSize);
        ApplyWallpaperPresentation();
        RefreshVisibleWallpapers();
        RefreshSelectedWallpapers();
        await SaveSettingsAsync();
    }

    private async void ColorRowsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        CurrentSettings.ColorRowsByHighestPriorityTag = ColorRowsToggle.IsOn;
        ApplyWallpaperPresentation();
        RefreshVisibleWallpapers();
        RefreshSelectedWallpapers();
        await SaveSettingsAsync();
    }

    private async void NsfwTabComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        CurrentSettings.NsfwTabMode = (NsfwTabMode)NsfwTabComboBox.SelectedIndex;
        NsfwNavTab.Visibility = CurrentSettings.NsfwTabMode != NsfwTabMode.Off ? Visibility.Visible : Visibility.Collapsed;
        RefreshVisibleWallpapers();
        await SaveSettingsAsync();
    }

    private async void RunOnStartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        CurrentSettings.RunOnStartup = RunOnStartupToggle.IsOn;
        await SaveSettingsAsync();
    }

    private async void MemoryUsageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || MemoryUsageComboBox.SelectedItem is not string memoryProfile)
        {
            return;
        }

        CurrentSettings.MemoryUsageProfile = memoryProfile;
        await SaveSettingsAsync();
    }

    private async void ThemeColorPresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
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

        await SaveSettingsAsync();
    }

    private async void ThemeColorTextBox_TextChanged(object sender, TextChangedEventArgs e)
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
        await SaveSettingsAsync();
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
        await SaveSettingsAsync();
    }

    private async void PrioritizeWorkshopNameToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings) return;
        CurrentSettings.PrioritizeWorkshopName = PrioritizeWorkshopNameToggle.IsOn;
        ApplyWallpaperPresentation();
        RefreshVisibleWallpapers();
        await SaveSettingsAsync();
    }

    private async void AutoMarkNsfwToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings) return;
        CurrentSettings.AutoMarkNsfwFromWorkshop = AutoMarkNsfwToggle.IsOn;
        await SaveSettingsAsync();
    }

    private async void RemoveCensorOnHoverToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings) return;
        CurrentSettings.RemoveCensorOnHover = RemoveCensorOnHoverToggle.IsOn;
        ApplyWallpaperPresentation();
        await SaveSettingsAsync();
    }

    private async void NsfwModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings) return;
        CurrentSettings.NsfwMode = (CensorshipMode)NsfwModeComboBox.SelectedIndex;
        ApplyWallpaperPresentation();
        await SaveSettingsAsync();
    }

    private async void MatureModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings) return;
        CurrentSettings.MatureMode = (CensorshipMode)MatureModeComboBox.SelectedIndex;
        ApplyWallpaperPresentation();
        await SaveSettingsAsync();
    }

    private async void CensorshipIntensitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoadingSettings) return;
        CurrentSettings.CensorshipIntensity = CensorshipIntensitySlider.Value;
        ApplyWallpaperPresentation();
        await SaveSettingsAsync();
    }

    private async void UseWorkshopTagsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings) return;
        CurrentSettings.UseWorkshopTags = UseWorkshopTagsToggle.IsOn;
        ApplyWallpaperPresentation();
        RefreshVisibleWallpapers();
        await SaveSettingsAsync();
    }

    private async void ColumnToggle_Click(object sender, RoutedEventArgs e)
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
        await SaveSettingsAsync();
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
        await SaveSettingsAsync();
    }

    private async void MoveTagUp_Click(object sender, RoutedEventArgs e)
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
        await SaveSettingsAsync();
        ApplyWallpaperPresentation();
        RefreshVisibleWallpapers();
        RefreshSelectedWallpapers();
    }

    private async void TagsListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
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

        await SaveSettingsAsync();
        ApplyWallpaperPresentation();
        RefreshVisibleWallpapers();
        RefreshSelectedWallpapers();
    }

    private async void MoveTagDown_Click(object sender, RoutedEventArgs e)
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
        await SaveSettingsAsync();
        ApplyWallpaperPresentation();
        RefreshVisibleWallpapers();
        RefreshSelectedWallpapers();
    }

    private async void RemoveTag_Click(object sender, RoutedEventArgs e)
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

        await SaveSettingsAsync();
        ApplyWallpaperPresentation();
        RefreshVisibleWallpapers();
        RefreshSelectedWallpapers();
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

    private async void ToggleHomeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuFlyoutItem)?.Tag is not WallpaperItem wallpaper)
        {
            return;
        }

        wallpaper.IsSelected = !wallpaper.IsSelected;
        RefreshSelectedWallpapers();
        await SaveSettingsAsync();
    }

    private async void ToggleHomeButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not WallpaperItem wallpaper)
        {
            return;
        }

        wallpaper.IsSelected = !wallpaper.IsSelected;
        RefreshVisibleWallpapers();
        RefreshSelectedWallpapers();
        await SaveSettingsAsync();
    }



    private async void ToggleNsfwMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuFlyoutItem)?.Tag is not WallpaperItem wallpaper)
        {
            return;
        }

        wallpaper.IsNsfw = !wallpaper.IsNsfw;

        ApplyWallpaperPresentation();
        RefreshVisibleWallpapers();
        RefreshSelectedWallpapers();
        await SaveSettingsAsync();
    }

    private async void ToggleWallpaperTagMenuItem_Click(object sender, RoutedEventArgs e)
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
        await SaveSettingsAsync();
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

        ShowPage(page);
        RefreshVisibleWallpapers();
    }

    private bool _showingNsfwTab = false;

    private async Task ScanLibraryAsync()
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
            await SaveSettingsAsync();
        }
    }

    private void RefreshVisibleWallpapers()
    {
        VisibleWallpapers.Clear();
        var sorted = SortWallpapers(Wallpapers.Where(ShouldShowWallpaper), CurrentSettings.LibrarySortMode);
        foreach (var wallpaper in sorted)
        {
            VisibleWallpapers.Add(wallpaper);
        }

        UpdateEmptyStates();
    }

    private void RefreshSelectedWallpapers()
    {
        SelectedWallpapers.Clear();
        var selected = Wallpapers.Where(item => item.IsSelected && ShouldShowWallpaper(item));
        
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
            _ => items.OrderBy(w => w.DisplayName, StringComparer.CurrentCultureIgnoreCase)
        };
    }

    private void ApplyWallpaperPresentation()
    {
        var hiddenColumns = CurrentSettings.HiddenLibraryColumns.ToHashSet(StringComparer.OrdinalIgnoreCase);

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
        }

        ApplyCensorship(NsfwPreviewItem);
        ApplyCensorship(MaturePreviewItem);

        ApplyColumnVisibility();
        ApplyCompactHeaderVisibility();
    }

    private void ApplyCensorship(WallpaperItem item)
    {
        var mode = item.IsNsfw ? CurrentSettings.NsfwMode : (item.IsMature ? CurrentSettings.MatureMode : CensorshipMode.Off);
        var intensity = CurrentSettings.CensorshipIntensity;

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
                item.BlurOpacity = 1.0;
                item.CensorshipOverlayOpacity = 0.3 + (0.7 * intensity);
                item.NsfwOverlayVisibility = Visibility.Collapsed;
                item.MatureOverlayVisibility = Visibility.Collapsed;
                item.BlurOverlayVisibility = Visibility.Visible;
                break;
            case CensorshipMode.Overlay:
                item.BlurOpacity = 1.0;
                item.CensorshipOverlayOpacity = 0.3 + (0.7 * intensity);
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
    }

    private void ApplyHomeViewMode(string viewMode)
    {
        var isThumbnail = viewMode == LibraryViewModes.Thumbnail;
        HomeListView.Visibility = isThumbnail ? Visibility.Collapsed : Visibility.Visible;
        HomeThumbnailView.Visibility = isThumbnail ? Visibility.Visible : Visibility.Collapsed;
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
    }

    private async Task SaveSettingsAsync()
    {
        ScanLibraryFromSettings();
        await _settingsStore.SaveAsync(CurrentSettings);
    }

    private void ScanLibraryFromSettings()
    {
        CurrentSettings.WallpaperDirectories = LibraryRoots.ToList();
        CurrentSettings.EngineExecutablePath = EnginePathTextBox.Text;
        CurrentSettings.Theme = FromThemeMode(ThemeComboBox.SelectedItem as string ?? "Auto");
        CurrentSettings.ThemeColor = NormalizeHexColor(ThemeColorTextBox.Text);
        CurrentSettings.LibraryViewMode = LibraryViewComboBox.SelectedItem as string ?? LibraryViewModes.List;
        CurrentSettings.HomeViewMode = HomeViewComboBox.SelectedItem as string ?? LibraryViewModes.Thumbnail;
        CurrentSettings.LibrarySortMode = LibrarySortComboBox.SelectedItem as string ?? "Name";
        CurrentSettings.HomeSortMode = HomeSortComboBox.SelectedItem as string ?? "Free Movement";
        CurrentSettings.CardSize = CardSizeComboBox.SelectedItem as string ?? CardSizeOptions.Medium;
        CurrentSettings.ColorRowsByHighestPriorityTag = ColorRowsToggle.IsOn;
        CurrentSettings.NsfwTabMode = (NsfwTabMode)NsfwTabComboBox.SelectedIndex;
        CurrentSettings.UseMicaBackdrop = true;
        CurrentSettings.RunOnStartup = RunOnStartupToggle.IsOn;
        CurrentSettings.MemoryUsageProfile = MemoryUsageComboBox.SelectedItem as string ?? "Balanced";
        CurrentSettings.PrioritizeWorkshopName = PrioritizeWorkshopNameToggle.IsOn;
        CurrentSettings.AutoMarkNsfwFromWorkshop = AutoMarkNsfwToggle.IsOn;
        CurrentSettings.NsfwMode = (CensorshipMode)NsfwModeComboBox.SelectedIndex;
        CurrentSettings.MatureMode = (CensorshipMode)MatureModeComboBox.SelectedIndex;
        CurrentSettings.CensorshipIntensity = CensorshipIntensitySlider.Value;
        CurrentSettings.UseWorkshopTags = UseWorkshopTagsToggle.IsOn;
        CurrentSettings.Tags = Tags.ToList();
        CurrentSettings.NsfwWallpaperKeys = Wallpapers.Where(wallpaper => wallpaper.IsNsfw).Select(wallpaper => wallpaper.Key).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        CurrentSettings.MatureWallpaperKeys = Wallpapers.Where(wallpaper => wallpaper.IsMature).Select(wallpaper => wallpaper.Key).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        CurrentSettings.WallpaperTags = Wallpapers
            .Where(wallpaper => wallpaper.Tags.Count > 0)
            .ToDictionary(wallpaper => wallpaper.Key, wallpaper => wallpaper.Tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);
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

    private void ThumbnailView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyWallpaperPresentation();
        RefreshVisibleWallpapers();
        RefreshSelectedWallpapers();
    }

    private void InitializePicker(object picker)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
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
            Text = tag?.Color ?? "#3A7AFE"
        };

        var colorPicker = new ColorPicker
        {
            Color = ParseHexColor(colorBox.Text),
            IsAlphaEnabled = false,
            IsAlphaSliderVisible = false,
            IsAlphaTextInputVisible = false,
            MinWidth = 220,
            Height = 260
        };

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

        void UpdatePreview()
        {
            var color = ParseHexColor(colorBox.Text);
            var brush = new SolidColorBrush(Color.FromArgb(40, color.R, color.G, color.B));
            foreach (var child in previewCards.Children.OfType<Border>())
            {
                child.Background = brush;
            }
            colorPicker.Color = color;
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
            colorBox.Text = FormatHexColor(args.NewColor);
            UpdatePreview();
        };

        var editorLayout = new Grid
        {
            ColumnSpacing = 22,
            Width = 1200,
            Height = 760
        };
        editorLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.46, GridUnitType.Star) });
        editorLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.54, GridUnitType.Star) });

        var leftPanel = new StackPanel
        {
            Spacing = 14,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        leftPanel.Children.Add(nameBox);
        leftPanel.Children.Add(colorBox);
        leftPanel.Children.Add(colorPicker);

        var rightPanel = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
        rightPanel.Children.Add(new TextBlock { Text = "Preview", FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        rightPanel.Children.Add(previewCards);

        editorLayout.Children.Add(leftPanel);
        Grid.SetColumn(rightPanel, 1);
        editorLayout.Children.Add(rightPanel);
        UpdatePreview();

        var dialog = new ContentDialog
        {
            Title = editingExisting ? "Modify tag" : "New tag",
            Content = editorLayout,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            FullSizeDesired = true,
            XamlRoot = RootGrid.XamlRoot
        };

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
        await SaveSettingsAsync();
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

    private async void ToggleMatureMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuFlyoutItem)?.Tag is not WallpaperItem wallpaper)
        {
            return;
        }

        wallpaper.IsMature = !wallpaper.IsMature;

        ApplyWallpaperPresentation();
        RefreshVisibleWallpapers();
        RefreshSelectedWallpapers();
        await SaveSettingsAsync();
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
            await SaveSettingsAsync();
            RefreshSelectedWallpapers();
            RefreshVisibleWallpapers();
        }
    }

    private async void HomeSortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || sender is not ComboBox comboBox || comboBox.SelectedItem is not string sortMode)
            return;

        CurrentSettings.HomeSortMode = sortMode;
        ApplyHomeSortMode();
        RefreshSelectedWallpapers();
        await SaveSettingsAsync();
    }

    private async void LibrarySortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || sender is not ComboBox comboBox || comboBox.SelectedItem is not string sortMode)
            return;

        CurrentSettings.LibrarySortMode = sortMode;
        RefreshVisibleWallpapers();
        await SaveSettingsAsync();
    }

    private async void HomeView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (CurrentSettings.HomeSortMode != "Free Movement")
            return;

        // Save the new order
        CurrentSettings.SelectedWallpaperKeys = SelectedWallpapers
            .Select(wallpaper => wallpaper.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        await SaveSettingsAsync();
    }
}
