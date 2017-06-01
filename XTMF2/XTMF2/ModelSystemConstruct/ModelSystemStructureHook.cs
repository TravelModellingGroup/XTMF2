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
        public string Name { get; private set; }

        public HookCardinality Cardinality { get; private set; }

        public int Index { get; private set; }

        public ModelSystemStructureHook(string name, HookCardinality cardinality, int index)
        {
            Name = name;
            Cardinality = cardinality;
            Index = index;
        }

        protected static HookCardinality GetCardinality(Type type, bool required)
        {
            if (type.IsArray)
            {
                return required ? HookCardinality.AtLeastOne : HookCardinality.AnyNumber;
            }
            if (typeof(IModule).GetTypeInfo().IsAssignableFrom(type))
            {
                return HookCardinality.Single;
            }
            return HookCardinality.Single;
        }

        /// <summary>
        /// Both origin and destination must be already created!
        /// </summary>
        /// <param name="origin"></param>
        /// <param name="destination"></param>
        internal abstract void Install(ModelSystemStructure origin, ModelSystemStructure destination, int index);

        /// <summary>
        /// Create the array of data with the given size
        /// </summary>
        /// <param name="length">The number of modules that will be installed</param>
        internal abstract void CreateArray(IModule origin, int length);
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
        readonly PropertyInfo Property;
        public PropertyHook(string name, PropertyInfo property, bool required, int index)
            : base(name, GetCardinality(property, required), index)
        {
            Property = property;
        }

        private static HookCardinality GetCardinality(PropertyInfo property, bool required)
        {
            return ModelSystemStructureHook.GetCardinality(property.PropertyType, required);
        }

        internal override void CreateArray(IModule origin, int length)
        {
            Property.SetValue(origin, Array.CreateInstance(Property.PropertyType.GetElementType(), length));
        }

        internal override void Install(ModelSystemStructure origin, ModelSystemStructure destination, int index)
        {
            switch (Cardinality)
            {
                case HookCardinality.Single:
                    {
                        Property.SetValue(origin.Module, destination.Module);
                    }
                    break;
                case HookCardinality.AnyNumber:
                case HookCardinality.AtLeastOne:
                    {
                        // the type is an array
                        var data = (Array)Property.GetValue(origin.Module);
                        data.SetValue(destination.Module, index);
                    }
                    break;
                default:
                    throw new NotImplementedException("Unknown Cardinality!");
            }
        }
    }

    /// <summary>
    /// A hook for the field of a model system structure
    /// </summary>
    sealed class FieldHook : ModelSystemStructureHook
    {
        readonly FieldInfo Field;
        public FieldHook(string name, FieldInfo field, bool required, int index)
            : base(name, GetCardinality(field, required), index)
        {
            Field = field;
        }

        private static HookCardinality GetCardinality(FieldInfo field, bool required)
        {
            return ModelSystemStructureHook.GetCardinality(field.FieldType, required);
        }

        internal override void CreateArray(IModule origin, int length)
        {
            Field.SetValue(origin, Array.CreateInstance(Field.FieldType.GetElementType(), length));
        }

        internal override void Install(ModelSystemStructure origin, ModelSystemStructure destination, int index)
        {
            switch (Cardinality)
            {
                case HookCardinality.Single:
                    {
                        Field.SetValue(origin.Module, destination.Module);
                    }
                    break;
                case HookCardinality.AnyNumber:
                case HookCardinality.AtLeastOne:
                    {
                        // the type is an array
                        var data = (Array)Field.GetValue(origin.Module);
                        data.SetValue(destination.Module, index);
                    }
                    break;
                default:
                    throw new NotImplementedException("Unknown Cardinality!");
            }
        }
    }
}
