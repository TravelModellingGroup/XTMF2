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
using System.Threading;
using XTMF2.Controllers;
using XTMF2.Editing;
using XTMF2.Repository;
using System.IO.Compression;
using System.Text.Json;
using System.Diagnostics;

namespace XTMF2.Editing
{
    /// <summary>
    /// The editing session for a project.
    /// </summary>
    public sealed class ProjectSession : IDisposable
    {
        /// <summary>
        /// The project that is being edited.
        /// </summary>
        public Project Project { get; private set; }

        /// <summary>
        /// The link to the XTMFRuntime
        /// </summary>
        private readonly XTMFRuntime _runtime;

        /// <summary>
        /// The lock that must be acquired before editing any member variables.
        /// </summary>
        private readonly object _sessionLock = new object();

        /// <summary>
        /// Invoked when a view into the model system session is disposed.
        /// </summary>
        /// <param name="mss">The model system session to be decremented</param>
        /// <param name="references"></param>
        internal void ModelSystemSessionDecrementing(ModelSystemSession mss, ref int references)
        {
            lock (_sessionLock)
            {
                if (Interlocked.Decrement(ref references) <= 0)
                {
                    lock (_sessionLock)
                    {
                        _activeSessions.Remove(mss.ModelSystemHeader);
                    }
                    // remove one reference to the project session
                    Dispose();
                }
            }
        }

        // This is 0 instead of 1 intentionally so that the controller adds a reference
        private int _references = 0;

        /// <summary>
        /// The number of references to this project session.
        /// </summary>
        public int References => _references;

        /// <summary>
        /// The active editing sessions for the model systems contained
        /// within this project.
        /// </summary>
        private readonly Dictionary<ModelSystemHeader, ModelSystemSession> _activeSessions = new Dictionary<ModelSystemHeader, ModelSystemSession>();

        /// <summary>
        /// The model systems that are contained in this project
        /// </summary>
        public ReadOnlyObservableCollection<ModelSystemHeader> ModelSystems => Project.ModelSystems;

        /// <summary>
        /// The directory that is storing the results of model runs.
        /// </summary>
        public string RunsDirectory => Project.RunsDirectory;

        /// <summary>
        /// Increment the number of references to this project session.
        /// </summary>
        /// <returns>A reference to this project session</returns>
        internal ProjectSession AddReference()
        {
            Interlocked.Increment(ref _references);
            return this;
        }

        /// <summary>
        /// Check to see if the given user has access to the project.
        /// </summary>
        /// <param name="user">The user to test for.</param>
        /// <returns>True if the user has access to the project, false otherwise.</returns>
        internal bool HasAccess(User user)
        {
            if (user is null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            lock (_sessionLock)
            {
                return Project.CanAccess(user);
            }
        }

        /// <summary>
        /// Get a reference to the module repository available from
        /// the XTMF runtime.
        /// </summary>
        /// <returns>The module repository</returns>
        internal ModuleRepository GetModuleRepository()
        {
            return _runtime.Modules;
        }

        /// <summary>
        /// Create a project session allowing for the interaction with
        /// and editing of a project.
        /// </summary>
        /// <param name="runtime">The XTMF runtime that this project is in.</param>
        /// <param name="project">The project that will be edited.</param>
        public ProjectSession(XTMFRuntime runtime, Project project)
        {
            Project = project;
            _runtime = runtime;
        }

        /// <summary>
        /// Remove a reference and if no references exist
        /// cleanup the project session.
        /// </summary>
        public void Dispose()
        {
            var left = Interlocked.Decrement(ref _references);
            if (left <= 0)
            {
                Dispose(true);
            }
        }

        private void Dispose(bool managed)
        {
            if (managed)
            {
                GC.SuppressFinalize(this);
            }
            _runtime.ProjectController.UnloadSession(this);
        }

        ~ProjectSession()
        {
            Dispose(false);
        }

        /// <summary>
        /// Save the project files.
        /// </summary>
        /// <param name="error">An error message if the save fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        public bool Save(out CommandError error)
        {
            lock (_sessionLock)
            {
                string errorString = null;
                if(!Project.Save(ref errorString))
                {
                    error = new CommandError(errorString);
                    return false;
                }
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Create a new model system with the given name.  The name must be unique.
        /// </summary>
        /// <param name="modelSystemName">The name of the model system (must be unique within the project).</param>
        /// <param name="modelSystem">The resulting model system session</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        public bool CreateNewModelSystem(User user, string modelSystemName, out ModelSystemHeader modelSystem, out CommandError error)
        {
            modelSystem = null;
            if (!ProjectController.ValidateProjectName(modelSystemName, out error))
            {
                return false;
            }
            lock (_sessionLock)
            {
                if(!Project.CanAccess(user))
                {
                    error = new CommandError("The user does not have access to this project!", true);
                    return false;
                }
                if (Project.ContainsModelSystem(modelSystemName))
                {
                    error = new CommandError("A model system with this name already exists.");
                    return false;
                }
                modelSystem = new ModelSystemHeader(Project, modelSystemName);
                return Project.Add(this, modelSystem, out error);
            }
        }

        /// <summary>
        /// Create a model system session allowing for the editing of a model system.
        /// </summary>
        /// <param name="user">The user that is requesting access.</param>
        /// <param name="modelSystemHeader">The model system reference to load.</param>
        /// <param name="session">The resulting session.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        public bool EditModelSystem(User user, ModelSystemHeader modelSystemHeader, out ModelSystemSession session, out CommandError error)
        {
            if (user is null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            if (modelSystemHeader is null)
            {
                throw new ArgumentNullException(nameof(modelSystemHeader));
            }
            session = null;
            lock (_sessionLock)
            {
                if (!Project.CanAccess(user))
                {
                    error = new CommandError("The given user does not have access to this project!", true);
                    return false;
                }
                if (!Project.ContainsModelSystem(modelSystemHeader))
                {
                    error = new CommandError("The model system header provided does not belong to this project!");
                    return false;
                }
                if (!_activeSessions.TryGetValue(modelSystemHeader, out session))
                {
                    if (ModelSystem.Load(this, modelSystemHeader, out session, out error))
                    {
                        _activeSessions.Add(modelSystemHeader, session);
                        Interlocked.Increment(ref _references);
                        return true;
                    }
                    return false;
                }
                else
                {
                    session.AddReference();
                }
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Remove a model system from the project.
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="modelSystem">The model system to remove.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        public bool RemoveModelSystem(User user, ModelSystemHeader modelSystem, out CommandError error)
        {
            if (user is null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            if (modelSystem is null)
            {
                throw new ArgumentNullException(nameof(modelSystem));
            }

            lock (_sessionLock)
            {
                if(Project.Owner != user)
                {
                    error = new CommandError("You can not remove a model system that you are not the owner of.", true);
                    return false;
                }
                if(_activeSessions.ContainsKey(modelSystem))
                {
                    error = new CommandError("You can not remove a model system that is currently being edited!");
                    return false;
                }
                var modelSystemFile = modelSystem.ModelSystemPath;
                if(!Project.Remove(this, modelSystem, out error))
                {
                    return false;
                }
                try
                {
                    File.Delete(modelSystemFile);
                }
#pragma warning disable CA1031 // If the model system file is already gone, then the operation actually succeeded.
                catch (IOException)
                {

                }
#pragma warning restore CA1031 // Do not catch general exception types
                return true;
            }
        }

        /// <summary>
        /// Exports a project meta-data and model systems to the given path as a zip file.
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="exportPath">The file that the project will be saved as.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        public bool ExportProject(User user, string exportPath, out CommandError error)
        {
            if (user is null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            if (string.IsNullOrWhiteSpace(exportPath))
            {
                throw new ArgumentException("message", nameof(exportPath));
            }

            lock(_sessionLock)
            {
                if(!Project.CanAccess(user))
                {
                    error = new CommandError("The user does not have access to the project.", true);
                    return false;
                }
                if(_activeSessions.Count > 0)
                {
                    error = new CommandError("The project is currently being edited and can not be exported.");
                    return false;
                }
                return ProjectFile.ExportProject(this, user, exportPath, out error);
            }
        }

        /// <summary>
        /// Exports a model system to file.
        /// </summary>
        /// <param name="user">The user that is issuing the command.</param>
        /// <param name="modelSystemHeader">The model system to export.</param>
        /// <param name="exportPath">The location to export the model system to.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with error message.</returns>
        public bool ExportModelSystem(User user, ModelSystemHeader modelSystemHeader, string exportPath, out CommandError error)
        {
            if (user is null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            if (modelSystemHeader is null)
            {
                throw new ArgumentNullException(nameof(modelSystemHeader));
            }

            if (string.IsNullOrWhiteSpace(exportPath))
            {
                error = new CommandError("The path to save the model system to must not be empty.");
                return false;
            }
            lock (_sessionLock)
            {
                if (!Project.CanAccess(user))
                {
                    error = new CommandError("The user does not have access to the project.", true);
                    return false;
                }
                if (_activeSessions.TryGetValue(modelSystemHeader, out var mss))
                {
                    error = new CommandError("The model system is currently being edited and can not be exported.");
                    return false;
                }
                return ModelSystemFile.ExportModelSystem(this, user, modelSystemHeader, exportPath, out error);
            }
        }

        /// <summary>
        /// Retrieve the header for a model system with a given name.
        /// </summary>
        /// <param name="user">The user executing the action.</param>
        /// <param name="modelSystemName">The name of the model system.</param>
        /// <param name="modelSystemHeader">The resulting model system header.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        public bool GetModelSystemHeader(User user, string modelSystemName, out ModelSystemHeader modelSystemHeader, out CommandError error)
        {
            if (user is null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            if (String.IsNullOrWhiteSpace(modelSystemName))
            {
                throw new ArgumentNullException(nameof(modelSystemName));
            }
            lock (_sessionLock)
            {
                if (!Project.CanAccess(user))
                {
                    modelSystemHeader = null;
                    error = new CommandError("User is unable to access project.", true);
                    return false;
                }
                return Project.GetModelSystemHeader(modelSystemName, out modelSystemHeader, out error);
            }
        }

        /// <summary>
        /// Share the project with the given user
        /// </summary>
        /// <param name="doingShare">The user that is issuing the share command</param>
        /// <param name="toSharWith">The person to share with</param>
        /// <param name="error">An error message if appropriate</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        public bool ShareWith(User doingShare, User toSharWith, out CommandError error)
        {
            // test our arguments
            if (doingShare is null)
            {
                throw new ArgumentNullException(nameof(doingShare));
            }
            if (toSharWith is null)
            {
                throw new ArgumentNullException(nameof(doingShare));
            }
            lock (_sessionLock)
            {
                if (!(doingShare.IsAdmin || doingShare == Project.Owner))
                {
                    error = new CommandError("The user sharing the project must either be the owner or an administrator!", true);
                    return false;
                }
                // now that we know that we can do the share
                return Project.AddAdditionalUser(toSharWith, out error);
            }
        }

        /// <summary>
        /// Create a project session for handling the information to do a run.
        /// </summary>
        /// <param name="runtime">The runtime to work within.</param>
        /// <returns></returns>
        internal static ProjectSession CreateRunSession(XTMFRuntime runtime)
        {
            return new ProjectSession(runtime, null);
        }

        /// <summary>
        /// Give ownership of a project to a different user
        /// </summary>
        /// <param name="owner"></param>
        /// <param name="newOwner"></param>
        /// <param name="error"></param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        public bool SwitchOwner(User owner, User newOwner, out CommandError error)
        {
            if (owner is null)
            {
                throw new ArgumentNullException(nameof(owner));
            }
            if (newOwner is null)
            {
                throw new ArgumentNullException(nameof(newOwner));
            }
            lock (_sessionLock)
            {
                if (!(owner.IsAdmin || owner == Project.Owner))
                {
                    error = new CommandError("The owner must either be an administrator or the original owner of the project.", true);
                    return false;
                }
                return Project.GiveOwnership(newOwner, out error);
            }
        }

        /// <summary>
        /// Remove access for a user to access a project.
        /// </summary>
        /// <param name="owner">The owner of the project.</param>
        /// <param name="toRestrict">The user to remove access to.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        public bool RestrictAccess(User owner, User toRestrict, out CommandError error)
        {
            if (owner is null)
            {
                throw new ArgumentNullException(nameof(owner));
            }
            if (toRestrict is null)
            {
                throw new ArgumentNullException(nameof(toRestrict));
            }
            lock (_sessionLock)
            {
                if (!(owner.IsAdmin || owner == Project.Owner))
                {
                    error = new CommandError("The owner must either be an administrator or the original owner of the project.", true);
                    return false;
                }
                if (toRestrict == Project.Owner)
                {
                    error = new CommandError("You can not restrict access to the owner of a project.");
                    return false;
                }
                return Project.RemoveAdditionalUser(toRestrict, out error);
            }
        }

        /// <summary>
        /// Import a model system from file.
        /// </summary>
        /// <param name="user">The user issuing the import file system command.</param>
        /// <param name="modelSystemFilePath">The path to the file to import.</param>
        /// <param name="modelSystemName">The name to give the model system within this project.</param>
        /// <param name="header">A resulting header for the newly imported model system.</param>
        /// <param name="error">The error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        public bool ImportModelSystem(User user, string modelSystemFilePath, string modelSystemName, 
            out ModelSystemHeader header, out CommandError error)
        {
            header = null;
            if (user is null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            if (string.IsNullOrWhiteSpace(modelSystemFilePath))
            {
                throw new ArgumentException(nameof(modelSystemFilePath));
            }

            if (string.IsNullOrWhiteSpace(modelSystemName))
            {
                throw new ArgumentException(nameof(modelSystemName));
            }
            try
            {
                using var archive = ZipFile.OpenRead(modelSystemFilePath);
                lock (_sessionLock)
                {
                    if (!HasAccess(user))
                    {
                        error = new CommandError("The user that issued the command does not have access to this project.", true);
                        return false;
                    }
                    if(Project.ContainsModelSystem(modelSystemName))
                    {
                        error = new CommandError("A model system with that name already exists!");
                        return false;
                    }
                    if(!ModelSystemFile.LoadModelSystemFile(modelSystemFilePath, out var msf, out error))
                    {
                        return false;
                    }
                    return Project.AddModelSystemFromModelSystemFile(modelSystemName, msf, out header, out error);
                }
            }
            catch (InvalidDataException e)
            {
                error = new CommandError(e.Message);
            }
            catch (IOException e)
            {
                error = new CommandError(e.Message);
            }
            return false;
        }

        /// <summary>
        /// Set a custom run directory for this project.
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="fullName">The path to where to store runs.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        public bool SetCustomRunDirectory(User user, string fullName, out CommandError error)
        {
            if (user is null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            if (string.IsNullOrWhiteSpace(fullName))
            {
                error = new CommandError($"A non-blank directory is expected.");
                return false;
            }
            lock(_sessionLock)
            {
                if(!Project.CanAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                return Project.SetCustomRunsDirectory(fullName, out error);
            }
        }

        /// <summary>
        /// Reset the project's run directory back to the default directory.
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        public bool ResetCustomRunDirectory(User user, out CommandError error)
        {
            if (user is null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            lock (_sessionLock)
            {
                if(!Project.CanAccess(user))
                {
                    error = new CommandError("The user can not access this project.", true);
                    return false;
                }
                return Project.ResetRunsDirectory(out error);
            }
        }

        /// <summary>
        /// Rename a model system.  The model system can not be currently
        /// being edited.
        /// </summary>
        /// <param name="user">The use issuing the command.</param>
        /// <param name="newName">The new name of the model system.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        public bool RenameModelSystem(User user, ModelSystemHeader modelSystem, string newName, out CommandError error)
        {
            if (user is null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            if (modelSystem is null)
            {
                throw new ArgumentNullException(nameof(modelSystem));
            }
            if (string.IsNullOrWhiteSpace(newName))
            {
                error = new CommandError("The name of the model system must not be blank.");
                return false;
            }
            lock(_sessionLock)
            {
                if(!Project.CanAccess(user))
                {
                    error = new CommandError("The user can not access this project.", true);
                    return false;
                }
                return Project.RenameModelSystem(modelSystem, newName, out error);
            }
        }
    }
}
