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
using XTMF2.Configuration;

namespace XTMF2
{
    public sealed class UserController
    {
        /// <summary>
        /// The users in the system.  Ensure you dereference the
        /// observable interface if you share this with other objects.
        /// </summary>
        public ReadOnlyObservableCollection<User> Users => new ReadOnlyObservableCollection<User>(_Users);

        private ObservableCollection<User> _Users;


        private SystemConfiguration SystemConfiguration;

        private object UserLock = new object();

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
                    new User(Path.Combine(SystemConfiguration.DefaultUserDirectory, userName), userName, true)
                };
            }
        }
    }
}
