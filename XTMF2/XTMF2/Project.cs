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
using System.Text;
using XTMF2.Editing;

namespace XTMF2
{
    public sealed class Project : INotifyPropertyChanged
    {
        public string Name { get; private set; }
        public string Description { get; private set; }
        public string Path { get; private set; }
        public User Owner { get; private set; }
        ObservableCollection<ModelSystemHeader> _ModelSystems = new ObservableCollection<ModelSystemHeader>();

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Get weather a user has access to the given project.
        /// </summary>
        /// <param name="user">The user to test for</param>
        /// <returns>True if the user is allowed</returns>
        public bool CanAccess(User user)
        {
            if(user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            // TODO: Implement a real check
            return true;
        }

        public ReadOnlyObservableCollection<ModelSystemHeader> ModelSystems => new ReadOnlyObservableCollection<ModelSystemHeader>(_ModelSystems);

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
    }
}
