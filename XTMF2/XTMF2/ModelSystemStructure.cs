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
using System.ComponentModel;
using System.Collections.Generic;

namespace XTMF2
{
    /// <summary>
    /// The basic building block of a model system
    /// </summary>
    public class ModelSystemStructure : INotifyPropertyChanged
    {
        /// <summary>
        /// The type that this will represent
        /// </summary>
        public Type Type { get; private set; }

        /// <summary>
        /// The name of the model system structure
        /// </summary>
        public string Name { get; private set; }

        public Point Location { get; private set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public void SetLocation(float x, float y)
        {
            Location = new Point()
            {
                X = x,
                Y = y
            };
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Location)));
        }

        internal ModelSystemStructure Clone()
        {
            return (ModelSystemStructure)MemberwiseClone();
        }

        /// <summary>
        /// Change the name of the model system structure
        /// </summary>
        /// <param name="name">The name to change it to</param>
        /// <param name="error">A description of the error if one occurs</param>
        /// <returns>True if the operation was successful, false otherwise</returns>
        public bool SetName(string name, ref string error)
        {
            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        public string Description { get; private set; }

        /// <summary>
        /// Change the name of the model system structure
        /// </summary>
        /// <param name="description">The description to change to</param>
        /// <param name="error">A description of the error if one occurs</param>
        /// <returns>True if the operation was successful, false otherwise</returns>
        public bool SetDescription(string description, ref string error)
        {
            return false;
        }

        public ModelSystemStructure(Type t)
        {
            Type = t;
            Name = GetName(t);
        }

        /// <summary>
        /// Get a default name from the type
        /// </summary>
        /// <param name="type">The type to derive the name from</param>
        /// <returns>True if the operation was successful, false otherwise</returns>
        private static string GetName(Type type)
        {
            if(type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }
            return type.Name;
        }
    }
}
