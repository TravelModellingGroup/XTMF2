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
using System.Threading;
using System.Threading.Tasks;
using XTMF2.Bus;
using XTMF2.Editing;
using XTMF2.GUI.ViewModels;


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

    /// <summary>
    /// Maps run IDs to the model system session and the user that submitted the run,
    /// so that optimization results can be forwarded to the correct session.
    /// </summary>
    private readonly Dictionary<string, (ModelSystemSession Session, User User)> _sessionsByRunId = new();

    /// <summary>
    /// Fires when an estimation or calibration run completes and has results ready to be
    /// optionally applied back to the model system.
    /// </summary>
    public event Action<string, ModelSystemSession, IReadOnlyList<(int nodeIndex, double value)>>? OptimizationResultsAvailable;

    /// <summary>
    /// If running in debug mode, the RunServerBus with be run within the same process as the GUI to make debugging easier.
    /// </summary>
    private RunServerBus? _runServerBus;

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
        hostBus.ClientReportedStatus += controller.OnClientReportedStatus;
        hostBus.ClientFinishedModelSystem += controller.OnClientFinishedModelSystem;
        hostBus.ClientErrorWhenRunningModelSystem += controller.OnClientErrorWhenRunningModelSystem;
        hostBus.ClientOptimizationResultsAvailable += controller.OnClientOptimizationResultsAvailable;
        hostBus.ClientIterationProgressAvailable += controller.OnClientIterationProgressAvailable;
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
        var id = Guid.NewGuid().ToString();
        var xtmfGUIFilePath = typeof(XTMF2.GUI.Program).Assembly.Location;
        var xtmfClientFileName = Path.Combine(Path.GetDirectoryName(xtmfGUIFilePath)!, "XTMF2.RunServer.dll");
        Process? client = null;
        try
        {
            if (!XTMF2.Bus.CreateStreams.CreateNewNamedPipeHost(id, out var hostStream, out error, () =>
            {
                // Client startup goes here
                var startInfo = new ProcessStartInfo()
                {
                    FileName = "dotnet",
                    Arguments = $"\"{xtmfClientFileName}\" -namedPipe \"{id}\"",
                    UseShellExecute = false,
                    CreateNoWindow = OperatingSystem.IsWindows(),
                    WorkingDirectory = Environment.CurrentDirectory
                };
                client = new()
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true
                };
                client.Start();
            }))
            {
                controller = null;
                return false;
            }
            var hostBus = new HostBus(hostStream, true);
            controller = new RunController(runtime, hostBus);
            hostBus.ClientReportedStatus += controller.OnClientReportedStatus;
            hostBus.ClientFinishedModelSystem += controller.OnClientFinishedModelSystem;
            hostBus.ClientErrorWhenRunningModelSystem += controller.OnClientErrorWhenRunningModelSystem;
            hostBus.ClientOptimizationResultsAvailable += controller.OnClientOptimizationResultsAvailable;
            hostBus.ClientIterationProgressAvailable += controller.OnClientIterationProgressAvailable;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to initialize the run controller: {ex.Message}";
            controller = null;
            return false;
        }
    }

    private void OnClientErrorWhenRunningModelSystem(object sender, string runID, string errorMessage, string stack)
    {
        RunsViewModel.NotifyError(runID, errorMessage, stack);
    }

    private void OnClientFinishedModelSystem(object? sender, string runID)
    {
        RunsViewModel.NotifyFinished(runID);
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
        => SendRun(projectSession, msSession, user, startToExecute, runName, RunMode.Normal, out id, out error);

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
        => SendRun(projectSession, msSession, user, startToExecute, runName, RunMode.Estimation, out id, out error);

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
        => SendRun(projectSession, msSession, user, startToExecute, runName, RunMode.Calibration, out id, out error);

    private bool SendRun(
        Project projectSession,
        ModelSystemSession msSession,
        User user,
        string startToExecute,
        string runName,
        RunMode runMode,
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
        if (!_hostBus.RunModelSystem(msSession, runDirectory, startToExecute, runMode, out id, out error))
        {
            return false;
        }
        lock (_sessionsByRunId)
        {
            _sessionsByRunId[id] = (msSession, user);
        }
        var vm = RunsViewModel.AddRun(id, runName);
        if (runMode != RunMode.Normal)
        {
            // Extract parameter metadata so the progress dialog can show names/bounds.
            var meta = msSession.GetOptimizationParameterMeta(runMode);
            var runId = id;
            vm.SetRunMode(runMode, meta, () => _hostBus.CancelModelRun(runId, out _));
        }
        return true;
    }

    private RunController(XTMFRuntime runtime, HostBus hostBus)
    {
        Runtime = runtime;
        _hostBus = hostBus;
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);
        _runServerBus?.Dispose();
        _hostBus.RequestClientShutdown(out _);
        _hostBus.Dispose();
    }
}
