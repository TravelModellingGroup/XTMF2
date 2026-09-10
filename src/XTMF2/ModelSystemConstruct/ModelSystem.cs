/*
    Copyright 2017-2019 University of Toronto

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
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Linq;
using System.ComponentModel;
using XTMF2.Editing;
using System.IO;
using XTMF2.Repository;
using XTMF2.ModelSystemConstruct;
using System.Diagnostics.CodeAnalysis;
using XTMF2.ModelSystemConstruct.Parameters.Compiler;
using XTMF2.Bus.Optimization;

namespace XTMF2
{
    /// <summary>
    /// Provides detailed access to the model system and control it.
    /// </summary>
    public sealed class ModelSystem : INotifyPropertyChanged
    {
        /// <summary>
        /// Create a new model system with the given header information.
        /// </summary>
        /// <param name="header">The header to create a new model system for.</param>
        public ModelSystem(ModelSystemHeader header)
        {
            Header = header;
            header.PropertyChanged += Header_PropertyChanged;
            GlobalBoundary = new Boundary(GlobalBoundaryName);
        }

        private void Header_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is ModelSystemHeader header)
            {
                switch (e.PropertyName)
                {
                    case nameof(Name):
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
                        break;
                    case nameof(Description):
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description)));
                        break;
                }
            }
        }

        /// <summary>
        /// The name of the model system
        /// </summary>
        public string Name => Header.Name;

        /// <summary>
        /// A description of the model system
        /// </summary>
        public string Description => Header.Description;

        /// <summary>
        /// A reference to the top level boundary of the model system.
        /// </summary>
        public Boundary GlobalBoundary { get; private set; }

        /// <summary>
        /// A reference to the information stored at the project level.
        /// </summary>
        internal ModelSystemHeader Header { get; private set; }

        /// <summary>
        /// The nodes which are allowed to be used as a variable for parameter expressions
        /// </summary>
        public ObservableCollection<Node> Variables { get; private set; } = new ObservableCollection<Node>();

        /// <summary>Named groups of estimation parameters.</summary>
        public ObservableCollection<EstimationGroup> EstimationGroups { get; private set; } = new ObservableCollection<EstimationGroup>();

        /// <summary>
        /// The single fitness function node used for all estimation groups.  Must implement
        /// <c>IFunction&lt;float&gt;</c> or <c>IFunction&lt;double&gt;</c>.
        /// May be <c>null</c> until assigned by the user.
        /// </summary>
        public Node? EstimationFitnessNode
        {
            get => _estimationFitnessNode;
            internal set
            {
                _estimationFitnessNode = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EstimationFitnessNode)));
            }
        }
        private Node? _estimationFitnessNode;

        /// <summary>Named groups of calibration parameters.</summary>
        public ObservableCollection<CalibrationGroup> CalibrationGroups { get; private set; } = new ObservableCollection<CalibrationGroup>();

        /// <summary>
        /// The algorithm configuration used for estimation runs.
        /// Defaults to <see cref="NelderMeadConfig"/> when not set.
        /// </summary>
        public EstimationAlgorithmConfig EstimationAlgorithmConfig
        {
            get => _estimationAlgorithmConfig;
            internal set
            {
                _estimationAlgorithmConfig = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EstimationAlgorithmConfig)));
            }
        }
        private EstimationAlgorithmConfig _estimationAlgorithmConfig = EstimationAlgorithmConfig.Default;

        /// <summary>
        /// Whether the estimation fitness function should be minimised or maximised.
        /// Default is <see cref="EstimationObjective.Minimize"/>.
        /// </summary>
        public EstimationObjective EstimationObjective
        {
            get => _estimationObjective;
            internal set
            {
                _estimationObjective = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EstimationObjective)));
            }
        }
        private EstimationObjective _estimationObjective = EstimationObjective.Minimize;

        /// <summary>
        /// Maps the integer serialisation index to each <see cref="Node"/>.
        /// Populated by <see cref="Load(Stream, ModuleRepository, ModelSystemHeader, ref string?)"/>
        /// and used by the optimisation loop to locate parameter nodes by index.
        /// </summary>
        internal IReadOnlyDictionary<int, Node>? NodesByLoadIndex { get; private set; }

        private const string GlobalBoundaryName = "global";
        private const string IndexProperty = "Index";
        private const string TypeProperty = "Type";
        private const string TypesProperty = "Types";
        private const string BoundariesProperty = "Boundaries";
        private const string VariablesProperty = "Variables";
        private const string EstimationGroupsProperty      = "EstimationGroups";
        private const string EstimationFitnessNodeProperty = "EstimationFitnessNode";
        private const string EstimationAlgorithmConfigProperty = "EstimationAlgorithmConfig";
        private const string EstimationObjectiveProperty   = "EstimationObjective";
        private const string CalibrationGroupsProperty     = "CalibrationGroups";

        /// <summary>
        /// The lock that must be acquired before editing the model system's attributes.
        /// </summary>
        private readonly object _modelSystemLock = new object();

        public event PropertyChangedEventHandler? PropertyChanged;

        internal bool Save(ref string? error)
        {
            lock (_modelSystemLock)
            {
                try
                {
                    // Writ the file to a temporary location first, and then move it to the final location. This is to prevent data loss in the case of an error during the save process.
                    var tempPath = Path.GetTempFileName();
                    var tempFileInfo = new FileInfo(tempPath);
                    bool saveResult = false;
                    using (var stream = tempFileInfo.OpenWrite())
                    {
                        saveResult = Save(ref error, stream, false);
                    }
                    if (saveResult)
                    {
                        var fileInfo = new FileInfo(Header.ModelSystemPath);
                        var dir = fileInfo.Directory;
                        if (dir is null)
                        {
                            error = $"The provided path '{Header.ModelSystemPath}' was invalid!";
                            return false;
                        }
                        if (!dir.Exists)
                        {
                            dir.Create();
                        }
                        File.Move(tempPath, fileInfo.FullName, true);
                    }
                    return saveResult;
                }
                catch (IOException e)
                {
                    error = e.Message;
                    return false;
                }
            }
        }

        private bool Save(ref string? error, Stream saveTo, bool leaveOpen)
        {
            try
            {
                using var writer = new Utf8JsonWriter(saveTo);
                var typeDictionary = GlobalBoundary.GetUsedTypes();
                writer.WriteStartObject();
                WriteTypes(writer, typeDictionary);
                var nodeDictionary = WriteBoundaries(writer, typeDictionary);
                // Keep the reverse mapping in sync so the host can map optimization-result
                // node indices back to live Node objects after a run completes.
                NodesByLoadIndex = new System.Collections.ObjectModel.ReadOnlyDictionary<int, Node>(
                    nodeDictionary.ToDictionary(kv => kv.Value, kv => kv.Key));
                WriteVariables(writer, nodeDictionary);
                WriteEstimationGroups(writer, nodeDictionary);
                WriteEstimationFitnessNode(writer, nodeDictionary);
                WriteEstimationAlgorithmConfig(writer);
                WriteEstimationObjective(writer);
                WriteCalibrationGroups(writer, nodeDictionary);
                writer.WriteEndObject();
                return true;
            }
            catch (IOException e)
            {
                error = e.Message;
                return false;
            }
            finally
            {
                if (!leaveOpen)
                {
                    saveTo.Dispose();
                }
            }
        }

        /// <summary>
        /// Check that all requirements have been met when constructing the model system.
        /// </summary>
        /// <param name="moduleName">The name of the module that is causing the validation error.</param>
        /// <param name="error">An error message if the validation fails.</param>
        /// <returns>True if the validation passes, false otherwise with an error message.</returns>
        internal bool Validate(ref string? moduleName, ref string? error)
        {
            return GlobalBoundary.Validate(ref moduleName, ref error);
        }

        /// <summary>
        /// Check that all requirements have been met when constructing the model system,
        /// returning the failing element ID when available.
        /// </summary>
        internal bool Validate(ref string? moduleName, ref string? error, ref Guid? elementId)
        {
            return GlobalBoundary.Validate(ref moduleName, ref error, ref elementId);
        }

        /// <summary>
        /// Generate the concrete model system for execution.
        /// </summary>
        /// <param name="runtime">The XTMF run time that we are executing within.</param>
        /// <param name="error">An error message if it can not be constructed.</param>
        /// <param name="elementId">The ID of the element that is causing the construction error.</param>
        /// <returns>True if it was created, false with message otherwise.</returns>
        internal bool Construct(XTMFRuntime runtime, ref string? error, ref Guid? elementId)
        {
            return GlobalBoundary.ConstructModules(runtime, ref error, ref elementId)
                && GlobalBoundary.ConstructLinks(ref error, ref elementId)
                && GlobalBoundary.ConstructEmptyLinks(ref error, ref elementId);;
        }

        /// <summary>
        /// Save the data to the given stream.
        /// This will not close the stream.
        /// </summary>
        /// <param name="error">An error message if the save fails.</param>
        /// <param name="saveTo">The stream to save the data to.</param>
        /// <returns></returns>
        internal bool Save(ref string? error, Stream saveTo)
        {
            return Save(ref error, saveTo, true);
        }

        private Dictionary<Node, int> WriteBoundaries(Utf8JsonWriter writer, Dictionary<Type, int> typeDictionary)
        {
            int index = 0;
            writer.WritePropertyName(BoundariesProperty);
            writer.WriteStartArray();
            Dictionary<Node, int> nodeDictionary = new Dictionary<Node, int>();
            // Pre-assign indices for ALL nodes (including ghost nodes that may reference
            // siblings) so that link serialisation can reference any node by index.
            GlobalBoundary.PreAssignNodeIndices(ref index, nodeDictionary);
            GlobalBoundary.Save(ref index, nodeDictionary, typeDictionary, writer);
            writer.WriteEndArray();
            return nodeDictionary;
        }

        private void WriteVariables(Utf8JsonWriter writer, Dictionary<Node, int> nodeDictionary)
        {
            writer.WritePropertyName(VariablesProperty);
            writer.WriteStartArray();
            foreach (var node in Variables)
            {
                if (nodeDictionary.TryGetValue(node, out var idx))
                    writer.WriteNumberValue(idx);
            }
            writer.WriteEndArray();
        }

        private void WriteEstimationGroups(Utf8JsonWriter writer, Dictionary<Node, int> nodeDictionary)
        {
            writer.WritePropertyName(EstimationGroupsProperty);
            writer.WriteStartArray();
            foreach (var group in EstimationGroups)
                group.Save(writer, nodeDictionary);
            writer.WriteEndArray();
        }

        private void WriteEstimationFitnessNode(Utf8JsonWriter writer, Dictionary<Node, int> nodeDictionary)
        {
            if (_estimationFitnessNode is not null
                && nodeDictionary.TryGetValue(_estimationFitnessNode, out var idx))
                writer.WriteNumber(EstimationFitnessNodeProperty, idx);
        }

        private void WriteEstimationAlgorithmConfig(Utf8JsonWriter writer)
        {
            writer.WritePropertyName(EstimationAlgorithmConfigProperty);
            _estimationAlgorithmConfig.Save(writer);
        }

        private void WriteEstimationObjective(Utf8JsonWriter writer)
        {
            writer.WriteString(EstimationObjectiveProperty, _estimationObjective.ToString());
        }

        private void WriteCalibrationGroups(Utf8JsonWriter writer, Dictionary<Node, int> nodeDictionary)
        {
            writer.WritePropertyName(CalibrationGroupsProperty);
            writer.WriteStartArray();
            foreach (var group in CalibrationGroups)
                group.Save(writer, nodeDictionary);
            writer.WriteEndArray();
        }

        private static void WriteTypes(Utf8JsonWriter writer, Dictionary<Type, int> typeDictionary)
        {
            writer.WritePropertyName(TypesProperty);
            writer.WriteStartArray();
            foreach (var type in typeDictionary)
            {
                writer.WriteStartObject();
                writer.WriteNumber(IndexProperty, type.Value);
                writer.WriteString(TypeProperty, type.Key.AssemblyQualifiedName);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        /// <summary>
        /// Check to see if the given boundary is within the model system.
        /// </summary>
        /// <param name="boundary"></param>
        /// <returns></returns>
        internal bool Contains(Boundary boundary)
        {
            lock (_modelSystemLock)
            {
                if (GlobalBoundary == boundary)
                {
                    return true;
                }
                return GlobalBoundary.Contains(boundary);
            }
        }

        internal static bool Load(string modelSystem, XTMFRuntime runtime, [NotNullWhen(true)] out ModelSystem? ms, [NotNullWhen(false)] ref string? error)
        {
            byte[]? converted = null;
            try
            {
                converted =  Encoding.UTF8.GetBytes(modelSystem);
            }
            catch(Exception e)
            {
                error = "Failed when converting model system to bytes! + \r\n" + e.Message;
                ms = null;
                return false;
            }
            using var stream = new MemoryStream(converted);
            var header = ModelSystemHeader.CreateRunHeader(runtime);
            ms = Load(stream, runtime.Modules, header, ref error, out _);
            if(error is not null)
            {
                error = "Failed when running the Load method of the model system! \r\n" + error;
            }
            return ms != null;
        }

        internal static bool Load(ProjectSession session, ModelSystemHeader modelSystemHeader, [NotNullWhen(true)] out ModelSystemSession? msSession, [NotNullWhen(false)] out CommandError? error, out List<string>? warnings)
        {
            // the parameters are have already been vetted
            var path = modelSystemHeader.ModelSystemPath;
            var info = new FileInfo(path);
            string? errorString = null;
            error = null;
            warnings = null;

            try
            {
                ModelSystem? ms;
                if (info.Exists)
                {
                    using var rawStream = File.OpenRead(modelSystemHeader.ModelSystemPath);
                    ms = Load(rawStream, session.GetModuleRepository(), modelSystemHeader, ref errorString, out var loadWarnings);
                    if (loadWarnings.Count > 0)
                        warnings = loadWarnings;
                }
                else
                {
                    ms = new ModelSystem(modelSystemHeader);
                }
                if (ms == null)
                {
                    msSession = null;
                    // Give a generic error message if one was not already supplied.
                    error = new CommandError(errorString ?? "Unable to create a model system session for the given header.");
                    return false;
                }
                msSession = new ModelSystemSession(session, ms);
                error = null;
                return true;
            }
            catch (IOException e)
            {
                error = new CommandError(e.Message);
                msSession = null;
                return false;
            }
        }

        internal static ModelSystem? Load(Stream rawStream, ModuleRepository modules, ModelSystemHeader modelSystemHeader,
            [NotNullWhen(false)] ref string? error, out List<string> warnings)
        {
            warnings = new List<string>();
            var capturedWarnings = warnings;
            try
            {
                var modelSystem = new ModelSystem(modelSystemHeader);
                using var stream = new MemoryStream();
                rawStream.CopyTo(stream);
                // Use only the bytes that were actually written (GetBuffer returns the full allocation
                // which may be padded with null bytes — those would cause JSON parse errors).
                var reader = new Utf8JsonReader(stream.GetBuffer().AsSpan(0, (int)stream.Length));
                var typeLookup = new Dictionary<int, Type>();
                var nodes = new Dictionary<int, Node>();
                List<(Node toAssignTo, string parameterExpression)> scriptedParameters = new();
                List<(Boundary ContainedIn, int RefIndex, int SelfIndex, Rectangle Location, Guid Id)> deferredGhostNodes = new();
                List<(Boundary ContainedIn, Node Origin, string HookName, int DestinationIndex, bool Disabled, bool Orthogonal, bool DestinationHidden, Guid LinkId, double? BreakpointX)> deferredLinks = new();
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        if (reader.ValueTextEquals(TypesProperty))
                        {
                            if (!LoadTypes(typeLookup, ref reader, ref error, capturedWarnings))
                            {
                                return null;
                            }
                        }
                        else if (reader.ValueTextEquals(BoundariesProperty))
                        {
                            if (!LoadBoundaries(modules, typeLookup, nodes, scriptedParameters, deferredGhostNodes, ref reader, modelSystem.GlobalBoundary, ref error, capturedWarnings, deferredLinks))
                            {
                                return null;
                            }
                        }
                        else if (reader.ValueTextEquals(VariablesProperty))
                        {
                            if (!LoadVariables(nodes, ref reader, modelSystem, ref error))
                            {
                                return null;
                            }
                        }
                        else if (reader.ValueTextEquals(EstimationGroupsProperty))
                        {
                            if (!LoadEstimationGroups(nodes, ref reader, modelSystem, ref error))
                            {
                                return null;
                            }
                        }
                        else if (reader.ValueTextEquals(EstimationFitnessNodeProperty))
                        {
                            reader.Read();
                            if (nodes.TryGetValue(reader.GetInt32(), out var fn))
                                modelSystem.EstimationFitnessNode = fn;
                        }
                        else if (reader.ValueTextEquals(EstimationAlgorithmConfigProperty))
                        {
                            reader.Read(); // move to StartObject
                            if (reader.TokenType == JsonTokenType.StartObject)
                            {
                                var cfg = EstimationAlgorithmConfig.Load(ref reader);
                                if (cfg is not null)
                                    modelSystem.EstimationAlgorithmConfig = cfg;
                            }
                        }
                        else if (reader.ValueTextEquals(EstimationObjectiveProperty))
                        {
                            reader.Read();
                            if (Enum.TryParse<EstimationObjective>(reader.GetString(), out var obj))
                                modelSystem.EstimationObjective = obj;
                        }
                        else if (reader.ValueTextEquals(CalibrationGroupsProperty))
                        {
                            if (!LoadCalibrationGroups(nodes, ref reader, modelSystem, ref error))
                            {
                                return null;
                            }
                        }
                        // Unknown properties are silently skipped for forward compatibility.
                    }
                }
                // Resolve deferred ghost nodes now that all boundaries and nodes are loaded.
                foreach (var (containedIn, refIndex, selfIndex, location, id) in deferredGhostNodes)
                {
                    if (!GhostNode.Resolve(nodes, containedIn, refIndex, selfIndex, location, id, out var ghost, ref error))
                    {
                        // Non-fatal: skip ghost nodes that can't be resolved (e.g. referenced node was removed).
                        continue;
                    }
                    containedIn.AddGhostNode(ghost!, out _);
                }
                // Resolve deferred links (e.g. inner FunctionTemplate links whose destination
                // FunctionInstance was not yet in the node dictionary when the link was first parsed).
                foreach (var (containedIn, origin, hookName, destIdx, disabled, orthogonal, destHidden, linkId, breakpointX) in deferredLinks)
                {
                    if (!nodes.TryGetValue(destIdx, out var destination))
                    {
                        capturedWarnings?.Add($"Deferred link from '{origin.Name}' via '{hookName}' could not be resolved: destination index {destIdx} not found.");
                        continue;
                    }
                    var hook = origin is FunctionInstance dfi
                        ? dfi.Hooks.FirstOrDefault(h => h.Name.Equals(hookName, StringComparison.OrdinalIgnoreCase))
                        : modules[origin.Type!].Hooks?.FirstOrDefault(h => h.Name.Equals(hookName, StringComparison.OrdinalIgnoreCase));
                    if (hook is null)
                    {
                        capturedWarnings?.Add($"Deferred link from '{origin.Name}': hook '{hookName}' not found.");
                        continue;
                    }
                    containedIn.AddLink(new SingleLink(origin, hook, destination, disabled, orthogonal, destHidden, linkId, breakpointX), out _);
                }
                // Expose the node index mapping for the optimisation loop.
                modelSystem.NodesByLoadIndex = new System.Collections.ObjectModel.ReadOnlyDictionary<int, Node>(nodes);
                // Now that all of the modules have been loaded we can process the scripted parameters
                foreach (var (toAssignTo, parameterExpression) in scriptedParameters)
                {
                    // Nodes inside a FunctionTemplate's InternalModules have template-local
                    // variables that shadow the global ones; combine them (local first).
                    var localVars = toAssignTo.ContainedWithin?.OwningFunctionTemplate?.LocalVariables;
                    IList<Node> allVars = localVars is { Count: > 0 }
                        ? localVars.Concat(modelSystem.Variables).ToList()
                        : (IList<Node>)modelSystem.Variables;
                    if (!toAssignTo.SetParameterExpression(allVars, parameterExpression, out CommandError? cmdError))
                    {
                        // TODO: Think about what to do in order to heal the model system
                    }
                }
                return modelSystem;
            }
            catch (JsonException e)
            {
                error = e.Message;
            }
            catch (IOException e)
            {
                error = e.Message;
            }
            return null;
        }

        private static bool FailWith(out string error, string message)
        {
            error = message;
            return false;
        }

        private static bool LoadVariables(Dictionary<int, Node> nodes, ref Utf8JsonReader reader, ModelSystem modelSystem, [NotNullWhen(false)] ref string? error)
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
            {
                return FailWith(out error, "Expected an array when loading variables!");
            }
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType == JsonTokenType.Number)
                {
                    var idx = reader.GetInt32();
                    if (nodes.TryGetValue(idx, out var node))
                        modelSystem.Variables.Add(node);
                }
            }
            return true;
        }

        private static bool LoadEstimationGroups(Dictionary<int, Node> nodes, ref Utf8JsonReader reader, ModelSystem modelSystem, [NotNullWhen(false)] ref string? error)
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
                return FailWith(out error, "Expected an array when loading estimation groups!");
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType == JsonTokenType.StartObject)
                {
                    var group = EstimationGroup.Load(nodes, ref reader, ref error);
                    if (group is not null)
                        modelSystem.EstimationGroups.Add(group);
                }
            }
            return true;
        }

        private static bool LoadCalibrationGroups(Dictionary<int, Node> nodes, ref Utf8JsonReader reader, ModelSystem modelSystem, [NotNullWhen(false)] ref string? error)
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
                return FailWith(out error, "Expected an array when loading calibration groups!");
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType == JsonTokenType.StartObject)
                {
                    var group = CalibrationGroup.Load(nodes, ref reader, ref error);
                    if (group is not null)
                        modelSystem.CalibrationGroups.Add(group);
                }
            }
            return true;
        }

        private static bool LoadTypes(Dictionary<int, Type> typeLookup, ref Utf8JsonReader reader, [NotNullWhen(false)] ref string? error, List<string>? warnings = null)
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
            {
                return FailWith(out error, "Expected to read in an array of types!");
            }
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                string? type = null;
                int index = -1;
                if (reader.TokenType != JsonTokenType.StartObject)
                {
                    return FailWith(out error, "Expected a start object token when starting to read in a type.");
                }
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        return FailWith(out error, "Invalid index!");
                    }
                    if (reader.ValueTextEquals(IndexProperty))
                    {
                        reader.Read();
                        if (reader.TokenType != JsonTokenType.Number)
                        {
                            return FailWith(out error, "While reading types we encountered an invalid index!");
                        }
                        index = reader.GetInt32();
                    }
                    else if (reader.ValueTextEquals(TypeProperty))
                    {
                        reader.Read();
                        type = reader.GetString();
                    }
                }
                if (type == null || index < 0)
                {
                    return FailWith(out error, $"An invalid type entry was found!");
                }
                var trueType = Type.GetType(type);
                if (trueType == null)
                {
                    warnings?.Add($"The type '{type}' could not be found. Nodes using this type will be skipped.");
                    continue;
                }
                if (typeLookup.ContainsKey(index))
                {
                    return FailWith(out error, $"While reading types the index {index} was previously defined!");
                }
                typeLookup.Add(index, trueType);
            }
            return true;
        }

        private static bool LoadBoundaries(ModuleRepository modules, Dictionary<int, Type> typeLookup, Dictionary<int, Node> nodes,
            List<(Node toAssignTo, string parameterExpression)> scriptedParameters,
            List<(Boundary ContainedIn, int RefIndex, int SelfIndex, Rectangle Location, Guid Id)> deferredGhostNodes,
            ref Utf8JsonReader reader, Boundary global, [NotNullWhen(false)] ref string? error, List<string>? warnings = null,
            List<(Boundary ContainedIn, Node Origin, string HookName, int DestinationIndex, bool Disabled, bool Orthogonal, bool DestinationHidden, Guid LinkId, double? BreakpointX)>? deferredLinks = null)
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
            {
                return FailWith(out error, "Expected to read an array when loading boundaries!");
            }

            if (!reader.Read())
            {
                return FailWith(out error, "Unexpected end of file when loading boundaries!");
            }

            if (!global.Load(modules, typeLookup, nodes, scriptedParameters, deferredGhostNodes, ref reader, ref error, warnings, deferredLinks))
            {
                return false;
            }

            if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
            {
                return FailWith(out error, "Expected to only have one boundary defined in the root!");
            }
            return true;
        }
    }
}
