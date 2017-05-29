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
    public sealed class ModuleRepository
    {
        private ConcurrentDictionary<Type, (ModuleAttribute Description, TypeInfo TypeInfo, ModelSystemStructureHook[] Hooks)> Data
            = new ConcurrentDictionary<Type, (ModuleAttribute Description, TypeInfo TypeInfo, ModelSystemStructureHook[] Hooks)>();
        private static TypeInfo IModuleTypeInfo = typeof(IModule).GetTypeInfo();

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
            Data[type] = GetTypeData(type);
        }

        public void AddIfModuleType(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }
            if (IModuleTypeInfo.IsAssignableFrom(type))
            {
                Data[type] = GetTypeData(type);
            }
        }

        private (ModuleAttribute Description, TypeInfo TypeInfo, ModelSystemStructureHook[] Hooks) GetTypeData(Type type)
        {
            var typeInfo = type.GetTypeInfo();
            var hooks = new List<ModelSystemStructureHook>();
            // Load properties and fields
            ModuleAttribute description = LoadModuleDescription(type);
            LoadFields(type, typeInfo, hooks);
            LoadProperties(type, typeInfo, hooks);
            // ensure there are no duplicates
            var duplicates = from h in hooks
                             where hooks.Any(other => h != other && h.Name.Equals(other.Name, StringComparison.OrdinalIgnoreCase))
                             select h;
            if (duplicates.Any())
            {
                throw new XTMFCodeStyleError(type, $"Duplicate properties with the name {duplicates.First().Name}!");
            }
            return (description, typeInfo, hooks.ToArray());
        }

        private ModuleAttribute LoadModuleDescription(Type type)
        {
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
                            // all parameters are required
                            hooks.Add(new FieldHook(parameter.Name, field, true));
                        }
                        else if (attributes.First() is SubModuleAttribute subModule)
                        {
                            hooks.Add(new FieldHook(subModule.Name, field, subModule.Required));
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
                            // all parameters are required
                            hooks.Add(new PropertyHook(parameter.Name, property, true));
                        }
                        else if (attributes.First() is SubModuleAttribute subModule)
                        {
                            hooks.Add(new PropertyHook(subModule.Name, property, subModule.Required));
                        }
                        else
                        {
                            throw new XTMFCodeStyleError(type, $"Unknown attribute defining sub module property {property.Name}!");
                        }
                    }
                }
            }
        }

        public (ModuleAttribute Description, TypeInfo TypeInfo, ModelSystemStructureHook[] Hooks) this[Type type]
        {
            get
            {
                if (!Data.TryGetValue(type, out var ret))
                {
                    Add(type);
                    Data.TryGetValue(type, out ret);
                }
                return ret;
            }
        }
    }
}
