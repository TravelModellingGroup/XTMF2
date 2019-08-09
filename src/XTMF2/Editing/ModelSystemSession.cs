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
using System.IO;
using System.Text;
using System.Threading;
using XTMF2.ModelSystemConstruct;
using XTMF2.Repository;

namespace XTMF2.Editing
{
    public sealed class ModelSystemSession : IDisposable
    {
        private int _References = 0;

        public int References => _References;

        public ModelSystem ModelSystem { get; internal set; }

        private readonly ProjectSession _session;

        private readonly object _sessionLock = new object();

        public ModelSystemHeader ModelSystemHeader { get; private set; }

        private readonly CommandBuffer Buffer = new CommandBuffer();

        public ModelSystemSession(ProjectSession session, ModelSystemHeader header)
        {
            ModelSystemHeader = header;
            _session = session.AddReference();
        }

        public void Dispose()
        {
            if (Interlocked.Decrement(ref _References) <= 0)
            {
                _session.UnloadSession(this);
            }
        }

        internal ModuleRepository GetModuleRepository()
        {
            return _session.GetModuleRepository();
        }

        /// <summary>
        /// Set the name of a given boundary.
        /// </summary>
        /// <param name="user">The user issuing the action.</param>
        /// <param name="boundary">The boundary to change.</param>
        /// <param name="name">The new name to assign to the boundary, must be unique.</param>
        /// <param name="error">An error message if the operation fails with the reason why.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message stored in error.</returns>
        public bool SetBoundaryName(User user, Boundary boundary, string name, ref string error)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            if (boundary == null)
            {
                throw new ArgumentNullException(nameof(boundary));
            }
            if (String.IsNullOrWhiteSpace(name))
            {
                error = "A boundary requires a unique name";
                return false;
            }
            lock (_sessionLock)
            {
                var oldName = boundary.Name;
                if (boundary.SetName(name, ref error))
                {
                    Buffer.AddUndo(new Command(() =>
                   {
                       string e = null;
                       return (boundary.SetName(oldName, ref e), e);
                   }, () =>
                   {
                       string e = null;
                       return (boundary.SetName(name, ref e), e);
                   }));
                    return true;
                }
                return false;
            }
        }

        public bool SetBoundaryDescription(User user, Boundary boundary, string description, ref string error)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            if (boundary == null)
            {
                throw new ArgumentNullException(nameof(boundary));
            }
            lock (_sessionLock)
            {
                var oldDescription = boundary.Description;
                if (boundary.SetDescription(description, ref error))
                {
                    Buffer.AddUndo(new Command(() =>
                    {
                        string e = null;
                        return (boundary.SetDescription(oldDescription, ref e), e);
                    }, () =>
                    {
                        string e = null;
                        return (boundary.SetDescription(description, ref e), e);
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
        public bool AddBoundary(User user, Boundary parentBoundary, string name, out Boundary boundary, ref string error)
        {
            boundary = null;
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            if (parentBoundary == null)
            {
                throw new ArgumentNullException(nameof(parentBoundary));
            }
            if (String.IsNullOrWhiteSpace(name))
            {
                error = "A boundary requires a unique name";
                return false;
            }
            lock (_sessionLock)
            {
                if (parentBoundary.AddBoundary(name, out boundary, ref error))
                {
                    var _b = boundary;
                    Buffer.AddUndo(new Command(() =>
                    {
                        string e = null;
                        return (parentBoundary.RemoveBoundary(_b, ref e), e);
                    }, () =>
                    {
                        string e = null;
                        return (parentBoundary.AddBoundary(_b, ref e), e);
                    }));
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="localUser"></param>
        /// <param name="location"></param>
        /// <param name="block"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        public bool AddCommentBlock(User user, Boundary boundary, string comment, Point location, out CommentBlock block, ref string error)
        {
            block = null;
            if (user is null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            if (boundary is null)
            {
                throw new ArgumentNullException(nameof(boundary));
            }
            if (String.IsNullOrWhiteSpace(comment))
            {
                error = "There was no comment to store.";
                return false;
            }
            lock (_sessionLock)
            {
                if(boundary.AddCommentBlock(comment, location, out block, ref error))
                {
                    var _block = block;
                    Buffer.AddUndo(new Command(()=>
                    {
                        string e = null;
                        return (boundary.RemoveCommentBlock(_block, ref e), e);
                    }, ()=>
                    {
                        string e = null;
                        return (boundary.AddCommentBlock(_block, ref e), e);
                    }));
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Remove the comment block from the given boundary.
        /// </summary>
        /// <param name="user">The user requesting the action.</param>
        /// <param name="boundary">The containing boundary</param>
        /// <param name="block">The comment block to remove.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false with an error message otherwise./returns>
        public bool RemoveCommentBlock(User user, Boundary boundary, CommentBlock block, ref string error)
        {
            if (user is null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            if (boundary is null)
            {
                throw new ArgumentNullException(nameof(boundary));
            }

            if (block is null)
            {
                throw new ArgumentNullException(nameof(block));
            }
            lock(_sessionLock)
            {
                if(boundary.RemoveCommentBlock(block, ref error))
                {
                    Buffer.AddUndo(new Command(() =>
                    {
                        string e = null;
                        return (boundary.AddCommentBlock(block, ref e), e);
                    }, () =>
                    {
                        string e = null;
                        return (boundary.RemoveCommentBlock(block, ref e), e);
                    }));
                    return true;
                }
                return false;
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
        public bool RemoveBoundary(User user, Boundary parentBoundary, Boundary boundary, ref string error)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            if (parentBoundary == null)
            {
                throw new ArgumentNullException(nameof(parentBoundary));
            }
            if (boundary == null)
            {
                throw new ArgumentNullException(nameof(boundary));
            }
            lock (_sessionLock)
            {
                var linksGoingToRemovedBoundary = ModelSystem.GlobalBoundary.GetLinksGoingToBoundary(boundary);
                if (parentBoundary.RemoveBoundary(boundary, ref error))
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
                    bool RemoveLinks(ref string error2)
                    {
                        foreach (var link in linksGoingToRemovedBoundary)
                        {
                            if (link is SingleLink sl)
                            {
                                if (!link.Origin.ContainedWithin.RemoveLink(link, ref error2))
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
                        return true;
                    }
                    if (!RemoveLinks(ref error))
                    {
                        return false;
                    }
                    Buffer.AddUndo(new Command(() =>
                    {
                        string e = null;
                        if (parentBoundary.AddBoundary(boundary, ref e))
                        {
                            foreach (var link in linksGoingToRemovedBoundary)
                            {
                                if (link is SingleLink sl)
                                {
                                    link.Origin.ContainedWithin.AddLink(link, ref e);
                                }
                                else if (link is MultiLink ml)
                                {
                                    var list = multiLinkHelper[ml];
                                    foreach (var (Index, MSS) in list)
                                    {
                                        ml.AddDestination(MSS, Index);
                                    }
                                }
                            }
                            return (true, null);
                        }
                        return (false, e);
                    }, () =>
                    {
                        string e = null;
                        if (!RemoveLinks(ref e))
                        {
                            return (false, e);
                        }
                        return (parentBoundary.RemoveBoundary(boundary, ref e), e);
                    }));
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Adds a new model system start element.
        /// </summary>
        /// <param name="user">The user requesting to add the new start node.</param>
        /// <param name="startName">The name of the start element.  This must be unique in the model system.</param>
        /// <param name="start">The newly created start node</param>
        /// <param name="error">A message describing why the start node was rejected.</param>
        /// <returns>True if the operation succeeds, false otherwise.</returns>
        public bool AddModelSystemStart(User user, Boundary boundary, string startName, out Start start, ref string error)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            if (boundary == null)
            {
                throw new ArgumentNullException(nameof(boundary));
            }
            const string badStartName = "The start name must be unique within the model system and not empty.";
            start = null;
            if (String.IsNullOrWhiteSpace(startName))
            {
                error = badStartName;
                return false;
            }
            lock (_sessionLock)
            {
                if (!ModelSystem.Contains(boundary))
                {
                    error = "The passed in boundary is not part of the model system!";
                    return false;
                }
                var success = boundary.AddStart(this, startName, out start, ref error);
                if (success)
                {
                    Start _start = start;
                    Buffer.AddUndo(new Command(() =>
                    {
                        string e = null;
                        return (boundary.RemoveStart(_start, ref e), e);
                    }, () =>
                    {
                        string e = null;
                        return (boundary.AddStart(this, startName, _start, ref e), e);
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
        public bool RemoveStart(User user, Start start, ref string error)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            if (start == null)
            {
                throw new ArgumentNullException(nameof(start));
            }
            lock (_sessionLock)
            {
                var boundary = start.ContainedWithin;
                if (boundary.RemoveStart(start, ref error))
                {
                    Buffer.AddUndo(new Command(() =>
                    {
                        string e = null;
                        return (boundary.AddStart(start, ref e), e);
                    }, () =>
                    {
                        string e = null;
                        return (boundary.RemoveStart(start, ref e), e);
                    }));
                    return true;
                }
                return false;
            }
        }

        public bool AddNode(User user, Boundary boundary, string name, Type type, out Node mss, ref string error)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            if (boundary == null)
            {
                throw new ArgumentNullException(nameof(boundary));
            }
            lock (_sessionLock)
            {
                bool success = boundary.AddNode(this, name, type, out mss, ref error);
                if (success)
                {
                    Node _mss = mss;
                    Buffer.AddUndo(new Command(() =>
                    {
                        string e = null;
                        return (boundary.RemoveNode(_mss, ref e), e);
                    }, () =>
                    {
                        string e = null;
                        return (boundary.AddNode(this, name, type, _mss, ref e), e);
                    }));
                }
                return success;
            }
        }

        public bool RemoveNode(User user, Node node, ref string error)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            lock (_sessionLock)
            {
                var boundary = node.ContainedWithin;
                if (boundary.RemoveNode(node, ref error))
                {
                    Buffer.AddUndo(new Command(() =>
                    {
                        string e = null;
                        return (boundary.AddNode(node, ref e), e);
                    }, () =>
                    {
                        string e = null;
                        return (boundary.RemoveNode(node, ref e), e);
                    }));
                    return true;
                }
                return false;
            }
        }

        public bool SetParameterValue(User user, Node basicParameter, string value, ref string error)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            if (basicParameter == null)
            {
                throw new ArgumentNullException(nameof(basicParameter));
            }
            lock (_sessionLock)
            {
                var previousValue = basicParameter.ParameterValue;
                if (basicParameter.SetParameterValue(this, value, ref error))
                {
                    Buffer.AddUndo(new Command(() =>
                    {
                        string e = null;
                        return (basicParameter.SetParameterValue(this, previousValue, ref e), e);
                    }, () =>
                    {
                        string e = null;
                        return (basicParameter.SetParameterValue(this, value, ref e), e);
                    }));
                    return true;
                }
                return false;
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
        public bool SetNodeDisabled(User user, Node node, bool disabled, ref string error)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            lock (_sessionLock)
            {
                if (node.SetDisabled(this, disabled))
                {
                    Buffer.AddUndo(new Command(() =>
                    {
                        return (node.SetDisabled(this, !disabled), String.Empty);
                    }, () =>
                    {
                        return (node.SetDisabled(this, disabled), String.Empty);
                    }));
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="user"></param>
        /// <param name="link"></param>
        /// <param name="disabled"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        public bool SetLinkDisabled(User user, Link link, bool disabled, ref string error)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            if (link == null)
            {
                throw new ArgumentNullException(nameof(link));
            }
            lock (_sessionLock)
            {
                if (link.SetDisabled(this, disabled))
                {
                    Buffer.AddUndo(new Command(() =>
                    {
                        return (link.SetDisabled(this, !disabled), String.Empty);
                    }, () =>
                    {
                        return (link.SetDisabled(this, disabled), String.Empty);
                    }));
                    return true;
                }
                return true;
            }
        }


        /// <summary>
        /// Save the model system
        /// </summary>
        /// <param name="error">An error message in case the save fails.</param>
        /// <returns>True if it succeeds, false with an error message otherwise.</returns>
        public bool Save(ref string error)
        {
            lock (_sessionLock)
            {
                return ModelSystem.Save(ref error);
            }
        }

        /// <summary>
        /// Save the model system to the given stream
        /// </summary>
        /// <param name="error">An error message if something goes wrong saving the model system.</param>
        /// <param name="saveTo">The stream to save the model system to.</param>
        /// <returns>True if the model system was saved successfully.</returns>
        public bool Save(ref string error, Stream saveTo)
        {
            lock (_sessionLock)
            {
                return ModelSystem.Save(ref error, saveTo);
            }
        }

        public bool RemoveLink(User user, Link link, ref string error)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            if (link == null)
            {
                throw new ArgumentNullException(nameof(link));
            }
            lock (_sessionLock)
            {
                var boundary = link.Origin.ContainedWithin;
                if (boundary.RemoveLink(link, ref error))
                {
                    Buffer.AddUndo(new Command(() =>
                   {
                       string e = null;
                       return (boundary.AddLink(link, ref e), e);
                   }, () =>
                   {
                       string e = null;
                       return (boundary.RemoveLink(link, ref e), e);
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
            Node destination, out Link link, ref string error)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            if (originHook == null)
            {
                throw new ArgumentNullException(nameof(originHook));
            }
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            link = null;
            lock (_sessionLock)
            {
                bool success = false;
                if (originHook.Cardinality == HookCardinality.Single
                    || originHook.Cardinality == HookCardinality.SingleOptional)
                {
                    if (origin.GetLink(originHook, out Link _link))
                    {
                        if (_link is SingleLink sl)
                        {
                            var originalDestination = sl.Destination;
                            success = sl.SetDestination(this, destination, ref error);
                            if (success)
                            {
                                Buffer.AddUndo(new Command(() =>
                                {
                                    string e = null;
                                    return (sl.SetDestination(this, originalDestination, ref e), e);
                                }, () =>
                                {
                                    string e = null;
                                    return (sl.SetDestination(this, destination, ref e), e);
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
                        success = origin.ContainedWithin.AddLink(origin, originHook, destination, out link, ref error);
                        if (success)
                        {
                            _link = link;
                            Buffer.AddUndo(new Command(() =>
                            {
                                string e = null;
                                return (origin.ContainedWithin.RemoveLink(_link, ref e), e);
                            }, () =>
                            {
                                string e = null;
                                return (origin.ContainedWithin.AddLink(origin, originHook, destination, _link, ref e), e);
                            }));
                        }
                    }
                }
                else
                {
                    success = origin.ContainedWithin.AddLink(origin, originHook, destination, out link, ref error);
                    if (success)
                    {
                        Link _link = link;
                        Buffer.AddUndo(new Command(() =>
                        {
                            string e = null;
                            return (origin.ContainedWithin.RemoveLink(_link, ref e), e);
                        }, () =>
                        {
                            string e = null;
                            return (origin.ContainedWithin.AddLink(origin, originHook, destination, _link, ref e), e);
                        }));
                    }
                }
                return success;
            }
        }

        /// <summary>
        /// Create a model system session to use for a run
        /// </summary>
        /// <param name="runtime">The XTMF runtime the run will occur in</param>
        /// <returns></returns>
        internal static ModelSystemSession CreateRunSession(ProjectSession session, ModelSystemHeader header)
        {
            return new ModelSystemSession(session, header);
        }

        public bool Undo(ref string error)
        {
            return Buffer.UndoCommands(ref error);
        }

        public bool Redo(ref string error)
        {
            return Buffer.RedoCommands(ref error);
        }

        /// <summary>
        /// Remove a single destination of a MultiLink
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="multiLink">The multi link to operate on</param>
        /// <param name="index">The index to remove</param>
        /// <param name="error">The error message if the operation fails.</param>
        /// <returns>True if successful, false otherwise with error message.</returns>
        public bool RemoveLinkDestination(User user, Link multiLink, int index, ref string error)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            if (multiLink == null)
            {
                throw new ArgumentNullException(nameof(multiLink));
            }
            if (multiLink is MultiLink ml)
            {
                lock (_sessionLock)
                {
                    var dests = ml.Destinations;
                    if (index >= dests.Count || index < 0)
                    {
                        error = "The index is out of bounds!";
                        return false;
                    }
                    var toRemove = dests[index];
                    ml.RemoveDestination(index);
                    Buffer.AddUndo(new Command(() =>
                    {
                        return (ml.AddDestination(toRemove, index), null);
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
                error = "The link was not a multi-link!";
                return false;
            }
        }
    }
}
