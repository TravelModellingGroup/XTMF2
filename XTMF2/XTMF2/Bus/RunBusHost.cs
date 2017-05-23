/*
    Copyright 2017 Travel Modelling Group, Department of Civil Engineering, University of Toronto

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
    along with XTMF.  If not, see <http://www.gnu.org/licenses/>.
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XTMF2.Editing;

namespace XTMF2.Bus
{
    /// <summary>
    /// Provides communication with the run process
    /// </summary>
    public sealed class RunBusHost : IDisposable
    {
        private Stream HostStream;
        private bool Owner;
        private volatile bool Exit = false;
        private volatile bool Exited = false;

        public RunBusHost(Stream hostStream, bool streamOwner)
        {
            Owner = streamOwner;
            HostStream = hostStream ?? throw new ArgumentNullException(nameof(hostStream));
            StartListenner();
        }

        ~RunBusHost()
        {
            Dispose(false);
        }

        private void Dispose(bool managed)
        {
            if(managed)
            {
                GC.SuppressFinalize(this);
            }
            Exit = true;
            while(!Exited)
            {
                Interlocked.MemoryBarrier();
                if(!Exited)
                {
                    Task.WaitAll(Task.Delay(50));
                }
                Interlocked.MemoryBarrier();
            }
            if (Owner)
            {
                HostStream.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        private enum In
        {
            Heartbeat = 0,
            ClientReady = 1,
            ClientExiting = 2,
            ClientFinishedModelSystem = 3,
            ClientErrorWhenRunningModelSystem = 4,
            ProgressUpdate = 5,
            SendModelSystemResult = 6
        }

        public event EventHandler ClientFinishedModelSystem;

        public delegate void RunError(object sender, string errorMessage, string stack);

        public event RunError ClientErrorWhenRunningModelSystem;

        public void StartListenner()
        {
            Task.Factory.StartNew((token) =>
            {
                try
                {
                    BinaryReader reader = new BinaryReader(HostStream, Encoding.Unicode, true);
                    while (!Exit)
                    {
                        try
                        {
                            var command = (In)reader.ReadInt32();
                            switch (command)
                            {
                                case In.Heartbeat:
                                    // do nothing
                                    break;
                                case In.ClientReady:
                                    break;
                                case In.ClientExiting:
                                    Exit = true;
                                    break;
                                case In.ClientFinishedModelSystem:
                                    try
                                    {
                                        ClientFinishedModelSystem?.Invoke(this, new EventArgs());
                                    }
                                    catch
                                    {

                                    }
                                    break;
                                case In.ClientErrorWhenRunningModelSystem:
                                    try
                                    {
                                        ClientErrorWhenRunningModelSystem?.Invoke(this, reader.ReadString(), reader.ReadString());
                                    }
                                    catch
                                    {

                                    }
                                    break;
                                default:
                                    throw new Exception($"Unsupported command: {Enum.GetName(typeof(In), command)}");
                            }
                        }
                        catch (TimeoutException)
                        {
                        }
                        System.Threading.Interlocked.MemoryBarrier();
                    }
                }
                catch (IOException)
                {
                }
                finally
                {
                    Exited = true;
                }
            }, TaskCreationOptions.LongRunning);
        }

        private enum Out
        {
            Heartbeat = 0,
            RunModelSystem = 1,
            CancelModelRun = 2,
            KillModelRun = 3
        }

        private object OutLock = new object();

        private void SendHeartbeat()
        {
            lock (OutLock)
            {
                var writer = new BinaryWriter(HostStream, Encoding.Unicode, true);
                writer.Write((int)Out.Heartbeat);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="modelSystem">The model system to execute</param>
        /// <param name="cwd">The directory to run in.</param>
        /// <param name="error">An error message if there is an issue creating the model system.</param>
        /// <returns>True if the model system was sent</returns>
        public bool RunModelSystem(ModelSystemSession modelSystem, string cwd, ref string error)
        {
            lock (OutLock)
            {
                using (var memStream = new MemoryStream())
                {
                    BinaryWriter write = new BinaryWriter(memStream, Encoding.Unicode, true);
                    if (!modelSystem.Save(ref error, memStream))
                    {
                        return false;
                    }
                    // int64
                    var writer = new BinaryWriter(HostStream, Encoding.Unicode, true);
                    writer.Write((int)Out.RunModelSystem);
                    writer.Write(cwd);
                    writer.Write(memStream.Length);
                    memStream.WriteTo(HostStream);
                    return true;
                } 
            }
        }
    }
}
