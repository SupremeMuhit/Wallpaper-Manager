using System.Text;

namespace WallpaperManager.Services;

public sealed class ScenePackageExtractor
{
    private const int MaxMagicLength = 32;
    private const int MaxEntryPathLength = 255;
    private const int CopyBufferSize = 81920;

    public Task<SceneExtractionResult> ExtractAsync(string packagePath, string outputDirectory)
    {
        return Task.Run(() => Extract(packagePath, outputDirectory));
    }

    private static SceneExtractionResult Extract(string packagePath, string outputDirectory)
    {
        if (!File.Exists(packagePath))
        {
            throw new FileNotFoundException("scene.pkg was not found.", packagePath);
        }

        Directory.CreateDirectory(outputDirectory);

        using var stream = File.Open(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        var signature = ReadSizedString(reader, MaxMagicLength, "scene package signature");
        var entries = ReadEntries(reader);

        var dataOffset = stream.Position;
        var outputRoot = Path.GetFullPath(outputDirectory);
        if (!outputRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            outputRoot += Path.DirectorySeparatorChar;
        }

        var extractedFiles = new List<string>(entries.Count);
        var buffer = new byte[CopyBufferSize];
        foreach (var entry in entries)
        {
            var outputPath = GetSafeOutputPath(outputRoot, entry.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            if (dataOffset + entry.Offset + entry.Length > stream.Length)
            {
                throw new EndOfStreamException("Scene package contains an entry beyond the end of the file.");
            }

            stream.Seek(dataOffset + entry.Offset, SeekOrigin.Begin);
            using var output = File.Create(outputPath);
            CopyExact(stream, output, entry.Length, buffer);
            extractedFiles.Add(outputPath);
        }

        return new SceneExtractionResult(signature, entries.Count, outputDirectory, extractedFiles);
    }

    private static List<ScenePackageEntry> ReadEntries(BinaryReader reader)
    {
        var fileCount = reader.ReadInt32();
        if (fileCount < 0)
        {
            throw new InvalidDataException("Invalid scene package file count.");
        }

        var entries = new List<ScenePackageEntry>(fileCount);
        for (var i = 0; i < fileCount; i++)
        {
            var relativePath = ReadSizedString(reader, MaxEntryPathLength, "scene package path");
            var offset = reader.ReadInt32();
            var length = reader.ReadInt32();
            if (offset < 0 || length < 0)
            {
                throw new InvalidDataException("Invalid scene package file entry.");
            }

            entries.Add(new ScenePackageEntry(relativePath, offset, length));
        }

        return entries;
    }

    private static string ReadSizedString(BinaryReader reader, int maxLength, string fieldName)
    {
        var length = reader.ReadInt32();
        if (length < 0)
        {
            throw new InvalidDataException($"Invalid {fieldName} length.");
        }

        if (length > maxLength)
        {
            throw new InvalidDataException($"The {fieldName} is too long.");
        }

        return Encoding.UTF8.GetString(reader.ReadBytes(length));
    }

    private static string GetSafeOutputPath(string outputRoot, string relativePath)
    {
        var normalized = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

        var fullPath = Path.GetFullPath(Path.Combine(outputRoot, normalized));
        if (!fullPath.StartsWith(outputRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Scene package contains an unsafe file path.");
        }

        return fullPath;
    }

    private static void CopyExact(Stream input, Stream output, int byteCount, byte[] buffer)
    {
        var remaining = byteCount;
        while (remaining > 0)
        {
            var read = input.Read(buffer, 0, Math.Min(buffer.Length, remaining));
            if (read <= 0)
            {
                throw new EndOfStreamException("Scene package ended unexpectedly.");
            }

            output.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private sealed record ScenePackageEntry(string RelativePath, int Offset, int Length);
}

public sealed record SceneExtractionResult(
    string Signature,
    int FileCount,
    string OutputDirectory,
    IReadOnlyList<string> ExtractedFiles);
