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
using System.Reflection;
using System.Collections.Concurrent;
using System.Linq;

namespace XTMF2.Repository
{
    /// <summary>
    /// Provides access to detailed information for modules.
    /// </summary>
    public sealed class ModuleRepository
    {
        private ConcurrentDictionary<Type, (ModuleAttribute Description, TypeInfo TypeInfo, ModelSystemStructureHook[] Hooks)> _Data
            = new ConcurrentDictionary<Type, (ModuleAttribute Description, TypeInfo TypeInfo, ModelSystemStructureHook[] Hooks)>();
        private static TypeInfo IModuleTypeInfo = typeof(IModule).GetTypeInfo();

        /// <summary>
        /// Add the given type to the module repository.
        /// </summary>
        /// <param name="type">The type to add to the repository.</param>
        public void Add(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }
            if (!IModuleTypeInfo.IsAssignableFrom(type))
            {
                throw new ArgumentException(nameof(type), "The type is not of a module!");
            }
            _Data[type] = GetTypeData(type);
        }

        /// <summary>
        /// Add the given type to the module repository if it it comes implements
        /// the IModule interface.
        /// </summary>
        /// <param name="type">The type to add.</param>
        public void AddIfModuleType(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }
            if(!(type.IsAbstract || type.IsInterface))
            {
                if (IModuleTypeInfo.IsAssignableFrom(type))
                {
                    _Data[type] = GetTypeData(type);
                }
            }
        }

        /// <summary>
        /// Get the XTMF information and type information for a given type.
        /// </summary>
        /// <param name="type">The type to get the information from.</param>
        /// <returns>The description, typeinfo, and hooks for the type.</returns>
        public (ModuleAttribute Description, TypeInfo TypeInfo, ModelSystemStructureHook[] Hooks) this[Type type]
        {
            get
            {
                if (type == null)
                {
                    throw new ArgumentNullException(nameof(type));
                }
                if (!_Data.TryGetValue(type, out var ret))
                {
                    Add(type);
                    _Data.TryGetValue(type, out ret);
                }
                return ret;
            }
        }

        private (ModuleAttribute Description, TypeInfo TypeInfo, ModelSystemStructureHook[] Hooks) GetTypeData(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            var typeInfo = type.GetTypeInfo();
            var hooks = new List<ModelSystemStructureHook>();
            // Load properties and fields
            ModuleAttribute description = LoadModuleDescription(type);
            LoadFields(type, typeInfo, hooks);
            LoadProperties(type, typeInfo, hooks);
            // ensure there are no duplicates
            var duplicates = from h in hooks
                             let name = h.Name
                             where hooks.Any(other => h != other && name.Equals(other.Name, StringComparison.OrdinalIgnoreCase))
                             select h;
            if (duplicates.Any())
            {
                throw new XTMFCodeStyleError(type, $"Duplicate properties with the name {duplicates.First().Name}!");
            }
            duplicates = from h in hooks
                         let index = h.Index
                         where hooks.Any(other => other.Index == index && h != other)
                         select h;
            if(duplicates.Any())
            {
                var first = duplicates.First();
                throw new XTMFCodeStyleError(type, $"Duplicate properties with same index {first.Index}!");
            }
            // sort the hooks so this can be relied upon
            hooks.Sort((first, second) => first.Index - second.Index);
            return (description, typeInfo, hooks.ToArray());
        }

        private ModuleAttribute LoadModuleDescription(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            var description = (ModuleAttribute)type.GetTypeInfo().GetCustomAttribute(typeof(ModuleAttribute));
            if(description == null)
            {
                throw new XTMFCodeStyleError(type, "There was no module meta-data stored for this type!");
            }
            if(String.IsNullOrWhiteSpace(description.Name))
            {
                throw new XTMFCodeStyleError(type, "The module meta-data's Name field was left blank!");
            }
            if(String.IsNullOrWhiteSpace(description.DocumentationLink))
            {
                throw new XTMFCodeStyleError(type, "The module meta-data's Documentation Link field was left blank!");
            }
            if (String.IsNullOrWhiteSpace(description.Description))
            {
                throw new XTMFCodeStyleError(type, "The module meta-data's Description field was left blank!");
            }
            return description;
        }

        private static void LoadFields(Type type, TypeInfo typeInfo, List<ModelSystemStructureHook> hooks)
        {
            if (typeInfo == null)
            {
                throw new ArgumentNullException(nameof(typeInfo));
            }
            if (hooks == null)
            {
                throw new ArgumentNullException(nameof(hooks));
            }

            foreach (var field in typeInfo.DeclaredFields)
            {
                if (field.IsPublic)
                {
                    var mType = field.FieldType;
                    var isArray = mType.IsArray;
                    if (isArray)
                    {
                        mType = mType.GetElementType();
                    }
                    var mInfo = mType.GetTypeInfo();
                    if (IModuleTypeInfo.IsAssignableFrom(mType))
                    {
                        // Get the attributes attached the property
                        var attributes = from at in field.GetCustomAttributes(true)
                                         let atType = at.GetType()
                                         where atType == typeof(SubModuleAttribute) || atType == typeof(ParameterAttribute)
                                         select at;
                        // Analyze the property to ensure proper code style
                        if (!attributes.Any())
                        {
                            throw new XTMFCodeStyleError(type, $"You must define an attribute defining the sub module property {field.Name}!");
                        }
                        if (attributes.Count() > 1)
                        {
                            throw new XTMFCodeStyleError(type, $"Only one attribute defining the sub module property {field.Name} is allowed!");
                        }
                        if (attributes.First() is ParameterAttribute parameter)
                        {
                            if(parameter.Index < 0)
                            {
                                throw new XTMFCodeStyleError(type, $"There is no index defined for sub module property {field.Name}!");
                            }
                            // all parameters are required
                            hooks.Add(new FieldHook(parameter.Name, field, true, parameter.Index));
                        }
                        else if (attributes.First() is SubModuleAttribute subModule)
                        {
                            if (subModule.Index < 0)
                            {
                                throw new XTMFCodeStyleError(type, $"There is no index defined for sub module property {field.Name}!");
                            }
                            hooks.Add(new FieldHook(subModule.Name, field, subModule.Required, subModule.Index));
                        }
                        else
                        {
                            throw new XTMFCodeStyleError(type, $"Unknown attribute defining sub module property {field.Name}!");
                        }
                    }
                }
            }
        }

        private static void LoadProperties(Type type, TypeInfo typeInfo, List<ModelSystemStructureHook> hooks)
        {
            if (typeInfo == null)
            {
                throw new ArgumentNullException(nameof(typeInfo));
            }

            if (hooks == null)
            {
                throw new ArgumentNullException(nameof(hooks));
            }

            foreach (var property in typeInfo.DeclaredProperties)
            {
                if ((property.GetMethod?.IsPublic ?? false) && (property.SetMethod?.IsPublic ?? false))
                {
                    var mType = property.PropertyType;
                    var isArray = mType.IsArray;
                    if (isArray)
                    {
                        mType = mType.GetElementType();
                    }
                    var mInfo = mType.GetTypeInfo();
                    if (IModuleTypeInfo.IsAssignableFrom(mType))
                    {
                        // Get the attributes attached the property
                        var attributes = from at in property.GetCustomAttributes(true)
                                         let atType = at.GetType()
                                         where atType == typeof(SubModuleAttribute) || atType == typeof(ParameterAttribute)
                                         select at;
                        // Analyze the property to ensure proper code style
                        if (!attributes.Any())
                        {
                            throw new XTMFCodeStyleError(type, $"You must define an attribute defining the sub module property {property.Name}!");
                        }
                        if (attributes.Count() > 1)
                        {
                            throw new XTMFCodeStyleError(type, $"Only one attribute defining the sub module property {property.Name} is allowed!");
                        }
                        if (!(property.CanRead && property.CanWrite))
                        {
                            throw new XTMFCodeStyleError(type, $"You must be able to read and write to the sub module property {property.Name}!");
                        }
                        if (attributes.First() is ParameterAttribute parameter)
                        {
                            if (parameter.Index < 0)
                            {
                                throw new XTMFCodeStyleError(type, $"There is no index defined for sub module property {property.Name}!");
                            }
                            // all parameters are required
                            hooks.Add(new PropertyHook(parameter.Name, property, true, parameter.Index));
                        }
                        else if (attributes.First() is SubModuleAttribute subModule)
                        {
                            if (subModule.Index < 0)
                            {
                                throw new XTMFCodeStyleError(type, $"There is no index defined for sub module property {property.Name}!");
                            }
                            hooks.Add(new PropertyHook(subModule.Name, property, subModule.Required, subModule.Index));
                        }
                        else
                        {
                            throw new XTMFCodeStyleError(type, $"Unknown attribute defining sub module property {property.Name}!");
                        }
                    }
                }
            }
        }
    }
}
