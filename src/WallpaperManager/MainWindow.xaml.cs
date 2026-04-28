using System.Collections.ObjectModel;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WallpaperManager.Models;
using WallpaperManager.Services;
using Windows.Storage.Pickers;
using Microsoft.UI;
using Windows.UI;

namespace WallpaperManager;

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
    private readonly DispatcherTimer _engineStatusTimer = new();
    private bool _isLoadingSettings;

    public ObservableCollection<WallpaperLibraryRoot> LibraryRoots { get; } = [];

    public ObservableCollection<WallpaperItem> Wallpapers { get; } = [];

    public ObservableCollection<WallpaperItem> VisibleWallpapers { get; } = [];

    public ObservableCollection<WallpaperItem> SelectedWallpapers { get; } = [];

    public ObservableCollection<WallpaperItem> HiddenWallpapers { get; } = [];

    public ObservableCollection<WallpaperTag> Tags { get; } = [];

    public IReadOnlyList<string> ThemeChoices { get; } = ThemeOptions.All;

    public IReadOnlyList<string> LibraryViewChoices { get; } = LibraryViewModes.All;

    public IReadOnlyList<string> CardSizeChoices { get; } = CardSizeOptions.All;

    public AppSettings CurrentSettings { get; private set; } = new();

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;

        _engineStatusTimer.Interval = TimeSpan.FromSeconds(3);
        _engineStatusTimer.Tick += (_, _) => UpdateEngineStatus();
        _engineStatusTimer.Start();

        LoadSettings();
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
        ThemeComboBox.SelectedItem = CurrentSettings.Theme;
        LibraryViewComboBox.SelectedItem = CurrentSettings.LibraryViewMode;
        HiddenViewComboBox.SelectedItem = CurrentSettings.LibraryViewMode;
        HomeViewComboBox.SelectedItem = CurrentSettings.HomeViewMode;
        CardSizeComboBox.SelectedItem = CurrentSettings.CardSize;
        HomeCardSizeComboBox.SelectedItem = CurrentSettings.CardSize;
        HiddenCardSizeComboBox.SelectedItem = CurrentSettings.CardSize;
        ColorRowsToggle.IsOn = CurrentSettings.ColorRowsByHighestPriorityTag;
        ShowHiddenPageToggle.IsOn = CurrentSettings.ShowHiddenWallpapersPage;
        HiddenNavigationItem.Visibility = CurrentSettings.ShowHiddenWallpapersPage ? Visibility.Visible : Visibility.Collapsed;

        ApplyTheme(CurrentSettings.Theme);
        ApplyColumnToggleState();
        ApplyLibraryViewMode(CurrentSettings.LibraryViewMode);
        ApplyHiddenViewMode(CurrentSettings.LibraryViewMode);
        ApplyHomeViewMode(CurrentSettings.HomeViewMode);
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
        if (_isLoadingSettings || ThemeComboBox.SelectedItem is not string theme)
        {
            return;
        }

        CurrentSettings.Theme = theme;
        ApplyTheme(theme);
        await SaveSettingsAsync();
    }

    private async void LibraryViewComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || LibraryViewComboBox.SelectedItem is not string viewMode)
        {
            return;
        }

        CurrentSettings.LibraryViewMode = viewMode;
        HiddenViewComboBox.SelectedItem = viewMode;
        ApplyLibraryViewMode(viewMode);
        ApplyHiddenViewMode(viewMode);
        await SaveSettingsAsync();
    }

    private async void HiddenViewComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || HiddenViewComboBox.SelectedItem is not string viewMode)
        {
            return;
        }

        CurrentSettings.LibraryViewMode = viewMode;
        LibraryViewComboBox.SelectedItem = viewMode;
        ApplyLibraryViewMode(viewMode);
        ApplyHiddenViewMode(viewMode);
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
        RefreshHiddenWallpapers();
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

    private async void ShowHiddenPageToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        if (ShowHiddenPageToggle.IsOn && !HasHiddenPassword())
        {
            var password = await PromptForNewHiddenPasswordAsync("Create Hidden Wallpapers password");
            if (string.IsNullOrEmpty(password))
            {
                _isLoadingSettings = true;
                ShowHiddenPageToggle.IsOn = false;
                _isLoadingSettings = false;
                return;
            }

            SetHiddenPassword(password);
        }

        CurrentSettings.ShowHiddenWallpapersPage = ShowHiddenPageToggle.IsOn;
        HiddenNavigationItem.Visibility = CurrentSettings.ShowHiddenWallpapersPage ? Visibility.Visible : Visibility.Collapsed;
        if (!CurrentSettings.ShowHiddenWallpapersPage && IsPageSelected("Hidden"))
        {
            ShowPage("Home");
        }

        await SaveSettingsAsync();
    }

    private async void ChangeHiddenPassword_Click(object sender, RoutedEventArgs e)
    {
        if (HasHiddenPassword() && !await PromptAndVerifyHiddenPasswordAsync("Current hidden password"))
        {
            return;
        }

        var password = await PromptForNewHiddenPasswordAsync("New Hidden Wallpapers password");
        if (string.IsNullOrEmpty(password))
        {
            return;
        }

        SetHiddenPassword(password);
        await SaveSettingsAsync();
    }

    private async void ColumnToggle_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as ToggleMenuFlyoutItem)?.Tag is not string column)
        {
            return;
        }

        var hiddenColumns = CurrentSettings.HiddenLibraryColumns;
        if ((sender as ToggleMenuFlyoutItem)?.IsChecked == true)
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
        var name = NewTagNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || Tags.Any(tag => string.Equals(tag.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        Tags.Add(new WallpaperTag
        {
            Name = name,
            Color = "#3A7AFE"
        });

        NewTagNameTextBox.Text = string.Empty;
        await SaveSettingsAsync();
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
        await SaveSettingsAsync();
        ApplyWallpaperPresentation();
        RefreshVisibleWallpapers();
        RefreshSelectedWallpapers();
    }

    private async void TagsListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
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

        var hideItem = new MenuFlyoutItem
        {
            Text = "Hide This",
            Tag = wallpaper
        };
        hideItem.Click += HideWallpaperMenuItem_Click;
        flyout.Items.Add(hideItem);

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
        RefreshHiddenWallpapers();
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

    private async void HideWallpaperMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuFlyoutItem)?.Tag is not WallpaperItem wallpaper)
        {
            return;
        }

        wallpaper.IsHidden = true;
        wallpaper.IsSelected = false;
        RefreshVisibleWallpapers();
        RefreshSelectedWallpapers();
        RefreshHiddenWallpapers();
        await SaveSettingsAsync();
    }

    private async void RestoreWallpaper_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not WallpaperItem wallpaper)
        {
            return;
        }

        wallpaper.IsHidden = false;
        RefreshVisibleWallpapers();
        RefreshSelectedWallpapers();
        RefreshHiddenWallpapers();
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
                username = "Wallpaper Manager",
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

    private async void ShowHiddenWallpapers_Click(object sender, RoutedEventArgs e)
    {
        var hiddenWallpapers = Wallpapers.Where(wallpaper => wallpaper.IsHidden).ToList();
        var panel = new StackPanel { Spacing = 8 };

        if (hiddenWallpapers.Count == 0)
        {
            panel.Children.Add(new TextBlock { Text = "No hidden wallpapers." });
        }

        foreach (var wallpaper in hiddenWallpapers)
        {
            var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 6, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new StackPanel { Spacing = 2 };
            title.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(wallpaper.DisplayName) ? wallpaper.IdText : wallpaper.DisplayName,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            title.Children.Add(new TextBlock
            {
                Text = wallpaper.IdText,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });

            var restoreButton = new Button { Content = "Restore", Tag = wallpaper };
            restoreButton.Click += async (_, _) =>
            {
                wallpaper.IsHidden = false;
                RefreshVisibleWallpapers();
                RefreshSelectedWallpapers();
                await SaveSettingsAsync();
                panel.Children.Remove(row);
                if (!Wallpapers.Any(item => item.IsHidden))
                {
                    panel.Children.Add(new TextBlock { Text = "No hidden wallpapers." });
                }
            };

            Grid.SetColumn(title, 0);
            Grid.SetColumn(restoreButton, 1);
            row.Children.Add(title);
            row.Children.Add(restoreButton);
            panel.Children.Add(row);
        }

        var dialog = new ContentDialog
        {
            Title = "Hidden wallpapers",
            Content = new ScrollViewer
            {
                MaxHeight = 420,
                Content = panel
            },
            CloseButtonText = "Done",
            XamlRoot = RootGrid.XamlRoot
        };

        await dialog.ShowAsync();
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

    private async void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var page = args.IsSettingsSelected
            ? "Settings"
            : (args.SelectedItem as NavigationViewItem)?.Tag as string ?? "Home";

        if (page == "Hidden" && !await EnsureHiddenWallpapersUnlockedAsync())
        {
            SelectNavigationPage("Home");
            ShowPage("Home");
            return;
        }

        ShowPage(page);
    }

    private async Task ScanLibraryAsync()
    {
        ScanLibraryFromSettings();

        var scannedWallpapers = await _wallpaperScanner.ScanAsync(
            LibraryRoots,
            CurrentSettings.SelectedWallpaperKeys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            CurrentSettings.HiddenWallpaperKeys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            CurrentSettings.NsfwWallpaperKeys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            CurrentSettings.WallpaperTags);

        Wallpapers.Clear();
        foreach (var wallpaper in scannedWallpapers)
        {
            Wallpapers.Add(wallpaper);
        }

        ApplyWallpaperPresentation();
        RefreshVisibleWallpapers();
        RefreshSelectedWallpapers();
        RefreshHiddenWallpapers();
        UpdateEmptyStates();
    }

    private void RefreshVisibleWallpapers()
    {
        VisibleWallpapers.Clear();
        foreach (var wallpaper in Wallpapers.Where(item => !item.IsHidden))
        {
            VisibleWallpapers.Add(wallpaper);
        }

        UpdateEmptyStates();
    }

    private void RefreshSelectedWallpapers()
    {
        SelectedWallpapers.Clear();
        foreach (var wallpaper in Wallpapers.Where(item => item.IsSelected && !item.IsHidden))
        {
            SelectedWallpapers.Add(wallpaper);
        }

        CurrentSettings.SelectedWallpaperKeys = SelectedWallpapers
            .Select(wallpaper => wallpaper.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        UpdateEmptyStates();
    }

    private void RefreshHiddenWallpapers()
    {
        HiddenWallpapers.Clear();
        foreach (var wallpaper in Wallpapers.Where(item => item.IsHidden))
        {
            HiddenWallpapers.Add(wallpaper);
        }

        UpdateEmptyStates();
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
            ApplySizePresentation(wallpaper, hiddenColumns);
        }

        ApplyColumnVisibility();
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
        ColumnsButton.Visibility = isThumbnail ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ApplyHomeViewMode(string viewMode)
    {
        var isThumbnail = viewMode == LibraryViewModes.Thumbnail;
        HomeListView.Visibility = isThumbnail ? Visibility.Collapsed : Visibility.Visible;
        HomeThumbnailView.Visibility = isThumbnail ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyHiddenViewMode(string viewMode)
    {
        var isThumbnail = viewMode == LibraryViewModes.Thumbnail;
        HiddenListView.Visibility = isThumbnail ? Visibility.Collapsed : Visibility.Visible;
        HiddenThumbnailView.Visibility = isThumbnail ? Visibility.Visible : Visibility.Collapsed;
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
        HiddenPage.Visibility = page == "Hidden" ? Visibility.Visible : Visibility.Collapsed;
        InfoPage.Visibility = page == "Info" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;

        PageTitle.Text = page;
        PageSubtitle.Text = page switch
        {
            "Library" => "All detected wallpapers within every configured directory.",
            "Hidden" => "Hidden wallpapers are kept out of Library and Home.",
            "Info" => "Creator details, acknowledgments, changelog, and contact.",
            "Settings" => "Configure directories, Wallpaper Engine, tags, columns, and theme.",
            _ => "Selected wallpapers from your local Wallpaper Engine library."
        };
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
        CurrentSettings.Theme = ThemeComboBox.SelectedItem as string ?? ThemeOptions.System;
        CurrentSettings.LibraryViewMode = LibraryViewComboBox.SelectedItem as string ?? LibraryViewModes.List;
        CurrentSettings.HomeViewMode = HomeViewComboBox.SelectedItem as string ?? LibraryViewModes.Thumbnail;
        CurrentSettings.CardSize = CardSizeComboBox.SelectedItem as string ?? CardSizeOptions.Medium;
        CurrentSettings.ShowHiddenWallpapersPage = ShowHiddenPageToggle.IsOn;
        CurrentSettings.ColorRowsByHighestPriorityTag = ColorRowsToggle.IsOn;
        CurrentSettings.Tags = Tags.ToList();
        CurrentSettings.HiddenWallpaperKeys = Wallpapers.Where(wallpaper => wallpaper.IsHidden).Select(wallpaper => wallpaper.Key).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        CurrentSettings.NsfwWallpaperKeys = Wallpapers.Where(wallpaper => wallpaper.IsNsfw).Select(wallpaper => wallpaper.Key).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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
        HiddenCountText.Text = $"{HiddenWallpapers.Count} hidden";
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

    private void InitializePicker(object picker)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
    }

    private async Task<bool> EnsureHiddenWallpapersUnlockedAsync()
    {
        if (!HasHiddenPassword())
        {
            var password = await PromptForNewHiddenPasswordAsync("Create Hidden Wallpapers password");
            if (string.IsNullOrEmpty(password))
            {
                return false;
            }

            SetHiddenPassword(password);
            CurrentSettings.ShowHiddenWallpapersPage = true;
            ShowHiddenPageToggle.IsOn = true;
            HiddenNavigationItem.Visibility = Visibility.Visible;
            await SaveSettingsAsync();
            return true;
        }

        return await PromptAndVerifyHiddenPasswordAsync("Hidden Wallpapers password");
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

    private async Task<bool> PromptAndVerifyHiddenPasswordAsync(string title)
    {
        var passwordBox = new PasswordBox { PlaceholderText = "Password" };
        var dialog = new ContentDialog
        {
            Title = title,
            Content = passwordBox,
            PrimaryButtonText = "Unlock",
            CloseButtonText = "Cancel",
            XamlRoot = RootGrid.XamlRoot
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary && VerifyHiddenPassword(passwordBox.Password);
    }

    private async Task<string?> PromptForNewHiddenPasswordAsync(string title)
    {
        var passwordBox = new PasswordBox { PlaceholderText = "Password" };
        var confirmBox = new PasswordBox { PlaceholderText = "Confirm password" };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = "No recovery is available if this password is lost.",
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(passwordBox);
        panel.Children.Add(confirmBox);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = RootGrid.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary
            || string.IsNullOrWhiteSpace(passwordBox.Password)
            || passwordBox.Password != confirmBox.Password)
        {
            return null;
        }

        return passwordBox.Password;
    }

    private bool HasHiddenPassword()
    {
        return !string.IsNullOrWhiteSpace(CurrentSettings.HiddenWallpapersPasswordSalt)
            && !string.IsNullOrWhiteSpace(CurrentSettings.HiddenWallpapersPasswordHash);
    }

    private void SetHiddenPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = HashPassword(password, salt);
        CurrentSettings.HiddenWallpapersPasswordSalt = Convert.ToBase64String(salt);
        CurrentSettings.HiddenWallpapersPasswordHash = Convert.ToBase64String(hash);
    }

    private bool VerifyHiddenPassword(string password)
    {
        if (!HasHiddenPassword())
        {
            return false;
        }

        var salt = Convert.FromBase64String(CurrentSettings.HiddenWallpapersPasswordSalt);
        var expectedHash = Convert.FromBase64String(CurrentSettings.HiddenWallpapersPasswordHash);
        var actualHash = HashPassword(password, salt);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private static byte[] HashPassword(string password, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
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

        if (HiddenCardSizeComboBox.SelectedItem as string != cardSize)
        {
            HiddenCardSizeComboBox.SelectedItem = cardSize;
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

    private static string NormalizeHexColor(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "#3A7AFE";
        }

        value = value.Trim();
        if (!value.StartsWith('#'))
        {
            value = $"#{value}";
        }

        return value.Length == 7 ? value : "#3A7AFE";
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
}
