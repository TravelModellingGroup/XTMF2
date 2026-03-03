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
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using XTMF2.Bus;
using XTMF2.Editing;


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
    /// The hostbus that this controller uses to communicate with the client. This is used to send commands to the client and receive status updates from the client.
    /// </summary>
    private HostBus _hostBus;

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
        var id = Guid.NewGuid().ToString();
        var xtmfGUIFilePath = typeof(XTMF2.GUI.Program).Assembly.Location;
        var xtmfClientFileName = Path.Combine(Path.GetDirectoryName(xtmfGUIFilePath)!, "XTMF2.Client.dll");
        Process? client = null;
        try
        {
            if(!XTMF2.Bus.CreateStreams.CreateNewNamedPipeHost(id, out var hostStream, out error, () =>
            {
                // Client startup goes here
                var startInfo = new ProcessStartInfo()
                {
                    FileName = "dotnet",
                    Arguments = $"\"{xtmfClientFileName}\" -namedPipe \"{id}\"",
                    CreateNoWindow = false,
                    WorkingDirectory = Environment.CurrentDirectory
                };
                client = new ()
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
            hostBus.ClientReportedStatus += StatusUpdateFromClient;
            hostBus.ClientFinishedModelSystem += ClientFinishedModelSystem;
            hostBus.ClientErrorWhenRunningModelSystem += ClientErrorWhenRunningModelSystem;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to initialize the run controller: {ex.Message}";
            controller = null;
            return false;
        }
    }

    private static void ClientErrorWhenRunningModelSystem(object sender, string runID, string errorMessage, string stack)
    {
        
    }

    private static void ClientFinishedModelSystem(object? sender, string e)
    {
        
    }

    private static void StatusUpdateFromClient(object sender, string runID, string status)
    {
        
    }

    /// <summary>
    /// Sends a run command to the model system.
    /// </summary>
    /// <param name="session">The model system session.</param>
    /// <param name="startToExecute">The command to start execution.</param>
    /// <param name="id">The unique ID of the run, null if the command fails.</param>
    /// <param name="error">The error information if the command fails.</param>
    /// <returns>True if the command is successfully sent, false otherwise.</returns>
    public bool SendRun(
        Project projectSession,
        ModelSystemSession msSession,
        string startToExecute,
        string runName,
        [NotNullWhen(true)]out string? id,
        [NotNullWhen(false)]out CommandError? error)
    {
        var projectDirectory = projectSession.ProjectDirectory;
        if(String.IsNullOrWhiteSpace(projectDirectory))
        {
            id = null;
            error = new CommandError("Project directory is not set.");
            return false;
        }
        var runDirectory = Path.Combine(projectDirectory, "runs", runName);
        if(!_hostBus.RunModelSystem(msSession, runDirectory, startToExecute, out id, out error))
        {
            return false;
        }
        return true;
    }

    private RunController(XTMFRuntime runtime, HostBus hostBus)
    {
        Runtime = runtime;
        _hostBus = hostBus;
    }

    public void Dispose()
    {
        _hostBus.Dispose();
    }
}
