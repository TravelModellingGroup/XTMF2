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
using System.Linq;
using System.Text;
using System.Text.Json;
using XTMF2.Editing;
using XTMF2.ModelSystemConstruct;

namespace XTMF2
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

        /// <summary>
        /// This lock must be obtained before changing any local settings.
        /// </summary>
        private readonly object _WriteLock = new object();
        private readonly ObservableCollection<Node> _Modules = new ObservableCollection<Node>();
        private readonly ObservableCollection<Start> _Starts = new ObservableCollection<Start>();
        private readonly ObservableCollection<Boundary> _Boundaries = new ObservableCollection<Boundary>();
        private readonly ObservableCollection<Link> _Links = new ObservableCollection<Link>();
        private readonly ObservableCollection<CommentBlock> _CommentBlocks = new ObservableCollection<CommentBlock>();

        /// <summary>
        /// Get readonly access to the links contained in this boundary.
        /// </summary>
        public ReadOnlyObservableCollection<Link> Links => new ReadOnlyObservableCollection<Link>(_Links);

        /// <summary>
        /// Create a new boundary, optionally with a parent
        /// </summary>
        /// <param name="name">The unique name of the boundary</param>
        /// <param name="parent">The parent of the boundary</param>
        public Boundary(string name, Boundary parent = null)
        {
            Name = name;
            Parent = parent;
        }

        /// <summary>
        /// Called when loading a boundary
        /// </summary>
        internal Boundary(Boundary parent)
        {
            Parent = parent;
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
            return _Boundaries.Any(b => b == boundary || b.Contains(boundary));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Provides a readonly view of the locally contained modules.
        /// </summary>
        public ReadOnlyObservableCollection<Node> Modules
        {
            get
            {
                lock (_WriteLock)
                {
                    return new ReadOnlyObservableCollection<Node>(_Modules);
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
                lock (_WriteLock)
                {
                    return new ReadOnlyObservableCollection<Start>(_Starts);
                }
            }
        }

        /// <summary>
        /// Creates a dictionary of type to index number for types contained in the model system.
        /// </summary>
        /// <returns>A dictionary mapping type to index.</returns>
        internal Dictionary<Type, int> GetUsedTypes()
        {
            List<Type> GetUsedTypes(Boundary current, List<Type> included)
            {
                foreach (var module in current._Modules)
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
                foreach (var child in current._Boundaries)
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
        internal bool ConstructModules(XTMFRuntime runtime, ref string error)
        {
            lock (_WriteLock)
            {
                foreach (var start in _Starts)
                {
                    if (!start.ConstructModule(runtime, ref error))
                    {
                        return false;
                    }
                }
                foreach (var module in _Modules)
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
                return true;
            }
        }

        internal bool HasChildWithName(string name)
        {
            return _Boundaries.Any(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Add a new child boundary
        /// </summary>
        /// <param name="name">The unique name for the boundary.</param>
        /// <param name="boundary">The resulting boundary.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if successful, false otherwise with an error message.</returns>
        internal bool AddBoundary(string name, out Boundary boundary, ref string error)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                boundary = null;
                error = "The name of a boundary must be set.";
                return false;
            }

            if (HasChildWithName(name))
            {
                boundary = null;
                error = "The name already exists in this boundary!";
                return false;
            }
            _Boundaries.Add((boundary = new Boundary(name, this)));
            return true;
        }

        /// <summary>
        /// Create a new documentation block at the given location
        /// </summary>
        /// <param name="position">The location in the boundary to add the documentation block</param>
        /// <param name="block">The resulting block</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if successful, false otherwise with an error message.</returns>
        internal bool AddCommentBlock(string documentation, Point position, out CommentBlock block, ref string error)
        {
            block = null;
            var _block = new CommentBlock(documentation, position);
            if (!AddCommentBlock(_block, ref error))
            {
                return false;
            }
            block = _block;
            return true;
        }

        internal bool AddCommentBlock(CommentBlock block, ref string error)
        {
            if (block is null)
            {
                throw new ArgumentNullException(nameof(block));
            }
            lock (_WriteLock)
            {
                if (_CommentBlocks.Contains(block))
                {
                    error = "The documentation block already belongs to the boundary!";
                    return false;
                }
                _CommentBlocks.Add(block);
                return true;
            }
        }

        internal bool RemoveCommentBlock(CommentBlock block, ref string error)
        {
            if (block is null)
            {
                throw new ArgumentNullException(nameof(block));
            }
            lock (_WriteLock)
            {
                if (!_CommentBlocks.Remove(block))
                {
                    error = "Unable to remove the documentation block from the boundary.";
                    return false;
                }
            }
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
                foreach (var child in current._Boundaries)
                {
                    stack.Push(child);
                }
                // don't bother analyzing the boundary being removed
                if (current != boundary)
                {
                    foreach (var link in current._Links)
                    {
                        if (link is SingleLink sl)
                        {
                            if (sl.Destination.ContainedWithin == boundary)
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
        internal bool AddBoundary(Boundary boundary, ref string error)
        {
            if (_Boundaries.Contains(boundary))
            {
                error = "The name already exists in this boundary!";
                return false;
            }
            _Boundaries.Add(boundary);
            return true;
        }

        internal bool RemoveBoundary(Boundary boundary, ref string error)
        {
            if (!_Boundaries.Remove(boundary))
            {
                error = "Unable to find boundary to remove it!";
                return false;
            }
            return true;
        }

        internal bool AddStart(Start start, ref string e)
        {
            if (_Starts.Contains(start))
            {
                e = "The start already exists in the boundary!";
                return false;
            }
            _Starts.Add(start);
            return true;
        }

        internal bool ConstructLinks(ref string error)
        {
            lock (_WriteLock)
            {
                foreach (var link in _Links)
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
                lock (_WriteLock)
                {
                    return new ReadOnlyObservableCollection<Boundary>(_Boundaries);
                }
            }
        }

        private readonly Boundary Parent;

        public string FullPath
        {
            get
            {
                Stack<Boundary> stack = new Stack<Boundary>();
                var current = this;
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
                lock (_WriteLock)
                {
                    return new ReadOnlyObservableCollection<CommentBlock>(_CommentBlocks);
                }
            }
        }

        internal bool AddNode(Node node, ref string e)
        {
            if (_Modules.Contains(node))
            {
                e = "The node already exists in the boundary!";
                return false;
            }
            _Modules.Add(node);
            return true;
        }

        internal void Save(ref int index, Dictionary<Node, int> nodeDictionary, Dictionary<Type, int> typeDictionary, Utf8JsonWriter writer)
        {
            lock (_WriteLock)
            {
                writer.WriteStartObject();
                writer.WriteString(NameProperty, Name);
                writer.WriteString(DescriptionProperty, Description);
                writer.WritePropertyName(StartsProperty);
                writer.WriteStartArray();
                foreach (var start in _Starts)
                {
                    start.Save(ref index, nodeDictionary, typeDictionary, writer);
                }
                writer.WriteEndArray();
                writer.WritePropertyName(NodesProperty);
                writer.WriteStartArray();
                foreach (var module in _Modules)
                {
                    module.Save(ref index, nodeDictionary, typeDictionary, writer);
                }
                writer.WriteEndArray();
                writer.WritePropertyName(BoundariesProperty);
                writer.WriteStartArray();
                foreach (var child in _Boundaries)
                {
                    child.Save(ref index, nodeDictionary, typeDictionary, writer);
                }
                writer.WriteEndArray();
                writer.WritePropertyName(LinksProperty);
                writer.WriteStartArray();
                foreach (var link in _Links)
                {
                    link.Save(nodeDictionary, writer);
                }
                writer.WriteEndArray();
                writer.WritePropertyName(CommentBlocksProperty);
                writer.WriteStartArray();
                foreach (var docBlock in _CommentBlocks)
                {
                    docBlock.Save(writer);
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
        }

        internal bool RemoveNode(Node node, ref string error)
        {
            if (!_Modules.Remove(node))
            {
                error = "Unable to find node in the boundary!";
                return false;
            }
            return true;
        }

        /// <summary>
        /// This invocation should only occur with a link that was generated
        /// by this boundary previously!
        /// </summary>
        /// <param name="link">The returning link</param>
        /// <param name="e">An error message if one occurs</param>
        /// <returns>True if it was added again, false otherwise with message.</returns>
        internal bool AddLink(Link link, ref string e)
        {
            if (link == null)
            {
                throw new ArgumentNullException(nameof(link));
            }
            if (link.Origin.ContainedWithin != this)
            {
                e = "This link is was not contained within this boundary!";
                return false;
            }
            if (_Links.Contains(link))
            {
                e = "This link is already contained within this boundary!";
                return false;
            }
            _Links.Add(link);
            return true;
        }

        private static bool FailWith(ref string error, string message)
        {
            error = message;
            return false;
        }

        internal bool Load(ModelSystemSession session, Dictionary<int, Type> typeLookup, Dictionary<int, Node> node,
            ref Utf8JsonReader reader, ref string error)
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return FailWith(ref error, "Unexpected token when reading boundary!");
            }
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName && reader.TokenType != JsonTokenType.Comment)
                {
                    return FailWith(ref error, "Unexpected token when reading boundary!");
                }
                if(reader.ValueTextEquals(NameProperty))
                {
                    reader.Read();
                    Name = reader.GetString();
                }
                else if(reader.ValueTextEquals(DescriptionProperty))
                {
                    reader.Read();
                    Description = reader.GetString();
                }
                else if(reader.ValueTextEquals(StartsProperty))
                {
                    if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
                    {
                        return FailWith(ref error, "Unexpected token when starting to read Starts for a boundary.");
                    }
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (!Start.Load(session, node, this, ref reader, out Start start, ref error))
                        {
                            return false;
                        }
                        _Starts.Add(start);
                    }
                }
                else if(reader.ValueTextEquals(NodesProperty))
                {
                    if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
                    {
                        return FailWith(ref error, "Unexpected token when starting to read Nodes for a boundary.");
                    }
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (reader.TokenType != JsonTokenType.Comment)
                        {
                            if (!Node.Load(session, typeLookup, node, this, ref reader, out Node mss, ref error))
                            {
                                return false;
                            }
                            _Modules.Add(mss);
                        }
                    }
                }
                else if(reader.ValueTextEquals(BoundariesProperty))
                {
                    if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
                    {
                        return FailWith(ref error, "Unexpected token when starting to read Modules for a boundary.");
                    }
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (reader.TokenType != JsonTokenType.Comment)
                        {
                            var boundary = new Boundary(this);
                            if (!boundary.Load(session, typeLookup, node, ref reader, ref error))
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
                        return FailWith(ref error, "Unexpected token when starting to read Links for a boundary.");
                    }
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (reader.TokenType != JsonTokenType.Comment)
                        {
                            if (!Link.Create(session, node, ref reader, out var link, ref error))
                            {
                                return false;
                            }
                            _Links.Add(link);
                        }
                    }
                }
                else if (reader.ValueTextEquals(CommentBlocksProperty))
                {
                    if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
                    {
                        return FailWith(ref error, "Unexpected token when starting to read Documentation Blocks for a boundary.");
                    }
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (reader.TokenType != JsonTokenType.Comment)
                        {
                            if (!CommentBlock.Load(ref reader, out CommentBlock block, ref error))
                            {
                                return false;
                            }
                            _CommentBlocks.Add(block);
                        }
                    }
                }
                else
                {
                    return FailWith(ref error, $"Unexpected value when reading boundary {reader.GetString()}");
                }
            }
            return true;
        }

        internal bool AddLink(Node origin, NodeHook originHook, Node destination, out Link link, ref string error)
        {
            switch (originHook.Cardinality)
            {
                case HookCardinality.Single:
                case HookCardinality.SingleOptional:
                    link = new SingleLink()
                    {
                        Origin = origin,
                        OriginHook = originHook,
                        Destination = destination
                    };
                    _Links.Add(link);
                    break;
                default:
                    {
                        var previous = _Links.FirstOrDefault(l => l.Origin == origin && l.OriginHook == originHook);
                        if (previous != null)
                        {
                            link = previous;
                        }
                        else
                        {
                            link = new MultiLink()
                            {
                                Origin = origin,
                                OriginHook = originHook
                            };
                        }
                        if (!((MultiLink)link).AddDestination(destination, ref error))
                        {
                            link = null;
                            return false;
                        }
                        // if we are successful and it didn't already exist add it to our list
                        if (previous == null)
                        {
                            _Links.Add(link);
                        }
                    }
                    break;
            }
            return true;
        }

        internal bool AddLink(Node origin, NodeHook originHook, Node destination, Link link, ref string error)
        {
            switch (originHook.Cardinality)
            {
                case HookCardinality.Single:
                case HookCardinality.SingleOptional:
                    _Links.Add(link);
                    break;
                default:
                    {
                        var previous = _Links.FirstOrDefault(l => l.Origin == origin && l.OriginHook == originHook);
                        if (previous != null)
                        {
                            link = previous;
                        }
                        if (!((MultiLink)link).AddDestination(destination, ref error))
                        {
                            return false;
                        }
                        // if we are successful and it didn't already exist add it to our list
                        if (previous == null)
                        {
                            _Links.Add(link);
                        }
                    }
                    break;
            }
            return true;
        }

        internal bool RemoveLink(Link link, ref string e)
        {
            if (!_Links.Remove(link))
            {
                e = "Unable to find the link to remove from the boundary!";
                return false;
            }
            return true;
        }

        public bool SetName(string name, ref string error)
        {
            if (String.IsNullOrWhiteSpace(name))
            {
                error = "A name cannot be whitespace.";
                return false;
            }
            var withName = Parent?.HasChildWithName(name);
            if (withName == true)
            {
                error = $"There already exists another boundary with the name {name} in the parent boundary!";
                return false;
            }
            Name = name;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            return true;
        }

        public bool SetDescription(string description, ref string error)
        {
            Description = description;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description)));
            return true;
        }

        internal bool RemoveStart(Start start, ref string error)
        {
            if (!_Starts.Remove(start))
            {
                error = "Unable to find a the given start!";
                return false;
            }
            return true;
        }

        internal bool AddStart(ModelSystemSession session, string startName, out Start start, ref string error)
        {
            start = null;
            // ensure the name is unique between starting points
            foreach (var ms in _Starts)
            {
                if (ms.Name.Equals(startName, StringComparison.OrdinalIgnoreCase))
                {
                    error = "There already exists a start with the same name!";
                    return false;
                }
            }
            start = new Start(session, startName, this, null, new Point(0, 0));
            _Starts.Add(start);
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
        internal bool AddStart(ModelSystemSession session, string startName, Start start, ref string error)
        {
            // ensure the name is unique between starting points
            foreach (var ms in _Starts)
            {
                if (ms.Name.Equals(startName, StringComparison.OrdinalIgnoreCase))
                {
                    error = "There already exists a start with the same name!";
                    return false;
                }
            }
            _Starts.Add(start);
            return true;
        }

        internal bool AddNode(ModelSystemSession session, string name, Type type, out Node node, ref string error)
        {
            node = Node.Create(session, name, type, this);
            _Modules.Add(node);
            return true;
        }

        internal bool AddNode(ModelSystemSession session, string name, Type type, Node node, ref string error)
        {
            _Modules.Add(node);
            return true;
        }
    }
}
