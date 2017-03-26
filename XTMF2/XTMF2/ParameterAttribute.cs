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

namespace XTMF2
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class ParameterAttribute : Attribute
    {
        public string Name { get; private set; }

        public string DefaultValue { get; private set; }

        public string Description { get; private set; }

        public ParameterAttribute(string name, string defaultValue, string description)
        {
            Name = name;
            DefaultValue = defaultValue;
            Description = description;
        }
    }
}
