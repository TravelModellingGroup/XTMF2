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

        private ProjectSession Session;

        private object SessionLock = new object();

        public ModelSystemHeader ModelSystemHeader { get; private set; }

        private CommandBuffer Buffer = new CommandBuffer();

        public ModelSystemSession(ProjectSession session, ModelSystemHeader header)
        {
            ModelSystemHeader = header;
            Session = session.AddReference();
        }

        public void Dispose()
        {
            if (Interlocked.Decrement(ref _References) <= 0)
            {
                Session.UnloadSession(this);
            }
        }

        internal ModuleRepository GetModuleRepository()
        {
            return Session.GetModuleRepository();
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
            lock (SessionLock)
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

        public bool RemoveBoundary(User user, Boundary parentBoundary, Boundary boundary, ref string error)
        {
            if(user == null)
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
            lock (SessionLock)
            {
                if(parentBoundary.RemoveBoundary(boundary, ref error))
                {
                    Buffer.AddUndo(new Command(() =>
                    {
                        string e = null;
                        return (parentBoundary.AddBoundary(boundary, ref e), e);
                    }, () =>
                    {
                        string e = null;
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
        /// <param name="user">The user requesting to add the new structure.</param>
        /// <param name="startName">The name of the start element.  This must be unique in the model system.</param>
        /// <param name="start">The newly created start structure</param>
        /// <param name="error">A message describing why the structure was rejected.</param>
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
            lock (SessionLock)
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
            lock (SessionLock)
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

        public bool AddModelSystemStructure(User user, Boundary boundary, string name, Type type, out ModelSystemStructure mss, ref string error)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            if (boundary == null)
            {
                throw new ArgumentNullException(nameof(boundary));
            }
            lock (SessionLock)
            {
                bool success = boundary.AddModelSystemStructure(this, name, type, out mss, ref error);
                if (success)
                {
                    ModelSystemStructure _mss = mss;
                    Buffer.AddUndo(new Command(() =>
                    {
                        string e = null;
                        return (boundary.RemoveModelSystemStructure(_mss, ref e), e);
                    }, () =>
                    {
                        string e = null;
                        return (boundary.AddModelSystemStructure(this, name, type, _mss, ref e), e);
                    }));
                }
                return success;
            }
        }

        public bool RemoveModelSystemStructure(User user, ModelSystemStructure mss, ref string error)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            if (mss == null)
            {
                throw new ArgumentNullException(nameof(mss));
            }
            lock (SessionLock)
            {
                var boundary = mss.ContainedWithin;
                if (boundary.RemoveModelSystemStructure(mss, ref error))
                {
                    Buffer.AddUndo(new Command(() =>
                    {
                        string e = null;
                        return (boundary.AddModelSystemStructure(mss, ref e), e);
                    }, () =>
                    {
                        string e = null;
                        return (boundary.RemoveModelSystemStructure(mss, ref e), e);
                    }));
                    return true;
                }
                return false;
            }
        }

        public bool SetParameterValue(User user, ModelSystemStructure basicParameter, string value, ref string error)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            if (basicParameter == null)
            {
                throw new ArgumentNullException(nameof(basicParameter));
            }
            lock (SessionLock)
            {
                return basicParameter.SetParameterValue(this, value, ref error);
            }
        }

        /// <summary>
        /// Save the model system
        /// </summary>
        /// <param name="error">An error message in case the save fails.</param>
        /// <returns>True if it succeeds, false with an error message otherwise.</returns>
        public bool Save(ref string error)
        {
            lock (SessionLock)
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
            lock (SessionLock)
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
            lock (SessionLock)
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
        /// Add a new link originating at a hook and going to the destination model system structure
        /// </summary>
        /// <param name="user"></param>
        /// <param name="origin"></param>
        /// <param name="originHook"></param>
        /// <param name="destination"></param>
        /// <param name="link"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        public bool AddLink(User user, ModelSystemStructure origin, ModelSystemStructureHook originHook,
            ModelSystemStructure destination, out Link link, ref string error)
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
            lock (SessionLock)
            {
                bool success = false;
                if (originHook.Cardinality == HookCardinality.Single)
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
    }
}
