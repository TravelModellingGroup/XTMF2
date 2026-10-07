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
using System.Linq;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XTMF2.Bus.Optimization;

namespace XTMF2.Bus
{
    /// <summary>
    /// Provides communication to the host and forwards communication
    /// to the Run.
    /// </summary>
    public sealed class RunServerBus : IDisposable, IRunOutputSink
    {
        private readonly Stream _clientHost;
        private readonly bool _owner;
        private volatile bool _exit = false;

        private readonly Scheduler? _runScheduler;
        private readonly RemoteRunRegistry? _remoteRunRegistry;
        private Func<IReadOnlyList<RunServerActivity>>? _coordinatorActivityProvider;
        private Func<IReadOnlyList<RunServerActivity>>? _workerActivityProvider;
        private Action? _beginDeploymentDrain;
        private Action? _cancelDeploymentDrain;
        private Func<bool>? _isDeploymentIdle;
        private Func<string, bool>? _activateDeployment;
        private readonly object _deploymentSync = new();
        private readonly HashSet<string> _deploymentRequests = new(StringComparer.Ordinal);
        private readonly List<string> _extraDlls;
        private readonly bool _usePrivateWorkspace;
        private readonly bool _allowDeployment;
        private string? _stagedDeploymentRoot;
        private string? _stagedDeploymentRequestId;
        private bool _deploymentIdle;

        /// <summary>
        /// The link to the XTMFRuntime
        /// </summary>
        public XTMFRuntime Runtime { get; private set; }

        /// <summary>
        /// Additional DLLs that the client should load.
        /// </summary>
        public IReadOnlyList<string> ExtraDlls => _extraDlls;

        IReadOnlyList<string> IRunOutputSink.ExtraDlls => _extraDlls;

        Task IRunOutputSink.StartProcessingRequestFromRun(string id, Stream clientToRunStream)
            => StartProcessingRequestFromRun(id, clientToRunStream);

        void IRunOutputSink.ModelRunFailedValidation(string runId, string? error, string? moduleName, string? elementId)
            => ModelRunFailedValidation(runId, error, moduleName, elementId);

        void IRunOutputSink.ModelRunFailed(string runId, string? message, string? stackTrace, string? moduleName, string? elementId)
            => ModelRunFailed(runId, message, stackTrace, moduleName, elementId);

        void IRunOutputSink.SendStatusMessage(string runId, string? message)
            => SendStatusMessage(runId, message);

        void IRunOutputSink.SendOptimizationResults(string runId, IReadOnlyList<(int nodeIndex, double value)> results)
            => SendOptimizationResults(runId, results);

        void IRunOutputSink.SendIterationProgress(string runId, int iteration, double fitness,
            int fitnessTestsThisIteration, IReadOnlyList<(int nodeIndex, double value)> values)
            => SendIterationProgress(runId, iteration, fitness, fitnessTestsThisIteration, values);

        void IRunOutputSink.ModelRunComplete(string runId)
            => ModelRunComplete(runId);

        void IRunOutputSink.SendRunArtifacts(string runId, string runDirectory)
            => SendRunArtifacts(runId, runDirectory);

        /// <summary>
        /// Create the bus to interact with the host.
        /// </summary>
        /// <param name="serverStream">A stream that connects to the host.</param>
        /// <param name="streamOwner">Should this bus assume ownership over the stream?</param>
        /// <param name="runtime">The XTMFRuntime to work within.</param>
        /// <param name="extraDlls">Additional DLLs that the client should load.</param>
        /// <param name="runLocal">If true, the model system will be run within the same process as the GUI.  This is only intended for debugging purposes.</param>
        public RunServerBus(Stream serverStream, bool streamOwner, XTMFRuntime runtime, List<string>? extraDlls = null,
            bool runLocal = false, bool usePrivateWorkspace = false, RemoteRunRegistry? remoteRunRegistry = null,
            bool allowDeployment = false)
        {
            Runtime = runtime;
            // Standalone debug/test buses own their scheduler. Production RunClient sessions
            // receive a process-owned registry so independent host connections share one queue.
            _remoteRunRegistry = remoteRunRegistry;
            _runScheduler = remoteRunRegistry is null ? new Scheduler(this, runLocal) : null;
            _clientHost = serverStream;
            _owner = streamOwner;
            _extraDlls = extraDlls ?? new List<string>();
            _usePrivateWorkspace = usePrivateWorkspace;
            _allowDeployment = allowDeployment;
        }

        private void Dispose(bool managed)
        {
            if (managed)
            {
                GC.SuppressFinalize(this);
                _runScheduler?.Dispose();
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
            KillClient = 4,
            SharedEstimationMessage = 5,
            DeploymentMessage = 6
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
            ClientIterationProgress = 10,
            ClientRunArtifacts = 11,
            SharedEstimationMessage = 12,
            DeploymentMessage = 13
        }

        public event Action<object, SharedEstimationRunRequest>? SharedEstimationRunRequested;

        public event Action<object, SharedEstimationCoordinatorRequest>? SharedEstimationCoordinatorRequested;

        public event Action<object, SharedEstimationWorkerRegistration, bool>? SharedEstimationWorkerRequested;

        public event Action<object, SharedEstimationCandidate>? SharedEstimationCandidateReceived;

        public event Action<object, string, string?>? SharedEstimationCancellationRequested;

        public event Action<object>? SharedEstimationJobsQueryRequested;

        public event Action<object, string, SharedEstimationWorkerEndpoint, bool>? SharedEstimationCoordinatorWorkerRequested;

        /// <summary>
        /// Attach this RunServer as a worker for shared-estimation requests received from its host.
        /// </summary>
        public SharedEstimationWorkerSession AttachSharedEstimationWorker()
            => new(this);

        internal Scheduler.ReservationLease ReserveSharedEstimationWorker(SharedEstimationRunRequest request)
        {
            var reservationId = request.RunId;
            if (!RunContext.CreateRunContext(Runtime, reservationId, request.ModelSystem,
                    request.WorkingDirectory, request.StartToExecute, RunMode.Estimation, out var context))
                throw new InvalidOperationException("Unable to create the shared-estimation worker queue entry.");
            return _remoteRunRegistry is not null
                ? _remoteRunRegistry.ReserveWorkerSlot(context, request.WorkerSlotCount)
                : _runScheduler!.ReserveGroup(request.RunId, context, request.WorkerSlotCount);
        }

        public void SetSharedActivityProviders(
            Func<IReadOnlyList<RunServerActivity>> coordinatorProvider,
            Func<IReadOnlyList<RunServerActivity>> workerProvider)
        {
            _coordinatorActivityProvider = coordinatorProvider ?? throw new ArgumentNullException(nameof(coordinatorProvider));
            _workerActivityProvider = workerProvider ?? throw new ArgumentNullException(nameof(workerProvider));
        }

        public void SetDeploymentGate(Action beginDrain, Action cancelDrain, Func<bool> isIdle,
            Func<string, bool>? activateDeployment = null)
        {
            _beginDeploymentDrain = beginDrain ?? throw new ArgumentNullException(nameof(beginDrain));
            _cancelDeploymentDrain = cancelDrain ?? throw new ArgumentNullException(nameof(cancelDrain));
            _isDeploymentIdle = isIdle ?? throw new ArgumentNullException(nameof(isIdle));
            _activateDeployment = activateDeployment;
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

        internal Task StartProcessingRequestFromRun(string id, Stream clientToRunStream, IRunOutputSink? outputSink = null)
        {
            var output = outputSink ?? this;
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
                                var heartbeatRunId = reader.ReadString();
                                if (outputSink is null)
                                    WriteHeartbeat(heartbeatRunId);
                                break;
                            case Out.ClientErrorValidatingModelSystem:
                            {
                                var runId = reader.ReadString();
                                var error = reader.ReadString();
                                var moduleName = reader.ReadString();
                                var elementId = reader.ReadString();
                                output.ModelRunFailedValidation(runId, error, moduleName, elementId);
                                return;
                            }
                            case Out.ClientErrorWhenRunningModelSystem:
                                output.ModelRunFailed(
                                    reader.ReadString(),
                                    reader.ReadString(),
                                    reader.ReadString(),
                                    reader.ReadString(),
                                    reader.ReadString());
                                return;
                            case Out.ClientReportedStatus:
                                output.SendStatusMessage(reader.ReadString(), reader.ReadString());
                                break;
                            case Out.ClientFinishedModelSystem:
                                output.ModelRunComplete(reader.ReadString());
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
                                    output.SendOptimizationResults(runId, results);
                                }
                                break;
                            case Out.ClientIterationProgress:
                                {
                                    var runId = reader.ReadString();
                                    int iteration = reader.ReadInt32();
                                    double fitness = reader.ReadDouble();
                                    int fitnessTestsThisIteration = reader.ReadInt32();
                                    int count = reader.ReadInt32();
                                    var values = new (int nodeIndex, double value)[count];
                                    for (int i = 0; i < count; i++)
                                        values[i] = (reader.ReadInt32(), reader.ReadDouble());
                                    output.SendIterationProgress(runId, iteration, fitness, fitnessTestsThisIteration, values);
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
            Console.WriteLine($"RunServer -> Host validation error: {runId}. {error ?? "No error message."}");
            Console.Out.Flush();
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
            Console.WriteLine($"RunServer -> Host runtime error: {runId}. {message ?? "No error message."}");
            Console.Out.Flush();
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

        internal void SendSharedEstimationResult(SharedEstimationEvaluationResult result)
            => WriteSharedEstimation(writer => SharedEstimationProtocol.WriteResult(writer, result));

        public void SendSharedEstimationProgress(SharedEstimationProgress progress)
            => WriteSharedEstimation(writer => SharedEstimationProtocol.WriteProgress(writer, progress));

        public void SendSharedEstimationCompletion(SharedEstimationCompletion completion)
            => WriteSharedEstimation(writer => SharedEstimationProtocol.WriteCompletion(writer, completion));

        public void SendSharedEstimationStatus(SharedEstimationStatus status)
            => WriteSharedEstimation(writer => SharedEstimationProtocol.WriteStatus(writer, status));

        internal void SendSharedEstimationWorkerReady(SharedEstimationWorkerReady readiness)
            => WriteSharedEstimation(writer => SharedEstimationProtocol.WriteWorkerReady(writer, readiness));

        public void SendSharedEstimationJobSnapshots(IReadOnlyList<SharedEstimationJobSnapshot> snapshots)
            => WriteSharedEstimation(writer => SharedEstimationProtocol.WriteJobSnapshots(writer, snapshots));

        public void SendServerActivitySnapshots(string requestId, IReadOnlyList<RunServerActivity> activities)
            => WriteSharedEstimation(writer => SharedEstimationProtocol.WriteServerActivitySnapshots(writer, requestId, activities));

        public void SendRemoteRunSnapshots(IReadOnlyList<RemoteRunSnapshot> snapshots)
            => WriteSharedEstimation(writer => SharedEstimationProtocol.WriteRemoteRunSnapshots(writer, snapshots));

        public void SendRemoteRunArtifacts(string runId, Stream? archive, long archiveLength, string? error)
            => WriteSharedEstimation(writer =>
                SharedEstimationProtocol.WriteRemoteRunArtifacts(writer, runId, archive, archiveLength, error));

        public void SendRemoteRunDeletionResponse(RemoteRunDeletionResponse response)
            => WriteSharedEstimation(writer => SharedEstimationProtocol.WriteRemoteRunDeleted(writer, response));

        public void SendSharedEstimationWorkerControlAcknowledgement(
            SharedEstimationWorkerControlAcknowledgement acknowledgement)
            => WriteSharedEstimation(writer => SharedEstimationProtocol.WriteWorkerControlAcknowledgement(writer, acknowledgement));

        private void WriteSharedEstimation(Action<BinaryWriter> writePayload)
        {
            Write(writer =>
            {
                writer.Write((int)Out.SharedEstimationMessage);
                writePayload(writer);
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
            int fitnessTestsThisIteration, IReadOnlyList<(int nodeIndex, double value)> values)
        {
            Write((writer) =>
            {
                writer.Write((int)Out.ClientIterationProgress);
                writer.Write(runId);
                writer.Write(iteration);
                writer.Write(fitness);
                writer.Write(fitnessTestsThisIteration);
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
            Console.WriteLine($"RunServer -> Host completion: {runId}");
            Console.Out.Flush();
            Write((writer) =>
            {
                writer.Write((int)Out.ClientFinishedModelSystem);
                writer.Write(runId);
            });
        }

        internal void SendRunArtifacts(string runId, string runDirectory)
        {
            string? archivePath = null;
            try
            {
                if (!Directory.Exists(runDirectory))
                    return;

                archivePath = Path.Combine(Path.GetTempPath(), $"XTMF2-{Guid.NewGuid():N}.zip");
                ZipFile.CreateFromDirectory(runDirectory, archivePath, CompressionLevel.Fastest, false);
                var archiveInfo = new FileInfo(archivePath);
                Write(writer =>
                {
                    writer.Write((int)Out.ClientRunArtifacts);
                    writer.Write(runId);
                    writer.Write(archiveInfo.Length);
                    using var archive = File.OpenRead(archivePath);
                    archive.CopyTo(writer.BaseStream);
                    writer.BaseStream.Flush();
                });
            }
            finally
            {
                if (archivePath is not null)
                {
                    try { File.Delete(archivePath); } catch { }
                }
            }
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
                                var requestedDirectory = reader.ReadString();
                                var cwd = _usePrivateWorkspace ? CreateRunDirectory(id) : requestedDirectory;
                                var start = reader.ReadString();
                                var runMode = (RunMode)reader.ReadInt32();
                                var runName = reader.ReadString();
                                var projectId = Guid.TryParse(reader.ReadString(), out var parsedProjectId)
                                    ? parsedProjectId
                                    : (Guid?)null;
                                var modelSystemId = Guid.TryParse(reader.ReadString(), out var parsedModelSystemId)
                                    ? parsedModelSystemId
                                    : (Guid?)null;
                                var ownerUserId = Guid.TryParse(reader.ReadString(), out var parsedOwnerUserId)
                                    ? parsedOwnerUserId
                                    : (Guid?)null;
                                var msSize = (int)reader.ReadInt64();
                                Console.WriteLine($"RunServer model system run issued: {id} (start '{start}', mode {runMode}, {msSize} bytes)");
                                Console.Out.Flush();
                                using var mem = CreateMemoryStreamLoadingFrom(reader.BaseStream, msSize);
                                var modelSystem = mem.ToArray();
                                if (RunContext.CreateRunContext(Runtime, id, modelSystem, cwd, start, runMode, out var context))
                                {
                                    if (_remoteRunRegistry is not null)
                                    {
                                        if (!_remoteRunRegistry.Submit(this, context, runName, runMode, cwd, start,
                                            modelSystem, projectId, modelSystemId, ownerUserId))
                                            ModelRunFailed(id, "A run with this ID is already registered.", string.Empty);
                                    }
                                    else
                                    {
                                        _runScheduler!.Run(context);
                                    }
                                }
                                else
                                {
                                    Console.WriteLine($"RunServer could not create model system run context: {id}");
                                    Console.Out.Flush();
                                }
                            }
                            break;
                        case In.KillClient:
                            _exit = true;
                            break;
                        case In.CancelModelRun:
                            {
                                var runId = reader.ReadString();
                                if (_remoteRunRegistry is not null)
                                    _remoteRunRegistry.Cancel(runId);
                                else
                                    _runScheduler!.RequestCancel(runId);
                            }
                            break;
                        case In.KillModelRun:
                            {
                                var runId = reader.ReadString();
                                if (_remoteRunRegistry is not null)
                                    _remoteRunRegistry.Kill(runId);
                                else
                                    _runScheduler!.Kill(runId);
                            }
                            break;
                        case In.SharedEstimationMessage:
                            {
                                var (_, messageType) = SharedEstimationProtocol.ReadHeader(reader);
                                switch (messageType)
                                {
                                    case SharedEstimationMessageType.StartCoordinator:
                                        SharedEstimationCoordinatorRequested?.Invoke(this,
                                            SharedEstimationProtocol.ReadCoordinatorRequestPayload(reader));
                                        break;
                                    case SharedEstimationMessageType.StartRun:
                                        SharedEstimationRunRequested?.Invoke(this,
                                            SharedEstimationProtocol.ReadRunRequestPayload(reader));
                                        break;
                                    case SharedEstimationMessageType.AddWorker:
                                        SharedEstimationWorkerRequested?.Invoke(this,
                                            SharedEstimationProtocol.ReadWorkerRegistrationPayload(reader), false);
                                        break;
                                    case SharedEstimationMessageType.RemoveWorker:
                                        SharedEstimationWorkerRequested?.Invoke(this,
                                            SharedEstimationProtocol.ReadWorkerRegistrationPayload(reader), true);
                                        break;
                                    case SharedEstimationMessageType.EvaluateCandidate:
                                        SharedEstimationCandidateReceived?.Invoke(this,
                                            SharedEstimationProtocol.ReadCandidatePayload(reader));
                                        break;
                                    case SharedEstimationMessageType.Cancel:
                                        var cancellation = SharedEstimationProtocol.ReadCancelPayload(reader);
                                        if (_remoteRunRegistry is not null)
                                            _remoteRunRegistry.Cancel(cancellation.RunId);
                                        else
                                            _runScheduler?.RequestCancel(cancellation.RunId);
                                        SharedEstimationCancellationRequested?.Invoke(this,
                                            cancellation.RunId, cancellation.Reason);
                                        break;
                                    case SharedEstimationMessageType.QueryJobs:
                                        SharedEstimationJobsQueryRequested?.Invoke(this);
                                        break;
                                    case SharedEstimationMessageType.QueryRemoteRuns:
                                        if (_remoteRunRegistry is not null)
                                        {
                                            _remoteRunRegistry.Attach(this);
                                            SendRemoteRunSnapshots(_remoteRunRegistry.GetSnapshots());
                                        }
                                        break;
                                    case SharedEstimationMessageType.QueryServerActivity:
                                        var requestId = SharedEstimationProtocol.ReadQueryServerActivityPayload(reader);
                                        SendServerActivitySnapshots(requestId, GetServerActivity());
                                        break;
                                    case SharedEstimationMessageType.GetRemoteRunArtifacts:
                                        {
                                            var runId = SharedEstimationProtocol.ReadGetRemoteRunArtifactsPayload(reader);
                                            FileStream? archive = null;
                                            var found = _remoteRunRegistry is not null &&
                                                _remoteRunRegistry.TryOpenArtifacts(runId, out archive);
                                            using (archive)
                                            {
                                                SendRemoteRunArtifacts(runId, archive, archive?.Length ?? 0,
                                                    found ? null : "No unacknowledged artifact archive is available for this run.");
                                            }
                                        }
                                        break;
                                    case SharedEstimationMessageType.AcknowledgeRemoteRun:
                                        _remoteRunRegistry?.AcknowledgeReceived(
                                            SharedEstimationProtocol.ReadAcknowledgeRemoteRunPayload(reader));
                                        break;
                                    case SharedEstimationMessageType.DeleteRemoteRun:
                                        {
                                            var request = SharedEstimationProtocol.ReadDeleteRemoteRunPayload(reader);
                                            string? error = null;
                                            var deleted = _remoteRunRegistry is not null &&
                                                _remoteRunRegistry.DeleteRun(request.RunId, out error);
                                            error ??= _remoteRunRegistry is null
                                                ? "Remote run retention is unavailable on this RunServer."
                                                : null;
                                            SendRemoteRunDeletionResponse(new RemoteRunDeletionResponse(
                                                request.RequestId, request.RunId, deleted, error));
                                        }
                                        break;
                                    case SharedEstimationMessageType.AddCoordinatorWorker:
                                        var addWorker = SharedEstimationProtocol.ReadCoordinatorWorkerRequestPayload(reader);
                                        SharedEstimationCoordinatorWorkerRequested?.Invoke(this,
                                            addWorker.RunId, addWorker.Worker, false);
                                        break;
                                    case SharedEstimationMessageType.RemoveCoordinatorWorker:
                                        var removeWorker = SharedEstimationProtocol.ReadCoordinatorWorkerRequestPayload(reader);
                                        SharedEstimationCoordinatorWorkerRequested?.Invoke(this,
                                            removeWorker.RunId, removeWorker.Worker, true);
                                        break;
                                    default:
                                        throw new InvalidDataException(
                                            $"Unexpected shared estimation message to RunServer: {messageType}.");
                                }
                            }
                            break;
                        case In.DeploymentMessage:
                            {
                                if (!_allowDeployment)
                                    throw new SecurityException("Deployment operations require an authenticated connection.");

                                var (_, messageType) = DeploymentProtocol.ReadHeader(reader);
                                switch (messageType)
                                {
                                    case DeploymentMessageType.BeginDrain:
                                        BeginDeploymentDrain(DeploymentProtocol.ReadRequestId(reader));
                                        break;
                                    case DeploymentMessageType.CancelDrain:
                                        CancelDeploymentDrain(DeploymentProtocol.ReadRequestId(reader));
                                        break;
                                    case DeploymentMessageType.DeployArchive:
                                        StageDeploymentArchive(DeploymentProtocol.ReadDeployArchive(reader));
                                        break;
                                    case DeploymentMessageType.ActivateDeployment:
                                        ActivateDeployment(DeploymentProtocol.ReadRequestId(reader));
                                        break;
                                    default:
                                        throw new InvalidDataException($"Unexpected deployment message from host: {messageType}.");
                                }
                            }
                            break;
                        // failsafe
                        default:
                            return;
                    }
                    Interlocked.MemoryBarrier();
                }
                catch (Exception ex)
                {
                    // if anything goes wrong, just exit the loop and end the process.
                    Console.Error.WriteLine($"[RunServerBus] Host connection request loop stopped: {ex}");
                    Console.Error.Flush();
                    return;
                }
            }
        }

        private IReadOnlyList<RunServerActivity> GetServerActivity()
        {
            var activities = new List<RunServerActivity>();
            if (_remoteRunRegistry is not null)
            {
                activities.AddRange(_remoteRunRegistry.GetActiveActivities());
            }
            else if (_runScheduler is not null)
            {
                foreach (var item in _runScheduler.GetInventory())
                    activities.Add(new RunServerActivity(item.Context.ID,
                        item.IsReservation ? "Shared estimation worker" : item.Context.StartToExecute,
                        item.IsReservation ? "Shared estimation worker" : item.Context.Mode.ToString(),
                        item.IsRunning ? RunServerActivityState.Running : RunServerActivityState.Queued,
                        item.IsReservation
                            ? item.IsRunning ? "Worker has the RunServer execution slot." : "Waiting in the RunServer queue."
                            : item.IsRunning ? "Running" : "Queued",
                        item.QueuePosition, ActiveWorkers: item.IsReservation && item.IsRunning ? 1 : 0));
            }

            if (_coordinatorActivityProvider is not null)
                activities.AddRange(_coordinatorActivityProvider());
            if (_workerActivityProvider is not null)
            {
                var queuedWorkerRunIds = activities
                    .Where(activity => activity.Kind == "Shared estimation worker")
                    .Select(activity => activity.RunId)
                    .ToHashSet(StringComparer.Ordinal);
                activities.AddRange(_workerActivityProvider()
                    .Where(activity => !queuedWorkerRunIds.Contains(activity.RunId)));
            }
            return activities;
        }

        private void BeginDeploymentDrain(string requestId)
        {
            if (!DeploymentProtocol.IsValidRequestId(requestId))
                throw new InvalidDataException("Invalid deployment request ID.");

            Console.WriteLine($"Deployment queued: {requestId}. Waiting for active RunServer work to finish.");
            Console.Out.Flush();
            lock (_deploymentSync)
            {
                if (_deploymentRequests.Count != 0)
                {
                    SendDeploymentFailure(requestId, "Another deployment is already in progress.");
                    return;
                }
                if (!_deploymentRequests.Add(requestId))
                    return;
                _deploymentIdle = false;
                _stagedDeploymentRoot = null;
                _stagedDeploymentRequestId = null;
            }

            try
            {
                var beginDrain = _beginDeploymentDrain ??
                    (_runScheduler is null ? null : new Action(_runScheduler.BeginDrain));
                beginDrain?.Invoke();
                SendDeploymentStatus(new RunServerDrainStatus(requestId, RunServerDrainState.Draining,
                    0, 0, "RunServer is draining existing work."));
                _ = Task.Run(() => CompleteDeploymentDrainAsync(requestId));
            }
            catch (Exception exception)
            {
                SendDeploymentFailure(requestId, exception.Message);
            }
        }

        private void CancelDeploymentDrain(string requestId)
        {
            if (!DeploymentProtocol.IsValidRequestId(requestId))
                throw new InvalidDataException("Invalid deployment request ID.");

            lock (_deploymentSync)
            {
                if (!_deploymentRequests.Contains(requestId))
                    return;
            }
            _cancelDeploymentDrain?.Invoke();
            lock (_deploymentSync)
            {
                _deploymentRequests.Remove(requestId);
                _deploymentIdle = false;
                _stagedDeploymentRoot = null;
                _stagedDeploymentRequestId = null;
            }
            SendDeploymentStatus(new RunServerDrainStatus(requestId, RunServerDrainState.Cancelled,
                0, 0, "RunServer deployment drain cancelled."));
        }

        private async Task CompleteDeploymentDrainAsync(string requestId)
        {
            while (true)
            {
                bool stillActive;
                lock (_deploymentSync)
                    stillActive = _deploymentRequests.Contains(requestId);
                if (!stillActive)
                    return;

                var activities = GetServerActivity();
                var activeCount = activities.Count(activity => activity.State == RunServerActivityState.Running);
                var queuedCount = activities.Count(activity => activity.State == RunServerActivityState.Queued);
                if ((_isDeploymentIdle?.Invoke() ?? _runScheduler?.IsIdle ?? true) && activities.Count == 0)
                {
                    SendDeploymentStatus(new RunServerDrainStatus(requestId, RunServerDrainState.Idle,
                        0, 0, "RunServer is idle and ready for deployment."));
                    lock (_deploymentSync)
                        _deploymentIdle = true;
                    SendDeploymentCompleted(new RunServerDrainStatus(requestId, RunServerDrainState.Idle,
                        0, 0, "RunServer is idle and ready for deployment."));
                    return;
                }

                SendDeploymentStatus(new RunServerDrainStatus(requestId, RunServerDrainState.Draining,
                    activeCount, queuedCount, "Waiting for existing RunServer work to finish."));
                await Task.Delay(250).ConfigureAwait(false);
            }
        }

        private void SendDeploymentStatus(RunServerDrainStatus status)
            => Write(writer =>
            {
                writer.Write((int)Out.DeploymentMessage);
                DeploymentProtocol.WriteDrainStatus(writer, status);
            });

        private void SendDeploymentFailure(string requestId, string message)
            => Write(writer =>
            {
                writer.Write((int)Out.DeploymentMessage);
                DeploymentProtocol.WriteDeploymentFailed(writer, requestId, message);
            });

        private void SendDeploymentCompleted(RunServerDrainStatus status)
            => Write(writer =>
            {
                writer.Write((int)Out.DeploymentMessage);
                DeploymentProtocol.WriteDrainCompleted(writer, status);
            });

        private void StageDeploymentArchive((string RequestId, byte[] Archive, string Sha256) deployment)
        {
            if (!DeploymentProtocol.IsValidRequestId(deployment.RequestId))
                throw new InvalidDataException("Invalid deployment request ID.");

            lock (_deploymentSync)
            {
                if (!_deploymentRequests.Contains(deployment.RequestId) || !_deploymentIdle)
                {
                    SendDeploymentFailure(deployment.RequestId,
                        "Deployment archive received before the matching drain completed.");
                    return;
                }
            }

            var actualHash = Convert.ToHexString(SHA256.HashData(deployment.Archive));
            if (!string.Equals(actualHash, deployment.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                SendDeploymentFailure(deployment.RequestId, "Deployment archive hash verification failed.");
                return;
            }

            try
            {
                using var archiveStream = new MemoryStream(deployment.Archive, writable: false);
                using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: false);
                if (archive.Entries.Count > DeploymentProtocol.MaxArchiveEntries)
                    throw new InvalidDataException("Deployment archive contains too many entries.");
                long expandedBytes = 0;
                foreach (var entry in archive.Entries)
                {
                    if (Path.IsPathRooted(entry.FullName) || entry.FullName.Contains("..", StringComparison.Ordinal))
                        throw new InvalidDataException("Deployment archive contains an unsafe path.");
                    if (entry.Length < 0 || entry.Length > DeploymentProtocol.MaxArchiveEntryBytes ||
                        (expandedBytes += entry.Length) > DeploymentProtocol.MaxExpandedArchiveBytes)
                        throw new InvalidDataException("Deployment archive expands beyond the allowed limit.");
                }

                var stagingRoot = Path.Combine(Path.GetTempPath(), "XTMF2", "Deployments", deployment.RequestId);
                Directory.CreateDirectory(stagingRoot);
                var archivePath = Path.Combine(stagingRoot, "deployment.zip");
                File.WriteAllBytes(archivePath, deployment.Archive);
                lock (_deploymentSync)
                {
                    _stagedDeploymentRoot = stagingRoot;
                    _stagedDeploymentRequestId = deployment.RequestId;
                }
                Console.WriteLine($"Deployment staged: {deployment.RequestId}. Archive hash verified.");
                Console.Out.Flush();
                SendDeploymentStaged(new RunServerDrainStatus(deployment.RequestId, RunServerDrainState.Staged,
                    0, 0, "Deployment archive staged and hash verified."));
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                SendDeploymentFailure(deployment.RequestId, exception.Message);
            }
        }

        private void SendDeploymentStaged(RunServerDrainStatus status)
            => Write(writer =>
            {
                writer.Write((int)Out.DeploymentMessage);
                DeploymentProtocol.WriteDeploymentStaged(writer, status);
            });

        private void ActivateDeployment(string requestId)
        {
            if (!DeploymentProtocol.IsValidRequestId(requestId))
                throw new InvalidDataException("Invalid deployment request ID.");

            string? stagingRoot;
            lock (_deploymentSync)
            {
                if (!string.Equals(_stagedDeploymentRequestId, requestId, StringComparison.Ordinal))
                {
                    SendDeploymentFailure(requestId, "Deployment activation does not match the staged archive.");
                    return;
                }
                stagingRoot = _stagedDeploymentRoot;
            }
            if (stagingRoot is null || !Directory.Exists(stagingRoot))
            {
                SendDeploymentFailure(requestId, "No verified deployment archive is staged.");
                return;
            }

            if (!(_activateDeployment?.Invoke(stagingRoot) ?? false))
                SendDeploymentFailure(requestId, "The RunServer could not activate the staged deployment.");
        }

        private static string CreateRunDirectory(string runId)
        {
            var directory = Path.Combine(Path.GetTempPath(), "XTMF2", "Runs", runId);
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
