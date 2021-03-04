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
using System.Linq;
using System.Text;
using XTMF2.Configuration;
using XTMF2.Editing;

namespace XTMF2.Controllers
{
    public sealed class UserController
    {
        private ObservableCollection<User> _users = new ObservableCollection<User>();

        private XTMFRuntime Runtime;
        private SystemConfiguration SystemConfiguration => Runtime.SystemConfiguration;
        private ProjectController ProjectController => Runtime.ProjectController;

        private object UserLock = new object();

        /// <summary>
        /// The users in the system.  Ensure you dereference the
        /// observable interface if you share this with other objects.
        /// </summary>
        public ReadOnlyObservableCollection<User> Users => new ReadOnlyObservableCollection<User>(_users);

        /// <summary>
        /// Create a new user with the given name.  Names must be unique.
        /// </summary>
        /// <param name="userName">The name to create the user with.</param>
        /// <param name="admin">Should the user have administrative permissions.</param>
        /// <param name="user">The resulting user, null if the operation fails.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        public bool CreateNew(string userName, bool admin, out User? user, out CommandError? error)
        {
            user = null;
            if (!ValidateUserName(userName))
            {
                error = new CommandError("Invalid name for a user.");
                return false;
            }
            lock (UserLock)
            {
                //ensure there is no other user with the same name
                if(_users.Any(u => u.UserName.Equals(userName, StringComparison.OrdinalIgnoreCase)))
                {
                    error = new CommandError("A user with this name already exists.");
                    return false;
                }
                _users.Add(user = new User(GetUserPath(userName), userName, admin));
                return user.Save(out error);
            }
        }

        /// <summary>
        /// Delete a user given their user name
        /// </summary>
        /// <param name="userName">The user to delete</param>
        /// <returns>If the delete succeeds</returns>
        public bool Delete(string userName)
        {
            if (userName == null)
            {
                throw new ArgumentNullException(nameof(userName));
            }
            lock (UserLock)
            {
                var foundUser = _users.FirstOrDefault(user => user.UserName.Equals(userName, StringComparison.OrdinalIgnoreCase));
                if (foundUser != null)
                {
                    return Delete(foundUser);
                }
                return foundUser != null;
            }
        }

        /// <summary>
        /// Delete a user given a reference to them
        /// </summary>
        /// <param name="user"></param>
        /// <returns>True if the user was deleted</returns>
        public bool Delete(User user)
        {
            if(user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            var projectController = ProjectController;
            lock (UserLock)
            {

                var userProjects = user.AvailableProjects;
                // make a copy of the projects to avoid altering a list
                // that is being enumerated
                foreach (var toDelete in (from p in userProjects
                                        where user == p.Owner
                                        select p ).ToList())
                {
                    projectController.DeleteProject(user, toDelete, out var error);
                }
                _users.Remove(user);
                // now remove all of the users files from the system.
                var userDir = new DirectoryInfo(user.UserPath);
                if(userDir.Exists)
                {
                    userDir.Delete(true);
                }
                return true;
            }
        }

        private static char[] InvalidCharacters =
                Path.GetInvalidPathChars().Union(Path.GetInvalidFileNameChars()).ToArray();

        /// <summary>
        /// Ensure that a project name does not contain
        /// invalid characters
        /// </summary>
        /// <param name="name">The name to validate</param>
        /// <returns>If the validation allows this project name.</returns>
        private static bool ValidateUserName(string name)
        {
            return !name.Any(c => InvalidCharacters.Contains(c));
        }

        /// <summary>
        /// Get a reference to the user with the given name.
        /// </summary>
        /// <param name="userName">The name of the user to get the reference for.</param>
        /// <returns>A reference to the user to find.  Null if the user does not exist.</returns>
        public User? GetUserByName(string userName)
        {
            lock (UserLock)
            {
                return _users.FirstOrDefault(user => user.UserName.Equals(userName, StringComparison.OrdinalIgnoreCase));
            }
        }

        internal UserController(XTMFRuntime runtime)
        {
            Runtime = runtime;
            LoadUsers();
        }

        private void LoadUsers()
        {
            var usersDir = new DirectoryInfo(SystemConfiguration.DefaultUserDirectory);
            if (!usersDir.Exists)
            {
                // if we need to create the directory then there are no users in the system
                usersDir.Create();
                CreateInitialUser();
                return;
            }
            else
            {
                // if the directory exists load it
                lock (UserLock)
                {
                    foreach(var potentialDir in usersDir.GetDirectories())
                    {
                        var userFile = potentialDir.GetFiles("User.xusr").FirstOrDefault();
                        if(userFile != null)
                        {
                            string? error = null;
                            if(User.Load(userFile.FullName, out var loadedUser, ref error))
                            {
                                _users.Add(loadedUser!);
                            }
                        }
                    }
                }
            }
            // if we have no users create a default user
            if(_users.Count <= 0)
            {
                lock(UserLock)
                {
                    CreateInitialUser();
                }
            }
        }

        private void CreateInitialUser()
        {
            var userName = "local";
            // Create a new user by default
            var firstUser = new User(GetUserPath(userName), userName, true);
            _users.Add(firstUser);
            firstUser.Save(out var _);
            return;
        }

        private string GetUserPath(string userName)
        {
            return Path.Combine(SystemConfiguration.DefaultUserDirectory, userName);
        }
    }
}
