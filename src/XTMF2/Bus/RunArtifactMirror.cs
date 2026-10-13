using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace XTMF2.Bus;

public static class RunArtifactMirror
{
    private const int TransferVersion = 1;
    private const int MaximumEntries = 100_000;
    private const string ManifestFileName = "manifest.json";
    private const string PayloadDirectoryName = "files";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal sealed record Entry(
        string RelativePath,
        bool IsDirectory,
        long Length,
        long CompressedLength,
        string Sha256,
        string PayloadName);

    private sealed record Manifest(int Version, Entry[] Entries);
    private sealed record SourceItem(string FullPath, string RelativePath, bool IsDirectory, long Length);

    internal static void CreateFromDirectory(string sourceDirectory, string mirrorDirectory,
        Action<long, long>? progress = null, int maxDegreeOfParallelism = 0)
    {
        var root = Path.GetFullPath(sourceDirectory);
        var destination = Path.GetFullPath(mirrorDirectory);
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException($"Artifact mirror already exists: {destination}");

        var staging = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        try
        {
            var enumerationOptions = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            var items = Directory.EnumerateFileSystemEntries(root, "*", enumerationOptions)
                .Select(path => CreateSourceItem(root, path))
                .OrderBy(item => item.RelativePath, StringComparer.Ordinal)
                .ToArray();
            if (items.Length > MaximumEntries)
                throw new InvalidDataException($"Artifact count exceeds the limit of {MaximumEntries}.");
            ValidateUniquePaths(items.Select(item => item.RelativePath));

            var totalBytes = items.Where(item => !item.IsDirectory).Sum(item => item.Length);
            var entries = new Entry[items.Length];
            var payloadDirectory = Path.Combine(staging, PayloadDirectoryName);
            Directory.CreateDirectory(payloadDirectory);
            long processedBytes = 0;
            long lastReportedBytes = 0;
            long lastReportedAt = Stopwatch.GetTimestamp();
            var progressLock = new object();
            progress?.Invoke(0, totalBytes);

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = maxDegreeOfParallelism > 0
                    ? maxDegreeOfParallelism
                    : Math.Max(1, Math.Min(Environment.ProcessorCount, 4))
            };
            Parallel.For(0, items.Length, parallelOptions, index =>
            {
                var item = items[index];
                if (item.IsDirectory)
                {
                    entries[index] = new Entry(item.RelativePath, true, 0, 0, string.Empty, string.Empty);
                    return;
                }

                var payloadName = $"{index:D8}.gz";
                var payloadPath = Path.Combine(payloadDirectory, payloadName);
                long fileBytes = 0;
                string sha256;
                using (var input = new FileStream(item.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var output = new FileStream(payloadPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
                    {
                        var buffer = new byte[81920];
                        while (true)
                        {
                            var read = input.Read(buffer, 0, buffer.Length);
                            if (read == 0)
                                break;
                            gzip.Write(buffer, 0, read);
                            hash.AppendData(buffer, 0, read);
                            fileBytes += read;
                            var currentBytes = Interlocked.Add(ref processedBytes, read);
                            lock (progressLock)
                            {
                                if (currentBytes - lastReportedBytes >= 1024 * 1024 ||
                                    Stopwatch.GetElapsedTime(lastReportedAt).TotalMilliseconds >= 250)
                                {
                                    progress?.Invoke(currentBytes, totalBytes);
                                    lastReportedBytes = currentBytes;
                                    lastReportedAt = Stopwatch.GetTimestamp();
                                }
                            }
                        }
                    }

                    if (fileBytes != item.Length)
                        throw new IOException($"Artifact changed while being compressed: {item.RelativePath}");

                    sha256 = Convert.ToHexString(hash.GetHashAndReset());
                }
                entries[index] = new Entry(item.RelativePath, false, fileBytes,
                    new FileInfo(payloadPath).Length, sha256, payloadName);
            });

            VerifyMirror(staging, entries);
            WriteManifest(Path.Combine(staging, ManifestFileName), entries);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            Directory.Move(staging, destination);
            progress?.Invoke(totalBytes, totalBytes);
        }
        catch
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
            throw;
        }
    }

    internal static IReadOnlyList<Entry> ReadManifest(string mirrorDirectory)
    {
        var manifestPath = Path.Combine(mirrorDirectory, ManifestFileName);
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllBytes(manifestPath), JsonOptions)
            ?? throw new InvalidDataException("Artifact mirror manifest is empty.");
        if (manifest.Version != 1 || manifest.Entries is null || manifest.Entries.Any(entry => entry is null))
            throw new InvalidDataException("Artifact mirror manifest version is not supported.");
        if (manifest.Entries.Length > MaximumEntries)
            throw new InvalidDataException($"Artifact count exceeds the limit of {MaximumEntries}.");

        ValidateUniquePaths(manifest.Entries.Select(entry => entry.RelativePath));
        foreach (var entry in manifest.Entries)
        {
            ValidateRelativePath(entry.RelativePath);
            if (entry.IsDirectory)
            {
                if (entry.Length != 0 || entry.CompressedLength != 0 ||
                    !string.IsNullOrEmpty(entry.PayloadName) || !string.IsNullOrEmpty(entry.Sha256))
                    throw new InvalidDataException("Artifact directory entry contains file data.");
            }
            else if (entry.Length < 0 || entry.CompressedLength < 0 ||
                     entry.Sha256 is null || entry.PayloadName is null ||
                     entry.PayloadName != Path.GetFileName(entry.PayloadName) ||
                     entry.PayloadName.Contains('\\') || entry.PayloadName.Contains(':') ||
                     !entry.PayloadName.EndsWith(".gz", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Artifact file entry has invalid metadata.");
            }
            if (!entry.IsDirectory)
            {
                try
                {
                    if (Convert.FromHexString(entry.Sha256).Length != 32)
                        throw new InvalidDataException("Artifact file checksum has an invalid length.");
                }
                catch (FormatException exception)
                {
                    throw new InvalidDataException("Artifact file checksum is invalid.", exception);
                }
            }
        }

        return manifest.Entries;
    }

    internal static string GetPayloadPath(string mirrorDirectory, Entry entry)
        => Path.Combine(mirrorDirectory, PayloadDirectoryName, entry.PayloadName);

    internal static void WriteTransfer(BinaryWriter writer, string mirrorDirectory)
    {
        var entries = ReadManifest(mirrorDirectory);
        writer.Write(TransferVersion);
        writer.Write(entries.Count);
        foreach (var entry in entries)
        {
            writer.Write(entry.IsDirectory);
            writer.Write(entry.RelativePath);
            writer.Write(entry.Length);
            writer.Write(entry.CompressedLength);
            if (!entry.IsDirectory)
                writer.Write(Convert.FromHexString(entry.Sha256));
        }

        foreach (var entry in entries.Where(entry => !entry.IsDirectory))
        {
            using var input = new FileStream(GetPayloadPath(mirrorDirectory, entry),
                FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length != entry.CompressedLength)
                throw new InvalidDataException($"Artifact mirror payload length is invalid: {entry.RelativePath}");
            CopyExactly(input, writer.BaseStream, entry.CompressedLength);
        }
    }

    internal static void ReadTransfer(BinaryReader reader, string mirrorDirectory,
        Action<long, long>? progress = null)
    {
        if (reader.ReadInt32() != TransferVersion)
            throw new InvalidDataException("Unsupported artifact mirror transfer version.");
        var count = reader.ReadInt32();
        if (count < 0 || count > MaximumEntries)
            throw new InvalidDataException($"Invalid artifact entry count: {count}.");

        var entries = new Entry[count];
        for (var index = 0; index < count; index++)
        {
            var isDirectory = reader.ReadBoolean();
            var relativePath = reader.ReadString();
            ValidateRelativePath(relativePath);
            var length = reader.ReadInt64();
            var compressedLength = reader.ReadInt64();
            if (length < 0 || compressedLength < 0 || (isDirectory && (length != 0 || compressedLength != 0)))
                throw new InvalidDataException($"Invalid artifact lengths: {relativePath}.");

            var sha256 = string.Empty;
            var payloadName = string.Empty;
            if (!isDirectory)
            {
                var hash = reader.ReadBytes(32);
                if (hash.Length != 32)
                    throw new EndOfStreamException("Artifact transfer ended in file metadata.");
                sha256 = Convert.ToHexString(hash);
                payloadName = $"{index:D8}.gz";
            }
            entries[index] = new Entry(relativePath, isDirectory, length, compressedLength, sha256, payloadName);
        }
        ValidateUniquePaths(entries.Select(entry => entry.RelativePath));
        var totalBytes = entries.Where(entry => !entry.IsDirectory)
            .Aggregate(0L, (total, entry) => checked(total + entry.CompressedLength));

        var destination = Path.GetFullPath(mirrorDirectory);
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException($"Artifact mirror destination already exists: {destination}");
        var staging = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.Combine(staging, PayloadDirectoryName));
        try
        {
            progress?.Invoke(0, totalBytes);
            long transferred = 0;
            var buffer = new byte[81920];
            foreach (var entry in entries.Where(entry => !entry.IsDirectory))
            {
                var payloadPath = Path.Combine(staging, PayloadDirectoryName, entry.PayloadName);
                using var output = new FileStream(payloadPath, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None);
                var remaining = entry.CompressedLength;
                while (remaining > 0)
                {
                    var read = reader.BaseStream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                    if (read == 0)
                        throw new EndOfStreamException($"Artifact payload was truncated: {entry.RelativePath}");
                    output.Write(buffer, 0, read);
                    remaining -= read;
                    transferred += read;
                    if (transferred == totalBytes || transferred % (1024 * 1024) < read)
                        progress?.Invoke(transferred, totalBytes);
                }
            }

            WriteManifest(Path.Combine(staging, ManifestFileName), entries);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            Directory.Move(staging, destination);
            progress?.Invoke(totalBytes, totalBytes);
        }
        catch
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
            throw;
        }
    }

    internal static void WriteManifest(string manifestPath, IReadOnlyList<Entry> entries)
    {
        var manifest = new Manifest(1, entries.ToArray());
        File.WriteAllBytes(manifestPath, JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions));
    }

    internal static void Verify(string mirrorDirectory)
        => VerifyMirror(mirrorDirectory, ReadManifest(mirrorDirectory));

    public static void ExtractToDirectory(string mirrorDirectory, string targetDirectory)
    {
        var entries = ReadManifest(mirrorDirectory);
        var root = Path.GetFullPath(targetDirectory);
        Directory.CreateDirectory(root);

        foreach (var entry in entries.Where(entry => entry.IsDirectory))
            Directory.CreateDirectory(GetContainedPath(root, entry.RelativePath));

        foreach (var entry in entries.Where(entry => !entry.IsDirectory))
        {
            var destination = GetContainedPath(root, entry.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                long length = 0;
                byte[] actualHash;
                using (var input = new FileStream(GetPayloadPath(mirrorDirectory, entry),
                           FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var gzip = new GZipStream(input, CompressionMode.Decompress))
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    var buffer = new byte[81920];
                    while (true)
                    {
                        var read = gzip.Read(buffer, 0, buffer.Length);
                        if (read == 0)
                            break;
                        if (read > entry.Length - length)
                            throw new InvalidDataException($"Artifact exceeds its declared size: {entry.RelativePath}");
                        output.Write(buffer, 0, read);
                        hash.AppendData(buffer, 0, read);
                        length += read;
                    }
                    output.Flush(true);
                    actualHash = hash.GetHashAndReset();
                }
                if (length != entry.Length ||
                    !CryptographicOperations.FixedTimeEquals(actualHash, Convert.FromHexString(entry.Sha256)))
                    throw new InvalidDataException($"Artifact verification failed: {entry.RelativePath}");
                File.Move(temporary, destination, true);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
    }

    private static SourceItem CreateSourceItem(string root, string path)
    {
        var attributes = File.GetAttributes(path);
        var isDirectory = attributes.HasFlag(FileAttributes.Directory);
        var relativePath = Path.GetRelativePath(root, path)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
        ValidateRelativePath(relativePath);
        return new SourceItem(path, relativePath, isDirectory,
            isDirectory ? 0 : new FileInfo(path).Length);
    }

    private static void CopyExactly(Stream source, Stream destination, long length)
    {
        var buffer = new byte[81920];
        while (length > 0)
        {
            var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, length));
            if (read == 0)
                throw new EndOfStreamException("Artifact mirror payload ended unexpectedly.");
            destination.Write(buffer, 0, read);
            length -= read;
        }
    }

    private static void VerifyMirror(string mirrorDirectory, IReadOnlyList<Entry> entries)
    {
        foreach (var entry in entries.Where(entry => !entry.IsDirectory))
        {
            using var input = new FileStream(GetPayloadPath(mirrorDirectory, entry),
                FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length != entry.CompressedLength)
                throw new InvalidDataException($"Artifact mirror payload length is invalid: {entry.RelativePath}");
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long length = 0;
            while (true)
            {
                var read = gzip.Read(buffer, 0, buffer.Length);
                if (read == 0)
                    break;
                hash.AppendData(buffer, 0, read);
                length += read;
            }
            if (length != entry.Length ||
                !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(entry.Sha256)))
                throw new InvalidDataException($"Artifact mirror verification failed: {entry.RelativePath}");
        }
    }

    private static string GetContainedPath(string root, string relativePath)
    {
        ValidateRelativePath(relativePath);
        var destination = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!destination.StartsWith(prefix, comparison))
            throw new InvalidDataException("Artifact path escapes the output directory.");
        return destination;
    }

    private static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains('\\') || path.Contains(':') ||
            path.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new InvalidDataException($"Invalid artifact path: {path}");
    }

    private static void ValidateUniquePaths(IEnumerable<string> paths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            ValidateRelativePath(path);
            if (!seen.Add(path))
                throw new InvalidDataException($"Duplicate artifact path: {path}");
        }
    }
}