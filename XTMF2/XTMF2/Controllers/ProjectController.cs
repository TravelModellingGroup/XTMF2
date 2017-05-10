﻿/*
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
using XTMF2.Editing;

namespace XTMF2.Controller
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
            LoadProjects(runtime);
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
                owner.AddedUserToProject(p);
                session = GetSession(p);
                return true;
            }
        }

        /// <summary>
        /// Load all project headers.
        /// Should only be called by system configuration.
        /// </summary>
        /// <returns>List of projects that failed to load an reason why</returns>
        private List<(string Path, string Error)> LoadProjects(XTMFRuntime runtime)
        {
            var errors = new List<(string Path, string Error)>();
            var allUsers = runtime.UserController.Users;
            // go through all users and scan their directory for projects
            foreach(var user in allUsers)
            {
                string dir = user.UserPath;
                DirectoryInfo userDir = new DirectoryInfo(dir);
                foreach(var subDir in userDir.GetDirectories())
                {
                    var projectFile = subDir.GetFiles().FirstOrDefault(f => f.Name == "Project.xpjt");
                    if(projectFile != null)
                    {
                        string error = null;
                        if(Project.Load(projectFile.FullName, out Project project, ref error))
                        {
                            Projects.Add(project, ref error);
                        }
                        else
                        {
                            errors.Add((projectFile.FullName, error));
                        }
                    }
                }
            }
            return errors;
        }

        public bool DeleteProject(User user, string projectName, ref string error)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            if (projectName == null)
            {
                throw new ArgumentNullException(nameof(projectName));
            }
            var project = GetProjects(user).FirstOrDefault(p => p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase));
            if(project == null)
            {
                error = $"User does not have a project called {projectName}!";
                return false;
            }
            return DeleteProject(user, project, ref error);
        }

        public bool DeleteProject(User owner, Project project, ref string error)
        {
            if(owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }
            if(project == null)
            {
                throw new ArgumentNullException(nameof(project));
            }
            lock (ControllerLock)
            {
                owner.RemovedUserForProject(project);
                foreach(var user in project.AdditionalUsers)
                {
                    user.RemovedUserForProject(project);
                }
                ActiveSessions.Remove(project);
                Projects.Remove(project);
                var directory = new DirectoryInfo(project.ProjectDirectory);
                if (directory.Exists)
                {
                    directory.Delete(true);
                    return true;
                }
                return false;
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