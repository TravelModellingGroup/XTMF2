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
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XTMF2.Bus.Optimization;
using XTMF2.Editing;

namespace XTMF2.Bus;

/// <summary>
/// Provides communication with the client process
/// </summary>
public sealed class HostBus : IDisposable
{
    private readonly Stream _HostStream;
    private readonly bool _Owner;
    private volatile bool _Exit = false;
    private volatile bool _Exited = false;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<RunServerActivityResponse>> _pendingActivityRequests = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<RemoteRunDeletionResponse>> _pendingRemoteRunDeletionRequests = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<RunServerDrainStatus>> _pendingDrainRequests = new(StringComparer.Ordinal);

    /// <summary>
    /// Create a host on a given stream.
    /// </summary>
    /// <param name="hostStream">The stream to host.</param>
    /// <param name="streamOwner">Should this bus assume ownership over the stream?</param>
    public HostBus(Stream hostStream, bool streamOwner)
    {
        _Owner = streamOwner;
        _HostStream = hostStream ?? throw new ArgumentNullException(nameof(hostStream));
        _listenerThread = StartListenner();
    }

    ~HostBus()
    {
        Dispose(false);
    }

    private void Dispose(bool managed)
    {
        if (managed)
        {
            GC.SuppressFinalize(this);
        }
        _Exit = true;
        if (_Owner)
        {
            _HostStream.Dispose();
        }
        while (!_Exited)
        {
            Interlocked.MemoryBarrier();
            if (!_Exited)
            {
                Task.WaitAll(Task.Delay(50));
            }
            Interlocked.MemoryBarrier();
        }
    }

    /// <summary>
    /// Disconnect from the client.
    /// </summary>
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

    /// <summary>
    /// This event is signalled when a client finishes running a model system.
    /// The parameter is the name of the completed model system.
    /// </summary>
    public event EventHandler<string>? ClientFinishedModelSystem;

    public event EventHandler<RunArtifactsReceivedEventArgs>? ClientRunArtifactsReceived;

    public sealed class RunArtifactsReceivedEventArgs : EventArgs
    {
        public string RunId { get; }
        public string ArchivePath { get; }

        internal RunArtifactsReceivedEventArgs(string runId, string archivePath)
        {
            RunId = runId;
            ArchivePath = archivePath;
        }
    }

    /// <summary>
    /// Raised when the connection to the RunServer is closed or lost.
    /// </summary>
    public event EventHandler? Disconnected;

    /// <summary>
    /// Used to report that a model system has had a run error.
    /// </summary>
    /// <param name="sender">The object reporting the event.</param>
    /// <param name="runID">The ID of the run that failed.</param>
    /// <param name="errorMessage">The error message from the error.</param>
    /// <param name="stack">The stack trace at the point of the error.</param>
    /// <param name="moduleName">The name of the module that caused the error, if known.</param>
    /// <param name="elementId">The model element ID (Node/FunctionInstance/etc.) associated with the failure, if it could be resolved from the failing runtime module.</param>
    public delegate void RunError(object sender, string runID, string errorMessage, string stack, string? moduleName, Guid? elementId);

    /// <summary>
    /// Used to report that a model system has had a run error, including optional
    /// module metadata for UI navigation.
    /// </summary>
    public delegate void RunErrorWithTarget(object sender, string runID, string errorMessage, string stack, string? moduleName, Guid? elementId);

    /// <summary>
    /// Used to trigger a status update from a model system.
    /// </summary>
    /// <param name="sender">The object reporting the event.</param>
    /// <param name="runID">The ID of the run that is sending the update.</param>
    /// <param name="status">The status message from the model system.</param>
    public delegate void ClientStatusUpdate(object sender, string runID, string status);

    /// <summary>
    /// This event is signalled when a client runs into an error.
    /// </summary>
    public event RunError? ClientErrorWhenRunningModelSystem;

    /// <summary>
    /// This event is triggered when the client has sent an update for the run's status message.
    /// </summary>
    public event ClientStatusUpdate? ClientReportedStatus;

    /// <summary>
    /// Carries the final parameter values produced by a completed estimation or calibration run.
    /// </summary>
    /// <param name="sender">The object reporting the event.</param>
    /// <param name="runID">The ID of the run that produced the results.</param>
    /// <param name="results">Ordered list of (node serialisation index, best value) pairs.</param>
    public delegate void OptimizationResultsAvailable(
        object sender, string runID, IReadOnlyList<(int nodeIndex, double value)> results);

    /// <summary>
    /// Fired immediately before <see cref="ClientFinishedModelSystem"/> when an estimation or
    /// calibration run completes, carrying the final optimised parameter values.
    /// </summary>
    public event OptimizationResultsAvailable? ClientOptimizationResultsAvailable;

    /// <summary>
    /// Carries per-iteration progress data from a running estimation or calibration loop.
    /// </summary>
    /// <param name="sender">The object reporting the event.</param>
    /// <param name="runID">The ID of the run sending the update.</param>
    /// <param name="iteration">The current iteration number (1-based).</param>
    /// <param name="fitness">The best fitness value seen so far.</param>
    /// <param name="fitnessTestsThisIteration">Number of fitness tests completed since the previous progress update.</param>
    /// <param name="values">Ordered list of (node serialisation index, current value) pairs.</param>
    public delegate void IterationProgressUpdate(
        object sender, string runID, int iteration, double fitness, int fitnessTestsThisIteration,
        IReadOnlyList<(int nodeIndex, double value)> values);

    /// <summary>
    /// Fired after each optimisation iteration with the parameter values that were tested.
    /// </summary>
    public event IterationProgressUpdate? ClientIterationProgressAvailable;

    public event EventHandler<SharedEstimationEvaluationResult>? SharedEstimationResultAvailable;

    public event EventHandler<SharedEstimationProgress>? SharedEstimationProgressAvailable;

    public event EventHandler<SharedEstimationCompletion>? SharedEstimationCompleted;

    public event EventHandler<IReadOnlyList<SharedEstimationJobSnapshot>>? SharedEstimationJobSnapshotsAvailable;

    public event EventHandler<IReadOnlyList<RemoteRunSnapshot>>? RemoteRunSnapshotsAvailable;

    public event EventHandler<RemoteRunArtifactsResponse>? RemoteRunArtifactsAvailable;

    public event EventHandler<SharedEstimationWorkerControlAcknowledgement>? SharedEstimationWorkerControlAcknowledged;

    public event EventHandler<SharedEstimationStatus>? SharedEstimationStatusAvailable;

    public event EventHandler<SharedEstimationWorkerReady>? SharedEstimationWorkerReadyAvailable;

    public event EventHandler<RunServerDrainStatus>? RunServerDrainStatusAvailable;

    private static void IgnoreWarnings(Action toRun)
    {
        /*
        * We are disabling the warning to catch a specific error since we are going to
        * be called into unknown code.
        */
#pragma warning disable CA1031
        try
        {
            toRun();
        }
        catch { }
#pragma warning restore CA1031
    }

    private Thread _listenerThread;

    /// <summary>
    /// Invoke this to start listening on a separate thread.
    /// </summary>
    private Thread StartListenner()
    {
        var listenerThread = new Thread(() => 
        {
            try
            {
                using var reader = new BinaryReader(_HostStream, Encoding.UTF8, true);
                while (!_Exit)
                {
                    var commandValue = reader.ReadInt32();
                    var command = (In)commandValue;
                    switch (command)
                    {
                        case In.Heartbeat:
                            // Read in the ID of the run that issued the Heartbeat.
                            reader.ReadString(); 
                            break;
                        case In.ClientReady:
                            break;
                        case In.ClientExiting:
                            _Exit = true;
                            break;
                        case In.ClientErrorValidatingModelSystem:
                            {
                                var runId = reader.ReadString();
                                var errMsg = reader.ReadString();
                                var moduleName = reader.ReadString();
                                var elementId = Guid.TryParse(reader.ReadString(), out var parsedId) ? (Guid?)parsedId : null;
                                Console.WriteLine($"Host <- RunServer validation error: {runId}. {errMsg}");
                                Console.Out.Flush();
                                IgnoreWarnings(() => ClientErrorWhenRunningModelSystem?.Invoke(this, runId, errMsg, String.Empty, moduleName, elementId));
                            }
                            break;
                        case In.ClientFinishedModelSystem:
                            {
                                var runId = reader.ReadString();
                                Console.WriteLine($"Host <- RunServer completion: {runId}");
                                Console.Out.Flush();
                                IgnoreWarnings(() => ClientFinishedModelSystem?.Invoke(this, runId));
                            }
                            break;
                        case In.ClientErrorWhenRunningModelSystem:
                            {
                                var runId = reader.ReadString();
                                var errMsg = reader.ReadString();
                                var stack = reader.ReadString();
                                var moduleName = reader.ReadString();
                                var elementIdText = reader.ReadString();
                                var elementId = Guid.TryParse(elementIdText, out var parsedId)
                                    ? (Guid?)parsedId
                                    : null;
                                Console.WriteLine($"Host <- RunServer runtime error: {runId}. {errMsg}");
                                Console.Out.Flush();
                                IgnoreWarnings(() => ClientErrorWhenRunningModelSystem?.Invoke(this, runId, errMsg, stack, moduleName, elementId));
                            }
                            break;
                        case In.ClientReportedStatus:
                            {
                                var runId = reader.ReadString();
                                var status = reader.ReadString();
                                IgnoreWarnings(() => ClientReportedStatus?.Invoke(this, runId, status));
                            }
                            break;
                        case In.ClientOptimizationResults:
                            {
                                var runId = reader.ReadString();
                                int count = reader.ReadInt32();
                                var results = new (int nodeIndex, double value)[count];
                                for (int i = 0; i < count; i++)
                                    results[i] = (reader.ReadInt32(), reader.ReadDouble());
                                IgnoreWarnings(() => ClientOptimizationResultsAvailable?.Invoke(this, runId, results));
                            }
                            break;
                        case In.ClientIterationProgress:
                            {
                                var runId = reader.ReadString();
                                int iteration = reader.ReadInt32();
                                double fitness = reader.ReadDouble();
                                int fitnessTestsThisIteration = reader.ReadInt32();
                                int count = reader.ReadInt32();
                                var values = new (int nodeIndex, double value)[count];
                                for (int i = 0; i < count; i++)
                                    values[i] = (reader.ReadInt32(), reader.ReadDouble());
                                IgnoreWarnings(() => ClientIterationProgressAvailable?.Invoke(
                                    this, runId, iteration, fitness, fitnessTestsThisIteration, values));
                            }
                            break;
                        case In.ClientRunArtifacts:
                            {
                                var runId = reader.ReadString();
                                var length = reader.ReadInt64();
                                var archivePath = Path.Combine(Path.GetTempPath(), $"XTMF2-{Guid.NewGuid():N}.zip");
                                try
                                {
                                    using (var archive = File.Create(archivePath))
                                        CopyExactly(reader.BaseStream, archive, length);
                                    IgnoreWarnings(() => ClientRunArtifactsReceived?.Invoke(
                                        this, new RunArtifactsReceivedEventArgs(runId, archivePath)));
                                }
                                finally
                                {
                                    try { File.Delete(archivePath); } catch { }
                                }
                            }
                            break;
                        case In.SharedEstimationMessage:
                            {
                                var (_, messageType) = SharedEstimationProtocol.ReadHeader(reader);
                                switch (messageType)
                                {
                                    case SharedEstimationMessageType.EvaluationResult:
                                        IgnoreWarnings(() => SharedEstimationResultAvailable?.Invoke(
                                            this, SharedEstimationProtocol.ReadResultPayload(reader)));
                                        break;
                                    case SharedEstimationMessageType.Progress:
                                        IgnoreWarnings(() => SharedEstimationProgressAvailable?.Invoke(
                                            this, SharedEstimationProtocol.ReadProgressPayload(reader)));
                                        break;
                                    case SharedEstimationMessageType.Complete:
                                        IgnoreWarnings(() => SharedEstimationCompleted?.Invoke(
                                            this, SharedEstimationProtocol.ReadCompletionPayload(reader)));
                                        break;
                                    case SharedEstimationMessageType.JobSnapshots:
                                        IgnoreWarnings(() => SharedEstimationJobSnapshotsAvailable?.Invoke(
                                            this, SharedEstimationProtocol.ReadJobSnapshotsPayload(reader)));
                                        break;
                                    case SharedEstimationMessageType.WorkerControlAcknowledgement:
                                        IgnoreWarnings(() => SharedEstimationWorkerControlAcknowledged?.Invoke(
                                            this, SharedEstimationProtocol.ReadWorkerControlAcknowledgementPayload(reader)));
                                        break;
                                    case SharedEstimationMessageType.Status:
                                        IgnoreWarnings(() => SharedEstimationStatusAvailable?.Invoke(
                                            this, SharedEstimationProtocol.ReadStatusPayload(reader)));
                                        break;
                                    case SharedEstimationMessageType.WorkerReady:
                                        var readiness = SharedEstimationProtocol.ReadWorkerReadyPayload(reader);
                                        IgnoreWarnings(() => SharedEstimationWorkerReadyAvailable?.Invoke(this, readiness));
                                        break;
                                    case SharedEstimationMessageType.RemoteRunSnapshots:
                                        IgnoreWarnings(() => RemoteRunSnapshotsAvailable?.Invoke(
                                            this, SharedEstimationProtocol.ReadRemoteRunSnapshotsPayload(reader)));
                                        break;
                                    case SharedEstimationMessageType.RemoteRunArtifacts:
                                        {
                                            var archivePath = Path.Combine(Path.GetTempPath(),
                                                $"XTMF2-{Guid.NewGuid():N}.zip");
                                            try
                                            {
                                                RemoteRunArtifactsResponse response;
                                                using (var archive = new FileStream(archivePath, FileMode.CreateNew,
                                                    FileAccess.Write, FileShare.Read))
                                                {
                                                    response = SharedEstimationProtocol.ReadRemoteRunArtifactsPayload(
                                                        reader, archive, archivePath);
                                                }
                                                IgnoreWarnings(() => RemoteRunArtifactsAvailable?.Invoke(this, response));
                                            }
                                            finally
                                            {
                                                try { File.Delete(archivePath); } catch { }
                                            }
                                        }
                                        break;
                                    case SharedEstimationMessageType.ServerActivitySnapshots:
                                        var activityResponse = SharedEstimationProtocol.ReadServerActivitySnapshotsPayload(reader);
                                        if (_pendingActivityRequests.TryRemove(activityResponse.RequestId, out var pendingActivity))
                                            pendingActivity.TrySetResult(activityResponse);
                                        break;
                                    case SharedEstimationMessageType.RemoteRunDeleted:
                                        var deletionResponse = SharedEstimationProtocol.ReadRemoteRunDeletedPayload(reader);
                                        if (_pendingRemoteRunDeletionRequests.TryRemove(deletionResponse.RequestId, out var pendingDeletion))
                                            pendingDeletion.TrySetResult(deletionResponse);
                                        break;
                                    default:
                                        throw new InvalidDataException(
                                            $"Unexpected shared estimation message from RunServer: {messageType}.");
                                }
                            }
                            break;
                        case In.DeploymentMessage:
                            {
                                var (_, messageType) = DeploymentProtocol.ReadHeader(reader);
                                switch (messageType)
                                {
                                    case DeploymentMessageType.DrainStatus:
                                    case DeploymentMessageType.DrainCompleted:
                                    case DeploymentMessageType.DeploymentStaged:
                                        var status = DeploymentProtocol.ReadStatusPayload(reader);
                                        if ((messageType == DeploymentMessageType.DrainCompleted ||
                                            messageType == DeploymentMessageType.DeploymentStaged) &&
                                            _pendingDrainRequests.TryGetValue(status.RequestId, out var pendingDrain))
                                        {
                                            pendingDrain.TrySetResult(status);
                                            _pendingDrainRequests.TryRemove(status.RequestId, out _);
                                        }
                                        IgnoreWarnings(() => RunServerDrainStatusAvailable?.Invoke(this, status));
                                        break;
                                    case DeploymentMessageType.DeploymentFailed:
                                        var requestId = DeploymentProtocol.ReadRequestId(reader);
                                        var failure = new RunServerDrainStatus(requestId, RunServerDrainState.Failed, 0, 0,
                                            DeploymentProtocol.ReadFailureMessage(reader));
                                        if (_pendingDrainRequests.TryGetValue(requestId, out var failedDrain))
                                            failedDrain.TrySetResult(failure);
                                        IgnoreWarnings(() => RunServerDrainStatusAvailable?.Invoke(this, failure));
                                        break;
                                    default:
                                        throw new InvalidDataException($"Unexpected deployment message from RunServer: {messageType}.");
                                }
                            }
                            break;
                        default:
                            throw new InvalidDataException(
                                $"Unsupported command value {commandValue}: {Enum.GetName<In>(command) ?? "unknown"}.");
                    }
                    System.Threading.Interlocked.MemoryBarrier();
                }
            }
            catch (Exception ex)
            {
                // The client has disconnected or crashed. Exit the listener thread.
                Console.Error.WriteLine($"[HostBus] RunServer connection listener stopped: {ex}");
                Console.Error.Flush();
            }
            finally
            {
                _Exited = true;
                foreach (var request in _pendingActivityRequests.ToArray())
                    if (_pendingActivityRequests.TryRemove(request.Key, out var pending))
                        pending.TrySetException(new IOException("The RunServer connection was closed before the activity response arrived."));
                foreach (var request in _pendingDrainRequests.ToArray())
                    if (_pendingDrainRequests.TryRemove(request.Key, out var pending))
                        pending.TrySetException(new IOException("The RunServer connection was closed during deployment drain."));
                IgnoreWarnings(() => Disconnected?.Invoke(this, EventArgs.Empty));
            }
        })
        {
            IsBackground = true,
            Name = "HostBus Listener Thread"
        };
        listenerThread.Start();
        return listenerThread;
    }

    private static void CopyExactly(Stream source, Stream destination, long length)
    {
        var buffer = new byte[81920];
        while (length > 0)
        {
            var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, length));
            if (read == 0)
                throw new EndOfStreamException("The RunServer artifact archive was truncated.");
            destination.Write(buffer, 0, read);
            length -= read;
        }
    }

    private enum Out
    {
        Heartbeat = 0,
        RunModelSystem = 1,
        CancelModelRun = 2,
        KillModelRun = 3,
        RequestClientShutdown = 4,
        SharedEstimationMessage = 5,
        DeploymentMessage = 6,
    }

    /// <summary>
    /// Take this lock before writing anything to the out stream.
    /// </summary>
    private readonly object _outLock = new object();

    /// <summary>
    /// 
    /// </summary>
    /// <param name="modelSystem">The model system to execute</param>
    /// <param name="cwd">The directory to run in.</param>
    /// <param name="startToExecute">The starting point for the model system run</param>
    /// <param name="id">The ID given to this model run.</param>
    /// <param name="error">An error message if there is an issue creating the model system.</param>
    /// <returns>True if the model system was sent</returns>
    public bool RunModelSystem(ModelSystemSession modelSystem, string cwd, string startToExecute, 
        [NotNullWhen(true)] out string? id, [NotNullWhen(false)] out CommandError? error)
        => RunModelSystem(modelSystem, cwd, startToExecute, RunMode.Normal, null, out id, out error);

    /// <summary>
    /// Send a run command to the client with an explicit run mode (Normal, Estimation or Calibration).
    /// </summary>
    /// <param name="modelSystem">The model system to execute.</param>
    /// <param name="cwd">The directory to run in.</param>
    /// <param name="startToExecute">The starting point for the model system run.</param>
    /// <param name="runMode">Whether to run normally or as an estimation/calibration job.</param>
    /// <param name="id">The unique ID of the run, null if the command fails.</param>
    /// <param name="error">An error message if there is an issue creating the model system.</param>
    /// <returns>True if the model system was sent</returns>
    public bool RunModelSystem(ModelSystemSession modelSystem, string cwd, string startToExecute,
        RunMode runMode,
        [NotNullWhen(true)] out string? id, [NotNullWhen(false)] out CommandError? error)
        => RunModelSystem(modelSystem, cwd, startToExecute, runMode, null, out id, out error);

    /// <summary>
    /// Send a run command and invoke <paramref name="onIdCreated"/> before the request is written,
    /// allowing consumers to register the run before fast clients can respond.
    /// </summary>
    public bool RunModelSystem(ModelSystemSession modelSystem, string cwd, string startToExecute,
        RunMode runMode,
        Action<string>? onIdCreated,
        [NotNullWhen(true)] out string? id, [NotNullWhen(false)] out CommandError? error,
        string? runName = null, Guid? ownerUserId = null)
    {
        id = null;
        lock (_outLock)
        {
            try
            {
                using var memStream = new MemoryStream();
                using var write = new BinaryWriter(memStream, Encoding.UTF8, true);
                if (!modelSystem.Save(out error, memStream))
                {
                    return false;
                }
                id = Guid.NewGuid().ToString();
                onIdCreated?.Invoke(id);
                // int64
                using var writer = new BinaryWriter(_HostStream, Encoding.UTF8, true);
                writer.Write((int)Out.RunModelSystem);
                writer.Write(id);
                writer.Write(cwd);
                writer.Write(startToExecute);
                writer.Write((int)runMode);
                writer.Write(runName ?? Path.GetFileName(cwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
                writer.Write(modelSystem.Project.Id.ToString("D"));
                writer.Write(modelSystem.ModelSystemHeader.Id.ToString("D"));
                writer.Write(ownerUserId?.ToString("D") ?? string.Empty);
                writer.Write(memStream.Length);
                memStream.WriteTo(_HostStream);
                return true;
            }
            catch (IOException e)
            {
                error = new CommandError(e.Message);
                return false;
            }
        }
    }

    /// <summary>
    /// Cancel a model run with the given ID. This will trigger an event on the client to cancel the run, but it is up to the client to decide how to handle this.
    /// </summary>
    /// <param name="runID">The ID of the run to cancel.</param>
    /// <param name="error">An error message if there is an issue canceling the run.</param>
    /// <returns>True if the cancel command was successfully sent, false otherwise.</returns>
    public bool CancelModelRun(string runID, [NotNullWhen(false)] out CommandError? error)
    {
        error = null;
        lock (_outLock)
        {
            try
            {
                using var writer = new BinaryWriter(_HostStream, Encoding.UTF8, true);
                writer.Write((int)Out.CancelModelRun);
                writer.Write(runID);
                return true;
            }
            catch (IOException e)
            {
                error = new CommandError(e.Message);
                return false;
            }
        }
    }

    /// <summary>
    /// Kill a model run with the given ID. This will trigger an event on the client to kill the run, but it is up to the client to decide how to handle this.
    /// </summary>
    /// <param name="runID">The ID of the run to kill.</param>
    /// <param name="error">An error message if there is an issue killing the run.</param>
    /// <returns>True if the kill command was successfully sent, false otherwise.</returns>
    public bool KillModelRun(string runID, [NotNullWhen(false)] out CommandError? error)
    {
        error = null;
        lock (_outLock)
        {
            try
            {
                using var writer = new BinaryWriter(_HostStream, Encoding.UTF8, true);
                writer.Write((int)Out.KillModelRun);
                writer.Write(runID);
                return true;
            }
            catch (IOException e)
            {
                error = new CommandError(e.Message);
                return false;
            }
        }
    }

    public bool RequestClientShutdown([NotNullWhen(false)] out CommandError? error)
    {
        error = null;
        lock (_outLock)
        {
            try
            {
                using var writer = new BinaryWriter(_HostStream, Encoding.UTF8, true);
                writer.Write((int)Out.RequestClientShutdown);
                return true;
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException or ArgumentException)
            {
                error = new CommandError(e.Message);
                return false;
            }
        }
    }

    public async Task<RunServerDrainStatus> BeginDeploymentDrainAsync(
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<RunServerDrainStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingDrainRequests.TryAdd(requestId, completion))
            throw new InvalidOperationException("Unable to register the deployment drain request.");

        try
        {
            if (!WriteDeployment(writer => DeploymentProtocol.WriteBeginDrain(writer, requestId), out var error))
                throw new IOException(error?.Message ?? "Unable to begin RunServer deployment drain.");
        }
        catch
        {
            _pendingDrainRequests.TryRemove(requestId, out _);
            throw;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCancellation.CancelAfter(timeout ?? TimeSpan.FromMinutes(30));
        using var registration = linkedCancellation.Token.Register(() =>
        {
            if (_pendingDrainRequests.TryRemove(requestId, out var pending))
                pending.TrySetCanceled(linkedCancellation.Token);
            WriteDeployment(writer => DeploymentProtocol.WriteCancelDrain(writer, requestId), out _);
        });

        try
        {
            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            _pendingDrainRequests.TryRemove(requestId, out _);
        }
    }

    public bool SendDeploymentArchive(string requestId, byte[] archive,
        [NotNullWhen(false)] out CommandError? error)
    {
        error = null;
        try
        {
            var hash = Convert.ToHexString(SHA256.HashData(archive));
            return WriteDeployment(writer => DeploymentProtocol.WriteDeployArchive(writer, requestId, archive, hash), out error);
        }
        catch (ArgumentException exception)
        {
            error = new CommandError(exception.Message);
            return false;
        }
    }

    public bool ActivateDeployment(string requestId,
        [NotNullWhen(false)] out CommandError? error)
        => WriteDeployment(writer => DeploymentProtocol.WriteActivateDeployment(writer, requestId), out error);

    public async Task<RunServerDrainStatus> SendDeploymentArchiveAsync(string requestId, byte[] archive,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource<RunServerDrainStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingDrainRequests.TryAdd(requestId, completion))
            throw new InvalidOperationException("A deployment request with this ID is already pending.");

        try
        {
            if (!SendDeploymentArchive(requestId, archive, out var error))
                throw new IOException(error?.Message ?? "Unable to send the deployment archive.");

            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCancellation.CancelAfter(timeout ?? TimeSpan.FromMinutes(30));
            using var registration = linkedCancellation.Token.Register(() =>
            {
                if (_pendingDrainRequests.TryRemove(requestId, out var pending))
                    pending.TrySetCanceled(linkedCancellation.Token);
            });
            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            _pendingDrainRequests.TryRemove(requestId, out _);
        }
    }

    public bool StartSharedEstimation(SharedEstimationRunRequest request,
        [NotNullWhen(false)] out CommandError? error)
        => WriteSharedEstimation(writer => SharedEstimationProtocol.WriteRunRequest(writer, request), out error);

    public bool StartRemoteSharedEstimation(SharedEstimationCoordinatorRequest request,
        [NotNullWhen(false)] out CommandError? error)
        => WriteSharedEstimation(writer => SharedEstimationProtocol.WriteCoordinatorRequest(writer, request), out error);

    public bool AddSharedEstimationWorker(SharedEstimationWorkerRegistration registration,
        [NotNullWhen(false)] out CommandError? error)
        => WriteSharedEstimation(writer => SharedEstimationProtocol.WriteWorkerRegistration(writer, registration, remove: false), out error);

    public bool RemoveSharedEstimationWorker(SharedEstimationWorkerRegistration registration,
        [NotNullWhen(false)] out CommandError? error)
        => WriteSharedEstimation(writer => SharedEstimationProtocol.WriteWorkerRegistration(writer, registration, remove: true), out error);

    public bool SendSharedEstimationCandidate(SharedEstimationCandidate candidate,
        [NotNullWhen(false)] out CommandError? error)
        => WriteSharedEstimation(writer => SharedEstimationProtocol.WriteCandidate(writer, candidate), out error);

    public bool CancelSharedEstimation(string runId, string? reason,
        [NotNullWhen(false)] out CommandError? error)
        => WriteSharedEstimation(writer => SharedEstimationProtocol.WriteCancel(writer, runId, reason), out error);

    public bool QuerySharedEstimationJobs([NotNullWhen(false)] out CommandError? error)
        => WriteSharedEstimation(SharedEstimationProtocol.WriteQueryJobs, out error);

    public bool QueryRemoteRuns([NotNullWhen(false)] out CommandError? error)
        => WriteSharedEstimation(SharedEstimationProtocol.WriteQueryRemoteRuns, out error);

    public async Task<RunServerActivityResponse> QueryServerActivityAsync(
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<RunServerActivityResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingActivityRequests.TryAdd(requestId, completion))
            throw new InvalidOperationException("Unable to register the RunServer activity request.");

        try
        {
            if (!WriteSharedEstimation(writer =>
                SharedEstimationProtocol.WriteQueryServerActivity(writer, requestId), out var error))
                throw new IOException(error?.Message ?? "Unable to query RunServer activity.");
        }
        catch
        {
            _pendingActivityRequests.TryRemove(requestId, out _);
            throw;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCancellation.CancelAfter(timeout ?? TimeSpan.FromSeconds(8));
        using var registration = linkedCancellation.Token.Register(() =>
        {
            if (_pendingActivityRequests.TryRemove(requestId, out var pending))
                pending.TrySetCanceled(linkedCancellation.Token);
        });

        try
        {
            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            _pendingActivityRequests.TryRemove(requestId, out _);
        }
    }

    public bool RequestRemoteRunArtifacts(string runId, [NotNullWhen(false)] out CommandError? error)
        => WriteSharedEstimation(writer => SharedEstimationProtocol.WriteGetRemoteRunArtifacts(writer, runId), out error);

    public bool AcknowledgeRemoteRun(string runId, [NotNullWhen(false)] out CommandError? error)
        => WriteSharedEstimation(writer => SharedEstimationProtocol.WriteAcknowledgeRemoteRun(writer, runId), out error);

    public async Task<RemoteRunDeletionResponse> DeleteRemoteRunAsync(string runId,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<RemoteRunDeletionResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingRemoteRunDeletionRequests.TryAdd(requestId, completion))
            throw new InvalidOperationException("Unable to register the remote run deletion request.");

        try
        {
            if (!WriteSharedEstimation(writer =>
                SharedEstimationProtocol.WriteDeleteRemoteRun(writer, requestId, runId), out var error))
                throw new IOException(error?.Message ?? "Unable to request remote run deletion.");
        }
        catch
        {
            _pendingRemoteRunDeletionRequests.TryRemove(requestId, out _);
            throw;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCancellation.CancelAfter(timeout ?? TimeSpan.FromSeconds(15));
        using var registration = linkedCancellation.Token.Register(() =>
        {
            if (_pendingRemoteRunDeletionRequests.TryRemove(requestId, out var pending))
                pending.TrySetCanceled(linkedCancellation.Token);
        });

        try
        {
            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            _pendingRemoteRunDeletionRequests.TryRemove(requestId, out _);
        }
    }

    public bool AddRemoteEstimationWorker(string runId, SharedEstimationWorkerEndpoint worker,
        [NotNullWhen(false)] out CommandError? error)
        => WriteSharedEstimation(writer =>
            SharedEstimationProtocol.WriteCoordinatorWorkerRequest(writer, runId, worker, remove: false), out error);

    public bool RemoveRemoteEstimationWorker(string runId, SharedEstimationWorkerEndpoint worker,
        [NotNullWhen(false)] out CommandError? error)
        => WriteSharedEstimation(writer =>
            SharedEstimationProtocol.WriteCoordinatorWorkerRequest(writer, runId, worker, remove: true), out error);

    private bool WriteSharedEstimation(Action<BinaryWriter> writePayload,
        [NotNullWhen(false)] out CommandError? error)
    {
        error = null;
        using var payload = new MemoryStream();
        try
        {
            using var payloadWriter = new BinaryWriter(payload, Encoding.UTF8, true);
            writePayload(payloadWriter);
            payloadWriter.Flush();
        }
        catch (ArgumentException e)
        {
            error = new CommandError(e.Message);
            return false;
        }

        lock (_outLock)
        {
            try
            {
                using var writer = new BinaryWriter(_HostStream, Encoding.UTF8, true);
                writer.Write((int)Out.SharedEstimationMessage);
                payload.Position = 0;
                payload.CopyTo(_HostStream);
                writer.Flush();
                return true;
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException or ArgumentException)
            {
                error = new CommandError(e.Message);
                return false;
            }
        }
    }

    private bool WriteDeployment(Action<BinaryWriter> writePayload,
        [NotNullWhen(false)] out CommandError? error)
    {
        error = null;
        lock (_outLock)
        {
            try
            {
                using var writer = new BinaryWriter(_HostStream, Encoding.UTF8, true);
                writer.Write((int)Out.DeploymentMessage);
                writePayload(writer);
                writer.Flush();
                return true;
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException or ArgumentException)
            {
                error = new CommandError(e.Message);
                return false;
            }
        }
    }

}

