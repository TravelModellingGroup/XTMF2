/*
    Copyright 2026 University of Toronto

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
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using XTMF2.Bus;
using XTMF2.Bus.Optimization;
using XTMF2.Controllers;
using XTMF2.Editing;
using XTMF2.GUI.ViewModels;
using XTMF2.GUI.Properties;
using XTMF2.ModelSystemConstruct;
using System.Linq;


namespace XTMF2.GUI;

public sealed record RunServerActivityServerSnapshot(
    RunServerEndpoint Endpoint,
    RunServerConnectionState ConnectionState,
    string? Error,
    IReadOnlyList<RunServerActivity> Activities);

/// <summary>
/// Used to control XTMF from the GUI. This is a separate class from XTMFRuntime 
/// to avoid circular dependencies between the GUI and the core runtime.
/// 
/// In the future this might be expanded to allow for communicating with XTMF2 hosts that are not on the same system.
/// </summary>
public class RunController : IDisposable
{
    /// <summary>
    /// The XTMF runtime that this controller controls.
    /// </summary>
    public XTMFRuntime Runtime { get; private set; }

    /// <summary>
    /// The view model that tracks active and completed runs for display in the Runs tab.
    /// </summary>
    public RunsViewModel RunsViewModel { get; } = new RunsViewModel();

    /// <summary>
    /// The hostbus that this controller uses to communicate with the client. This is used to send commands to the client and receive status updates from the client.
    /// </summary>
    private HostBus _hostBus;
    private readonly RunServerConnectionManager _connections;
    private const string LocalEndpointId = "local";
    private readonly object _hostBusSubscriptionLock = new();
    private readonly HashSet<HostBus> _subscribedHostBuses = new();

    /// <summary>
    /// Maps run IDs to the model system session and the user that submitted the run,
    /// so that optimization results can be forwarded to the correct session.
    /// </summary>
    private readonly Dictionary<string, (ModelSystemSession Session, User User)> _sessionsByRunId = new();
    private readonly Dictionary<string, HostBus> _hostBusesByRunId = new();
    private readonly Dictionary<string, string> _runDirectoriesByRunId = new();
    private readonly Dictionary<string, IReadOnlyList<(int nodeIndex, string name, double min, double max)>>
        _remoteEstimationMetadata = new();
    private readonly Dictionary<string, IReadOnlyDictionary<string, SharedEstimationWorkerEndpoint>>
        _remoteWorkerEndpointsByRunId = new();
    private readonly HashSet<string> _disconnectedRunServerIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RemoteRunSnapshot> _recoveredRemoteRuns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SharedEstimationJobSnapshot> _recoveredSharedEstimationRuns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HostBus> _remoteRunBusesByRunId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (HostBus HostBus, string TargetDirectory)> _pendingRemoteReceipts = new(StringComparer.Ordinal);
    private readonly SharedEstimationCompletionGate _remoteCompletionGate = new();
    private readonly HashSet<string> _automaticBindingAttempts = new(StringComparer.Ordinal);
    private User? _recoveryUser;

    /// <summary>
    /// Fires when an estimation or calibration run completes and has results ready to be
    /// optionally applied back to the model system.
    /// </summary>
    public event Action<string, ModelSystemSession, IReadOnlyList<(int nodeIndex, double value)>>? OptimizationResultsAvailable;

    /// <summary>
    /// Raised when a configured RunServer changes connection state.
    /// </summary>
    public event Action<RunServerConnectionInfo>? RunServerStateChanged;

    /// <summary>
    /// If running in debug mode, the RunServerBus with be run within the same process as the GUI to make debugging easier.
    /// </summary>
    private RunServerBus? _runServerBus;
    private readonly Process? _runServerProcess;

    /// <summary>
    /// Generate a new Run controller
    /// </summary>
    /// <param name="runtime"></param>
    /// <param name="controller">The newly created RunController instance.</param>
    /// <param name="error">The reason that we can not generate a new run constroller for the XTMFRuntime.</param>
    /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
    internal static bool InitializeRunController(XTMFRuntime runtime,
        [NotNullWhen(true)] out RunController? controller,
        [NotNullWhen(false)] ref string? error)
    {

        if (Debugger.IsAttached)
        {
            return InitializeForDebugging(runtime, out controller, out error);
        }
        else
        {
            return InitializeInSeparateProcess(runtime, out controller, out error);
        }
    }

    private static bool InitializeForDebugging(XTMFRuntime runtime, out RunController? controller, out string? error)
    {
        if(!XTMF2.Bus.CreateStreams.CreateDebugBusses(runtime, out HostBus hostBus, out RunServerBus runServerBus, out error))
        {
            controller = null;
            return false;
        }

        controller = new RunController(runtime, hostBus)
        {
            _runServerBus = runServerBus
        };
        controller.SubscribeToHostBus(hostBus);
        controller.ConnectConfiguredRunServers();
        // Start the client processing in a separate thread to avoid blocking the GUI
        Task.Factory.StartNew(
            () =>
            {
                runServerBus.ProcessRequests();
            }, TaskCreationOptions.LongRunning);
        error = null;
        return true;
    }

    private static bool InitializeInSeparateProcess(XTMFRuntime runtime, out RunController? controller, out string? error)
    {
        var xtmfGUIFilePath = typeof(XTMF2.GUI.Program).Assembly.Location;
        var xtmfClientFileName = Path.Combine(Path.GetDirectoryName(xtmfGUIFilePath)!, "XTMF2.RunServer.dll");
        Process? client = null;
        try
        {
            var startInfo = new ProcessStartInfo()
            {
                FileName = "dotnet",
                Arguments = $"\"{xtmfClientFileName}\" -tcp 127.0.0.1 0",
                UseShellExecute = false,
                CreateNoWindow = OperatingSystem.IsWindows(),
                WorkingDirectory = Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            client = new() { StartInfo = startInfo, EnableRaisingEvents = true };
            client.Start();
            var listenLine = client.StandardOutput.ReadLine();
            if (!TryParseListeningPort(listenLine, out var hostPort))
            {
                error = "The local RunServer did not report a listening TCP port.";
                controller = null;
                return false;
            }
            if (!XTMF2.Bus.CreateStreams.CreateTcpClient("127.0.0.1", hostPort, out var hostStream, out error))
            {
                controller = null;
                return false;
            }
            var hostBus = new HostBus(hostStream!, true);
            controller = new RunController(runtime, hostBus, client);
            controller.SubscribeToHostBus(hostBus);
            controller.ConnectConfiguredRunServers();
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to initialize the run controller: {ex.Message}";
            controller = null;
            return false;
        }
    }

    private void OnClientErrorWhenRunningModelSystem(object sender, string runID, string errorMessage, string stack, string? moduleName, Guid? elementId)
    {
        Console.WriteLine($"GUI received RunServer runtime error: {runID}: {errorMessage}");
        Console.Out.Flush();
        RunsViewModel.NotifyError(runID, errorMessage, stack, moduleName, elementId);
    }

    private void OnClientFinishedModelSystem(object? sender, string runID)
    {
        Console.WriteLine($"GUI received RunServer completion: {runID}");
        Console.Out.Flush();
        RunsViewModel.NotifyFinished(runID);
    }

    private void OnClientRunArtifactsReceived(object? sender, HostBus.RunArtifactsReceivedEventArgs args)
    {
        string? targetDirectory;
        var hostBus = sender as HostBus;
        lock (_sessionsByRunId)
        {
            _runDirectoriesByRunId.TryGetValue(args.RunId, out targetDirectory);
            if (targetDirectory is null && hostBus is not null &&
                _remoteRunBusesByRunId.TryGetValue(args.RunId, out var remoteRunBus) &&
                ReferenceEquals(remoteRunBus, hostBus))
            {
                targetDirectory = RemoteRunOutputPaths.GetLocalDirectory(args.RunId);
            }
        }

        if (targetDirectory is null && hostBus is not null &&
            _connections.TryGetEndpoint(hostBus, out var endpoint) && endpoint is { IsLocal: false })
        {
            targetDirectory = RemoteRunOutputPaths.GetLocalDirectory(args.RunId);
        }

        if (targetDirectory is null)
        {
            RunsViewModel.NotifyArtifactTransferFailed(args.RunId, "The local run directory is no longer available.");
            return;
        }

        try
        {
            ExtractRunArtifacts(args.ArchivePath, targetDirectory);
            RunsViewModel.NotifyArtifactsTransferred(args.RunId);
        }
        catch (Exception ex)
        {
            RunsViewModel.NotifyArtifactTransferFailed(args.RunId, ex.Message);
        }
    }

    private static void ExtractRunArtifacts(string archivePath, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        var root = Path.GetFullPath(targetDirectory);
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var destination = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!destination.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(destination, root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The RunServer returned an invalid artifact path.");
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, true);
        }
    }

    private void OnClientReportedStatus(object sender, string runID, string status)
    {
        RunsViewModel.NotifyStatus(runID, status);
    }

    private void OnClientIterationProgressAvailable(
        object sender, string runID, int iteration, double fitness, int fitnessTestsThisIteration,
        IReadOnlyList<(int nodeIndex, double value)> values)
    {
        RunsViewModel.NotifyIterationProgress(runID, iteration, fitness, fitnessTestsThisIteration, values);
    }

    private void OnSharedEstimationProgress(object? sender, SharedEstimationProgress progress)
    {
        RunsViewModel.NotifySharedEstimationProgress(progress);
    }

    private void OnSharedEstimationStatus(object? sender, SharedEstimationStatus status)
    {
        RunsViewModel.NotifyStatus(status.RunId, status.Message);
    }

    private void OnSharedEstimationWorkerControlAcknowledged(
        object? sender, SharedEstimationWorkerControlAcknowledgement acknowledgement)
    {
        RunsViewModel.NotifyRemoteWorkerAcknowledgement(acknowledgement);
    }

    private void OnSharedEstimationCompleted(object? sender, SharedEstimationCompletion completion)
    {
        RunsViewModel.NotifyRecoveredSharedEstimationCompletion(completion);
        ModelSystemSession? session = null;
        User? user = null;
        IReadOnlyList<(int nodeIndex, string name, double min, double max)>? metadata = null;
        SharedEstimationCompletion? acceptedCompletion;
        lock (_sessionsByRunId)
        {
            bool hasMetadata = _remoteEstimationMetadata.TryGetValue(completion.RunId, out metadata);
            bool hasSession = _sessionsByRunId.TryGetValue(completion.RunId, out var entry);
            if (hasSession)
            {
                session = entry.Session;
                user = entry.User;
            }
            bool hasContext = hasMetadata && hasSession;
            if (!_remoteCompletionGate.TryReceive(completion, hasContext, out acceptedCompletion) ||
                acceptedCompletion is null)
                return;
        }
        if (session is null || user is null || metadata is null)
            return;
        if (!completion.Succeeded)
        {
            RunsViewModel.NotifyError(completion.RunId,
                completion.FailureReason ?? "Remote estimation failed.", String.Empty, null, null);
            return;
        }

        if (metadata is null || completion.BestParameters.Count != metadata.Count)
            return;
        var results = metadata.Select((item, index) => (item.nodeIndex, completion.BestParameters[index])).ToArray();
        RunsViewModel.NotifyOptimizationResults(completion.RunId, session, user, results);
        OptimizationResultsAvailable?.Invoke(completion.RunId, session, results);
        RunsViewModel.NotifyFinished(completion.RunId,
            $"[Estimation] converged after {completion.TotalEvaluations} fitness test(s) " +
            $"in {completion.Iterations} iteration(s). Best fitness = {completion.BestFitness:G6}.");
    }

    private void OnClientOptimizationResultsAvailable(
        object sender, string runID, IReadOnlyList<(int nodeIndex, double value)> results)
    {
        (ModelSystemSession Session, User User) entry;
        lock (_sessionsByRunId)
        {
            if (!_sessionsByRunId.TryGetValue(runID, out entry)) return;
        }
        RunsViewModel.NotifyOptimizationResults(runID, entry.Session, entry.User, results);
        OptimizationResultsAvailable?.Invoke(runID, entry.Session, results);
    }

    /// <summary>
    /// Sends a run command to the model system.
    /// </summary>
    /// <param name="user">The user submitting the run.</param>
    /// <param name="session">The model system session.</param>
    /// <param name="startToExecute">The command to start execution.</param>
    /// <param name="id">The unique ID of the run, null if the command fails.</param>
    /// <param name="error">The error information if the command fails.</param>
    /// <returns>True if the command is successfully sent, false otherwise.</returns>
    public bool SendRun(
        Project projectSession,
        ModelSystemSession msSession,
        User user,
        string startToExecute,
        string runName,
        [NotNullWhen(true)] out string? id,
        [NotNullWhen(false)] out CommandError? error)
        => SendRun(projectSession, msSession, user, startToExecute, runName, RunMode.Normal, LocalEndpointId, out id, out error);

    public bool SendRun(
        Project projectSession,
        ModelSystemSession msSession,
        User user,
        string startToExecute,
        string runName,
        string endpointId,
        [NotNullWhen(true)] out string? id,
        [NotNullWhen(false)] out CommandError? error)
        => SendRun(projectSession, msSession, user, startToExecute, runName, RunMode.Normal, endpointId, out id, out error);

    /// <summary>
    /// Sends an estimation run (Nelder-Mead loop) to the client process.
    /// </summary>
    public bool SendEstimationRun(
        Project projectSession,
        ModelSystemSession msSession,
        User user,
        string startToExecute,
        string runName,
        [NotNullWhen(true)] out string? id,
        [NotNullWhen(false)] out CommandError? error)
        => SendRun(projectSession, msSession, user, startToExecute, runName, RunMode.Estimation, LocalEndpointId, out id, out error);

    public bool SendEstimationRun(
        Project projectSession,
        ModelSystemSession msSession,
        User user,
        string startToExecute,
        string runName,
        string endpointId,
        [NotNullWhen(true)] out string? id,
        [NotNullWhen(false)] out CommandError? error)
        => SendRun(projectSession, msSession, user, startToExecute, runName, RunMode.Estimation, endpointId, out id, out error);

    /// <summary>
    /// Starts an estimation run across the selected connected RunServers.
    /// </summary>
    public bool SendSharedEstimationRun(
        Project projectSession,
        ModelSystemSession msSession,
        User user,
        string startToExecute,
        string runName,
        IReadOnlyList<string> workerEndpointIds,
        IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>>? basicParameterOverridesByWorker,
        [NotNullWhen(true)] out string? id,
        [NotNullWhen(false)] out CommandError? error)
    {
        id = null;
        error = null;
        if (String.IsNullOrWhiteSpace(projectSession.ProjectDirectory))
        {
            error = new CommandError("Project directory is not set.");
            return false;
        }
        if (workerEndpointIds.Count == 0)
        {
            error = new CommandError("At least one shared-estimation worker is required.");
            return false;
        }

        var modelSystem = msSession.ModelSystem;
        var enabledEntries = modelSystem.EstimationGroups
            .Where(group => group.IsEnabled)
            .SelectMany(group => group.Parameters)
            .Where(entry => entry.IsEnabled)
            .ToArray();
        var metadata = msSession.GetOptimizationParameterMeta(RunMode.Estimation);
        if (enabledEntries.Length == 0 || enabledEntries.Length != metadata.Count)
        {
            error = new CommandError("Estimation requires at least one enabled estimation parameter.");
            return false;
        }
        if (modelSystem.EstimationFitnessNode is null)
        {
            error = new CommandError("Estimation requires a fitness node.");
            return false;
        }

        var parameterEntries = enabledEntries
            .Select((entry, index) => (entry, nodeIndex: metadata[index].nodeIndex))
            .ToArray();

        var runId = Guid.NewGuid().ToString();
        var runDirectory = Path.Combine(projectSession.ProjectDirectory, "runs", runName);
        using var modelStream = new MemoryStream();
        if (!msSession.Save(out error, modelStream))
            return false;

        var pool = new SharedEstimationWorkerPool();
        foreach (var endpointId in workerEndpointIds.Distinct(StringComparer.Ordinal))
        {
            if (!_connections.TryGet(endpointId, out var hostBus) || hostBus is null)
            {
                pool.Dispose();
                error = new CommandError($"RunServer '{endpointId}' is not connected.");
                return false;
            }
            if (!pool.AddExistingWorker(endpointId, endpointId, hostBus, out var workerError))
            {
                pool.Dispose();
                error = new CommandError(workerError ?? $"Unable to add RunServer '{endpointId}'.");
                return false;
            }
        }

        var lower = parameterEntries.Select(item => item.entry.Min).ToArray();
        var upper = parameterEntries.Select(item => item.entry.Max).ToArray();
        var initial = parameterEntries.Select(item => item.entry.NullHypothesis).ToArray();
        var algorithm = modelSystem.EstimationAlgorithmConfig.CreateAlgorithm(
            parameterEntries.Length, lower, upper, initial,
            modelSystem.EstimationObjective == EstimationObjective.Maximize);
        var request = new SharedEstimationRunRequest(
            runId, runDirectory, startToExecute, modelStream.ToArray(),
            ProjectId: msSession.Project.Id,
            ModelSystemId: msSession.ModelSystemHeader.Id,
            OwnerUserId: user.UserId);
        if (!pool.StartRun(request, out var startError, basicParameterOverridesByWorker))
        {
            pool.Dispose();
            error = new CommandError(startError ?? "Unable to start shared estimation.");
            return false;
        }

        var serverLabel = $"Shared ({workerEndpointIds.Count} workers)";
        var runViewModel = RunsViewModel.AddRun(runId, runName, runDirectory, serverLabel, msSession, user);
        var cancellation = new CancellationTokenSource();
        runViewModel.SetRunMode(RunMode.Estimation, metadata, () =>
        {
            cancellation.Cancel();
            pool.CancelRun("Cancelled by user.");
        });
        lock (_sessionsByRunId)
            _sessionsByRunId[runId] = (msSession, user);

        _ = Task.Run(() =>
        {
            try
            {
                var runner = new SharedEstimationCoordinatorRun(runId, algorithm, pool.Coordinator,
                    modelSystem.EstimationObjective == EstimationObjective.Maximize,
                    Path.Combine(runDirectory, "estimation_report.csv"),
                    metadata.Select(item => item.name).ToArray());
                var completion = runner.Execute(
                    progress: progress => RunsViewModel.NotifySharedEstimationProgress(progress),
                    cancellationToken: cancellation.Token);
                if (!completion.Succeeded)
                {
                    RunsViewModel.NotifyError(runId, completion.FailureReason ?? "Shared estimation failed.",
                        String.Empty, null, null);
                    return;
                }

                var results = parameterEntries
                    .Select((item, index) => (nodeIndex: item.nodeIndex, value: completion.BestParameters[index]))
                    .ToArray();
                RunsViewModel.NotifyOptimizationResults(runId, msSession, user, results);
                OptimizationResultsAvailable?.Invoke(runId, msSession, results);
                RunsViewModel.NotifyFinished(runId,
                    $"[Estimation] converged after {completion.TotalEvaluations} fitness test(s) " +
                    $"in {completion.Iterations} iteration(s). Best fitness = {completion.BestFitness:G6}.");
            }
            finally
            {
                cancellation.Dispose();
                pool.Dispose();
                lock (_sessionsByRunId)
                    _sessionsByRunId.Remove(runId);
            }
        });

        id = runId;
        return true;
    }

    public bool SendRemoteSharedEstimationRun(
        Project projectSession,
        ModelSystemSession msSession,
        User user,
        string startToExecute,
        string runName,
        string orchestratorEndpointId,
        IReadOnlyList<string> workerEndpointIds,
        IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>>? basicParameterOverridesByWorker,
        IReadOnlyDictionary<string, int>? concurrentRunsByEndpoint,
        [NotNullWhen(true)] out string? id,
        [NotNullWhen(false)] out CommandError? error)
    {
        id = null;
        error = null;
        if (!_connections.TryGet(orchestratorEndpointId, out var orchestrator) || orchestrator is null)
        {
            error = new CommandError($"RunServer '{orchestratorEndpointId}' is not connected.");
            return false;
        }
        var modelSystem = msSession.ModelSystem;
        var enabledEntries = modelSystem.EstimationGroups
            .Where(group => group.IsEnabled)
            .SelectMany(group => group.Parameters)
            .Where(entry => entry.IsEnabled)
            .ToArray();
        var metadata = msSession.GetOptimizationParameterMeta(RunMode.Estimation);
        if (enabledEntries.Length == 0 || enabledEntries.Length != metadata.Count)
        {
            error = new CommandError("Estimation requires at least one enabled estimation parameter.");
            return false;
        }
        if (modelSystem.EstimationFitnessNode is null)
        {
            error = new CommandError("Estimation requires a fitness node.");
            return false;
        }

        var endpoints = GetConnectedRunServers()
            .Where(endpoint => workerEndpointIds.Contains(endpoint.Id, StringComparer.Ordinal) &&
                               endpoint.Id != orchestratorEndpointId)
            .ToArray();
        var invalidWorker = endpoints.FirstOrDefault(endpoint => endpoint.IsLocal ||
            string.IsNullOrWhiteSpace(endpoint.Token) ||
            string.IsNullOrWhiteSpace(endpoint.CertificateFingerprint));
        if (invalidWorker is not null)
        {
            error = new CommandError(endpointError(invalidWorker));
            return false;
        }

        static string endpointError(RunServerEndpoint endpoint)
            => endpoint.IsLocal
                ? $"Local RunServer '{endpoint.Name}' cannot be used as a worker with a remote coordinator. Select a network RunServer instead."
                : $"RunServer '{endpoint.Name}' is missing its security token or certificate fingerprint.";

        using var modelStream = new MemoryStream();
        if (!msSession.Save(out error, modelStream))
            return false;
        var entries = enabledEntries
            .Select((entry, index) => (entry, nodeIndex: metadata[index].nodeIndex))
            .ToArray();
        IReadOnlyDictionary<int, string>? coordinatorOverrides = null;
        if (basicParameterOverridesByWorker is not null)
            basicParameterOverridesByWorker.TryGetValue(orchestratorEndpointId, out coordinatorOverrides);
        var request = new SharedEstimationCoordinatorRequest(
            new SharedEstimationRunRequest(Guid.NewGuid().ToString(),
                Path.Combine(projectSession.ProjectDirectory!, "runs", runName),
                startToExecute, modelStream.ToArray(), coordinatorOverrides,
                msSession.Project.Id, msSession.ModelSystemHeader.Id, user.UserId),
            endpoints.Select(endpoint => new SharedEstimationWorkerEndpoint(
                endpoint.Id, endpoint.Id, endpoint.Address, endpoint.Port, endpoint.Token,
                endpoint.CertificateFingerprint,
                basicParameterOverridesByWorker is not null &&
                basicParameterOverridesByWorker.TryGetValue(endpoint.Id, out var overrides)
                    ? overrides
                    : null,
                concurrentRunsByEndpoint is not null &&
                concurrentRunsByEndpoint.TryGetValue(endpoint.Id, out var concurrentRuns)
                    ? concurrentRuns
                    : 1)).ToArray(),
            modelSystem.EstimationAlgorithmConfig.AlgorithmId,
            modelSystem.EstimationAlgorithmConfig.GetParameters(),
            entries.Select(item => item.entry.Min).ToArray(),
            entries.Select(item => item.entry.Max).ToArray(),
            entries.Select(item => item.entry.NullHypothesis).ToArray(),
            modelSystem.EstimationObjective == EstimationObjective.Maximize,
            UseCoordinatorAsWorker: true,
            Parameters: metadata.Select(item => new SharedEstimationParameterMetadata(
                item.nodeIndex, item.name, item.min, item.max)).ToArray(),
            CoordinatorConcurrentRuns: concurrentRunsByEndpoint is not null &&
                concurrentRunsByEndpoint.TryGetValue(orchestratorEndpointId, out var coordinatorConcurrentRuns)
                    ? coordinatorConcurrentRuns
                    : 1);

        if (!orchestrator.StartRemoteSharedEstimation(request, out error))
            return false;

        id = request.Run.RunId;
        var submittedRunId = request.Run.RunId;
        var runDirectory = request.Run.WorkingDirectory;
        var runViewModel = RunsViewModel.AddRun(id, runName, runDirectory,
            $"{orchestratorEndpointId} (coordinator + {endpoints.Length} remote worker(s))", msSession, user);
        runViewModel.SetRemoteRunTracking(orchestratorEndpointId != LocalEndpointId);
        runViewModel.SetRunMode(RunMode.Estimation, metadata, () =>
        {
            orchestrator.CancelSharedEstimation(submittedRunId, "Cancelled by user.", out _);
        });
        var initialWorkers = request.Workers.ToDictionary(worker => worker.WorkerId,
            worker => worker, StringComparer.Ordinal);
        var availableWorkers = GetConnectedRunServers()
            .Where(endpoint => endpoint.Id != orchestratorEndpointId)
            .Select(endpoint => initialWorkers.TryGetValue(endpoint.Id, out var initial)
                ? initial
                : new SharedEstimationWorkerEndpoint(endpoint.Id, endpoint.Id, endpoint.Address,
                    endpoint.Port, endpoint.Token, endpoint.CertificateFingerprint))
            .ToArray();
        lock (_sessionsByRunId)
        {
            _hostBusesByRunId[submittedRunId] = orchestrator;
            _remoteWorkerEndpointsByRunId[submittedRunId] = availableWorkers.ToDictionary(worker => worker.WorkerId,
                worker => worker, StringComparer.Ordinal);
        }
        var workerRows = availableWorkers.Select(ToRunServerEndpoint).ToList();
        var configuredWorkerCounts = availableWorkers.ToDictionary(worker => worker.WorkerId,
            worker => worker.ConcurrentRuns, StringComparer.Ordinal);
        if (request.UseCoordinatorAsWorker)
        {
            var coordinatorEndpoint = GetConnectedRunServers()
                .FirstOrDefault(candidate => candidate.Id == orchestratorEndpointId);
            workerRows.Add(ToCoordinatorRunServerEndpoint(coordinatorEndpoint, orchestratorEndpointId));
            configuredWorkerCounts[RemoteEstimationWorkerViewModel.CoordinatorWorkerId] =
                request.CoordinatorConcurrentRuns;
        }
        runViewModel.SetRemoteEstimationWorkers(
            workerRows,
            initialWorkers.Keys.ToArray(),
            workerId => ChangeRemoteEstimationWorker(submittedRunId, workerId, add: true),
            workerId => ChangeRemoteEstimationWorker(submittedRunId, workerId, add: false),
            configuredWorkerCounts);
        SharedEstimationCompletion? pendingCompletion;
        lock (_sessionsByRunId)
        {
            _sessionsByRunId[id] = (msSession, user);
            _remoteEstimationMetadata[id] = metadata;
            pendingCompletion = _remoteCompletionGate.MarkReady(id);
        }
        if (pendingCompletion is not null)
            OnSharedEstimationCompleted(orchestrator, pendingCompletion);
        return true;
    }

    private static RunServerEndpoint ToRunServerEndpoint(SharedEstimationWorkerEndpoint endpoint)
        => new()
        {
            Id = endpoint.EndpointId,
            Name = Settings.Default.RunServers.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, endpoint.EndpointId, StringComparison.Ordinal))?.Name
                ?? endpoint.EndpointId,
            Address = endpoint.Address,
            Port = endpoint.Port,
            Token = endpoint.Token,
            CertificateFingerprint = endpoint.CertificateFingerprint
        };

    private static RunServerEndpoint ToCoordinatorRunServerEndpoint(
        RunServerEndpoint? endpoint, string fallbackName)
        => new()
        {
            Id = RemoteEstimationWorkerViewModel.CoordinatorWorkerId,
            Name = $"Coordinator ({endpoint?.Name ?? fallbackName})",
            Address = endpoint?.Address ?? fallbackName,
            Port = endpoint?.Port ?? 0,
            IsLocal = endpoint?.IsLocal ?? false
        };

    private string? ChangeRemoteEstimationWorker(string runId, string workerId, bool add)
    {
        lock (_sessionsByRunId)
        {
            if (!_hostBusesByRunId.TryGetValue(runId, out var bus) ||
                !_remoteWorkerEndpointsByRunId.TryGetValue(runId, out var workers) ||
                !workers.TryGetValue(workerId, out var worker))
                return "The remote estimation connection or worker is unavailable.";
            var sent = add
                ? bus.AddRemoteEstimationWorker(runId, worker, out var error)
                : bus.RemoveRemoteEstimationWorker(runId, worker, out error);
            return sent ? null : error?.Message ?? "Unable to send the worker change.";
        }
    }

    /// <summary>
    /// Sends a calibration run (proportional-update loop) to the client process.
    /// </summary>
    public bool SendCalibrationRun(
        Project projectSession,
        ModelSystemSession msSession,
        User user,
        string startToExecute,
        string runName,
        [NotNullWhen(true)] out string? id,
        [NotNullWhen(false)] out CommandError? error)
        => SendRun(projectSession, msSession, user, startToExecute, runName, RunMode.Calibration, LocalEndpointId, out id, out error);

    public bool SendCalibrationRun(
        Project projectSession,
        ModelSystemSession msSession,
        User user,
        string startToExecute,
        string runName,
        string endpointId,
        [NotNullWhen(true)] out string? id,
        [NotNullWhen(false)] out CommandError? error)
        => SendRun(projectSession, msSession, user, startToExecute, runName, RunMode.Calibration, endpointId, out id, out error);

    private bool SendRun(
        Project projectSession,
        ModelSystemSession msSession,
        User user,
        string startToExecute,
        string runName,
        RunMode runMode,
        string endpointId,
        [NotNullWhen(true)] out string? id,
        [NotNullWhen(false)] out CommandError? error)
    {
        var projectDirectory = projectSession.ProjectDirectory;
        if (String.IsNullOrWhiteSpace(projectDirectory))
        {
            id = null;
            error = new CommandError("Project directory is not set.");
            return false;
        }
        var runDirectory = Path.Combine(projectDirectory, "runs", runName);
        if (!_connections.TryGet(endpointId, out var hostBus) || hostBus is null)
        {
            id = null;
            error = new CommandError($"RunServer '{endpointId}' is not connected.");
            return false;
        }
        var endpoint = Settings.Default.RunServers.FirstOrDefault(endpoint => endpoint.Id == endpointId);
        var serverLabel = endpoint is null
            ? endpointId
            : endpoint.Port > 0
                ? $"{endpoint.Name} ({endpoint.Address}:{endpoint.Port})"
                : $"{endpoint.Name} ({endpoint.Address})";
        RunViewModel? runViewModel = null;
        if (!hostBus.RunModelSystem(
                msSession,
                runDirectory,
                startToExecute,
                runMode,
                runId =>
                {
                    runViewModel = RunsViewModel.AddRun(runId, runName, runDirectory, serverLabel, msSession, user);
                },
                out id,
                out error,
                runName,
                user.UserId))
        {
            return false;
        }
        lock (_sessionsByRunId)
        {
            _sessionsByRunId[id] = (msSession, user);
            _hostBusesByRunId[id] = hostBus;
            _runDirectoriesByRunId[id] = runDirectory;
            if (endpointId != LocalEndpointId)
                _remoteRunBusesByRunId[id] = hostBus;
        }
        runViewModel?.SetRemoteRunTracking(endpointId != LocalEndpointId);
        if (runMode != RunMode.Normal)
        {
            // Extract parameter metadata so the progress dialog can show names/bounds.
            var meta = msSession.GetOptimizationParameterMeta(runMode);
            var runId = id;
            runViewModel?.SetRunMode(runMode, meta, () => hostBus.CancelModelRun(runId, out _));
        }
        return true;
    }

    private static bool TryParseListeningPort(string? line, out int port)
    {
        port = 0;
        const string prefix = "RunServer listening on ";
        if (line is null || !line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var endpoint = line[prefix.Length..];
        var separator = endpoint.LastIndexOf(':');
        return separator >= 0 && int.TryParse(endpoint[(separator + 1)..], out port) && port is > 0 and <= 65535;
    }

    private RunController(XTMFRuntime runtime, HostBus hostBus, Process? runServerProcess = null)
    {
        Runtime = runtime;
        _hostBus = hostBus;
        _runServerProcess = runServerProcess;
        _connections = new RunServerConnectionManager();
        _connections.StateChanged += OnRunServerConnectionStateChanged;
        _connections.ConnectionAvailable += SubscribeToHostBus;
        _connections.AddConnection(RunServerEndpoint.CreateLocal(), hostBus, out _);
    }

    /// <summary>
    /// Connects to an externally managed TCP RunServer and makes it available for routing.
    /// </summary>
    public bool ConnectRunServer(RunServerEndpoint endpoint, out string? error)
    {
        if (!endpoint.Enabled)
        {
            error = $"RunServer '{endpoint.Name}' is disabled.";
            return false;
        }

        if (!_connections.Connect(endpoint, out error))
            return false;

        return true;
    }

    private void ConnectConfiguredRunServers()
    {
        var configuredEndpoints = Settings.Default.RunServers
            .Where(endpoint => !endpoint.IsLocal && endpoint.Port != 0)
            .ToDictionary(endpoint => endpoint.Id, StringComparer.Ordinal);

        foreach (var state in _connections.GetStates())
        {
            if (!state.Endpoint.IsLocal &&
                (!configuredEndpoints.TryGetValue(state.Endpoint.Id, out var endpoint) || !endpoint.Enabled))
                _connections.Remove(state.Endpoint.Id);
        }

        foreach (var endpoint in Settings.Default.RunServers)
        {
            if (endpoint.IsLocal || endpoint.Port == 0 || !endpoint.Enabled)
                continue;

            try
            {
                ConnectRunServer(endpoint, out _);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RunController] Failed to connect to RunServer '{endpoint.Name}': {ex.Message}");
            }
        }
    }

    public void RefreshConfiguredRunServers()
        => ConnectConfiguredRunServers();

    private void OnRunServerConnectionStateChanged(RunServerConnectionInfo state)
    {
        RunServerStateChanged?.Invoke(state);
        if (state.Endpoint.IsLocal)
            return;

        if (state.State == RunServerConnectionState.Disconnected)
        {
            lock (_sessionsByRunId)
                _disconnectedRunServerIds.Add(state.Endpoint.Id);
            foreach (var runId in GetEstimationRunsForWorker(state.Endpoint.Id))
                RunsViewModel.NotifyRemoteWorkerDisconnected(runId, state.Endpoint.Id);
            return;
        }

        if (state.State != RunServerConnectionState.Available)
            return;

        bool wasDisconnected;
        lock (_sessionsByRunId)
            wasDisconnected = _disconnectedRunServerIds.Remove(state.Endpoint.Id);
        if (!wasDisconnected)
            return;

        foreach (var runId in GetEstimationRunsForWorker(state.Endpoint.Id))
            RunsViewModel.ReconnectRemoteWorker(runId, state.Endpoint.Id);
    }

    private string[] GetEstimationRunsForWorker(string endpointId)
    {
        lock (_sessionsByRunId)
            return _remoteWorkerEndpointsByRunId
                .Where(entry => entry.Value.ContainsKey(endpointId))
                .Select(entry => entry.Key)
                .ToArray();
    }

    public bool TryGetRunServer(string endpointId, out HostBus? hostBus)
        => _connections.TryGet(endpointId, out hostBus);

    public IReadOnlyList<RunServerEndpoint> GetConnectedRunServers()
    {
        return _connections.GetStates()
            .Where(state => state.State == RunServerConnectionState.Available)
            .Select(state => state.Endpoint.Clone())
            .ToArray();
    }

    public IReadOnlyList<RunServerConnectionInfo> GetRunServerStates()
        => _connections.GetStates();

    public async Task<IReadOnlyList<RunServerActivityServerSnapshot>> QueryRunServerActivityAsync(
        CancellationToken cancellationToken = default)
    {
        var states = _connections.GetStates();
        var requests = states.Select(async state =>
        {
            if (state.State != RunServerConnectionState.Available ||
                !_connections.TryGet(state.Endpoint.Id, out var hostBus) || hostBus is null)
            {
                return new RunServerActivityServerSnapshot(state.Endpoint, state.State,
                    state.Error ?? (state.State == RunServerConnectionState.Connecting ? "Connecting" : "Not connected"),
                    Array.Empty<RunServerActivity>());
            }

            try
            {
                var response = await hostBus.QueryServerActivityAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                return new RunServerActivityServerSnapshot(state.Endpoint, state.State, null, response.Activities);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new RunServerActivityServerSnapshot(state.Endpoint, state.State,
                    "Timed out waiting for RunServer activity.", Array.Empty<RunServerActivity>());
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new RunServerActivityServerSnapshot(state.Endpoint, state.State,
                    exception.Message, Array.Empty<RunServerActivity>());
            }
        }).ToArray();

        return await Task.WhenAll(requests).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RunServerActivity>> QueryLocalRunServerActivityAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_connections.TryGet(LocalEndpointId, out var hostBus) || hostBus is null)
            throw new IOException("The local RunServer is not connected.");

        var response = await hostBus.QueryServerActivityAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return response.Activities;
    }

    public async Task<RunServerDrainStatus> BeginRunServerDeploymentDrainAsync(
        string endpointId, CancellationToken cancellationToken = default)
    {
        if (!_connections.TryGet(endpointId, out var hostBus) || hostBus is null)
            throw new IOException($"RunServer '{endpointId}' is not connected.");
        return await hostBus.BeginDeploymentDrainAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<RunServerDrainStatus> StageLocalRunServerDeploymentAsync(
        string endpointId, IReadOnlyList<string> selectedModules,
        CancellationToken cancellationToken = default)
    {
        if (!_connections.TryGet(endpointId, out var hostBus) || hostBus is null)
            throw new IOException($"RunServer '{endpointId}' is not connected.");

        var archive = RunServerDeploymentBuilder.CreateLocalArchive(selectedModules);
        var status = await hostBus.BeginDeploymentDrainAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (status.State != RunServerDrainState.Idle)
            return status;
        var staged = await hostBus.SendDeploymentArchiveAsync(status.RequestId, archive.Content,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (staged.State != RunServerDrainState.Staged)
            return staged;
        if (!hostBus.ActivateDeployment(staged.RequestId, out var error))
            throw new IOException(error?.Message ?? "Unable to activate the staged RunServer deployment.");
        return staged;
    }

    public bool KillRunServerActivity(string endpointId, RunServerActivity activity, out string? error)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (!_connections.TryGet(endpointId, out var hostBus) || hostBus is null)
        {
            error = $"RunServer '{endpointId}' is not connected.";
            return false;
        }

        if (activity.Kind is "Shared estimation coordinator" or "Shared estimation worker")
        {
            var sent = hostBus.CancelSharedEstimation(activity.RunId,
                "Stopped from RunServer Activity window.", out var commandError);
            error = commandError?.Message;
            return sent;
        }
        var killed = hostBus.KillModelRun(activity.RunId, out var killError);
        error = killError?.Message;
        return killed;
    }

    private void SubscribeToHostBus(HostBus hostBus)
    {
        lock (_hostBusSubscriptionLock)
        {
            if (!_subscribedHostBuses.Add(hostBus))
                return;
        }

        hostBus.ClientReportedStatus += OnClientReportedStatus;
        hostBus.ClientFinishedModelSystem += OnClientFinishedModelSystem;
        hostBus.ClientErrorWhenRunningModelSystem += OnClientErrorWhenRunningModelSystem;
        hostBus.ClientOptimizationResultsAvailable += OnClientOptimizationResultsAvailable;
        hostBus.ClientIterationProgressAvailable += OnClientIterationProgressAvailable;
        hostBus.SharedEstimationProgressAvailable += OnSharedEstimationProgress;
        hostBus.SharedEstimationCompleted += OnSharedEstimationCompleted;
        hostBus.SharedEstimationWorkerControlAcknowledged += OnSharedEstimationWorkerControlAcknowledged;
        hostBus.SharedEstimationStatusAvailable += OnSharedEstimationStatus;
        hostBus.SharedEstimationJobSnapshotsAvailable += OnSharedEstimationJobSnapshotsAvailable;
        hostBus.RemoteRunSnapshotsAvailable += OnRemoteRunSnapshotsAvailable;
        hostBus.RemoteRunArtifactsAvailable += OnRemoteRunArtifactsAvailable;
        hostBus.ClientRunArtifactsReceived += OnClientRunArtifactsReceived;
        hostBus.Disconnected += OnHostBusDisconnected;
        hostBus.QuerySharedEstimationJobs(out _);
        hostBus.QueryRemoteRuns(out _);
    }

    private void OnSharedEstimationJobSnapshotsAvailable(
        object? sender, IReadOnlyList<SharedEstimationJobSnapshot> snapshots)
    {
        var hostBus = sender as HostBus;
        var endpoint = hostBus is not null && _connections.TryGetEndpoint(hostBus, out var matchedEndpoint)
            ? matchedEndpoint
            : null;
        foreach (var snapshot in snapshots)
        {
            lock (_sessionsByRunId)
            {
                _recoveredSharedEstimationRuns[snapshot.RunId] = snapshot;
                if (hostBus is not null)
                    _hostBusesByRunId[snapshot.RunId] = hostBus;
            }
            if (!IsOwnedByCurrentUser(snapshot.OwnerUserId))
                continue;
            var isRemoteRun = endpoint is null || !endpoint.IsLocal;
            var runServerName = endpoint?.Name ?? "Remote RunServer";
            var run = RunsViewModel.RestoreSharedEstimationRun(snapshot, runServerName,
                () => hostBus?.CancelSharedEstimation(snapshot.RunId, "Cancelled by user.", out _));
            run.SetRemoteRunTracking(isRemoteRun);
            if (!isRemoteRun)
                run.SetLocalOutputDirectory(snapshot.WorkingDirectory);
            var workers = GetConnectedRunServers()
                .Where(worker => endpoint is null || worker.Id != endpoint.Id)
                .Select(worker => new SharedEstimationWorkerEndpoint(worker.Id, worker.Id,
                    worker.Address, worker.Port, worker.Token, worker.CertificateFingerprint))
                .ToArray();
            lock (_sessionsByRunId)
                _remoteWorkerEndpointsByRunId[snapshot.RunId] = workers.ToDictionary(
                    worker => worker.WorkerId, worker => worker, StringComparer.Ordinal);
            var workerRows = workers.Select(ToRunServerEndpoint).ToList();
            if (snapshot.ConfiguredWorkerCounts?.ContainsKey(
                    RemoteEstimationWorkerViewModel.CoordinatorWorkerId) == true)
                workerRows.Add(ToCoordinatorRunServerEndpoint(endpoint, runServerName));
            RunsViewModel.ConfigureRecoveredSharedEstimationWorkers(snapshot.RunId,
                workerRows,
                snapshot.ActiveWorkerIds ?? Array.Empty<string>(),
                workerId => ChangeRemoteEstimationWorker(snapshot.RunId, workerId, add: true),
                workerId => ChangeRemoteEstimationWorker(snapshot.RunId, workerId, add: false),
                snapshot.ConfiguredWorkerCounts);
            if (snapshot.Completion is not null)
                OnSharedEstimationCompleted(sender, snapshot.Completion);
            TryAutomaticallyBindRecoveredRun(snapshot.RunId);
        }
    }

    private void OnRemoteRunSnapshotsAvailable(object? sender, IReadOnlyList<RemoteRunSnapshot> snapshots)
    {
        var hostBus = sender as HostBus;
        var endpoint = hostBus is not null && _connections.TryGetEndpoint(hostBus, out var matchedEndpoint)
            ? matchedEndpoint
            : null;
        foreach (var snapshot in snapshots)
        {
            string? localRunDirectory = null;
            lock (_sessionsByRunId)
            {
                _recoveredRemoteRuns[snapshot.RunId] = snapshot;
                if (hostBus is not null)
                    _remoteRunBusesByRunId[snapshot.RunId] = hostBus;
                if (_sessionsByRunId.ContainsKey(snapshot.RunId))
                    _runDirectoriesByRunId.TryGetValue(snapshot.RunId, out localRunDirectory);
            }
            if (!IsOwnedByCurrentUser(snapshot.OwnerUserId))
                continue;
            var isRemoteRun = endpoint is null || !endpoint.IsLocal;
            var run = RunsViewModel.RestoreRemoteRun(snapshot, endpoint?.Name ?? "Remote RunServer",
                () => hostBus?.CancelModelRun(snapshot.RunId, out _));
            run.SetRemoteRunTracking(isRemoteRun);
            if (isRemoteRun)
            {
                RunsViewModel.NotifyRemoteOutputAvailability(snapshot.RunId, snapshot.ArtifactsAvailable);
            }
            else
            {
                var localOutputDirectory = localRunDirectory ?? snapshot.WorkingDirectory;
                run.SetLocalOutputDirectory(localOutputDirectory);
            }
            if (localRunDirectory is not null && hostBus is not null)
                ReceiveBoundRemoteCompletion(run, snapshot, hostBus, localRunDirectory);
            TryAutomaticallyBindRecoveredRun(snapshot.RunId);
        }
    }

    public void EnableAutomaticRecoveredRunBinding(User user)
    {
        _recoveryUser = user ?? throw new ArgumentNullException(nameof(user));
        RemoteRunSnapshot[] remoteSnapshots;
        SharedEstimationJobSnapshot[] sharedSnapshots;
        lock (_sessionsByRunId)
        {
            remoteSnapshots = _recoveredRemoteRuns.Values.ToArray();
            sharedSnapshots = _recoveredSharedEstimationRuns.Values.ToArray();
        }
        foreach (var snapshot in remoteSnapshots)
        {
            HostBus? hostBus;
            lock (_sessionsByRunId)
                _remoteRunBusesByRunId.TryGetValue(snapshot.RunId, out hostBus);
            OnRemoteRunSnapshotsAvailable(hostBus, [snapshot]);
        }
        foreach (var snapshot in sharedSnapshots)
        {
            HostBus? hostBus;
            lock (_sessionsByRunId)
                _hostBusesByRunId.TryGetValue(snapshot.RunId, out hostBus);
            OnSharedEstimationJobSnapshotsAvailable(hostBus, [snapshot]);
        }
    }

    private bool IsOwnedByCurrentUser(Guid? ownerUserId)
        => ownerUserId is not null && _recoveryUser is not null && ownerUserId == _recoveryUser.UserId;

    private void TryAutomaticallyBindRecoveredRun(string runId)
    {
        var user = _recoveryUser;
        if (user is null)
            return;

        Guid? projectId;
        Guid? modelSystemId;
        lock (_sessionsByRunId)
        {
            if (!_automaticBindingAttempts.Add(runId))
                return;
            if (_recoveredRemoteRuns.TryGetValue(runId, out var remoteSnapshot))
            {
                projectId = remoteSnapshot.ProjectId;
                modelSystemId = remoteSnapshot.ModelSystemId;
            }
            else if (_recoveredSharedEstimationRuns.TryGetValue(runId, out var estimationSnapshot))
            {
                projectId = estimationSnapshot.ProjectId;
                modelSystemId = estimationSnapshot.ModelSystemId;
            }
            else
            {
                return;
            }
        }
        if (projectId is null || modelSystemId is null)
            return;

        var projects = ProjectController.GetProjects(user).Where(project => project.Id == projectId).ToArray();
        if (projects.Length != 1)
            return;
        var headers = projects[0].ModelSystems.Where(header => header.Id == modelSystemId).ToArray();
        if (headers.Length != 1 ||
            !Runtime.ProjectController.GetProjectSession(user, projects[0], out var projectSession, out _))
            return;

        using (projectSession)
        {
            if (!projectSession.EditModelSystem(user, headers[0], out var modelSystemSession, out _, out _) ||
                modelSystemSession is null)
                return;
            if (!BindRecoveredRemoteRun(runId, modelSystemSession, user, out _))
                modelSystemSession.Dispose();
        }
    }

    private void ReceiveBoundRemoteCompletion(RunViewModel run, RemoteRunSnapshot snapshot,
        HostBus hostBus, string localRunDirectory)
    {
        try
        {
            PersistRemoteRunSnapshot(snapshot);
            if (snapshot.ArtifactsAvailable)
            {
                lock (_sessionsByRunId)
                    _pendingRemoteReceipts[snapshot.RunId] = (hostBus, localRunDirectory);
                RunsViewModel.NotifyRemoteReceiptPending(snapshot.RunId, true);
                if (!hostBus.RequestRemoteRunArtifacts(snapshot.RunId, out var requestError))
                    throw new IOException(requestError?.Message ?? "Unable to request remote run artifacts.");
            }
            else if (!hostBus.AcknowledgeRemoteRun(snapshot.RunId, out var acknowledgeError))
            {
                throw new IOException(acknowledgeError?.Message ?? "Unable to acknowledge the remote completion.");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            run.AppendStatus($"Remote completion receipt is pending: {exception.Message}");
        }
    }

    public bool BindRecoveredRemoteRun(string runId, ModelSystemSession session, User user, out string? error)
    {
        RemoteRunSnapshot? snapshot;
        SharedEstimationJobSnapshot? sharedSnapshot;
        lock (_sessionsByRunId)
        {
            _recoveredRemoteRuns.TryGetValue(runId, out snapshot);
            _recoveredSharedEstimationRuns.TryGetValue(runId, out sharedSnapshot);
        }
        if (snapshot is null && sharedSnapshot is not null)
            return BindRecoveredSharedEstimationRun(runId, sharedSnapshot, session, user, out error);
        if (snapshot is null)
        {
            error = "The remote run snapshot is no longer available.";
            return false;
        }

        using var modelStream = new MemoryStream();
        if (!session.Save(out var saveError, modelStream))
        {
            error = saveError?.Message ?? "Unable to serialize the selected model system.";
            return false;
        }
        var modelHash = Convert.ToHexString(SHA256.HashData(modelStream.ToArray()));
        if (!string.Equals(modelHash, snapshot.ModelSystemHash, StringComparison.OrdinalIgnoreCase))
        {
            error = "The selected model system does not match the model system used by this remote run.";
            return false;
        }

        var projectDirectory = session.Project.ProjectDirectory;
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            error = "The selected project does not have a local directory.";
            return false;
        }
        var localRunName = Path.GetFileName(snapshot.RunName.Replace('\\', '/').Trim('/'));
        if (string.IsNullOrWhiteSpace(localRunName) || localRunName is "." or "..")
            localRunName = runId;
        var cachedOutputDirectory = RemoteRunOutputPaths.GetLocalDirectory(runId);
        var targetDirectory = !snapshot.ArtifactsAvailable && Directory.Exists(cachedOutputDirectory)
            ? cachedOutputDirectory
            : Path.Combine(projectDirectory, "runs", localRunName);
        HostBus? hostBus;
        lock (_sessionsByRunId)
        {
            _remoteRunBusesByRunId.TryGetValue(runId, out hostBus);
            _runDirectoriesByRunId[runId] = targetDirectory;
            if (hostBus is not null)
                _pendingRemoteReceipts[runId] = (hostBus, targetDirectory);
        }
        if (!RunsViewModel.SetRecoveredRunOutputDirectory(runId, targetDirectory))
        {
            error = "The recovered run is not present in the Runs list.";
            return false;
        }
        if (hostBus is null)
        {
            error = "The RunServer connection for this run is no longer available.";
            return false;
        }
        try
        {
            PersistRemoteRunSnapshot(snapshot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = $"Unable to persist the remote run receipt: {exception.Message}";
            return false;
        }
        if (snapshot.ArtifactsAvailable)
        {
            lock (_sessionsByRunId)
                _pendingRemoteReceipts[runId] = (hostBus, targetDirectory);
            if (!hostBus.RequestRemoteRunArtifacts(runId, out var requestError))
            {
                error = requestError?.Message ?? "Unable to request remote run artifacts.";
                return false;
            }
        }
        else
        {
            if (!hostBus.AcknowledgeRemoteRun(runId, out var acknowledgeError))
            {
                error = acknowledgeError?.Message ?? "Unable to acknowledge the remote completion.";
                return false;
            }
        }
        var metadata = session.GetOptimizationParameterMeta(snapshot.RunMode);
        if (!RunsViewModel.BindRecoveredRun(runId, session, user, metadata))
        {
            error = "The recovered run is not present in the Runs list.";
            return false;
        }
        lock (_sessionsByRunId)
            _sessionsByRunId[runId] = (session, user);
        error = null;
        return true;
    }

    private bool BindRecoveredSharedEstimationRun(string runId, SharedEstimationJobSnapshot snapshot,
        ModelSystemSession session, User user, out string? error)
    {
        using var modelStream = new MemoryStream();
        if (!session.Save(out var saveError, modelStream))
        {
            error = saveError?.Message ?? "Unable to serialize the selected model system.";
            return false;
        }
        var modelHash = Convert.ToHexString(SHA256.HashData(modelStream.ToArray()));
        if (!string.Equals(modelHash, snapshot.ModelSystemHash, StringComparison.OrdinalIgnoreCase))
        {
            error = "The selected model system does not match the model system used by this remote estimation.";
            return false;
        }
        var metadata = session.GetOptimizationParameterMeta(RunMode.Estimation);
        if (snapshot.Parameters is { Count: > 0 } remoteParameters &&
            (remoteParameters.Count != metadata.Count ||
             remoteParameters.Where((parameter, index) => parameter.NodeIndex != metadata[index].nodeIndex).Any()))
        {
            error = "The selected model system's estimation parameters do not match the remote run.";
            return false;
        }
        if (!RunsViewModel.BindRecoveredRun(runId, session, user, metadata))
        {
            error = "The recovered estimation is not present in the Runs list.";
            return false;
        }
        SharedEstimationCompletion? pendingCompletion;
        lock (_sessionsByRunId)
        {
            _sessionsByRunId[runId] = (session, user);
            _remoteEstimationMetadata[runId] = metadata;
            pendingCompletion = _remoteCompletionGate.MarkReady(runId);
        }
        if (pendingCompletion is not null)
            OnSharedEstimationCompleted(_hostBusesByRunId.GetValueOrDefault(runId), pendingCompletion);
        error = null;
        return true;
    }

    public bool RetryRemoteRunReceipt(string runId, out string? error)
    {
        (HostBus HostBus, string TargetDirectory) receipt;
        lock (_sessionsByRunId)
        {
            if (!_pendingRemoteReceipts.TryGetValue(runId, out receipt))
            {
                error = "There is no pending remote receipt for this run.";
                return false;
            }
        }
        if (!receipt.HostBus.RequestRemoteRunArtifacts(runId, out var requestError))
        {
            error = requestError?.Message ?? "Unable to request remote run artifacts.";
            return false;
        }
        error = null;
        return true;
    }

    public bool RequestRemoteRunOutputTransfer(string runId, out string? error)
    {
        HostBus? hostBus;
        string targetDirectory;
        lock (_sessionsByRunId)
        {
            _remoteRunBusesByRunId.TryGetValue(runId, out hostBus);
            if (hostBus is null)
                _hostBusesByRunId.TryGetValue(runId, out hostBus);
            if (_runDirectoriesByRunId.TryGetValue(runId, out var knownDirectory))
                targetDirectory = knownDirectory;
            else
                targetDirectory = RemoteRunOutputPaths.GetLocalDirectory(runId);
            if (hostBus is not null)
                _pendingRemoteReceipts[runId] = (hostBus, targetDirectory);
        }

        if (hostBus is null)
        {
            error = "The RunServer connection for this run is no longer available.";
            return false;
        }
        RunsViewModel.NotifyRemoteReceiptPending(runId, true);
        if (!hostBus.RequestRemoteRunArtifacts(runId, out var requestError))
        {
            RunsViewModel.NotifyRemoteReceiptPending(runId, false);
            error = requestError?.Message ?? "Unable to request remote run output.";
            return false;
        }
        error = null;
        return true;
    }

    public async Task<RemoteRunDeletionResponse> DeleteRemoteRunAsync(string runId,
        CancellationToken cancellationToken = default)
    {
        HostBus? hostBus;
        lock (_sessionsByRunId)
        {
            _remoteRunBusesByRunId.TryGetValue(runId, out hostBus);
            if (hostBus is null)
                _hostBusesByRunId.TryGetValue(runId, out hostBus);
        }
        if (hostBus is null)
            return new RemoteRunDeletionResponse(string.Empty, runId, false,
                "The RunServer connection for this run is no longer available.");

        var result = await hostBus.DeleteRemoteRunAsync(runId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (result.IsDeletedOrNotFound)
        {
            lock (_sessionsByRunId)
            {
                _recoveredRemoteRuns.Remove(runId);
                _remoteRunBusesByRunId.Remove(runId);
                _hostBusesByRunId.Remove(runId);
                _runDirectoriesByRunId.Remove(runId);
                _pendingRemoteReceipts.Remove(runId);
                _sessionsByRunId.Remove(runId);
            }
        }
        return result;
    }

    private static void PersistRemoteRunSnapshot(RemoteRunSnapshot snapshot)
    {
        var receiptDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XTMF2", "GUI", "RemoteRunReceipts");
        Directory.CreateDirectory(receiptDirectory);
        var key = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(snapshot.RunId)));
        var path = Path.Combine(receiptDirectory, $"{key}.json");
        var temporaryPath = path + ".tmp";
        var options = new System.Text.Json.JsonSerializerOptions
        {
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
        };
        File.WriteAllBytes(temporaryPath, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(snapshot, options));
        File.Move(temporaryPath, path, true);
    }

    private void OnRemoteRunArtifactsAvailable(object? sender, RemoteRunArtifactsResponse response)
    {
        (HostBus HostBus, string TargetDirectory) receipt;
        RemoteRunSnapshot? snapshot;
        lock (_sessionsByRunId)
        {
            if (!_pendingRemoteReceipts.TryGetValue(response.RunId, out receipt))
                return;
            _recoveredRemoteRuns.TryGetValue(response.RunId, out snapshot);
        }
        if (response.Archive is null)
        {
            RunsViewModel.NotifyArtifactTransferFailed(response.RunId,
                response.Error ?? "The RunServer returned no artifact archive.");
            return;
        }

        try
        {
            using var archiveStream = new MemoryStream(response.Archive, writable: false);
            ExtractRunArtifacts(archiveStream, receipt.TargetDirectory);
            RunsViewModel.NotifyArtifactsTransferred(response.RunId);
            if (snapshot is not null)
                PersistRemoteRunSnapshot(snapshot with { ArtifactsAvailable = false });
            if (!receipt.HostBus.AcknowledgeRemoteRun(response.RunId, out var error))
                throw new IOException(error?.Message ?? "Unable to acknowledge the remote completion.");
            lock (_sessionsByRunId)
            {
                _pendingRemoteReceipts.Remove(response.RunId);
                if (snapshot is not null)
                    _recoveredRemoteRuns[response.RunId] = snapshot with { ArtifactsAvailable = false };
            }
            RunsViewModel.NotifyRemoteReceiptCompleted(response.RunId);
            RunsViewModel.NotifyArtifactsTransferred(response.RunId);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            RunsViewModel.NotifyArtifactTransferFailed(response.RunId, exception.Message);
        }
    }

    private static void ExtractRunArtifacts(Stream archiveStream, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        var root = Path.GetFullPath(targetDirectory);
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var entry in archive.Entries)
        {
            var destination = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!destination.StartsWith(rootWithSeparator, StringComparison.Ordinal)
                && !string.Equals(destination, root, StringComparison.Ordinal))
                throw new InvalidDataException("The RunServer returned an invalid artifact path.");
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, true);
        }
    }

    private void OnHostBusDisconnected(object? sender, EventArgs e)
    {
        if (sender is HostBus hostBus)
        {
            string[] affectedRuns;
            lock (_sessionsByRunId)
                affectedRuns = _hostBusesByRunId
                    .Where(entry => ReferenceEquals(entry.Value, hostBus))
                    .Select(entry => entry.Key)
                    .ToArray();
            foreach (var runId in affectedRuns)
                RunsViewModel.NotifyConnectionLost(runId);
            UnsubscribeFromHostBus(hostBus);
        }
    }

    private void UnsubscribeFromHostBus(HostBus hostBus)
    {
        lock (_hostBusSubscriptionLock)
        {
            if (!_subscribedHostBuses.Remove(hostBus))
                return;
        }

        hostBus.ClientReportedStatus -= OnClientReportedStatus;
        hostBus.ClientFinishedModelSystem -= OnClientFinishedModelSystem;
        hostBus.ClientErrorWhenRunningModelSystem -= OnClientErrorWhenRunningModelSystem;
        hostBus.ClientOptimizationResultsAvailable -= OnClientOptimizationResultsAvailable;
        hostBus.ClientIterationProgressAvailable -= OnClientIterationProgressAvailable;
        hostBus.SharedEstimationProgressAvailable -= OnSharedEstimationProgress;
        hostBus.SharedEstimationCompleted -= OnSharedEstimationCompleted;
        hostBus.SharedEstimationWorkerControlAcknowledged -= OnSharedEstimationWorkerControlAcknowledged;
        hostBus.SharedEstimationStatusAvailable -= OnSharedEstimationStatus;
        hostBus.SharedEstimationJobSnapshotsAvailable -= OnSharedEstimationJobSnapshotsAvailable;
        hostBus.RemoteRunSnapshotsAvailable -= OnRemoteRunSnapshotsAvailable;
        hostBus.RemoteRunArtifactsAvailable -= OnRemoteRunArtifactsAvailable;
        hostBus.ClientRunArtifactsReceived -= OnClientRunArtifactsReceived;
        hostBus.Disconnected -= OnHostBusDisconnected;
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);
        HostBus[] subscribedHostBuses;
        lock (_hostBusSubscriptionLock)
            subscribedHostBuses = _subscribedHostBuses.ToArray();
        foreach (var hostBus in subscribedHostBuses)
            UnsubscribeFromHostBus(hostBus);
        _runServerBus?.Dispose();
        _connections.Dispose();
        if (_runServerProcess is { HasExited: false })
        {
            try
            {
                _runServerProcess.Kill(entireProcessTree: true);
                _runServerProcess.WaitForExit(2000);
            }
            catch
            {
                // The process may have exited while the connection was closing.
            }
        }
        _runServerProcess?.Dispose();
    }
}

internal sealed class SharedEstimationCompletionGate
{
    private readonly object _sync = new();
    private readonly HashSet<string> _completedRunIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SharedEstimationCompletion> _pending = new(StringComparer.Ordinal);

    public bool TryReceive(SharedEstimationCompletion completion, bool contextReady,
        out SharedEstimationCompletion? accepted)
    {
        lock (_sync)
        {
            accepted = null;
            if (_completedRunIds.Contains(completion.RunId))
                return false;
            if (!contextReady)
            {
                _pending[completion.RunId] = completion;
                return true;
            }

            _pending.Remove(completion.RunId);
            _completedRunIds.Add(completion.RunId);
            accepted = completion;
            return true;
        }
    }

    public SharedEstimationCompletion? MarkReady(string runId)
    {
        lock (_sync)
        {
            if (_completedRunIds.Contains(runId) || !_pending.Remove(runId, out var completion))
                return null;
            return completion;
        }
    }
}
