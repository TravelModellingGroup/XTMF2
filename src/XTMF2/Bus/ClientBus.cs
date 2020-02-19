/*
    Copyright 2017-2020 Travel Modelling Group, Department of Civil Engineering, University of Toronto

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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace XTMF2.Bus
{
    /// <summary>
    /// Provides communication to the host and forwards communication
    /// to the Run.
    /// </summary>
    public sealed class ClientBus : IDisposable
    {
        private readonly Stream _clientHost;
        private readonly bool _owner;
        private volatile bool _exit = false;

        private readonly Scheduler _runScheduler;
        private readonly List<string> _extraDlls;

        /// <summary>
        /// The link to the XTMFRuntime
        /// </summary>
        public XTMFRuntime Runtime { get; private set; }

        /// <summary>
        /// Additional DLLs that the client should load.
        /// </summary>
        public IReadOnlyList<string> ExtraDlls => _extraDlls;

        /// <summary>
        /// Create the bus to interact with the host.
        /// </summary>
        /// <param name="serverStream">A stream that connects to the host.</param>
        /// <param name="streamOwner">Should this bus assume ownership over the stream?</param>
        /// <param name="runtime">The XTMFRuntime to work within.</param>
        public ClientBus(Stream serverStream, bool streamOwner, XTMFRuntime runtime, List<string>? extraDlls = null)
        {
            Runtime = runtime;
            _runScheduler = new Scheduler(this);
            _clientHost = serverStream;
            _owner = streamOwner;
            _extraDlls = extraDlls ?? new List<string>();
        }

        private void Dispose(bool managed)
        {
            if (managed)
            {
                GC.SuppressFinalize(this);
                _runScheduler.Dispose();
            }
            if (_owner)
            {
                _clientHost.Dispose();
            }
        }

        /// <summary>
        /// Shutdown the connection to the host.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        ~ClientBus()
        {
            Dispose(false);
        }

        private enum In
        {
            Heartbeat = 0,
            RunModelSystem = 1,
            CancelModelRun = 2,
            KillModelRun = 3,
            KillClient = 4
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
        private readonly object _writeLock = new object();

        private void Write(Action<BinaryWriter> writeWith)
        {
            lock (_writeLock)
            {
                using var writer = new BinaryWriter(_clientHost, Encoding.UTF8, true);
                writeWith(writer);
            }
        }

        internal void StartProcessingRequestFromRun(string id, Stream clientToRunStream)
        {
            Task.Factory.StartNew(() =>
            {
                var reader = new BinaryReader(clientToRunStream, Encoding.UTF8, true);
                try
                {
                    while (true)
                    {
                        switch((Out)reader.ReadInt32())
                        {
                            case Out.Heartbeat:
                                WriteHeartbeat(reader.ReadString());
                                break;
                            case Out.ClientErrorValidatingModelSystem:
                                ModelRunFailedValidation(reader.ReadString(), reader.ReadString());
                                return;
                            case Out.ClientErrorWhenRunningModelSystem:
                                ModelRunFailed(reader.ReadString(), reader.ReadString(), reader.ReadString());
                                return;
                            case Out.ClientReportedStatus:
                                SendStatusMessage(reader.ReadString(), reader.ReadString());
                                break;
                            case Out.ClientFinishedModelSystem:
                                ModelRunComplete(reader.ReadString());
                                return;
                            case Out.ProgressUpdate:
                                SendProgressUpdate(reader.ReadString(), reader.ReadSingle());
                                return;
                            default:
                                return;
                        }
                    }
                }
                catch
                {

                }
            }, TaskCreationOptions.LongRunning);
        }

        private void SendProgressUpdate(string runId, float progress)
        {
            Write(writer =>
            {
                writer.Write((int)Out.ProgressUpdate);
                writer.Write(progress);
            });
        }

        /// <summary>
        /// Signal to the host the run is still alive.
        /// </summary>
        /// <param name="runId">The ID of the run that is reporting that it still exists.</param>
        internal void WriteHeartbeat(string runId)
        {
            Write(writer =>
            {
                writer.Write((int)Out.Heartbeat);
                writer.Write(runId);
            });
        }

        /// <summary>
        /// Signal to the host that the run failed in the validation step.
        /// </summary>
        /// <param name="context">The run that failed.</param>
        /// <param name="error">The error message.</param>
        internal void ModelRunFailedValidation(string runId, string error)
        {
            Write((writer) =>
            {
                writer.Write((int)Out.ClientErrorValidatingModelSystem);
                writer.Write(runId);
                writer.Write(error ?? "No error message!");
            });
        }

        /// <summary>
        /// Signal to the host that the run failed during runtime.
        /// </summary>
        /// <param name="context">The run that failed.</param>
        /// <param name="message">The message containing the error.</param>
        /// <param name="stackTrace">The stack trace from the time of the error.</param>
        internal void ModelRunFailed(string runId, string? message, string? stackTrace)
        {
            Write((writer) =>
            {
                writer.Write((int)(Out.ClientErrorWhenRunningModelSystem));
                writer.Write(runId);
                writer.Write(message ?? String.Empty);
                writer.Write(stackTrace ?? String.Empty);
            });
        }

        /// <summary>
        /// Report to the host the current status message.
        /// </summary>
        /// <param name="message">The current status message.</param>
        internal void SendStatusMessage(string runId, string? message)
        {
            Write((writer) =>
            {
                writer.Write((int)(Out.ClientReportedStatus));
                writer.Write(runId);
                writer.Write(message ?? String.Empty);
            });
        }

        /// <summary>
        /// Signal to the host that the run has completed.
        /// </summary>
        /// <param name="context">The run that has completed.</param>
        internal void ModelRunComplete(string runId)
        {
            Write((writer) =>
            {
                writer.Write((int)Out.ClientFinishedModelSystem);
                writer.Write(runId);
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
            // the writer will clear things up
            using var reader = new BinaryReader(_clientHost, Encoding.UTF8, false);
            while (!_exit)
            {
                switch ((In)reader.ReadInt32())
                {
                    case In.RunModelSystem:
                        {
                            var id = reader.ReadString();
                            var cwd = reader.ReadString();
                            var start = reader.ReadString();
                            var msSize = (int)reader.ReadInt64();
                            using var mem = CreateMemoryStreamLoadingFrom(reader.BaseStream, msSize);
                            if (RunContext.CreateRunContext(Runtime, id, mem.ToArray(), cwd, start, out var context))
                            {
                                _runScheduler.Run(context);
                            }
                        }
                        break;
                    case In.KillClient:
                        _exit = true;
                        break;
                    // failsafe
                    default:
                        return;
                }
                Interlocked.MemoryBarrier();
            }
        }
    }
}
