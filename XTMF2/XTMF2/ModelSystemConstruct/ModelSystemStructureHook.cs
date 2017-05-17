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
using System.Reflection;
using System.Text;

namespace XTMF2
{
    /// <summary>
    /// Defines the places to connect links between model system structures.
    /// </summary>
    public abstract class ModelSystemStructureHook
    {
        public ModelSystemStructure Structure { get; private set; }

        public string Name { get; protected set; }

        public HookCardinality Cardinality { get; protected set; }

        public ModelSystemStructureHook(ModelSystemStructure structure)
        {
            Structure = structure;
        }
    }

    // Cardinality 
    public enum HookCardinality
    {
        Single,
        AtLeastOne,
        AnyNumber
    }

    /// <summary>
    /// A hook on the property of a model system structure
    /// </summary>
    sealed class PropertyHook : ModelSystemStructureHook
    {
        public PropertyHook(ModelSystemStructure structure) : base(structure)
        {
        }
    }

    /// <summary>
    /// A hook for the field of a model system structure
    /// </summary>
    sealed class FieldHook : ModelSystemStructureHook
    {
        public FieldHook(ModelSystemStructure structure) : base(structure)
        {
        }
    }
}
