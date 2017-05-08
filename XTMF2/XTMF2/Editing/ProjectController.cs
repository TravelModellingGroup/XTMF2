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
using XTMF2.Repository;
using System.Linq;

namespace XTMF2.Editing
{
    public sealed class ProjectController
    {
        /// <summary>
        /// The collection of projects that are available
        /// </summary>
        private ProjectRepository Projects = new ProjectRepository();

        private XTMFRuntime Runtime;

        private object ControllerLock = new object();

        public ProjectController(XTMFRuntime runtime)
        {
            Runtime = runtime;
        }

        public ReadOnlyObservableCollection<Project> GetProjects(User user)
        {
            return user.AvailableProjects;
        }

        public bool CreateNewProject(User owner, string name, out ProjectSession session, ref string error)
        {
            session = null;
            if (!ValidateProjectName(name))
            {
                error = "Invalid project name!";
                return false;
            }
            var path = GetPath(owner, name);
            lock (ControllerLock)
            {
                if(owner.HasProjectWithName(name))
                {
                    error = "A project with that name already exists!";
                    return false;
                }
                if (!Projects.CreateNew(path, name, owner, out Project p, ref error))
                {
                    return false;
                }
                session = GetSession(p);
                return true;
            }
        }

        /// <summary>
        /// This should only be invoked by the project session
        /// </summary>
        /// <param name="projectSession"></param>
        internal void UnloadSession(ProjectSession projectSession)
        {
            ActiveSessions.Remove(projectSession.Project);
        }

        private string GetPath(User owner, string name)
        {
            return Path.Combine(owner.UserPath, name);
        }

        public ProjectSession GetSession(Project project)
        {
            lock(ControllerLock)
            {
                if(!ActiveSessions.TryGetValue(project, out var session))
                {
                    session = new ProjectSession(Runtime, project);
                }
                session.IncrementCounter();
                return session;
            }
        }

        private Dictionary<Project, ProjectSession> ActiveSessions = new Dictionary<Project, ProjectSession>();

        private static char[] InvalidCharacters =
            Path.GetInvalidPathChars().Union(Path.GetInvalidFileNameChars()).ToArray();

        /// <summary>
        /// Ensure that a project name does not contain
        /// invalid characters
        /// </summary>
        /// <param name="name">The name to validate</param>
        /// <returns>If the validation allows this project name.</returns>
        private static bool ValidateProjectName(string name)
        {
            return !name.Any(c => InvalidCharacters.Contains(c));
        }
    }
}
