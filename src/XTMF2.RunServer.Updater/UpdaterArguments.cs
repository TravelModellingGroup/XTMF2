namespace XTMF2.RunServer.Updater;

internal sealed record UpdaterArguments(
    int PreviousProcessId,
    string InstallationDirectory,
    string ServerProcessPath,
    string ServerAssemblyPath,
    string DeploymentDirectory,
    string StagingRoot,
    IReadOnlyList<string> ServerArguments)
{
    public static UpdaterArguments Parse(string[] args)
    {
        int? previousProcessId = null;
        string? installationDirectory = null;
        string? serverProcessPath = null;
        string? serverAssemblyPath = null;
        string? deploymentDirectory = null;
        string? stagingRoot = null;
        var serverArguments = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var option = args[i];
            if (option == "--server-arg")
            {
                serverArguments.Add(ReadValue(args, ref i, option));
                continue;
            }

            var value = ReadValue(args, ref i, option);
            switch (option)
            {
                case "--old-pid":
                    if (!int.TryParse(value, out var parsedProcessId) || parsedProcessId <= 0 || previousProcessId.HasValue)
                        throw new ArgumentException("--old-pid must be specified once with a positive process ID.");
                    previousProcessId = parsedProcessId;
                    break;
                case "--install-dir":
                    if (installationDirectory is not null)
                        throw new ArgumentException("--install-dir may only be specified once.");
                    installationDirectory = GetFullPath(value, option);
                    break;
                case "--server-path":
                    if (serverProcessPath is not null)
                        throw new ArgumentException("--server-path may only be specified once.");
                    serverProcessPath = GetFullPath(value, option);
                    break;
                case "--server-assembly":
                    if (serverAssemblyPath is not null)
                        throw new ArgumentException("--server-assembly may only be specified once.");
                    serverAssemblyPath = GetFullPath(value, option);
                    break;
                case "--deployment-dir":
                    if (deploymentDirectory is not null)
                        throw new ArgumentException("--deployment-dir may only be specified once.");
                    deploymentDirectory = GetFullPath(value, option);
                    break;
                case "--staging-root":
                    if (stagingRoot is not null)
                        throw new ArgumentException("--staging-root may only be specified once.");
                    stagingRoot = GetFullPath(value, option);
                    break;
                default:
                    throw new ArgumentException($"Unknown updater option '{option}'.");
            }
        }

        if (!previousProcessId.HasValue || installationDirectory is null || serverProcessPath is null ||
            serverAssemblyPath is null || deploymentDirectory is null || stagingRoot is null)
            throw new ArgumentException("Updater options --old-pid, --install-dir, --server-path, --server-assembly, --deployment-dir, and --staging-root are required.");

        return new UpdaterArguments(previousProcessId.Value, installationDirectory, serverProcessPath,
            serverAssemblyPath, deploymentDirectory, stagingRoot, serverArguments);
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"Missing value for updater option '{option}'.");
        return args[++index];
    }

    private static string GetFullPath(string value, string option)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Updater option '{option}' cannot be empty.");
        return Path.GetFullPath(value);
    }
}
