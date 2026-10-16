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
using System.Linq;
using System.IO;
using System.Globalization;
using System.Text;
using System.Text.Json;
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
            ConfigureTimestampedConsole();
            if (args.Length == 0)
            {
                var defaultConfigPath = Path.Combine(AppContext.BaseDirectory, "XTMF2.RunServer.config");
                if (!File.Exists(defaultConfigPath))
                {
                    Console.WriteLine($"RunServer configuration file not found: {defaultConfigPath}");
                    Console.WriteLine("Usage: XTMF2.RunServer [-config path] [-setup-security DIRECTORY]");
                    return;
                }
                StartConfiguredServer(defaultConfigPath);
                return;
            }

            if (string.Equals(args[0], "-config", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length != 2)
                {
                    Console.WriteLine("Usage: XTMF2.RunServer -config path");
                    return;
                }
                StartConfiguredServer(args[1]);
                return;
            }

            _originalArguments = args;
            LogStartup("Process started; parsing command-line arguments.");
            LogDeploymentStartupSummary();
            List<string> dllsToLoad = new List<string>();
            string? error = null;
            string? serverStorageDirectory = null;
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
                        Console.WriteLine("-config must be the first argument and must be followed by exactly one configuration path.");
                        return;
                    case "-setup-security":
                        if (i + 1 >= args.Length)
                        {
                            Console.WriteLine("Expected a directory after -setup-security.");
                            return;
                        }
                        SetupSecurity(args[++i]);
                        return;
                    case "-serverstoragedirectory":
                        if (i + 1 >= args.Length)
                        {
                            Console.WriteLine("Expected a directory after -serverStorageDirectory.");
                            return;
                        }
                        serverStorageDirectory = args[++i];
                        break;
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
                        RunTcpServer(tcpAddress, tcpPort, securityDirectory, dllsToLoad, serverStorageDirectory);
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
                            if (serverStorageDirectory is null)
                            {
                                RunClient(serverStream, dllsToLoad, config: null,
                                    usePrivateWorkspace: false, remoteEstimationRegistry: null,
                                    remoteRunRegistry: new RemoteRunRegistry(), allowDeployment: false);
                            }
                            else
                            {
                                var storagePaths = new XTMFStoragePaths(serverStorageDirectory);
                                Directory.CreateDirectory(storagePaths.DataRoot);
                                LogNamedPipeConfiguration(storagePaths, dllsToLoad);
                                using var remoteEstimationRegistry = new RemoteSharedEstimationRegistry();
                                using var remoteRunRegistry = new RemoteRunRegistry(
                                    storagePaths.RemoteRunDirectory, storagePaths.RunWorkspaceBaseDirectory);
                                RunClient(serverStream, dllsToLoad, config: null,
                                    usePrivateWorkspace: true, remoteEstimationRegistry: remoteEstimationRegistry,
                                    remoteRunRegistry: remoteRunRegistry, allowDeployment: false,
                                    storagePaths: storagePaths);
                            }
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

        private static void StartConfiguredServer(string configPath)
        {
            var fullConfigPath = Path.GetFullPath(configPath);
            _originalArguments = ["-config", fullConfigPath];
            try
            {
                RunTcpServer(RunServerConfiguration.Load(fullConfigPath));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or System.Text.Json.JsonException or ArgumentException or CryptographicException)
            {
                Console.Error.WriteLine($"Unable to start RunServer: {exception.Message}");
                Console.Error.Flush();
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
                Console.WriteLine("Add the security directory to XTMF2.RunServer.config, then start with: XTMF2.RunServer -config XTMF2.RunServer.config");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
            {
                Console.WriteLine($"Unable to create RunServer security files: {ex.Message}");
            }
        }

        private static bool IsLoopbackAddress(string address)
            => string.Equals(address, "localhost", StringComparison.OrdinalIgnoreCase) ||
                (IPAddress.TryParse(address, out var parsed) && IPAddress.IsLoopback(parsed));

        private static void RunTcpServer(string address, int port, string? securityDirectory,
            List<string> extraDlls, string? serverStorageDirectory)
        {
            var configuration = new RunServerConfiguration
            {
                ServerStorageDirectory = serverStorageDirectory ?? XTMFStoragePaths.GetDefaultRunServerDataRoot(),
                ListenAddress = address,
                Port = port,
                SecurityDirectory = securityDirectory,
                AdditionalAssemblies = extraDlls,
                AllowDeployment = securityDirectory is not null,
            };
            configuration.Validate();
            RunTcpServer(configuration);
        }

        private static void RunTcpServer(RunServerConfiguration configuration)
        {
            configuration.Validate();
            var storagePaths = configuration.CreateStoragePaths();
            Directory.CreateDirectory(storagePaths.DataRoot);
            LogConfiguration(configuration, storagePaths);
            LogStartup($"Initializing TCP server on {configuration.ListenAddress}:{configuration.Port}.");
            X509Certificate2? certificate = null;
            string? token = null;
            if (configuration.SecurityDirectory is not null)
            {
                LogStartup("Loading TCP server security credentials.");
                try
                {
                    certificate = RunServerSecurity.LoadCertificate(configuration.SecurityDirectory);
                    token = RunServerSecurity.LoadToken(configuration.SecurityDirectory);
                    LogStartup("TCP server security credentials loaded.");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
                {
                    Console.WriteLine($"Unable to load RunServer security files: {ex.Message}");
                    return;
                }
            }

            LogStartup("Binding the TCP listener.");
            if (!CreateStreams.CreateTcpListener(configuration.ListenAddress, configuration.Port, out var listener, out var boundPort, out var error))
            {
                Console.WriteLine("Error creating TCP RunServer listener\r\n" + error);
                return;
            }

            var tcpListener = listener!;
            Console.WriteLine($"RunServer listening on {configuration.ListenAddress}:{boundPort}");
            if (certificate is not null)
                Console.WriteLine($"Certificate fingerprint: {RunServerSecurity.GetFingerprint(certificate)}");
            Console.Out.Flush();
            LogStartup($"TCP listener bound on {configuration.ListenAddress}:{boundPort}.");
            SignalDeploymentReady();
            using (tcpListener)
            using (var shutdown = new CancellationTokenSource())
            using (var remoteEstimationRegistry = new RemoteSharedEstimationRegistry())
            using (var remoteRunRegistry = new RemoteRunRegistry(
                storagePaths.RemoteRunDirectory, storagePaths.RunWorkspaceBaseDirectory))
            {
                Console.CancelKeyPress += (_, eventArgs) =>
                {
                    eventArgs.Cancel = true;
                    shutdown.Cancel();
                    tcpListener.Stop();
                };

                LogStartup($"READY: accepting TCP connections on {configuration.ListenAddress}:{boundPort}.");
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
                    _ = configuration.SecurityDirectory is null
                        ? Task.Run(() => RunTcpClientUnsecured(acceptedClient, configuration, storagePaths,
                            remoteEstimationRegistry, remoteRunRegistry))
                        : Task.Run(() => RunTcpClient(acceptedClient, certificate!, token!, configuration, storagePaths,
                            remoteEstimationRegistry, remoteRunRegistry));
                }
            }
        }

        private static void RunTcpClientUnsecured(TcpClient client, RunServerConfiguration configuration,
            XTMFStoragePaths storagePaths,
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
                    RunClient(stream, configuration.AdditionalAssemblies, config: null,
                        usePrivateWorkspace: true,
                        remoteEstimationRegistry: remoteEstimationRegistry,
                        remoteRunRegistry: remoteRunRegistry, allowDeployment: configuration.AllowDeployment,
                        storagePaths: storagePaths);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Local RunServer client session failed for {remoteEndpoint}: {ex.Message}");
                    Console.Out.Flush();
                }
            }
        }

        private static void RunTcpClient(TcpClient client, X509Certificate2 certificate, string token,
            RunServerConfiguration configuration, XTMFStoragePaths storagePaths,
            RemoteSharedEstimationRegistry remoteEstimationRegistry, RemoteRunRegistry remoteRunRegistry)
        {
            var remoteEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown endpoint";
            using (client)
            {
                if (!CreateStreams.AuthenticateSecureTcpClient(client, certificate, token, out var stream, out var error))
                {
                    Console.WriteLine($"RunServer TLS/token authentication failed for {remoteEndpoint}: {error}");
                    Console.Out.Flush();
                    return;
                }
                Console.WriteLine($"RunServer connection authenticated successfully from {remoteEndpoint}");
                Console.Out.Flush();
                using (stream)
                {
                    try
                    {
                        RunClient(stream, configuration.AdditionalAssemblies, config: null,
                            usePrivateWorkspace: true,
                            remoteEstimationRegistry: remoteEstimationRegistry,
                            remoteRunRegistry: remoteRunRegistry, allowDeployment: configuration.AllowDeployment,
                            storagePaths: storagePaths);
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

        private static void RunClient(Stream serverStream, List<string> extraDlls, SystemConfiguration? config,
            bool usePrivateWorkspace, RemoteSharedEstimationRegistry? remoteEstimationRegistry,
            RemoteRunRegistry remoteRunRegistry, bool allowDeployment, XTMFStoragePaths? storagePaths = null)
        {
            LogStartup("Initializing the XTMF runtime for a client connection.");
            var runtime = storagePaths is null
                ? XTMFRuntime.CreateRuntime(config)
                : XTMFRuntime.CreateRuntime(storagePaths);
            LogStartup("XTMF runtime initialized.");
            var loadedConfig = runtime.SystemConfiguration;
            foreach (var dll in extraDlls)
            {
                LogStartup($"Loading additional assembly '{Path.GetFileName(dll)}'.");
                loadedConfig.LoadAssembly(dll);
                LogStartup("Additional assembly loaded.");
            }
            using var ownedRemoteEstimationRegistry = remoteEstimationRegistry is null
                ? new RemoteSharedEstimationRegistry()
                : null;
            var registry = remoteEstimationRegistry ?? ownedRemoteEstimationRegistry!;
            using var clientBus = new RunServerBus(serverStream, true, runtime, extraDlls,
                System.Diagnostics.Debugger.IsAttached, usePrivateWorkspace, remoteRunRegistry, allowDeployment,
                storagePaths);
            using var sharedEstimationWorker = clientBus.AttachSharedEstimationWorker();
            using var remoteCoordinator = new RemoteSharedEstimationCoordinatorSession(clientBus, registry);
            clientBus.SetDeploymentGate(
                () =>
                {
                    remoteRunRegistry.BeginDrain();
                    registry.BeginDrain();
                },
                () =>
                {
                    remoteRunRegistry.EndDrain();
                    registry.EndDrain();
                },
                () =>
                    remoteRunRegistry.IsIdle && registry.IsIdle,
                RestartWithOriginalArguments);
            clientBus.SetSharedActivityProviders(registry.GetActiveActivities,
                sharedEstimationWorker.GetActiveActivities);
            try
            {
                clientBus.ProcessRequests();
            }
            finally
            {
                remoteRunRegistry.Detach(clientBus);
            }
        }

        private static void LogStartup(string message)
        {
            Console.Error.WriteLine($"[RunServer] {message}");
            Console.Error.Flush();
        }

        private static void LogConfiguration(RunServerConfiguration configuration, XTMFStoragePaths storagePaths)
        {
            var effectiveConfiguration = new RunServerConfiguration
            {
                Version = configuration.Version,
                ServerStorageDirectory = storagePaths.DataRoot,
                ListenAddress = configuration.ListenAddress,
                Port = configuration.Port,
                SecurityDirectory = configuration.SecurityDirectory is null
                    ? null
                    : Path.GetFullPath(configuration.SecurityDirectory),
                AdditionalAssemblies = configuration.AdditionalAssemblies
                    .Select(assembly => Path.GetFullPath(assembly))
                    .ToList(),
                AllowDeployment = configuration.AllowDeployment,
                RunWorkspaceBaseDirectory = storagePaths.RunWorkspaceBaseDirectory,
            };
            Console.WriteLine("Effective RunServer configuration:");
            Console.WriteLine(JsonSerializer.Serialize(effectiveConfiguration, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            }));
        }

        private static void LogNamedPipeConfiguration(XTMFStoragePaths storagePaths, List<string> additionalAssemblies)
        {
            Console.WriteLine("Effective RunServer configuration:");
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                transport = "namedPipe",
                serverStorageDirectory = storagePaths.DataRoot,
                runWorkspaceBaseDirectory = storagePaths.RunWorkspaceBaseDirectory,
                additionalAssemblies = additionalAssemblies.Select(assembly => Path.GetFullPath(assembly)).ToArray(),
                allowDeployment = false,
            }, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static void ConfigureTimestampedConsole()
        {
            Console.SetOut(new TimestampedTextWriter(Console.Out));
            Console.SetError(new TimestampedTextWriter(Console.Error));
        }

        private sealed class TimestampedTextWriter(TextWriter inner) : TextWriter
        {
            private readonly object _sync = new();
            private bool _atLineStart = true;

            public override Encoding Encoding => inner.Encoding;

            public override void Write(char value)
            {
                lock (_sync)
                    WriteCore(value);
            }

            public override void Write(string? value)
            {
                if (value is null)
                    return;
                lock (_sync)
                {
                    foreach (var character in value)
                        WriteCore(character);
                }
            }

            public override void WriteLine()
            {
                lock (_sync)
                {
                    foreach (var character in NewLine)
                        WriteCore(character);
                    inner.Flush();
                }
            }

            public override void WriteLine(string? value)
            {
                lock (_sync)
                {
                    if (value is not null)
                    {
                        foreach (var character in value)
                            WriteCore(character);
                    }
                    foreach (var character in NewLine)
                        WriteCore(character);
                    inner.Flush();
                }
            }

            public override void Flush()
            {
                lock (_sync)
                    inner.Flush();
            }

            private void WriteCore(char value)
            {
                if (_atLineStart)
                {
                    inner.Write($"[{DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture)}] ");
                    _atLineStart = false;
                }
                inner.Write(value);
                if (value == '\n')
                    _atLineStart = true;
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

                var updaterPath = Path.Combine(processDirectory,
                    OperatingSystem.IsWindows() ? "XTMF2.RunServer.Updater.exe" : "XTMF2.RunServer.Updater");
                if (!File.Exists(updaterPath))
                    throw new FileNotFoundException("The RunServer updater executable is missing.", updaterPath);

                var updaterInfo = new ProcessStartInfo(updaterPath)
                {
                    UseShellExecute = false,
                    WorkingDirectory = processDirectory
                };
                updaterInfo.ArgumentList.Add("--old-pid");
                updaterInfo.ArgumentList.Add(Environment.ProcessId.ToString());
                updaterInfo.ArgumentList.Add("--install-dir");
                updaterInfo.ArgumentList.Add(processDirectory);
                updaterInfo.ArgumentList.Add("--server-path");
                updaterInfo.ArgumentList.Add(processPath);
                updaterInfo.ArgumentList.Add("--server-assembly");
                updaterInfo.ArgumentList.Add(typeof(Program).Assembly.Location);
                updaterInfo.ArgumentList.Add("--deployment-dir");
                updaterInfo.ArgumentList.Add(deploymentDirectory);
                updaterInfo.ArgumentList.Add("--staging-root");
                updaterInfo.ArgumentList.Add(stagingRoot);
                foreach (var argument in _originalArguments)
                {
                    updaterInfo.ArgumentList.Add("--server-arg");
                    updaterInfo.ArgumentList.Add(argument);
                }
                if (System.Diagnostics.Process.Start(updaterInfo) is null)
                    throw new InvalidOperationException("Unable to start the RunServer updater.");
                Environment.Exit(0);
                return true;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or
                InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
            {
                Console.Error.WriteLine($"Unable to restart RunServer: {exception.Message}");
                if (deploymentDirectory is not null)
                {
                    try
                    {
                        Directory.Delete(deploymentDirectory, recursive: true);
                    }
                    catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException)
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
            if (!string.IsNullOrWhiteSpace(readyFile))
            {
                try
                {
                    File.WriteAllText(readyFile, "ready");
                }
                catch (IOException exception)
                {
                    Console.Error.WriteLine($"Unable to signal RunServer readiness: {exception.Message}");
                }
            }
            Environment.SetEnvironmentVariable("XTMF2_DEPLOYMENT_READY_FILE", null);
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