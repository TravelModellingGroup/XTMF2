/*
    Copyright 2021, Travel Modelling Group, University of Toronto

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
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using XTMF2.Editing;
using XTMF2.Repository;
using XTMF2.RuntimeModules;

namespace XTMF2.ModelSystemConstruct
{
    /// <summary>
    /// This class provides the logic for creating a function template.
    /// Function templates are then used in a model system by instantiating all of the
    /// needed references via <see cref="FunctionInstance"/>.
    /// <para>
    /// A function template may declare zero or more <see cref="FunctionParameters"/>.
    /// Each <see cref="FunctionParameter"/> acts as a typed placeholder inside the
    /// template's <see cref="InternalModules"/> boundary: internal nodes link <em>to</em>
    /// parameters, and the concrete module is provided at run-time by the
    /// <see cref="FunctionInstance"/> that instantiates this template.
    /// </para>
    /// </summary>
    public sealed class FunctionTemplate : INotifyPropertyChanged
    {
        // ── JSON property names ───────────────────────────────────────────
        private const string NameProperty               = "Name";
        private const string LocationProperty           = "Location";
        private const string FunctionParametersProperty = "FunctionParameters";
        private const string EntryNodeProperty          = "EntryNode";
        private const string LocalVariablesProperty     = "LocalVariables";
        private const string LocationXProperty          = "X";
        private const string LocationYProperty          = "Y";
        private const string LocationWProperty          = "Width";
        private const string LocationHProperty          = "Height";

        private string _name = String.Empty;

        /// <summary>
        /// The unique name of the function template within containing boundary
        /// </summary>
        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }

        // ── Entry node ───────────────────────────────────────────────────
        private Node? _entryNode;

        /// <summary>
        /// The node in <see cref="InternalModules"/> that defines the runtime type of this
        /// function template. Any node in InternalModules may serve as the entry node.
        /// When set, the template's <see cref="Type"/> property reflects the entry node's type,
        /// and <see cref="FunctionInstance"/> objects whose hooks point to this template will
        /// participate in the same type-compatibility checks as regular nodes.
        /// </summary>
        public Node? EntryNode => _entryNode;

        /// <summary>
        /// The module type of the <see cref="EntryNode"/>, or <c>null</c> when no entry
        /// node has been assigned. This type is used by link compatibility checks so that
        /// a hook expecting, e.g., <c>IAction</c> can connect to a
        /// <see cref="FunctionInstance"/> backed by this template.
        /// </summary>
        public Type? Type => _entryNode?.Type;

        /// <summary>
        /// Designates <paramref name="entryNode"/> as the entry node for this template.
        /// Pass <c>null</c> to clear the entry-node assignment.
        /// Called only by <see cref="Editing.ModelSystemSession"/> so that the change
        /// participates in undo/redo.
        /// </summary>
        internal void SetEntryNode(Node? entryNode)
        {
            _entryNode = entryNode;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EntryNode)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Type)));
        }

        // ── Canvas location ───────────────────────────────────────────────
        private Rectangle _location = new Rectangle(40, 40, 200, 120);

        /// <summary>
        /// The position and size of this function template's container box on the canvas.
        /// </summary>
        public Rectangle Location
        {
            get => _location;
        }

        /// <summary>Sets the canvas location of the function template (called by the session).</summary>
        internal void SetLocation(Rectangle location)
        {
            _location = location;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Location)));
        }

        // ── Local variables (nodes in InternalModules that can be referenced by
        //    name in scripted parameter expressions inside the template) ────
        private readonly ObservableCollection<Node> _localVariables = new();

        /// <summary>
        /// Nodes inside <see cref="InternalModules"/> that are designated as template-local
        /// variables. These are resolved BEFORE <see cref="ModelSystem.Variables"/> when
        /// compiling scripted expressions for nodes inside this template. Scripts on
        /// boundaries outside the template cannot see these variables.
        /// </summary>
        public ReadOnlyObservableCollection<Node> LocalVariables { get; private set; } = null!;

        // ── Function parameters ───────────────────────────────────────────
        private readonly ObservableCollection<FunctionParameter> _functionParameters = new();

        /// <summary>
        /// The typed parameter slots declared on this function template.
        /// <para>
        /// Each <see cref="FunctionParameter"/> is a virtual placeholder inside
        /// <see cref="InternalModules"/>: internal nodes link <em>to</em> a parameter,
        /// and the actual module is supplied at run-time by the
        /// <see cref="FunctionInstance"/> that instantiates this template.
        /// On the parent boundary's canvas a <see cref="FunctionInstance"/> exposes one
        /// outgoing hook per <see cref="FunctionParameter"/>, allowing external nodes to
        /// be wired in.
        /// </para>
        /// </summary>
        public ReadOnlyObservableCollection<FunctionParameter> FunctionParameters { get; }

        /// <summary>
        /// Adds a new <see cref="FunctionParameter"/> to this template.
        /// The name must be unique within this template.
        /// </summary>
        internal bool AddFunctionParameter(string name, Type type, Rectangle location,
            [NotNullWhen(true)] out FunctionParameter? parameter,
            [NotNullWhen(false)] out CommandError? error)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                parameter = null;
                error = new CommandError("A FunctionParameter name must not be empty.");
                return false;
            }
            if (_functionParameters.Any(fp => fp.Name.Equals(name, StringComparison.Ordinal)))
            {
                parameter = null;
                error = new CommandError($"A FunctionParameter named '{name}' already exists in template '{Name}'.");
                return false;
            }
            parameter = new FunctionParameter(name, type, this, location);
            _functionParameters.Add(parameter);
            error = null;
            return true;
        }

        /// <summary>
        /// Forcibly adds an already-constructed <see cref="FunctionParameter"/> back to the
        /// collection (used for undo of a removal).
        /// </summary>
        internal void RestoreFunctionParameter(FunctionParameter parameter, int index)
        {
            if (!_functionParameters.Contains(parameter))
                _functionParameters.Insert(Math.Min(index, _functionParameters.Count), parameter);
        }

        /// <summary>
        /// Removes a <see cref="FunctionParameter"/> from this template.
        /// </summary>
        internal bool RemoveFunctionParameter(FunctionParameter parameter,
            [NotNullWhen(false)] out CommandError? error)
        {
            if (!_functionParameters.Remove(parameter))
            {
                error = new CommandError($"FunctionParameter '{parameter.Name}' was not found in template '{Name}'.");
                return false;
            }
            error = null;
            return true;
        }

        /// <summary>
        /// Renames <paramref name="parameter"/> to <paramref name="newName"/>.
        /// The new name must be unique within this template.
        /// </summary>
        internal bool RenameFunctionParameter(FunctionParameter parameter, string newName,
            [NotNullWhen(false)] out CommandError? error)
        {
            if (string.IsNullOrWhiteSpace(newName))
            {
                error = new CommandError("A FunctionParameter name must not be empty.");
                return false;
            }
            if (_functionParameters.Any(fp => !ReferenceEquals(fp, parameter)
                && fp.Name.Equals(newName, StringComparison.Ordinal)))
            {
                error = new CommandError($"A FunctionParameter named '{newName}' already exists in template '{Name}'.");
                return false;
            }
            parameter.SetName(newName, out _);
            error = null;
            return true;
        }

        /// <summary>
        /// This boundary provides the location for modules that are contained within the function template.
        /// These modules can not be referenced from outside of the function template.
        /// </summary>
        public Boundary InternalModules { get; private set; }

        /// <summary>
        /// The boundary that this function template belongs to.
        /// </summary>
        public Boundary Parent { get; }

        /// <summary>
        /// Construct a new function template
        /// </summary>
        /// <param name="name">The name of the function template.</param>
        /// <param name="parent">The boundary that owns this function template.</param>
        /// <param name="internalModules">An optional boundary to use as the InternalModules of this template; if null, an empty boundary will be created.</param>
        public FunctionTemplate(string name, Boundary parent, Boundary? internalModules = null)
        {
            _name = name;
            Parent = parent;
            InternalModules = internalModules ?? new Boundary("InternalModules", parent);
            // Register this template as the owner of InternalModules so that expression
            // compilation can discover the local variable scope from any node inside it.
            InternalModules.OwningFunctionTemplate = this;
            FunctionParameters = new ReadOnlyObservableCollection<FunctionParameter>(_functionParameters);
            LocalVariables     = new ReadOnlyObservableCollection<Node>(_localVariables);
        }

        // ── Local variable management (called by ModelSystemSession) ──────

        /// <summary>
        /// Returns <c>true</c> if <paramref name="node"/> is eligible to be a local variable.
        /// A node is eligible when it is inside <see cref="InternalModules"/> and its effective
        /// parameter type is one of the four basic types (bool, int, float, string), either
        /// directly (e.g. the node is a <c>BasicParameter&lt;int&gt;</c>) or via
        /// <see cref="IFunction{T}"/> (e.g. a <see cref="FunctionParameter"/> whose type is
        /// <c>IFunction&lt;int&gt;</c>).
        /// </summary>
        public static bool IsValidLocalVariableNode(Node node, [NotNullWhen(false)] out CommandError? error)
        {
            Type? t;
            if (node is FunctionParameter fp)
            {
                t = ExtractIFunctionInnerType(fp.Type);
            }
            else
            {
                // Prefer ParameterValue.Type (most accurate at runtime), but fall back to the
                // generic argument of the node's module type so that a freshly-created
                // BasicParameter<int> node (ParameterValue still null) is still eligible.
                t = node.ParameterValue?.Type ?? ExtractBasicParameterInnerType(node.Type);
            }
            if (t is null)
            {
                error = new CommandError(
                    $"Node '{node.Name}' does not have a basic-type parameter value and cannot be used as a local variable.");
                return false;
            }
            if (t != typeof(bool) && t != typeof(int) && t != typeof(float) && t != typeof(string))
            {
                error = new CommandError(
                    $"Node '{node.Name}' has type '{t.FullName}' which is not a supported variable type (bool, int, float, string).");
                return false;
            }
            error = null;
            return true;
        }

        /// <summary>
        /// If <paramref name="type"/> is <c>IFunction&lt;T&gt;</c> for a basic supported T,
        /// returns T; otherwise returns null.
        /// </summary>
        public static Type? ExtractIFunctionInnerType(Type? type)
        {
            if (type is null || !type.IsGenericType) return null;
            if (type.GetGenericTypeDefinition() != typeof(IFunction<>)) return null;
            var inner = type.GetGenericArguments()[0];
            return (inner == typeof(bool) || inner == typeof(int)
                 || inner == typeof(float) || inner == typeof(string))
                ? inner : null;
        }

        /// <summary>
        /// If <paramref name="nodeType"/> is the closed generic form of
        /// <see cref="RuntimeModules.BasicParameter{T}"/> or
        /// <see cref="RuntimeModules.ScriptedParameter{T}"/> for a supported basic T,
        /// returns T; otherwise returns null.
        /// </summary>
        private static Type? ExtractBasicParameterInnerType(Type? nodeType)
        {
            if (nodeType is null || !nodeType.IsGenericType) return null;
            var td = nodeType.GetGenericTypeDefinition();
            if (td != typeof(RuntimeModules.BasicParameter<>)
             && td != typeof(RuntimeModules.ScriptedParameter<>))
                return null;
            var inner = nodeType.GetGenericArguments()[0];
            return (inner == typeof(bool) || inner == typeof(int)
                 || inner == typeof(float) || inner == typeof(string))
                ? inner : null;
        }

        /// <summary>
        /// Adds <paramref name="node"/> to <see cref="LocalVariables"/>.
        /// The node must be inside <see cref="InternalModules"/> and not already present.
        /// </summary>
        internal bool AddLocalVariable(Node node, [NotNullWhen(false)] out CommandError? error)
        {
            if (!IsValidLocalVariableNode(node, out error)) return false;
            if (_localVariables.Contains(node))
            {
                error = new CommandError($"Node '{node.Name}' is already a local variable of template '{Name}'.");
                return false;
            }
            _localVariables.Add(node);
            error = null;
            return true;
        }

        /// <summary>
        /// Removes <paramref name="node"/> from <see cref="LocalVariables"/>.
        /// </summary>
        internal bool RemoveLocalVariable(Node node, [NotNullWhen(false)] out CommandError? error)
        {
            if (!_localVariables.Remove(node))
            {
                error = new CommandError($"Node '{node.Name}' is not a local variable of template '{Name}'.");
                return false;
            }
            error = null;
            return true;
        }

        /// <summary>
        /// Save the function template to the stream.
        /// </summary>
        /// <param name="index">A counting for module indexes.</param>
        /// <param name="nodeDictionary">A lookup given an index of contained nodes.</param>
        /// <param name="typeDictionary">The known types and indexes for them.</param>
        /// <param name="writer">The stream that is being written to.</param>
        internal void Save(ref int index, Dictionary<Node, int> nodeDictionary, Dictionary<Type, int> typeDictionary, Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString(NameProperty, Name);

            // Location
            writer.WritePropertyName(LocationProperty);
            writer.WriteStartObject();
            writer.WriteNumber(LocationXProperty, Location.X);
            writer.WriteNumber(LocationYProperty, Location.Y);
            writer.WriteNumber(LocationWProperty, Location.Width);
            writer.WriteNumber(LocationHProperty, Location.Height);
            writer.WriteEndObject();

            // FunctionParameters BEFORE InternalModules so their indices are defined
            // before any internal links that reference them as destinations are written.
            writer.WritePropertyName(FunctionParametersProperty);
            writer.WriteStartArray();
            foreach (var fp in _functionParameters)
            {
                // Ensure the type is recorded.
                if (!typeDictionary.ContainsKey(fp.Type!))
                    typeDictionary[fp.Type!] = typeDictionary.Count;
                fp.Save(ref index, nodeDictionary, typeDictionary, writer);
            }
            writer.WriteEndArray();

            // Internal modules (must come after FunctionParameters so FP indices are established).
            writer.WritePropertyName(nameof(InternalModules));
            InternalModules.Save(ref index, nodeDictionary, typeDictionary, writer);

            // Entry node – stored as an integer index (null means not set)
            if (_entryNode != null && nodeDictionary.TryGetValue(_entryNode, out int entryIdx))
                writer.WriteNumber(EntryNodeProperty, entryIdx);

            // Local variables – stored as an array of node indices
            if (_localVariables.Count > 0)
            {
                writer.WritePropertyName(LocalVariablesProperty);
                writer.WriteStartArray();
                foreach (var lv in _localVariables)
                {
                    if (nodeDictionary.TryGetValue(lv, out int lvIdx))
                        writer.WriteNumberValue(lvIdx);
                }
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        /// <summary>
        /// Load the Function template from the given stream.
        /// </summary>
        /// <param name="modules">The repository of modules.</param>
        /// <param name="typeLookup">A lookup from index to type.</param>
        /// <param name="node">A reference from node index to node object.</param>
        /// <param name="reader">The reader to use for parsing the FunctionTemplate and its children.</param>
        /// <param name="parent">The boundary that contains this function template.</param>
        /// <param name="template">The function template that was created by loading the file.</param>
        /// <param name="error">An error message if we failed to load the function template or its children.</param>
        /// <returns>True if the operation succeeded, false otherwise with an error message.</returns>
        internal static bool Load(ModuleRepository modules, Dictionary<int, Type> typeLookup, Dictionary<int, Node> node, List<(Node toAssignTo, string parameterExpression)> scriptedParameters,
            ref Utf8JsonReader reader, Boundary parent, [NotNullWhen(true)] out FunctionTemplate? template, [NotNullWhen(false)] ref string? error)
        {
            List<(Boundary ContainedIn, int RefIndex, int SelfIndex, Rectangle Location)> deferredGhostNodes = new();
            return Load(modules, typeLookup, node, scriptedParameters, deferredGhostNodes, ref reader, parent, out template, ref error);
        }

        internal static bool Load(ModuleRepository modules, Dictionary<int, Type> typeLookup, Dictionary<int, Node> node,
            List<(Node toAssignTo, string parameterExpression)> scriptedParameters,
            List<(Boundary ContainedIn, int RefIndex, int SelfIndex, Rectangle Location)> deferredGhostNodes,
            ref Utf8JsonReader reader, Boundary parent,
            [NotNullWhen(true)] out FunctionTemplate? template,
            [NotNullWhen(false)] ref string? error)
        {
            template = null;
            string? name = null;
            Rectangle location = new Rectangle(40, 40, 200, 120);
            var innerModules = new Boundary(parent);
            int? deferredEntryNodeIndex = null;

            if (reader.TokenType != JsonTokenType.StartObject)
                return Helper.FailWith(out error, "Unexpected token when reading FunctionTemplate!");

            // Partial template reference: created as soon as we read the name so that
            // FunctionParameter.Load() can reference it.
            FunctionTemplate? partialTemplate = null;
            // Deferred list of local variable indices; resolved after nodes are loaded.
            List<int>? deferredLocalVarIds = null;

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName) continue;

                if (reader.ValueTextEquals(NameProperty))
                {
                    reader.Read();
                    name = reader.GetString();
                    if (partialTemplate is null && name is not null)
                        partialTemplate = new FunctionTemplate(name, parent, innerModules);
                }
                else if (reader.ValueTextEquals(LocationProperty))
                {
                    reader.Read(); // StartObject
                    float lx = 40, ly = 40, lw = 200, lh = 120;
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        if (reader.TokenType != JsonTokenType.PropertyName) continue;
                        if (reader.ValueTextEquals(LocationXProperty))      { reader.Read(); lx = reader.GetSingle(); }
                        else if (reader.ValueTextEquals(LocationYProperty)) { reader.Read(); ly = reader.GetSingle(); }
                        else if (reader.ValueTextEquals(LocationWProperty)) { reader.Read(); lw = reader.GetSingle(); }
                        else if (reader.ValueTextEquals(LocationHProperty)) { reader.Read(); lh = reader.GetSingle(); }
                        else reader.Skip();
                    }
                    location = new Rectangle(lx, ly, lw, lh);
                }
                else if (reader.ValueTextEquals(FunctionParametersProperty))
                {
                    // FunctionParameters must be loaded before InternalModules so that
                    // their node-dict indices are in the dictionary when internal links are loaded.
                    if (partialTemplate is null)
                    {
                        error = "FunctionParameters section appeared before the template Name was read.";
                        return false;
                    }
                    reader.Read(); // StartArray
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (!FunctionParameter.Load(typeLookup, node, ref reader, partialTemplate,
                                out var fp, ref error))
                            return false;
                        partialTemplate._functionParameters.Add(fp!);
                    }
                }
                else if (reader.ValueTextEquals(nameof(InternalModules)))
                {
                    reader.Read();
                    if (!innerModules.Load(modules, typeLookup, node, scriptedParameters, deferredGhostNodes, ref reader, ref error))
                        return false;
                }
                else if (reader.ValueTextEquals(EntryNodeProperty))
                {
                    reader.Read();
                    if (reader.TokenType == JsonTokenType.Number)
                        deferredEntryNodeIndex = reader.GetInt32();
                }
                else if (reader.ValueTextEquals(LocalVariablesProperty))
                {
                    reader.Read(); // StartArray
                    deferredLocalVarIds = new List<int>();
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (reader.TokenType == JsonTokenType.Number)
                            deferredLocalVarIds.Add(reader.GetInt32());
                    }
                }
                else
                {
                    reader.Skip();
                }
            }

            if (name is null)
                return Helper.FailWith(out error, "Function template did not include a name!");

            template = partialTemplate ?? new FunctionTemplate(name, parent, innerModules);
            template.SetLocation(location);

            // Resolve the entry node index.
            if (deferredEntryNodeIndex.HasValue
                && node.TryGetValue(deferredEntryNodeIndex.Value, out var entryNodeCandidate))
            {
                template._entryNode = entryNodeCandidate;
            }

            // Resolve local variable indices.
            if (deferredLocalVarIds is not null)
            {
                foreach (var idx in deferredLocalVarIds)
                {
                    if (node.TryGetValue(idx, out var lvNode))
                        template._localVariables.Add(lvNode);
                }
            }

            return true;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
