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
        AppSettings settings;

        if (File.Exists(_settingsPath))
        {
            try
            {
                await using var stream = File.OpenRead(_settingsPath);
                settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions) ?? new AppSettings();
            }
            catch (JsonException)
            {
                // File is empty or corrupted
                settings = new AppSettings();
            }
            catch (Exception)
            {
                settings = new AppSettings();
            }
        }
        else
        {
            settings = new AppSettings();

            // Migrate from legacy multi-directory file
            var legacyRoots = await LoadLegacyRootsAsync();
            if (legacyRoots.Count > 0)
            {
                settings.WallpaperDirectory = legacyRoots[0].Path;
            }
        }

        // Migrate from multi-directory list to single directory (one-time)
        if (string.IsNullOrWhiteSpace(settings.WallpaperDirectory) && settings.WallpaperDirectories.Count > 0)
        {
            settings.WallpaperDirectory = settings.WallpaperDirectories[0].Path;
        }

        return settings;
    }

    public async Task SaveAsync(AppSettings settings)
    {
        var tempPath = _settingsPath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions);
        }
        File.Move(tempPath, _settingsPath, overwrite: true);
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
