/*
    Copyright 2017 University of Toronto

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

namespace XTMF2.ModelSystemConstruct
{
    /// <summary>
    /// Provides a grouping of modules, link origins, and sub boundaries
    /// </summary>
    public sealed class Boundary : INotifyPropertyChanged
    {
        /// <summary>
        /// The name of the boundary
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// A description of the boundary's purpose
        /// </summary>
        public string Description { get; private set; }

        private const string NameProperty = "Name";
        private const string DescriptionProperty = "Description";
        private const string StartsProperty = "Starts";
        private const string NodesProperty = "Nodes";
        private const string BoundariesProperty = "Boundaries";
        private const string LinksProperty = "Links";
        private const string CommentBlocksProperty = "CommentBlocks";
        private const string FunctionTemplateProperty = "FunctionTemplates";

        /// <summary>
        /// This lock must be obtained before changing any local settings.
        /// </summary>
        private readonly object _writeLock = new object();
        private readonly ObservableCollection<Node> _modules = new ObservableCollection<Node>();
        private readonly ObservableCollection<Start> _starts = new ObservableCollection<Start>();
        private readonly ObservableCollection<Boundary> _boundaries = new ObservableCollection<Boundary>();
        private readonly ObservableCollection<Link> _links = new ObservableCollection<Link>();
        private readonly ObservableCollection<CommentBlock> _commentBlocks = new ObservableCollection<CommentBlock>();
        private readonly ObservableCollection<FunctionTemplate> _functionTemplates = new ObservableCollection<FunctionTemplate>();

        /// <summary>
        /// Get readonly access to the links contained in this boundary.
        /// </summary>
        public ReadOnlyObservableCollection<Link> Links => new ReadOnlyObservableCollection<Link>(_links);

        /// <summary>
        /// Create a new boundary, optionally with a parent
        /// </summary>
        /// <param name="name">The unique name of the boundary</param>
        /// <param name="parent">The parent of the boundary</param>
        internal Boundary(string name, Boundary? parent = null)
        {
            Name = name;
            Parent = parent;
            Description = string.Empty;
        }

        /// <summary>
        /// Called when loading a boundary
        /// </summary>
        internal Boundary(Boundary parent)
        {
            Parent = parent;
            Name = string.Empty;
            Description = string.Empty;
        }

        /// <summary>
        /// Check to see if a given boundary exists is, or is in this boundary.
        /// </summary>
        /// <param name="boundary">The boundary to check for.</param>
        /// <returns>True if the boundary is this boundary, or is contained within.</returns>
        internal bool Contains(Boundary boundary)
        {
            if (boundary == null)
            {
                throw new ArgumentNullException(nameof(boundary));
            }
            return _boundaries.Any(b => b == boundary || b.Contains(boundary));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Provides a readonly view of the locally contained modules.
        /// </summary>
        public ReadOnlyObservableCollection<Node> Modules
        {
            get
            {
                lock (_writeLock)
                {
                    return new ReadOnlyObservableCollection<Node>(_modules);
                }
            }
        }

        /// <summary>
        /// Provides a readonly view of the locally contained Starts.
        /// </summary>
        public ReadOnlyObservableCollection<Start> Starts
        {
            get
            {
                lock (_writeLock)
                {
                    return new ReadOnlyObservableCollection<Start>(_starts);
                }
            }
        }

        public ReadOnlyObservableCollection<FunctionTemplate> FunctionTemplates
        {
            get
            {
                lock(_writeLock)
                {
                    return new ReadOnlyObservableCollection<FunctionTemplate>(_functionTemplates);
                }
            }
        }

        internal bool Validate(ref string? moduleName, ref string? error)
        {
            foreach (var module in _modules)
            {
                if (!module.Validate(ref moduleName, ref error))
                {
                    return false;
                }
            }
            foreach (var children in _boundaries)
            {
                if (!children.Validate(ref moduleName, ref error))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Creates a dictionary of type to index number for types contained in the model system.
        /// </summary>
        /// <returns>A dictionary mapping type to index.</returns>
        internal Dictionary<Type, int> GetUsedTypes()
        {
            static List<Type> GetUsedTypes(Boundary current, List<Type> included)
            {
                foreach (var module in current._modules)
                {
                    var t = module.Type;
                    if (t != null)
                    {
                        if (!included.Contains(t))
                        {
                            included.Add(t);
                        }
                    }
                }
                foreach (var child in current._boundaries)
                {
                    GetUsedTypes(child, included);
                }
                return included;
            }
            return GetUsedTypes(this, new List<Type>()).Select((type, index) => (type, index))
                .ToDictionary(e => e.type, e => e.index);
        }

        /// <summary>
        /// Constructs the model system's modules
        /// </summary>
        /// <param name="runtime">The XTMF runtime to run from.</param>
        /// <param name="error">An error message if the construction fails.</param>
        /// <returns>True if successful, false otherwise with an error message.</returns>
        internal bool ConstructModules(XTMFRuntime runtime, ref string? error)
        {
            lock (_writeLock)
            {
                foreach (var start in _starts)
                {
                    if (!start.ConstructModule(runtime, ref error))
                    {
                        return false;
                    }
                }
                foreach (var module in _modules)
                {
                    if (!module.ConstructModule(runtime, ref error))
                    {
                        return false;
                    }
                }
                // now construct all of the children
                foreach (var child in Boundaries)
                {
                    if (!child.ConstructModules(runtime, ref error))
                    {
                        return false;
                    }
                }
                error = null;
                return true;
            }
        }

        internal bool HasChildWithName(string name)
        {
            return _boundaries.Any(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Add a new child boundary
        /// </summary>
        /// <param name="name">The unique name for the boundary.</param>
        /// <param name="boundary">The resulting boundary.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if successful, false otherwise with an error message.</returns>
        internal bool AddBoundary(string name, out Boundary? boundary, out CommandError? error)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                boundary = null;
                error = new CommandError("The name of a boundary must be set.");
                return false;
            }

            if (HasChildWithName(name))
            {
                boundary = null;
                error = new CommandError("The name already exists in this boundary!");
                return false;
            }
            _boundaries.Add(boundary = new Boundary(name, this));
            error = null;
            return true;
        }

        /// <summary>
        /// Create a new documentation block at the given location
        /// </summary>
        /// <param name="position">The location in the boundary to add the documentation block</param>
        /// <param name="block">The resulting block</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if successful, false otherwise with an error message.</returns>
        internal bool AddCommentBlock(string documentation, Rectangle position, out CommentBlock? block, out CommandError? error)
        {
            block = null;
            var _block = new CommentBlock(documentation, position);
            if (!AddCommentBlock(_block, out error))
            {
                return false;
            }
            block = _block;
            return true;
        }

        internal bool AddCommentBlock(CommentBlock block, out CommandError? error)
        {
            if (block is null)
            {
                throw new ArgumentNullException(nameof(block));
            }
            error = null;
            lock (_writeLock)
            {
                if (_commentBlocks.Contains(block))
                {
                    error = new CommandError("The documentation block already belongs to the boundary!");
                    return false;
                }
                _commentBlocks.Add(block);
                return true;
            }
        }

        internal bool RemoveCommentBlock(CommentBlock block, out CommandError? error)
        {
            if (block is null)
            {
                throw new ArgumentNullException(nameof(block));
            }
            lock (_writeLock)
            {
                if (!_commentBlocks.Remove(block))
                {
                    error = new CommandError("Unable to remove the documentation block from the boundary.");
                    return false;
                }
            }
            error = null;
            return true;
        }

        /// <summary>
        /// Collect all links going to a given boundary.
        /// </summary>
        /// <param name="boundary">The boundary to get links to.</param>
        /// <returns>A list of all links going to the given boundary.</returns>
        internal List<Link> GetLinksGoingToBoundary(Boundary boundary)
        {
            var ret = new List<Link>();
            var stack = new Stack<Boundary>();
            stack.Push(this);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                foreach (var child in current._boundaries)
                {
                    stack.Push(child);
                }
                // don't bother analyzing the boundary being removed
                if (current != boundary)
                {
                    foreach (var link in current._links)
                    {
                        if (link is SingleLink sl)
                        {
                            if (sl.Destination!.ContainedWithin == boundary)
                            {
                                ret.Add(link);
                            }
                        }
                        else if (link is MultiLink ml)
                        {
                            foreach (var dest in ml.Destinations)
                            {
                                if (dest.ContainedWithin == boundary)
                                {
                                    ret.Add(link);
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            return ret;
        }

        /// <summary>
        /// Add a boundary
        /// </summary>
        /// <param name="boundary"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        internal bool AddBoundary(Boundary boundary, out CommandError? error)
        {
            if (_boundaries.Contains(boundary))
            {
                error = new CommandError("The name already exists in this boundary!");
                return false;
            }
            _boundaries.Add(boundary);
            error = null;
            return true;
        }

        internal bool RemoveBoundary(Boundary boundary, out CommandError? error)
        {
            if (!_boundaries.Remove(boundary))
            {
                error = new CommandError("Unable to find boundary to remove it!");
                return false;
            }
            error = null;
            return true;
        }

        internal bool AddStart(Start start, out CommandError? error)
        {
            if (_starts.Contains(start))
            {
                error = new CommandError("The start already exists in the boundary!");
                return false;
            }
            _starts.Add(start);
            error = null;
            return true;
        }

        internal bool AddFunctionTemplate(string name, out FunctionTemplate? template, out CommandError? error)
        {
            template = null;
            error = null;
            lock (_writeLock)
            {
                if(_functionTemplates.Any(t => t.Name == name))
                {
                    error = new CommandError($"The function template name '{name}' has already been used.");
                    return false;
                }
                _functionTemplates.Add(template = new FunctionTemplate(name, this));
                return true;
            }
        }

        internal bool AddFunctionTemplate(FunctionTemplate template, out CommandError? error)
        {
            lock (_writeLock)
            {
                if (_functionTemplates.Contains(template))
                {
                    error = new CommandError("The function template already exists in the boundary!");
                    return false;
                }
                _functionTemplates.Add(template);
                error = null;
                return true;
            }
        }

        internal bool RemoveFunctionTemplate(FunctionTemplate template, out CommandError? error)
        {
            lock (_writeLock)
            {
                if (!_functionTemplates.Contains(template))
                {
                    error = new CommandError("The function template does not exist within the boundary!");
                    return false;
                }
                _functionTemplates.Remove(template);
                error = null;
                return true;
            }
        }

        internal bool ConstructLinks(ref string? error)
        {
            lock (_writeLock)
            {
                foreach (var link in _links)
                {
                    if (!link.Construct(ref error))
                    {
                        return false;
                    }
                }
                // now construct all of the children
                foreach (var child in Boundaries)
                {
                    if (!child.ConstructLinks(ref error))
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        public ReadOnlyObservableCollection<Boundary> Boundaries
        {
            get
            {
                lock (_writeLock)
                {
                    return new ReadOnlyObservableCollection<Boundary>(_boundaries);
                }
            }
        }

        private readonly Boundary? Parent;

        public string FullPath
        {
            get
            {
                Stack<Boundary> stack = new Stack<Boundary>();
                Boundary? current = this;
                while (current != null)
                {
                    stack.Push(current);
                    current = current.Parent;
                }
                return string.Join(".", from b in stack
                                        select b.Name);
            }
        }

        public ReadOnlyObservableCollection<CommentBlock> CommentBlocks
        {
            get
            {
                lock (_writeLock)
                {
                    return new ReadOnlyObservableCollection<CommentBlock>(_commentBlocks);
                }
            }
        }

        internal bool AddNode(ModuleRepository modules, string name, Type type, Rectangle location, out Node? node, out CommandError? error)
        {
            node = Node.Create(modules, name, type, this, location);
            if (node is null)
            {
                return Helper.FailWith(out error, $"Unable to create a node with the name {name} of type {type.FullName}!");
            }
            _modules.Add(node);
            error = null;
            return true;
        }

        internal bool AddNode(Node node, out CommandError? e)
        {
            if (_modules.Contains(node))
            {
                e = new CommandError("The node already exists in the boundary!");
                return false;
            }
            _modules.Add(node);
            e = null;
            return true;
        }

        internal void Save(ref int index, Dictionary<Node, int> nodeDictionary, Dictionary<Type, int> typeDictionary, Utf8JsonWriter writer)
        {
            lock (_writeLock)
            {
                writer.WriteStartObject();
                writer.WriteString(NameProperty, Name);
                writer.WriteString(DescriptionProperty, Description);
                writer.WritePropertyName(StartsProperty);
                writer.WriteStartArray();
                foreach (var start in _starts)
                {
                    start.Save(ref index, nodeDictionary, typeDictionary, writer);
                }
                writer.WriteEndArray();
                writer.WritePropertyName(NodesProperty);
                writer.WriteStartArray();
                foreach (var module in _modules)
                {
                    module.Save(ref index, nodeDictionary, typeDictionary, writer);
                }
                writer.WriteEndArray();
                writer.WritePropertyName(BoundariesProperty);
                writer.WriteStartArray();
                foreach (var child in _boundaries)
                {
                    child.Save(ref index, nodeDictionary, typeDictionary, writer);
                }
                writer.WriteEndArray();
                writer.WritePropertyName(LinksProperty);
                writer.WriteStartArray();
                foreach (var link in _links)
                {
                    link.Save(nodeDictionary, writer);
                }
                writer.WriteEndArray();
                writer.WritePropertyName(CommentBlocksProperty);
                writer.WriteStartArray();
                foreach (var docBlock in _commentBlocks)
                {
                    docBlock.Save(writer);
                }
                writer.WriteEndArray();
                writer.WritePropertyName(FunctionTemplateProperty);
                writer.WriteStartArray();
                foreach(var functionTemplate in FunctionTemplates)
                {
                    functionTemplate.Save(ref index, nodeDictionary, typeDictionary, writer);
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
        }

        internal bool RemoveNode(Node node, out CommandError? error)
        {
            if (!_modules.Remove(node))
            {
                error = new CommandError("Unable to find node in the boundary!");
                return false;
            }
            error = null;
            return true;
        }

        /// <summary>
        /// This invocation should only occur with a link that was generated
        /// by this boundary previously!
        /// </summary>
        /// <param name="link">The returning link</param>
        /// <param name="e">An error message if one occurs</param>
        /// <returns>True if it was added again, false otherwise with message.</returns>
        internal bool AddLink(Link link, out CommandError? e)
        {
            if (link == null)
            {
                throw new ArgumentNullException(nameof(link));
            }
            if (link.Origin!.ContainedWithin != this)
            {
                e = new CommandError("This link is was not contained within this boundary!");
                return false;
            }
            if (_links.Contains(link))
            {
                e = new CommandError("This link is already contained within this boundary!");
                return false;
            }
            _links.Add(link);
            e = null;
            return true;
        }

        

        internal bool Load(ModuleRepository modules, Dictionary<int, Type> typeLookup, Dictionary<int, Node> node,
            ref Utf8JsonReader reader, [NotNullWhen(false)] ref string? error)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                return Helper.FailWith(out error, "Unexpected token when reading boundary!");
            }
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName && reader.TokenType != JsonTokenType.Comment)
                {
                    return Helper.FailWith(out error, "Unexpected token when reading boundary!");
                }
                if (reader.ValueTextEquals(NameProperty))
                {
                    reader.Read();
                    var temp = reader.GetString();
                    if (temp is null)
                    {
                        error = "Unable to read the Name property!";
                        return false;
                    }
                    Name = temp;
                }
                else if (reader.ValueTextEquals(DescriptionProperty))
                {
                    reader.Read();
                    var temp = reader.GetString();
                    if (temp is null)
                    {
                        error = "Unable to read the Description property!";
                        return false;
                    }
                    Description = temp;
                }
                else if (reader.ValueTextEquals(StartsProperty))
                {
                    if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
                    {
                        return Helper.FailWith(out error, "Unexpected token when starting to read Starts for a boundary.");
                    }
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (!Start.Load(modules, node, this, ref reader, out var start, ref error))
                        {
                            return false;
                        }
                        _starts.Add(start!);
                    }
                }
                else if (reader.ValueTextEquals(NodesProperty))
                {
                    if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
                    {
                        return Helper.FailWith(out error, "Unexpected token when starting to read Nodes for a boundary.");
                    }
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (reader.TokenType != JsonTokenType.Comment)
                        {
                            if (!Node.Load(modules, typeLookup, node, this, ref reader, out var mss, ref error))
                            {
                                return false;
                            }
                            _modules.Add(mss!);
                        }
                    }
                }
                else if (reader.ValueTextEquals(BoundariesProperty))
                {
                    if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
                    {
                        return Helper.FailWith(out error, "Unexpected token when starting to read Modules for a boundary.");
                    }
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (reader.TokenType != JsonTokenType.Comment)
                        {
                            var boundary = new Boundary(this);
                            if (!boundary.Load(modules, typeLookup, node, ref reader, ref error))
                            {
                                return false;
                            }
                        }
                    }
                }
                else if (reader.ValueTextEquals(LinksProperty))
                {
                    if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
                    {
                        return Helper.FailWith(out error, "Unexpected token when starting to read Links for a boundary.");
                    }
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (reader.TokenType != JsonTokenType.Comment)
                        {
                            if (!Link.Create(modules, node, ref reader, out var link, ref error))
                            {
                                return false;
                            }
                            _links.Add(link!);
                        }
                    }
                }
                else if (reader.ValueTextEquals(CommentBlocksProperty))
                {
                    if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
                    {
                        return Helper.FailWith(out error, "Unexpected token when starting to read Documentation Blocks for a boundary.");
                    }
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (reader.TokenType != JsonTokenType.Comment)
                        {
                            if (!CommentBlock.Load(ref reader, out var block, ref error))
                            {
                                return false;
                            }
                            _commentBlocks.Add(block!);
                        }
                    }
                }
                else if (reader.ValueTextEquals(FunctionTemplateProperty))
                {
                    if(!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
                    {
                        return Helper.FailWith(out error, "Unexpected token when starting to read Function Templates for a boundary.");
                    }
                    while(reader.Read() &&  reader.TokenType != JsonTokenType.EndArray)
                    {
                        if(reader.TokenType != JsonTokenType.Comment)
                        {
                            if(!FunctionTemplate.Load(modules, typeLookup, node, ref reader, this, out var template, ref error))
                            {
                                return false;
                            }
                            _functionTemplates.Add(template!);
                        }
                    }
                }
                else
                {
                    return Helper.FailWith(out error, $"Unexpected value when reading boundary {reader.GetString()}");
                }
            }
            return true;
        }

        internal bool AddLink(Node origin, NodeHook originHook, Node destination, out Link? link, out CommandError? error)
        {
            switch (originHook.Cardinality)
            {
                case HookCardinality.Single:
                case HookCardinality.SingleOptional:
                    link = new SingleLink(origin, originHook, destination, false);
                    _links.Add(link);
                    break;
                default:
                    {
                        var previous = _links.FirstOrDefault(l => l.Origin == origin && l.OriginHook == originHook);
                        if (previous != null)
                        {
                            link = previous;
                            if (link is MultiLink ml)
                            {
                                if (!ml.AddDestination(destination, out error))
                                {
                                    return false;
                                }
                            }
                        }
                        else
                        {
                            link = new MultiLink(origin, originHook, new List<Node>() { destination }, false);
                            _links.Add(link);
                        }
                    }
                    break;
            }
            error = null;
            return true;
        }

        internal bool AddLink(Node origin, NodeHook originHook, Node destination, Link link, out CommandError? error)
        {
            switch (originHook.Cardinality)
            {
                case HookCardinality.Single:
                case HookCardinality.SingleOptional:
                    _links.Add(link);
                    break;
                default:
                    {
                        var previous = _links.FirstOrDefault(l => l.Origin == origin && l.OriginHook == originHook);
                        if (previous != null)
                        {
                            link = previous;
                        }
                        if (!((MultiLink)link).AddDestination(destination, out error))
                        {
                            return false;
                        }
                        // if we are successful and it didn't already exist add it to our list
                        if (previous == null)
                        {
                            _links.Add(link);
                        }
                    }
                    break;
            }
            error = null;
            return true;
        }

        internal bool RemoveLink(Link link, out CommandError? error)
        {
            if (!_links.Remove(link))
            {
                error = new CommandError("Unable to find the link to remove from the boundary!");
                return false;
            }
            error = null;
            return true;
        }

        internal bool SetName(string name, out CommandError? error)
        {
            if (String.IsNullOrWhiteSpace(name))
            {
                error = new CommandError("A name cannot be whitespace.");
                return false;
            }
            var withName = Parent?.HasChildWithName(name);
            if (withName == true)
            {
                error = new CommandError($"There already exists another boundary with the name {name} in the parent boundary!");
                return false;
            }
            Name = name;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            error = null;
            return true;
        }

        internal bool SetDescription(string description, out CommandError? error)
        {
            error = null;
            Description = description;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description)));
            return true;
        }

        internal bool AddStart(ModelSystemSession session, string startName, Rectangle location, out Start? start, out CommandError? error)
        {
            start = null;
            // ensure the name is unique between starting points
            foreach (var ms in _starts)
            {
                if (ms.Name.Equals(startName, StringComparison.OrdinalIgnoreCase))
                {
                    error = new CommandError("There already exists a start with the same name!");
                    return false;
                }
            }
            start = new Start(session.GetModuleRepository(), startName, this, string.Empty, location);
            _starts.Add(start);
            error = null;
            return true;
        }

        /// <summary>
        /// Add the given start to the boundary
        /// </summary>
        /// <param name="session"></param>
        /// <param name="startName"></param>
        /// <param name="start"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        internal bool AddStart(string startName, Start start, out CommandError? error)
        {
            // ensure the name is unique between starting points
            foreach (var ms in _starts)
            {
                if (ms.Name.Equals(startName, StringComparison.OrdinalIgnoreCase))
                {
                    error = new CommandError("There already exists a start with the same name!");
                    return false;
                }
            }
            _starts.Add(start);
            error = null;
            return true;
        }

        internal bool RemoveStart(Start start, out CommandError? error)
        {
            if (!_starts.Remove(start))
            {
                error = new CommandError("Unable to find a the given start!");
                return false;
            }
            error = null;
            return true;
        }
    }
}
