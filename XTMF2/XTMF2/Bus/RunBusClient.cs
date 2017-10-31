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

        /// <summary>
        /// The link to the XTMFRuntime
        /// </summary>
        public XTMFRuntime Runtime { get; private set; }

        /// <summary>
        /// Create the bus to interact with the host.
        /// </summary>
        /// <param name="serverStream">A stream that connects to the host.</param>
        /// <param name="streamOwner">Should this bus assume ownership over the stream?</param>
        /// <param name="runtime">The XTMFRuntime to work within.</param>
        public RunBusClient(Stream serverStream, bool streamOwner, XTMFRuntime runtime)
        {
            Runtime = runtime;
            Runs = new Scheduler(this);
            ClientHost = serverStream;
            Owner = streamOwner;
            Runtime.ClientBus = this;
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

        /// <summary>
        /// Shutdown the connection to the host.
        /// </summary>
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
            SendModelSystemResult = 7,
            ClientReportedStatus = 8
        }

        /// <summary>
        /// This must be obtained before sending any data to the host
        /// </summary>
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

        /// <summary>
        /// Signal to the host that the run failed in the validation step.
        /// </summary>
        /// <param name="context">The run that failed.</param>
        /// <param name="error">The error message.</param>
        internal void ModelRunFailedValidation(RunContext context, string error)
        {
            Write((writer) =>
            {
                writer.Write((int)Out.ClientErrorValidatingModelSystem);
                writer.Write(context.ID);
                writer.Write(error ?? "No error message!");
            });
        }

        /// <summary>
        /// Signal to the host that the run failed during runtime.
        /// </summary>
        /// <param name="context">The run that failed.</param>
        /// <param name="message">The message containing the error.</param>
        /// <param name="stackTrace">The stack trace from the time of the error.</param>
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

        /// <summary>
        /// Report to the host the current status message.
        /// </summary>
        /// <param name="message">The current status message.</param>
        internal void SendStatusMessage(string message)
        {
            Write((writer) =>
            {
                writer.Write((int)(Out.ClientReportedStatus));
                writer.Write(Runs.Current.ID);
                writer.Write(message ?? String.Empty);
            });
        }

        /// <summary>
        /// Signal to the host that the run has completed.
        /// </summary>
        /// <param name="context">The run that has completed.</param>
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
                                    if (RunContext.CreateRunContext(Runtime, id, mem.ToArray(), cwd, start, out var context))
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
