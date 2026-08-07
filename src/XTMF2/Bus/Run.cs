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
        private ModelSystem? _modelSystem;

        /// <summary>
        /// A reference to the XTMFRuntime that will execute the model system.
        /// </summary>
        private readonly XTMFRuntime _runtime;

        /// <summary>The run mode; controls post-execution result collection.</summary>
        private readonly RunMode _runMode;

        /// <summary>True when this run is executing in estimation mode.</summary>
        internal bool IsEstimationRun => _runMode == RunMode.Estimation;

        /// <summary>True when this run is executing in calibration mode.</summary>
        internal bool IsCalibrationRun => _runMode == RunMode.Calibration;

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
        /// The fully-constructed model system after a successful execution.
        /// Set only when <see cref="StartRun"/> returns without error.
        /// Used by the optimisation loop to read fitness / calibration-target values.
        /// </summary>
        internal ModelSystem? ModelSystemAfterRun { get; private set; }

        /// <summary>
        /// The fitness value collected from <see cref="ModelSystem.EstimationFitnessNode"/>
        /// after a successful estimation iteration.  Only valid when
        /// <see cref="_runMode"/> is <see cref="RunMode.Estimation"/>.
        /// </summary>
        internal double? FitnessValue { get; private set; }

        /// <summary>
        /// Per-entry calibration signals collected from each
        /// <see cref="XTMF2.ModelSystemConstruct.CalibrationEntry.TargetNode"/> after a
        /// successful calibration iteration.  Only valid when
        /// <see cref="_runMode"/> is <see cref="RunMode.Calibration"/>.
        /// Ordered identically to the flat list of enabled calibration parameters.
        /// </summary>
        internal IReadOnlyList<double>? CalibrationTargetValues { get; private set; }

        /// <summary>
        /// Creates a Run that is ready to execute.
        /// </summary>
        /// <param name="id">The ID of the run</param>
        /// <param name="modelSystem">The serialised model system as bytes.</param>
        /// <param name="startToExecute">The Start that will be the point in which the model system will be invoked from.</param>
        /// <param name="runtime">The instance of XTMF that will be used.</param>
        /// <param name="cwd">The directory to run the model system in.</param>
        /// <param name="runMode">Controls post-execution result collection (Normal / Estimation / Calibration).</param>
        public Run(string id, byte[] modelSystem, string startToExecute, XTMFRuntime runtime, string cwd,
            RunMode runMode = RunMode.Normal)
        {
            ID = id;
            _modelSystemAsData = modelSystem;
            StartToExecute = startToExecute;
            HasExecuted = false;
            _runtime = runtime;
            _currentWorkingDirectory = cwd;
            _runMode = runMode;
        }

        /// <summary>
        /// Validate the model system contained within this run context.
        /// </summary>
        /// <param name="error">An error message if the model system is invalid.</param>
        /// <returns>True if the model system is valid, false otherwise with an error message.</returns>
        private bool ValidateModelSystem(ref string? error, ref string? moduleName, ref Guid? elementId)
        {
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
            if (!ModelSystem.Load(modelSystemAsString, _runtime, out var ms, ref error)
                || !ms!.Construct(_runtime, ref error)
                || !ms!.Validate(ref moduleName, ref error, ref elementId))
            {
                RunResults.WriteValidationError(_currentWorkingDirectory, moduleName, "Failed when validating the model system! " + error + "\r\n" + modelSystemAsString);
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

        private bool GetStart(List<string> startPath, out Start? start, ref string? error)
        {
            start = null;
            if (_modelSystem == null)
            {
                throw new InvalidOperationException("The model system must be constructed before trying to get the name of a start!");
            }
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
            try
            {
                modelSystemAsString = Encoding.UTF8.GetString(rawData);
                return !String.IsNullOrWhiteSpace(modelSystemAsString);
            }
            catch(Exception e)
            {
                throw new Exception("Failed when converting model system to a string! + \r\n" + e.Message);
            }
        }

        /// <summary>
        /// Execute the run context.
        /// </summary>
        /// <param name="error">The error message if the run fails.</param>
        /// <param name="stackTrace">The stack trace at the point of the error if the run fails.</param>
        /// <returns>True if the run succeeds, false otherwise with an error message and a stack trace.</returns>
        public RunError? StartRun()
        {
            string? error = null, moduleName = null, stackTrace = string.Empty;
            Guid? elementId = null;
            var runBus = _runtime.RunBus;
            if (runBus is not null)
                runBus.CurrentRun = this;
            if (!ValidateModelSystem(ref error, ref moduleName, ref elementId))
            {
                return new RunError(RunErrorType.Validation, $"Failed when validating the model system! {error}", moduleName, stackTrace, elementId);
            }
            if(!GetStart(Start.ParseStartString(StartToExecute), out var startingMss, ref error))
            {
                return new RunError(RunErrorType.Validation, $"Failed when getting the start point! {error}", moduleName, stackTrace, elementId);
            }
            if(startingMss == null)
            {
                return new RunError(RunErrorType.Validation, "Unable to find the starting point for this run!", moduleName, stackTrace, elementId);
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
                if(startingMss.Module is IAction modelStart)
                {
                    modelStart.Invoke();
                }
                else
                {
                    return new RunError(RunErrorType.Runtime, "Unable to invoking the starting module!", startingMss.Module?.Name ?? "Unknown module", string.Empty);
                }
                RunResults.WriteRunCompleted(_currentWorkingDirectory);
                // Expose the model system for post-execution result collection.
                ModelSystemAfterRun = _modelSystem;
                CollectOptimizationResults();
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
                elementId = null;
                string? failingModuleName = null;
                if (e is XTMFRuntimeException xtmfError)
                {
                    if (!TryResolveModelElementForRuntimeModule(_modelSystem, xtmfError.FailingModule,
                        out failingModuleName, out elementId))
                    {
                        failingModuleName = xtmfError.FailingModule?.Name ?? "Unknown module";
                    }
                }
                return new RunError(RunErrorType.Runtime, error, failingModuleName, stackTrace, elementId);
            }
            finally
            {
                Directory.SetCurrentDirectory(originalDir);
                if (runBus is not null && ReferenceEquals(runBus.CurrentRun, this))
                    runBus.CurrentRun = null;
            }
            // success for now
            return null;
        }

        /// <summary>
        /// Attempts to map a failing runtime module instance back to a model element
        /// that is visible on the canvas.
        /// </summary>
        internal static bool TryResolveModelElementForRuntimeModule(
            ModelSystem? modelSystem,
            IModule? failingModule,
            out string? moduleName,
            out Guid? elementId)
        {
            moduleName = null;
            elementId = null;
            if (modelSystem is null || failingModule is null)
                return false;

            var toProcess = new Stack<Boundary>();
            toProcess.Push(modelSystem.GlobalBoundary);
            while (toProcess.TryPop(out var boundary))
            {
                foreach (var child in boundary.Boundaries)
                    toProcess.Push(child);

                foreach (var start in boundary.Starts)
                {
                    if (ReferenceEquals(start.Module, failingModule))
                    {
                        moduleName = start.Name;
                        elementId = start.Id;
                        return true;
                    }
                }

                foreach (var node in boundary.Modules)
                {
                    if (ReferenceEquals(node.Module, failingModule))
                    {
                        moduleName = node.Name;
                        elementId = node.Id;
                        return true;
                    }
                }

                foreach (var fi in boundary.FunctionInstances)
                {
                    if (ReferenceEquals(fi.Module, failingModule))
                    {
                        moduleName = fi.Name;
                        elementId = fi.Id;
                        return true;
                    }

                    // Runtime failures from internal template modules are surfaced on the
                    // function-instance canvas element because internals are not visible at
                    // the boundary scope where the instance is placed.
                    foreach (var internalStart in fi.Template.InternalModules.Starts)
                    {
                        if (ReferenceEquals(fi.GetRuntimeModule(internalStart), failingModule))
                        {
                            moduleName = fi.Name;
                            elementId = fi.Id;
                            return true;
                        }
                    }
                    foreach (var internalNode in fi.Template.InternalModules.Modules)
                    {
                        if (ReferenceEquals(fi.GetRuntimeModule(internalNode), failingModule))
                        {
                            moduleName = fi.Name;
                            elementId = fi.Id;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private bool RuntimeValidation(ref string? moduleName, ref string? errorMessage)
        {
            Stack<Boundary> toProcess = new Stack<Boundary>();
            toProcess.Push(_modelSystem!.GlobalBoundary);
            while (toProcess.TryPop(out Boundary? current))
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
                foreach (var fi in current.FunctionInstances)
                {
                    if (!fi.ValidateRuntimeModules(ref moduleName, ref errorMessage))
                        return false;
                }
            }
            return true;
        }

        /// <summary>
        /// After a successful run, collect the fitness value (estimation) or all calibration
        /// target signals (calibration) from the constructed module instances.
        /// </summary>
        private void CollectOptimizationResults()
        {
            if (_modelSystem is null) return;

            if (_runMode == RunMode.Estimation)
            {
                var fitnessNode = _modelSystem.EstimationFitnessNode;
                if (fitnessNode?.Module is IFunction<double> fd)
                    FitnessValue = fd.Invoke();
                else if (fitnessNode?.Module is IFunction<float> ff)
                    FitnessValue = (double)ff.Invoke();
            }
            else if (_runMode == RunMode.Calibration)
            {
                var targets = new List<double>();
                foreach (var group in _modelSystem.CalibrationGroups)
                {
                    foreach (var entry in group.Parameters)
                    {
                        if (!entry.IsEnabled || entry.ModelOutputNode is null || entry.TargetOutputNode is null) continue;
                        double modelVal  = entry.ModelOutputNode.Module  is IFunction<double> fdM  ? fdM.Invoke()
                                         : entry.ModelOutputNode.Module  is IFunction<float>  ffM  ? (double)ffM.Invoke()
                                         : 1.0;
                        double targetVal = entry.TargetOutputNode.Module is IFunction<double> fdT  ? fdT.Invoke()
                                         : entry.TargetOutputNode.Module is IFunction<float>  ffT  ? (double)ffT.Invoke()
                                         : 1.0;
                        targets.Add(modelVal == 0.0 ? 1.0 : targetVal / modelVal);
                    }
                }
                CalibrationTargetValues = targets;
            }
        }
    }
}
