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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
        while (!_Exited)
        {
            Interlocked.MemoryBarrier();
            if (!_Exited)
            {
                Task.WaitAll(Task.Delay(50));
            }
            Interlocked.MemoryBarrier();
        }
        if (_Owner)
        {
            _HostStream.Dispose();
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
        ClientIterationProgress = 10
    }

    /// <summary>
    /// This event is signalled when a client finishes running a model system.
    /// The parameter is the name of the completed model system.
    /// </summary>
    public event EventHandler<string>? ClientFinishedModelSystem;

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
    /// <param name="values">Ordered list of (node serialisation index, current value) pairs.</param>
    public delegate void IterationProgressUpdate(
        object sender, string runID, int iteration, double fitness,
        IReadOnlyList<(int nodeIndex, double value)> values);

    /// <summary>
    /// Fired after each optimisation iteration with the parameter values that were tested.
    /// </summary>
    public event IterationProgressUpdate? ClientIterationProgressAvailable;

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
                    var command = (In)reader.ReadInt32();
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
                                IgnoreWarnings(() => ClientErrorWhenRunningModelSystem?.Invoke(this, runId, errMsg, String.Empty, moduleName, elementId));
                            }
                            break;
                        case In.ClientFinishedModelSystem:
                            {
                                var runId = reader.ReadString();
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
                                int count = reader.ReadInt32();
                                var values = new (int nodeIndex, double value)[count];
                                for (int i = 0; i < count; i++)
                                    values[i] = (reader.ReadInt32(), reader.ReadDouble());
                                IgnoreWarnings(() => ClientIterationProgressAvailable?.Invoke(this, runId, iteration, fitness, values));
                            }
                            break;
                        default:
                            throw new Exception($"Unsupported command: {Enum.GetName<In>(command)}");
                    }
                    System.Threading.Interlocked.MemoryBarrier();
                }
            }
            catch(Exception)
            {
                // The client has disconnected or crashed. Exit the listener thread.
            }
            finally
            {
                _Exited = true;
            }
        })
        {
            IsBackground = true,
            Name = "HostBus Listener Thread"
        };
        listenerThread.Start();
        return listenerThread;
    }

    private enum Out
    {
        Heartbeat = 0,
        RunModelSystem = 1,
        CancelModelRun = 2,
        KillModelRun = 3,
        RequestClientShutdown = 4,
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
        => RunModelSystem(modelSystem, cwd, startToExecute, RunMode.Normal, out id, out error);

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
                // int64
                using var writer = new BinaryWriter(_HostStream, Encoding.UTF8, true);
                writer.Write((int)Out.RunModelSystem);
                writer.Write(id);
                writer.Write(cwd);
                writer.Write(startToExecute);
                writer.Write((int)runMode);
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
            catch (IOException e)
            {
                error = new CommandError(e.Message);
                return false;
            }
        }
    }

}

