/*
    Copyright 2017 University of Toronto

    This file is part of XTMF2.

    XTMF2 is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    XTMF2 is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with XTMF2.  If not, see <http://www.gnu.org/licenses/>.
*/
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Compression;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using XTMF2.Bus;
using XTMF2.Bus.Optimization;
using XTMF2.Configuration;

namespace XTMF2.Client
{
    public class Program
    {
        private static string[] _originalArguments = Array.Empty<string>();

        [MTAThread]
        static void Main(string[] args)
        {
            _originalArguments = args;
            if (string.Equals(Environment.GetEnvironmentVariable("XTMF2_DEPLOYMENT_WATCHDOG"), "1", StringComparison.Ordinal))
            {
                RunDeploymentWatchdog(args);
                return;
            }
            LogDeploymentStartupSummary();
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: XTMF.Run [-setup-security DIRECTORY] [-loadDLL dllPath] [-tcp ADDRESS PORT -security DIRECTORY] [-namedPipe PIPE_NAME]");
                return;
            }
            List<string> dllsToLoad = new List<string>();
            string? error = null;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "-loaddll":
                        if (i + 1 < args.Length)
                        {
                            dllsToLoad.Add(args[++i]);
                        }
                        else
                        {
                            Console.WriteLine("No second argument for a dll to load!");
                        }
                        break;
                    case "-config":
                        Console.WriteLine("Custom configurations are not supported yet.");
                        return;
                    case "-setup-security":
                        if (i + 1 >= args.Length)
                        {
                            Console.WriteLine("Expected a directory after -setup-security.");
                            return;
                        }
                        SetupSecurity(args[++i]);
                        return;
                    case "-remote":
                        Console.WriteLine("Remote connections are not supported yet.");
                        return;
                    case "-tcp":
                        if (i + 2 >= args.Length)
                        {
                            Console.WriteLine("Expected an address and port after getting a -tcp instruction!");
                            return;
                        }
                        var tcpAddress = args[++i];
                        if (!int.TryParse(args[++i], out var tcpPort))
                        {
                            Console.WriteLine("Expected a numeric TCP port after the -tcp address!");
                            return;
                        }
                        string? securityDirectory = null;
                        if (i + 2 < args.Length && string.Equals(args[i + 1], "-security", StringComparison.OrdinalIgnoreCase))
                        {
                            securityDirectory = args[i += 2];
                        }
                        else if (!IsLoopbackAddress(tcpAddress))
                        {
                            Console.WriteLine("Remote TCP RunServers require -security DIRECTORY.");
                            return;
                        }
                        RunTcpServer(tcpAddress, tcpPort, securityDirectory, dllsToLoad);
                        break;
                    case "-namedpipe":
                        if (args.Length == ++i)
                        {
                            Console.WriteLine("Expected a pipe name after getting a -namedPipe instruction!");
                            return;
                        }
                        Stream? serverStream = null;
                        try
                        {
                            if (!CreateStreams.CreateNamedPipeClient(args[i], out serverStream, out error))
                            {
                                Console.WriteLine("Error creating run client\r\n" + error);
                                return;
                            }
                            if(serverStream == null)
                            {
                                Console.WriteLine("Unable to create a connection to the host!");
                                return;
                            }
                            RunClient(serverStream, dllsToLoad);
                        }
                        finally
                        {
                            serverStream?.Dispose();
                        }
                        break;
                    default:
                        Console.WriteLine($"Unknown argument '{args[i]}'!");
                        return;
                }
            }
        }

        private static void SetupSecurity(string directory)
        {
            try
            {
                RunServerSecurity.SaveSetup(directory, out var token, out var fingerprint);
                Console.WriteLine($"RunServer security files created in {directory}");
                Console.WriteLine($"Certificate fingerprint: {fingerprint}");
                Console.WriteLine("Token: read runserver-token.txt and enter it in the XTMF2 GUI.");
                Console.WriteLine($"Start with: XTMF2.RunServer -tcp 0.0.0.0 PORT -security {directory}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
            {
                Console.WriteLine($"Unable to create RunServer security files: {ex.Message}");
            }
        }

        private static bool IsLoopbackAddress(string address)
            => string.Equals(address, "localhost", StringComparison.OrdinalIgnoreCase) ||
                (IPAddress.TryParse(address, out var parsed) && IPAddress.IsLoopback(parsed));

        private static void RunTcpServer(string address, int port, string? securityDirectory, List<string> extraDlls)
        {
            X509Certificate2? certificate = null;
            string? token = null;
            if (securityDirectory is not null)
            {
                try
                {
                    certificate = RunServerSecurity.LoadCertificate(securityDirectory);
                    token = RunServerSecurity.LoadToken(securityDirectory);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
                {
                    Console.WriteLine($"Unable to load RunServer security files: {ex.Message}");
                    return;
                }
            }

            if (!CreateStreams.CreateTcpListener(address, port, out var listener, out var boundPort, out var error))
            {
                Console.WriteLine("Error creating TCP RunServer listener\r\n" + error);
                return;
            }

            var tcpListener = listener!;
            Console.WriteLine($"RunServer listening on {address}:{boundPort}");
            if (certificate is not null)
                Console.WriteLine($"Certificate fingerprint: {RunServerSecurity.GetFingerprint(certificate)}");
            Console.Out.Flush();
            SignalDeploymentReady();
            using (tcpListener)
            using (var shutdown = new CancellationTokenSource())
            using (var remoteEstimationRegistry = new RemoteSharedEstimationRegistry())
            using (var remoteRunRegistry = new RemoteRunRegistry())
            {
                Console.CancelKeyPress += (_, eventArgs) =>
                {
                    eventArgs.Cancel = true;
                    shutdown.Cancel();
                    tcpListener.Stop();
                };

                while (!shutdown.IsCancellationRequested)
                {
                    TcpClient? client = null;
                    try
                    {
                        client = tcpListener.AcceptTcpClient();
                    }
                    catch (SocketException) when (shutdown.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (SocketException ex)
                    {
                        Console.WriteLine($"RunServer listener error while accepting a connection: {ex.Message}");
                        Console.Out.Flush();
                        continue;
                    }

                    if (client is null)
                        continue;

                    Console.WriteLine($"RunServer connection attempt from {client.Client.RemoteEndPoint?.ToString() ?? "unknown endpoint"}");
                    Console.Out.Flush();
                    var acceptedClient = client;
                    _ = securityDirectory is null
                        ? Task.Run(() => RunTcpClientUnsecured(acceptedClient, extraDlls,
                            remoteEstimationRegistry, remoteRunRegistry))
                        : Task.Run(() => RunTcpClient(acceptedClient, certificate!, token!, extraDlls,
                            remoteEstimationRegistry, remoteRunRegistry));
                }
            }
        }

        private static void RunTcpClientUnsecured(TcpClient client, List<string> extraDlls,
            RemoteSharedEstimationRegistry remoteEstimationRegistry, RemoteRunRegistry remoteRunRegistry)
        {
            var remoteEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown endpoint";
            Console.WriteLine($"Local RunServer connection accepted from {remoteEndpoint}");
            Console.Out.Flush();
            using (client)
            using (var stream = client.GetStream())
            {
                try
                {
                    RunClient(stream, extraDlls, usePrivateWorkspace: true,
                        allowDeployment: false,
                        remoteEstimationRegistry: remoteEstimationRegistry,
                        remoteRunRegistry: remoteRunRegistry);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Local RunServer client session failed for {remoteEndpoint}: {ex.Message}");
                    Console.Out.Flush();
                }
            }
        }

        private static void RunTcpClient(TcpClient client, X509Certificate2 certificate, string token, List<string> extraDlls,
            RemoteSharedEstimationRegistry remoteEstimationRegistry, RemoteRunRegistry remoteRunRegistry)
        {
            var remoteEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown endpoint";
            using (client)
            {
                if (!CreateStreams.AuthenticateSecureTcpClient(client, certificate, token, out var stream, out var error))
                {
                    Console.WriteLine($"RunServer security validation failed for {remoteEndpoint}: {error}");
                    Console.Out.Flush();
                    return;
                }
                Console.WriteLine($"RunServer connection authenticated successfully from {remoteEndpoint}");
                Console.Out.Flush();
                using (stream)
                {
                    try
                    {
                        RunClient(stream, extraDlls, usePrivateWorkspace: true,
                            allowDeployment: true,
                            remoteEstimationRegistry: remoteEstimationRegistry,
                            remoteRunRegistry: remoteRunRegistry);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"RunServer client session failed for {remoteEndpoint}: {ex.Message}");
                        Console.Out.Flush();
                    }
                }
                Console.WriteLine($"RunServer connection disconnected from {remoteEndpoint}");
                Console.Out.Flush();
            }
        }

        private static void RunClient(Stream serverStream, List<string> extraDlls, SystemConfiguration? config = null,
            bool usePrivateWorkspace = false, RemoteSharedEstimationRegistry? remoteEstimationRegistry = null,
            RemoteRunRegistry? remoteRunRegistry = null, bool allowDeployment = false)
        {
            var runtime = XTMFRuntime.CreateRuntime(config);
            var loadedConfig = runtime.SystemConfiguration;
            foreach (var dll in extraDlls)
            {
                loadedConfig.LoadAssembly(dll);
            }
            using var ownedRemoteEstimationRegistry = remoteEstimationRegistry is null
                ? new RemoteSharedEstimationRegistry()
                : null;
            var registry = remoteEstimationRegistry ?? ownedRemoteEstimationRegistry!;
            using var clientBus = new RunServerBus(serverStream, true, runtime, extraDlls,
                System.Diagnostics.Debugger.IsAttached, usePrivateWorkspace, remoteRunRegistry, allowDeployment);
            using var sharedEstimationWorker = clientBus.AttachSharedEstimationWorker();
            using var remoteCoordinator = new RemoteSharedEstimationCoordinatorSession(clientBus, registry);
            clientBus.SetDeploymentGate(
                () =>
                {
                    remoteRunRegistry?.BeginDrain();
                    registry.BeginDrain();
                },
                () =>
                {
                    remoteRunRegistry?.EndDrain();
                    registry.EndDrain();
                },
                () =>
                    (remoteRunRegistry?.IsIdle ?? true) && registry.IsIdle,
                RestartWithOriginalArguments);
            clientBus.SetSharedActivityProviders(registry.GetActiveActivities,
                sharedEstimationWorker.GetActiveActivities);
            try
            {
                clientBus.ProcessRequests();
            }
            finally
            {
                remoteRunRegistry?.Detach(clientBus);
            }
        }

        private static bool RestartWithOriginalArguments(string stagingRoot)
        {
            if (!Directory.Exists(stagingRoot) || !File.Exists(Path.Combine(stagingRoot, "deployment.zip")))
                return false;

            var processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath))
                return false;

            string? deploymentDirectory = null;
            try
            {
                var processDirectory = GetInstallationDirectory(processPath);
                if (string.IsNullOrWhiteSpace(processDirectory))
                    return false;

                deploymentDirectory = Path.Combine(processDirectory, ".deployments",
                    DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(deploymentDirectory);
                var archivePath = Path.Combine(deploymentDirectory, "deployment.zip");
                File.Copy(Path.Combine(stagingRoot, "deployment.zip"), archivePath);
                ExtractDeploymentArchive(archivePath, deploymentDirectory);
                EnsureDeploymentPayload(deploymentDirectory);

                var readyFile = Path.Combine(deploymentDirectory, ".deployment-ready");
                var watchdogInfo = CreateRunServerStartInfo(processPath, processDirectory, _originalArguments);
                watchdogInfo.Environment["XTMF2_DEPLOYMENT_WATCHDOG"] = "1";
                watchdogInfo.Environment["XTMF2_DEPLOYMENT_WATCHDOG_OLD_PID"] =
                    Environment.ProcessId.ToString();
                watchdogInfo.Environment["XTMF2_DEPLOYMENT_WATCHDOG_READY_FILE"] = readyFile;
                watchdogInfo.Environment["XTMF2_DEPLOYMENT_WATCHDOG_PROCESS_PATH"] = processPath;
                watchdogInfo.Environment["XTMF2_DEPLOYMENT_WATCHDOG_DEPLOYMENT_DIRECTORY"] = deploymentDirectory;
                foreach (var argument in _originalArguments)
                    watchdogInfo.ArgumentList.Add(argument);
                if (Process.Start(watchdogInfo) is null)
                    throw new InvalidOperationException("Unable to start the deployment watchdog.");
                Environment.Exit(0);
                return true;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or
                InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Console.Error.WriteLine($"Unable to restart RunServer: {exception.Message}");
                if (deploymentDirectory is not null)
                {
                    try
                    {
                        Directory.Delete(deploymentDirectory, recursive: true);
                    }
                    catch (IOException cleanupException)
                    {
                        Console.Error.WriteLine($"Unable to clean up failed deployment: {cleanupException.Message}");
                    }
                }
                return false;
            }
        }

        private static void SignalDeploymentReady()
        {
            var readyFile = Environment.GetEnvironmentVariable("XTMF2_DEPLOYMENT_READY_FILE");
            if (string.IsNullOrWhiteSpace(readyFile))
                return;

            try
            {
                File.WriteAllText(readyFile, "ready");
            }
            catch (IOException exception)
            {
                Console.Error.WriteLine($"Unable to signal RunServer readiness: {exception.Message}");
            }
        }

        private static void RunDeploymentWatchdog(string[] arguments)
        {
            var readyFile = Environment.GetEnvironmentVariable("XTMF2_DEPLOYMENT_WATCHDOG_READY_FILE");
            var processPath = Environment.GetEnvironmentVariable("XTMF2_DEPLOYMENT_WATCHDOG_PROCESS_PATH");
            var deploymentDirectory = Environment.GetEnvironmentVariable("XTMF2_DEPLOYMENT_WATCHDOG_DEPLOYMENT_DIRECTORY");
            if (string.IsNullOrWhiteSpace(readyFile) || string.IsNullOrWhiteSpace(processPath) ||
                string.IsNullOrWhiteSpace(deploymentDirectory))
                return;

            var oldProcessIdText = Environment.GetEnvironmentVariable("XTMF2_DEPLOYMENT_WATCHDOG_OLD_PID");
            _ = int.TryParse(oldProcessIdText, out var oldProcessId);
            var installationDirectory = GetInstallationDirectory(processPath);
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    if (oldProcessId <= 0 || Process.GetProcessById(oldProcessId).HasExited)
                        break;
                }
                catch (ArgumentException)
                {
                    break;
                }
                Thread.Sleep(250);
            }

            string? backupDirectory = null;
            Process? deployedProcess = null;
            try
            {
                if (oldProcessId > 0)
                {
                    try
                    {
                        if (!Process.GetProcessById(oldProcessId).HasExited)
                            throw new InvalidOperationException("The previous RunServer process did not exit before activation.");
                    }
                    catch (ArgumentException)
                    {
                    }
                }

                backupDirectory = Path.Combine(deploymentDirectory, ".backup");
                Directory.CreateDirectory(backupDirectory);
                EnsureDeploymentPayload(deploymentDirectory);
                ReplaceDeploymentFile(Path.Combine(installationDirectory, "XTMF2.dll"),
                    Path.Combine(deploymentDirectory, "XTMF2.dll"), Path.Combine(backupDirectory, "XTMF2.dll"));
                ReplaceDeploymentDirectory(Path.Combine(installationDirectory, "Modules"),
                    Path.Combine(deploymentDirectory, "Modules"), Path.Combine(backupDirectory, "Modules"));

                var startInfo = CreateRunServerStartInfo(processPath, installationDirectory, arguments);
                ClearDeploymentWatchdogEnvironment(startInfo);
                startInfo.Environment["XTMF2_DEPLOYMENT_MANIFEST"] =
                    Path.Combine(deploymentDirectory, "deployment-manifest.txt");
                startInfo.Environment["XTMF2_DEPLOYMENT_READY_FILE"] = readyFile;
                foreach (var argument in arguments)
                    startInfo.ArgumentList.Add(argument);
                deployedProcess = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Unable to start the deployed RunServer.");

                var readyDeadline = DateTime.UtcNow.AddSeconds(30);
                while (!File.Exists(readyFile) && DateTime.UtcNow < readyDeadline)
                {
                    if (deployedProcess.HasExited)
                        throw new InvalidOperationException("The deployed RunServer exited before becoming ready.");
                    Thread.Sleep(250);
                }
                if (!File.Exists(readyFile))
                    throw new InvalidOperationException("The deployed RunServer did not become ready.");

                Directory.Delete(backupDirectory, recursive: true);
                Directory.Delete(deploymentDirectory, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Console.Error.WriteLine($"Unable to activate the deployed RunServer: {exception.Message}");
                if (backupDirectory is null)
                    return;
                try
                {
                    if (deployedProcess is not null && !deployedProcess.HasExited)
                        deployedProcess.Kill(entireProcessTree: true);
                    RestoreDeploymentFile(Path.Combine(installationDirectory, "XTMF2.dll"),
                        Path.Combine(backupDirectory ?? string.Empty, "XTMF2.dll"));
                    RestoreDeploymentDirectory(Path.Combine(installationDirectory, "Modules"),
                        Path.Combine(backupDirectory ?? string.Empty, "Modules"));
                    var rollbackInfo = CreateRunServerStartInfo(processPath, installationDirectory, arguments);
                    ClearDeploymentWatchdogEnvironment(rollbackInfo);
                    Process.Start(rollbackInfo);
                }
                catch (Exception rollbackException) when (rollbackException is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    Console.Error.WriteLine($"Unable to restore the previous RunServer version: {rollbackException.Message}");
                }
            }
        }

        private static void ClearDeploymentWatchdogEnvironment(ProcessStartInfo startInfo)
        {
            startInfo.Environment.Remove("XTMF2_DEPLOYMENT_WATCHDOG");
            startInfo.Environment.Remove("XTMF2_DEPLOYMENT_WATCHDOG_OLD_PID");
            startInfo.Environment.Remove("XTMF2_DEPLOYMENT_WATCHDOG_READY_FILE");
            startInfo.Environment.Remove("XTMF2_DEPLOYMENT_WATCHDOG_PROCESS_PATH");
            startInfo.Environment.Remove("XTMF2_DEPLOYMENT_WATCHDOG_DEPLOYMENT_DIRECTORY");
        }

        private static ProcessStartInfo CreateRunServerStartInfo(string processPath, string workingDirectory,
            IEnumerable<string> arguments)
        {
            var startInfo = new ProcessStartInfo(processPath)
            {
                UseShellExecute = false,
                WorkingDirectory = workingDirectory,
                RedirectStandardInput = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                CreateNoWindow = false
            };
            if (IsDotnetHost(processPath))
                startInfo.ArgumentList.Add(typeof(Program).Assembly.Location);
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);
            return startInfo;
        }

        private static bool IsDotnetHost(string processPath)
        {
            var fileName = Path.GetFileNameWithoutExtension(processPath);
            return string.Equals(fileName, "dotnet", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetInstallationDirectory(string processPath)
        {
            if (IsDotnetHost(processPath))
                return Path.GetDirectoryName(typeof(Program).Assembly.Location) ?? Environment.CurrentDirectory;
            return Path.GetDirectoryName(processPath) ?? Environment.CurrentDirectory;
        }

        private static void ReplaceDeploymentFile(string destination, string staged, string backup)
        {
            if (!File.Exists(staged))
                throw new InvalidDataException($"Deployment is missing required file '{Path.GetFileName(staged)}'.");
            if (File.Exists(destination))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Move(destination, backup, overwrite: true);
            }
            File.Move(staged, destination, overwrite: true);
        }

        private static void ExtractDeploymentArchive(string archivePath, string destinationRoot)
        {
            using var archive = ZipFile.OpenRead(archivePath);
            foreach (var entry in archive.Entries)
            {
                var destination = Path.GetFullPath(Path.Combine(destinationRoot,
                    entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                var root = Path.GetFullPath(destinationRoot + Path.DirectorySeparatorChar);
                if (!destination.StartsWith(root, StringComparison.Ordinal))
                    throw new InvalidDataException("Deployment archive contains an unsafe path.");
                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destination);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
            }
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

        private static void RestoreDeploymentFile(string destination, string backup)
        {
            if (File.Exists(backup))
                File.Move(backup, destination, overwrite: true);
        }

        private static void ReplaceDeploymentDirectory(string destination, string staged, string backup)
        {
            if (!Directory.Exists(staged))
                throw new InvalidDataException("Deployment is missing the required Modules directory.");
            if (Directory.Exists(destination))
                Directory.Move(destination, backup);
            Directory.Move(staged, destination);
        }

        private static void RestoreDeploymentDirectory(string destination, string backup)
        {
            if (!Directory.Exists(backup))
                return;
            if (Directory.Exists(destination))
                Directory.Delete(destination, recursive: true);
            Directory.Move(backup, destination);
        }

        private static void LogDeploymentStartupSummary()
        {
            var manifestPath = Environment.GetEnvironmentVariable("XTMF2_DEPLOYMENT_MANIFEST");
            if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
                return;

            try
            {
                var modules = File.ReadAllLines(manifestPath)
                    .Where(module => module.Length <= 256 &&
                        !module.Any(char.IsControl) &&
                        (module.Equals("XTMF2.dll", StringComparison.Ordinal) ||
                         module.StartsWith("Modules/", StringComparison.Ordinal)))
                    .Take(4096)
                    .ToArray();
                Console.WriteLine($"RunServer restarted with deployment containing {modules.Length} module file(s):");
                foreach (var module in modules)
                    Console.WriteLine($"  {module}");
                Console.Out.Flush();
                Environment.SetEnvironmentVariable("XTMF2_DEPLOYMENT_MANIFEST", null);
            }
            catch (IOException exception)
            {
                Console.WriteLine($"RunServer restarted, but deployment overview could not be read: {exception.Message}");
                Console.Out.Flush();
            }
        }
    }
}