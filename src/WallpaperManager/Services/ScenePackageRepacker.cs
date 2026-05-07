using System.Text;

namespace WallpaperManager.Services;

public sealed class ScenePackageRepacker
{
    private const string DefaultSignature = "PKGV0002";
    private const int MaxMagicLength = 32;
    private const int MaxEntryPathLength = 255;
    private const int CopyBufferSize = 81920;

    private static readonly HashSet<string> ExcludedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".DS_Store",
        "Thumbs.db",
        "desktop.ini",
        "scene.pkg",
        "scene.pkg.bak"
    };

    public Task<SceneRepackResult> RepackAsync(
        string inputDirectory,
        string outputPackagePath,
        string? signature = null,
        bool createBackup = false)
    {
        return Task.Run(() => Repack(inputDirectory, outputPackagePath, signature, createBackup));
    }

    private static SceneRepackResult Repack(
        string inputDirectory,
        string outputPackagePath,
        string? signature,
        bool createBackup)
    {
        if (!Directory.Exists(inputDirectory))
        {
            throw new DirectoryNotFoundException("Scene extraction folder was not found.");
        }

        if (string.IsNullOrWhiteSpace(outputPackagePath))
        {
            throw new ArgumentException("Output scene.pkg path is empty.", nameof(outputPackagePath));
        }

        signature = string.IsNullOrWhiteSpace(signature) ? DefaultSignature : signature;
        if (Encoding.UTF8.GetByteCount(signature) > MaxMagicLength)
        {
            throw new InvalidDataException("Scene package signature is too long.");
        }

        var inputRoot = Path.GetFullPath(inputDirectory);
        var outputPath = Path.GetFullPath(outputPackagePath);
        var entries = CollectEntries(inputRoot, outputPath);
        if (entries.Count == 0)
        {
            throw new InvalidDataException("Scene folder does not contain any files to repack.");
        }

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        if (createBackup && File.Exists(outputPath))
        {
            var backupPath = outputPath + ".bak";
            if (!File.Exists(backupPath))
            {
                File.Copy(outputPath, backupPath);
            }
        }

        using var stream = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);

        WriteSizedString(writer, signature);
        writer.Write(entries.Count);

        var currentOffset = 0;
        foreach (var entry in entries)
        {
            WriteSizedString(writer, entry.RelativePath);
            writer.Write(currentOffset);
            writer.Write(entry.Length);
            currentOffset += entry.Length;
        }

        var buffer = new byte[CopyBufferSize];
        foreach (var entry in entries)
        {
            using var input = File.Open(entry.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            input.CopyTo(stream, buffer.Length);
        }

        return new SceneRepackResult(signature, entries.Count, outputPath, entries.Sum(entry => entry.Length));
    }

    private static List<SceneRepackEntry> CollectEntries(string inputRoot, string outputPackagePath)
    {
        var rootWithSeparator = inputRoot.EndsWith(Path.DirectorySeparatorChar)
            ? inputRoot
            : inputRoot + Path.DirectorySeparatorChar;

        var entries = new List<SceneRepackEntry>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var filePath in Directory.EnumerateFiles(inputRoot, "*", SearchOption.AllDirectories))
        {
            var fullPath = Path.GetFullPath(filePath);
            if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Scene folder contains a file outside the input directory.");
            }

            if (string.Equals(fullPath, outputPackagePath, StringComparison.OrdinalIgnoreCase) ||
                ExcludedFileNames.Contains(Path.GetFileName(fullPath)))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(inputRoot, fullPath).Replace('\\', '/');
            if (relativePath.StartsWith("../", StringComparison.Ordinal) ||
                relativePath.Contains("/../", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Scene folder contains an unsafe file path.");
            }

            if (Encoding.UTF8.GetByteCount(relativePath) > MaxEntryPathLength)
            {
                throw new InvalidDataException($"Scene file path is too long: {relativePath}");
            }

            if (!seenPaths.Add(relativePath))
            {
                throw new InvalidDataException($"Duplicate scene file path: {relativePath}");
            }

            entries.Add(new SceneRepackEntry(relativePath, fullPath, checked((int)new FileInfo(fullPath).Length)));
        }

        return entries
            .OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.RelativePath, StringComparer.Ordinal)
            .ToList();
    }

    private static void WriteSizedString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private sealed record SceneRepackEntry(string RelativePath, string FullPath, int Length);
}

public sealed record SceneRepackResult(string Signature, int FileCount, string OutputPackagePath, long PayloadBytes);
