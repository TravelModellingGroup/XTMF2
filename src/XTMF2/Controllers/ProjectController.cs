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
using XTMF2.Editing;

namespace XTMF2.Controllers
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
            if (!ValidateProjectName(name, ref error))
            {
                return false;
            }
            var path = GetPath(owner, name);
            lock (ControllerLock)
            {
                if (owner.OwnsProjectWithName(name))
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
        /// Import a project file.
        /// </summary>
        /// <param name="owner">The user that will own this project.</param>
        /// <param name="name">The name to give this project. This must be unique for the given user.</param>
        /// <param name="filePath">The path to the project file.</param>
        /// <param name="session">An editing session for the imported project.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation completes successfully, false otherwise with an error message.</returns>
        public bool ImportProjectFile(User owner, string name, string filePath, out ProjectSession session, ref string error)
        {
            session = null;
            if (owner is null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                error = "A project must have a non-blank unique name.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(filePath))
            {
                error = "The path to the project file must be not null or whitespace";
                return false;
            }
            if (!ValidateProjectName(name, ref error))
            {
                return false;
            }
            lock (ControllerLock)
            {
                if(owner.OwnsProjectWithName(name))
                {
                    error = "The user already has project with that name!";
                    return false;
                }
                if(!ProjectFile.ImportProject(owner, name, filePath, out Project project, ref error))
                {
                    return false;
                }
                if(!Projects.Add(project, ref error))
                {
                    project.Delete(ref error);
                    return false;
                }
                owner.AddedUserToProject(project);
                session = GetSession(project);
                return true;
            }
        }

        public bool GetProjectSession(User user, Project project, out ProjectSession session, ref string error)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            if (project == null)
            {
                throw new ArgumentNullException(nameof(project));
            }
            lock(ControllerLock)
            {
                if(!project.CanAccess(user))
                {
                    error = "The user does not have access to this project!";
                    session = null;
                    return false;
                }
                session = GetSession(project);
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
            foreach (var user in allUsers)
            {
                string dir = user.UserPath;
                DirectoryInfo userDir = new DirectoryInfo(dir);
                foreach (var subDir in userDir.GetDirectories())
                {
                    var projectFile = subDir.GetFiles().FirstOrDefault(f => f.Name == "Project.xpjt");
                    if (projectFile != null)
                    {
                        string error = null;
                        if (Project.Load(runtime.UserController, projectFile.FullName, out Project project, ref error))
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

        public bool GetProject(string userName, string projectName, out Project project, ref string error)
        {
            if (String.IsNullOrWhiteSpace(userName))
            {
                throw new ArgumentNullException(nameof(userName));
            }
            if (String.IsNullOrWhiteSpace(projectName))
            {
                throw new ArgumentNullException(nameof(projectName));
            }
            var user = Runtime.UserController.GetUserByName(userName);
            if(user == null)
            {
                project = null;
                error = $"Unable to find a user with the name {userName}!";
                return false;
            }
            return GetProject(user, projectName, out project, ref error);
        }

        public bool GetProject(User user, string projectName, out Project project, ref string error)
        {
            if(user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            if(String.IsNullOrWhiteSpace(projectName))
            {
                throw new ArgumentNullException(nameof(projectName));
            }
            lock (ControllerLock)
            {
                return Projects.GetProject(user, projectName, out project, ref error);
            }
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
            if (project == null)
            {
                error = $"User does not have a project called {projectName}!";
                return false;
            }
            return DeleteProject(user, project, ref error);
        }

        public bool DeleteProject(User owner, Project project, ref string error)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }
            if (project == null)
            {
                throw new ArgumentNullException(nameof(project));
            }
            lock (ControllerLock)
            {
                owner.RemovedUserForProject(project);
                foreach (var user in project.AdditionalUsers)
                {
                    user.RemovedUserForProject(project);
                }
                ActiveSessions.Remove(project);
                Projects.Remove(project);
                return project.Delete(ref error);
            }
        }

        /// <summary>
        /// This should only be invoked by the project session
        /// </summary>
        /// <param name="projectSession"></param>
        internal void UnloadSession(ProjectSession projectSession)
        {
            var project = projectSession.Project;
            // The project will be null if we are running the model system.
            if (!(project is null))
            {
                ActiveSessions.Remove(project);
            }
        }

        private string GetPath(User owner, string name)
        {
            return Path.Combine(owner.UserPath, name);
        }

        private ProjectSession GetSession(Project project)
        {
            lock (ControllerLock)
            {
                if (!ActiveSessions.TryGetValue(project, out var session))
                {
                    session = new ProjectSession(Runtime, project);
                    ActiveSessions.Add(project, session);
                }
                return session.AddReference();
            }
        }

        /// <summary>
        /// Ensure that a project name does not contain
        /// invalid characters
        /// </summary>
        /// <param name="name">The name to validate</param>
        /// <param name="error">A description of why the name was invalid.</param>
        /// <returns>If the validation allows this project name.</returns>
        public static bool ValidateProjectName(string name, ref string error)
        {
            if (String.IsNullOrWhiteSpace(name))
            {
                error = "A name must contain characters";
                return false;
            }
            if (name.Any(c => InvalidCharacters.Contains(c)))
            {
                error = "An invalid character was found in the name.";
                return false;
            }
            return true;
        }

        private Dictionary<Project, ProjectSession> ActiveSessions = new Dictionary<Project, ProjectSession>();

        private static char[] InvalidCharacters =
            Path.GetInvalidPathChars().Union(Path.GetInvalidFileNameChars()).ToArray();
    }
}
