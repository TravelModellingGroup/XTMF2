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
using System.Threading;
using System.Threading.Tasks;
using XTMF2.Bus;
using XTMF2.Bus.Optimization;
using XTMF2.Editing;
using XTMF2.GUI.ViewModels;
using XTMF2.GUI.Properties;
using XTMF2.ModelSystemConstruct;
using System.Linq;


namespace XTMF2.GUI;

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
    private readonly HashSet<string> _completedRemoteRuns = new(StringComparer.Ordinal);

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
        lock (_sessionsByRunId)
            _runDirectoriesByRunId.TryGetValue(args.RunId, out targetDirectory);

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
        object sender, string runID, int iteration, double fitness,
        IReadOnlyList<(int nodeIndex, double value)> values)
    {
        RunsViewModel.NotifyIterationProgress(runID, iteration, fitness, values);
    }

    private void OnSharedEstimationProgress(object? sender, SharedEstimationProgress progress)
    {
        RunsViewModel.NotifyStatus(progress.RunId,
            $"[Remote estimation] iteration {progress.Iteration}: best fitness = {progress.BestFitness:G6}");
    }

    private void OnSharedEstimationWorkerControlAcknowledged(
        object? sender, SharedEstimationWorkerControlAcknowledgement acknowledgement)
    {
        RunsViewModel.NotifyRemoteWorkerAcknowledgement(acknowledgement);
    }

    private void OnSharedEstimationCompleted(object? sender, SharedEstimationCompletion completion)
    {
        lock (_sessionsByRunId)
        {
            if (!_completedRemoteRuns.Add(completion.RunId))
                return;
        }
        if (!completion.Succeeded)
        {
            RunsViewModel.NotifyError(completion.RunId,
                completion.FailureReason ?? "Remote estimation failed.", String.Empty, null, null);
            return;
        }

        (ModelSystemSession Session, User User) entry;
        IReadOnlyList<(int nodeIndex, string name, double min, double max)> metadata;
        lock (_sessionsByRunId)
        {
            if (!_remoteEstimationMetadata.TryGetValue(completion.RunId, out var remoteMetadata))
                return;
            if (!_sessionsByRunId.TryGetValue(completion.RunId, out entry) ||
                remoteMetadata is null)
                return;
            metadata = remoteMetadata;
        }
        if (completion.BestParameters.Count != metadata.Count)
            return;
        var results = metadata.Select((item, index) => (item.nodeIndex, completion.BestParameters[index])).ToArray();
        RunsViewModel.NotifyOptimizationResults(completion.RunId, entry.Session, entry.User, results);
        OptimizationResultsAvailable?.Invoke(completion.RunId, entry.Session, results);
        RunsViewModel.NotifyFinished(completion.RunId);
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
            runId, runDirectory, startToExecute, modelStream.ToArray());
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
                var runner = new SharedEstimationCoordinatorRun(runId, algorithm, pool.Coordinator);
                var completion = runner.Execute(
                    progress: progress => RunsViewModel.NotifyStatus(runId,
                        $"[Shared estimation] iteration {progress.Iteration}: best fitness = {progress.BestFitness:G6}"),
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
                RunsViewModel.NotifyFinished(runId);
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
        if (endpoints.Length == 0)
        {
            error = new CommandError("Select at least one worker RunServer in addition to the orchestrator.");
            return false;
        }

        using var modelStream = new MemoryStream();
        if (!msSession.Save(out error, modelStream))
            return false;
        var entries = enabledEntries
            .Select((entry, index) => (entry, nodeIndex: metadata[index].nodeIndex))
            .ToArray();
        var request = new SharedEstimationCoordinatorRequest(
            new SharedEstimationRunRequest(Guid.NewGuid().ToString(),
                Path.Combine(projectSession.ProjectDirectory!, "runs", runName),
                startToExecute, modelStream.ToArray()),
            endpoints.Select(endpoint => new SharedEstimationWorkerEndpoint(
                endpoint.Id, endpoint.Id, endpoint.Address, endpoint.Port, endpoint.Token,
                endpoint.CertificateFingerprint,
                basicParameterOverridesByWorker is not null &&
                basicParameterOverridesByWorker.TryGetValue(endpoint.Id, out var overrides)
                    ? overrides
                    : null)).ToArray(),
            modelSystem.EstimationAlgorithmConfig.AlgorithmId,
            modelSystem.EstimationAlgorithmConfig.GetParameters(),
            entries.Select(item => item.entry.Min).ToArray(),
            entries.Select(item => item.entry.Max).ToArray(),
            entries.Select(item => item.entry.NullHypothesis).ToArray(),
            modelSystem.EstimationObjective == EstimationObjective.Maximize);

        if (!orchestrator.StartRemoteSharedEstimation(request, out error))
            return false;

        id = request.Run.RunId;
        var submittedRunId = request.Run.RunId;
        var runDirectory = request.Run.WorkingDirectory;
        var runViewModel = RunsViewModel.AddRun(id, runName, runDirectory,
            $"{orchestratorEndpointId} (orchestrator, {endpoints.Length} workers)", msSession, user);
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
        runViewModel.SetRemoteEstimationWorkers(
            availableWorkers.Select(ToRunServerEndpoint).ToArray(),
            initialWorkers.Keys.ToArray(),
            workerId => ChangeRemoteEstimationWorker(submittedRunId, workerId, add: true),
            workerId => ChangeRemoteEstimationWorker(submittedRunId, workerId, add: false));
        lock (_sessionsByRunId)
            _sessionsByRunId[id] = (msSession, user);
        lock (_sessionsByRunId)
            _remoteEstimationMetadata[id] = metadata;
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
                out error))
        {
            return false;
        }
        lock (_sessionsByRunId)
        {
            _sessionsByRunId[id] = (msSession, user);
            _hostBusesByRunId[id] = hostBus;
            _runDirectoriesByRunId[id] = runDirectory;
        }
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
        _connections.StateChanged += state => RunServerStateChanged?.Invoke(state);
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
        hostBus.SharedEstimationJobSnapshotsAvailable += OnSharedEstimationJobSnapshotsAvailable;
        hostBus.ClientRunArtifactsReceived += OnClientRunArtifactsReceived;
        hostBus.Disconnected += OnHostBusDisconnected;
        hostBus.QuerySharedEstimationJobs(out _);
    }

    private void OnSharedEstimationJobSnapshotsAvailable(
        object? sender, IReadOnlyList<SharedEstimationJobSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            if (snapshot.ActiveWorkerIds is not null)
                RunsViewModel.NotifyRemoteWorkerSnapshot(snapshot.RunId, snapshot.ActiveWorkerIds);
            if (snapshot.Progress is not null)
                OnSharedEstimationProgress(sender, snapshot.Progress);
            if (snapshot.Completion is not null)
                OnSharedEstimationCompleted(sender, snapshot.Completion);
        }
    }

    private void OnHostBusDisconnected(object? sender, EventArgs e)
    {
        if (sender is HostBus hostBus)
            UnsubscribeFromHostBus(hostBus);
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
        hostBus.SharedEstimationJobSnapshotsAvailable -= OnSharedEstimationJobSnapshotsAvailable;
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
