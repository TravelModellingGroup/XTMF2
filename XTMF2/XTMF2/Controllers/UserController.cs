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

namespace XTMF2.Controller
{
    public sealed class UserController
    {
        private ObservableCollection<User> _Users;


        private SystemConfiguration SystemConfiguration;

        private object UserLock = new object();

        /// <summary>
        /// The users in the system.  Ensure you dereference the
        /// observable interface if you share this with other objects.
        /// </summary>
        public ReadOnlyObservableCollection<User> Users => new ReadOnlyObservableCollection<User>(_Users);

        public bool CreateNew(string userName, bool admin, out User user, ref string error)
        {
            user = null;
            if (!ValidateUserName(userName))
            {
                error = "Invalid name for a user.";
                return false;
            }
            lock (UserLock)
            {
                //ensure there is no other user with the same name
                if(_Users.Any(u => u.UserName.Equals(userName, StringComparison.OrdinalIgnoreCase)))
                {
                    error = "A user with this name already exists.";
                    return false;
                }
                _Users.Add(user = new User(GetUserPath(userName), userName, admin));
                return true;
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
                var foundUser = _Users.FirstOrDefault(user => user.UserName.Equals(userName, StringComparison.OrdinalIgnoreCase));
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
            var projectController = XTMFRuntime.Reference.ProjectController;
            lock (UserLock)
            {

                var userProjects = user.AvailableProjects;
                foreach (var toDelete in (from p in userProjects
                                        where user == p.Owner
                                        select p ).ToList())
                {
                    string error = null;
                    projectController.DeleteProject(user, toDelete, ref error);
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


        public UserController(SystemConfiguration configuration)
        {
            SystemConfiguration = configuration;
            LoadUsers();
        }

        private void LoadUsers()
        {
            lock (UserLock)
            {
                var userName = "local";
                // Create a new user by default
                _Users = new ObservableCollection<User>()
                {
                    new User(GetUserPath(userName), userName, true)
                };
            }
        }

        private string GetUserPath(string userName)
        {
            return Path.Combine(SystemConfiguration.DefaultUserDirectory, userName);
        }
    }
}
