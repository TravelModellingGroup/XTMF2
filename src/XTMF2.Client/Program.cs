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
                Console.WriteLine("Usage: XTMF.Run [-loadDLL dllPath] [-tcp ADDRESS PORT] [-namedPipe PIPE_NAME]");
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
                        RunTcpServer(tcpAddress, tcpPort, dllsToLoad);
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

        private static void RunTcpServer(string address, int port, List<string> extraDlls)
        {
            if (!CreateStreams.CreateTcpListener(address, port, out var listener, out var boundPort, out var error))
            {
                Console.WriteLine("Error creating TCP RunServer listener\r\n" + error);
                return;
            }

            var tcpListener = listener!;
            Console.WriteLine($"RunServer listening on {address}:{boundPort}");
            Console.Out.Flush();
            using (tcpListener)
            using (var shutdown = new CancellationTokenSource())
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

                    if (client is null)
                        continue;

                    var acceptedClient = client;
                    _ = Task.Run(() => RunTcpClient(acceptedClient, extraDlls));
                }
            }
        }

        private static void RunTcpClient(TcpClient client, List<string> extraDlls)
        {
            using (client)
            using (var stream = client.GetStream())
            {
                RunClient(stream, extraDlls, usePrivateWorkspace: true);
            }
        }

        private static void RunClient(Stream serverStream, List<string> extraDlls, SystemConfiguration? config = null, bool usePrivateWorkspace = false)
        {
            var runtime = XTMFRuntime.CreateRuntime(config);
            var loadedConfig = runtime.SystemConfiguration;
            foreach (var dll in extraDlls)
            {
                loadedConfig.LoadAssembly(dll);
            }
            using var clientBus = new RunServerBus(serverStream, true, runtime, extraDlls, System.Diagnostics.Debugger.IsAttached, usePrivateWorkspace);
            clientBus.ProcessRequests();
        }
    }
}