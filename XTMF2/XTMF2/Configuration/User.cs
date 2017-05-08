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
using System.Text;
using System.Linq;

namespace XTMF2
{
    public sealed class User
    {
        public Guid UserId { get; set; }

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

        public User(Guid userId, string userName, bool admin = false)
        {
            UserId = userId;
            UserName = userName;
            Admin = admin;
        }

        internal void AddedUserToProject(Project p)
        {
            if(p == null)
            {
                throw new ArgumentNullException(nameof(p));
            }
            lock (ProjectLock)
            {
                _AvailableProjects.Add(p);
            }
        }

        /// <summary>
        /// Checks to see if a user has a project with the given name already defined and is the owner.
        /// </summary>
        /// <param name="name">The name of the project</param>
        /// <returns>True if there is already a project defined with the name and is the owner.</returns>
        internal bool HasProjectWithName(string name)
        {
            lock(ProjectLock)
            {
                return _AvailableProjects.Any(p => p.Owner == this && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            }
        }
    }
}
