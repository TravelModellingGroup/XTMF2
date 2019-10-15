﻿/*
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
        /// Unload a model system session and reduce the number of references
        /// to this project session.
        /// </summary>
        /// <param name="modelSystemSession">The model system session to remove.</param>
        internal void UnloadSession(ModelSystemSession modelSystemSession)
        {
            lock (_sessionLock)
            {
                _activeSessions.Remove(modelSystemSession.ModelSystemHeader);
            }
            // remove one reference to the project session
            Dispose();
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
        public bool Save(ref string error)
        {
            lock (_sessionLock)
            {
                return Project.Save(ref error);
            }
        }

        /// <summary>
        /// Create a new model system with the given name.  The name must be unique.
        /// </summary>
        /// <param name="modelSystemName">The name of the model system (must be unique within the project).</param>
        /// <param name="modelSystem">The resulting model system session</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        public bool CreateNewModelSystem(string modelSystemName, out ModelSystemHeader modelSystem, ref string error)
        {
            modelSystem = null;
            if (!ProjectController.ValidateProjectName(modelSystemName, ref error))
            {
                return false;
            }
            lock (_sessionLock)
            {
                if (Project.ContainsModelSystem(modelSystemName))
                {
                    error = "A model system with this name already exists.";
                    return false;
                }
                modelSystem = new ModelSystemHeader(Project, modelSystemName);
                return Project.Add(this, modelSystem, ref error); ;
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
        public bool EditModelSystem(User user, ModelSystemHeader modelSystemHeader, out ModelSystemSession session, ref string error)
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
                if (!Project.ContainsModelSystem(modelSystemHeader))
                {
                    error = "The model system header provided does not belong to this project!";
                    return false;
                }
                if (!Project.CanAccess(user))
                {
                    error = "The given user does not have access to this project!";
                    return false;
                }
                if (!_activeSessions.TryGetValue(modelSystemHeader, out session))
                {
                    if (ModelSystem.Load(this, modelSystemHeader, out session, ref error))
                    {
                        _activeSessions.Add(modelSystemHeader, session);
                        return true;
                    }
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// Constant strings used for importing and exporting
        /// a model system.
        /// </summary>
        private static class ModelSystemFileStrings
        {
            /// <summary>
            /// A string used for the meta-data to gather the name of the model system.
            /// </summary>
            internal const string Name = "Name";
            /// <summary>
            /// A string used in the meta-data to provide a description of the model system.
            /// </summary>
            internal const string Description = "Description";
            /// <summary>
            /// A string used in the meta-data to give the name of the user that exported the model system
            /// </summary>
            internal const string ExportedBy = "ExportedBy";
            /// <summary>
            /// A string used by the meta-data to give what time in UTC that the model system was exported at.
            /// </summary>
            internal const string ExportedOn = "ExportedOn";
            /// <summary>
            /// A string used by the meta-data to indicate what version of XTMF exported the model system.
            /// </summary>
            internal const string VersionMajor = "VersionMajor";
            /// <summary>
            /// A string used by the meta-data to indicate what version of XTMF exported the model system.
            /// </summary>
            internal const string VersionMinor = "VersionMinor";

            /// <summary>
            /// The name of the file within the archive for where the model system was stored.
            /// </summary>
            internal const string ModelSystemFile = "ModelSystem.xmsys";
            /// <summary>
            /// The name of the file within the archive for where the meta-data was stored.
            /// </summary>
            internal const string MetaDataFile = "metadata.json";
        }

        /// <summary>
        /// Exports a model system to file.
        /// </summary>
        /// <param name="user">The user that is issuing the command.</param>
        /// <param name="modelSystemHeader">The model system to export.</param>
        /// <param name="exportPath">The location to export the model system to.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with error message.</returns>
        public bool ExportModelSystem(User user, ModelSystemHeader modelSystemHeader, string exportPath, ref string error)
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
                error = "The path to save the model system to must not be empty.";
                return false;
            }
            var tempDirName = Path.Combine(Path.GetTempPath(), "XTMF-" + Project.Name + modelSystemHeader.Name + Guid.NewGuid());
            System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
            try
            {
                lock (_sessionLock)
                {
                    if (!Project.CanAccess(user))
                    {
                        error = "The user does not have access to the project.";
                        return false;
                    }
                    if (_activeSessions.TryGetValue(modelSystemHeader, out var mss))
                    {
                        error = "The model system is currently being edited and can not be exported.";
                        return false;
                    }
                    var tempDir = new DirectoryInfo(tempDirName);
                    if (!tempDir.Exists)
                    {
                        tempDir.Create();
                    }
                    // copy in the model system file
                    File.Copy(modelSystemHeader.ModelSystemPath, Path.Combine(tempDir.FullName, ModelSystemFileStrings.ModelSystemFile));
                    using (var metadataStream = File.OpenWrite(Path.Combine(tempDir.FullName, ModelSystemFileStrings.MetaDataFile)))
                    using (var writer = new Utf8JsonWriter(metadataStream))
                    {
                        writer.WriteStartObject();
                        writer.WriteString(ModelSystemFileStrings.Name, modelSystemHeader.Name);
                        writer.WriteString(ModelSystemFileStrings.Description, modelSystemHeader.Description);
                        writer.WriteString(ModelSystemFileStrings.ExportedOn, DateTime.UtcNow);
                        writer.WriteString(ModelSystemFileStrings.ExportedBy, user.UserName);
                        writer.WriteNumber(ModelSystemFileStrings.VersionMajor, fvi.FileMajorPart);
                        writer.WriteNumber(ModelSystemFileStrings.VersionMinor, fvi.FileMinorPart);
                        writer.WriteEndObject();
                    }
                    // Zip the temporary directory and store it.
                    ZipFile.CreateFromDirectory(tempDirName, exportPath);
                    return true;
                }
            }
            catch (IOException e)
            {
                error = e.Message;
            }
            finally
            {
                // Try to clean up the temporary directory if it still exists.
                try
                {
                    var tempDir = new DirectoryInfo(tempDirName);
                    if (tempDir.Exists)
                    {
                        tempDir.Delete(true);
                    }
                }
                /*
                 * This will warn that we should catch a more specific exception however there is no recovery in any case.
                 * The operation has already been successful even if we are unable to clean up the temporary storage.
                 */
                #pragma warning disable CA1031
                catch (IOException)
                
                {
                    // If we don't have access to the temporary storage there is nothing else that we can do.
                }
                #pragma warning restore CA1031
            }
            return false;
        }

        /// <summary>
        /// Retrieve the header for a model system with a given name.
        /// </summary>
        /// <param name="user">The user executing the action.</param>
        /// <param name="modelSystemName">The name of the model system.</param>
        /// <param name="modelSystemHeader">The resulting model system header.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        public bool GetModelSystemHeader(User user, string modelSystemName, out ModelSystemHeader modelSystemHeader, ref string error)
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
                    error = "User is unable to access project.";
                    return false;
                }
                return Project.GetModelSystemHeader(modelSystemName, out modelSystemHeader, ref error);
            }
        }

        /// <summary>
        /// Share the project with the given user
        /// </summary>
        /// <param name="doingShare">The user that is issuing the share command</param>
        /// <param name="toSharWith">The person to share with</param>
        /// <param name="error">An error message if appropriate</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        public bool ShareWith(User doingShare, User toSharWith, ref string error)
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
                if (!(doingShare.Admin || doingShare == Project.Owner))
                {
                    error = "The user sharing the project must either be the owner or an administrator!";
                    return false;
                }
                // now that we know that we can do the share
                return Project.AddAdditionalUser(toSharWith, ref error);
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
        public bool SwitchOwner(User owner, User newOwner, ref string error)
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
                if (!(owner.Admin || owner == Project.Owner))
                {
                    error = "The owner must either be an administrator or the original owner of the project.";
                    return false;
                }
                return Project.GiveOwnership(newOwner, ref error);
            }
        }

        /// <summary>
        /// Remove access for a user to access a project.
        /// </summary>
        /// <param name="owner">The owner of the project.</param>
        /// <param name="toRestrict">The user to remove access to.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        public bool RestrictAccess(User owner, User toRestrict, ref string error)
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
                if (!(owner.Admin || owner == Project.Owner))
                {
                    error = "The owner must either be an administrator or the original owner of the project.";
                    return false;
                }
                if (toRestrict == Project.Owner)
                {
                    error = "You can not restrict access to the owner of a project.";
                    return false;
                }
                return Project.RemoveAdditionalUser(toRestrict, ref error);
            }
        }
    }
}
