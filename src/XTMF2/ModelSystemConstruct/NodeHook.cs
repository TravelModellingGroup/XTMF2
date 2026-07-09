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
using XTMF2.ModelSystemConstruct;

namespace XTMF2
{
    /// <summary>
    /// Defines the places to connect links between nodes.
    /// </summary>
    public abstract class NodeHook
    {
        public virtual string Name { get; protected set; }

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

        /// <summary>
        /// True when this hook passes execution context to its destination.
        /// </summary>
        public bool PassesExecution { get; private set; }

        public NodeHook(string name, HookCardinality cardinality, int index, bool isParameter, string? defaultValue, bool passesExecution = false)
        {
            Name = name;
            Cardinality = cardinality;
            Index = index;
            IsParameter = isParameter;
            DefaultValue = defaultValue;
            PassesExecution = passesExecution;
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
        /// Install pre-resolved module instances directly, bypassing <see cref="Node.Module"/>.
        /// Called when constructing per-instance FunctionInstance clones at runtime.
        /// </summary>
        internal abstract void Install(IModule origin, IModule destination, int index);

        /// <summary>
        /// Create the array of data with the given size
        /// </summary>
        /// <param name="length">The number of modules that will be installed</param>
        internal abstract void CreateArray(IModule origin, int length);

        /// <summary>
        /// Check to see if the hook for the given module was previously set.
        /// </summary>
        /// <param name="module">The module to check.</param>
        /// <returns>True if there is something assigned to that property, false otherwise.</returns>
        internal abstract bool AnyInstalled(IModule module);
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
        public PropertyHook(string name, PropertyInfo property, bool required, int index, bool isParameter, string? defaultValue, bool passesExecution = false)
            : base(name, GetCardinality(property, required), index, isParameter, defaultValue, passesExecution)
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
            var elementType = Property.PropertyType.GetElementType()!;
            if (length == 0)
            {
                // Use reflection to get access to the Array.Empty<T>() method using reflection.
                var emptyMethod = typeof(Array).GetMethod(nameof(Array.Empty))!.MakeGenericMethod(elementType!);
                var emptyArray = emptyMethod.Invoke(null, null);
                Property.SetValue(origin, emptyArray);
            }
            else
            {
                Property.SetValue(origin, Array.CreateInstance(elementType, length));
            }

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

        internal override void Install(IModule origin, IModule destination, int index)
        {
            switch (Cardinality)
            {
                case HookCardinality.Single:
                case HookCardinality.SingleOptional:
                    Property.SetValue(origin, destination);
                    break;
                case HookCardinality.AnyNumber:
                case HookCardinality.AtLeastOne:
                    if (Property.GetValue(origin) is Array data)
                        data.SetValue(destination, index);
                    break;
                default:
                    throw new NotImplementedException("Unknown Cardinality!");
            }
        }

        internal override bool AnyInstalled(IModule module)
        {
            return Property.GetValue(module) is not null;
        }
    }

    /// <summary>
    /// A hook for the field of a node
    /// </summary>
    sealed class FieldHook : NodeHook
    {
        readonly FieldInfo Field;
        public FieldHook(string name, FieldInfo field, bool required, int index, bool isParameter, string? defaultValue, bool passesExecution = false)
            : base(name, GetCardinality(field, required), index, isParameter, defaultValue, passesExecution)
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
            var elementType = Field.FieldType.GetElementType();
            if (length == 0)
            {
                // Use reflection to get access to the Array.Empty<T>() method using reflection.
                var emptyMethod = typeof(Array).GetMethod(nameof(Array.Empty))!.MakeGenericMethod(elementType!);
                var emptyArray = emptyMethod.Invoke(null, null);
                Field.SetValue(origin, emptyArray);
            }
            else
            {
                Field.SetValue(origin, Array.CreateInstance(Field.FieldType.GetElementType()!, length));
            }
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

        internal override void Install(IModule origin, IModule destination, int index)
        {
            switch (Cardinality)
            {
                case HookCardinality.Single:
                case HookCardinality.SingleOptional:
                    Field.SetValue(origin, destination);
                    break;
                case HookCardinality.AnyNumber:
                case HookCardinality.AtLeastOne:
                    if (Field.GetValue(origin) is Array data)
                        data.SetValue(destination, index);
                    break;
                default:
                    throw new NotImplementedException("Unknown Cardinality!");
            }
        }

        internal override bool AnyInstalled(IModule module)
        {
            return Field.GetValue(module) is not null;
        }
    }

    /// <summary>
    /// A hook on a <see cref="ModelSystemConstruct.FunctionInstance"/> that corresponds
    /// to one of the owning template's <see cref="ModelSystemConstruct.FunctionParameter"/>
    /// slots.  Outgoing links from the function instance to external nodes use this hook type;
    /// the actual module wiring is performed transitively by
    /// <see cref="ModelSystemConstruct.FunctionInstance.ConstructRuntimeLinks"/>.
    /// </summary>
    public sealed class FunctionParameterHook : NodeHook
    {
        /// <summary>The function-parameter slot this hook represents.</summary>
        public ModelSystemConstruct.FunctionParameter Parameter { get; }

        /// <inheritdoc/>
        public override Type Type => Parameter.Type;

        /// <param name="parameter">The function-parameter slot this hook exposes.</param>
        /// <param name="index">Ordinal position among the template's FunctionParameters.</param>
        public FunctionParameterHook(ModelSystemConstruct.FunctionParameter parameter, int index)
            : base(parameter.Name, HookCardinality.SingleOptional, index, isParameter: false, defaultValue: null, passesExecution: false)
        {
            Parameter = parameter;
        }

        /// <summary>
        /// Always returns the current name of the underlying <see cref="Parameter"/>, so that
        /// renaming a <see cref="FunctionParameter"/> is immediately visible on all
        /// <see cref="FunctionInstance"/> hook rows without rebuilding the hooks list.
        /// </summary>
        public override string Name => Parameter.Name;

        // ── Install is intentionally a no-op ─────────────────────────────
        // The module wiring for FunctionParameter hooks is not performed through
        // the standard Install path; instead SingleLink.Construct detects a
        // FunctionInstance origin with a FunctionParameterHook and calls
        // FunctionInstance.BindParameter directly.

        internal override void Install(Node origin, Node destination, int index) { /* no-op */ }
        internal override void Install(IModule origin, IModule destination, int index) { /* no-op */ }
        internal override void CreateArray(IModule origin, int length) { /* no-op */ }
        internal override bool AnyInstalled(IModule module) => false;
    }
}
