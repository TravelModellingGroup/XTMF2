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
    /// <summary>
    /// Provides access to the projects contained within the instance of
    /// XTMF.
    /// </summary>
    public sealed class ProjectRepository : Repository<Project>
    {
        /// <summary>
        /// Get a list of projects that the user is allowed to access.
        /// </summary>
        /// <param name="user">The user to get access for.</param>
        /// <returns>A list of accessable projects.</returns>
        internal List<Project> GetAvailableForUser(User user)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

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
        /// <returns>True if successful, false otherwise with an error message.</returns>
        internal bool CreateNew(string path, string name, User owner, out Project ret, ref string error)
        {
            if (!Project.New(owner, name, null, out ret, ref error))
            {
                return false;
            }
            return Add(ret, ref error);
        }

        /// <summary>
        /// Get the project for the given user.
        /// </summary>
        /// <param name="user">The user trying to access the project.</param>
        /// <param name="projectName">The name of the project.</param>
        /// <param name="project">The resulting project.</param>
        /// <param name="error">An error message if the undo fails.</param>
        /// <returns>True if successful, false otherwise with an error message.</returns>
        internal bool GetProject(User user, string projectName, out Project project, ref string error)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            project = null;
            var possibleProjects = _Store.Where(p => p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase)).OrderBy(p => p.Owner == user);
            if(!possibleProjects.Any())
            {
                error = $"Unable to find a project with the name {projectName}";

                return false;
            }
            foreach(var p in possibleProjects)
            {
                if(p.CanAccess(user))
                {
                    project = p;
                    return true;
                }
            }
            error = "Unable to access project";
            return project != null;
        }
    }
}
