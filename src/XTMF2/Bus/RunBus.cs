/*
    Copyright 2020 Travel Modelling Group, Department of Civil Engineering, University of Toronto

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
    /// This bus provides the interconnect between the Client and the Run for XTMF2.
    /// Host <-> Client <-> Run
    /// </summary>
    public sealed class RunBus : IDisposable
    {
        private readonly Stream _toClient;
        private readonly bool _streamOwner;
        private XTMFRuntime _runtime;
        private volatile bool _Exit = false;
        private string _id = string.Empty;

        public RunBus(Stream toClient, bool streamOwner, XTMFRuntime runtime)
        {
            _toClient = toClient;
            _streamOwner = streamOwner;
            _runtime = runtime;
            runtime.RunBus = this;
        }

        private enum In
        {
            Heartbeat = 0,
            RunModelSystem = 1,
            CancelModelRun = 2,
            KillModelRun = 3,
            KillRun = 4
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
                using var writer = new BinaryWriter(_toClient, Encoding.UTF8, true);
                writeWith(writer);
            }
        }



        /// <summary>
        /// Signal to the host that the run failed in the validation step.
        /// </summary>
        /// <param name="error">The error message.</param>
        internal void ModelRunFailedValidation(string error)
        {
            Write((writer) =>
            {
                writer.Write((int)Out.ClientErrorValidatingModelSystem);
                writer.Write(_id);
                writer.Write(error ?? "No error message!");
            });
        }

        /// <summary>
        /// Signal to the host that the run failed during runtime.
        /// </summary>
        /// <param name="message">The message containing the error.</param>
        /// <param name="stackTrace">The stack trace from the time of the error.</param>
        internal void ModelRunFailed(string message, string stackTrace)
        {
            Write((writer) =>
            {
                writer.Write((int)(Out.ClientErrorWhenRunningModelSystem));
                writer.Write(_id);
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
                writer.Write(_id);
                writer.Write(message ?? String.Empty);
            });
        }

        /// <summary>
        /// Signal to the host that the run has completed.
        /// </summary>
        internal void ModelRunComplete()
        {
            Write((writer) =>
            {
                writer.Write((int)Out.ClientFinishedModelSystem);
                writer.Write(_id);
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
            using var reader = new BinaryReader(_toClient, Encoding.UTF8, false);
            while (!_Exit)
            {
                switch ((In)reader.ReadInt32())
                {
                    case In.RunModelSystem:
                        {
                            _id = reader.ReadString();
                            var cwd = reader.ReadString();
                            var start = reader.ReadString();
                            var msSize = (int)reader.ReadInt64();
                            using var mem = CreateMemoryStreamLoadingFrom(reader.BaseStream, msSize);
                            var run = new Run(_id, mem.ToArray(), start, _runtime, cwd);
                            Task.Run(() =>
                            {
                                var error = run.StartRun();
                                switch (error?.Type)
                                {
                                    case RunErrorType.Validation:
                                        ModelRunFailedValidation(error.Message);
                                        break;
                                    case RunErrorType.RuntimeValidation:
                                        ModelRunFailedValidation(error.Message);
                                        break;
                                    case RunErrorType.Runtime:
                                        ModelRunFailed(error.Message, error.StackTrace);
                                        break;
                                    default:
                                        ModelRunComplete();
                                        break;
                                }
                                _toClient.Dispose();
                                Environment.Exit(0);
                            });
                        }
                        break;
                    case In.KillRun:
                        _Exit = true;
                        Environment.Exit(0);
                        break;
                    // failsafe
                    default:
                        return;
                }
                Interlocked.MemoryBarrier();
            }
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    if (_streamOwner)
                    {
                        _toClient.Dispose();
                    }
                }
                disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            Dispose(true);
        }
        #endregion
    }
}
