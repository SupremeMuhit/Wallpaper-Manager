using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

namespace WallpaperManager.Models;

public sealed class HomeGroupViewModel : INotifyPropertyChanged
{
    private bool _isCollapsed;
    public string Title { get; set; } = string.Empty;
    public string ListId { get; set; } = string.Empty;
    
    public Visibility SettingsVisibility => string.IsNullOrEmpty(ListId) ? Visibility.Collapsed : Visibility.Visible;

    private Visibility _listVisibility = Visibility.Collapsed;
    public Visibility ListVisibility
    {
        get => _isCollapsed ? Visibility.Collapsed : _listVisibility;
        set { _listVisibility = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsExpanded)); }
    }

    private Visibility _gridVisibility = Visibility.Visible;
    public Visibility GridVisibility
    {
        get => _isCollapsed ? Visibility.Collapsed : _gridVisibility;
        set { _gridVisibility = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsExpanded)); }
    }

    private bool _canReorder;
    public bool CanReorder
    {
        get => _canReorder;
        set { _canReorder = value; OnPropertyChanged(); }
    }

    private Visibility _compactHeaderVisibility = Visibility.Collapsed;
    public Visibility CompactHeaderVisibility
    {
        get => _compactHeaderVisibility;
        set { _compactHeaderVisibility = value; OnPropertyChanged(); }
    }

    public bool IsCollapsed
    {
        get => _isCollapsed;
        set
        {
            if (_isCollapsed != value)
            {
                _isCollapsed = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsExpanded));
                OnPropertyChanged(nameof(CollapseGlyph));
            }
        }
    }

    public Visibility IsExpanded => _isCollapsed ? Visibility.Collapsed : Visibility.Visible;
    
    // e70d is ChevronDown, e70e is ChevronUp
    public string CollapseGlyph => _isCollapsed ? "\uE70E" : "\uE70D";

    public ObservableCollection<WallpaperItem> Wallpapers { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
