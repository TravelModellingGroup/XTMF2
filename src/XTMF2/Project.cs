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
using System.ComponentModel;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using XTMF2.Editing;
using XTMF2.Controllers;
using System.Linq;

namespace XTMF2
{
    /// <summary>
    /// A collection of model systems with access rights
    /// for users.
    /// </summary>
    public sealed class Project : INotifyPropertyChanged
    {
        private const string ProjectFile = "Project.xpjt";
        public string Name { get; private set; }
        public string Description { get; private set; }
        public string ProjectFilePath { get; private set; }
        public string ProjectDirectory => Path.GetDirectoryName(ProjectFilePath);
        public User Owner { get; private set; }
        public ReadOnlyCollection<User> AdditionalUsers => new ReadOnlyCollection<User>(_AdditionalUsers);
        ObservableCollection<User> _AdditionalUsers = new ObservableCollection<User>();
        ObservableCollection<ModelSystemHeader> _ModelSystems = new ObservableCollection<ModelSystemHeader>();
        public ReadOnlyObservableCollection<ModelSystemHeader> ModelSystems => new ReadOnlyObservableCollection<ModelSystemHeader>(_ModelSystems);

        public string RunsDirectory => Path.Combine(ProjectDirectory, "Runs");

        private object ProjectLock = new object();

        public event PropertyChangedEventHandler PropertyChanged;

        private Project()
        {

        }

        public static bool Load(UserController userController, string filePath, out Project project, ref string error)
        {
            project = new Project()
            {
                ProjectFilePath = filePath
            };
            try
            {
                using (var fileStream = new StreamReader(File.OpenRead(filePath)))
                {
                    using (var reader = new JsonTextReader(fileStream))
                    {
                        while (reader.Read())
                        {
                            if (reader.TokenType == JsonToken.PropertyName)
                            {
                                switch (reader.Value)
                                {
                                    case "Name":
                                        project.Name = reader.ReadAsString();
                                        break;
                                    case "Description":
                                        project.Description = reader.ReadAsString();
                                        break;
                                    case "ModelSystemHeaders":
                                        {
                                            reader.Read();
                                            if (reader.TokenType != JsonToken.StartArray)
                                            {
                                                error = "We expected a start of array but found a " + Enum.GetName(typeof(JsonToken), reader.TokenType);
                                                return false;
                                            }
                                            while (reader.Read() && reader.TokenType != JsonToken.EndArray)
                                            {
                                                project._ModelSystems.Add(ModelSystemHeader.Load(project, reader));
                                            }
                                        }
                                        break;
                                    case "Owner":
                                        {
                                            var user = userController.GetUserByName(reader.ReadAsString());
                                            project.Owner = user;
                                            user?.AddedUserToProject(project);
                                        }
                                        break;
                                    case "AdditionalUsers":
                                        {
                                            reader.Read();
                                            if(reader.TokenType != JsonToken.StartArray)
                                            {
                                                throw new Exception("Expected Start Array while loading project!");
                                            }
                                            while (reader.Read() && reader.TokenType != JsonToken.EndArray)
                                            {
                                                var user = userController.GetUserByName(reader.ReadAsString());
                                                project._AdditionalUsers.Add(user);
                                                user.AddedUserToProject(project);
                                            }
                                        }
                                        break;
                                    // if we don't know what it is just continue on.
                                    default:
                                        reader.Skip();
                                        break;
                                }
                            }
                        }
                    }
                }
                if(project.Owner == null)
                {
                    error = "Unable to load an owner for the given project.";
                    return false;
                }
                return true;
            }
            catch (JsonException e)
            {
                error = e.Message;
            }
            catch (IOException e)
            {
                error = e.Message;
            }
            return false;
        }

        internal bool GetModelSystemHeader(string modelSystemName, out ModelSystemHeader modelSystemHeader, ref string error)
        {
            modelSystemHeader = _ModelSystems.FirstOrDefault(msh => msh.Name.Equals(modelSystemName, StringComparison.OrdinalIgnoreCase));
            if(modelSystemHeader == null)
            {
                error = "A model system with the given name was not found!";
            }
            return modelSystemHeader != null;
        }

        internal bool ContainsModelSystem(ModelSystemHeader modelSystemHeader)
        {
            return _ModelSystems.Contains(modelSystemHeader);
        }

        internal bool ContainsModelSystem(string modelSystemName)
        {
            return _ModelSystems.Any(ms => ms.Name.Equals(modelSystemName, StringComparison.OrdinalIgnoreCase));
        }

        internal bool GiveOwnership(User newOwner, ref string error)
        {
            lock(ProjectLock)
            {
                var previousOwner = Owner;
                Owner = newOwner;
                previousOwner.RemovedUserForProject(this);
                // update the references to this project
                if (_AdditionalUsers.Contains(newOwner))
                {
                    _AdditionalUsers.Remove(newOwner);
                }
                else
                {
                    newOwner.AddedUserToProject(this);
                }
                return true;
            }
        }

        internal bool AddAdditionalUser(User toShareWith, ref string error)
        {
            lock (ProjectLock)
            {
                if (_AdditionalUsers.Contains(toShareWith))
                {
                    error = "The user already has access to this project.";
                    return false;
                }
                _AdditionalUsers.Add(toShareWith);
                toShareWith.AddedUserToProject(this);
                return true;
            }
        }

        internal bool RemoveAdditionalUser(User toRemove, ref string error)
        {
            lock (ProjectLock)
            {
                if (!_AdditionalUsers.Contains(toRemove))
                {
                    error = "The user already does not access to this project.";
                    return false;
                }
                _AdditionalUsers.Remove(toRemove);
                toRemove.RemovedUserForProject(this);
                return true;
            }
        }

        public static bool New(User owner, string name, string description, out Project project, ref string error)
        {
            project = new Project()
            {
                Name = name,
                Description = description,
                Owner = owner,
                ProjectFilePath = GetPath(owner, name)
            };
            return project.Save(ref error);
        }


        private static string GetPath(User owner, string name)
        {
            var dir = Path.Combine(owner.UserPath, name);
            var info = new DirectoryInfo(dir);
            if (!info.Exists)
            {
                info.Create();
            }
            return Path.Combine(dir, ProjectFile);
        }

        /// <summary>
        /// Save the project
        /// </summary>
        /// <param name="error"></param>
        /// <returns></returns>
        internal bool Save(ref string error)
        {
            var temp = Path.GetTempFileName();
            try
            {
                using (var tempFile = new StreamWriter(File.Create(temp)))
                {
                    using (var writer = new JsonTextWriter(tempFile))
                    {
                        writer.WriteStartObject();
                        writer.WritePropertyName("Name");
                        writer.WriteValue(Name);
                        writer.WritePropertyName("Description");
                        writer.WriteValue(Description);
                        writer.WritePropertyName("Owner");
                        writer.WriteValue(Owner.UserName);
                        if (_AdditionalUsers.Count > 0)
                        {
                            writer.WritePropertyName("AdditionalUsers");
                            writer.WriteStartArray();
                            foreach(var user in _AdditionalUsers)
                            {
                                writer.WriteValue(user.UserName);
                            }
                            writer.WriteEndArray();
                        }
                        writer.WritePropertyName("ModelSystemHeaders");
                        writer.WriteStartArray();
                        foreach (var ms in ModelSystems)
                        {
                            ms.Save(writer);
                        }
                        writer.WriteEndArray();
                        writer.WriteEndObject();
                    }
                }
                File.Copy(temp, ProjectFilePath, true);
                return true;
            }
            catch (IOException e)
            {
                error = e.Message;
                return false;
            }
            finally
            {
                // ensure we don't leak any data
                var tempFile = new FileInfo(temp);
                if (tempFile.Exists)
                {
                    tempFile.Delete();
                }
            }
        }

        /// <summary>
        /// Get weather a user has access to the given project.
        /// </summary>
        /// <param name="user">The user to test for</param>
        /// <returns>True if the user is allowed</returns>
        public bool CanAccess(User user)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            return user == Owner || _AdditionalUsers.Contains(user);
        }

        public bool SetName(ProjectSession session, string name, ref string error)
        {
            if (String.IsNullOrWhiteSpace(name))
            {
                error = "A name cannot be whitespace.";
                return false;
            }
            Name = name;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            return true;
        }

        public bool SetDescription(ProjectSession session, string description, ref string error)
        {
            Description = description;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description)));
            return true;
        }

        public bool Remove(ProjectSession session, ModelSystemHeader modelSystemHeader, ref string error)
        {
            if (modelSystemHeader == null)
            {
                throw new ArgumentNullException(nameof(modelSystemHeader));
            }
            if (_ModelSystems.Remove(modelSystemHeader))
            {
                return true;
            }
            error = "Unable to find the model system!";
            return false;
        }

        public bool Add(ProjectSession session, ModelSystemHeader modelSystemHeader, ref string error)
        {
            if (modelSystemHeader == null)
            {
                throw new ArgumentNullException(nameof(modelSystemHeader));
            }
            _ModelSystems.Add(modelSystemHeader);
            return true;
        }

        /// <summary>
        /// Add a model system to the project from a model system file.
        /// </summary>
        /// <param name="projectSession">The project session that the model system is going into.</param>
        /// <param name="modelSystemName">The name to use for the new model system.  It must be unique.</param>
        /// <param name="msf">The model system file to use.</param>
        /// <param name="header">A model system header to the newly imported model system.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise.</returns>
        internal bool AddModelSystemFromModelSystemFile(ProjectSession projectSession, string modelSystemName, 
            ModelSystemFile msf, out ModelSystemHeader header, ref string error)
        {
            header = null;
            if (projectSession is null)
            {
                throw new ArgumentNullException(nameof(projectSession));
            }

            if (msf is null)
            {
                throw new ArgumentNullException(nameof(msf));
            }
            if(ContainsModelSystem(modelSystemName))
            {
                error = "A model system with this name already exists!";
                return false;
            }
            var tempHeader = new ModelSystemHeader(this, modelSystemName, msf.Description);
            if(!msf.ExtractModelSystemTo(tempHeader.ModelSystemPath, ref error))
            {
                return false;
            }
            _ModelSystems.Add(tempHeader);
            header = tempHeader;
            return true;
        }
    }
}
