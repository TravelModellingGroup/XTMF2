/*
    Copyright 2017-2019 University of Toronto

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
    /// <summary>
    /// The project controller is used for getting access to
    /// ProjectSessions, and creating new or deleting projects.
    /// </summary>
    public sealed class ProjectController
    {
        /// <summary>
        /// The collection of projects that are available
        /// </summary>
        private readonly ProjectRepository _projects = new ProjectRepository();

        private readonly XTMFRuntime _runtime;

        private readonly object _controllerLock = new object();

        internal ProjectController(XTMFRuntime runtime)
        {
            _runtime = runtime;
            LoadProjects(runtime);
        }

        /// <summary>
        /// Gets an observable collection of projects that the user
        /// has access to.
        /// </summary>
        /// <param name="user">The user to get the collection for.</param>
        /// <returns>A read only collection of the projects available to this user.</returns>
        public static ReadOnlyObservableCollection<Project> GetProjects(User user)
        {
            if (user is null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            return user.AvailableProjects;
        }

        /// <summary>
        /// Create a new project.  The name of the project needs to be unique
        /// for the given owner.
        /// </summary>
        /// <param name="owner">The user that owns the project.</param>
        /// <param name="name">The name of the project.</param>
        /// <param name="session">An editing session for this newly created project.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        public bool CreateNewProject(User owner, string name, out ProjectSession? session, out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(owner);
            Helper.ThrowIfNullOrWhitespace(name);

            session = null;
            if (!ValidateProjectName(name, out error))
            {
                return false;
            }
            lock (_controllerLock)
            {
                if (owner.OwnsProjectWithName(name))
                {
                    error = new CommandError("A project with that name already exists!");
                    return false;
                }
                if (!_projects.CreateNew(name, owner, out var p, out error))
                {
                    return false;
                }
                owner.AddedUserToProject(p!);
                session = GetSession(p!);
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
        public bool ImportProjectFile(User owner, string name, string filePath, out ProjectSession? session, out CommandError? error)
        {
            session = null;
            ArgumentNullException.ThrowIfNull(owner);            

            if (string.IsNullOrWhiteSpace(name))
            {
                error = new CommandError("A project must have a non-blank unique name.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(filePath))
            {
                error = new CommandError("The path to the project file must be not null or whitespace");
                return false;
            }
            if (!ValidateProjectName(name, out error))
            {
                return false;
            }
            lock (_controllerLock)
            {
                if(owner.OwnsProjectWithName(name))
                {
                    error = new CommandError("The user already has project with that name!");
                    return false;
                }
                if(!ProjectFile.ImportProject(owner, name, filePath, out var project, out error))
                {
                    return false;
                }
                string? errorString = null;
                if(!_projects.Add(project!, ref errorString))
                {
                    project!.Delete(out var _);
                    error = new CommandError(errorString!);
                    return false;
                }
                owner.AddedUserToProject(project!);
                session = GetSession(project!);
                return true;
            }
        }

        /// <summary>
        /// Gets a project session for the given project.
        /// </summary>
        /// <param name="user">The user requesting the project session.</param>
        /// <param name="project">The project that the session will be associated with.</param>
        /// <param name="session">The resulting project session.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation completes successfully, false otherwise with an error message.</returns>
        public bool GetProjectSession(User user, Project project, out ProjectSession? session, out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(project);

            lock(_controllerLock)
            {
                if(!project.CanAccess(user))
                {
                    error = new CommandError("The user does not have access to this project!", true);
                    session = null;
                    return false;
                }
                session = GetSession(project);
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Load all project headers.
        /// Should only be called by system configuration.
        /// </summary>
        /// <returns>List of projects that failed to load an reason why</returns>
        private List<(string Path, string? Error)> LoadProjects(XTMFRuntime runtime)
        {
            var errors = new List<(string Path, string? Error)>();
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
                        string? error = null;
                        if (Project.Load(runtime.UserController, projectFile.FullName, out Project project, ref error))
                        {
                            _projects.Add(project!, ref error);
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

        /// <summary>
        /// Get a reference to the project with the user's name and project name as a string.
        /// If the user has access to multiple projects with the same name, the one that they
        /// own will be selected.
        /// </summary>
        /// <param name="userName">The name of the user that this is for.</param>
        /// <param name="projectName">The name of the project to get a reference to.</param>
        /// <param name="project">The resulting project reference.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation completes successfully, false otherwise with an error message.</returns>
        public bool GetProject(string userName, string projectName, out Project? project, out CommandError? error)
        {
            Helper.ThrowIfNullOrWhitespace(userName);
            Helper.ThrowIfNullOrWhitespace(projectName);
            
            var user = _runtime.UserController.GetUserByName(userName);
            if(user == null)
            {
                project = null;
                error = new CommandError($"Unable to find a user with the name {userName}!");
                return false;
            }
            return GetProject(user, projectName, out project, out error);
        }

        /// <summary>
        /// Get a reference to the project with the user's name and project name as a string.
        /// If the user has access to multiple projects with the same name, the one that they
        /// own will be selected.
        /// </summary>
        /// <param name="userName">The name of the user that this is for.</param>
        /// <param name="projectName">The name of the project to get a reference to.</param>
        /// <param name="project">The resulting project reference.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation completes successfully, false otherwise with an error message.</returns>
        public bool GetProject(User user, string projectName, out Project? project, out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            Helper.ThrowIfNullOrWhitespace(projectName);
            
            lock (_controllerLock)
            {
                return _projects.GetProject(user, projectName, out project, out error);
            }
        }

        /// <summary>
        /// Delete a project with the given name.  The user must
        /// be the owner in order to delete the project.
        /// </summary>
        /// <param name="user">The owner of the project.</param>
        /// <param name="projectName">The name of the project to delete.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation completes successfully, false otherwise with an error message.</returns>
        public bool DeleteProject(User user, string projectName, out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            Helper.ThrowIfNullOrWhitespace(projectName);
            
            lock (_controllerLock)
            {
                var project = GetProjects(user).FirstOrDefault(p => p.Name!.Equals(projectName, StringComparison.OrdinalIgnoreCase));
                if (project == null)
                {
                    error = new CommandError($"User does not have a project called {projectName}!");
                    return false;
                }
                return DeleteProject(user, project, out error);
            }
        }

        /// <summary>
        /// Delete the given project.  The user must be the owner of the project.
        /// </summary>
        /// <param name="owner">The owner of the project.</param>
        /// <param name="project">The project to delete.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation completes successfully, false otherwise with an error message.</returns>
        public bool DeleteProject(User owner, Project project, out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(owner);
            ArgumentNullException.ThrowIfNull(project);

            lock (_controllerLock)
            {
                owner.RemovedUserForProject(project);
                foreach (var user in project.AdditionalUsers)
                {
                    user.RemovedUserForProject(project);
                }
                _activeSessions.Remove(project);
                _projects.Remove(project);
                return project.Delete(out error);
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
                _activeSessions.Remove(project);
            }
        }

        private string GetPath(User owner, string name)
        {
            return Path.Combine(owner.UserPath, name);
        }

        private ProjectSession GetSession(Project project)
        {
            lock (_controllerLock)
            {
                if (!_activeSessions.TryGetValue(project, out var session))
                {
                    session = new ProjectSession(_runtime, project);
                    _activeSessions.Add(project, session);
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
        public static bool ValidateProjectName(string name, ref string? error)
        {
            if (String.IsNullOrWhiteSpace(name))
            {
                error = "A name must contain characters";
                return false;
            }
            if (name.Any(c => _invalidCharacters.Contains(c)))
            {
                error = "An invalid character was found in the name.";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Ensure that a project name does not contain
        /// invalid characters
        /// </summary>
        /// <param name="name">The name to validate</param>
        /// <param name="error">A description of why the name was invalid.</param>
        /// <returns>If the validation allows this project name.</returns>
        public static bool ValidateProjectName(string name, out CommandError? error)
        {
            if (String.IsNullOrWhiteSpace(name))
            {
                error = new CommandError("A name must contain characters");
                return false;
            }
            if (name.Any(c => _invalidCharacters.Contains(c)))
            {
                error = new CommandError("An invalid character was found in the name.");
                return false;
            }
            error = null;
            return true;
        }

        private readonly Dictionary<Project, ProjectSession> _activeSessions = new Dictionary<Project, ProjectSession>();

        private static readonly char[] _invalidCharacters =
            Path.GetInvalidPathChars().Union(Path.GetInvalidFileNameChars()).ToArray();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="user"></param>
        /// <param name="project"></param>
        /// <param name="newProjectName"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        public bool RenameProject(User user, Project project, string newProjectName, out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);

            if (!ValidateProjectName(newProjectName, out error))
            {
                return false;
            }
            lock(_controllerLock)
            {
                if(!project.CanAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                if (user.OwnsProjectWithName(newProjectName))
                {
                    error = new CommandError($"The user already owns a project with the name {newProjectName}");
                    return false;
                }
                if (_activeSessions.ContainsKey(project))
                {
                    error = new CommandError("You can not rename a project that is currently being edited");
                    return false;
                }
                return project.SetName(newProjectName, out error);
            }
        }
    }
}
