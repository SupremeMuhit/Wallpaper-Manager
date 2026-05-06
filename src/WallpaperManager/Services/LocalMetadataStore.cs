using System.Text.Json;
using WallpaperManager.Models;

namespace WallpaperManager.Services;

public sealed class LocalMetadataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<Dictionary<string, LocalMetadataFile>> LoadForRootsAsync(IEnumerable<string> rootPaths)
    {
        var result = new Dictionary<string, LocalMetadataFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var rootPath in rootPaths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var metadata = await LoadForRootAsync(rootPath);
            result[rootPath] = metadata;
        }

        return result;
    }

    public async Task<LocalMetadataFile> LoadForRootAsync(string rootPath)
    {
        try
        {
            EnsureCarbonDirectories(rootPath);
            var localJsonPath = GetLocalJsonPath(rootPath);
            if (!File.Exists(localJsonPath))
            {
                return new LocalMetadataFile();
            }

            var json = await File.ReadAllTextAsync(localJsonPath);
            var parsed = JsonSerializer.Deserialize<LocalMetadataFile>(json, JsonOptions) ?? new LocalMetadataFile();
            parsed.Wallpapers ??= new(StringComparer.OrdinalIgnoreCase);
            return parsed;
        }
        catch
        {
            return new LocalMetadataFile();
        }
    }

    public async Task SaveForRootAsync(string rootPath, LocalMetadataFile metadata)
    {
        EnsureCarbonDirectories(rootPath);
        var localJsonPath = GetLocalJsonPath(rootPath);
        var json = JsonSerializer.Serialize(metadata, JsonOptions);
        await File.WriteAllTextAsync(localJsonPath, json);
    }

    public void EnsureCarbonDirectories(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return;
        }

        var carbonDir = GetCarbonDirectory(rootPath);
        Directory.CreateDirectory(carbonDir);
        Directory.CreateDirectory(Path.Combine(carbonDir, "Scene Extractions"));
    }

    private static string GetCarbonDirectory(string rootPath) => Path.Combine(rootPath, ".carbon");
    private static string GetLocalJsonPath(string rootPath) => Path.Combine(GetCarbonDirectory(rootPath), "local.json");
}
