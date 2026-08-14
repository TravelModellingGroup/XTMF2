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
    public sealed class RunServerBus : IDisposable
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
        /// <param name="extraDlls">Additional DLLs that the client should load.</param>
        /// <param name="runLocal">If true, the model system will be run within the same process as the GUI.  This is only intended for debugging purposes.</param>
        public RunServerBus(Stream serverStream, bool streamOwner, XTMFRuntime runtime, List<string>? extraDlls = null, bool runLocal = false)
        {
            Runtime = runtime;
            _runScheduler = new Scheduler(this, runLocal);
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

        ~RunServerBus()
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
            ClientReportedStatus = 8,
            ClientOptimizationResults = 9,
            ClientIterationProgress = 10
        }

        /// <summary>
        /// This must be obtained before sending any data to the host
        /// </summary>
        private readonly Lock _writeLock = new();

        private void Write(Action<BinaryWriter> writeWith)
        {
            lock (_writeLock)
            {
                using var writer = new BinaryWriter(_clientHost, Encoding.UTF8, true);
                writeWith(writer);
            }
        }

        internal Task StartProcessingRequestFromRun(string id, Stream clientToRunStream)
        {
            return Task.Factory.StartNew(() =>
            {
                // leaveOpen=false so the stream is disposed when the reader exits.
                using var reader = new BinaryReader(clientToRunStream, Encoding.UTF8, false);
                try
                {
                    while (true)
                    {
                        switch ((Out)reader.ReadInt32())
                        {
                            case Out.Heartbeat:
                                WriteHeartbeat(reader.ReadString());
                                break;
                            case Out.ClientErrorValidatingModelSystem:
                            {
                                var runId = reader.ReadString();
                                var error = reader.ReadString();
                                var moduleName = reader.ReadString();
                                var elementId = reader.ReadString();
                                ModelRunFailedValidation(runId,error, moduleName, elementId);
                                return;
                            }
                            case Out.ClientErrorWhenRunningModelSystem:
                                ModelRunFailed(
                                    reader.ReadString(),
                                    reader.ReadString(),
                                    reader.ReadString(),
                                    reader.ReadString(),
                                    reader.ReadString());
                                return;
                            case Out.ClientReportedStatus:
                                SendStatusMessage(reader.ReadString(), reader.ReadString());
                                break;
                            case Out.ClientFinishedModelSystem:
                                ModelRunComplete(reader.ReadString());
                                return;
                            case Out.ProgressUpdate:
                                SendProgressUpdate(reader.ReadString(), reader.ReadSingle());
                                break;
                            case Out.ClientOptimizationResults:
                                {
                                    var runId = reader.ReadString();
                                    int count = reader.ReadInt32();
                                    var results = new List<(int nodeIndex, double value)>(count);
                                    for (int i = 0; i < count; i++)
                                        results.Add((reader.ReadInt32(), reader.ReadDouble()));
                                    SendOptimizationResults(runId, results);
                                }
                                break;
                            case Out.ClientIterationProgress:
                                {
                                    var runId = reader.ReadString();
                                    int iteration = reader.ReadInt32();
                                    double fitness = reader.ReadDouble();
                                    int count = reader.ReadInt32();
                                    var values = new (int nodeIndex, double value)[count];
                                    for (int i = 0; i < count; i++)
                                        values[i] = (reader.ReadInt32(), reader.ReadDouble());
                                    SendIterationProgress(runId, iteration, fitness, values);
                                }
                                break;
                            default:
                                Console.WriteLine("Unknown message from run!");
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
        internal void ModelRunFailedValidation(string runId, string? error, string? moduleName = null, string? elementId = null)
        {
            Write((writer) =>
            {
                writer.Write((int)Out.ClientErrorValidatingModelSystem);
                writer.Write(runId);
                writer.Write(error ?? "No error message!");
                writer.Write(moduleName ?? String.Empty);
                writer.Write(elementId ?? String.Empty);
            });
        }

        /// <summary>
        /// Signal to the host that the run failed during runtime.
        /// </summary>
        /// <param name="context">The run that failed.</param>
        /// <param name="message">The message containing the error.</param>
        /// <param name="stackTrace">The stack trace from the time of the error.</param>
        /// <param name="moduleName">The resolved module name, when available.</param>
        /// <param name="elementId">The resolved model element ID, when available.</param>
        internal void ModelRunFailed(string runId, string? message, string? stackTrace, string? moduleName = null, string? elementId = null)
        {
            Write((writer) =>
            {
                writer.Write((int)(Out.ClientErrorWhenRunningModelSystem));
                writer.Write(runId);
                writer.Write(message ?? String.Empty);
                writer.Write(stackTrace ?? String.Empty);
                writer.Write(moduleName ?? String.Empty);
                writer.Write(elementId ?? String.Empty);
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
        /// Sends the final optimised parameter values to the host so the user can
        /// choose whether to apply them back to the model system.
        /// </summary>
        internal void SendOptimizationResults(string runId, IReadOnlyList<(int nodeIndex, double value)> results)
        {
            Write((writer) =>
            {
                writer.Write((int)Out.ClientOptimizationResults);
                writer.Write(runId);
                writer.Write(results.Count);
                foreach (var (idx, val) in results)
                {
                    writer.Write(idx);
                    writer.Write(val);
                }
            });
        }

        /// <summary>
        /// Sends the per-iteration parameter snapshot to the host for live display.
        /// </summary>
        internal void SendIterationProgress(string runId, int iteration, double fitness,
            IReadOnlyList<(int nodeIndex, double value)> values)
        {
            Write((writer) =>
            {
                writer.Write((int)Out.ClientIterationProgress);
                writer.Write(runId);
                writer.Write(iteration);
                writer.Write(fitness);
                writer.Write(values.Count);
                foreach (var (idx, val) in values)
                {
                    writer.Write(idx);
                    writer.Write(val);
                }
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
                try
                {
                    switch ((In)reader.ReadInt32())
                    {
                        case In.RunModelSystem:
                            {
                                var id = reader.ReadString();
                                var cwd = reader.ReadString();
                                var start = reader.ReadString();
                                var runMode = (RunMode)reader.ReadInt32();
                                var msSize = (int)reader.ReadInt64();
                                using var mem = CreateMemoryStreamLoadingFrom(reader.BaseStream, msSize);
                                if (RunContext.CreateRunContext(Runtime, id, mem.ToArray(), cwd, start, runMode, out var context))
                                {
                                    _runScheduler.Run(context);
                                }
                            }
                            break;
                        case In.KillClient:
                            _exit = true;
                            break;
                        case In.CancelModelRun:
                            {
                                var runId = reader.ReadString();
                                _runScheduler.RequestCancel(runId);
                            }
                            break;
                        // failsafe
                        default:
                            return;
                    }
                    Interlocked.MemoryBarrier();
                }
                catch
                {
                    // if anything goes wrong, just exit the loop and end the process.
                    return;
                }
            }
        }
    }
}
