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
using System.Text;
using System.Threading;
using XTMF2.ModelSystemConstruct;

namespace XTMF2.Editing
{
    public sealed class ModelSystemSession : IDisposable
    {
        private int _References = 1;

        public int References => _References;

        public ModelSystem ModelSystem { get; private set; }

        private ProjectSession Session;

        private object SessionLock = new object();

        public ModelSystemSession(ProjectSession session, ModelSystem modelSystem)
        {
            ModelSystem = modelSystem;
            Session = session.AddReference();
        }

        public void Dispose()
        {
            if (Interlocked.Decrement(ref _References) <= 0)
            {
                Session.UnloadSession(this);
            }
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
                var ms = ModelSystem;
                if (!ms.Contains(boundary))
                {
                    error = "The passed in boundary is not part of the model system!";
                    return false;
                }
                return boundary.AddStart(startName, out start, ref error);
            }
            
        }
    }
}
