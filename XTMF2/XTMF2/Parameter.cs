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
using System.ComponentModel;
using System.Text;

namespace XTMF2
{
    /// <summary>
    /// A name and value that gets bound to a ParameterHook and thusly to a
    /// model system structure.
    /// </summary>
    public abstract class Parameter : INotifyPropertyChanged
    {
        /// <summary>
        /// 
        /// </summary>
        public string Name { get; private set; }

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        /// <param name="error"></param>
        /// <returns>True if the operation was successful, false otherwise</returns>
        public bool SetName(string name, ref string error)
        {
            if(!ValidateName(name, ref error))
            {
                return false;
            }
            Name = name;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        public string Value { get; private set; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="newValue"></param>
        /// <param name="error"></param>
        /// <returns>True if the operation was successful, false otherwise</returns>
        public bool SetValue (string newValue, ref string error)
        {
            if (!ValidateName(newValue, ref error))
            {
                return false;
            }
            Value = newValue;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            return true;
        }

        /// <summary>
        /// Validate the change of a name against
        /// </summary>
        /// <param name="newValue">The name to change to</param>
        /// <param name="error"></param>
        /// <returns>True if the value is acceptable, false otherwise</returns>
        protected abstract bool ValidateName(string newValue, ref string error);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="newValue"></param>
        /// <param name="error"></param>
        /// <returns>True if the value is acceptable, false otherwise</returns>
        protected abstract bool ValidateValue(string newValue, ref string error);
    }
}
