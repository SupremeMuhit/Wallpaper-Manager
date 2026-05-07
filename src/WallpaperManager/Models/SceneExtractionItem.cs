namespace WallpaperManager.Models;

public sealed class SceneExtractionItem
{
    public string Name { get; set; } = string.Empty;
    public string DirectoryPath { get; set; } = string.Empty;
    public string OutputPackagePath { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public long SizeBytes { get; set; }
    public DateTime LastWriteTime { get; set; }

    public string FileCountText => $"{FileCount:N0} files";
    public string SizeText => FormatSize(SizeBytes);
    public string LastWriteText => LastWriteTime == DateTime.MinValue
        ? string.Empty
        : LastWriteTime.ToLocalTime().ToString("g");
    public string PackageTargetText => string.IsNullOrWhiteSpace(OutputPackagePath)
        ? "No package target"
        : OutputPackagePath;

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
