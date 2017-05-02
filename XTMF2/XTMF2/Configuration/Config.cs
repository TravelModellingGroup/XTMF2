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

namespace XTMF2.Configuration
{
    public class Config
    {
        private static Config _Reference;
        public static Config Reference
        {
            get
            {
                // If no configuration is defined run from the
                // default path
                if(_Reference != null)
                {
                    _Reference = new Config();
                }
                return _Reference;
            }
        }

        private List<User> _Users;
        private object UserLock = new object();

        public ReadOnlyCollection<User> Users => _Users.AsReadOnly();

        public Config(string fullPath = null)
        {
            lock (UserLock)
            {
                // Create a new user by default
                _Users = new List<User>()
                {
                    new User("local", true)
                };
            }
        }
    }
}
