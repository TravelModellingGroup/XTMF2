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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using XTMF2.Bus;
using XTMF2.Configuration;

namespace XTMF2.Client
{
    public class Program
    {
        [MTAThread]
        static void Main(string[] args)
        {
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
            using (tcpListener)
            using (var shutdown = new CancellationTokenSource())
            using (var remoteEstimationRegistry = new RemoteSharedEstimationRegistry())
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
                        ? Task.Run(() => RunTcpClientUnsecured(acceptedClient, extraDlls, remoteEstimationRegistry))
                        : Task.Run(() => RunTcpClient(acceptedClient, certificate!, token!, extraDlls, remoteEstimationRegistry));
                }
            }
        }

        private static void RunTcpClientUnsecured(TcpClient client, List<string> extraDlls,
            RemoteSharedEstimationRegistry remoteEstimationRegistry)
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
                        remoteEstimationRegistry: remoteEstimationRegistry);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Local RunServer client session failed for {remoteEndpoint}: {ex.Message}");
                    Console.Out.Flush();
                }
            }
        }

        private static void RunTcpClient(TcpClient client, X509Certificate2 certificate, string token, List<string> extraDlls,
            RemoteSharedEstimationRegistry remoteEstimationRegistry)
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
                            remoteEstimationRegistry: remoteEstimationRegistry);
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
            bool usePrivateWorkspace = false, RemoteSharedEstimationRegistry? remoteEstimationRegistry = null)
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
            using var clientBus = new RunServerBus(serverStream, true, runtime, extraDlls, System.Diagnostics.Debugger.IsAttached, usePrivateWorkspace);
            using var sharedEstimationWorker = clientBus.AttachSharedEstimationWorker();
            using var remoteCoordinator = new RemoteSharedEstimationCoordinatorSession(clientBus, registry);
            clientBus.ProcessRequests();
        }
    }
}