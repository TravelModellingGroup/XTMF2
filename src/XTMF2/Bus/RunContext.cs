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
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using XTMF2.ModelSystemConstruct;

namespace XTMF2.Bus
{
    /// <summary>
    /// Used to represent all of the information required to run a model system.
    /// </summary>
    public sealed class RunContext
    {
        /// <summary>
        /// A string representation of the model system.
        /// </summary>
        private readonly byte[] _modelSystem;

        /// <summary>
        /// The directory that this run will be executed in.
        /// </summary>
        private readonly string _currentWorkingDirectory;

        /// <summary>
        /// A reference to the XTMFRuntime that will execute the model system.
        /// </summary>
        private readonly XTMFRuntime _runtime;

        /// <summary>
        /// Set tot true if the model system has finished executing.
        /// </summary>
        public bool HasExecuted { get; private set; }

        /// <summary>
        /// The unique identifier for this run.
        /// </summary>
        public string ID { get; private set; }

        /// <summary>
        /// The name of the module to use as a starting point.
        /// </summary>
        public string StartToExecute { get; private set; }

        private RunContext(XTMFRuntime runtime, string id, byte[] modelSystem, string cwd, string start)
        {
            _runtime = runtime;
            ID = id;
            _modelSystem = modelSystem;
            _currentWorkingDirectory = cwd;
            HasExecuted = false;
            StartToExecute = start;
        }

        /// <summary>
        /// Create a model system run context in the given XTMF runtime.
        /// </summary>
        /// <param name="runtime">The runtime to work within.</param>
        /// <param name="id">The unique ID for this run.</param>
        /// <param name="modelSystem">The model system stored as bytes.</param>
        /// <param name="cwd">The directory to execute the model system in.</param>
        /// <param name="start">The starting point to run in.</param>
        /// <param name="context">The resulting context.</param>
        /// <returns>True if the model system was able to be processed, false otherwise.</returns>
        public static bool CreateRunContext(XTMFRuntime runtime, string id, byte[] modelSystem, string cwd,
            string start, out RunContext context)
        {
            context = new RunContext(runtime, id, modelSystem, cwd, start);
            return true;
        }

        private Stream CreateRunBusLocal(ClientBus clientBus)
        {
            var pipeName = Guid.NewGuid().ToString();
            string error = null;
            Stream clientToRunStream = null;
            CreateStreams.CreateNewNamedPipeHost(pipeName, out clientToRunStream, ref error, () =>
            {
                clientBus.StartProcessingRequestFromRun(ID, clientToRunStream);
                if (CreateStreams.CreateNamedPipeClient(pipeName, out var runToClientStream, ref error))
                {
                    Task.Factory.StartNew(() =>
                    {
                        using var rb = new RunBus(ID, runToClientStream, true, _runtime);
                        rb.ProcessRequests();
                    }, TaskCreationOptions.LongRunning);
                }
            });
            return clientToRunStream;
        }

        private (Stream clientToRunStream, Process runProcess) CreateRunBusRemote(ClientBus clientBus)
        {
            var pipeName = Guid.NewGuid().ToString();
            string error = null;
            Stream clientToRunStream = null;
            Process runProcess = null;
            CreateStreams.CreateNewNamedPipeHost(pipeName, out clientToRunStream, ref error, () =>
            {
                var path = Path.GetDirectoryName(typeof(ClientBus).GetTypeInfo().Assembly.Location);
                var startInfo = new ProcessStartInfo()
                {
                    FileName = "dotnet",
                    Arguments = $"\"{Path.Combine(path, "XTMF2.Run.dll")}\" -runID \"{ID}\" {GetExtraDlls(clientBus)}-namedPipe \"{pipeName}\"",
                    CreateNoWindow = false,
                    WorkingDirectory = path
                };
                runProcess = new Process()
                {
                    StartInfo = startInfo
                };
                runProcess.EnableRaisingEvents = true;
                runProcess.Start();
            });
            clientBus.StartProcessingRequestFromRun(ID, clientToRunStream);
            return (clientToRunStream, runProcess);
        }

        private string GetExtraDlls(ClientBus client)
        {
            var builder = new StringBuilder();
            foreach (var dll in client.ExtraDlls)
            {
                builder.Append("-loaddll ");
                builder.Append(dll);
                builder.Append(' ');
            }
            return builder.ToString();
        }


        public void RunInNewProcess(ClientBus client)
        {
            (Stream stream, Process runProcess) = CreateRunBusRemote(client);
            using var writer = new BinaryWriter(stream, Encoding.UTF8, false);
            // Send the commend to start a run
            writer.Write((int)1);
            writer.Write(ID);
            writer.Write(_currentWorkingDirectory);
            writer.Write(StartToExecute);
            writer.Write(_modelSystem.LongLength);
            writer.Write(_modelSystem);
            runProcess.WaitForExit();
        }

        public void RunInCurrentProcess(ClientBus client)
        {
            using var stream = CreateRunBusLocal(client);
            new Run(ID, _modelSystem, StartToExecute, _runtime, _currentWorkingDirectory).StartRun();
        }
    }
}
