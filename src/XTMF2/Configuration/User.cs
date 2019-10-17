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
using System.Text.Json;
using System.Linq;
using System.IO;

namespace XTMF2
{
    /// <summary>
    /// Provides the interactions for a user of XTMF.
    /// </summary>
    public sealed class User
    {
        /// <summary>
        /// The unique name for the user
        /// </summary>
        public string UserName { get; }

        /// <summary>
        /// Does this user have administrative rights?
        /// </summary>
        public bool Admin { get; private set; }

        private const string UserNameProperty = "UserName";
        private const string AdminProperty = "Admin";

        /// <summary>
        /// Lock this before editing a user's available projects
        /// </summary>
        private object _ProjectLock = new object();

        public ReadOnlyObservableCollection<Project> AvailableProjects
        {
            get
            {
                return new ReadOnlyObservableCollection<Project>(_AvailableProjects);
            }
        }

        /// <summary>
        /// The path to the user's storage.
        /// </summary>
        public string UserPath { get; internal set; }

        /// <summary>
        /// The projects available for this user.
        /// </summary>
        private ObservableCollection<Project> _AvailableProjects = new ObservableCollection<Project>();

        /// <summary>
        /// Create a new user
        /// </summary>
        /// <param name="userPath">The default storage location for this user</param>
        /// <param name="userName">A unique name for this user</param>
        /// <param name="admin">Does this user have administrative privileges?</param>
        internal User(string userPath, string userName, bool admin = false)
        {
            UserName = userName;
            Admin = admin;
            UserPath = userPath;
        }

        /// <summary>
        /// Give a user the access to a project
        /// </summary>
        /// <param name="project">The project to access</param>
        internal void AddedUserToProject(Project project)
        {
            if (project == null)
            {
                throw new ArgumentNullException(nameof(project));
            }
            lock (_ProjectLock)
            {
                _AvailableProjects.Add(project);
            }
        }

        /// <summary>
        /// Checks to see if a user has a project with the given name already defined and is the owner.
        /// </summary>
        /// <param name="name">The name of the project</param>
        /// <returns>True if there is already a project defined with the name and is the owner.</returns>
        internal bool HasProjectWithName(string name)
        {
            lock (_ProjectLock)
            {
                return _AvailableProjects.Any(p => p.Owner == this && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>
        /// Remove the given project from the list of projects that the user
        /// is allowed to access.
        /// </summary>
        /// <param name="project">The project to remove access from.</param>
        internal void RemovedUserForProject(Project project)
        {
            lock (_ProjectLock)
            {
                _AvailableProjects.Remove(project);
            }
        }

        /// <summary>
        /// Load a user from file.
        /// </summary>
        /// <param name="userFile">The file location to load.</param>
        /// <param name="user">The resulting user</param>
        /// <param name="error">An error message in case of failure.</param>
        /// <returns>True if successful, false otherwise with an error message.</returns>
        internal static bool Load(string userFile, out User user, ref string error)
        {
            string userName = null;
            bool admin = false;
            try
            {
                var buffer = File.ReadAllBytes(userFile);
                var reader = new Utf8JsonReader(buffer.AsSpan());
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        if (reader.ValueTextEquals(UserNameProperty))
                        {
                            reader.Read();
                            userName = reader.GetString();
                        }
                        else if (reader.ValueTextEquals(AdminProperty))
                        {
                            reader.Read();
                            admin = reader.GetBoolean();
                        }
                    }
                }
            }
            catch (JsonException e)
            {
                error = e.Message;
                user = null;
                return false;
            }
            if (userName == null)
            {
                user = null;
                error = "The user file failed to contain a user name!";
                return false;
            }
            user = new User(Path.GetDirectoryName(userFile), userName, admin);
            return true;
        }

        /// <summary>
        /// Save the user information.
        /// </summary>
        /// <param name="error">The error message in case of failure.</param>
        /// <returns>True if successful, false otherwise with an error message.</returns>
        internal bool Save(ref string error)
        {
            var temp = Path.GetTempFileName();
            try
            {
                var userDir = new DirectoryInfo(UserPath);
                if (!userDir.Exists)
                {
                    userDir.Create();
                }
                using (var stream = File.Create(temp))
                using (var writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    writer.WriteString(UserNameProperty, UserName);
                    writer.WriteBoolean(AdminProperty, Admin);
                    writer.WriteEndObject();
                }
                // when we have complete copy the results
                File.Copy(temp, Path.Combine(UserPath, "User.xusr"), true);
                return true;
            }
            catch (IOException e)
            {
                error = e.Message;
                return false;
            }
            finally
            {
                // make sure we cleanup the temporary file
                var tempFile = new FileInfo(temp);
                if (tempFile.Exists)
                {
                    tempFile.Delete();
                }
            }
        }
    }
}
