/*
    Copyright 2017-2021 University of Toronto
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
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.ComponentModel;
using XTMF2.ModelSystemConstruct;
using XTMF2.Repository;

namespace XTMF2.Editing
{
    public sealed class ModelSystemSession : IDisposable, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private int _References = 0;

        public int References => _References;

        public ModelSystem ModelSystem { get; internal set; }

        private readonly ProjectSession _session;

        private readonly Lock _sessionLock = new ();

        public ModelSystemHeader ModelSystemHeader { get; private set; }

        /// <summary>
        /// The project that this model system session belongs to.
        /// </summary>
        public Project Project => _session.Project;

        private readonly CommandBuffer Buffer = new CommandBuffer();

        public ModelSystemSession(ProjectSession session, ModelSystem modelSystem)
        {
            ModelSystem = modelSystem;
            ModelSystemHeader = modelSystem.Header;
            _session = session.AddReference();
            ((INotifyPropertyChanged)Buffer).PropertyChanged += OnBufferPropertyChanged;
        }

        private void OnBufferPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(CanUndo) or nameof(CanRedo))
                PropertyChanged?.Invoke(this, e);
        }

        public void Dispose()
        {
            _session.ModelSystemSessionDecrementing(this, ref _References);
        }

        internal ModuleRepository GetModuleRepository()
        {
            return _session.GetModuleRepository();
        }

        /// <summary>
        /// A live, observable read-only view of all module types registered in the runtime.
        /// GUI components can bind to this to populate type-picker lists.
        /// </summary>
        public System.Collections.ObjectModel.ReadOnlyObservableCollection<Type> LoadedModuleTypes
            => GetModuleRepository().LoadedModuleTypes;

        /// <summary>
        /// Snapshot of open-generic module types registered in the runtime (e.g.
        /// <c>BasicParameter&lt;&gt;</c>).  Use <see cref="GetCompatibleModuleTypes"/> to
        /// obtain the full set of types (closed + constructed) that satisfy a specific hook.
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<Type> OpenGenericModuleTypes
            => GetModuleRepository().OpenGenericModuleTypes;

        /// <summary>
        /// Returns every module type that is compatible with <paramref name="hookType"/>:
        /// closed types already in <see cref="LoadedModuleTypes"/> that are directly assignable,
        /// plus any closed generics that can be constructed from open-generic module types.
        /// </summary>
        public System.Collections.Generic.IEnumerable<Type> GetCompatibleModuleTypes(Type hookType)
        {
            var repo = GetModuleRepository();
            return repo.LoadedModuleTypes
                       .Where(t => hookType.IsAssignableFrom(t))
                       .Concat(repo.GetCompatibleConstructedTypes(hookType));
        }

        /// <summary>
        /// Change the type of an existing node, with full undo/redo support.
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="node">The node whose type should change.</param>
        /// <param name="type">The new module type.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeded, false with a message otherwise.</returns>
        public bool SetNodeType(User user, Node node, Type type, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(node);
            ArgumentNullException.ThrowIfNull(type);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var previousType = node.Type;
                string? err = null;
                if (node.SetType(GetModuleRepository(), type, ref err))
                {
                    Buffer.AddUndo(new Command(() =>
                    {
                        string? e = null;
                        _ = node.SetType(GetModuleRepository(), previousType, ref e);
                        return (true, e is null ? null : new CommandError(e));
                    }, () =>
                    {
                        string? e = null;
                        _ = node.SetType(GetModuleRepository(), type, ref e);
                        return (true, e is null ? null : new CommandError(e));
                    }));
                    error = null;
                    return true;
                }
                error = new CommandError(err ?? "Failed to set the node type.");
                return false;
            }
        }

        /// <summary>
        /// Set the name of a given boundary.
        /// </summary>
        /// <param name="user">The user issuing the action.</param>
        /// <param name="boundary">The boundary to change.</param>
        /// <param name="name">The new name to assign to the boundary, must be unique.</param>
        /// <param name="error">An error message if the operation fails with the reason why.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message stored in error.</returns>
        public bool SetBoundaryName(User user, Boundary boundary, string name, [NotNullWhen(false)] out CommandError? error)
        {
            error = null;
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(boundary);

            if (String.IsNullOrWhiteSpace(name))
            {
                error = new CommandError("A boundary requires a unique name");
                return false;
            }
            if (!_session.HasAccess(user))
            {
                error = new CommandError("The user does not have access to this project.", true);
                return false;
            }
            lock (_sessionLock)
            {
                var oldName = boundary.Name;
                if (boundary.SetName(name, out error))
                {
                    Buffer.AddUndo(new Command(() =>
                    {
                        return (boundary.SetName(oldName, out var e), e);
                    }, () =>
                    {
                        return (boundary.SetName(name, out var e), e);
                    }));
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Sets the description for the given boundary.
        /// </summary>
        /// <param name="user">The user issuing the action.</param>
        /// <param name="boundary">The boundary to change.</param>
        /// <param name="description">The new description to assign to the boundary.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message stored in error.</returns>
        public bool SetBoundaryDescription(User user, Boundary boundary, string description, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(boundary);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var oldDescription = boundary.Description;
                if (boundary.SetDescription(description, out error))
                {
                    Buffer.AddUndo(new Command(() =>
                    {
                        return (boundary.SetDescription(oldDescription, out var e), e);
                    }, () =>
                    {
                        return (boundary.SetDescription(description, out var e), e);
                    }));
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Add a new boundary to a parent boundary
        /// </summary>
        /// <param name="user">The user requesting the action</param>
        /// <param name="parentBoundary">The boundary that will gain the child</param>
        /// <param name="name">The name of the new boundary</param>
        /// <param name="boundary">The resulting boundary</param>
        /// <param name="error">An error message if the operation fails</param>
        /// <returns>True if the operation works, false otherwise with an error message.</returns>
        public bool AddBoundary(User user, Boundary parentBoundary, string name, out Boundary? boundary, [NotNullWhen(false)] out CommandError? error)
        {
            boundary = null;
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(parentBoundary);

            if (String.IsNullOrWhiteSpace(name))
            {
                error = new CommandError("A boundary requires a unique name");
                return false;
            }
            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                if (parentBoundary.AddBoundary(name, out boundary, out error))
                {
                    var _b = boundary!;
                    Buffer.AddUndo(new Command(() =>
                    {
                        return (parentBoundary.RemoveBoundary(_b, out var e), e);
                    }, () =>
                    {
                        return (parentBoundary.AddBoundary(_b, out var e), e);
                    }));
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Add a new comment block to a boundary at the given location.
        /// </summary>
        /// <param name="localUser">The user requesting this operation.</param>
        /// <param name="location">The location to send the request to.</param>
        /// <param name="block">The newly generated comment block, null if the operation fails.</param>
        /// <param name="error">The error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false with an error message otherwise.</returns>
        public bool AddCommentBlock(User user, Boundary boundary, string comment, Rectangle location, out CommentBlock? block,
            [NotNullWhen(false)] out CommandError? error)
        {
            block = null;
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(boundary);

            if (String.IsNullOrWhiteSpace(comment))
            {
                error = new CommandError("There was no comment to store.");
                return false;
            }
            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                if (boundary.AddCommentBlock(comment, location, out block, out error))
                {
                    var _block = block!;
                    Buffer.AddUndo(new Command(() =>
                    {
                        return (boundary.RemoveCommentBlock(_block, out var e), e);
                    }, () =>
                    {
                        return (boundary.AddCommentBlock(_block, out var e), e);
                    }));
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Add a new reference to the model system session
        /// </summary>
        internal void AddReference()
        {
            Interlocked.Increment(ref _References);
        }

        /// <summary>
        /// Remove the comment block from the given boundary.
        /// </summary>
        /// <param name="user">The user requesting the action.</param>
        /// <param name="boundary">The containing boundary</param>
        /// <param name="block">The comment block to remove.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false with an error message otherwise.</returns>
        public bool RemoveCommentBlock(User user, Boundary boundary, CommentBlock block, [NotNullWhen(false)]out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(boundary);
            ArgumentNullException.ThrowIfNull(block);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                if (boundary.RemoveCommentBlock(block, out error))
                {
                    Buffer.AddUndo(new Command(() =>
                    {
                        return (boundary.AddCommentBlock(block, out var e), e);
                    }, () =>
                    {
                        return (boundary.RemoveCommentBlock(block, out var e), e);
                    }));
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Sets the location of the CommentBlock
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="commentBlock">The comment block to change.</param>
        /// <param name="newLocation">The location to set the comment block to.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        public bool SetCommentBlockLocation(User user, CommentBlock commentBlock, Rectangle newLocation, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(commentBlock);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var oldLocation = commentBlock.Location;
                commentBlock.Location = newLocation;
                Buffer.AddUndo(new Command(() =>
                {
                    commentBlock.Location = oldLocation;
                    return (true, null);
                }, () =>
                {
                    commentBlock.Location = newLocation;
                    return (true, null);
                }));
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Set the comment text within a comment block
        /// </summary>
        /// <param name="user">The using issuing the command.</param>
        /// <param name="commentBlock">The comment block to edit.</param>
        /// <param name="newText">The new text to set.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        public bool SetCommentBlockText(User user, CommentBlock commentBlock, string newText, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(commentBlock);

            if (string.IsNullOrEmpty(newText))
            {
                error = new CommandError("A comment block must have text!");
                return false;
            }
            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var oldComment = commentBlock.Comment;
                commentBlock.Comment = newText;
                Buffer.AddUndo(new Command(() =>
                {
                    commentBlock.Comment = oldComment;
                    return (true, null);
                }, () =>
                {
                    commentBlock.Comment = newText;
                    return (true, null);
                }));
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Remove a boundary from a model system
        /// </summary>
        /// <param name="user">The user that removed the boundary</param>
        /// <param name="parentBoundary">The parent boundary of this one to remove</param>
        /// <param name="boundary">The boundary to remove</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false with an error message otherwise.</returns>
        public bool RemoveBoundary(User user, Boundary parentBoundary, Boundary boundary, [NotNullWhen(false)]out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(parentBoundary);
            ArgumentNullException.ThrowIfNull(boundary);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var linksGoingToRemovedBoundary = ModelSystem.GlobalBoundary.GetLinksGoingToBoundary(boundary);
                if (parentBoundary.RemoveBoundary(boundary, out error))
                {
                    var multiLinkHelper = new Dictionary<MultiLink, List<(int Index, Node MSS)>>();
                    foreach (var link in linksGoingToRemovedBoundary)
                    {
                        if (link is MultiLink ml)
                        {
                            var list = new List<(int Index, Node MSS)>();
                            var dests = ml.Destinations;
                            for (int i = 0; i < dests.Count; i++)
                            {
                                if (dests[i].ContainedWithin == boundary)
                                {
                                    list.Add((i, dests[i]));
                                }
                            }
                            multiLinkHelper[ml] = list;
                        }
                    }
                    bool RemoveLinks([NotNullWhen(false)] out CommandError? error2)
                    {
                        foreach (var link in linksGoingToRemovedBoundary)
                        {
                            if (link is SingleLink sl)
                            {
                                if (!link.Origin!.ContainedWithin!.RemoveLink(link, out error2))
                                {
                                    return false;
                                }
                            }
                            else if (link is MultiLink ml)
                            {
                                var dests = ml.Destinations;
                                for (int i = 0; i < dests.Count; i++)
                                {
                                    if (dests[i].ContainedWithin == boundary)
                                    {
                                        ml.RemoveDestination(i);
                                        i--;
                                    }
                                }
                            }
                        }
                        error2 = null;
                        return true;
                    }
                    if (!RemoveLinks(out error))
                    {
                        return false;
                    }
                    Buffer.AddUndo(new Command(() =>
                    {
                        if (parentBoundary.AddBoundary(boundary, out var e))
                        {
                            foreach (var link in linksGoingToRemovedBoundary)
                            {
                                if (link is SingleLink sl)
                                {
                                    link.Origin!.ContainedWithin!.AddLink(link, out e);
                                }
                                else if (link is MultiLink ml)
                                {
                                    var list = multiLinkHelper[ml];
                                    foreach (var (Index, MSS) in list)
                                    {
                                        if(!ml.AddDestination(MSS, Index, out e))
                                        {                                            
                                            return (false, e);
                                        }
                                    }
                                }
                            }
                            return (true, null);
                        }
                        return (false, e);
                    }, () =>
                    {
                        if (!RemoveLinks(out var e))
                        {
                            return (false, e);
                        }
                        return (parentBoundary.RemoveBoundary(boundary, out e), e);
                    }));
                    return true;
                }
            }
            error = new CommandError("Failed to remove the boundary.");
            return false;
        }

        /// <summary>
        /// Adds a new model system start element.
        /// </summary>
        /// <param name="user">The user requesting to add the new start node.</param>
        /// <param name="startName">The name of the start element.  This must be unique in the model system.</param>
        /// <param name="location">The location to put the start.</param>
        /// <param name="start">The newly created start node</param>
        /// <param name="error">A message describing why the start node was rejected.</param>
        /// <returns>True if the operation succeeds, false otherwise.</returns>
        public bool AddModelSystemStart(User user, Boundary boundary, string startName, Rectangle location, out Start? start, [NotNullWhen(false)]out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(boundary);

            const string badStartName = "The start name must be unique within the model system and not empty.";
            start = null;
            if (String.IsNullOrWhiteSpace(startName))
            {
                error = new CommandError(badStartName);
                return false;
            }
            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                if (!ModelSystem.Contains(boundary))
                {
                    error = new CommandError("The passed in boundary is not part of the model system!");
                    return false;
                }
                var success = boundary.AddStart(this, startName, location, out start, out error);
                if (success)
                {
                    Start _start = start!;
                    Buffer.AddUndo(new Command(() =>
                    {
                        return (boundary.RemoveStart(_start, out var e), e);
                    }, () =>
                    {
                        return (boundary.AddStart(startName, _start, out var e), e);
                    }));
                }
                return success;
            }
        }

        /// <summary>
        /// Remove the given start from the given boundary.
        /// </summary>
        /// <param name="user"></param>
        /// <param name="start"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        public bool RemoveStart(User user, Start start, [NotNullWhen(false)]out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(start);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var boundary = start.ContainedWithin!;
                if (boundary.RemoveStart(start, out error))
                {
                    Buffer.AddUndo(new Command(() =>
                    {
                        return (boundary.AddStart(start, out var e), e);
                    }, () =>
                    {
                        return (boundary.RemoveStart(start, out var e), e);
                    }));
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Creates a new node in the given boundary.
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="boundary">The boundary that the new node will be created in.</param>
        /// <param name="name">The name to give to the new node.</param>
        /// <param name="type">The type of module to assign to the new node.</param>
        /// <param name="location">The location to create the new node.</param>
        /// <param name="node">The resulting node if the operation succeeds, null if the operation fails.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message stored in error.</returns>
        public bool AddNode(User user, Boundary boundary, string name, Type type, Rectangle location, out Node? node, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(boundary);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    node = null;
                    return false;
                }
                if (boundary.AddNode(GetModuleRepository(), name, type, location, out node, out error))
                {
                    Node _node = node!;
                    Buffer.AddUndo(new Command(() =>
                    {
                        return (boundary.RemoveNode(_node, out var e), e);
                    }, () =>
                    {
                        return (boundary.AddNode(_node, out var e), e);
                    }));
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Add a node to the boundary in addition to generating all of the parameters as BasicParameters with their default values.
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="boundary">The boundary to add the module to.</param>
        /// <param name="name">The name of the node to add.</param>
        /// <param name="type">The type of the module to use.</param>
        /// <param name="node">The resulting node object.</param>
        /// <param name="children"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        public bool AddNodeGenerateParameters(User user, Boundary boundary, string name, Type type,
            Rectangle location, out Node? node, out List<Node>? children, [NotNullWhen(false)]out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(boundary);

            children = null;
            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    node = null;
                    return false;
                }
                bool success = boundary.AddNode(GetModuleRepository(), name, type, location, out node, out error);
                if (success)
                {
                    // now generate the children
                    List<Link> links;
                    (children, links) = GetChidren(node!, boundary);
                    var localChildren = children;
                    void Add()
                    {
                        CommandError? e = null;
                        foreach (var child in localChildren)
                        {
                            boundary.AddNode(child, out e);
                        }
                        foreach (var link in links)
                        {
                            boundary.AddLink(link, out e);
                        }
                    }
                    void Remove()
                    {
                        CommandError? e = null;
                        foreach (var link in links)
                        {
                            boundary.RemoveLink(link, out e);
                        }
                        foreach (var child in localChildren!)
                        {
                            boundary.RemoveNode(child, out e);
                        }
                    }
                    Add();
                    Node _node = node!;
                    Buffer.AddUndo(new Command(() =>
                    {
                        Remove();
                        return (boundary.RemoveNode(_node, out var e), e);
                    }, () =>
                    {
                        if (boundary.AddNode(_node, out var e))
                        {
                            Add();
                            return (true, null);
                        }
                        return (false, e);
                    }));
                }
                return success;
            }
        }

        private (List<Node> children, List<Link> links) GetChidren(Node baseNode, Boundary boundary)
        {
            var t = baseNode.Type;

            var nodes = new List<Node>();
            var links = new List<Link>();
            // If the type of the node is null then there are no children nor links.
            if (t is null)
            {
                return (nodes, links);
            }
            (var description, var typeinfo, var hooks) = GetModuleRepository()[t];

            foreach (var hook in hooks.Where(h => h.IsParameter))
            {
                // we can only add children for references to a generic
                var type = hook.Type;
                var genericParameters = type.GetGenericArguments();
                if (genericParameters.Length == 1)
                {
                    var functionType = typeof(RuntimeModules.BasicParameter<>).MakeGenericType(genericParameters[0]);
                    if (type.IsAssignableFrom(functionType))
                    {
                        var child = Node.Create(this.GetModuleRepository(), hook.Name, functionType, boundary, Rectangle.Hidden);
                        
                        if (child?.SetParameterValue(ParameterExpression.CreateParameter(hook.DefaultValue!, genericParameters[0]), out var error) == true)
                        {
                            nodes.Add(child);
                            // Construct the link object directly without adding it to the boundary.
                            // Add() will first add child nodes (creating NodeViewModels) and THEN
                            // add links, so that TryAddLinkViewModel can resolve the destination.
                            links.Add(new SingleLink(baseNode, hook, child, false));
                        }
                    }
                }
            }
            // Return null for now so it doesn't pass tests
            return (nodes, links);
        }

        /// <summary>
        /// Remove the given node.
        /// All outgoing links (where this node is the origin) and all incoming links (where
        /// this node is a destination) are also removed, keeping the model consistent.
        /// The removal — including all affected links — is registered as a single undoable command.
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="node">The node to be removed.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        /// <summary>
        /// Add a ghost node — a visual alias for <paramref name="referencedNode"/> —
        /// to <paramref name="boundary"/> at <paramref name="location"/>.
        /// Ghost nodes mirror their referenced node's name, have no hooks, and are
        /// rendered with a dashed outline.  When the real node is deleted all ghost
        /// nodes referencing it are automatically deleted as well.
        /// </summary>
        public bool AddGhostNode(User user, Boundary boundary, Node referencedNode, Rectangle location,
            [NotNullWhen(true)] out GhostNode? ghostNode,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(boundary);
            ArgumentNullException.ThrowIfNull(referencedNode);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    ghostNode = null;
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }

                var ghost = new GhostNode(referencedNode, boundary, location);
                if (!boundary.AddGhostNode(ghost, out error))
                {
                    ghostNode = null;
                    return false;
                }
                ghostNode = ghost;

                Buffer.AddUndo(new Command(() =>
                {
                    return (boundary.RemoveGhostNode(ghost, out var e), e);
                }, () =>
                {
                    return (boundary.AddGhostNode(ghost, out var e), e);
                }));
                return true;
            }
        }

        /// <summary>
        /// Remove a ghost node from its boundary, also removing any incoming links.
        /// </summary>
        public bool RemoveGhostNode(User user, GhostNode ghostNode,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(ghostNode);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }

                var boundary = ghostNode.ContainedWithin!;
                var incomingLinks = GetLinksGoingTo(ghostNode);
                var multiLinkInfo = BuildMultiLinkRestoreInfo(incomingLinks, ghostNode);

                RemoveIncomingLinks(incomingLinks, ghostNode, multiLinkInfo);

                if (boundary.RemoveGhostNode(ghostNode, out error))
                {
                    Buffer.AddUndo(new Command(() =>
                    {
                        if (boundary.AddGhostNode(ghostNode, out var e))
                        {
                            RestoreIncomingLinks(incomingLinks, ghostNode, multiLinkInfo);
                            return (true, null);
                        }
                        return (false, e);
                    }, () =>
                    {
                        RemoveIncomingLinks(incomingLinks, ghostNode, multiLinkInfo);
                        return (boundary.RemoveGhostNode(ghostNode, out var e), e);
                    }));
                    return true;
                }
                else
                {
                    RestoreIncomingLinks(incomingLinks, ghostNode, multiLinkInfo);
                    return false;
                }
            }
        }

        /// <summary>
        /// Moves a regular node (and all of its outgoing links) from its current boundary to
        /// <paramref name="targetBoundary"/>.  The operation is undoable.
        /// </summary>
        public bool MoveNodeToBoundary(User user, Node node, Boundary targetBoundary,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(node);
            ArgumentNullException.ThrowIfNull(targetBoundary);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }

                var oldBoundary = node.ContainedWithin!;
                if (ReferenceEquals(oldBoundary, targetBoundary)) { error = null; return true; }

                // A node that lives inside a FunctionTemplate's InternalModules may only be moved
                // to other boundaries within the *same* FunctionTemplate, never outside it.
                var oldFt = FindOwningFunctionTemplate(ModelSystem.GlobalBoundary, oldBoundary);
                var newFt = FindOwningFunctionTemplate(ModelSystem.GlobalBoundary, targetBoundary);
                if (!ReferenceEquals(oldFt, newFt))
                {
                    error = new CommandError(
                        oldFt is not null
                            ? $"Node '{node.Name}' is contained within FunctionTemplate '{oldFt.Name}' and cannot be moved outside of it."
                            : $"Cannot move a node from the global scope into FunctionTemplate '{newFt!.Name}'.");
                    return false;
                }

                // Outgoing links live in the origin node's boundary and must follow the node.
                var outgoingLinks = oldBoundary.Links.Where(l => l.Origin == node).ToList();

                // Collect hidden nodes: destination nodes of outgoing links whose location is
                // Rectangle.Hidden (i.e. inlined parameter nodes).  These must travel with the node.
                var hiddenNodes = outgoingLinks
                    .SelectMany(l => l is SingleLink sl
                        ? (sl.Destination is not null ? new[] { sl.Destination } : Array.Empty<Node>())
                        : (l is MultiLink ml ? ml.Destinations.ToArray() : Array.Empty<Node>()))
                    .Where(n => n.Location.Equals(Rectangle.Hidden) && ReferenceEquals(n.ContainedWithin, oldBoundary))
                    .Distinct()
                    .ToList();

                if (!oldBoundary.RemoveNode(node, out error)) return false;

                foreach (var link in outgoingLinks)
                    oldBoundary.RemoveLink(link, out _);

                foreach (var hidden in hiddenNodes)
                    oldBoundary.RemoveNode(hidden, out _);

                node.UpdateContainedWithin(targetBoundary);
                foreach (var hidden in hiddenNodes)
                    hidden.UpdateContainedWithin(targetBoundary);

                if (!targetBoundary.AddNode(node, out error))
                {
                    // Roll back hidden nodes and links before returning.
                    foreach (var hidden in hiddenNodes)
                    {
                        hidden.UpdateContainedWithin(oldBoundary);
                        oldBoundary.AddNode(hidden, out _);
                    }
                    node.UpdateContainedWithin(oldBoundary);
                    oldBoundary.AddNode(node, out _);
                    foreach (var link in outgoingLinks)
                        oldBoundary.AddLink(link, out _);
                    return false;
                }

                foreach (var hidden in hiddenNodes)
                    targetBoundary.AddNode(hidden, out _);

                foreach (var link in outgoingLinks)
                    targetBoundary.AddLink(link, out _);

                Buffer.AddUndo(new Command(() =>
                {
                    foreach (var link in outgoingLinks) targetBoundary.RemoveLink(link, out _);
                    foreach (var hidden in hiddenNodes)
                    {
                        targetBoundary.RemoveNode(hidden, out _);
                        hidden.UpdateContainedWithin(oldBoundary);
                        oldBoundary.AddNode(hidden, out _);
                    }
                    targetBoundary.RemoveNode(node, out _);
                    node.UpdateContainedWithin(oldBoundary);
                    oldBoundary.AddNode(node, out _);
                    foreach (var link in outgoingLinks) oldBoundary.AddLink(link, out _);
                    return (true, null);
                }, () =>
                {
                    foreach (var link in outgoingLinks) oldBoundary.RemoveLink(link, out _);
                    foreach (var hidden in hiddenNodes)
                    {
                        oldBoundary.RemoveNode(hidden, out _);
                        hidden.UpdateContainedWithin(targetBoundary);
                    }
                    oldBoundary.RemoveNode(node, out _);
                    node.UpdateContainedWithin(targetBoundary);
                    targetBoundary.AddNode(node, out _);
                    foreach (var hidden in hiddenNodes) targetBoundary.AddNode(hidden, out _);
                    foreach (var link in outgoingLinks) targetBoundary.AddLink(link, out _);
                    return (true, null);
                }));

                error = null;
                return true;
            }
        }

        /// <summary>
        /// Moves a ghost node from its current boundary to <paramref name="targetBoundary"/>.
        /// The operation is undoable.
        /// </summary>
        public bool MoveGhostNodeToBoundary(User user, GhostNode ghostNode, Boundary targetBoundary,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(ghostNode);
            ArgumentNullException.ThrowIfNull(targetBoundary);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }

                var oldBoundary = ghostNode.ContainedWithin!;
                if (ReferenceEquals(oldBoundary, targetBoundary)) { error = null; return true; }

                // Ghost nodes are subject to the same FunctionTemplate scope restriction as nodes.
                var oldFt = FindOwningFunctionTemplate(ModelSystem.GlobalBoundary, oldBoundary);
                var newFt = FindOwningFunctionTemplate(ModelSystem.GlobalBoundary, targetBoundary);
                if (!ReferenceEquals(oldFt, newFt))
                {
                    error = new CommandError(
                        oldFt is not null
                            ? $"Ghost node '{ghostNode.Name}' is contained within FunctionTemplate '{oldFt.Name}' and cannot be moved outside of it."
                            : $"Cannot move a ghost node from the global scope into FunctionTemplate '{newFt!.Name}'.");
                    return false;
                }

                if (!oldBoundary.RemoveGhostNode(ghostNode, out error)) return false;

                ghostNode.UpdateContainedWithin(targetBoundary);

                if (!targetBoundary.AddGhostNode(ghostNode, out error))
                {
                    ghostNode.UpdateContainedWithin(oldBoundary);
                    oldBoundary.AddGhostNode(ghostNode, out _);
                    return false;
                }

                Buffer.AddUndo(new Command(() =>
                {
                    targetBoundary.RemoveGhostNode(ghostNode, out _);
                    ghostNode.UpdateContainedWithin(oldBoundary);
                    oldBoundary.AddGhostNode(ghostNode, out _);
                    return (true, null);
                }, () =>
                {
                    oldBoundary.RemoveGhostNode(ghostNode, out _);
                    ghostNode.UpdateContainedWithin(targetBoundary);
                    targetBoundary.AddGhostNode(ghostNode, out _);
                    return (true, null);
                }));

                error = null;
                return true;
            }
        }

        public bool RemoveNode(User user, Node node, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(node);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var boundary = node.ContainedWithin!;

                // Collect all outgoing links (this node is the origin; stored in its own boundary).
                var outgoingLinks = boundary.Links.Where(l => l.Origin == node).ToList();

                // Collect all links that point TO this node from any boundary.
                var incomingLinks = GetLinksGoingTo(node);

                // For multi-links in the incoming set, record the exact destination indices
                // that refer to 'node' so they can be faithfully restored on undo.
                var multiLinkRestoreInfo = BuildMultiLinkRestoreInfo(incomingLinks, node);

                // Cascade: collect all ghost nodes referencing this node, plus their links.
                var ghostsOfNode = GetAllGhostNodesOf(node);
                // Per ghost: (ghost, incomingLinksToGhost, multiLinkRestoreInfo for ghost)
                var ghostCascadeData = ghostsOfNode
                    .Select(g =>
                    {
                        var gLinks = GetLinksGoingTo(g);
                        return (Ghost: g, Links: gLinks, MultiInfo: BuildMultiLinkRestoreInfo(gLinks, g));
                    })
                    .ToList();

                // Remove all incoming links (or just the relevant destination entries).
                void RemoveIncoming()
                {
                    RemoveIncomingLinks(incomingLinks, node, multiLinkRestoreInfo);
                }

                // Restore all incoming links (inverse of RemoveIncoming).
                void RestoreIncoming()
                {
                    RestoreIncomingLinks(incomingLinks, node, multiLinkRestoreInfo);
                }

                void RemoveGhostCascade()
                {
                    foreach (var (ghost, gLinks, gMultiInfo) in ghostCascadeData)
                    {
                        RemoveIncomingLinks(gLinks, ghost, gMultiInfo);
                        ghost.ContainedWithin!.RemoveGhostNode(ghost, out _);
                    }
                }

                void RestoreGhostCascade()
                {
                    foreach (var (ghost, gLinks, gMultiInfo) in ghostCascadeData)
                    {
                        ghost.ContainedWithin!.AddGhostNode(ghost, out _);
                        RestoreIncomingLinks(gLinks, ghost, gMultiInfo);
                    }
                }

                // Remove incoming links, then ghost cascade, then outgoing links, then the node itself.
                RemoveIncoming();
                RemoveGhostCascade();
                foreach (var link in outgoingLinks)
                    boundary.RemoveLink(link, out _);

                // Also remove from model system variables if present, capturing position for undo.
                var variableIndex = ModelSystem.Variables.IndexOf(node);
                if (variableIndex >= 0)
                    ModelSystem.Variables.RemoveAt(variableIndex);

                // If the node lives inside a FunctionTemplate's InternalModules and was exposed
                // as a hook, remove it from the exposed list so the template stays consistent.
                var owningTemplate = boundary.Parent?.FunctionTemplates
                    .FirstOrDefault(ft => ft.InternalModules == boundary);
                bool wasExposedInTemplate = owningTemplate is not null && owningTemplate.ExposedNodes.Contains(node);
                if (wasExposedInTemplate)
                    owningTemplate!.RemoveExposedNode(node);

                if (boundary.RemoveNode(node, out error))
                {
                    Buffer.AddUndo(new Command(() =>
                    {
                        // Undo: restore node first, then its outgoing links, then all incoming links, then ghosts.
                        if (boundary.AddNode(node, out var e))
                        {
                            foreach (var link in outgoingLinks)
                                boundary.AddLink(link, out e);
                            RestoreIncoming();
                            RestoreGhostCascade();
                            if (variableIndex >= 0)
                            {
                                var restoreIdx = Math.Min(variableIndex, ModelSystem.Variables.Count);
                                ModelSystem.Variables.Insert(restoreIdx, node);
                            }
                            if (wasExposedInTemplate)
                                owningTemplate!.AddExposedNode(node);
                            return (true, null);
                        }
                        return (false, e);
                    }, () =>
                    {
                        // Redo: same sequence as the original removal.
                        RemoveIncoming();
                        RemoveGhostCascade();
                        foreach (var link in outgoingLinks)
                            boundary.RemoveLink(link, out _);
                        ModelSystem.Variables.Remove(node);
                        if (wasExposedInTemplate)
                            owningTemplate!.RemoveExposedNode(node);
                        return (boundary.RemoveNode(node, out var e), e);
                    }));
                    return true;
                }
                else
                {
                    // Node removal failed; roll back the link removals and ghost cascade.
                    foreach (var link in outgoingLinks)
                        boundary.AddLink(link, out _);
                    RestoreGhostCascade();
                    RestoreIncoming();
                    if (variableIndex >= 0)
                    {
                        var restoreIdx = Math.Min(variableIndex, ModelSystem.Variables.Count);
                        ModelSystem.Variables.Insert(restoreIdx, node);
                    }
                    if (wasExposedInTemplate)
                        owningTemplate!.AddExposedNode(node);
                    return false;
                }
            }
        }

        /// <summary>
        /// Remove the given node and all of its generic parameters.
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="node">The node to remove.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, False with an error message otherwise.</returns>
        public bool RemoveNodeGenerateParameters(User user, Node node, [NotNullWhen(false)]out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(node);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var boundary = node.ContainedWithin!;
                // Find all of the basic parameters for the node that we need to remove.
                var basicParameters = new List<(Node basicParameterNode, SingleLink basicParameterLink)>();
                var advancedParameters = new List<Link>();
                foreach (var link in boundary.Links.Where(link => link.Origin == node))
                {
                    if (link.OriginHook!.IsParameter && link is SingleLink singleLink)
                    {
                        var destNode = singleLink.Destination!;
                        var destType = destNode.Type;
                        if (destType != null && destType.IsGenericType && destType.GetGenericTypeDefinition() == typeof(RuntimeModules.BasicParameter<>))
                        {
                            // check to see if this would be the only link referencing it.
                            List<Link> linksGoingTo = GetLinksGoingTo(destNode);
                            if (linksGoingTo?.Count == 1)
                            {
                                basicParameters.Add((destNode, singleLink));
                            }
                            else
                            {
                                advancedParameters.Add(singleLink);
                            }
                        }
                    }
                }
                if (boundary.RemoveNode(node, out error))
                {
                    void RemoveParameters()
                    {
                        CommandError? e;
                        foreach (var p in advancedParameters)
                        {
                            boundary.RemoveLink(p, out e);
                        }
                        foreach (var (basicParameterNode, basicParameterLink) in basicParameters)
                        {
                            boundary.RemoveLink(basicParameterLink, out e);
                            boundary.RemoveNode(basicParameterNode, out e);
                        }
                    }
                    void AddParameters()
                    {
                        CommandError? e;
                        if (boundary is object)
                        {
                            foreach (var (basicParameterNode, basicParameterLink) in basicParameters!)
                            {
                                boundary.AddNode(basicParameterNode, out e);
                                boundary.AddLink(basicParameterLink, out e);
                            }
                            foreach (var p in advancedParameters!)
                            {
                                boundary.AddLink(p, out e);
                            }
                        }
                    }
                    RemoveParameters();
                    Buffer.AddUndo(new Command(() =>
                    {
                        if (boundary.AddNode(node, out var e))
                        {
                            AddParameters();
                            return (true, null);
                        }
                        return (false, e);
                    }, () =>
                    {
                        if (boundary.RemoveNode(node, out var e))
                        {
                            RemoveParameters();
                            return (true, null);
                        }
                        return (false, e);
                    }));
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Renames the given node (or start) within the model system, with undo support.
        /// </summary>
        /// <param name="user">The user issuing the action.</param>
        /// <param name="node">The node (or start) to rename.</param>
        /// <param name="name">The new name to assign.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise.</returns>
        public bool SetNodeName(User user, Node node, string name, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(node);

            if (string.IsNullOrWhiteSpace(name))
            {
                error = new CommandError("A node name must not be empty or whitespace.");
                return false;
            }

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }

                var oldName = node.Name;
                if (node.SetName(name, out error))
                {
                    Buffer.AddUndo(new Command(() =>
                    {
                        return (node.SetName(oldName, out var e), e);
                    }, () =>
                    {
                        return (node.SetName(name, out var e), e);
                    }));
                    return true;
                }
                return false;
            }
        }

        public bool SetNodeLocation(User user, Node mss, Rectangle newLocation, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(mss);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var oldLocation = mss.Location;
                mss.SetLocation(newLocation);
                Buffer.AddUndo(new Command(() =>
                {
                    mss.SetLocation(oldLocation);
                    return (true, null);
                }, () =>
                {
                    mss.SetLocation(newLocation);
                    return (true, null);
                }));
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Walks the entire boundary tree rooted at <paramref name="root"/> and returns the
        /// <see cref="FunctionTemplate"/> whose <see cref="FunctionTemplate.InternalModules"/>
        /// subtree contains <paramref name="boundary"/>, or <see langword="null"/> when the
        /// boundary belongs to the global (non-FunctionTemplate) scope.
        /// </summary>
        private static FunctionTemplate? FindOwningFunctionTemplate(Boundary root, Boundary boundary)
        {
            foreach (var ft in root.FunctionTemplates)
            {
                if (ReferenceEquals(ft.InternalModules, boundary) || ft.InternalModules.Contains(boundary))
                    return ft;
            }
            foreach (var child in root.Boundaries)
            {
                var result = FindOwningFunctionTemplate(child, boundary);
                if (result is not null)
                    return result;
            }
            return null;
        }

        private List<Link> GetLinksGoingTo(Node destNode)
        {
            var ret = new List<Link>();
            var destBoundary = destNode.ContainedWithin!;
            ret.AddRange(destBoundary.Links.Where(l => l.HasDestination(destNode)));
            ret.AddRange(ModelSystem.GlobalBoundary.GetLinksGoingToBoundary(destBoundary).Where(l => l.HasDestination(destNode)));
            return ret;
        }

        /// <summary>
        /// Builds a restore-info dictionary for MultiLink entries that point to
        /// <paramref name="destNode"/> in <paramref name="incomingLinks"/>.
        /// </summary>
        private static Dictionary<MultiLink, List<(int Index, Node Dest)>> BuildMultiLinkRestoreInfo(
            List<Link> incomingLinks, Node destNode)
        {
            var info = new Dictionary<MultiLink, List<(int Index, Node Dest)>>();
            foreach (var link in incomingLinks)
            {
                if (link is MultiLink ml)
                {
                    var list = new List<(int Index, Node Dest)>();
                    var dests = ml.Destinations;
                    for (int i = 0; i < dests.Count; i++)
                    {
                        if (dests[i] == destNode)
                            list.Add((i, dests[i]));
                    }
                    info[ml] = list;
                }
            }
            return info;
        }

        /// <summary>
        /// Removes incoming links that target <paramref name="destNode"/> from their
        /// respective boundaries, using pre-computed multi-link restore info.
        /// </summary>
        private static void RemoveIncomingLinks(
            List<Link> incomingLinks, Node destNode,
            Dictionary<MultiLink, List<(int Index, Node Dest)>> multiLinkInfo)
        {
            foreach (var link in incomingLinks)
            {
                if (link is SingleLink)
                {
                    link.Origin!.ContainedWithin!.RemoveLink(link, out _);
                }
                else if (link is MultiLink ml && multiLinkInfo.TryGetValue(ml, out var list))
                {
                    // Remove back-to-front so indices remain valid.
                    for (int i = list.Count - 1; i >= 0; i--)
                        ml.RemoveDestination(list[i].Index);

                    if (ml.Destinations.Count == 0)
                        ml.Origin!.ContainedWithin!.RemoveLink(ml, out _);
                }
            }
        }

        /// <summary>
        /// Restores incoming links that were previously removed by
        /// <see cref="RemoveIncomingLinks"/>.
        /// </summary>
        private static void RestoreIncomingLinks(
            List<Link> incomingLinks, Node destNode,
            Dictionary<MultiLink, List<(int Index, Node Dest)>> multiLinkInfo)
        {
            foreach (var link in incomingLinks)
            {
                if (link is SingleLink)
                {
                    link.Origin!.ContainedWithin!.AddLink(link, out _);
                }
                else if (link is MultiLink ml && multiLinkInfo.TryGetValue(ml, out var list))
                {
                    // Re-add the link object if it was fully removed.
                    if (!ml.Origin!.ContainedWithin!.Links.Contains(ml))
                        ml.Origin.ContainedWithin.AddLink(ml, out _);

                    // Re-insert destinations in original order.
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (!ml.AddDestination(list[i].Dest, list[i].Index, out _))
                            return;
                    }
                }
            }
        }

        /// <summary>
        /// Returns every <see cref="GhostNode"/> anywhere in the model system that
        /// references <paramref name="realNode"/>.
        /// </summary>
        /// <summary>
        /// Returns every <see cref="FunctionInstance"/> anywhere in the model system that
        /// references <paramref name="template"/>.
        /// </summary>
        private List<FunctionInstance> GetAllFunctionInstancesOf(FunctionTemplate template)
        {
            var result = new List<FunctionInstance>();
            var stack = new Stack<Boundary>();
            stack.Push(ModelSystem.GlobalBoundary);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                foreach (var child in current.Boundaries)
                    stack.Push(child);
                foreach (var ft in current.FunctionTemplates)
                    stack.Push(ft.InternalModules);
                foreach (var fi in current.FunctionInstances)
                    if (fi.Template == template)
                        result.Add(fi);
            }
            return result;
        }

        private List<GhostNode> GetAllGhostNodesOf(Node realNode)
        {
            var result = new List<GhostNode>();
            var stack = new Stack<Boundary>();
            stack.Push(ModelSystem.GlobalBoundary);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                foreach (var child in current.Boundaries)
                    stack.Push(child);
                // Also traverse the InternalModules of every FunctionTemplate in this
                // boundary — they are not exposed through Boundaries and would otherwise
                // be missed, leaving orphaned ghost nodes after the real node is deleted.
                foreach (var ft in current.FunctionTemplates)
                    stack.Push(ft.InternalModules);
                foreach (var ghost in current.GhostNodes)
                    if (ghost.ReferencedNode == realNode)
                        result.Add(ghost);
            }
            return result;
        }

        /// <summary>
        /// Set the value of a parameter
        /// </summary>
        /// <param name="user">The user issuing the command</param>
        /// <param name="basicParameter">The parameter to set.</param>
        /// <param name="value">The value to set the parameter to.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        public bool SetParameterValue(User user, Node basicParameter, string value, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(basicParameter);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var previousValue = basicParameter.ParameterValue;
                var newValue = ParameterExpression.CreateParameter(value, basicParameter.Type.GetGenericArguments()[0]);
                if (basicParameter.SetParameterValue(newValue, out error))
                {
                    Buffer.AddUndo(new Command(() =>
                    {
                        return (basicParameter.SetParameterValue(previousValue!, out var e), e);
                    }, () =>
                    {
                        return (basicParameter.SetParameterValue(newValue, out var e), e);
                    }));
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Set the value of a parameter to an expression
        /// </summary>
        /// <param name="user">The user issuing the command</param>
        /// <param name="basicParameter">The parameter to set.</param>
        /// <param name="value">The value to set the parameter to.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        public bool SetParameterExpression(User user, Node basicParameter, string expression, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(basicParameter);

            lock(_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var previousType = basicParameter.Type;
                var previousValue = basicParameter.ParameterValue;
                if(basicParameter.SetParameterExpression(ModelSystem.Variables, expression, out error))
                {
                    var newType = basicParameter.Type;
                    var newExpression = basicParameter.ParameterValue;
                    Buffer.AddUndo(new Command(()=>
                    {
                        string? error = null;
                        _ = basicParameter.SetType(GetModuleRepository(), previousType, ref error);
                        _ = basicParameter.SetParameterValue(previousValue, out var e);
                        return (true, e);
                    }, ()=>
                    {
                        string? error = null;
                        _ = basicParameter.SetType(GetModuleRepository(), newType, ref error);
                        _ = basicParameter.SetParameterValue(newExpression, out var e);
                        return (true, e);
                    }));
                    return true;
                }
                return false;
            }
        }     

        /// <summary>
        /// Add a node to the model system's variable list, making it available for use
        /// in parameter expressions.
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="node">The node to add as a variable.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if successful, false otherwise with an error message.</returns>
        public bool AddVariable(User user, Node node, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(node);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                if (ModelSystem.Variables.Contains(node))
                {
                    error = new CommandError("The node is already a model system variable.");
                    return false;
                }
                ModelSystem.Variables.Add(node);
                Buffer.AddUndo(new Command(
                    () => { ModelSystem.Variables.Remove(node); return (true, null); },
                    () => { ModelSystem.Variables.Add(node);    return (true, null); }));
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Remove a node from the model system's variable list.
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="node">The node to remove.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if successful, false otherwise with an error message.</returns>
        public bool RemoveVariable(User user, Node node, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(node);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var idx = ModelSystem.Variables.IndexOf(node);
                if (idx < 0)
                {
                    error = new CommandError("The node is not a model system variable.");
                    return false;
                }
                ModelSystem.Variables.RemoveAt(idx);
                Buffer.AddUndo(new Command(
                    () => {
                        var restoreIdx = Math.Min(idx, ModelSystem.Variables.Count);
                        ModelSystem.Variables.Insert(restoreIdx, node);
                        return (true, null);
                    },
                    () => { ModelSystem.Variables.Remove(node); return (true, null); }));
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Set the node to the disabled state.
        /// </summary>
        /// <param name="user">The user issuing the command</param>
        /// <param name="node">The node</param>
        /// <param name="disabled">If it should be disabled (true) or not (false).</param>
        /// <param name="error">An error message explaining why the operation failed.</param>
        /// <returns>True if the operation completed successfully, false otherwise.</returns>
        public bool SetNodeDisabled(User user, Node node, bool disabled, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(node);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                error = null;
                if (node.SetDisabled(disabled, out error))
                {
                    Buffer.AddUndo(new Command(() =>
                    {
                        return (node.SetDisabled(!disabled, out var error), error);
                    }, () =>
                    {
                        return (node.SetDisabled(disabled, out var error), error);
                    }));
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Set if the given link should be disabled.
        /// </summary>
        /// <param name="user">The user issuing the command</param>
        /// <param name="link">The link to operate on.</param>
        /// <param name="disabled">If it should be disabled (true) or not (false).</param>
        /// <param name="error">An error message explaining why the operation failed.</param>
        /// <returns>True if the operation completed successfully, false otherwise.</returns>
        public bool SetLinkDisabled(User user, Link link, bool disabled, [NotNullWhen(false)]out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(link);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                if (link.SetDisabled(disabled, out error))
                {
                    Buffer.AddUndo(new Command(() =>
                    {
                        return (link.SetDisabled(!disabled, out var error), error);
                    }, () =>
                    {
                        return (link.SetDisabled(disabled, out var error), error);
                    }));
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Save the model system
        /// </summary>
        /// <param name="error">An error message in case the save fails.</param>
        /// <returns>True if it succeeds, false with an error message otherwise.</returns>
        public bool Save(out CommandError? error)
        {
            lock (_sessionLock)
            {
                string? errorString = null;
                if (!ModelSystem.Save(ref errorString))
                {
                    error = new CommandError(errorString ?? "No error message given when failing to save the model system!");
                    return false;
                }
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Save the model system to the given stream
        /// </summary>
        /// <param name="error">An error message if something goes wrong saving the model system.</param>
        /// <param name="saveTo">The stream to save the model system to.</param>
        /// <returns>True if the model system was saved successfully.</returns>
        public bool Save([NotNullWhen(false)] out CommandError? error, Stream saveTo)
        {
            lock (_sessionLock)
            {
                string? errorString = null;
                if (!ModelSystem.Save(ref errorString, saveTo))
                {
                    error = new CommandError(errorString ?? "No error message given when failing to save the model system!");
                    return false;
                }
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Remove the given link from the model system.
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="link">The link to remove.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        public bool RemoveLink(User user, Link link, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(link);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var boundary = link.Origin!.ContainedWithin!;
                if (boundary.RemoveLink(link, out error))
                {
                    Buffer.AddUndo(new Command(() =>
                    {
                        return (boundary.AddLink(link, out var e), e);
                    }, () =>
                    {
                        return (boundary.RemoveLink(link, out var e), e);
                    }));
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Add a new link originating at a hook and going to the destination node
        /// </summary>
        /// <param name="user"></param>
        /// <param name="origin"></param>
        /// <param name="originHook"></param>
        /// <param name="destination"></param>
        /// <param name="link"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        public bool AddLink(User user, Node origin, NodeHook originHook,
            Node destination, out Link? link, out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(origin);
            ArgumentNullException.ThrowIfNull(originHook);
            ArgumentNullException.ThrowIfNull(destination);
            link = null;

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                bool success = false;
                if (originHook.Cardinality == HookCardinality.Single
                    || originHook.Cardinality == HookCardinality.SingleOptional)
                {
                    if (origin.GetLink(originHook, out Link? _link))
                    {
                        if (_link is SingleLink sl)
                        {
                            var originalDestination = sl.Destination!;
                            success = sl.SetDestination(destination, out error);
                            if (success)
                            {
                                Buffer.AddUndo(new Command(() =>
                                {
                                    return (sl.SetDestination(originalDestination, out var e), e);
                                }, () =>
                                {
                                    return (sl.SetDestination(destination, out var e), e);
                                }
                                ));
                            }
                        }
                        else
                        {
                            throw new InvalidOperationException("A single cardinality link was not a SingleLink!");
                        }
                    }
                    else
                    {
                        success = origin.ContainedWithin!.AddLink(origin, originHook, destination, out link, out error);
                        if (success)
                        {
                            _link = link!;
                            Buffer.AddUndo(new Command(() =>
                            {
                                return (origin.ContainedWithin.RemoveLink(_link, out var e), e);
                            }, () =>
                            {
                                return (origin.ContainedWithin.AddLink(origin, originHook, destination, _link, out var e), e);
                            }));
                        }
                    }
                }
                else
                {
                    success = origin.ContainedWithin!.AddLink(origin, originHook, destination, out link, out error);
                    if (success)
                    {
                        Link _link = link!;
                        Buffer.AddUndo(new Command(() =>
                        {
                            return (origin.ContainedWithin.RemoveLink(_link, out var e), e);
                        }, () =>
                        {
                            return (origin.ContainedWithin.AddLink(origin, originHook, destination, _link, out var e), e);
                        }));
                    }
                }
                return success;
            }
        }

        /// <summary>
        /// Add a new function template to a boundary.
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="boundary">The boundary that the function template is being added to.</param>
        /// <param name="functionTemplateName">The name of the function template. This must be not null or whitespace.</param>
        /// <param name="functionTemplate">The newly created functionTemplate if successful, null otherwise.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        /// <exception cref="ArgumentNullException">Thrown if the user or boundary are null.</exception>
        public bool AddFunctionTemplate(User user, Boundary boundary, string functionTemplateName, out FunctionTemplate? functionTemplate, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(boundary);
            error = null;
            functionTemplate = null;

            if (string.IsNullOrWhiteSpace(functionTemplateName))
            {
                error = new CommandError($"You can not add a function template with an empty name!");
                return false;
            }
            lock(_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                if (ModelSystem.GlobalBoundary.ContainsFunctionTemplateName(functionTemplateName))
                {
                    error = new CommandError($"A function template named '{functionTemplateName}' already exists in the model system.");
                    return false;
                }
                if (!boundary.AddFunctionTemplate(functionTemplateName, out var template, out error))
                {
                    return false;
                }
                functionTemplate = template!;
                Buffer.AddUndo(new Command(() =>
                {
                    return (boundary.RemoveFunctionTemplate(template!, out var error), error);
                }, () =>
                {
                    return (boundary.AddFunctionTemplate(template!, out var error), error);
                }));
                return true;
            }
        }

        /// <summary>
        /// Remove the given function template from the boundary.
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="boundary">The boundary that the function template is being added to.</param>
        /// <param name="functionTemplate">The function template to remove from the given boundary.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        /// <exception cref="ArgumentNullException">Thrown if the user, boundary, or function template are null.</exception>
        public bool RemoveFunctionTemplate(User user, Boundary boundary, FunctionTemplate functionTemplate, [NotNullWhen(false)]  out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(boundary);
            ArgumentNullException.ThrowIfNull(functionTemplate);
            error = null;

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }

                // Refuse the removal if at least one FunctionInstance still references this template.
                var referencing = GetAllFunctionInstancesOf(functionTemplate);
                if (referencing.Count > 0)
                {
                    var names = string.Join(", ", referencing.Select(fi => $"'{fi.Name}'"));
                    error = new CommandError(
                        $"Cannot remove FunctionTemplate '{functionTemplate.Name}' because it is still " +
                        $"referenced by the following function instance(s): {names}. " +
                        $"Remove those instances first.");
                    return false;
                }

                if (!boundary.RemoveFunctionTemplate(functionTemplate, out error))
                {
                    error = new CommandError($"Failed to remove function template {functionTemplate.Name} from boundary {boundary.Name}: {error?.Message}");
                    return false;
                }
                Buffer.AddUndo(new Command(() =>
                {
                    return (boundary.AddFunctionTemplate(functionTemplate, out var error), error);
                }, () =>
                {
                    return (boundary.RemoveFunctionTemplate(functionTemplate, out var error), error);
                }));
                return true;
            }
        }

        /// <summary>
        /// Renames a function template within a boundary.
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="template">The function template to rename.</param>
        /// <param name="newName">The new name. Must not be null or whitespace.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        public bool RenameFunctionTemplate(User user, FunctionTemplate template, string newName,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(template);
            error = null;
            if (string.IsNullOrWhiteSpace(newName))
            {
                error = new CommandError("A function template name must not be empty.");
                return false;
            }
            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var oldName = template.Name;
                if (!string.Equals(oldName, newName, StringComparison.Ordinal)
                    && ModelSystem.GlobalBoundary.ContainsFunctionTemplateName(newName))
                {
                    error = new CommandError($"A function template named '{newName}' already exists in the model system.");
                    return false;
                }
                template.Name = newName;
                Buffer.AddUndo(new Command(() =>
                {
                    template.Name = oldName;
                    return (true, null);
                }, () =>
                {
                    template.Name = newName;
                    return (true, null);
                }));
                return true;
            }
        }

        /// <summary>
        /// Moves or resizes a function template's canvas container box.
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="template">The function template whose location is being updated.</param>
        /// <param name="newLocation">The new canvas location rectangle.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        public bool SetFunctionTemplateLocation(User user, FunctionTemplate template, Rectangle newLocation,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(template);
            error = null;
            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var oldLocation = template.Location;
                template.SetLocation(newLocation);
                Buffer.AddUndo(new Command(() =>
                {
                    template.SetLocation(oldLocation);
                    return (true, null);
                }, () =>
                {
                    template.SetLocation(newLocation);
                    return (true, null);
                }));
                return true;
            }
        }

        /// <summary>
        /// Designates <paramref name="entryNode"/> as the <see cref="FunctionTemplate.EntryNode"/>
        /// of <paramref name="template"/>, or clears it when <paramref name="entryNode"/> is <c>null</c>.
        /// <para>
        /// The entry node must be a <see cref="Start"/> that belongs to
        /// <paramref name="template"/>'s <see cref="FunctionTemplate.InternalModules"/>.
        /// It defines the runtime type of the template so that
        /// <see cref="FunctionInstance"/> objects can participate in hook-compatibility checks.
        /// </para>
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="template">The function template to modify.</param>
        /// <param name="entryNode">The start node to use as the entry point, or <c>null</c> to clear.</param>
        /// <param name="error">An error description when the method returns <c>false</c>.</param>
        /// <returns><c>true</c> on success; <c>false</c> with a populated <paramref name="error"/> on failure.</returns>
        public bool SetFunctionTemplateEntryNode(User user, FunctionTemplate template, Node? entryNode,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(template);
            error = null;
            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                // Validate: the entry node (when non-null) must live in InternalModules.
                if (entryNode != null && entryNode.ContainedWithin != template.InternalModules)
                {
                    error = new CommandError(
                        $"The node '{entryNode.Name}' does not belong to the InternalModules of template '{template.Name}'.");
                    return false;
                }
                var oldEntryNode = template.EntryNode;
                template.SetEntryNode(entryNode);
                Buffer.AddUndo(new Command(() =>
                {
                    template.SetEntryNode(oldEntryNode);
                    return (true, null);
                }, () =>
                {
                    template.SetEntryNode(entryNode);
                    return (true, null);
                }));
                return true;
            }
        }

        /// <summary>
        /// Toggles whether a node within a function template's InternalModules is exposed
        /// as an external hook on the template's canvas container.
        /// Adds the node when it is not yet exposed; removes it when it already is.
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="template">The function template that owns the node.</param>
        /// <param name="node">A node in <paramref name="template"/>'s InternalModules.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        public bool ToggleFunctionTemplateExposedNode(User user, FunctionTemplate template, Node node,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(template);
            ArgumentNullException.ThrowIfNull(node);
            error = null;
            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                bool wasExposed = template.ExposedNodes.Contains(node);
                if (!template.ToggleExposedNode(node, out error))
                    return false;
                Buffer.AddUndo(new Command(() =>
                {
                    if (wasExposed) template.AddExposedNode(node);
                    else            template.RemoveExposedNode(node);
                    return (true, null);
                }, () =>
                {
                    if (wasExposed) template.RemoveExposedNode(node);
                    else            template.AddExposedNode(node);
                    return (true, null);
                }));
                return true;
            }
        }

        // ── FunctionInstance ──────────────────────────────────────────────

        /// <summary>
        /// Places a new <see cref="FunctionInstance"/> of <paramref name="template"/> into
        /// <paramref name="boundary"/>.
        /// </summary>
        public bool AddFunctionInstance(User user, Boundary boundary, FunctionTemplate template,
            string name, Rectangle location,
            out FunctionInstance? instance, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(boundary);
            ArgumentNullException.ThrowIfNull(template);
            instance = null;
            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                if (!boundary.AddFunctionInstance(name, template, location, out instance, out error))
                    return false;
                var captured = instance!;
                Buffer.AddUndo(new Command(() =>
                {
                    return (boundary.RemoveFunctionInstance(captured, out var e), e);
                }, () =>
                {
                    return (boundary.AddFunctionInstance(captured, out var e), e);
                }));
                return true;
            }
        }

        /// <summary>
        /// Removes <paramref name="instance"/> from its containing boundary.
        /// </summary>
        public bool RemoveFunctionInstance(User user, FunctionInstance instance,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(instance);
            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var boundary = instance.ContainedWithin;
                if (!boundary.RemoveFunctionInstance(instance, out error))
                    return false;
                Buffer.AddUndo(new Command(() =>
                {
                    return (boundary.AddFunctionInstance(instance, out var e), e);
                }, () =>
                {
                    return (boundary.RemoveFunctionInstance(instance, out var e), e);
                }));
                return true;
            }
        }

        /// <summary>
        /// Renames <paramref name="instance"/>.
        /// </summary>
        public bool RenameFunctionInstance(User user, FunctionInstance instance, string newName,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(instance);
            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var oldName = instance.Name;
                if (!instance.SetName(newName, out error))
                    return false;
                Buffer.AddUndo(new Command(() =>
                {
                    instance.SetName(oldName, out _);
                    return (true, null);
                }, () =>
                {
                    instance.SetName(newName, out _);
                    return (true, null);
                }));
                return true;
            }
        }

        /// <summary>
        /// Moves / resizes a <see cref="FunctionInstance"/> on the canvas.
        /// </summary>
        public bool SetFunctionInstanceLocation(User user, FunctionInstance instance, Rectangle newLocation,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(instance);
            error = null;
            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var oldLocation = instance.Location;
                instance.SetLocation(newLocation);
                Buffer.AddUndo(new Command(() =>
                {
                    instance.SetLocation(oldLocation);
                    return (true, null);
                }, () =>
                {
                    instance.SetLocation(newLocation);
                    return (true, null);
                }));
                return true;
            }
        }

        /// <summary>
        /// Create a model system session to use for a run
        /// </summary>
        /// <param name="runtime">The XTMF runtime the run will occur in</param>
        /// <returns></returns>
        internal static ModelSystemSession CreateRunSession(ProjectSession session, ModelSystem modelSystem)        {
            return new ModelSystemSession(session, modelSystem);
        }

        /// <summary>
        /// Undo the previous command.
        /// </summary>
        /// <param name="user">The user requesting the undo.</param>
        /// <param name="error">An error message if the undo fails.</param>
        /// <returns>True if the undo succeeds, false otherwise with an error message.</returns>
        public bool Undo(User user, out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                return Buffer.UndoCommands(out error);
            }
        }

        /// <summary>
        /// Redo the previously undon command.
        /// </summary>
        /// <param name="user">The user requesting the redo.</param>
        /// <param name="error">An error message if the redo fails.</param>
        /// <returns>True if the redo succeeds, false otherwise with an error message.</returns>
        public bool Redo(User user, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);

            if (!_session.HasAccess(user))
            {
                error = new CommandError("The user does not have access to this project.", true);
                return false;
            }
            return Buffer.RedoCommands(out error);
        }

        /// <summary>True when there is at least one command that can be undone.</summary>
        public bool CanUndo => Buffer.CanUndo;

        /// <summary>True when there is at least one command that can be redone.</summary>
        public bool CanRedo => Buffer.CanRedo;

        /// <summary>
        /// Remove a single destination of a MultiLink
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="multiLink">The multi link to operate on</param>
        /// <param name="index">The index to remove</param>
        /// <param name="error">The error message if the operation fails.</param>
        /// <returns>True if successful, false otherwise with error message.</returns>
        /// <summary>
        /// Moves a destination within a multi-link from one index to another.
        /// </summary>
        public bool MoveLinkDestination(User user, Link multiLink, int fromIndex, int toIndex, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(multiLink);
            error = null;

            if (multiLink is not MultiLink ml)
            {
                error = new CommandError("The link is not a multi-link!");
                return false;
            }

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var count = ml.Destinations.Count;
                if (fromIndex < 0 || fromIndex >= count || toIndex < 0 || toIndex >= count)
                {
                    error = new CommandError("Index is out of bounds!");
                    return false;
                }
                ml.MoveDestination(fromIndex, toIndex);
                int _from = fromIndex, _to = toIndex;
                Buffer.AddUndo(new Command(
                    () => { ml.MoveDestination(_to, _from); return (true, null); },
                    () => { ml.MoveDestination(_from, _to); return (true, null); }));
                return true;
            }
        }

        public bool RemoveLinkDestination(User user, Link multiLink, int index, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(multiLink);
            error = null;

            if (multiLink is MultiLink ml)
            {
                lock (_sessionLock)
                {
                    if (!_session.HasAccess(user))
                    {
                        error = new CommandError("The user does not have access to this project.", true);
                        return false;
                    }
                    var dests = ml.Destinations;
                    if (index >= dests.Count || index < 0)
                    {
                        error = new CommandError("The index is out of bounds!");
                        return false;
                    }
                    var toRemove = dests[index];
                    ml.RemoveDestination(index);
                    Buffer.AddUndo(new Command(() =>
                    {
                        return (ml.AddDestination(toRemove, index, out var e), e);
                    }, () =>
                    {
                        ml.RemoveDestination(index);
                        return (true, null);
                    }));
                    return true;
                }
            }
            else
            {
                error = new CommandError("The link was not a multi-link!");
                return false;
            }
        }

        /// <summary>
        /// Export the model system to a file at the given path.
        /// </summary>
        /// <param name="user">The user performing the export.</param>
        /// <param name="exportPath">The path to export the model system to.</param>
        /// <param name="error">The error message if the export fails.</param>
        /// <returns>True if the export was successful, false otherwise with error message.</returns>
        public bool ExportModelSystem(User user, string exportPath, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(exportPath);
            error = null;

            lock (_sessionLock)
            {
                return ModelSystemFile.ExportModelSystemFromSession(user, this, exportPath, out error);
            }
        }
    }
}
