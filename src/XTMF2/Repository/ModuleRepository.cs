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
using System.Collections.ObjectModel;
using System.Text;
using System.Reflection;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;

namespace XTMF2.Repository
{
    /// <summary>
    /// Provides access to detailed information for modules.
    /// </summary>
    public sealed class ModuleRepository
    {
        private ConcurrentDictionary<Type, (ModuleAttribute Description, TypeInfo TypeInfo, NodeHook[] Hooks)> _Data
            = new ConcurrentDictionary<Type, (ModuleAttribute Description, TypeInfo TypeInfo, NodeHook[] Hooks)>();
        private static TypeInfo IModuleTypeInfo = typeof(IModule).GetTypeInfo();

        private Lock _moduleTypesLock = new();
        private readonly ObservableCollection<Type> _moduleTypes = new ObservableCollection<Type>();

        /// <summary>
        /// Open-generic module types (e.g. <c>BasicParameter&lt;&gt;</c>) that carry a
        /// <see cref="ModuleAttribute"/> but cannot be directly instantiated.
        /// GUI code can call <see cref="GetCompatibleConstructedTypes"/> to obtain a list of
        /// concrete closed-generic constructions that are compatible with a given hook type.
        /// </summary>
        private readonly List<Type> _openGenericModuleTypes = new();

        /// <summary>
        /// An observable, read-only list of every module <see cref="Type"/> currently loaded
        /// into this repository.  Consumers can bind to this collection; it is updated on the
        /// thread that calls <see cref="Add"/> or <see cref="AddIfModuleType"/>.
        /// </summary>
        public ReadOnlyObservableCollection<Type> LoadedModuleTypes { get; }
            = null!; // assigned in constructor

        /// <summary>
        /// Snapshot of every open-generic module type that has been registered
        /// (e.g. <c>BasicParameter&lt;&gt;</c>, <c>ScriptedParameter&lt;&gt;</c>).
        /// Use <see cref="GetCompatibleConstructedTypes"/> to build closed generics for a hook.
        /// </summary>
        public IReadOnlyList<Type> OpenGenericModuleTypes
        {
            get { lock (_moduleTypesLock) { return _openGenericModuleTypes.ToList(); } }
        }

        public ModuleRepository()
        {
            LoadedModuleTypes = new ReadOnlyObservableCollection<Type>(_moduleTypes);
        }

        /// <summary>
        /// Returns all closed-generic constructions of registered open-generic module types that
        /// are assignable to <paramref name="requiredType"/>.
        /// For example, if the hook requires <c>IFunction&lt;bool&gt;</c>, this yields
        /// <c>BasicParameter&lt;bool&gt;</c>, <c>ScriptedParameter&lt;bool&gt;</c>, etc.
        /// </summary>
        public IEnumerable<Type> GetCompatibleConstructedTypes(Type requiredType)
        {
            IReadOnlyList<Type> openGenerics;
            lock (_moduleTypesLock) { openGenerics = _openGenericModuleTypes.ToList(); }

            foreach (var og in openGenerics)
            {
                if (TryConstructToSatisfy(og, requiredType, out var constructed))
                    yield return constructed!;
            }
        }

        /// <summary>
        /// Attempts to construct a closed generic type from <paramref name="openGeneric"/> such
        /// that the result is assignable to (or implements) <paramref name="requiredType"/>.
        /// </summary>
        private static bool TryConstructToSatisfy(Type openGeneric, Type requiredType, out Type? constructed)
        {
            constructed = null;
            if (!openGeneric.IsGenericTypeDefinition) return false;
            if (!requiredType.IsGenericType) return false;

            var reqGenDef  = requiredType.GetGenericTypeDefinition();
            var reqArgs    = requiredType.GetGenericArguments();
            var typeParams = openGeneric.GetGenericArguments();

            // Walk all interfaces + base types of the open generic looking for one whose
            // generic definition matches reqGenDef.
            var candidates = openGeneric.GetInterfaces()
                .Concat(GetBaseChain(openGeneric))
                .Where(t => t is not null && t.IsGenericType && t.GetGenericTypeDefinition() == reqGenDef);

            foreach (var candidate in candidates)
            {
                var mapping = new Dictionary<Type, Type>();
                if (TryBuildTypeMapping(typeParams, candidate!.GetGenericArguments(), reqArgs, mapping)
                    && typeParams.All(tp => mapping.ContainsKey(tp)))
                {
                    try
                    {
                        constructed = openGeneric.MakeGenericType(typeParams.Select(tp => mapping[tp]).ToArray());
                        return true;
                    }
                    catch { /* generic constraints violated – try next candidate */ }
                }
            }
            return false;
        }

        private static IEnumerable<Type> GetBaseChain(Type t)
        {
            var current = t.BaseType;
            while (current != null && current != typeof(object))
            {
                yield return current;
                current = current.BaseType;
            }
        }

        /// <summary>
        /// Recursively maps the open generic type parameters in <paramref name="ifaceArgs"/>
        /// to the concrete types in <paramref name="reqArgs"/>.
        /// </summary>
        private static bool TryBuildTypeMapping(Type[] typeParams, Type[] ifaceArgs, Type[] reqArgs,
            Dictionary<Type, Type> mapping)
        {
            if (ifaceArgs.Length != reqArgs.Length) return false;
            for (int i = 0; i < ifaceArgs.Length; i++)
            {
                var ifaceArg = ifaceArgs[i];
                var reqArg   = reqArgs[i];

                if (ifaceArg.IsGenericParameter)
                {
                    // Validate generic constraints on the type parameter.
                    foreach (var constraint in ifaceArg.GetGenericParameterConstraints())
                        if (!constraint.IsAssignableFrom(reqArg)) return false;

                    if (mapping.TryGetValue(ifaceArg, out var existing))
                    {
                        if (existing != reqArg) return false; // conflicting mapping
                    }
                    else
                    {
                        mapping[ifaceArg] = reqArg;
                    }
                }
                else if (ifaceArg.IsGenericType && reqArg.IsGenericType)
                {
                    // Recurse for nested generic arguments.
                    if (ifaceArg.GetGenericTypeDefinition() != reqArg.GetGenericTypeDefinition()) return false;
                    if (!TryBuildTypeMapping(typeParams, ifaceArg.GetGenericArguments(),
                                            reqArg.GetGenericArguments(), mapping)) return false;
                }
                else if (ifaceArg != reqArg)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Adds <paramref name="type"/> to <see cref="LoadedModuleTypes"/> if it is not already
        /// present. Must be called after the type is successfully stored in <see cref="_Data"/>.
        /// </summary>
        private void TrackType(Type type)
        {
            lock(_moduleTypesLock)
            {   
                if (!_moduleTypes.Contains(type))
                {
                    _moduleTypes.Add(type);
                }
            }
        }

        /// <summary>
        /// Add the given type to the module repository.
        /// </summary>
        /// <param name="type">The type to add to the repository.</param>
        public void Add(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);

            if (!IModuleTypeInfo.IsAssignableFrom(type))
            {
                throw new ArgumentException(nameof(type), "The type is not of a module!");
            }
            _Data[type] = GetTypeData(type);
            TrackType(type);
        }

        /// <summary>
        /// Add the given type to the module repository if it implements
        /// the IModule interface.
        /// </summary>
        /// <param name="type">The type to add.</param>
        public void AddIfModuleType(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }
            if (type.IsAbstract || type.IsInterface)
                return;

            if (type.IsGenericTypeDefinition)
            {
                // Open generic types cannot be assigned to IModule directly; track them
                // separately so the GUI can construct compatible closed types for hooks.
                if (type.GetCustomAttribute<ModuleAttribute>() != null)
                {
                    lock (_moduleTypesLock)
                    {
                        if (!_openGenericModuleTypes.Contains(type))
                            _openGenericModuleTypes.Add(type);
                    }
                }
            }
            else if (IModuleTypeInfo.IsAssignableFrom(type))
            {
                _Data[type] = GetTypeData(type);
                TrackType(type);
            }
        }

        /// <summary>
        /// Get the XTMF information and type information for a given type.
        /// </summary>
        /// <param name="type">The type to get the information from.</param>
        /// <returns>The description, typeinfo, and hooks for the type.</returns>
        public (ModuleAttribute Description, TypeInfo TypeInfo, NodeHook[] Hooks) this[Type type]
        {
            get
            {
                if (type == null)
                {
                    throw new ArgumentNullException(nameof(type));
                }
                if (!_Data.TryGetValue(type, out var ret))
                {
                    // Add() calls TrackType() internally, so LoadedModuleTypes is updated here too.
                    Add(type);
                    _Data.TryGetValue(type, out ret);
                }
                return ret;
            }
        }

        private (ModuleAttribute Description, TypeInfo TypeInfo, NodeHook[] Hooks) GetTypeData(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            var typeInfo = type.GetTypeInfo();
            var hooks = new List<NodeHook>();
            // Load properties and fields
            ModuleAttribute description = LoadModuleDescription(type);
            LoadFields(type, typeInfo, hooks);
            LoadProperties(type, typeInfo, hooks);
            // Make sure all of the hooks have names
            if (hooks.Any(h => String.IsNullOrWhiteSpace(h.Name)))
            {
                throw new XTMFCodeStyleError(type, "All sub module properties in must have a name defined in their attribute!");
            }
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

            var description = (ModuleAttribute?)type.GetTypeInfo().GetCustomAttribute(typeof(ModuleAttribute));
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

        private static void LoadFields(Type type, TypeInfo typeInfo, List<NodeHook> hooks)
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
                        mType = mType.GetElementType()!;
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
                            hooks.Add(new FieldHook(parameter.Name!, field, true, parameter.Index, true, parameter.DefaultValue));
                        }
                        else if (attributes.First() is SubModuleAttribute subModule)
                        {
                            if (subModule.Index < 0)
                            {
                                throw new XTMFCodeStyleError(type, $"There is no index defined for sub module property {field.Name}!");
                            }
                            hooks.Add(new FieldHook(subModule.Name!, field, subModule.Required, subModule.Index, false, null));
                        }
                        else
                        {
                            throw new XTMFCodeStyleError(type, $"Unknown attribute defining sub module property {field.Name}!");
                        }
                    }
                }
            }
        }

        private static void LoadProperties(Type type, TypeInfo typeInfo, List<NodeHook> hooks)
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
                        mType = mType.GetElementType()!;
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
                            hooks.Add(new PropertyHook(parameter.Name!, property, true, parameter.Index, true, parameter.DefaultValue));
                        }
                        else if (attributes.First() is SubModuleAttribute subModule)
                        {
                            if (subModule.Index < 0)
                            {
                                throw new XTMFCodeStyleError(type, $"There is no index defined for sub module property {property.Name}!");
                            }
                            hooks.Add(new PropertyHook(subModule.Name!, property, subModule.Required, subModule.Index, false, null));
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
