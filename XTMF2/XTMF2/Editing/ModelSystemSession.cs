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
                if(success)
                {
                    Start _start = start;
                    Buffer.AddUndo(new Command(() =>
                    {
                        string e = null;
                        return (boundary.RemoveStart(_start, ref e), e);
                    }, () =>
                    {
                        string e = null;
                        return (boundary.AddStart(this, startName, out _start, ref e), e);
                    }));
                }
                return success;
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
                        return (boundary.AddModelSystemStructure(this, name, type, out _mss, ref e), e);
                    }));
                }
                return success;
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
            if(user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            if(originHook == null)
            {
                throw new ArgumentNullException(nameof(originHook));
            }
            lock(SessionLock)
            {
                var success = origin.ContainedWithin.AddLink(origin, originHook, destination, out link, ref error);
                if(success)
                {
                    Link _link = link;
                    Buffer.AddUndo(new Command(() =>
                    {
                        string e = null;
                        return (origin.ContainedWithin.RemoveLink(_link, ref e), e);
                    }, () =>
                    {
                        string e = null;
                        return (origin.ContainedWithin.AddLink(origin, originHook, destination, out _link, ref e), e);
                    }));
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
    }
}
