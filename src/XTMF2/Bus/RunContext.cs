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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using XTMF2.Bus.Optimization;
using XTMF2.ModelSystemConstruct;
using XTMF2.ModelSystemConstruct.Parameters;
using XTMF2.RuntimeModules;

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

        /// <summary>The run mode; controls whether a single execution or an optimisation loop is performed.</summary>
        private RunMode _runMode = RunMode.Normal;

        /// <summary>Set to true to request that the optimisation loop terminates after the current iteration.</summary>
        private volatile bool _cancelRequested;

        /// <summary>
        /// Requests that the optimisation loop for this run terminates, if this run's ID matches.
        /// </summary>
        internal void RequestCancelRun(string runId)
        {
            if (ID == runId)
                _cancelRequested = true;
        }

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
        /// <param name="runMode">Normal, Estimation or Calibration.</param>
        /// <param name="context">The resulting context.</param>
        /// <returns>True if the model system was able to be processed, false otherwise.</returns>
        public static bool CreateRunContext(XTMFRuntime runtime, string id, byte[] modelSystem, string cwd,
            string start, RunMode runMode, out RunContext context)
        {
            context = new RunContext(runtime, id, modelSystem, cwd, start) { _runMode = runMode };
            return true;
        }

        /// <summary>
        /// Create a normal-mode run context (backwards-compatible overload).
        /// </summary>
        public static bool CreateRunContext(XTMFRuntime runtime, string id, byte[] modelSystem, string cwd,
            string start, out RunContext context)
            => CreateRunContext(runtime, id, modelSystem, cwd, start, RunMode.Normal, out context);

        private (RunBus runBus, Task readerTask) CreateRunBusLocal(RunServerBus clientBus)
        {
            var pipeName = Guid.NewGuid().ToString();
            string? error = null;
            Stream? clientToRunStream = null;
            RunBus? runBus = null;
            Task? readerTask = null;
            CreateStreams.CreateNewNamedPipeHost(pipeName, out clientToRunStream, out error, () =>
            {
                // Start the reader task. It takes ownership of clientToRunStream and disposes it
                // when it exits (leaveOpen=false in the BinaryReader inside StartProcessingRequestFromRun).
                readerTask = clientBus.StartProcessingRequestFromRun(ID, clientToRunStream!);
                if (CreateStreams.CreateNamedPipeClient(pipeName, out var runToClientStream, out error))
                {
                    // Construct RunBus synchronously so that _runtime.RunBus is set before
                    // CreateRunBusLocal returns and Run.StartRun() is called.
                    runBus = new RunBus(ID, runToClientStream!, true, _runtime);
                }
            });
            return (runBus!, readerTask!);
        }

        private (Stream clientToRunStream, Process runProcess) CreateRunBusRemote(RunServerBus clientBus)
        {
            var pipeName = Guid.NewGuid().ToString();
            string? error = null;
            Process? runProcess = null;
            CreateStreams.CreateNewNamedPipeHost(pipeName, out var clientToRunStream, out error, () =>
            {
                var path = Path.GetDirectoryName(typeof(RunServerBus).GetTypeInfo().Assembly.Location)!;
                var startInfo = new ProcessStartInfo()
                {
                    FileName = "dotnet",
                    Arguments = $"\"{Path.Combine(path, "XTMF2.Run.dll")}\" -runID \"{ID}\" {GetExtraDlls(clientBus)}-namedPipe \"{pipeName}\"",
                    UseShellExecute = false,
                    CreateNoWindow = OperatingSystem.IsWindows(),
                    WorkingDirectory = path
                };
                runProcess = new Process()
                {
                    StartInfo = startInfo
                };
                runProcess.EnableRaisingEvents = true;
                runProcess.Start();
            });
            clientBus.StartProcessingRequestFromRun(ID, clientToRunStream!);
            return (clientToRunStream!, runProcess!);
        }

        private string GetExtraDlls(RunServerBus client)
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


        public void RunInNewProcess(RunServerBus client)
        {
            // Optimisation runs always orchestrate their loop in-process; only the individual
            // model-system executions within each iteration use the out-of-process runner
            // (future multi-server extension).  For now, fall through to RunInCurrentProcess.
            if (_runMode != RunMode.Normal)
            {
                RunInCurrentProcess(client);
                return;
            }
            (Stream stream, Process runProcess) = CreateRunBusRemote(client);
            using var writer = new BinaryWriter(stream, Encoding.UTF8, false);
            // Send the commend to start a run
            writer.Write((int)1);
            writer.Write(ID);
            writer.Write(_currentWorkingDirectory);
            writer.Write(StartToExecute);
            writer.Write((int)_runMode);
            writer.Write(_modelSystem.LongLength);
            writer.Write(_modelSystem);
            runProcess.WaitForExit();
        }

        public void RunInCurrentProcess(RunServerBus client)
        {
            if (_runMode == RunMode.Estimation)
            {
                RunEstimationLoop(client);
                return;
            }
            if (_runMode == RunMode.Calibration)
            {
                RunCalibrationLoop(client);
                return;
            }
            // ── Normal run ────────────────────────────────────────────────────────
            (RunBus runBus, Task readerTask) = CreateRunBusLocal(client);
            using (runBus)
            {
                var error = new Run(ID, _modelSystem, StartToExecute, _runtime, _currentWorkingDirectory).StartRun();
                // Send the terminal message through RunBus so the reader task can forward it to
                // RunServerBus (and on to HostBus) and then exit cleanly.
                switch (error?.Type)
                {
                    case RunErrorType.Validation:
                    case RunErrorType.RuntimeValidation:
                        runBus.ModelRunFailedValidation(error.Message, error.ModuleName, error.ElementId);
                        break;
                    case RunErrorType.Runtime:
                        runBus.ModelRunFailed(error.Message, error.StackTrace, error.ModuleName, error.ElementId);
                        break;
                    default:
                        runBus.ModelRunComplete();
                        break;
                }
            }
            // Wait for the reader task to finish draining and forwarding all messages
            // (including the terminal one above) before returning. This ensures no messages
            // are lost due to early stream disposal.
            readerTask.Wait(5000);
        }

        // ── Optimisation helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Maximum number of proportional-update rounds per calibration job.
        /// </summary>
        private const int DefaultMaxCalibrationIterations = 100;

        /// <summary>
        /// Runs the full Nelder-Mead estimation loop.  The model system is built and
        /// validated once; each Nelder-Mead evaluation updates module values directly
        /// and re-invokes the same <see cref="IAction"/> start module.
        /// </summary>
        private void RunEstimationLoop(RunServerBus client)
        {
            // Build, construct, and runtime-validate the model system once.
            if (!BuildAndValidateModelSystem(out var ms, out var start, out var buildError))
            {
                if (buildError!.Type is RunErrorType.Validation or RunErrorType.RuntimeValidation)
                    client.ModelRunFailedValidation(ID, buildError.Message ?? "Estimation setup failed");
                else
                    client.ModelRunFailed(ID, buildError.Message, buildError.StackTrace, buildError.ModuleName, buildError.ElementId?.ToString());
                return;
            }

            // Build ordered list of (nodeIndex, entry, setter) for all enabled parameters.
            var nodesByIndex = ms!.NodesByLoadIndex;
            var nodeToIndex = nodesByIndex is null
                ? new Dictionary<Node, int>()
                : nodesByIndex.ToDictionary(kv => kv.Value, kv => kv.Key);

            var paramEntries = new List<(int nodeIndex, EstimationEntry entry, Action<double> setter)>();
            foreach (var group in ms.EstimationGroups)
            {
                if (!group.IsEnabled) continue;
                foreach (var entry in group.Parameters)
                {
                    if (!entry.IsEnabled) continue;
                    if (!nodeToIndex.TryGetValue(entry.Node, out var idx)) continue;
                    var setter = BuildDoubleSetter(entry.Node);
                    if (setter is null) continue;
                    paramEntries.Add((idx, entry, setter));
                }
            }
            if (paramEntries.Count == 0)
            {
                client.ModelRunFailedValidation(ID, "Estimation requires at least one enabled estimation parameter.");
                return;
            }

            var fitnessReader = ms.EstimationFitnessNode is { } fn ? BuildSignalReader(fn) : null;
            if (fitnessReader is null)
            {
                client.ModelRunFailedValidation(ID, "Estimation requires a fitness node that returns a numeric value.");
                return;
            }

            if (start!.Module is not IAction modelStart)
            {
                client.ModelRunFailedValidation(ID, "The configured start module does not implement IAction.");
                return;
            }

            int n = paramEntries.Count;
            double[] lower = paramEntries.Select(p => p.entry.Min).ToArray();
            double[] upper = paramEntries.Select(p => p.entry.Max).ToArray();
            double[] initial = paramEntries.Select(p => p.entry.NullHypothesis).ToArray();

            var algorithmConfig = ms.EstimationAlgorithmConfig;
            var objective = ms.EstimationObjective;
            bool isMaximize = objective == EstimationObjective.Maximize;
            var algorithm = algorithmConfig.CreateAlgorithm(n, lower, upper, initial, isMaximize);

            int iterationCount = 0;
            RunError? firstRunError = null;
            double[]? latestValues = null;

            var originalDir = Directory.GetCurrentDirectory();
            // Install a lightweight RunBus so modules inside the model system can forward
            // status messages to the client via _runtime.RunBus?.SendStatusMessage(...).
            using var loopRunBus = new RunBus(ID, msg => client.SendStatusMessage(ID, msg), _runtime);
            var loopRun = new Run(ID, _modelSystem, StartToExecute, _runtime, _currentWorkingDirectory, RunMode.Estimation);
            loopRunBus.CurrentRun = loopRun;
            try
            {
                Directory.SetCurrentDirectory(_currentWorkingDirectory);
                algorithm.Run(
                    fitnessEvaluator: values =>
                    {
                        if (_cancelRequested) return double.MaxValue;
                        iterationCount++;
                        // Apply the parameter vector directly to the already-constructed modules.
                        for (int i = 0; i < paramEntries.Count; i++)
                            paramEntries[i].setter(values[i]);
                        try
                        {
                            modelStart.Invoke();
                        }
                        catch (Exception e)
                        {
                            while (e.InnerException is Exception inner) e = inner;
                            Guid? elementId = null;
                            string? moduleName = null;
                            if (e is XTMFRuntimeException xe)
                            {
                                if (!Run.TryResolveModelElementForRuntimeModule(ms, xe.FailingModule, out moduleName, out elementId))
                                    moduleName = xe.FailingModule?.Name;
                            }
                            firstRunError = new RunError(RunErrorType.Runtime, e.Message, moduleName, e.StackTrace, elementId);
                            return double.MaxValue;
                        }
                        latestValues = (double[])values.Clone();
                        double rawFitness = fitnessReader();
                        return rawFitness;
                    },
                    progressCallback: (iter, fitness) =>
                    {
                        // algorithm.BestFitness already returns the user-facing value.
                        client.SendStatusMessage(ID,
                            $"[Estimation] iteration {iter}: best fitness = {fitness:G6}");
                        var progressValues = paramEntries
                            .Select((p, i) => (p.nodeIndex, latestValues?[i] ?? p.entry.NullHypothesis))
                            .ToList();
                        client.SendIterationProgress(ID, iter, fitness, progressValues);
                    },
                    shouldCancel: () => _cancelRequested);
            }
            catch (Exception ex)
            {
                string? moduleName = null;
                string? elementId = null;
                if (ex is XTMFRuntimeException xe)
                {
                    if (!Run.TryResolveModelElementForRuntimeModule(ms, xe.FailingModule, out moduleName, out var resolvedElementId))
                        moduleName = xe.FailingModule?.Name;
                    elementId = resolvedElementId?.ToString();
                }
                client.ModelRunFailed(ID, ex.Message, ex.StackTrace, moduleName, elementId);
                return;
            }
            finally
            {
                Directory.SetCurrentDirectory(originalDir);
                if (ReferenceEquals(loopRunBus.CurrentRun, loopRun))
                    loopRunBus.CurrentRun = null;
            }

            if (firstRunError != null)
            {
                if (firstRunError.Type is RunErrorType.Validation or RunErrorType.RuntimeValidation)
                    client.ModelRunFailedValidation(ID, firstRunError.Message ?? "Unknown validation error");
                else
                    client.ModelRunFailed(ID, firstRunError.Message, firstRunError.StackTrace, firstRunError.ModuleName, firstRunError.ElementId?.ToString());
                return;
            }

            // algorithm.BestFitness already returns the user-facing value.
            client.SendStatusMessage(ID,
                $"[Estimation] converged after {iterationCount} evaluations. " +
                $"Best fitness = {algorithm.BestFitness:G6}. ");
            var estimationResults = paramEntries
                .Select((p, i) => (p.nodeIndex, algorithm.BestParameters[i]))
                .ToList();
            client.SendOptimizationResults(ID, estimationResults);
            client.ModelRunComplete(ID);
        }

        /// <summary>
        /// Runs the proportional-update calibration loop.  The model system is built and
        /// validated once; each iteration updates module values directly, re-invokes the
        /// same <see cref="IAction"/> start module, and reads calibration target ratios
        /// directly from the already-constructed module instances.
        /// </summary>
        private void RunCalibrationLoop(RunServerBus client)
        {
            // Build, construct, and runtime-validate the model system once.
            if (!BuildAndValidateModelSystem(out var ms, out var start, out var buildError))
            {
                if (buildError!.Type is RunErrorType.Validation or RunErrorType.RuntimeValidation)
                    client.ModelRunFailedValidation(ID, buildError.Message ?? "Calibration setup failed");
                else
                    client.ModelRunFailed(ID, buildError.Message, buildError.StackTrace, buildError.ModuleName, buildError.ElementId?.ToString());
                return;
            }

            var nodesByIndex = ms!.NodesByLoadIndex;
            var nodeToIndex = nodesByIndex is null
                ? new Dictionary<Node, int>()
                : nodesByIndex.ToDictionary(kv => kv.Value, kv => kv.Key);

            // Ordered list of (nodeIndex, entry, setter, modelOutputReader, targetOutputReader) for all enabled parameters.
            var paramEntries = new List<(int nodeIndex, CalibrationEntry entry, Action<double> setter, Func<double> modelOutputReader, Func<double> targetOutputReader)>();
            foreach (var group in ms.CalibrationGroups)
                foreach (var entry in group.Parameters)
                {
                    if (!entry.IsEnabled || entry.ModelOutputNode is null || entry.TargetOutputNode is null) continue;
                    if (!nodeToIndex.TryGetValue(entry.Node, out var idx)) continue;
                    var setter            = BuildDoubleSetter(entry.Node);
                    var modelOutputReader = BuildSignalReader(entry.ModelOutputNode);
                    var targetOutputReader = BuildSignalReader(entry.TargetOutputNode);
                    if (setter is null || modelOutputReader is null || targetOutputReader is null) continue;
                    paramEntries.Add((idx, entry, setter, modelOutputReader, targetOutputReader));
                }

            if (paramEntries.Count == 0)
            {
                client.ModelRunFailedValidation(ID,
                    "Calibration requires at least one enabled calibration parameter with both a model output node and a target output node assigned.");
                return;
            }

            if (start!.Module is not IAction modelStart)
            {
                client.ModelRunFailedValidation(ID, "The configured start module does not implement IAction.");
                return;
            }

            // Starting values: read from the current module parameter values.
            double[] current = paramEntries
                .Select(p => GetInitialParameterValue(p.entry.Node))
                .ToArray();

            var originalDir = Directory.GetCurrentDirectory();
            // Install a lightweight RunBus so modules inside the model system can forward
            // status messages to the client via _runtime.RunBus?.SendStatusMessage(...).
            using var loopRunBus = new RunBus(ID, msg => client.SendStatusMessage(ID, msg), _runtime);
            var loopRun = new Run(ID, _modelSystem, StartToExecute, _runtime, _currentWorkingDirectory, RunMode.Calibration);
            loopRunBus.CurrentRun = loopRun;
            try
            {
                Directory.SetCurrentDirectory(_currentWorkingDirectory);
                for (int iter = 0; iter < DefaultMaxCalibrationIterations; iter++)
                {
                    if (_cancelRequested) break;

                    // Apply current parameter values directly to the constructed modules.
                    for (int i = 0; i < paramEntries.Count; i++)
                        paramEntries[i].setter(current[i]);

                    try
                    {
                        modelStart.Invoke();
                    }
                    catch (Exception e)
                    {
                        while (e.InnerException is Exception inner) e = inner;
                        string? moduleName = null;
                        string? elementId = null;
                        if (e is XTMFRuntimeException xe)
                        {
                            if (!Run.TryResolveModelElementForRuntimeModule(ms, xe.FailingModule, out moduleName, out var resolvedElementId))
                                moduleName = xe.FailingModule?.Name;
                            elementId = resolvedElementId?.ToString();
                        }
                        client.ModelRunFailed(ID, e.Message, e.StackTrace, moduleName, elementId);
                        return;
                    }

                    double[] modelledValues = paramEntries.Select(p => p.modelOutputReader()).ToArray();
                    double[] targetValues = paramEntries.Select(p => p.targetOutputReader()).ToArray();
                    
                    // Compute ratios: target / model. Guard against division by zero.
                    double[] ratios = new double[paramEntries.Count];
                    for (int i = 0; i < paramEntries.Count; i++)
                    {
                        ratios[i] = modelledValues[i] != 0 ? targetValues[i] / modelledValues[i] : 1.0;
                    }
                    

                    bool converged = true;
                    for (int i = 0; i < paramEntries.Count; i++)
                    {
                        double delta = Math.Abs(ratios[i] - 1.0);
                        if (delta > paramEntries[i].entry.ErrorTolerance)
                        {
                            converged = false;
                            current[i] = paramEntries[i].entry.Algorithm.Apply(
                                current[i], modelledValues[i], targetValues[i],
                                paramEntries[i].entry.Min, paramEntries[i].entry.Max,
                                paramEntries[i].entry.StepSize);
                        }
                        // if within tolerance: leave current[i] unchanged
                    }

                    double maxDelta = ratios.Select(r => Math.Abs(r - 1.0)).Max();
                    client.SendStatusMessage(ID,
                        $"[Calibration] iteration {iter + 1}: max |ratio-1| = {maxDelta:G4}");
                    var calibProgressValues = paramEntries
                        .Select((p, i) => (p.nodeIndex, current[i]))
                        .ToList();
                    client.SendIterationProgress(ID, iter + 1, maxDelta, calibProgressValues);

                    if (converged) break;
                }
            }
            finally
            {
                Directory.SetCurrentDirectory(originalDir);
                if (ReferenceEquals(loopRunBus.CurrentRun, loopRun))
                    loopRunBus.CurrentRun = null;
            }

            client.SendStatusMessage(ID,
                $"[Calibration] complete. Final parameters: " +
                $"[{string.Join(", ", current.Select(v => v.ToString("G6")))}]");
            var calibrationResults = paramEntries
                .Select((p, i) => (p.nodeIndex, current[i]))
                .ToList();
            client.SendOptimizationResults(ID, calibrationResults);
            client.ModelRunComplete(ID);
        }

        /// <summary>
        /// Loads, constructs, and validates the model system from the stored bytes, then
        /// locates the configured start module.  Performs both structural and runtime
        /// validation in one pass so the model is ready to invoke.
        /// </summary>
        private bool BuildAndValidateModelSystem(
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ModelSystem? ms,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Start? start,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out RunError? error)
        {
            ms = null; start = null;
            string? errorMsg = null, moduleName = null;
            Guid? elementId = null;

            try { Directory.CreateDirectory(_currentWorkingDirectory); }
            catch (IOException e)
            {
                error = new RunError(RunErrorType.Validation, e.Message, null, string.Empty);
                return false;
            }

            var msString = Encoding.UTF8.GetString(_modelSystem);
            if (string.IsNullOrWhiteSpace(msString))
            {
                error = new RunError(RunErrorType.Validation, "Model system data is empty.", null, string.Empty);
                return false;
            }

            if (!ModelSystem.Load(msString, _runtime, out ms, ref errorMsg)
                || !ms!.Construct(_runtime, ref errorMsg)
                || !ms!.Validate(ref moduleName, ref errorMsg, ref elementId))
            {
                error = new RunError(RunErrorType.Validation,
                    errorMsg ?? "Failed to build model system.", moduleName, string.Empty, elementId);
                return false;
            }

            if (!FindStart(ms, Start.ParseStartString(StartToExecute), out start, ref errorMsg))
            {
                error = new RunError(RunErrorType.Validation,
                    errorMsg ?? "Start not found.", null, string.Empty);
                return false;
            }

            if (!RuntimeValidateAll(ms, ref moduleName, ref errorMsg, ref elementId))
            {
                RunResults.WriteValidationError(_currentWorkingDirectory, moduleName, errorMsg);
                error = new RunError(RunErrorType.RuntimeValidation,
                    errorMsg ?? "Runtime validation failed.", moduleName, string.Empty, elementId);
                return false;
            }

            error = null;
            return true;
        }

        private static bool FindStart(
            ModelSystem ms,
            List<string> startPath,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Start? start,
            ref string? error)
        {
            start = null;
            if (startPath.Count == 0) { error = "No start path was defined!"; return false; }
            Boundary current = ms.GlobalBoundary;
            for (int i = 0; i < startPath.Count - 1; i++)
            {
                bool found = false;
                foreach (var child in current.Boundaries)
                {
                    if (child.Name.Equals(startPath[i], StringComparison.OrdinalIgnoreCase))
                    { found = true; current = child; break; }
                }
                if (!found)
                {
                    error = $"Unable to find a child boundary named '{startPath[i]}'."; return false;
                }
            }
            var startName = startPath[startPath.Count - 1];
            foreach (var s in current.Starts)
            {
                if (startName.Equals(s.Name, StringComparison.OrdinalIgnoreCase))
                { start = s; return true; }
            }
            error = $"Unable to find '{startName}' within boundary '{current.FullPath}'.";
            return false;
        }

        private static bool RuntimeValidateAll(
            ModelSystem ms, ref string? moduleName, ref string? errorMessage, ref Guid? elementId)
        {
            var toProcess = new Stack<Boundary>();
            toProcess.Push(ms.GlobalBoundary);
            while (toProcess.TryPop(out var current))
            {
                foreach (var child in current.Boundaries)
                    toProcess.Push(child);
                foreach (var module in current.Modules)
                {
                    if (module.Module is IModule realModule)
                    {
                        try
                        {
                            if (!realModule.RuntimeValidation(ref errorMessage))
                            { moduleName = module.Name; elementId = module.Id; return false; }
                        }
                        catch (Exception e)
                        { moduleName = module.Name; elementId = module.Id; errorMessage = e.Message; return false; }
                    }
                }
                foreach (var fi in current.FunctionInstances)
                {
                    if (!fi.ValidateRuntimeModules(ref moduleName, ref errorMessage, ref elementId))
                        return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Reads the current numeric value from a parameter node's module.  Falls back to
        /// parsing <see cref="Node.ParameterValue"/>, and ultimately defaults to 1.0.
        /// </summary>
        private static double GetInitialParameterValue(Node node)
        {
            var module = node.Module;
            if (module is IFunction<double> fd) return fd.Invoke();
            if (module is IFunction<float>  ff) return (double)ff.Invoke();
            if (node.ParameterValue is { } pv &&
                double.TryParse(pv.ToString(), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var d))
                return d;
            return 1.0;
        }

        /// <summary>
        /// Returns a delegate that sets a <c>double</c> value on the
        /// <see cref="ISetableValue{T}"/> module attached to <paramref name="node"/>.
        /// Returns <c>null</c> if the module does not implement a settable interface.
        /// </summary>
        private static Action<double>? BuildDoubleSetter(Node node)
        {
            var module = node.Module;

            if (module is null) return null;

            if (module is ISetableValue<double> sd) return v =>
            {
                 sd.Set(v);
                _ = node.SetParameterValue(ParameterExpression.CreateParameter(v.ToString(CultureInfo.InvariantCulture), typeof(double)), out var _);
            };
            if (module is ISetableValue<float> sf) return v => 
            {
                sf.Set((float)v);
                _ = node.SetParameterValue(ParameterExpression.CreateParameter(((float)v).ToString(CultureInfo.InvariantCulture), typeof(float)), out var _);
            };
            if (module is BasicParameter<double> bd) return v => 
            { 
                bd.Value = v;
                _ = node.SetParameterValue(ParameterExpression.CreateParameter(v.ToString(CultureInfo.InvariantCulture), typeof(double)), out var _); 
            };
            if (module is BasicParameter<float> bf) return v => 
            { 
                bf.Value = (float)v;
                _ = node.SetParameterValue(ParameterExpression.CreateParameter(((float)v).ToString(CultureInfo.InvariantCulture), typeof(float)), out var _); 
            };

            // Fallback for any other ISetableValue<T> via reflection.
            foreach (var iface in module.GetType().GetInterfaces())
            {
                if (iface.IsGenericType &&
                    iface.GetGenericTypeDefinition() == typeof(ISetableValue<>))
                {
                    var valueType = iface.GetGenericArguments()[0];
                    var setMethod = iface.GetMethod("Set")!;
                    return v => setMethod.Invoke(module, [Convert.ChangeType(v, valueType)]);
                }
            }
            return null;
        }

        /// <summary>
        /// Returns a delegate that reads a <c>double</c> signal from the
        /// <see cref="IFunction{T}"/> module attached to <paramref name="node"/>.
        /// Returns <c>null</c> if the module is not a readable numeric function.
        /// </summary>
        private static Func<double>? BuildSignalReader(Node node)
        {
            var module = node.Module;
            if (module is IFunction<double> fd) return () => fd.Invoke();
            if (module is IFunction<float> ff) return () => (double)ff.Invoke();
            return null;
        }
    }
}
