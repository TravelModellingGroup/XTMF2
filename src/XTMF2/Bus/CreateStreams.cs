/*
    Copyright 2017-2026 University of Toronto

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
using System.Text;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;

namespace XTMF2.Bus
{
    public static class CreateStreams
    {
        /// <summary>
        /// Creates a TCP listener, invokes <paramref name="createClient"/> with the bound
        /// port, and waits for one client connection. A port of zero requests an ephemeral
        /// port from the operating system.
        /// </summary>
        public static bool CreateNewTcpHost(
            string address,
            int port,
            [NotNullWhen(true)] out Stream? stream,
            out int boundPort,
            [NotNullWhen(false)] out string? error,
            Action<int> createClient,
            int timeoutMilliseconds = 5000)
        {
            stream = null;
            boundPort = 0;
            error = null;

            if (!IPAddress.TryParse(address, out var ipAddress))
            {
                error = $"The TCP host address '{address}' is not a valid IP address.";
                return false;
            }

            if (port is < 0 or > IPEndPoint.MaxPort)
            {
                error = $"The TCP port '{port}' is outside the valid range.";
                return false;
            }

            try
            {
                var listener = new TcpListener(ipAddress, port);
                listener.Start();
                boundPort = ((IPEndPoint)listener.LocalEndpoint).Port;

                try
                {
                    createClient(boundPort);
                    var acceptTask = listener.AcceptTcpClientAsync();
                    if (!acceptTask.Wait(timeoutMilliseconds) || !acceptTask.IsCompletedSuccessfully)
                    {
                        error = "No TCP client connection was received before the timeout.";
                        return false;
                    }

                    stream = acceptTask.Result.GetStream();
                    return true;
                }
                finally
                {
                    listener.Stop();
                }
            }
            catch (Exception ex) when (ex is SocketException or IOException or InvalidOperationException)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Connects to a TCP RunServer endpoint.
        /// </summary>
        public static bool CreateTcpClient(
            string address,
            int port,
            [NotNullWhen(true)] out Stream? stream,
            [NotNullWhen(false)] out string? error,
            int timeoutMilliseconds = 5000)
        {
            stream = null;
            error = null;

            if (string.IsNullOrWhiteSpace(address))
            {
                error = "The TCP host address is required.";
                return false;
            }

            if (port is < 1 or > IPEndPoint.MaxPort)
            {
                error = $"The TCP port '{port}' is outside the valid range.";
                return false;
            }

            var client = new TcpClient();
            try
            {
                var connectTask = client.ConnectAsync(address, port);
                if (!connectTask.Wait(timeoutMilliseconds) || !connectTask.IsCompletedSuccessfully)
                {
                    error = $"Unable to connect to TCP RunServer {address}:{port}.";
                    return false;
                }

                stream = client.GetStream();
                return true;
            }
            catch (Exception ex) when (ex is SocketException or IOException or InvalidOperationException)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (stream is null)
                {
                    client.Dispose();
                }
            }
        }

        /// <summary>
        /// Create a new named pipe host
        /// </summary>
        /// <param name="name">The name for the pipe</param>
        /// <param name="stream">The resulting stream</param>
        /// <param name="error">An error message if there is an exception</param>
        /// <returns>True if successful, false with message otherwise</returns>
        public static bool CreateNewNamedPipeHost(string name,
            [NotNullWhen(true)] out Stream? stream,
            [NotNullWhen(false)] out string? error,
            Action createClient)
        {
            try
            {
                NamedPipeServerStream host = new(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                stream = host;
                var waitTask = host.WaitForConnectionAsync();
                createClient();
                waitTask.Wait(5000);
                if (!host.IsConnected)
                {
                    // failed to get a connection back from the client
                    host.Dispose();
                    stream = null;
                    error = "No connection from the run client received!";
                    return false;
                }
                error = null;
                return true;
            }
            catch (IOException e)
            {
                error = e.Message;
                stream = null;
                return false;
            }
        }

        /// <summary>
        /// Create a client for a named pipe.
        /// </summary>
        /// <param name="name">The name of the pipe to connect to.</param>
        /// <param name="stream">The resulting stream</param>
        /// <param name="error">An error message if there is a problem</param>
        /// <returns>True if successful, false with message otherwise</returns>
        public static bool CreateNamedPipeClient(string name,
            [NotNullWhen(true)] out Stream? stream,
            [NotNullWhen(false)] out string? error)
        {
            try
            {
                // Connect to the named pipe server
                var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
                try
                {
                    client.Connect(1000);
                }
                catch (TimeoutException)
                {
                    stream = null;
                    error = $"Unable to create connection to the host with the named pipe {name}";
                    return false;
                }
                stream = client;
                error = null;
                return true;
            }
            catch (IOException e)
            {
                error = e.Message;
            }
            stream = null;
            return false;
        }

        public static bool CreateDebugBusses(XTMFRuntime runtime, 
            [NotNullWhen(true)] out HostBus hostBus, 
            [NotNullWhen(true)] out RunServerBus runServerBus,
            [NotNullWhen(false)] out string? error)
        {
            Stream? clientStream = null;
            string? clientError = null;
            if (!CreateNewTcpHost("127.0.0.1", 0, out var hostStream, out _, out error, port =>
            {
                if (!CreateTcpClient("127.0.0.1", port, out clientStream, out clientError))
                {
                    return;
                }
            }))
            {
                hostBus = null!;
                runServerBus = null!;
                return false;
            }
            hostBus = new HostBus(hostStream!, true);
            runServerBus = new RunServerBus(clientStream!, true, runtime, null, true);
            return true;
        }
    }
}
