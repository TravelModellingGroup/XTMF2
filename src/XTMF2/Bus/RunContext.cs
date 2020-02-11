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
using System.IO;
using System.Text;
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
        private readonly string _ModelSystemAsString;

        /// <summary>
        /// The directory that this run will be executed in.
        /// </summary>
        private readonly string _CurrentWorkingDirectory;

        /// <summary>
        /// The processed representation of the model system.
        /// </summary>
        private ModelSystem _ModelSystem;

        /// <summary>
        /// A reference to the XTMFRuntime that will execute the model system.
        /// </summary>
        private readonly XTMFRuntime _Runtime;

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

        private RunContext(XTMFRuntime runtime, string id, string modelSystem, string cwd, string start)
        {
            _Runtime = runtime;
            ID = id;
            _ModelSystemAsString = modelSystem;
            _CurrentWorkingDirectory = cwd;
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
            if (!Convert(modelSystem, out string modelSystemAsString))
            {
                context = null;
                return false;
            }
            context = new RunContext(runtime, id, modelSystemAsString, cwd, start);
            return true;
        }

        private static bool Convert(byte[] rawData, out string modelSystemAsString)
        {
            modelSystemAsString = Encoding.Unicode.GetString(rawData);
            return !String.IsNullOrWhiteSpace(modelSystemAsString);
        }

        /// <summary>
        /// Validate the model system contained within this run context.
        /// </summary>
        /// <param name="error">An error message if the model system is invalid.</param>
        /// <returns>True if the model system is valid, false otherwise with an error message.</returns>
        internal bool ValidateModelSystem(ref string error)
        {
            string moduleName = null;
            // Make sure that we are able to actually construct the directory
            try
            {
                Directory.CreateDirectory(_CurrentWorkingDirectory);
            }
            catch (IOException e)
            {
                error = e.Message;
                return false;
            }
            // Construct the model system
            if (!ModelSystem.Load(_ModelSystemAsString, _Runtime, out ModelSystem ms, ref error)
                || !ms.Construct(_Runtime, ref error)
                || !ms.Validate(ref moduleName, ref error))
            {
                RunResults.WriteValidationError(_CurrentWorkingDirectory, moduleName, error);
                return false;
            }
            _ModelSystem = ms;
            // Ensure that the starting point exists
            if (!GetStart(Start.ParseStartString(StartToExecute),
                out var _, ref error))
            {
                _ModelSystem = null;
                return false;
            }
            return true;
        }

        private bool GetStart(List<string> startPath, out Start start, ref string error)
        {
            start = null;
            if (startPath.Count == 0)
            {
                error = "No start path was defined!";
                return false;
            }
            // get the boundary the start should be contained within.
            Boundary current = _ModelSystem.GlobalBoundary;
            for (int i = 0; i < startPath.Count - 1; i++)
            {
                bool found = false;
                foreach (var child in current.Boundaries)
                {
                    if (child.Name.Equals(startPath[i], StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        current = child;
                    }
                }
                if (!found)
                {
                    error = $"Unable to find a child boundary named {startPath[i]} in parent boundary {current.Name}!";
                    return false;
                }
            }
            var startName = startPath[startPath.Count - 1];
            foreach (var s in current.Starts)
            {
                if (startName.Equals(s.Name, StringComparison.OrdinalIgnoreCase))
                {
                    start = s;
                    return true;
                }
            }
            error = $"Unable to find {startName} within boundary = {current.FullPath}.";
            return false;
        }

        /// <summary>
        /// Execute the run context.
        /// </summary>
        /// <param name="error">The error message if the run fails.</param>
        /// <param name="stackTrace">The stack trace at the point of the error if the run fails.</param>
        /// <returns>True if the run succeeds, false otherwise with an error message and a stack trace.</returns>
        internal bool Run(ref string error, ref string stackTrace)
        {
            if (!GetStart(Start.ParseStartString(StartToExecute), out var startingMss, ref error))
            {
                return false;
            }
            var originalDir = Directory.GetCurrentDirectory();
            try
            {
                string moduleName = null;
                Directory.SetCurrentDirectory(_CurrentWorkingDirectory);
                if (!RuntimeValidation(ref moduleName, ref error))
                {
                    RunResults.WriteValidationError(_CurrentWorkingDirectory, moduleName, error);
                    return false;
                }
                ((IAction)startingMss.Module).Invoke();
                RunResults.WriteRunCompleted(_CurrentWorkingDirectory);
            }
            catch (Exception e)
            {
                error = e.Message;
                stackTrace = e.StackTrace;
                RunResults.WriteError(_CurrentWorkingDirectory, e);
                return false;
            }
            finally
            {
                Directory.SetCurrentDirectory(originalDir);
            }
            // success for now
            return true;
        }

        private bool RuntimeValidation(ref string moduleName, ref string errorMessage)
        {
            Stack<Boundary> toProcess = new Stack<Boundary>();
            toProcess.Push(_ModelSystem.GlobalBoundary);
            while (toProcess.TryPop(out Boundary current))
            {
                foreach (var child in current.Boundaries)
                {
                    toProcess.Push(child);
                }
                foreach (var module in current.Modules)
                {
                    if (module.Module is IModule realModule)
                    {
                        try
                        {
                            if (!realModule.RuntimeValidation(ref errorMessage))
                            {
                                moduleName = module.Name;
                                return false;
                            }
                        }
                        catch (Exception e)
                        {
                            moduleName = module.Name;
                            errorMessage = e.Message;
                            return false;
                        }
                    }
                }
            }
            return true;
        }
    }
}
