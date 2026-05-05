using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;

namespace WallpaperManager.Models;

public sealed class WorkshopDownloadItem : INotifyPropertyChanged
{
    private string _workshopId = string.Empty;
    private WorkshopMetadata? _metadata;
    private double _progress;
    private string _status = "Pending";
    private bool _isDownloading;
    private bool _isCompleted;
    private bool _isFailed;

    public string WorkshopId
    {
        get => _workshopId;
        set => SetProperty(ref _workshopId, value);
    }

    public WorkshopMetadata? Metadata
    {
        get => _metadata;
        set
        {
            if (SetProperty(ref _metadata, value))
            {
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(SizeText));
                OnPropertyChanged(nameof(PreviewImage));
            }
        }
    }

    public string DisplayName => Metadata?.Title ?? "Unknown Wallpaper";

    public string SizeText => Metadata != null ? FormatSize(Metadata.FileSize) : "Unknown size";

    public BitmapImage? PreviewImage => !string.IsNullOrEmpty(Metadata?.PreviewUrl) 
        ? new BitmapImage(new Uri(Metadata.PreviewUrl)) 
        : null;

    public double Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        set => SetProperty(ref _isDownloading, value);
    }

    public bool IsCompleted
    {
        get => _isCompleted;
        set => SetProperty(ref _isCompleted, value);
    }

    public bool IsFailed
    {
        get => _isFailed;
        set => SetProperty(ref _isFailed, value);
    }

    public Visibility ProgressVisibility => IsDownloading ? Visibility.Visible : Visibility.Collapsed;
    public Visibility StatusVisibility => !IsDownloading ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        if (propertyName == nameof(IsDownloading))
        {
            OnPropertyChanged(nameof(ProgressVisibility));
            OnPropertyChanged(nameof(StatusVisibility));
        }
    }

    private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(storage, value)) return false;
        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private static string FormatSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }
}
