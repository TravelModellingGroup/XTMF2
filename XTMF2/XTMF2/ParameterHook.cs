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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace XTMF2
{
    /// <summary>
    /// The parameter hook connects a parameter to the module instance
    /// </summary>
    public abstract class ParameterHook
    {
        /// <summary>
        /// The name of the parameter hook
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// The type for this parameter
        /// </summary>
        public abstract Type Type { get; }

        /// <summary>
        /// The parameter that the parameter hook is currently bound to.
        /// </summary>
        public Parameter Parameter { get; private set; }

        /// <summary>
        /// Set the parameter that is bound to the parameter hook
        /// </summary>
        /// <param name="parameter">The parameter to bind this hook to.</param>
        /// <param name="error">A description of the error if one occurs</param>
        /// <returns>True if the operation was successful, false otherwise</returns>
        public bool SetParameter(Parameter parameter, ref string error)
        {
            return false;
        }

        /// <summary>
        /// Assign the parameter to the instance of the model system
        /// </summary>
        /// <param name="module">The module object to assign the value of the parameter to.</param>
        /// <param name="error">A description of the error if one occurs</param>
        /// <returns>True if the operation was successful, false otherwise</returns>
        public abstract bool AssignValueToModule(object module, ref string error);

        private static readonly ConcurrentDictionary<Type, List<ParameterHook>> StoredHooks = new ConcurrentDictionary<Type, List<ParameterHook>>();

        internal ParameterAttribute Attribute { get; }

        public string DefaultValue => Attribute.DefaultValue;

        public ParameterHook(ParameterAttribute attribute)
        {
            Attribute = attribute;
        }

        internal static List<ParameterHook> CreateParameterHooks(Type moduleType)
        {
            if (moduleType == null)
            {
                throw new ArgumentNullException(nameof(moduleType));
            }
            // check to see if we already have this cached
            if (StoredHooks.TryGetValue(moduleType, out var previouslyStored))
            {
                return previouslyStored;
            }
            // if the values were not previously cached we will need to use reflection to get the parameters
            var moduleTypeInfo = moduleType.GetTypeInfo();
            var fieldHooks = from field in moduleType.GetRuntimeFields()
                             let parameterAt = (ParameterAttribute)field.GetCustomAttribute(typeof(ParameterAttribute))
                             where parameterAt != null
                             select (ParameterHook)new FieldParameterHook(field, parameterAt);
            var propertyHooks = from property in moduleType.GetRuntimeProperties()
                                let parameterAt = (ParameterAttribute)property.GetCustomAttribute(typeof(ParameterAttribute))
                                where parameterAt != null
                                select (ParameterHook)new PropertyParameterHook(property, parameterAt);
            var ret = fieldHooks.Union(propertyHooks).ToList();
            StoredHooks[moduleType] = ret;
            return ret;
        }
    }
}
