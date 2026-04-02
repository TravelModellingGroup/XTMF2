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
using System.Text;
using System.Text.Json;
using XTMF2.Editing;
using XTMF2.Repository;
using XTMF2.RuntimeModules;

namespace XTMF2.ModelSystemConstruct
{
    /// <summary>
    /// This class provides the logic for creating a function template.
    /// Function templates are then used in a model system by instantiating all of the
    /// needed references.
    /// </summary>
    public sealed class FunctionTemplate : INotifyPropertyChanged
    {
        // ── JSON property names ───────────────────────────────────────────
        private const string NameProperty           = "Name";
        private const string LocationProperty       = "Location";
        private const string ExposedNodesProperty   = "ExposedNodes";
        private const string EntryNodeProperty      = "EntryNode";
        private const string LocationXProperty      = "X";
        private const string LocationYProperty      = "Y";
        private const string LocationWProperty      = "Width";
        private const string LocationHProperty      = "Height";

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

        // ── Exposed nodes ─────────────────────────────────────────────────
        /// <summary>
        /// Nodes within <see cref="InternalModules"/> that are exposed as external hooks
        /// when the template is viewed from the parent boundary.
        /// </summary>
        private readonly ObservableCollection<Node> _exposedNodes = new();

        /// <summary>
        /// Read-only view of the nodes exposed as hooks on this function template's canvas box.
        /// </summary>
        public ReadOnlyObservableCollection<Node> ExposedNodes { get; }

        /// <summary>
        /// Toggles the exposure of <paramref name="node"/>.
        /// Adding it when not present; removing it when already exposed.
        /// Node must belong to <see cref="InternalModules"/>.
        /// </summary>
        internal bool ToggleExposedNode(Node node, [NotNullWhen(false)] out CommandError? error)
        {
            if (!InternalModules.Modules.Contains(node))
            {
                error = new CommandError($"Node '{node.Name}' does not belong to the InternalModules of template '{Name}'.");
                return false;
            }
            var nodeType = node.Type;
            bool isBasicParameter = nodeType is { IsGenericType: true }
                && nodeType.GetGenericTypeDefinition() == typeof(BasicParameter<>);
            if (!isBasicParameter)
            {
                error = new CommandError($"Only BasicParameter nodes can be exposed as hooks. '{node.Name}' is not a BasicParameter.");
                return false;
            }
            error = null;
            if (_exposedNodes.Contains(node))
                _exposedNodes.Remove(node);
            else
                _exposedNodes.Add(node);
            return true;
        }

        /// <summary>
        /// Forcibly removes <paramref name="node"/> from the exposed nodes list (used for undo).
        /// </summary>
        internal void RemoveExposedNode(Node node) => _exposedNodes.Remove(node);

        /// <summary>
        /// Forcibly adds <paramref name="node"/> to the exposed nodes list (used for undo).
        /// </summary>
        internal void AddExposedNode(Node node)
        {
            if (!_exposedNodes.Contains(node))
                _exposedNodes.Add(node);
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
            ExposedNodes = new ReadOnlyObservableCollection<Node>(_exposedNodes);
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

            // Internal modules (must come before ExposedNodes so that indices are defined)
            writer.WritePropertyName(nameof(InternalModules));
            InternalModules.Save(ref index, nodeDictionary, typeDictionary, writer);

            // Exposed nodes – stored as integer indices
            writer.WritePropertyName(ExposedNodesProperty);
            writer.WriteStartArray();
            foreach (var en in _exposedNodes)
            {
                if (nodeDictionary.TryGetValue(en, out int idx))
                    writer.WriteNumberValue(idx);
            }
            writer.WriteEndArray();

            // Entry node – stored as an integer index (null means not set)
            if (_entryNode != null && nodeDictionary.TryGetValue(_entryNode, out int entryIdx))
                writer.WriteNumber(EntryNodeProperty, entryIdx);

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

        internal static bool Load(ModuleRepository modules, Dictionary<int, Type> typeLookup, Dictionary<int, Node> node, List<(Node toAssignTo, string parameterExpression)> scriptedParameters,
            List<(Boundary ContainedIn, int RefIndex, int SelfIndex, Rectangle Location)> deferredGhostNodes,
            ref Utf8JsonReader reader, Boundary parent, [NotNullWhen(true)] out FunctionTemplate? template, [NotNullWhen(false)] ref string? error)
        {
            template = null;
            string? name = null;
            Rectangle location = new Rectangle(40, 40, 200, 120);
            var innerModules = new Boundary(parent);
            // Exposed node indices – resolved after InternalModules is loaded.
            var deferredExposedIndices = new List<int>();
            // Entry node index – resolved after InternalModules is loaded.
            int? deferredEntryNodeIndex = null;

            if(reader.TokenType != JsonTokenType.StartObject)
            {
                return Helper.FailWith(out error, "Unexpected token when reading FunctionTemplate!");
            }
            while(reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if(reader.TokenType != JsonTokenType.PropertyName)
                {
                    continue;
                }
                if(reader.ValueTextEquals(NameProperty))
                {
                    reader.Read();
                    name = reader.GetString();
                }
                else if(reader.ValueTextEquals(LocationProperty))
                {
                    reader.Read(); // StartObject
                    float lx = 40, ly = 40, lw = 200, lh = 120;
                    while(reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        if(reader.TokenType != JsonTokenType.PropertyName) continue;
                        if(reader.ValueTextEquals(LocationXProperty))      { reader.Read(); lx = reader.GetSingle(); }
                        else if(reader.ValueTextEquals(LocationYProperty)) { reader.Read(); ly = reader.GetSingle(); }
                        else if(reader.ValueTextEquals(LocationWProperty)) { reader.Read(); lw = reader.GetSingle(); }
                        else if(reader.ValueTextEquals(LocationHProperty)) { reader.Read(); lh = reader.GetSingle(); }
                        else reader.Skip();
                    }
                    location = new Rectangle(lx, ly, lw, lh);
                }
                else if(reader.ValueTextEquals(nameof(InternalModules)))
                {
                    reader.Read();
                    if(!innerModules.Load(modules, typeLookup, node, scriptedParameters, deferredGhostNodes, ref reader, ref error))
                    {
                        return false;
                    }
                }
                else if(reader.ValueTextEquals(ExposedNodesProperty))
                {
                    reader.Read(); // StartArray
                    while(reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if(reader.TokenType == JsonTokenType.Number)
                            deferredExposedIndices.Add(reader.GetInt32());
                    }
                }
                else if(reader.ValueTextEquals(EntryNodeProperty))
                {
                    reader.Read();
                    if(reader.TokenType == JsonTokenType.Number)
                        deferredEntryNodeIndex = reader.GetInt32();
                }
                else
                {
                    reader.Skip();
                }
            }
            if(name is null)
            {
                return Helper.FailWith(out error, "Function template did not include a name!");
            }
            template = new FunctionTemplate(name, parent, innerModules);
            // Replace the empty InternalModules created by the constructor with the one
            // loaded from disk (which already contains the correct nodes, starts, links, etc.).
            template.SetLocation(location);

            // Resolve exposed node indices now that InternalModules nodes are in the dictionary.
            foreach (int idx in deferredExposedIndices)
            {
                if (node.TryGetValue(idx, out var exposedNode))
                    template._exposedNodes.Add(exposedNode);
            }

            // Resolve the entry node index.
            if (deferredEntryNodeIndex.HasValue
                && node.TryGetValue(deferredEntryNodeIndex.Value, out var entryNodeCandidate))
            {
                template._entryNode = entryNodeCandidate;
            }

            return true;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
