/*
    Copyright 2020 Travel Modelling Group, Department of Civil Engineering, University of Toronto

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
    internal sealed class Run
    {
        /// <summary>
        /// A representation of the model system.
        /// </summary>
        private readonly byte[] _modelSystemAsData;

        /// <summary>
        /// The directory that this run will be executed in.
        /// </summary>
        private readonly string _currentWorkingDirectory;

        /// <summary>
        /// The processed representation of the model system.
        /// </summary>
        private ModelSystem _modelSystem;

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

        /// <summary>
        /// Creates a Run that is ready to execute.
        /// </summary>
        /// <param name="id">The ID of the run</param>
        /// <param name="modelSystemAsString">A representation of the model system as a string.</param>
        /// <param name="startToExecute">The Start that will be the point in which the model system will be invoked from.</param>
        /// <param name="runtime">The instance of XTMF that will be used.</param>
        /// <param name="cwd">The directory to run the model system in.</param>
        public Run(string id, byte[] modelSystem, string startToExecute, XTMFRuntime runtime, string cwd)
        {
            ID = id;
            _modelSystemAsData = modelSystem;
            StartToExecute = startToExecute;
            HasExecuted = false;
            _runtime = runtime;
            _currentWorkingDirectory = cwd;
        }

        /// <summary>
        /// Validate the model system contained within this run context.
        /// </summary>
        /// <param name="error">An error message if the model system is invalid.</param>
        /// <returns>True if the model system is valid, false otherwise with an error message.</returns>
        private bool ValidateModelSystem(ref string error)
        {
            string moduleName = null;
            // Make sure that we are able to actually construct the directory
            try
            {
                Directory.CreateDirectory(_currentWorkingDirectory);
            }
            catch (IOException e)
            {
                error = e.Message;
                return false;
            }
            // Construct the model system
            if(!Convert(_modelSystemAsData, out var modelSystemAsString))
            {
                error = "Unable to convert model system data into a string!";
                return false;
            }
            if (!ModelSystem.Load(modelSystemAsString, _runtime, out ModelSystem ms, ref error)
                || !ms.Construct(_runtime, ref error)
                || !ms.Validate(ref moduleName, ref error))
            {
                RunResults.WriteValidationError(_currentWorkingDirectory, moduleName, error);
                return false;
            }
            _modelSystem = ms;
            // Ensure that the starting point exists
            if (!GetStart(Start.ParseStartString(StartToExecute),
                out var _, ref error))
            {
                _modelSystem = null;
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
            Boundary current = _modelSystem.GlobalBoundary;
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

        private static bool Convert(byte[] rawData, out string modelSystemAsString)
        {
            modelSystemAsString = Encoding.Unicode.GetString(rawData);
            return !String.IsNullOrWhiteSpace(modelSystemAsString);
        }

        /// <summary>
        /// Execute the run context.
        /// </summary>
        /// <param name="error">The error message if the run fails.</param>
        /// <param name="stackTrace">The stack trace at the point of the error if the run fails.</param>
        /// <returns>True if the run succeeds, false otherwise with an error message and a stack trace.</returns>
        public RunError StartRun()
        {
            string error = null, moduleName = null, stackTrace = string.Empty;
            if (!ValidateModelSystem(ref error) || !GetStart(Start.ParseStartString(StartToExecute), out var startingMss, ref error))
            {
                return new RunError(RunErrorType.Validation, error, moduleName, stackTrace);
            }
            var originalDir = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(_currentWorkingDirectory);
                if (!RuntimeValidation(ref moduleName, ref error))
                {
                    RunResults.WriteValidationError(_currentWorkingDirectory, moduleName, error);
                    return new RunError(RunErrorType.Runtime, error, moduleName, stackTrace);
                }
                ((IAction)startingMss.Module).Invoke();
                RunResults.WriteRunCompleted(_currentWorkingDirectory);
            }
            catch (Exception e)
            {
                while (e.InnerException is Exception current)
                {
                    e = current;
                }
                error = e.Message;
                stackTrace = e.StackTrace;
                RunResults.WriteError(_currentWorkingDirectory, e);
                return new RunError(RunErrorType.Runtime, error, 
                    e is XTMFRuntimeException xtmfError ? xtmfError.FailingModule.Name : null, stackTrace);
            }
            finally
            {
                Directory.SetCurrentDirectory(originalDir);
            }
            // success for now
            return null;
        }

        private bool RuntimeValidation(ref string moduleName, ref string errorMessage)
        {
            Stack<Boundary> toProcess = new Stack<Boundary>();
            toProcess.Push(_modelSystem.GlobalBoundary);
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
