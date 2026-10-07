using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace XTMF2.Bus;

internal static class RunArtifactArchive
{
    public static void CreateFromDirectory(string sourceDirectory, string archivePath,
        Action<long, long>? progress = null)
    {
        var root = Path.GetFullPath(sourceDirectory);
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        var items = Directory.EnumerateFileSystemEntries(root, "*", enumerationOptions)
            .Select(path => CreateArchiveItem(root, path))
            .OrderBy(item => item.RelativePath, StringComparer.Ordinal)
            .ToArray();
        var totalBytes = items.Sum(item => item.Length);
        long processedBytes = 0;
        long lastReportedBytes = 0;
        long lastReportedAt = Stopwatch.GetTimestamp();
        progress?.Invoke(0, totalBytes);

        using var output = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in items)
            {
                var entryName = item.RelativePath.Replace(Path.DirectorySeparatorChar, '/')
                    .Replace(Path.AltDirectorySeparatorChar, '/');
                if (item.Attributes.HasFlag(FileAttributes.Directory))
                {
                    archive.CreateEntry(entryName.TrimEnd('/') + "/");
                    continue;
                }

                var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                using var input = new FileStream(item.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var destination = entry.Open();
                while (true)
                {
                    var read = input.Read(buffer, 0, buffer.Length);
                    if (read == 0)
                        break;
                    destination.Write(buffer, 0, read);
                    processedBytes += read;
                    if (processedBytes - lastReportedBytes >= 1024 * 1024 ||
                        Stopwatch.GetElapsedTime(lastReportedAt).TotalMilliseconds >= 250)
                    {
                        progress?.Invoke(processedBytes, totalBytes);
                        lastReportedBytes = processedBytes;
                        lastReportedAt = Stopwatch.GetTimestamp();
                    }
                }
            }
        }
        progress?.Invoke(processedBytes, totalBytes);
    }

    private static ArchiveItem CreateArchiveItem(string root, string path)
    {
        var attributes = File.GetAttributes(path);
        return new ArchiveItem(path, Path.GetRelativePath(root, path), attributes,
            attributes.HasFlag(FileAttributes.Directory) ? 0 : new FileInfo(path).Length);
    }

    private sealed record ArchiveItem(string FullPath, string RelativePath, FileAttributes Attributes, long Length);
}