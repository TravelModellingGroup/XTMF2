using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace XTMF2.GUI;

internal enum StorageRootMigrationMode
{
    Copy,
    Move,
}

internal sealed record StorageRootMigrationProgress(string Status, double? Percentage);

internal static class StorageRootMigration
{
    private const int BufferSize = 1024 * 1024;

    public static void Migrate(string sourceRoot, string destinationRoot, StorageRootMigrationMode mode,
        IProgress<StorageRootMigrationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);

        var source = Path.GetFullPath(sourceRoot);
        var destination = Path.GetFullPath(destinationRoot);
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"The current data directory does not exist: {source}");
        if (IsSameOrNestedPath(source, destination) || IsSameOrNestedPath(destination, source))
            throw new InvalidOperationException("The current and new data directories must not overlap.");

        progress?.Report(new StorageRootMigrationProgress("Scanning current data...", null));
        var directories = new List<string>();
        var files = Scan(source, directories, cancellationToken);
        var totalWork = files.Aggregate(0L, (total, file) => checked(total + Math.Max(file.Length, 1)));
        long completedWork = 0;

        progress?.Report(new StorageRootMigrationProgress("Preparing destination folder structure...", null));
        Directory.CreateDirectory(destination);
        foreach (var relativeDirectory in directories.OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(destination, relativeDirectory));
        }

        var projectFiles = files.Where(file => IsRootDirectory(file.RelativePath, "Users")).ToArray();
        var runFiles = files.Where(file => IsRootDirectory(file.RelativePath, "Runs") ||
            IsRootDirectory(file.RelativePath, "RemoteRuns")).ToArray();
        var otherFiles = files.Except(projectFiles).Except(runFiles).ToArray();

        completedWork = CopyGroup(projectFiles, "Copying projects and model systems...", source, destination,
            totalWork, completedWork, progress, cancellationToken);
        completedWork = CopyGroup(otherFiles, "Copying other XTMF data...", source, destination,
            totalWork, completedWork, progress, cancellationToken);
        completedWork = CopyGroup(runFiles, "Copying run data...", source, destination,
            totalWork, completedWork, progress, cancellationToken);

        if (mode == StorageRootMigrationMode.Move)
        {
            progress?.Report(new StorageRootMigrationProgress("Removing copied source data...", null));
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Delete(source, recursive: true);
        }

        progress?.Report(new StorageRootMigrationProgress("Migration complete.", 100));
    }

    private static List<MigrationFile> Scan(string sourceRoot, List<string> directories,
        CancellationToken cancellationToken)
    {
        var files = new List<MigrationFile>();
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(sourceRoot);

        while (pendingDirectories.TryPop(out var currentDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var entry in Directory.EnumerateFileSystemEntries(currentDirectory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Storage migration does not support symbolic links: {entry}");

                var relativePath = Path.GetRelativePath(sourceRoot, entry);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    directories.Add(relativePath);
                    pendingDirectories.Push(entry);
                }
                else
                {
                    files.Add(new MigrationFile(entry, relativePath, new FileInfo(entry).Length));
                }
            }
        }

        return files.OrderBy(file => file.RelativePath, StringComparer.Ordinal).ToList();
    }

    private static long CopyGroup(IReadOnlyList<MigrationFile> files, string status, string sourceRoot,
        string destinationRoot, long totalWork, long completedWork,
        IProgress<StorageRootMigrationProgress>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(new StorageRootMigrationProgress(status, Percentage(completedWork, totalWork)));
        var lastReportedPercentage = (int)Percentage(completedWork, totalWork);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destinationPath = Path.Combine(destinationRoot, file.RelativePath);
            var copiedForFile = 0L;
            CopyFile(file, sourceRoot, destinationRoot, destinationPath, bytesCopied =>
            {
                copiedForFile = bytesCopied;
                var percentage = Percentage(completedWork + copiedForFile, totalWork);
                if ((int)percentage > lastReportedPercentage)
                {
                    lastReportedPercentage = (int)percentage;
                    progress?.Report(new StorageRootMigrationProgress(status, percentage));
                }
            }, cancellationToken);

            completedWork += Math.Max(file.Length, 1);
            var completedPercentage = Percentage(completedWork, totalWork);
            if ((int)completedPercentage > lastReportedPercentage)
            {
                lastReportedPercentage = (int)completedPercentage;
                progress?.Report(new StorageRootMigrationProgress(status, completedPercentage));
            }
        }

        return completedWork;
    }

    private static void CopyFile(MigrationFile file, string sourceRoot, string destinationRoot,
        string destinationPath, Action<long> reportBytesCopied, CancellationToken cancellationToken)
    {
        var destinationDirectory = Path.GetDirectoryName(destinationPath)!;
        Directory.CreateDirectory(destinationDirectory);
        var temporaryPath = Path.Combine(destinationDirectory,
            $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.xtmf-copy");

        try
        {
            var migratedManifest = RewriteRunManifest(file, sourceRoot, destinationRoot);
            if (migratedManifest is not null)
            {
                if (new FileInfo(file.SourcePath).Length != file.Length)
                    throw new IOException($"The source file changed while it was being copied: {file.SourcePath}");
                using var target = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    BufferSize, FileOptions.SequentialScan);
                target.Write(migratedManifest);
                target.Flush(flushToDisk: true);
                reportBytesCopied(file.Length);
            }
            else
            {
                using var source = new FileStream(file.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    BufferSize, FileOptions.SequentialScan);
                using var target = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    BufferSize, FileOptions.SequentialScan);
                var buffer = new byte[BufferSize];
                long bytesCopied = 0;
                int bytesRead;
                while ((bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    target.Write(buffer, 0, bytesRead);
                    bytesCopied += bytesRead;
                    reportBytesCopied(bytesCopied);
                }
                target.Flush(flushToDisk: true);

                if (bytesCopied != file.Length || source.Length != file.Length)
                    throw new IOException($"The source file changed while it was being copied: {file.SourcePath}");
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static byte[]? RewriteRunManifest(MigrationFile file, string sourceRoot, string destinationRoot)
    {
        if (!IsRootDirectory(file.RelativePath, "RemoteRuns") ||
            !string.Equals(Path.GetExtension(file.RelativePath), ".json", StringComparison.OrdinalIgnoreCase))
            return null;

        JsonNode? manifest;
        try
        {
            manifest = JsonNode.Parse(File.ReadAllBytes(file.SourcePath));
        }
        catch (JsonException)
        {
            return null;
        }

        if (manifest is not JsonObject manifestObject)
            return null;

        var workingDirectoryProperty = manifestObject.FirstOrDefault(property =>
            string.Equals(property.Key, "WorkingDirectory", StringComparison.OrdinalIgnoreCase));
        if (workingDirectoryProperty.Value is not JsonValue workingDirectoryValue ||
            !workingDirectoryValue.TryGetValue<string>(out var workingDirectory) ||
            string.IsNullOrWhiteSpace(workingDirectory) || !Path.IsPathFullyQualified(workingDirectory))
            return null;

        var fullWorkingDirectory = Path.GetFullPath(workingDirectory);
        var relativeWorkingDirectory = Path.GetRelativePath(sourceRoot, fullWorkingDirectory);
        if (!IsRelativePathWithinRoot(relativeWorkingDirectory))
            return null;

        manifestObject[workingDirectoryProperty.Key] = Path.GetFullPath(
            Path.Combine(destinationRoot, relativeWorkingDirectory));
        return JsonSerializer.SerializeToUtf8Bytes(manifest);
    }

    private static bool IsRootDirectory(string relativePath, string directoryName)
    {
        var separatorIndex = relativePath.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        var rootDirectory = separatorIndex < 0 ? relativePath : relativePath[..separatorIndex];
        return string.Equals(rootDirectory, directoryName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameOrNestedPath(string root, string candidate)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return relative == "." || IsRelativePathWithinRoot(relative);
    }

    private static bool IsRelativePathWithinRoot(string relativePath)
        => !Path.IsPathRooted(relativePath) && relativePath != ".." &&
            !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
            !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);

    private static double Percentage(long completed, long total)
        => total == 0 ? 100 : Math.Clamp(completed * 100d / total, 0, 100);

    private sealed record MigrationFile(string SourcePath, string RelativePath, long Length);
}