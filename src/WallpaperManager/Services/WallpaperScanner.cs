using System.Text.RegularExpressions;
using WallpaperManager.Models;

namespace WallpaperManager.Services;

public sealed partial class WallpaperScanner
{
    private static readonly HashSet<string> PreviewExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mkv", ".mov", ".avi", ".wmv"
    };

    public Task<IReadOnlyList<WallpaperItem>> ScanAsync(
        IEnumerable<WallpaperLibraryRoot> roots,
        IReadOnlySet<string> selectedKeys,
        IReadOnlySet<string> nsfwKeys,
        IReadOnlyDictionary<string, List<string>> wallpaperTags)
    {
        return Task.Run(() =>
        {
            var wallpapers = new List<WallpaperItem>();
            var seenDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var root in roots)
            {
                if (string.IsNullOrWhiteSpace(root.Path) || !Directory.Exists(root.Path))
                {
                    continue;
                }

                foreach (var directory in EnumerateCandidateDirectories(root.Path))
                {
                    if (!seenDirectories.Add(directory) || !IsWallpaperDirectory(directory))
                    {
                        continue;
                    }

                    var item = CreateWallpaperItem(directory);
                    item.IsSelected = selectedKeys.Contains(item.Key);
                    item.IsNsfw = nsfwKeys.Contains(item.Key);
                    item.Tags = wallpaperTags.TryGetValue(item.Key, out var tags) ? [.. tags] : [];
                    wallpapers.Add(item);
                }
            }

            return (IReadOnlyList<WallpaperItem>)wallpapers
                .OrderBy(wallpaper => wallpaper.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        });
    }

    private static IEnumerable<string> EnumerateCandidateDirectories(string root)
    {
        yield return root;

        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            string[] children;
            try
            {
                children = Directory.GetDirectories(pending.Pop());
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var child in children)
            {
                yield return child;
                pending.Push(child);
            }
        }
    }

    private static bool IsWallpaperDirectory(string directory)
    {
        var files = GetFiles(directory);
        var hasPreview = files.Any(file => string.Equals(Path.GetFileNameWithoutExtension(file), "preview", StringComparison.OrdinalIgnoreCase)
            && PreviewExtensions.Contains(Path.GetExtension(file)));
        var hasMetadata = files.Any(file => IsNamed(file, "project.json") || IsNamed(file, "meta.json"));
        var hasScene = files.Any(file => IsNamed(file, "scene.pkg"));
        var hasHtml = files.Any(file => IsNamed(file, "index.html"));
        var hasVideo = files.Any(file => VideoExtensions.Contains(Path.GetExtension(file)));

        return hasMetadata || hasScene || hasHtml || (hasPreview && hasVideo);
    }

    private static WallpaperItem CreateWallpaperItem(string directory)
    {
        var files = GetFiles(directory);
        var (steamId, localName) = ParseFolderName(Path.GetFileName(directory));

        return new WallpaperItem
        {
            DirectoryPath = directory,
            PreviewPath = files.FirstOrDefault(file => string.Equals(Path.GetFileNameWithoutExtension(file), "preview", StringComparison.OrdinalIgnoreCase)
                && PreviewExtensions.Contains(Path.GetExtension(file))) ?? string.Empty,
            LaunchPath = GetLaunchPath(directory, files),
            LocalName = localName,
            SteamId = steamId,
            SizeBytes = GetDirectorySize(directory)
        };
    }

    private static string GetLaunchPath(string directory, IReadOnlyList<string> files)
    {
        return files.FirstOrDefault(file => IsNamed(file, "project.json"))
            ?? files.FirstOrDefault(file => IsNamed(file, "index.html"))
            ?? files.FirstOrDefault(file => VideoExtensions.Contains(Path.GetExtension(file)))
            ?? files.FirstOrDefault(file => IsNamed(file, "scene.pkg"))
            ?? directory;
    }

    private static (string SteamId, string LocalName) ParseFolderName(string folderName)
    {
        var preferredMatch = PreferredNamePattern().Match(folderName);
        if (preferredMatch.Success)
        {
            return (preferredMatch.Groups["id"].Value, preferredMatch.Groups["name"].Value.Trim());
        }

        return SteamIdOnlyPattern().IsMatch(folderName)
            ? (folderName, string.Empty)
            : (string.Empty, folderName);
    }

    private static string[] GetFiles(string directory)
    {
        try
        {
            return Directory.GetFiles(directory);
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static long GetDirectorySize(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Sum(file =>
            {
                try
                {
                    return new FileInfo(file).Length;
                }
                catch (IOException)
                {
                    return 0;
                }
                catch (UnauthorizedAccessException)
                {
                    return 0;
                }
            });
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static bool IsNamed(string path, string fileName)
    {
        return string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"^\[(?<id>\d+)\]\s*(?<name>.+)$")]
    private static partial Regex PreferredNamePattern();

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex SteamIdOnlyPattern();
}
