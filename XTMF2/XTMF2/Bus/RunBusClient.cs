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

namespace XTMF2.Bus
{
    /// <summary>
    /// Provides communication to the XTMF Interface
    /// </summary>
    public sealed class RunBusClient : IDisposable
    {
        private Stream ClientHost;
        private bool Owner;
        private volatile bool Exit = false;

        private Scheduler Runs;

        public RunBusClient(Stream serverStream, bool owner)
        {
            Runs = new Scheduler(this);
            ClientHost = serverStream;
            Owner = owner;
        }

        private void Dispose(bool managed)
        {
            if (managed)
            {
                GC.SuppressFinalize(this);
                Runs.Dispose();
            }
            if (Owner)
            {
                ClientHost.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        ~RunBusClient()
        {
            Dispose(false);
        }

        private enum In
        {
            Heartbeat = 0,
            RunModelSystem = 1,
            CancelModelRun = 2,
            KillModelRun = 3
        }

        private enum Out
        {
            Heartbeat = 0,
            ClientReady = 1,
            ClientExiting = 2,
            ClientFinishedModelSystem = 3,
            ClientErrorWhenRunningModelSystem = 4,
            ClientErrorValidatingModelSystem = 5,
            ProgressUpdate = 6,
            SendModelSystemResult = 7
        }

        private object WriteLock = new object();

        private void Write(Action<BinaryWriter> writeWith)
        {
            lock (WriteLock)
            {
                using (var writer = new BinaryWriter(ClientHost, Encoding.Unicode, true))
                {
                    writeWith(writer);
                }
            }
        }

        internal void ModelRunFailedValidation(RunContext context, string error)
        {
            Write((writer) =>
            {
                writer.Write((int)Out.ClientErrorValidatingModelSystem);
                writer.Write(context.ID);
                writer.Write(error ?? "No error message!");
            });
        }

        internal void ModelRunFailed(RunContext context, string message, string stackTrace)
        {
            Write((writer) =>
            {
                writer.Write((int)(Out.ClientErrorWhenRunningModelSystem));
                writer.Write(context.ID);
                writer.Write(message ?? String.Empty);
                writer.Write(stackTrace ?? String.Empty);
            });
        }

        internal void ModelRunComplete(RunContext context)
        {
            Write((writer) =>
            {
                writer.Write((int)Out.ClientFinishedModelSystem);
            });
        }

        private static MemoryStream CreateMemoryStreamLoadingFrom(Stream source, int bytes)
        {
            // Read things in parts in case the whole dataset is not ready before we start reading.
            var backend = new byte[bytes];
            int offset = 0;
            while (offset < bytes)
            {
                offset += source.Read(backend, offset, bytes - offset);
            }
            return new MemoryStream(backend);
        }

        /// <summary>
        /// Consumes the current thread to answer requests from the host
        /// </summary>
        public void ProcessRequests()
        {
            try
            {
                // the writer will clear things up
                BinaryReader reader = new BinaryReader(ClientHost, Encoding.Unicode, false);
                while (!Exit)
                {
                    switch ((In)reader.ReadInt32())
                    {
                        case In.RunModelSystem:
                            {
                                var id = reader.ReadString();
                                var cwd = reader.ReadString();
                                var start = reader.ReadString();
                                var msSize = (int)reader.ReadInt64();
                                using (var mem = CreateMemoryStreamLoadingFrom(reader.BaseStream, msSize))
                                {
                                    if (RunContext.CreateRunContext(id, mem.ToArray(), cwd, start, out var context))
                                    {
                                        Runs.Run(context);
                                    }
                                }
                            }
                            break;
                        // failsafe
                        default:
                            return;
                    }
                    Interlocked.MemoryBarrier();
                }
            }
            catch (IOException)
            {

            }
        }
    }
}
