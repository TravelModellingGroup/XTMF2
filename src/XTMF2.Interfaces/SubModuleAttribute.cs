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
using System.Text;

namespace XTMF2
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class SubModuleAttribute : Attribute
    {
        public SubModuleAttribute()
        {
            // Set the index to -1 to ensure it is defined
            Index = -1;
        }

        /// <summary>
        /// The name of the submodule / Parameter.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// The description of the submodule / Parameter.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Whether the submodule / Parameter is required.
        /// </summary>
        public bool Required { get; set; }

        /// <summary>
        /// Whether the submodule / Parameter passes execution if false then the submodule is considered input, if true then we either pass execution to the submodule or it is output.
        /// </summary>
        public bool PassesExecution { get; set; }

        /// <summary>
        /// The index of the submodule / Parameter within the module.  This must be unique within the module.
        /// </summary>
        public int Index { get; set; }
    }
}
