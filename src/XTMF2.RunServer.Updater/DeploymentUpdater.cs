using System.ComponentModel;
using System.Diagnostics;

namespace XTMF2.RunServer.Updater;

internal static class DeploymentUpdater
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(30);

    public static int Run(UpdaterArguments options)
    {
        if (!WaitForPreviousServer(options.PreviousProcessId))
        {
            Console.Error.WriteLine("The previous RunServer process did not exit before the update timeout.");
            TryDeleteDirectory(options.DeploymentDirectory);
            TryDeleteDirectory(options.StagingRoot);
            return 1;
        }

        var deploymentFiles = new DeploymentFileTransaction(options.InstallationDirectory, options.DeploymentDirectory);
        Process? deployedServer = null;
        try
        {
            deploymentFiles.Apply();

            var readyFile = Path.Combine(options.DeploymentDirectory, ".deployment-ready");
            var startInfo = CreateRunServerStartInfo(options);
            startInfo.Environment["XTMF2_DEPLOYMENT_MANIFEST"] =
                Path.Combine(options.DeploymentDirectory, "deployment-manifest.txt");
            startInfo.Environment["XTMF2_DEPLOYMENT_READY_FILE"] = readyFile;
            deployedServer = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Unable to start the deployed RunServer.");
            WaitForReadiness(deployedServer, readyFile);

            deployedServer.Dispose();
            deployedServer = null;
            if (TryDeleteDirectory(deploymentFiles.BackupDirectory))
                TryDeleteDirectory(options.DeploymentDirectory);
            TryDeleteDirectory(options.StagingRoot);
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or
            InvalidOperationException or ArgumentException or Win32Exception)
        {
            Console.Error.WriteLine($"Unable to activate the deployed RunServer: {exception.Message}");
            if (!StopProcess(deployedServer))
            {
                Console.Error.WriteLine($"Unable to stop the deployed RunServer; deployment backup retained at '{deploymentFiles.BackupDirectory}'.");
                return 1;
            }
            var rollbackSucceeded = false;
            try
            {
                deploymentFiles.Rollback();
                if (Process.Start(CreateRunServerStartInfo(options)) is null)
                    throw new InvalidOperationException("Unable to restart the previous RunServer version.");
                rollbackSucceeded = true;
            }
            catch (Exception rollbackException) when (rollbackException is IOException or UnauthorizedAccessException or
                InvalidOperationException or ArgumentException or Win32Exception)
            {
                Console.Error.WriteLine($"Unable to restore the previous RunServer version: {rollbackException.Message}");
            }

            if (rollbackSucceeded)
            {
                TryDeleteDirectory(options.DeploymentDirectory);
                TryDeleteDirectory(options.StagingRoot);
            }
            return 1;
        }
        finally
        {
            deployedServer?.Dispose();
        }
    }

    private static bool WaitForPreviousServer(int processId)
    {
        try
        {
            using var previousServer = Process.GetProcessById(processId);
            return previousServer.WaitForExit((int)ProcessTimeout.TotalMilliseconds);
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (Win32Exception exception)
        {
            Console.Error.WriteLine($"Unable to wait for the previous RunServer process: {exception.Message}");
            return false;
        }
    }

    private static void WaitForReadiness(Process deployedServer, string readyFile)
    {
        var deadline = DateTime.UtcNow + ProcessTimeout;
        while (!File.Exists(readyFile))
        {
            if (deployedServer.HasExited)
                throw new InvalidOperationException("The deployed RunServer exited before becoming ready.");
            if (DateTime.UtcNow >= deadline)
                throw new InvalidOperationException("The deployed RunServer did not become ready.");
            Thread.Sleep(250);
        }
    }

    private static ProcessStartInfo CreateRunServerStartInfo(UpdaterArguments options)
    {
        var startInfo = new ProcessStartInfo(options.ServerProcessPath)
        {
            UseShellExecute = false,
            WorkingDirectory = options.InstallationDirectory
        };
        if (IsDotnetHost(options.ServerProcessPath))
            startInfo.ArgumentList.Add(options.ServerAssemblyPath);
        foreach (var argument in options.ServerArguments)
            startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    private static bool IsDotnetHost(string processPath)
        => string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase);

    private static bool StopProcess(Process? process)
    {
        if (process is null)
            return true;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                return process.WaitForExit((int)ProcessTimeout.TotalMilliseconds);
            }
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            Console.Error.WriteLine($"Unable to terminate the deployed RunServer: {exception.Message}");
            return false;
        }
    }

    private static bool TryDeleteDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            return true;
        try
        {
            Directory.Delete(directory, recursive: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Unable to clean up deployment directory '{directory}': {exception.Message}");
            return false;
        }
    }
}
