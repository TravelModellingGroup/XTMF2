using System.IO.Compression;

namespace XTMF2.RunServer.Updater;

internal sealed class DeploymentFileTransaction
{
    private readonly string _deploymentDirectory;
    private readonly string _runtimePath;
    private readonly string _modulesPath;
    private readonly bool _hadOriginalRuntime;
    private readonly bool _hadOriginalModules;

    public DeploymentFileTransaction(string installationDirectory, string deploymentDirectory)
    {
        _deploymentDirectory = deploymentDirectory;
        _runtimePath = Path.Combine(installationDirectory, "XTMF2.dll");
        _modulesPath = Path.Combine(installationDirectory, "Modules");
        _hadOriginalRuntime = File.Exists(_runtimePath);
        _hadOriginalModules = Directory.Exists(_modulesPath);
        BackupDirectory = Path.Combine(deploymentDirectory, ".backup");
    }

    public string BackupDirectory { get; }

    public void Apply()
    {
        EnsureDeploymentPayload(_deploymentDirectory);
        Directory.CreateDirectory(BackupDirectory);
        ReplaceDeploymentFile(_runtimePath,
            Path.Combine(_deploymentDirectory, "XTMF2.dll"),
            Path.Combine(BackupDirectory, "XTMF2.dll"));
        ReplaceDeploymentDirectory(_modulesPath,
            Path.Combine(_deploymentDirectory, "Modules"),
            Path.Combine(BackupDirectory, "Modules"));
    }

    public void Rollback()
    {
        RestoreDeploymentFile(_runtimePath,
            Path.Combine(BackupDirectory, "XTMF2.dll"), _hadOriginalRuntime);
        RestoreDeploymentDirectory(_modulesPath,
            Path.Combine(BackupDirectory, "Modules"), _hadOriginalModules);
    }

    private static void EnsureDeploymentPayload(string deploymentDirectory)
    {
        var runtimePath = Path.Combine(deploymentDirectory, "XTMF2.dll");
        var modulesPath = Path.Combine(deploymentDirectory, "Modules");
        if (File.Exists(runtimePath) && Directory.Exists(modulesPath))
            return;

        var archivePath = Path.Combine(deploymentDirectory, "deployment.zip");
        if (!File.Exists(archivePath))
            throw new InvalidDataException("Deployment staging data is incomplete.");
        ExtractDeploymentArchive(archivePath, deploymentDirectory);
        if (!File.Exists(runtimePath) || !Directory.Exists(modulesPath))
            throw new InvalidDataException("Deployment archive is missing XTMF2.dll or Modules.");
    }

    private static void ExtractDeploymentArchive(string archivePath, string destinationRoot)
    {
        var root = Path.GetFullPath(destinationRoot);
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var normalizedName = entry.FullName.Replace('\\', '/');
            if (normalizedName.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment is "." or ".."))
                throw new InvalidDataException("Deployment archive contains an unsafe path.");
            if (!normalizedName.Equals("XTMF2.dll", StringComparison.Ordinal) &&
                !normalizedName.Equals("deployment-manifest.txt", StringComparison.Ordinal) &&
                !normalizedName.Equals("Modules", StringComparison.Ordinal) &&
                !normalizedName.StartsWith("Modules/", StringComparison.Ordinal))
                throw new InvalidDataException("Deployment archive contains an unexpected file.");
            if (normalizedName.Equals("Modules", StringComparison.Ordinal) && !string.IsNullOrEmpty(entry.Name))
                throw new InvalidDataException("Deployment archive contains an invalid Modules entry.");

            var relativePath = normalizedName.Replace('/', Path.DirectorySeparatorChar);
            var destination = Path.GetFullPath(Path.Combine(root, relativePath));
            var relativeDestination = Path.GetRelativePath(root, destination);
            if (Path.IsPathRooted(relativeDestination) || relativeDestination == ".." ||
                relativeDestination.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidDataException("Deployment archive contains an unsafe path.");
            if (normalizedName.StartsWith("Modules/", StringComparison.Ordinal) && !string.IsNullOrEmpty(entry.Name))
            {
                var modulesRoot = Path.GetFullPath(Path.Combine(root, "Modules")) + Path.DirectorySeparatorChar;
                var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (!destination.StartsWith(modulesRoot, comparison))
                    throw new InvalidDataException("Deployment archive contains an unsafe module path.");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }
    }

    private static void ReplaceDeploymentFile(string destination, string staged, string backup)
    {
        if (!File.Exists(staged))
            throw new InvalidDataException($"Deployment is missing required file '{Path.GetFileName(staged)}'.");
        if (File.Exists(destination))
            File.Move(destination, backup);
        File.Move(staged, destination);
    }

    private static void ReplaceDeploymentDirectory(string destination, string staged, string backup)
    {
        if (!Directory.Exists(staged))
            throw new InvalidDataException("Deployment is missing the required Modules directory.");
        if (Directory.Exists(destination))
            Directory.Move(destination, backup);
        Directory.Move(staged, destination);
    }

    private static void RestoreDeploymentFile(string destination, string backup, bool hadOriginal)
    {
        if (File.Exists(backup))
        {
            if (File.Exists(destination))
                File.Delete(destination);
            File.Move(backup, destination);
        }
        else if (!hadOriginal && File.Exists(destination))
        {
            File.Delete(destination);
        }
    }

    private static void RestoreDeploymentDirectory(string destination, string backup, bool hadOriginal)
    {
        if (Directory.Exists(backup))
        {
            if (Directory.Exists(destination))
                Directory.Delete(destination, recursive: true);
            Directory.Move(backup, destination);
        }
        else if (!hadOriginal && Directory.Exists(destination))
        {
            Directory.Delete(destination, recursive: true);
        }
    }
}
