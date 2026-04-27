using System.Text.Json;
using WallpaperManager.Models;

namespace WallpaperManager.Services;

public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _legacyRootsPath;
    private readonly string _settingsPath;

    public AppSettingsStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directory = Path.Combine(appData, "WallpaperManager");
        Directory.CreateDirectory(directory);

        _legacyRootsPath = Path.Combine(directory, "library-roots.json");
        _settingsPath = Path.Combine(directory, "settings.json");
    }

    public async Task<AppSettings> LoadAsync()
    {
        if (File.Exists(_settingsPath))
        {
            await using var stream = File.OpenRead(_settingsPath);
            return await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions) ?? new AppSettings();
        }

        return new AppSettings
        {
            WallpaperDirectories = await LoadLegacyRootsAsync()
        };
    }

    public async Task SaveAsync(AppSettings settings)
    {
        await using var stream = File.Create(_settingsPath);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions);
    }

    private async Task<List<WallpaperLibraryRoot>> LoadLegacyRootsAsync()
    {
        if (!File.Exists(_legacyRootsPath))
        {
            return [];
        }

        await using var stream = File.OpenRead(_legacyRootsPath);
        return await JsonSerializer.DeserializeAsync<List<WallpaperLibraryRoot>>(stream, JsonOptions) ?? [];
    }
}
