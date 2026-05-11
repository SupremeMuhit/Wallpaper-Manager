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
        IReadOnlySet<string> matureKeys,
        IReadOnlyDictionary<string, List<string>> wallpaperTags,
        bool considerSubdirectoryAsTag = false)
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

                    var item = CreateWallpaperItem(directory, root.Path);
                    item.IsSelected = selectedKeys.Contains(item.Key);
                    item.IsNsfw = nsfwKeys.Contains(item.Key);
                    item.IsMature = matureKeys.Contains(item.Key);
                    item.Tags = wallpaperTags.TryGetValue(item.Key, out var tags) ? [.. tags] : [];
                    if (considerSubdirectoryAsTag)
                    {
                        var relativePath = Path.GetRelativePath(root.Path, directory);
                        var parts = relativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 1 && parts[0] != ".")
                        {
                            var subDirName = parts[0];
                            if (!string.IsNullOrWhiteSpace(subDirName) && !item.Tags.Contains(subDirName, StringComparer.OrdinalIgnoreCase))
                            {
                                item.Tags.Add(subDirName);
                            }
                        }
                    }
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

    private static WallpaperItem CreateWallpaperItem(string directory, string libraryRootPath)
    {
        var files = GetFiles(directory);
        var steamId = ParseFolderName(Path.GetFileName(directory));

        return new WallpaperItem
        {
            DirectoryPath = directory,
            LibraryRootPath = libraryRootPath,
            PreviewPath = files.FirstOrDefault(file => string.Equals(Path.GetFileNameWithoutExtension(file), "preview", StringComparison.OrdinalIgnoreCase)
                && PreviewExtensions.Contains(Path.GetExtension(file))) ?? string.Empty,
            LaunchPath = GetLaunchPath(directory, files),
            DateModified = Directory.GetCreationTimeUtc(directory),
            LocalName = string.Empty,
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

    private static string ParseFolderName(string folderName)
    {
        return SteamIdOnlyPattern().IsMatch(folderName)
            ? folderName
            : string.Empty;
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

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex SteamIdOnlyPattern();
}
