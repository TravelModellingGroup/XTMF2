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
    /// Defines the places to connect links between nodes.
    /// </summary>
    public abstract class NodeHook
    {
        public string Name { get; private set; }

        public HookCardinality Cardinality { get; private set; }

        public int Index { get; private set; }

        /// <summary>
        /// Is the hook a parameter?
        /// </summary>
        public bool IsParameter { get; private set; }

        /// <summary>
        /// The type of the hook.
        /// </summary>
        public abstract Type Type { get; }

        /// <summary>
        /// The default value of the parameter
        /// </summary>
        public string? DefaultValue;

        public NodeHook(string name, HookCardinality cardinality, int index, bool isParameter, string? defaultValue)
        {
            Name = name;
            Cardinality = cardinality;
            Index = index;
            IsParameter = isParameter;
            DefaultValue = defaultValue;
        }

        protected static HookCardinality GetCardinality(Type type, bool required)
        {
            if (type.IsArray)
            {
                return required ? HookCardinality.AtLeastOne : HookCardinality.AnyNumber;
            }
            // If it is a single link
            if (typeof(IModule).GetTypeInfo().IsAssignableFrom(type))
            {
                return required ? HookCardinality.Single : HookCardinality.SingleOptional;
            }
            return required ? HookCardinality.Single : HookCardinality.SingleOptional;
        }

        /// <summary>
        /// Both origin and destination must be already created!
        /// </summary>
        /// <param name="origin"></param>
        /// <param name="destination"></param>
        internal abstract void Install(Node origin, Node destination, int index);

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
        SingleOptional,
        AtLeastOne,
        AnyNumber
    }

    /// <summary>
    /// A hook on the property of a node
    /// </summary>
    sealed class PropertyHook : NodeHook
    {
        readonly PropertyInfo Property;
        public PropertyHook(string name, PropertyInfo property, bool required, int index, bool isParameter, string? defaultValue)
            : base(name, GetCardinality(property, required), index, isParameter, defaultValue)
        {
            Property = property;
        }

        public override Type Type => Property.PropertyType;

        private static HookCardinality GetCardinality(PropertyInfo property, bool required)
        {
            return NodeHook.GetCardinality(property.PropertyType, required);
        }

        internal override void CreateArray(IModule origin, int length)
        {
            Property.SetValue(origin, Array.CreateInstance(Property.PropertyType.GetElementType()!, length));
        }

        internal override void Install(Node origin, Node destination, int index)
        {
            switch (Cardinality)
            {
                case HookCardinality.Single:
                case HookCardinality.SingleOptional:
                    {
                        Property.SetValue(origin.Module, destination.Module);
                    }
                    break;
                case HookCardinality.AnyNumber:
                case HookCardinality.AtLeastOne:
                    {
                        // the type is an array
                        if (Property.GetValue(origin.Module) is Array data)
                        {
                            data.SetValue(destination.Module, index);
                        }
                    }
                    break;
                default:
                    throw new NotImplementedException("Unknown Cardinality!");
            }
        }
    }

    /// <summary>
    /// A hook for the field of a node
    /// </summary>
    sealed class FieldHook : NodeHook
    {
        readonly FieldInfo Field;
        public FieldHook(string name, FieldInfo field, bool required, int index, bool isParameter, string? defaultValue)
            : base(name, GetCardinality(field, required), index, isParameter, defaultValue)
        {
            Field = field;
        }

        public override Type Type => Field.FieldType;

        private static HookCardinality GetCardinality(FieldInfo field, bool required)
        {
            return NodeHook.GetCardinality(field.FieldType, required);
        }

        internal override void CreateArray(IModule origin, int length)
        {
            Field.SetValue(origin, Array.CreateInstance(Field.FieldType.GetElementType()!, length));
        }

        internal override void Install(Node origin, Node destination, int index)
        {
            switch (Cardinality)
            {
                case HookCardinality.Single:
                case HookCardinality.SingleOptional:
                    {
                        Field.SetValue(origin.Module, destination.Module);
                    }
                    break;
                case HookCardinality.AnyNumber:
                case HookCardinality.AtLeastOne:
                    {
                        // the type is an array
                        if (Field.GetValue(origin.Module) is Array data)
                        {
                            data.SetValue(destination.Module, index);
                        }
                    }
                    break;
                default:
                    throw new NotImplementedException("Unknown Cardinality!");
            }
        }
    }
}
