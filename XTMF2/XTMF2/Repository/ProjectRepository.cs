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
using System.Text;
using System.Linq;

namespace XTMF2.Repository
{
    public sealed class ProjectRepository : Repository<Project>
    {
        internal List<Project> GetAvailableForUser(User user)
        {
            lock (StoreLock)
            {
                return (from project in _Store
                        where project.CanAccess(user)
                        select project).ToList();
            }
        }

        /// <summary>
        /// Create a new project and add it to the repository
        /// </summary>
        /// <param name="name">The name of the project</param>
        /// <param name="owner">The owner of the project</param>
        /// <param name="error">A message containing a description of the error</param>
        /// <param name="ret">The returned project</param>
        /// <returns>The created project, null if there is an error.</returns>
        internal bool CreateNew(string path, string name, User owner, out Project ret, ref string error)
        {
            if (!Project.New(owner, name, null, out ret, ref error))
            {
                return false;
            }
            return Add(ret, ref error);
        }
    }
}
