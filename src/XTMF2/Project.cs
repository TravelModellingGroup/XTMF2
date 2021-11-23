/*
    Copyright 2017-2021 University of Toronto

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
using XTMF2.Editing;
using XTMF2.Controllers;
using System.Linq;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;

namespace XTMF2
{
    /// <summary>
    /// A collection of model systems with access rights
    /// for users.
    /// </summary>
    public sealed class Project : INotifyPropertyChanged
    {
        private const string ProjectFile = "Project.xpjt";
        private const string NameProperty = "Name";
        private const string DescriptionProperty = "Description";
        private const string ModelSystemHeadersProperty = "ModelSystemHeaders";
        private const string OwnerProperty = "Owner";
        private const string AdditionalUsersProperty = "AdditionalUsers";
        private const string CustomRunDirectoryProperty = "CustomRunDirectory";
        private const string AlternativeRunDirectories = "AlternativeRunDirectories";

        public string? Name { get; private set; }
        public string? Description { get; private set; }
        public string? ProjectFilePath { get; private set; }
        public string? ProjectDirectory => Path.GetDirectoryName(ProjectFilePath);
        public User? Owner { get; private set; }
        public ReadOnlyCollection<User> AdditionalUsers => new ReadOnlyCollection<User>(_AdditionalUsers);
        private readonly ObservableCollection<User> _AdditionalUsers = new ObservableCollection<User>();
        private readonly ObservableCollection<ModelSystemHeader> _ModelSystems = new ObservableCollection<ModelSystemHeader>();

        public ReadOnlyObservableCollection<ModelSystemHeader> ModelSystems => new ReadOnlyObservableCollection<ModelSystemHeader>(_ModelSystems);

        private readonly ObservableCollection<string> _additionalPreviousRunDirectories = new ObservableCollection<string>();
        public ReadOnlyObservableCollection<string> AdditionalPreviousRunDirectories => new ReadOnlyObservableCollection<string>(_additionalPreviousRunDirectories);

        public string? RunsDirectory => HasCustomRunDirectory && _customRunDirectory != null ? _customRunDirectory : DefaultRunDirectory;
        private string? DefaultRunDirectory => Path.Combine(ProjectDirectory!, "Runs");

        public bool HasCustomRunDirectory => !(_customRunDirectory is null);

        private string? _customRunDirectory = null;

        private readonly object _projectLock = new object();

        public event PropertyChangedEventHandler? PropertyChanged;

        private Project()
        {
        }

        internal static bool Load(UserController userController, string filePath, [NotNullWhen(true)] out Project? project, [NotNullWhen(false)] ref string? error)
        {
            project = new Project()
            {
                ProjectFilePath = filePath
            };
            try
            {
                byte[] buffer = File.ReadAllBytes(filePath);
                var reader = new Utf8JsonReader(buffer.AsSpan());
                {
                    while (reader.Read())
                    {
                        if (reader.TokenType == JsonTokenType.PropertyName)
                        {
                            if (reader.ValueTextEquals(NameProperty))
                            {
                                reader.Read();
                                project.Name = reader.GetString();
                            }
                            else if (reader.ValueTextEquals(DescriptionProperty))
                            {
                                reader.Read();
                                project.Description = reader.GetString();
                            }
                            else if (reader.ValueTextEquals(ModelSystemHeadersProperty))
                            {
                                reader.Read();
                                if (reader.TokenType != JsonTokenType.StartArray)
                                {
                                    error = "We expected a start of array but found a " + Enum.GetName(typeof(JsonTokenType), reader.TokenType);
                                    return false;
                                }
                                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                                {
                                    project._ModelSystems.Add(ModelSystemHeader.Load(project, ref reader));
                                }
                            }
                            else if (reader.ValueTextEquals(OwnerProperty))
                            {
                                reader.Read();
                                if (reader.TokenType != JsonTokenType.String)
                                {
                                    error = "We expected a string token but found a " + Enum.GetName(typeof(JsonTokenType), reader.TokenType);
                                    return false;
                                }
                                var user = userController.GetUserByName(reader.GetString()!);
                                project.Owner = user;
                                user?.AddedUserToProject(project);
                            }
                            else if (reader.ValueTextEquals(AdditionalUsersProperty))
                            {
                                reader.Read();
                                if (reader.TokenType != JsonTokenType.StartArray)
                                {
                                    throw new Exception("Expected Start Array while loading project!");
                                }
                                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                                {
                                    if (reader.TokenType != JsonTokenType.String)
                                    {
                                        error = "We expected a string token but found a " + Enum.GetName(typeof(JsonTokenType), reader.TokenType);
                                        return false;
                                    }
                                    var user = userController.GetUserByName(reader.GetString()!);
                                    // If we are unable to find the user, silently ignore.
                                    if(!(user is null))
                                    {
                                        project._AdditionalUsers.Add(user);
                                        user.AddedUserToProject(project);
                                    }
                                }
                            }
                            else if (reader.ValueTextEquals(CustomRunDirectoryProperty))
                            {
                                reader.Read();
                                if (reader.TokenType == JsonTokenType.String)
                                {
                                    project._customRunDirectory = reader.GetString()!;
                                }
                            }
                            else if (reader.ValueTextEquals(AlternativeRunDirectories))
                            {
                                reader.Read();
                                if (reader.TokenType == JsonTokenType.StartArray)
                                {
                                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                                    {
                                        if (reader.TokenType == JsonTokenType.String)
                                        {
                                            project._additionalPreviousRunDirectories.Add(reader.GetString()!);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                reader.Skip();
                            }
                        }
                    }
                }
                if (project.Owner == null)
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

        internal static bool Load(ProjectFile projectFile, string projectName, User owner, [NotNullWhen(true)] out Project? project, [NotNullWhen(false)] out CommandError? error)
        {
            bool deleteProject = true;
            Project? toReturn = null;
            try
            {
                project = null;
                if (!New(owner, projectName, projectFile.Description, out toReturn, out error))
                {
                    return false;
                }
                foreach (var msf in projectFile.ModelSystems)
                {
                    if (!toReturn!.AddModelSystemFromModelSystemFile(msf.Name, msf, out var _, out error))
                    {
                        return false;
                    }
                }
                project = toReturn;
                deleteProject = false;
                return true;
            }
            finally
            {
                if (deleteProject && (toReturn is not null))
                {
                    // If we fail when deleting the project it is alright.
                    toReturn.Delete(out var _);
                }
            }
        }

        /// <summary>
        /// Delete the project.  This should only be called from the project controller
        /// unless the project has never been added to the project controller.
        /// </summary>
        internal bool Delete([NotNullWhen(false)] out CommandError? error)
        {
            try
            {
                var directory = new DirectoryInfo(ProjectDirectory!);
                if (directory.Exists)
                {
                    directory.Delete(true);
                }
                error = null;
                return true;
            }
            catch (IOException e)
            {
                error = new CommandError(e.Message);
                return false;
            }
        }

        internal bool GetModelSystemHeader(string modelSystemName, [NotNullWhen(true)] out ModelSystemHeader? modelSystemHeader, [NotNullWhen(false)] out CommandError? error)
        {
            modelSystemHeader = _ModelSystems.FirstOrDefault(msh => msh.Name.Equals(modelSystemName, StringComparison.OrdinalIgnoreCase));
            if (modelSystemHeader == null)
            {
                error = new CommandError("A model system with the given name was not found!");
                return false;
            }
            error = null;
            return true;
        }

        internal bool ContainsModelSystem(ModelSystemHeader modelSystemHeader)
        {
            return _ModelSystems.Contains(modelSystemHeader);
        }

        internal bool ContainsModelSystem(string modelSystemName)
        {
            return _ModelSystems.Any(ms => ms.Name.Equals(modelSystemName, StringComparison.OrdinalIgnoreCase));
        }

        private static bool SaveOrError(Project project, [NotNullWhen(false)] out CommandError? error)
        {
            string? errorString = null;
            if (!project.Save(ref errorString))
            {
                error = new CommandError(errorString ?? $"Unknown error when trying to save the project {project.Name}!");
                return false;
            }
            else
            {
                error = null;
                return true;
            }
        }

        private bool SaveOrError([NotNullWhen(false)] out CommandError? error)
        {
            return SaveOrError(this, out error);
        }

        internal bool GiveOwnership(User newOwner, [NotNullWhen(false)] out CommandError? error)
        {
            lock (_projectLock)
            {
                var previousOwner = Owner!;
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
                return SaveOrError(out error);
            }
        }

        internal bool AddAdditionalUser(User toShareWith, [NotNullWhen(false)] out CommandError? error)
        {
            lock (_projectLock)
            {
                if (_AdditionalUsers.Contains(toShareWith))
                {
                    error = new CommandError("The user already has access to this project.");
                    return false;
                }
                _AdditionalUsers.Add(toShareWith);
                toShareWith.AddedUserToProject(this);
                return SaveOrError(out error);
            }
        }

        internal bool RemoveAdditionalUser(User toRemove, [NotNullWhen(false)] out CommandError? error)
        {
            lock (_projectLock)
            {
                if (!_AdditionalUsers.Contains(toRemove))
                {
                    error = new CommandError("The user already does not access to this project.");
                    return false;
                }
                _AdditionalUsers.Remove(toRemove);
                toRemove.RemovedUserForProject(this);
                return SaveOrError(out error);
            }
        }

        internal static bool New(User owner, string name, string description, [NotNullWhen(true)] out Project? project, [NotNullWhen(false)] out CommandError? error)
        {
            project = new Project()
            {
                Name = name,
                Description = description,
                Owner = owner,
                ProjectFilePath = GetPath(owner, name)
            };
            return SaveOrError(project, out error);
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
        internal bool Save([NotNullWhen(false)] ref string? error)
        {
            var temp = Path.GetTempFileName();
            try
            {
                using (var tempFile = File.Create(temp))
                {
                    using var writer = new Utf8JsonWriter(tempFile);
                    writer.WriteStartObject();
                    writer.WriteString(NameProperty, Name);
                    writer.WriteString(DescriptionProperty, Description);
                    writer.WriteString(OwnerProperty, Owner!.UserName);
                    if (_AdditionalUsers.Count > 0)
                    {
                        writer.WritePropertyName(AdditionalUsersProperty);
                        writer.WriteStartArray();
                        foreach (var user in _AdditionalUsers)
                        {
                            writer.WriteStringValue(user.UserName);
                        }
                        writer.WriteEndArray();
                    }
                    writer.WriteStartArray(ModelSystemHeadersProperty);
                    foreach (var ms in ModelSystems)
                    {
                        ms.Save(writer);
                    }
                    writer.WriteEndArray();
                    if (HasCustomRunDirectory)
                    {
                        writer.WriteString(CustomRunDirectoryProperty, _customRunDirectory);
                    }
                    if (_additionalPreviousRunDirectories.Count > 0)
                    {
                        writer.WriteStartArray(AlternativeRunDirectories);
                        foreach (var path in _additionalPreviousRunDirectories)
                        {
                            writer.WriteStringValue(path);
                        }
                        writer.WriteEndArray();
                    }
                    writer.WriteEndObject();
                }
                File.Copy(temp, ProjectFilePath!, true);
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
        internal bool CanAccess(User user)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            return user == Owner || _AdditionalUsers.Contains(user);
        }

        internal bool SetName(string name, [NotNullWhen(false)] out CommandError? error)
        {
            if (String.IsNullOrWhiteSpace(name))
            {
                error = new CommandError("A name cannot be whitespace.");
                return false;
            }
            var oldName = Name;
            try
            {
                var userPath = Owner!.UserPath;
                var dir = new DirectoryInfo(ProjectDirectory!);
                dir.MoveTo(Path.Combine(userPath, name));
                Name = name;
                ProjectFilePath = Path.Combine(userPath, name, ProjectFile);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
                return SaveOrError(out error);
            }
            catch (IOException e)
            {
                error = new CommandError(e.Message);
                return false;
            }
        }

        internal bool SetDescription(ProjectSession session, string description, [NotNullWhen(false)] out CommandError? error)
        {
            Description = description;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description)));
            error = null;
            return true;
        }

        internal bool Remove(ProjectSession session, ModelSystemHeader modelSystemHeader, [NotNullWhen(false)] out CommandError? error)
        {
            if (modelSystemHeader == null)
            {
                throw new ArgumentNullException(nameof(modelSystemHeader));
            }
            if (_ModelSystems.Remove(modelSystemHeader))
            {
                return SaveOrError(out error);
            }
            error = new CommandError("Unable to find the model system!");
            return false;
        }

        internal bool Add(ProjectSession session, ModelSystemHeader modelSystemHeader, [NotNullWhen(false)] out CommandError? error)
        {
            if (modelSystemHeader == null)
            {
                throw new ArgumentNullException(nameof(modelSystemHeader));
            }
            _ModelSystems.Add(modelSystemHeader);
            return SaveOrError(out error);
        }

        internal bool RenameModelSystem(ModelSystemHeader modelSystem, string newName, [NotNullWhen(false)] out CommandError? error)
        {
            if (_ModelSystems.Any(ms => newName.Equals(ms.Name, StringComparison.InvariantCultureIgnoreCase)))
            {
                error = new CommandError($"A model system with the name '{newName}' already exists in the project.");
                return false;
            }
            return modelSystem.SetName(newName, out error);
        }

        /// <summary>
        /// Add a model system to the project from a model system file.
        /// </summary>
        /// <param name="modelSystemName">The name to use for the new model system.  It must be unique.</param>
        /// <param name="msf">The model system file to use.</param>
        /// <param name="header">A model system header to the newly imported model system.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise.</returns>
        internal bool AddModelSystemFromModelSystemFile(string modelSystemName,
            ModelSystemFile msf, [NotNullWhen(true)] out ModelSystemHeader? header, [NotNullWhen(false)] out CommandError? error)
        {
            header = null;
            if (msf is null)
            {
                throw new ArgumentNullException(nameof(msf));
            }
            if (ContainsModelSystem(modelSystemName))
            {
                error = new CommandError("A model system with this name already exists!");
                return false;
            }
            var tempHeader = new ModelSystemHeader(this, modelSystemName, msf.Description);
            if (!msf.ExtractModelSystemTo(tempHeader.ModelSystemPath, out error))
            {
                return false;
            }
            _ModelSystems.Add(tempHeader);
            header = tempHeader;
            return SaveOrError(out error);
        }

        internal bool AddAlternativePastRunDirectory(string pastRunDirectory, out CommandError? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(pastRunDirectory))
            {
                error = new CommandError("Invalid Past Run Directory");
                return false;
            }
            try
            {
                // Make sure that we can actually use this directory
                var dir = new DirectoryInfo(pastRunDirectory);
                if (!dir.Exists)
                {
                    error = new CommandError($"The alternative run directory '{pastRunDirectory}' does not exist!");
                    return false;
                }
            }
            catch (ArgumentException e)
            {
                error = new CommandError($"Unable to add '{pastRunDirectory}' for the alternative past runs directory. {e.Message}");
                return false;
            }
            catch (IOException e)
            {
                error = new CommandError($"Unable to add '{pastRunDirectory}' for the alternative past runs directory. {e.Message}");
                return false;
            }
            _additionalPreviousRunDirectories.Add(pastRunDirectory);
            return true;
        }

        internal bool RemoveAlternativePastRunDirectory(string pastRunDirectory, out CommandError? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(pastRunDirectory))
            {
                error = new CommandError("Invalid Past Run Directory");
                return false;
            }
            var index = _additionalPreviousRunDirectories.IndexOf(pastRunDirectory);
            if (index < 0)
            {
                error = new CommandError($"Unable to find a past run directory '{pastRunDirectory}' to remove!");
                return false;
            }
            _additionalPreviousRunDirectories.RemoveAt(index);
            return true;
        }

        internal bool SetCustomRunsDirectory(string runDirectory, out CommandError? error)
        {
            var alreadyCustom = HasCustomRunDirectory;
            try
            {
                // Make sure that we can actually use this directory
                var dir = new DirectoryInfo(runDirectory);
                if (!dir.Exists)
                {
                    dir.Create();
                }
            }
            catch (ArgumentException e)
            {
                error = new CommandError($"Unable to use '{runDirectory}' for the custom runs directory. {e.Message}");
                return false;
            }
            catch (IOException e)
            {
                error = new CommandError($"Unable to use '{runDirectory}' for the custom runs directory. {e.Message}");
                return false;
            }
            _customRunDirectory = runDirectory;
            if (!alreadyCustom)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasCustomRunDirectory)));
            }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RunsDirectory)));
            return SaveOrError(out error);
        }

        internal bool ResetRunsDirectory(out CommandError? error)
        {
            var alreadyCustom = HasCustomRunDirectory;
            try
            {
                // Make sure that we can actually use this directory
                var dir = new DirectoryInfo(DefaultRunDirectory!);
                if (!dir.Exists)
                {
                    dir.Create();
                }
            }
            catch (IOException e)
            {
                error = new CommandError($"Unable to use '{DefaultRunDirectory}' for the runs directory. {e.Message}");
                return false;
            }
            _customRunDirectory = null;
            if (alreadyCustom)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasCustomRunDirectory)));
            }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RunsDirectory)));
            return SaveOrError(out error);
        }
    }
}
