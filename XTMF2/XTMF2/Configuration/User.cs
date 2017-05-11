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
using System.Text;
using System.Linq;
using System.IO;
using Newtonsoft.Json;

namespace XTMF2
{
    public sealed class User
    {
        public string UserName { get; }

        public bool Admin { get; private set; }

        private object ProjectLock = new object();

        public ReadOnlyObservableCollection<Project> AvailableProjects
        {
            get
            {
                return new ReadOnlyObservableCollection<Project>(_AvailableProjects);
            }
        }

        public string UserPath { get; internal set; }

        private ObservableCollection<Project> _AvailableProjects = new ObservableCollection<Project>();

        public User(string userPath, string userName, bool admin = false)
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
            lock (ProjectLock)
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
            lock (ProjectLock)
            {
                return _AvailableProjects.Any(p => p.Owner == this && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            }
        }

        internal void RemovedUserForProject(Project project)
        {
            lock (ProjectLock)
            {
                _AvailableProjects.Remove(project);
            }
        }

        internal static bool Load(string userFile, out User user, ref string error)
        {
            string userName = null;
            bool admin = false;
            try
            {
                using (var stream = new StreamReader(File.OpenRead(userFile)))
                using (var reader = new JsonTextReader(stream))
                {
                    while(reader.Read())
                    {
                        if(reader.TokenType == JsonToken.PropertyName)
                        {
                            switch(reader.Value)
                            {
                                case "UserName":
                                    userName = reader.ReadAsString();
                                    break;
                                case "Admin":
                                    {
                                        
                                        var result = reader.ReadAsBoolean();
                                        if(result == null)
                                        {
                                            user = null;
                                            error = "Invalid Admin element!";
                                            return false;
                                        }
                                        admin = (bool)result;
                                    }
                                    break;
                            }
                        }
                    }
                }
            }
            catch(JsonException e)
            {
                error = e.Message;
                user = null;
                return false;
            }
            if(userName == null)
            {
                user = null;
                error = "The user file failed to contain a user name!";
                return false;
            }
            user = new User(Path.GetDirectoryName(userFile), userName, admin);
            return true;
        }

        internal bool Save(ref string error)
        {
            var temp = Path.GetTempFileName();
            try
            {
                var userDir = new DirectoryInfo(UserPath);
                if(!userDir.Exists)
                {
                    userDir.Create();
                }
                using (var stream = new StreamWriter(File.Create(temp)))
                using (JsonTextWriter writer = new JsonTextWriter(stream))
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName("UserName");
                    writer.WriteValue(UserName);
                    writer.WritePropertyName("Admin");
                    writer.WriteValue(Admin);
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
