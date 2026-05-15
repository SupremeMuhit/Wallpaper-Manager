using System.Collections.ObjectModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using System.Net.Http;
using System.Runtime.InteropServices;
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
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.Graphics.Canvas.Effects;
using Windows.Graphics.Effects;
using WinRT.Interop;

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
    private readonly LocalMetadataStore _localMetadataStore = new();
    private readonly ScenePackageExtractor _scenePackageExtractor = new();
    private readonly ScenePackageRepacker _scenePackageRepacker = new();
    private readonly DispatcherTimer _engineStatusTimer = new();
    private readonly GlobalHotkeyService _hotkeyService = new();
    private MicaBackdrop? _micaBackdrop;
    private AppWindow? _appWindow;
    private IntPtr _hwnd;
    private IntPtr _trayIconHandle;
    private WindowProc? _newWndProc;
    private IntPtr _oldWndProc;
    private bool _trayIconAdded;
    private bool _isExitRequested;
    private bool _hasShownTrayHint;
    private bool _isLoadingSettings;
    private Dictionary<string, LocalMetadataFile> _localMetadataByRoot = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<WallpaperLibraryRoot> LibraryRoots { get; } = [];

    public ObservableCollection<WallpaperItem> Wallpapers { get; } = [];

    public ObservableCollection<WallpaperItem> VisibleWallpapers { get; } = [];

    public ObservableCollection<WallpaperItem> SelectedWallpapers { get; } = [];

    public ObservableCollection<HomeGroupViewModel> HomeGroups { get; } = [];

    public ObservableCollection<WallpaperList> HomeListButtons { get; } = [];

    public ObservableCollection<WorkshopDownloadItem> BatchDownloadItems { get; } = [];

    public ObservableCollection<SceneExtractionItem> SceneExtractionItems { get; } = [];

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
    
    private List<WallpaperItem> _draggedWallpapers = [];

    public IReadOnlyList<string> NsfwTabChoices { get; } = ["Off", "Only NSFW", "NSFW and Mature"];

    public ObservableCollection<SettingsCategory> SettingsCategories { get; } =
    [
        new() { Tag = "EngineWallpaper", Title = "Engine Setting", Icon = "\uE8A7" },
        new() { Tag = "Appearance", Title = "Appearance", Icon = "\uE771" },
        new() { Tag = "Library", Title = "Library", Icon = "\uE8B7" },
        new() { Tag = "NsfwMature", Title = "NSFW / Mature", Icon = "\uE8D4" },
        new() { Tag = "Tags", Title = "Tags", Icon = "\uE8EC" },
        new() { Tag = "SwitchingShortcuts", Title = "Switching Shortcuts", Icon = "\uE81E" },
        new() { Tag = "AdvanceSettings", Title = "Advance Settings", Icon = "\uE713" },
        new() { Tag = "About", Title = "About", Icon = "\uE946" },
        new() { Tag = "Contact", Title = "Contact", Icon = "\uE715" }
    ];

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        InitializeTrayBehavior();

        _engineStatusTimer.Interval = TimeSpan.FromSeconds(3);
        _engineStatusTimer.Tick += (_, _) => UpdateEngineStatus();
        _engineStatusTimer.Start();

        _saveSettingsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveSettingsTimer.Tick += async (_, _) =>
        {
            _saveSettingsTimer.Stop();
            CurrentSettings.Tags = Tags.ToList();
            CurrentSettings.WallpaperLists = HomeListButtons.ToList();
            CurrentSettings.WallpaperTags = [];

            // Update keys from current wallpapers
            CurrentSettings.NsfwWallpaperKeys = Wallpapers.Where(w => w.IsNsfw).Select(w => w.Key).ToList();
            CurrentSettings.MatureWallpaperKeys = Wallpapers.Where(w => w.IsMature).Select(w => w.Key).ToList();

            try { await SaveLocalMetadataAsync(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"SaveLocalMetadata failed: {ex.Message}"); }

            try
            {
                await _settingsStore.SaveAsync(CurrentSettings);
                ContextMenuService.ApplySettings(CurrentSettings, Wallpapers.ToList());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Save settings failed: {ex.Message}");
            }
        };

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
        
        _hotkeyService.HotkeyPressed += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                TriggerNextWallpaperAction();
                FlashHotkeyIndicator();
            });
        };
        
        LoadSettings();
    }

    private readonly DispatcherTimer _sizeChangedTimer = new();

    private readonly DispatcherTimer _hotkeyIndicatorTimer = new()
    {
        Interval = TimeSpan.FromSeconds(2)
    };

    private void FlashHotkeyIndicator()
    {
        if (!CurrentSettings.IsDevMode) return;
        if (HotkeyDebugIndicator == null) return;

        // Turn green
        HotkeyDebugIndicator.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 34, 197, 94));

        _hotkeyIndicatorTimer.Stop();
        _hotkeyIndicatorTimer.Tick -= HotkeyIndicatorTimer_Tick;
        _hotkeyIndicatorTimer.Tick += HotkeyIndicatorTimer_Tick;
        _hotkeyIndicatorTimer.Start();
    }

    private void HotkeyIndicatorTimer_Tick(object? sender, object e)
    {
        _hotkeyIndicatorTimer.Stop();
        if (HotkeyDebugIndicator != null)
        {
            // Restore to gray
            HotkeyDebugIndicator.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128));
        }
    }

    private void InitializeTrayBehavior()
    {
        _hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Closing += AppWindow_Closing;

        _newWndProc = TrayWndProc;
        _oldWndProc = SetWindowLongPtr(_hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_newWndProc));
        SetAppIcon();
        AddTrayIcon();
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isExitRequested || !CurrentSettings.QuitToTray)
        {
            DisposeTrayIcon();
            return;
        }

        args.Cancel = true;
        HideToTray();
    }

    private void HideToTray()
    {
        _appWindow?.Hide();

        if (!_hasShownTrayHint)
        {
            ShowTrayBalloon(
                "Carbon is still running",
                "Use the tray icon to reopen it or exit.");
            _hasShownTrayHint = true;
        }
    }

    private void ShowFromTray()
    {
        _appWindow?.Show();
        Activate();
    }

    private void ExitFromTray()
    {
        _isExitRequested = true;
        DisposeTrayIcon();
        Close();
    }

    private static string GetIcoPath()
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "logo.ico");
        if (System.IO.File.Exists(path))
            return path;
        path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(Program).Assembly.Location)!, "Assets", "logo.ico");
        return path;
    }

    private void SetAppIcon()
    {
        var icoPath = GetIcoPath();
        if (!System.IO.File.Exists(icoPath))
            return;

        try { _appWindow?.SetIcon(icoPath); } catch { }

        var hIcon = LoadImage(IntPtr.Zero, icoPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
        if (hIcon != IntPtr.Zero)
        {
            SendMessage(_hwnd, WM_SETICON, new IntPtr(ICON_SMALL), hIcon);
            SendMessage(_hwnd, WM_SETICON, new IntPtr(ICON_BIG), hIcon);
        }
    }

    private void DisposeTrayIcon()
    {
        if (_trayIconAdded)
        {
            var data = CreateNotifyIconData();
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _trayIconAdded = false;
        }

        if (_oldWndProc != IntPtr.Zero && _hwnd != IntPtr.Zero)
        {
            SetWindowLongPtr(_hwnd, GWLP_WNDPROC, _oldWndProc);
            _oldWndProc = IntPtr.Zero;
        }
    }

    private void AddTrayIcon()
    {
        var icoPath = GetIcoPath();
        _trayIconHandle = LoadImage(IntPtr.Zero, icoPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
        if (_trayIconHandle == IntPtr.Zero)
            _trayIconHandle = LoadIcon(IntPtr.Zero, IDI_APPLICATION);
        var data = CreateNotifyIconData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        data.hIcon = _trayIconHandle;
        data.szTip = "Carbon";
        _trayIconAdded = Shell_NotifyIcon(NIM_ADD, ref data);
    }

    private void ShowTrayBalloon(string title, string message)
    {
        if (!_trayIconAdded)
        {
            return;
        }

        var data = CreateNotifyIconData();
        data.uFlags = NIF_INFO;
        data.szInfoTitle = title;
        data.szInfo = message;
        data.dwInfoFlags = NIIF_INFO;
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    private NOTIFYICONDATA CreateNotifyIconData()
    {
        var data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uCallbackMessage = WM_TRAYICON,
            szTip = string.Empty,
            szInfo = string.Empty,
            szInfoTitle = string.Empty
        };

        return data;
    }

    private IntPtr TrayWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAYICON)
        {
            var mouseMessage = lParam.ToInt32();
            if (mouseMessage is WM_LBUTTONUP or WM_LBUTTONDBLCLK)
            {
                DispatcherQueue.TryEnqueue(ShowFromTray);
                return IntPtr.Zero;
            }

            if (mouseMessage == WM_RBUTTONUP)
            {
                DispatcherQueue.TryEnqueue(ShowTrayMenu);
                return IntPtr.Zero;
            }
        }

        return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
    }

    private void ShowTrayMenu()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        var menu = CreatePopupMenu();
        AppendMenu(menu, MF_STRING, TrayCommandOpen, "Open Carbon");
        AppendMenu(menu, MF_SEPARATOR, 0, string.Empty);
        AppendMenu(menu, MF_STRING, TrayCommandExit, "Exit");

        GetCursorPos(out var point);
        SetForegroundWindow(_hwnd);
        var command = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, point.X, point.Y, 0, _hwnd, IntPtr.Zero);
        DestroyMenu(menu);

        switch (command)
        {
            case TrayCommandOpen:
                ShowFromTray();
                break;
            case TrayCommandExit:
                ExitFromTray();
                break;
        }
    }

    private delegate IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const int GWLP_WNDPROC = -4;
    private const uint WM_APP = 0x8000;
    private const uint WM_TRAYICON = WM_APP + 1;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;
    private const uint NIIF_INFO = 0x00000001;
    private const uint MF_STRING = 0x00000000;
    private const uint MF_SEPARATOR = 0x00000800;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TrayCommandOpen = 1001;
    private const uint TrayCommandExit = 1002;
    private const uint WM_SETICON = 0x0080;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;
    private static readonly IntPtr IDI_APPLICATION = new(32512);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractAssociatedIcon(IntPtr hInst, string lpIconPath, out int lpiIcon);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;
    private const uint LR_DEFAULTSIZE = 0x00000040;

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

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

        WallpaperDirectoryTextBox.Text = CurrentSettings.WallpaperDirectory;

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
        NsfwNavTab.Visibility = CurrentSettings.NsfwTabMode != NsfwTabMode.Off ? Visibility.Visible : Visibility.Collapsed;
        RunOnStartupToggle.IsOn = CurrentSettings.RunOnStartup;
        MemoryUsageComboBox.SelectedItem = CurrentSettings.MemoryUsageProfile;
        AutoMarkNsfwToggle.IsOn = CurrentSettings.DontAutoMarkNsfwFromWorkshop;
        RemoveCensorOnHoverToggle.IsOn = CurrentSettings.RemoveCensorOnHover;
        NsfwModeComboBox.SelectedIndex = (int)CurrentSettings.NsfwMode;
        MatureModeComboBox.SelectedIndex = (int)CurrentSettings.MatureMode;
        LibraryHideComboBox.SelectedIndex = (int)CurrentSettings.LibraryHideMode;
        QuitToTrayToggle.IsOn = CurrentSettings.QuitToTray;
        ContextMenuNextToggle.IsOn = CurrentSettings.ContextMenuNextWallpaper;
        ContextMenuSwitchToggle.IsOn = CurrentSettings.ContextMenuSwitchWallpaper;
        ContextMenuNextModeComboBox.SelectedItem = CurrentSettings.ContextMenuNextMode;
        GlobalShortcutTextBox.Text = CurrentSettings.GlobalShortcut;
        _hotkeyService.RegisterShortcut(CurrentSettings.GlobalShortcut);

        RefreshHomeListButtons();
        RefreshContextMenuListComboBox();

        
        InitializeCardButtonsList();

        BlurIntensitySlider.Value = CurrentSettings.BlurIntensity;
        OverlayOpacitySlider.Value = CurrentSettings.OverlayOpacity;
        DontShowWorkshopTagsToggle.IsOn = CurrentSettings.DontShowWorkshopTags;
        ConsiderSubdirectoryAsTagToggle.IsOn = CurrentSettings.ConsiderSubdirectoryAsTag;

        ApplyTheme(CurrentSettings.Theme);
        CurrentSettings.UseMicaBackdrop = true;
        ApplyBackdrop(true);
        ApplyThemeColor(CurrentSettings.ThemeColor);
        ApplyColumnToggleState();
        ApplyLibraryViewMode(CurrentSettings.LibraryViewMode);
        ApplyHomeViewMode(CurrentSettings.HomeViewMode);
        ApplyHomeSortMode();
        UpdateDevModeUi();
        RefreshVisibleTags();
        UpdateEngineStatus();
        
        await ScanLibraryAsync();
        _isLoadingSettings = false;
    }

    private async void BrowseDirectory_Click(object sender, RoutedEventArgs e)
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

        CurrentSettings.WallpaperDirectory = System.IO.Path.GetFullPath(folder.Path);
        WallpaperDirectoryTextBox.Text = CurrentSettings.WallpaperDirectory;
        TriggerSaveSettings();
        await ScanLibraryAsync();
    }

    private async void ClearDirectory_Click(object sender, RoutedEventArgs e)
    {
        CurrentSettings.WallpaperDirectory = string.Empty;
        WallpaperDirectoryTextBox.Text = string.Empty;
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

    private async void NsfwTabComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WallpaperManager", "nsfw_debug.log");
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

        try
        {
            File.AppendAllText(logPath, $"[{timestamp}] Handler fired. _isLoadingSettings={_isLoadingSettings}, SelectedIndex={NsfwTabComboBox.SelectedIndex}, CurrentSettings.NsfwTabMode={CurrentSettings.NsfwTabMode}\n");
        }
        catch { }

        if (_isLoadingSettings || NsfwTabComboBox.SelectedIndex == -1)
        {
            try { File.AppendAllText(logPath, $"[{timestamp}] EARLY RETURN: _isLoadingSettings={_isLoadingSettings}, SelectedIndex={NsfwTabComboBox.SelectedIndex}\n"); } catch { }
            return;
        }

        var newMode = (NsfwTabMode)NsfwTabComboBox.SelectedIndex;
        if (newMode == CurrentSettings.NsfwTabMode)
        {
            try { File.AppendAllText(logPath, $"[{timestamp}] EARLY RETURN: newMode ({newMode}) == CurrentSettings.NsfwTabMode ({CurrentSettings.NsfwTabMode})\n"); } catch { }
            return;
        }

        CurrentSettings.NsfwTabMode = newMode;
        NsfwNavTab.Visibility = CurrentSettings.NsfwTabMode != NsfwTabMode.Off ? Visibility.Visible : Visibility.Collapsed;
        RefreshVisibleWallpapers();

        try { File.AppendAllText(logPath, $"[{timestamp}] About to save. CurrentSettings.NsfwTabMode={CurrentSettings.NsfwTabMode}\n"); } catch { }

        try
        {
            await _settingsStore.SaveAsync(CurrentSettings);
            try { File.AppendAllText(logPath, $"[{timestamp}] SAVE SUCCEEDED\n"); } catch { }
        }
        catch (Exception ex)
        {
            try { File.AppendAllText(logPath, $"[{timestamp}] SAVE FAILED: {ex.Message}\n"); } catch { }
        }
    }

    private void QuitToTrayToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        CurrentSettings.QuitToTray = QuitToTrayToggle.IsOn;
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
        CurrentSettings.DontAutoMarkNsfwFromWorkshop = AutoMarkNsfwToggle.IsOn;
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

    private void DontShowWorkshopTagsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings) return;
        CurrentSettings.DontShowWorkshopTags = DontShowWorkshopTagsToggle.IsOn;
        ApplyWallpaperPresentation();
        RefreshVisibleWallpapers();
        TriggerSaveSettings();
    }

    private void ConsiderSubdirectoryAsTagToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings) return;
        CurrentSettings.ConsiderSubdirectoryAsTag = ConsiderSubdirectoryAsTagToggle.IsOn;
        _ = ScanLibraryAsync();
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
        _downloadService.ResetCancellation();
        DownloadSuccessPanel.Visibility = Visibility.Collapsed;
        ActiveDownloadPanel.Visibility = Visibility.Collapsed;
        SkipCurrentDownloadButton.Visibility = Visibility.Collapsed;
        
        var selectedRoot = DownloadPathSelector.SelectedItem as WallpaperLibraryRoot;
        var downloadDir = selectedRoot?.Path;

        if (string.IsNullOrEmpty(downloadDir))
        {
            ShowDownloadInfo("Path Error", "Please select a download directory.", InfoBarSeverity.Error);
            return;
        }

        var input = WorkshopUrlInput.Text.Trim();
        var lines = input.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var ids = lines.Select(line => _downloadService.ExtractWorkshopId(line))
                      .OfType<string>()
                      .Distinct()
                      .ToList();

        if (ids.Count == 0)
        {
            ShowDownloadInfo("Invalid input", "Please enter valid Steam Workshop URLs or IDs.", InfoBarSeverity.Error);
            return;
        }

        ExecuteDownloadButton.IsEnabled = false;
        CancelDownloadButton.IsEnabled = true;
        CancelDownloadButton.Visibility = Visibility.Visible;
        SkipCurrentDownloadButton.IsEnabled = true;
        SkipCurrentDownloadButton.Visibility = Visibility.Visible;
        ActiveDownloadPanel.Visibility = Visibility.Visible;
        ActiveDownloadProgressBar.IsIndeterminate = false;
        ActiveDownloadProgressBar.Value = 0;
        ActiveDownloadStatusText.Text = "Starting...";

        int successCount = 0;
        string? forcedAccount = null;
        if (CurrentSettings.IsDevMode && ForcedAccountComboBox.SelectedIndex > 0)
        {
            forcedAccount = ForcedAccountComboBox.SelectedItem as string;
        }

        if (ids.Count > 1)
        {
            var batchItems = BatchDownloadItems
                .Where(item => ids.Contains(item.WorkshopId))
                .ToList();

            if (batchItems.Count != ids.Count)
            {
                BatchDownloadItems.Clear();
                foreach (var id in ids)
                {
                    BatchDownloadItems.Add(new WorkshopDownloadItem { WorkshopId = id });
                }

                batchItems = BatchDownloadItems.ToList();
                BatchDownloadListView.Visibility = Visibility.Visible;
                WorkshopPreviewPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                ids = batchItems.Select(item => item.WorkshopId).ToList();
            }

            foreach (var item in batchItems)
            {
                item.IsDownloading = false;
                item.IsCompleted = false;
                item.IsFailed = false;
                item.Progress = 0;
                item.Status = "Pending";
            }

            foreach (var item in batchItems)
            {
                if (_downloadService.IsCancelled()) break;

                SkipCurrentDownloadButton.IsEnabled = true;
                item.IsDownloading = true;
                item.Status = "Downloading...";
                item.Progress = 0;

                var workshopId = item.WorkshopId;
                var accountInfo = CurrentSettings.IsDevMode ? " (Dev Mode)" : "";
                ActiveDownloadStatusText.Text = $"Downloading {item.DisplayName}{accountInfo}...";

                var success = await Task.Run(async () =>
                {
                    return await _downloadService.DownloadAsync(workshopId, downloadDir, (progress, status) =>
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            item.Progress = progress;
                            ActiveDownloadProgressBar.IsIndeterminate = progress <= 0;
                            ActiveDownloadProgressBar.Value = progress;
                            item.Status = status;
                            ActiveDownloadStatusText.Text = $"{item.DisplayName} - {status}";
                        });
                    }, forcedAccount);
                });

                item.IsDownloading = false;
                if (success)
                {
                    successCount++;
                    item.IsCompleted = true;
                    item.Status = "Completed";
                    item.Progress = 100;
                    
                    try
                    {
                        if (item.Metadata != null)
                        {
                            var metaPath = Path.Combine(downloadDir, workshopId, "meta.json");
                            var json = System.Text.Json.JsonSerializer.Serialize(item.Metadata, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                            await File.WriteAllTextAsync(metaPath, json);
                        }
                    }
                    catch { }
                }
                else
                {
                    var wasSkipped = _downloadService.IsCurrentDownloadSkipped();
                    item.IsFailed = !_downloadService.IsCancelled() && !wasSkipped;
                    item.Status = _downloadService.IsCancelled()
                        ? "Cancelled"
                        : wasSkipped
                            ? "Skipped"
                            : string.IsNullOrWhiteSpace(_downloadService.LastFailureMessage)
                                ? "Failed"
                                : _downloadService.LastFailureMessage;
                }

                if (_downloadService.IsCancelled()) break;
            }
            
            ShowDownloadInfo("Batch Complete", $"Downloaded {successCount} of {ids.Count} wallpapers.", successCount == ids.Count ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        }
        else
        {
            var workshopId = ids[0];
            SkipCurrentDownloadButton.IsEnabled = true;
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
                }, forcedAccount);
            });

            if (success)
            {
                successCount++;
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
                catch { }

                DownloadSuccessPanel.Visibility = Visibility.Visible;
                WorkshopUrlInput.Text = string.Empty;
                WorkshopPreviewPanel.Visibility = Visibility.Collapsed;
                ShowDownloadInfo("Success", $"Wallpaper {workshopId} downloaded successfully.", InfoBarSeverity.Success);
            }
            else
            {
                var message = _downloadService.IsCancelled()
                    ? "Download was cancelled."
                    : string.IsNullOrWhiteSpace(_downloadService.LastFailureMessage)
                        ? "There was an error downloading the wallpaper."
                        : _downloadService.LastFailureMessage;
                ShowDownloadInfo("Download Failed", message, InfoBarSeverity.Error);
            }
        }

        ExecuteDownloadButton.IsEnabled = true;
        CancelDownloadButton.Visibility = Visibility.Collapsed;
        SkipCurrentDownloadButton.Visibility = Visibility.Collapsed;
        ActiveDownloadPanel.Visibility = Visibility.Collapsed;
        ActiveDownloadProgressBar.IsIndeterminate = false;
        if (successCount > 0)
        {
            await ScanLibraryAsync();
        }
    }

    private void SkipAccount_Click(object sender, RoutedEventArgs e)
    {
        _downloadService.SkipCurrentAccount();
    }

    private void SkipCurrentDownload_Click(object sender, RoutedEventArgs e)
    {
        _downloadService.SkipCurrentDownload();
        SkipCurrentDownloadButton.IsEnabled = false;
        ActiveDownloadStatusText.Text = "Skipping current download...";
    }

    private string _lastPreviewInput = string.Empty;
    private async void WorkshopUrlInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        var input = WorkshopUrlInput.Text.Trim();
        if (input == _lastPreviewInput) return;
        _lastPreviewInput = input;

        if (string.IsNullOrEmpty(input))
        {
            WorkshopPreviewPanel.Visibility = Visibility.Collapsed;
            BatchDownloadListView.Visibility = Visibility.Collapsed;
            WorkshopLoadingPanel.Visibility = Visibility.Collapsed;
            BatchDownloadItems.Clear();
            return;
        }

        var lines = input.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var ids = lines.Select(line => _downloadService.ExtractWorkshopId(line))
                      .OfType<string>()
                      .Distinct()
                      .ToList();

        if (ids.Count == 0)
        {
            WorkshopPreviewPanel.Visibility = Visibility.Collapsed;
            BatchDownloadListView.Visibility = Visibility.Collapsed;
            WorkshopLoadingPanel.Visibility = Visibility.Collapsed;
            BatchDownloadItems.Clear();
            return;
        }

        if (ids.Count == 1)
        {
            BatchDownloadListView.Visibility = Visibility.Collapsed;
            BatchDownloadItems.Clear();
            await ShowSinglePreview(ids[0]);
        }
        else
        {
            WorkshopPreviewPanel.Visibility = Visibility.Collapsed;
            WorkshopLoadingPanel.Visibility = Visibility.Visible;
            BatchDownloadListView.Visibility = Visibility.Visible;
            
            BatchDownloadItems.Clear();
            foreach (var id in ids)
            {
                BatchDownloadItems.Add(new WorkshopDownloadItem { WorkshopId = id });
            }

            try
            {
                var currentInput = input;
                var metadataMap = await _workshopService.FetchBatchAsync(ids);
                
                // Only update if input hasn't changed and we are still in batch mode
                var currentIds = GetCurrentWorkshopIds();
                if (WorkshopUrlInput.Text.Trim() == currentInput && currentIds.Count > 1)
                {
                    foreach (var item in BatchDownloadItems)
                    {
                        if (metadataMap.TryGetValue(item.WorkshopId, out var meta))
                        {
                            item.Metadata = meta;
                        }
                    }
                }
            }
            catch { }
            finally
            {
                if (WorkshopUrlInput.Text.Trim() == input)
                {
                    WorkshopLoadingPanel.Visibility = Visibility.Collapsed;
                }
            }
        }
    }

    private List<string> GetCurrentWorkshopIds()
    {
        var input = WorkshopUrlInput.Text.Trim();
        var lines = input.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        return lines.Select(line => _downloadService.ExtractWorkshopId(line))
                    .OfType<string>()
                    .Distinct()
                    .ToList();
    }

    private async Task ShowSinglePreview(string workshopId)
    {
        WorkshopPreviewPanel.Visibility = Visibility.Collapsed;
        WorkshopLoadingPanel.Visibility = Visibility.Visible;

        try
        {
            var metadata = await _workshopService.FetchAsync(workshopId);
            var currentIds = GetCurrentWorkshopIds();
            
            // Only show if there is exactly one ID and it's the one we fetched
            if (metadata != null && currentIds.Count == 1 && currentIds[0] == workshopId)
            {
                WorkshopPreviewTitle.Text = metadata.Title;
                WorkshopPreviewDescription.Text = metadata.Description;
                WorkshopPreviewTags.ItemsSource = metadata.Tags;
                WorkshopPreviewSize.Text = FormatSize(metadata.FileSize);
                WorkshopPreviewDate.Text = metadata.TimeUpdated.ToString("MMM dd, yyyy");
                
                if (!string.IsNullOrEmpty(metadata.PreviewUrl))
                {
                    WorkshopPreviewImage.Source = new BitmapImage(new Uri(metadata.PreviewUrl));
                }

                WorkshopPreviewPanel.Visibility = Visibility.Visible;
                BatchDownloadListView.Visibility = Visibility.Collapsed;
            }
        }
        catch { }
        finally
        {
            var currentIds = GetCurrentWorkshopIds();
            if (currentIds.Count == 1 && currentIds[0] == workshopId)
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
                username = "Carbon",
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
        CurrentSettings.CurrentlyPlayingWallpaperKey = wallpaper.Key;
        TriggerSaveSettings();
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

    private void RefreshSceneExtractorPage()
    {
        SceneExtractorWallpaperSelector.ItemsSource = Wallpapers
            .Where(wallpaper => File.Exists(Path.Combine(wallpaper.DirectoryPath, "scene.pkg")))
            .OrderBy(wallpaper => wallpaper.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (SceneExtractorWallpaperSelector.SelectedIndex == -1 && SceneExtractorWallpaperSelector.Items.Count > 0)
        {
            SceneExtractorWallpaperSelector.SelectedIndex = 0;
        }

        UpdateSceneExtractionOutputPath();
        RefreshSceneExtractionItems();
    }

    private void SceneExtractorWallpaperSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSceneExtractionOutputPath();
    }

    private void UpdateSceneExtractionOutputPath()
    {
        if (SceneExtractorWallpaperSelector.SelectedItem is not WallpaperItem wallpaper ||
            string.IsNullOrWhiteSpace(wallpaper.LibraryRootPath))
        {
            SceneExtractionOutputPathText.Text = "No scene wallpaper selected.";
            SceneExtractionOutputSummaryText.Text = "Choose a wallpaper with a scene.pkg file to see its extraction output here.";
            return;
        }

        var outputDirectory = GetSceneExtractionOutputDirectory(wallpaper);
        SceneExtractionOutputPathText.Text = outputDirectory;
        SceneExtractionOutputSummaryText.Text = Directory.Exists(outputDirectory)
            ? BuildSceneExtractionOutputSummary(outputDirectory)
            : "Output folder has not been created yet.";
    }

    private string GetSceneExtractionOutputDirectory(WallpaperItem wallpaper)
    {
        var root = _localMetadataStore.GetSceneExtractionsDirectory(wallpaper.LibraryRootPath);
        var folderName = !string.IsNullOrWhiteSpace(wallpaper.SteamId)
            ? wallpaper.SteamId
            : Path.GetFileName(wallpaper.DirectoryPath.TrimEnd(Path.DirectorySeparatorChar));

        return Path.Combine(root, folderName);
    }

    private async void ExtractScene_Click(object sender, RoutedEventArgs e)
    {
        if (SceneExtractorWallpaperSelector.SelectedItem is not WallpaperItem wallpaper)
        {
            ShowSceneExtractionInfo("No wallpaper selected", "Choose a wallpaper with a scene.pkg file.", InfoBarSeverity.Warning);
            return;
        }

        var scenePackagePath = Path.Combine(wallpaper.DirectoryPath, "scene.pkg");
        if (!File.Exists(scenePackagePath))
        {
            ShowSceneExtractionInfo("No scene.pkg", "The selected wallpaper does not contain a scene.pkg file.", InfoBarSeverity.Warning);
            return;
        }

        ExtractSceneButton.IsEnabled = false;
        SceneExtractionProgressRing.Visibility = Visibility.Visible;
        SceneExtractionProgressRing.IsActive = true;

        try
        {
            var outputDirectory = GetSceneExtractionOutputDirectory(wallpaper);
            var result = await _scenePackageExtractor.ExtractAsync(scenePackagePath, outputDirectory);
            SceneExtractionOutputPathText.Text = result.OutputDirectory;
            SceneExtractionOutputSummaryText.Text = BuildSceneExtractionOutputSummary(result);
            RefreshSceneExtractionItems();
            ShowSceneExtractionInfo("Scene extracted", $"Extracted {result.FileCount:N0} files from {result.Signature}.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowSceneExtractionInfo("Extraction failed", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SceneExtractionProgressRing.IsActive = false;
            SceneExtractionProgressRing.Visibility = Visibility.Collapsed;
            ExtractSceneButton.IsEnabled = true;
        }
    }

    private void OpenSceneExtraction_Click(object sender, RoutedEventArgs e)
    {
        var path = SceneExtractionOutputPathText.Text;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            ShowSceneExtractionInfo("Output missing", "Extract a scene first, then open the output folder.", InfoBarSeverity.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private void RefreshSceneExtractions_Click(object sender, RoutedEventArgs e)
    {
        RefreshSceneExtractionItems();
    }

    private void RefreshSceneExtractionItems()
    {
        var items = new List<SceneExtractionItem>();
        var sceneRoots = string.IsNullOrWhiteSpace(CurrentSettings.WallpaperDirectory) ? Array.Empty<string>() : new[] { CurrentSettings.WallpaperDirectory };
        foreach (var rootPath in sceneRoots)
        {
            var extractionRoot = _localMetadataStore.GetSceneExtractionsDirectory(rootPath);
            if (!Directory.Exists(extractionRoot))
            {
                continue;
            }

            foreach (var directory in Directory.GetDirectories(extractionRoot))
            {
                var item = CreateSceneExtractionItem(directory, rootPath);
                if (item.FileCount > 0)
                {
                    items.Add(item);
                }
            }
        }

        SceneExtractionItems.Clear();
        foreach (var item in items
            .OrderByDescending(item => item.LastWriteTime)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            SceneExtractionItems.Add(item);
        }
    }

    private SceneExtractionItem CreateSceneExtractionItem(string directory, string libraryRootPath)
    {
        var folderName = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar));
        var matchingWallpaper = Wallpapers.FirstOrDefault(wallpaper =>
            string.Equals(wallpaper.LibraryRootPath, libraryRootPath, StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(wallpaper.SteamId, folderName, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(Path.GetFileName(wallpaper.DirectoryPath.TrimEnd(Path.DirectorySeparatorChar)), folderName, StringComparison.OrdinalIgnoreCase)));

        var outputPackagePath = Path.Combine(directory, "scene.pkg");

        var files = GetSceneExtractionFiles(directory, outputPackagePath);
        return new SceneExtractionItem
        {
            Name = matchingWallpaper?.DisplayName ?? folderName,
            DirectoryPath = directory,
            OutputPackagePath = outputPackagePath,
            FileCount = files.Count,
            SizeBytes = files.Sum(file => file.Length),
            LastWriteTime = files.Count == 0 ? DateTime.MinValue : files.Max(file => file.LastWriteTimeUtc)
        };
    }

    private static List<FileInfo> GetSceneExtractionFiles(string directory, string outputPackagePath)
    {
        try
        {
            return Directory
                .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Where(path => !string.Equals(Path.GetFullPath(path), Path.GetFullPath(outputPackagePath), StringComparison.OrdinalIgnoreCase))
                .Select(path => new FileInfo(path))
                .Where(file => !string.Equals(file.Name, "scene.pkg.bak", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private async void RepackScene_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not SceneExtractionItem item)
        {
            return;
        }

        SetSceneActionButtonsEnabled(false);
        SceneExtractionProgressRing.Visibility = Visibility.Visible;
        SceneExtractionProgressRing.IsActive = true;

        try
        {
            var result = await _scenePackageRepacker.RepackAsync(
                item.DirectoryPath,
                item.OutputPackagePath);

            ShowSceneExtractionInfo("Scene repacked", $"Packed {result.FileCount:N0} files into {result.OutputPackagePath}.", InfoBarSeverity.Success);
            SceneExtractionOutputPathText.Text = item.DirectoryPath;
            SceneExtractionOutputSummaryText.Text = $"Repacked to:\n{result.OutputPackagePath}";
            RefreshSceneExtractionItems();
        }
        catch (Exception ex)
        {
            ShowSceneExtractionInfo("Repack failed", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SceneExtractionProgressRing.IsActive = false;
            SceneExtractionProgressRing.Visibility = Visibility.Collapsed;
            SetSceneActionButtonsEnabled(true);
        }
    }

    private void OpenSceneItemLocation_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not SceneExtractionItem item || !Directory.Exists(item.DirectoryPath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = item.DirectoryPath,
            UseShellExecute = true
        });
    }

    private void OpenSceneItemEditor_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not SceneExtractionItem item)
        {
            return;
        }

        if (!TryOpenWallpaperEditor(item.DirectoryPath))
        {
            ShowSceneExtractionInfo("Editor unavailable", "Set the Wallpaper Engine executable path in Settings first.", InfoBarSeverity.Warning);
        }
    }

    private bool TryOpenWallpaperEditor(string projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(CurrentSettings.EngineExecutablePath) ||
            !File.Exists(CurrentSettings.EngineExecutablePath) ||
            !Directory.Exists(projectDirectory))
        {
            return false;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = CurrentSettings.EngineExecutablePath,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("-editor");
        startInfo.ArgumentList.Add(projectDirectory);
        Process.Start(startInfo);
        return true;
    }

    private void SetSceneActionButtonsEnabled(bool isEnabled)
    {
        ExtractSceneButton.IsEnabled = isEnabled;
        SceneExtractionListView.IsEnabled = isEnabled;
    }

    private static string BuildSceneExtractionOutputSummary(SceneExtractionResult result)
    {
        if (result.ExtractedFiles.Count == 0)
        {
            return "No files were extracted.";
        }

        var previewFiles = result.ExtractedFiles
            .Select(path => Path.GetRelativePath(result.OutputDirectory, path))
            .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
            .Take(8)
            .ToList();

        var summary = $"Extracted {result.FileCount:N0} files:\n" + string.Join("\n", previewFiles);
        var remaining = result.FileCount - previewFiles.Count;
        return remaining > 0
            ? $"{summary}\n...and {remaining:N0} more."
            : summary;
    }

    private static string BuildSceneExtractionOutputSummary(string outputDirectory)
    {
        string[] files;
        try
        {
            files = Directory.GetFiles(outputDirectory, "*", SearchOption.AllDirectories);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "Output folder exists, but its contents could not be read.";
        }

        if (files.Length == 0)
        {
            return "Output folder exists but is empty.";
        }

        var previewFiles = files
            .Select(path => Path.GetRelativePath(outputDirectory, path))
            .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
            .Take(8)
            .ToList();

        var summary = $"Current output contains {files.Length:N0} files:\n" + string.Join("\n", previewFiles);
        var remaining = files.Length - previewFiles.Count;
        return remaining > 0
            ? $"{summary}\n...and {remaining:N0} more."
            : summary;
    }

    private void ShowSceneExtractionInfo(string title, string message, InfoBarSeverity severity)
    {
        SceneExtractionInfoBar.Title = title;
        SceneExtractionInfoBar.Message = message;
        SceneExtractionInfoBar.Severity = severity;
        SceneExtractionInfoBar.IsOpen = true;
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
            var downloadRoots = string.IsNullOrWhiteSpace(CurrentSettings.WallpaperDirectory)
                ? Array.Empty<WallpaperLibraryRoot>()
                : new[] { WallpaperLibraryRoot.FromPath(CurrentSettings.WallpaperDirectory) };
            DownloadPathSelector.ItemsSource = downloadRoots;
            if (downloadRoots.Length > 0 && DownloadPathSelector.SelectedIndex == -1)
            {
                DownloadPathSelector.SelectedIndex = 0;
            }
        }

        if (page == "SceneExtractor")
        {
            RefreshSceneExtractorPage();
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

        var libraryRoots = string.IsNullOrWhiteSpace(CurrentSettings.WallpaperDirectory)
            ? Array.Empty<WallpaperLibraryRoot>()
            : new[] { WallpaperLibraryRoot.FromPath(CurrentSettings.WallpaperDirectory) };
        _localMetadataByRoot = await _localMetadataStore.LoadForRootsAsync(libraryRoots.Select(root => root.Path));

        var scannedWallpapers = await _wallpaperScanner.ScanAsync(
            libraryRoots,
            CurrentSettings.SelectedWallpaperKeys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            CurrentSettings.NsfwWallpaperKeys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            CurrentSettings.MatureWallpaperKeys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, List<string>>(),
            CurrentSettings.ConsiderSubdirectoryAsTag);

        Wallpapers.Clear();
        foreach (var wallpaper in scannedWallpapers)
        {
            ApplyLocalMetadata(wallpaper);
            Wallpapers.Add(wallpaper);
            
            foreach (var tag in wallpaper.Tags)
            {
                if (!string.IsNullOrWhiteSpace(tag) && !Tags.Any(t => string.Equals(t.Name, tag, StringComparison.OrdinalIgnoreCase)))
                {
                    Tags.Add(new WallpaperTag { Name = tag, Color = "#3A7AFE" });
                }
            }
        }

        RefreshVisibleTags();
        ApplyWallpaperPresentation();
        RefreshVisibleWallpapers();
        RefreshSelectedWallpapers();
        UpdateEmptyStates();
        if (SceneExtractorPage.Visibility == Visibility.Visible)
        {
            RefreshSceneExtractorPage();
        }

        _ = FetchWorkshopMetadataAsync();

        // Keep context menu registry entries in sync with current wallpapers
        ContextMenuService.ApplySettings(CurrentSettings, Wallpapers.ToList());
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

            if (!CurrentSettings.DontAutoMarkNsfwFromWorkshop)
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
            else
            {
                // If "Dont Automark" is ON, we should also unmark those that match workshop metadata
                // to effectively "disable" the automatic treatment.
                if (meta.IsMature && wallpaper.IsMature)
                {
                    wallpaper.IsMature = false;
                    needsSave = true;
                }
                if (meta.IsAdult && wallpaper.IsNsfw)
                {
                    wallpaper.IsNsfw = false;
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
        foreach (var wallpaper in Wallpapers.Where(item => item.IsSelected))
        {
            SelectedWallpapers.Add(wallpaper);
        }

        // Rebuild HomeGroups
        var previousGroups = HomeGroups.ToList();
        HomeGroups.Clear();

        // 1. Unlisted Group
        var unlistedKeys = SelectedWallpapers.Select(w => w.Key).ToList();
        foreach (var list in CurrentSettings.WallpaperLists)
        {
            var listKeys = list.WallpaperKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            unlistedKeys.RemoveAll(k => listKeys.Contains(k));
        }

        // Ensure HomeViewMode is valid
        if (string.IsNullOrEmpty(CurrentSettings.HomeViewMode) || !LibraryViewModes.All.Contains(CurrentSettings.HomeViewMode))
        {
            CurrentSettings.HomeViewMode = LibraryViewModes.Thumbnail;
        }

        var isListVisible = CurrentSettings.HomeViewMode == LibraryViewModes.List ? Visibility.Visible : Visibility.Collapsed;
        var isGridVisible = CurrentSettings.HomeViewMode == LibraryViewModes.Thumbnail ? Visibility.Visible : Visibility.Collapsed;
        var canReorder = CurrentSettings.HomeSortMode == "Free Movement";
        var compactHeaderVisible = CurrentSettings.CardSize == CardSizeOptions.Large ? Visibility.Collapsed : Visibility.Visible;

        var unlistedGroup = new HomeGroupViewModel 
        { 
            Title = "Unlisted", 
            ListId = string.Empty,
            IsCollapsed = previousGroups.FirstOrDefault(g => g.ListId == string.Empty)?.IsCollapsed ?? false,
            ListVisibility = isListVisible,
            GridVisibility = isGridVisible,
            CanReorder = canReorder,
            CompactHeaderVisibility = compactHeaderVisible
        };
        
        var unlistedItems = SelectedWallpapers.Where(w => unlistedKeys.Contains(w.Key, StringComparer.OrdinalIgnoreCase));
        if (CurrentSettings.HomeSortMode == "Free Movement")
        {
            unlistedItems = unlistedItems.OrderBy(w => CurrentSettings.SelectedWallpaperKeys.IndexOf(w.Key));
        }
        else
        {
            unlistedItems = SortWallpapers(unlistedItems, CurrentSettings.HomeSortMode);
        }
        foreach (var item in unlistedItems) unlistedGroup.Wallpapers.Add(item);
        HomeGroups.Add(unlistedGroup);

        // 2. Custom Lists
        foreach (var list in CurrentSettings.WallpaperLists)
        {
            var group = new HomeGroupViewModel 
            { 
                Title = list.Name, 
                ListId = list.Id,
                IsCollapsed = previousGroups.FirstOrDefault(g => g.ListId == list.Id)?.IsCollapsed ?? false,
                ListVisibility = isListVisible,
                GridVisibility = isGridVisible,
                CanReorder = canReorder,
                CompactHeaderVisibility = compactHeaderVisible
            };
            
            var listItems = SelectedWallpapers.Where(w => list.WallpaperKeys.Contains(w.Key, StringComparer.OrdinalIgnoreCase));
            if (CurrentSettings.HomeSortMode == "Free Movement")
            {
                listItems = listItems.OrderBy(w => list.WallpaperKeys.IndexOf(w.Key));
            }
            else
            {
                listItems = SortWallpapers(listItems, CurrentSettings.HomeSortMode);
            }
            foreach (var item in listItems) group.Wallpapers.Add(item);
            HomeGroups.Add(group);
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

            wallpaper.DontShowWorkshopTags = CurrentSettings.DontShowWorkshopTags;

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
            new() { Id = CardButtonIds.AddLocalName, Name = "Add Local Name", Glyph = "\uE70F" },
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
            
        foreach (var group in HomeGroups)
        {
            group.CompactHeaderVisibility = visible;
        }
            
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
        var isListVisible = viewMode == LibraryViewModes.List ? Visibility.Visible : Visibility.Collapsed;
        var isGridVisible = viewMode == LibraryViewModes.Thumbnail ? Visibility.Visible : Visibility.Collapsed;
        
        foreach (var group in HomeGroups)
        {
            group.ListVisibility = isListVisible;
            group.GridVisibility = isGridVisible;
        }

        ApplyWallpaperPresentation();
    }

    private void ApplyHomeSortMode()
    {
        var canReorder = CurrentSettings.HomeSortMode == "Free Movement";
        foreach (var group in HomeGroups)
        {
            group.CanReorder = canReorder;
        }
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
        SceneExtractorPage.Visibility = page == "SceneExtractor" ? Visibility.Visible : Visibility.Collapsed;
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
            "SceneExtractor" => "Extract scene.pkg files into the local .carbon scene extraction folder.",
            "Downloader" => "Directly download Steam Workshop wallpapers into your library.",
            "Guide" => "Folder naming, scanning rules, and practical usage notes.",
            "Settings" => "Engine and wallpaper, appearance, library, and tags.",
            _ => "Selected wallpapers from your local Wallpaper Engine library."
        };
    }

    private async void DevModeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (DevModeToggle.IsOn)
        {
            if (CurrentSettings.IsDevMode) return;

            var passwordBox = new PasswordBox { PlaceholderText = "Enter developer password" };
            var dialog = new ContentDialog
            {
                Title = "Dev Mode Access",
                Content = passwordBox,
                PrimaryButtonText = "Unlock",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = RootGrid.XamlRoot
            };

            var result = await dialog.ShowAsync();
            var password = Environment.GetEnvironmentVariable("DEV_MODE_PASSWORD") ?? "red-sucks-kazi-kucks";

            if (result == ContentDialogResult.Primary && passwordBox.Password == password)
            {
                CurrentSettings.IsDevMode = true;
                UpdateDevModeUi();
                TriggerSaveSettings();
            }
            else
            {
                DevModeToggle.IsOn = false;
                if (result == ContentDialogResult.Primary)
                {
                    ShowDownloadInfo("Access Denied", "Incorrect developer password.", InfoBarSeverity.Error);
                }
            }
        }
        else
        {
            CurrentSettings.IsDevMode = false;
            UpdateDevModeUi();
            TriggerSaveSettings();
        }
    }

    private void UpdateDevModeUi()
    {
        if (DevAccountSelectorPanel == null) return;
        
        DevAccountSelectorPanel.Visibility = CurrentSettings.IsDevMode ? Visibility.Visible : Visibility.Collapsed;

        // Show/hide the hotkey indicator dot
        if (HotkeyDebugIndicator != null)
        {
            HotkeyDebugIndicator.Visibility = CurrentSettings.IsDevMode ? Visibility.Visible : Visibility.Collapsed;
            HotkeyDebugIndicator.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)); // reset to gray
        }
        if (CurrentSettings.IsDevMode)
        {
            var accounts = _downloadService.GetAvailableAccounts();
            var currentSelection = ForcedAccountComboBox.SelectedItem as string;
            
            ForcedAccountComboBox.Items.Clear();
            ForcedAccountComboBox.Items.Add("Random (Default)");
            foreach (var account in accounts) ForcedAccountComboBox.Items.Add(account);

            ForcedAccountComboBox.IsEnabled = accounts.Count > 0;
            
            if (!string.IsNullOrEmpty(currentSelection) && ForcedAccountComboBox.Items.Contains(currentSelection))
            {
                ForcedAccountComboBox.SelectedItem = currentSelection;
            }
            else
            {
                ForcedAccountComboBox.SelectedIndex = 0;
            }
        }
    }

    private void CancelDownload_Click(object sender, RoutedEventArgs e)
    {
        _downloadService.CancelDownload();
        CancelDownloadButton.IsEnabled = false;
        SkipCurrentDownloadButton.IsEnabled = false;
        ActiveDownloadStatusText.Text = "Cancelling downloads...";
    }

    private void RemoveFromBatch_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is WorkshopDownloadItem item)
        {
            BatchDownloadItems.Remove(item);
            
            // Update the input box to reflect the removal
            var ids = BatchDownloadItems.Select(i => i.WorkshopId).ToList();
            if (ids.Count > 0)
            {
                WorkshopUrlInput.Text = string.Join(Environment.NewLine, ids);
            }
            else
            {
                WorkshopUrlInput.Text = string.Empty;
            }
        }
    }

    private void RefreshHomeListButtons()
    {
        HomeListButtons.Clear();
        foreach (var list in CurrentSettings.WallpaperLists)
        {
            HomeListButtons.Add(list);
        }
    }

    private void RefreshContextMenuListComboBox()
    {
        var items = new List<string> { "Entire Home" };
        items.AddRange(CurrentSettings.WallpaperLists.Select(l => l.Name));
        
        ContextMenuListComboBox.ItemsSource = items;
        
        if (string.IsNullOrEmpty(CurrentSettings.ContextMenuListId))
        {
            ContextMenuListComboBox.SelectedIndex = 0;
        }
        else
        {
            var targetList = CurrentSettings.WallpaperLists.FirstOrDefault(l => l.Id == CurrentSettings.ContextMenuListId);
            if (targetList != null)
            {
                ContextMenuListComboBox.SelectedIndex = items.IndexOf(targetList.Name);
            }
            else
            {
                ContextMenuListComboBox.SelectedIndex = 0;
            }
        }
    }

    private void GlobalShortcutTextBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        var key = e.Key;
        if (key == VirtualKey.Control || key == VirtualKey.Shift || key == VirtualKey.Menu) return;

        var modifiers = new List<string>();
        var state = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        if (state.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)) modifiers.Add("Ctrl");
        
        state = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        if (state.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)) modifiers.Add("Shift");
        
        state = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu);
        if (state.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)) modifiers.Add("Alt");

        modifiers.Add(key.ToString());
        string shortcut = string.Join("+", modifiers);
        
        GlobalShortcutTextBox.Text = shortcut;
        CurrentSettings.GlobalShortcut = shortcut;
        _hotkeyService.RegisterShortcut(shortcut);
        TriggerSaveSettings();
        e.Handled = true;
    }

    private void ClearGlobalShortcut_Click(object sender, RoutedEventArgs e)
    {
        GlobalShortcutTextBox.Text = "None";
        CurrentSettings.GlobalShortcut = "None";
        _hotkeyService.Unregister();
        TriggerSaveSettings();
    }

    private void TriggerNextWallpaperAction()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("Hotkey pressed! Triggering next wallpaper action...");
            var listId = CurrentSettings.ContextMenuListId;
            var mode = CurrentSettings.ContextMenuNextMode;
            
            var candidates = ContextMenuService.GetWallpapersForList(CurrentSettings, Wallpapers.ToList());

            if (candidates == null || candidates.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("No candidates found for next wallpaper.");
                return;
            }

            WallpaperItem chosen = null;
            var random = new Random();
            if (mode == "Random")
            {
                chosen = candidates[random.Next(candidates.Count)];
            }
            else
            {
                if (!string.IsNullOrEmpty(CurrentSettings.CurrentlyPlayingWallpaperKey))
                {
                    var currentIndex = candidates.FindIndex(c => c.Key == CurrentSettings.CurrentlyPlayingWallpaperKey);
                    if (currentIndex >= 0 && currentIndex < candidates.Count - 1)
                    {
                        chosen = candidates[currentIndex + 1];
                    }
                    else
                    {
                        chosen = candidates[0];
                    }
                }
                else
                {
                    chosen = candidates[0];
                }
            }

            if (chosen != null)
            {
                System.Diagnostics.Debug.WriteLine($"Switching to: {chosen.DisplayName}");
                RunWallpaper(chosen);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CRITICAL ERROR in TriggerNextWallpaperAction: {ex}");
        }
    }

    private void ContextMenuNextToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings) return;
        CurrentSettings.ContextMenuNextWallpaper = ContextMenuNextToggle.IsOn;
        TriggerSaveSettings();

        if (ContextMenuNextToggle.IsOn)
            ContextMenuService.RegisterNextWallpaper();
        else
            ContextMenuService.UnregisterNextWallpaper();
    }

    private void ContextMenuSwitchToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings) return;
        CurrentSettings.ContextMenuSwitchWallpaper = ContextMenuSwitchToggle.IsOn;
        TriggerSaveSettings();

        if (ContextMenuSwitchToggle.IsOn)
            ContextMenuService.RegisterSwitchWallpaper(Wallpapers.ToList());
        else
            ContextMenuService.UnregisterSwitchWallpaper();
    }

    private void ContextMenuListComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || ContextMenuListComboBox.SelectedIndex == -1) return;
        
        if (ContextMenuListComboBox.SelectedIndex == 0)
        {
            CurrentSettings.ContextMenuListId = string.Empty;
        }
        else
        {
            var selectedName = ContextMenuListComboBox.SelectedItem as string;
            var list = CurrentSettings.WallpaperLists.FirstOrDefault(l => l.Name == selectedName);
            if (list != null)
            {
                CurrentSettings.ContextMenuListId = list.Id;
            }
        }
        TriggerSaveSettings();

        // Refresh the Switch submenu entries if the toggle is on
        if (CurrentSettings.ContextMenuSwitchWallpaper)
        {
            ContextMenuService.ApplySettings(CurrentSettings, Wallpapers.ToList());
        }
    }

    private void ContextMenuNextModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || ContextMenuNextModeComboBox.SelectedItem is not string mode) return;
        CurrentSettings.ContextMenuNextMode = mode;
        TriggerSaveSettings();
    }

    private void HomeListEntireHome_Click(object sender, RoutedEventArgs e)
    {
        // No longer used, handled by grouping
    }

    private void HomeListButton_Click(object sender, RoutedEventArgs e)
    {
        // No longer used, handled by grouping
    }

    private async void CreateHomeList_Click(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox { PlaceholderText = "List name", Width = 300 };
        var dialog = new ContentDialog
        {
            Title = "Create New List",
            Content = nameBox,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(nameBox.Text))
        {
            var name = nameBox.Text.Trim();
            if (CurrentSettings.WallpaperLists.Any(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                ShowDownloadInfo("List exists", $"A list named '{name}' already exists.", InfoBarSeverity.Error);
                return;
            }

            var newList = new WallpaperList { Id = Guid.NewGuid().ToString(), Name = name };
            CurrentSettings.WallpaperLists.Add(newList);
            RefreshHomeListButtons();
            RefreshContextMenuListComboBox();
            TriggerSaveSettings();
        }
    }

    private async void RenameHomeList_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is string listId)
        {
            var list = CurrentSettings.WallpaperLists.FirstOrDefault(l => l.Id == listId);
            if (list == null) return;
            
            var nameBox = new TextBox { Text = list.Name, Width = 300 };
            var dialog = new ContentDialog
            {
                Title = "Rename List",
                Content = nameBox,
                PrimaryButtonText = "Rename",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = RootGrid.XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(nameBox.Text))
            {
                var name = nameBox.Text.Trim();
                if (!string.Equals(list.Name, name, StringComparison.OrdinalIgnoreCase) && 
                    CurrentSettings.WallpaperLists.Any(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    ShowDownloadInfo("List exists", $"A list named '{name}' already exists.", InfoBarSeverity.Error);
                    return;
                }

                list.Name = name;
                RefreshHomeListButtons();
                RefreshContextMenuListComboBox();
                TriggerSaveSettings();
                RefreshSelectedWallpapers();
            }
        }
    }

    private async void DeleteHomeList_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is string listId)
        {
            var list = CurrentSettings.WallpaperLists.FirstOrDefault(l => l.Id == listId);
            if (list == null) return;
            
            var dialog = new ContentDialog
            {
                Title = "Delete List",
                Content = $"Are you sure you want to delete the list '{list.Name}'? The wallpapers will remain in 'Unlisted'.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = RootGrid.XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                CurrentSettings.WallpaperLists.Remove(list);
                RefreshHomeListButtons();
                RefreshContextMenuListComboBox();
                TriggerSaveSettings();
                RefreshSelectedWallpapers();
            }
        }
    }

    private void GroupSettings_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string listId)
        {
            var menu = new MenuFlyout();
            
            var renameItem = new MenuFlyoutItem { Text = "Rename", Icon = new FontIcon { Glyph = "\uE8AC" }, Tag = listId };
            renameItem.Click += RenameHomeList_Click;
            menu.Items.Add(renameItem);
            
            var deleteItem = new MenuFlyoutItem { Text = "Delete", Icon = new FontIcon { Glyph = "\uE74D" }, Tag = listId };
            deleteItem.Click += DeleteHomeList_Click;
            menu.Items.Add(deleteItem);
            
            menu.ShowAt(btn);
        }
    }

    private void ShowSettingsSection(string? section)
    {
        EngineWallpaperSettingsPanel.Visibility = section == "EngineWallpaper" ? Visibility.Visible : Visibility.Collapsed;
        AppearanceSettingsPanel.Visibility = section == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
        LibrarySettingsPanel.Visibility = section == "Library" ? Visibility.Visible : Visibility.Collapsed;
        NsfwMatureSettingsPanel.Visibility = section == "NsfwMature" ? Visibility.Visible : Visibility.Collapsed;
        TagSettingsPanel.Visibility = section == "Tags" ? Visibility.Visible : Visibility.Collapsed;
        SwitchingShortcutsSettingsPanel.Visibility = section == "SwitchingShortcuts" ? Visibility.Visible : Visibility.Collapsed;
        AdvanceSettingsPanel.Visibility = section == "AdvanceSettings" ? Visibility.Visible : Visibility.Collapsed;
        AboutSettingsPanel.Visibility = section == "About" ? Visibility.Visible : Visibility.Collapsed;
        ContactSettingsPanel.Visibility = section == "Contact" ? Visibility.Visible : Visibility.Collapsed;

        if (section == "NsfwMature")
        {
            UpdatePreviewWallpapers();
            ApplyWallpaperPresentation();
        }

        if (section == "AdvanceSettings")
        {
            DevModeToggle.IsOn = CurrentSettings.IsDevMode;
        }
    }

    private DispatcherTimer _saveSettingsTimer;

    private void TriggerSaveSettings()
    {
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

    private void ApplyLocalMetadata(WallpaperItem wallpaper)
    {
        if (string.IsNullOrWhiteSpace(wallpaper.LibraryRootPath) || string.IsNullOrWhiteSpace(wallpaper.SteamId))
        {
            return;
        }

        if (!_localMetadataByRoot.TryGetValue(wallpaper.LibraryRootPath, out var rootMetadata))
        {
            return;
        }

        if (!rootMetadata.Wallpapers.TryGetValue(wallpaper.SteamId, out var localMeta))
        {
            return;
        }

        wallpaper.LocalName = localMeta.LocalName ?? string.Empty;
        
        var loadedTags = localMeta.LocalTags?.Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        foreach (var tag in loadedTags)
        {
            if (!wallpaper.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                wallpaper.Tags.Add(tag);
            }
        }
    }

    private async Task SaveLocalMetadataAsync()
    {
        var byRoot = Wallpapers
            .Where(w => !string.IsNullOrWhiteSpace(w.LibraryRootPath))
            .GroupBy(w => w.LibraryRootPath, StringComparer.OrdinalIgnoreCase);

        foreach (var rootGroup in byRoot)
        {
            var rootPath = rootGroup.Key;
            var file = new LocalMetadataFile();

            foreach (var wallpaper in rootGroup.Where(w => !string.IsNullOrWhiteSpace(w.SteamId)))
            {
                var localName = wallpaper.LocalName?.Trim() ?? string.Empty;
                var localTags = wallpaper.Tags
                    .Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (string.IsNullOrWhiteSpace(localName) && localTags.Count == 0)
                {
                    continue;
                }

                file.Wallpapers[wallpaper.SteamId] = new LocalWallpaperMetadata
                {
                    LocalName = localName,
                    LocalTags = localTags
                };
            }

            await _localMetadataStore.SaveForRootAsync(rootPath, file);
            _localMetadataByRoot[rootPath] = file;
        }

        if (!string.IsNullOrWhiteSpace(CurrentSettings.WallpaperDirectory))
        {
            _localMetadataStore.EnsureCarbonDirectories(CurrentSettings.WallpaperDirectory);
        }
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
            var availableWidth = LibraryThumbnailView.ActualWidth;
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
        var secondaryBrush = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

        void AddInfoRow(StackPanel panel, string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = secondaryBrush,
                TextWrapping = TextWrapping.Wrap
            });

            var valueBlock = new TextBlock
            {
                Text = value,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(valueBlock, 1);
            row.Children.Add(valueBlock);
            panel.Children.Add(row);
        }
        
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
        leftDetails.Children.Add(new TextBlock { Text = $"ID: {wallpaper.IdText}", Foreground = secondaryBrush });
        leftDetails.Children.Add(new TextBlock { Text = $"Size: {wallpaper.SizeText}", Foreground = secondaryBrush });

        var rightDetails = new StackPanel { Spacing = 4 };
        if (wallpaper.IsNsfw) rightDetails.Children.Add(new TextBlock { Text = "NSFW", Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red) });
        if (wallpaper.IsMature) rightDetails.Children.Add(new TextBlock { Text = "Mature", Foreground = new SolidColorBrush(Microsoft.UI.Colors.Orange) });
        if (wallpaper.Tags.Count > 0) rightDetails.Children.Add(new TextBlock { Text = $"Tags: {wallpaper.TagsText}", Foreground = secondaryBrush, TextWrapping = TextWrapping.Wrap });

        detailsGrid.Children.Add(leftDetails);
        Grid.SetColumn(leftDetails, 0);
        detailsGrid.Children.Add(rightDetails);
        Grid.SetColumn(rightDetails, 1);

        infoInner.Children.Add(detailsGrid);
        if (meta != null)
        {
            var workshopRows = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
            AddInfoRow(workshopRows, "Original", meta.Title);
            AddInfoRow(workshopRows, "Subscribers", meta.SubscriptionCount.ToString("N0"));
            AddInfoRow(workshopRows, "Views", meta.ViewCount.ToString("N0"));
            AddInfoRow(workshopRows, "Favorites", meta.FavoriteCount.ToString("N0"));
            AddInfoRow(workshopRows, "Rating", meta.ContentRating);
            AddInfoRow(workshopRows, "Tags", string.Join(", ", meta.Tags));
            infoInner.Children.Add(workshopRows);
        }

        infoCard.Child = infoInner;
        leftStack.Children.Add(infoCard);
        
        rootGrid.Children.Add(leftStack);
        Grid.SetColumn(leftStack, 0);

        // Right Side: Description
        var rightStack = new StackPanel { Spacing = 12 };
        if (meta != null)
        {
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
                    Foreground = secondaryBrush
                });

                descInner.Children.Add(new TextBlock
                {
                    Text = desc,
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

    private void HomeView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        _draggedWallpapers = e.Items.Cast<WallpaperItem>().ToList();
    }

    private void HomeView_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Move;
    }

    private void HomeView_Drop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not HomeGroupViewModel targetGroup)
        {
            return;
        }

        if (_draggedWallpapers == null || _draggedWallpapers.Count == 0) return;

        foreach (var item in _draggedWallpapers)
        {
            MoveWallpaperToList(item, targetGroup.ListId);
        }
        
        _draggedWallpapers.Clear();
    }

    private void HomeView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (CurrentSettings.HomeSortMode != "Free Movement")
            return;
            
        if (sender.DataContext is not HomeGroupViewModel group)
            return;

        // The items in the group.Wallpapers observable collection have been reordered by the GridView/ListView
        var newKeys = group.Wallpapers
            .Select(wallpaper => wallpaper.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
            
        var activeList = CurrentSettings.WallpaperLists.FirstOrDefault(l => l.Id == group.ListId);

        if (activeList != null)
        {
            activeList.WallpaperKeys = newKeys;
        }
        else
        {
            CurrentSettings.SelectedWallpaperKeys = newKeys;
        }

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
                case "ChangeList":
                    ShowChangeListMenu(btn, item);
                    break;
                case "RemoveFromHome":
                    ToggleHomeStatus(item);
                    break;
                case CardButtonIds.ThreeDot:
                    ShowWallpaperActions(btn, item);
                    break;
                case CardButtonIds.AddTag:
                    ShowAddTagFlyout(btn, item);
                    break;
                case CardButtonIds.AddLocalName:
                    await EditLocalNameAsync(item);
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
        AddDynamicMenuItem(menu, CardButtonIds.AddLocalName, item);
        AddDynamicMenuItem(menu, CardButtonIds.AddTag, item);

        if (item.OverflowButtons.Count > 0)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            foreach (var btnId in item.OverflowButtons)
            {
                // Avoid duplicates if we already added them above as basic actions
                if (btnId == CardButtonIds.ThreeDot || btnId == CardButtonIds.AddToHome || btnId == CardButtonIds.Details || btnId == CardButtonIds.AddTag || btnId == CardButtonIds.AddLocalName)
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
            CardButtonIds.AddLocalName => "Add Local Name",
            CardButtonIds.Delete => "Delete Wallpaper",
            CardButtonIds.Details => "Wallpaper Details",
            _ => string.Empty
        };

        if (string.IsNullOrEmpty(text)) return;

        IconElement? icon = buttonId switch
        {
            CardButtonIds.AddToHome => new FontIcon { Glyph = item.HomeActionGlyph },
            CardButtonIds.AddTag => new SymbolIcon(Symbol.Tag),
            CardButtonIds.AddLocalName => new SymbolIcon(Symbol.Edit),
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
                case CardButtonIds.AddLocalName: await EditLocalNameAsync(item); break;
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
            if (!string.IsNullOrEmpty(tagName) && !item.Tags.Contains(tagName, StringComparer.OrdinalIgnoreCase))
            {
                item.Tags.Add(tagName);
                item.OnPropertyChanged(nameof(WallpaperItem.TagsText));
                TriggerSaveSettings();
                ApplyWallpaperPresentation();
            }
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

    private async Task EditLocalNameAsync(WallpaperItem item)
    {
        var input = new TextBox
        {
            PlaceholderText = "Custom local name",
            Text = item.LocalName ?? string.Empty
        };

        var dialog = new ContentDialog
        {
            Title = "Add Local Name",
            Content = input,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        item.LocalName = input.Text.Trim();
        item.OnPropertyChanged(nameof(WallpaperItem.LocalName));
        item.OnPropertyChanged(nameof(WallpaperItem.DisplayName));

        RefreshVisibleWallpapers();
        RefreshSelectedWallpapers();
        TriggerSaveSettings();
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
            // Also remove from any lists
            foreach (var list in CurrentSettings.WallpaperLists)
            {
                list.WallpaperKeys.Remove(item.Key);
            }
        }

        RefreshSelectedWallpapers();
        TriggerSaveSettings();
    }

    private void ShowChangeListMenu(FrameworkElement anchor, WallpaperItem item)
    {
        var menu = new MenuFlyout();
        
        // Unlisted option
        var unlistedItem = new MenuFlyoutItem { Text = "Unlisted" };
        unlistedItem.Click += (s, e) => MoveWallpaperToList(item, string.Empty);
        menu.Items.Add(unlistedItem);
        
        if (CurrentSettings.WallpaperLists.Any())
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            foreach (var list in CurrentSettings.WallpaperLists)
            {
                var listItem = new MenuFlyoutItem { Text = list.Name, Tag = list.Id };
                listItem.Click += (s, e) => MoveWallpaperToList(item, (string)listItem.Tag);
                menu.Items.Add(listItem);
            }
        }
        
        menu.ShowAt(anchor);
    }

    private void MoveWallpaperToList(WallpaperItem item, string targetListId)
    {
        // Remove from all existing lists
        foreach (var list in CurrentSettings.WallpaperLists)
        {
            list.WallpaperKeys.Remove(item.Key);
        }
        
        // Add to target list if not unlisted
        if (!string.IsNullOrEmpty(targetListId))
        {
            var targetList = CurrentSettings.WallpaperLists.FirstOrDefault(l => l.Id == targetListId);
            if (targetList != null && !targetList.WallpaperKeys.Contains(item.Key))
            {
                targetList.WallpaperKeys.Add(item.Key);
            }
        }
        
        RefreshSelectedWallpapers();
        TriggerSaveSettings();
    }

    private string FormatSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return string.Format("{0:n1} {1}", number, suffixes[counter]);
    }
}
