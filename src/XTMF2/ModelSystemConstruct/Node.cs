/*
    Copyright 2017-2018 University of Toronto
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
using System.ComponentModel;
using System.Collections.Generic;
using XTMF2.Editing;
using System.Text.Json;
using System.Collections.ObjectModel;
using System.Reflection;
using XTMF2.RuntimeModules;
using System.Collections.Concurrent;
using System.Linq;
using XTMF2.Repository;

namespace XTMF2.ModelSystemConstruct
{
    /// <summary>
    /// The basic building block of a model system
    /// </summary>
    public class Node : INotifyPropertyChanged
    {
        /// <summary>
        /// The boundary that this node is contained within
        /// </summary>
        public Boundary ContainedWithin { get; protected set; }

        protected const string NameProperty = "Name";
        protected const string DescriptionProperty = "Description";
        protected const string XProperty = "X";
        protected const string TypeProperty = "Type";
        protected const string YProperty = "Y";
        protected const string WidthProperty = "Width";
        protected const string HeightProperty = "Height";
        protected const string IndexProperty = "Index";
        protected const string ParameterProperty = "Parameter";
        protected const string DisabledProperty = "Disabled";

        /// <summary>
        /// Don't use this field as the setter
        /// will properly create the node hooks.
        /// </summary>
        private Type _type;

        /// <summary>
        /// The type that this will represent
        /// </summary>
        public Type Type => _type;

        /// <summary>
        /// A parameter value to use if this is a parameter type
        /// </summary>
        public string? ParameterValue { get; private set; }

        /// <summary>
        /// Create the hooks for the node
        /// </summary>
        private void CreateNodeHooks(ModuleRepository repository)
        {
            Hooks = repository[_type!].Hooks;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Hooks)));
        }

        /// <summary>
        /// Get a readonly list of possible hooks to use to interface with other nodes.
        /// </summary>
        public IReadOnlyList<NodeHook> Hooks { get; private set; }

        /// <summary>
        /// The name of the node
        /// </summary>
        public string Name { get; protected set; }

        /// <summary>
        /// The location to graphically place this node within a boundary
        /// </summary>
        public Rectangle Location { get; protected set; }

        /// <summary>
        /// The link to the executing object.
        /// This is only set during a run.
        /// </summary>
        public IModule? Module { get; private set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Set the location of the node
        /// </summary>
        /// <param name="newLocation">The location to use.</param>
        internal void SetLocation(Rectangle newLocation)
        {
            Location = newLocation;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Location)));
        }

        /// <summary>
        /// Change the name of the node
        /// </summary>
        /// <param name="name">The name to change it to</param>
        /// <param name="error">A description of the error if one occurs</param>
        /// <returns>True if the operation was successful, false otherwise</returns>
        internal bool SetName(string name, out CommandError? error)
        {
            if (String.IsNullOrWhiteSpace(name))
            {
                return FailWith(out error, "A name cannot be whitespace.");
            }
            Name = name;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            error = null;
            return true;
        }

        /// <summary>
        /// Set the value of a parameter
        /// </summary>
        /// <param name="sesson">The session that this will be edited with</param>
        /// <param name="value">The value to change the parameter to.</param>
        /// <param name="error">A description of the error if one occurs</param>
        /// <returns>True if the operation was successful, false otherwise</returns>
        internal bool SetParameterValue(string value, out CommandError? error)
        {
            // ensure that the value is allowed
            if (Type == null)
            {
                return FailWith(out error, "Unable to set the parameter value of a node that lacks a type!");
            }
            string? errorString = null;
            if (!ArbitraryParameterParser.Check(Type.GenericTypeArguments[0], value, ref errorString))
            {
                return FailWith(out error, $"Unable to create a parse the value {value} for type {Type.GenericTypeArguments[0].FullName}!");
            }
            ParameterValue = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ParameterValue)));
            error = null;
            return true;
        }

        internal bool Validate(ref string? moduleName, ref string? error)
        {
            foreach (var hook in Hooks)
            {
                // if the 
                if (hook.Cardinality == HookCardinality.Single
                   || hook.Cardinality == HookCardinality.AtLeastOne)
                {
                    if (!ContainedWithin.Links.Any(l => l.Origin == this && l.OriginHook == hook))
                    {
                        moduleName = Name;
                        error = $"A required link was not assigned for the hook {hook.Name}!";
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// An optional description for this node
        /// </summary>
        public string Description { get; protected set; } = string.Empty;

        /// <summary>
        /// Holds the state if this module should be disabled for a model run.
        /// </summary>
        public bool IsDisabled { get; private set; }

        private static readonly Type[] EmptyConstructor = Array.Empty<Type>();

        private static readonly Type[] RuntimeConstructor = new Type[] { typeof(XTMFRuntime) };

        private static readonly Type GenericParameter = typeof(BasicParameter<>);

        private static readonly ConcurrentDictionary<Type, FieldInfo> GenericValue = new ConcurrentDictionary<Type, FieldInfo>();

        /// <summary>
        /// Setup the module as defined in this node.
        /// </summary>
        /// <param name="error">A description of the error if one occurs</param>
        /// <returns>True if the operation was successful, false otherwise</returns>
        internal bool ConstructModule(XTMFRuntime runtime, ref string? error)
        {
            if (_type is null)
            {
                return FailWith(out error, $"Unable to construct a module named {Name} without a type!");
            }
            var typeInfo = _type.GetTypeInfo();
            var module = (
                typeInfo.GetConstructor(RuntimeConstructor)?.Invoke(new[] { runtime })
                ?? typeInfo.GetConstructor(EmptyConstructor)?.Invoke(EmptyConstructor)) as IModule;
            if (!(module is IModule))
            {
                return FailWith(out error, $"Unable to construct a module of type {_type.GetTypeInfo().AssemblyQualifiedName}!");
            }
            Module = module;
            Module.Name = Name;
            if (_type.IsConstructedGenericType && _type.GetGenericTypeDefinition() == GenericParameter)
            {
                var paramType = _type.GenericTypeArguments[0];
                var paramValue = ParameterValue ?? "";
                var (Sucess, Value) = ArbitraryParameterParser.ArbitraryParameterParse(paramType, paramValue, ref error);
                if (!Sucess)
                {
                    return FailWith(out error, $"Unable to assign the value of {paramValue} to type {paramType.FullName}!");
                }
                if (!GenericValue.TryGetValue(_type, out var info))
                {
                    info = _type.GetRuntimeField("Value");
                    if (info == null)
                    {
                        return FailWith(out error, $"Unable find a field named 'Value' on type {_type.FullName} in order to assign a value to it!");
                    }
                    GenericValue[paramType] = info;
                }
                info.SetValue(Module, Value);
            }
            error = null;
            return true;
        }

        /// <summary>
        /// Change the name of the node
        /// </summary>
        /// <param name="description">The description to change to</param>
        /// <param name="error">A description of the error if one occurs</param>
        /// <returns>True if the operation was successful, false otherwise</returns>
        internal bool SetDescription(string description)
        {
            Description = description;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description)));
            return true;
        }

        /// <summary>
        /// Change the type of the node.
        /// </summary>
        /// <param name="session">The current editing session.</param>
        /// <param name="type">The type to set this node to</param>
        /// <param name="error"></param>
        internal bool SetType(ModuleRepository modules, Type type, ref string? error)
        {
            if (type == null)
            {
                return FailWith(out error, "The given type was null!");
            }
            if (_type != type)
            {
                _type = type;
                CreateNodeHooks(modules);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Type)));
            }
            error = null;
            return true;
        }

        /// <summary>
        /// Set the module to the given disabled state.
        /// </summary>
        /// <param name="modelSystemSession">The model system session</param>
        /// <param name="disabled"></param>
        /// <returns></returns>
        internal bool SetDisabled(bool disabled, out CommandError? error)
        {
            IsDisabled = disabled;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDisabled)));
            error = null;
            return true;
        }

        /// <summary>
        /// Create a new node with name only.
        /// Only invoke this if you are going to set the type explicitly right after.
        /// </summary>
        /// <param name="name">The name of the node</param>
        protected Node(string name, Type type, Boundary containedWithin, IReadOnlyList<NodeHook> hooks, Rectangle location)
        {
            Name = name;
            _type = type;
            ContainedWithin = containedWithin;
            Hooks = hooks;
            Location = location;
        }

        /// <summary>
        /// Fail with the given message.
        /// </summary>
        /// <param name="error">The place to store the message</param>
        /// <param name="message">The message to fail with.</param>
        /// <returns>Always false</returns>
        private static bool FailWith(out CommandError error, string message)
        {
            error = new CommandError(message);
            return false;
        }

        private static bool FailWith(out string error, string message)
        {
            error = message;
            return false;
        }

        /// <summary>
        /// Fail with the given message.
        /// </summary>
        /// <param name="mss">The node to null out.</param>
        /// <param name="error">The place to store the message</param>
        /// <param name="message">The message to fail with.</param>
        /// <returns>Always false</returns>
        private static bool FailWith(out Node? mss, out string error, string message)
        {
            mss = null;
            error = message;
            return false;
        }

        internal bool GetLink(NodeHook hook, out Link? link)
        {
            return (link = (from l in ContainedWithin!.Links
                            where l.Origin == this && l.OriginHook == hook
                            select l).FirstOrDefault()) != null;
        }

        internal virtual void Save(ref int index, Dictionary<Node, int> moduleDictionary, Dictionary<Type, int> typeDictionary, Utf8JsonWriter writer)
        {
            moduleDictionary.Add(this, index);
            writer.WriteStartObject();
            writer.WriteString(NameProperty, Name);
            writer.WriteString(DescriptionProperty, Description);
            writer.WriteNumber(TypeProperty, typeDictionary[_type!]);
            writer.WriteNumber(XProperty, Location.X);
            writer.WriteNumber(YProperty, Location.Y);
            writer.WriteNumber(WidthProperty, Location.Width);
            writer.WriteNumber(HeightProperty, Location.Height);
            writer.WriteNumber(IndexProperty, index++);
            if (!String.IsNullOrEmpty(ParameterValue))
            {
                writer.WriteString(ParameterProperty, ParameterValue);
            }
            if (IsDisabled)
            {
                writer.WriteBoolean(DisabledProperty, true);
            }
            writer.WriteEndObject();
        }

        internal static bool Load(ModuleRepository modules, Dictionary<int, Type> typeLookup, Dictionary<int, Node> nodes,
            Boundary boundary, ref Utf8JsonReader reader, out Node? mss, ref string? error)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                return FailWith(out mss, out error, "Invalid token when loading a start!");
            }
            Type? type = null;
            string? name = null;
            int index = -1;
            bool disabled = false;
            Rectangle point = new Rectangle();
            string description = string.Empty;
            string? parameter = null;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType == JsonTokenType.Comment) continue;
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    return FailWith(out mss, out error, "Invalid token when loading start");
                }
                if (reader.ValueTextEquals(NameProperty))
                {
                    reader.Read();
                    name = reader.GetString();
                }
                else if (reader.ValueTextEquals(DescriptionProperty))
                {
                    reader.Read();
                    description = reader.GetString() ?? string.Empty;
                }
                else if (reader.ValueTextEquals(XProperty))
                {
                    reader.Read();
                    point = new Rectangle(reader.GetSingle(), point.Y, point.Width, point.Height);
                }
                else if (reader.ValueTextEquals(YProperty))
                {
                    reader.Read();
                    point = new Rectangle(point.X, reader.GetSingle(), point.Width, point.Height);
                }
                else if (reader.ValueTextEquals(WidthProperty))
                {
                    reader.Read();
                    point = new Rectangle(point.X, point.Y, reader.GetSingle(), point.Height);
                }
                else if (reader.ValueTextEquals(HeightProperty))
                {
                    reader.Read();
                    point = new Rectangle(point.X, point.Y, point.Width, reader.GetSingle());
                }
                else if (reader.ValueTextEquals(IndexProperty))
                {
                    reader.Read();
                    index = reader.GetInt32();
                }
                else if (reader.ValueTextEquals(TypeProperty))
                {
                    reader.Read();
                    var typeIndex = reader.GetInt32();
                    if (!typeLookup.TryGetValue(typeIndex, out type))
                    {
                        return FailWith(out mss, out error, $"Invalid type index {typeIndex}!");
                    }
                }
                else if (reader.ValueTextEquals(ParameterProperty))
                {
                    reader.Read();
                    parameter = reader.GetString() ?? string.Empty;
                }
                else if (reader.ValueTextEquals(DisabledProperty))
                {
                    reader.Read();
                    disabled = reader.GetBoolean();
                }
                else
                {
                    return FailWith(out mss, out error, $"Undefined parameter type {reader.GetString()} when loading a start!");
                }
            }
            if (name == null)
            {
                return FailWith(out mss, out error, "Undefined name for a start in boundary " + boundary.FullPath);
            }
            if (index < 0)
            {
                return FailWith(out mss, out error, $"While loading {boundary.FullPath}.{name} we were unable to parse a valid index!");
            }
            if (nodes.ContainsKey(index))
            {
                return FailWith(out mss, out error, $"Index {index} already exists!");
            }
            if (type is null)
            {
                return FailWith(out mss, out error, $"When trying to create a node {name} there was no type defined!");
            }
            (_, _, var hooks) = modules[type];
            if (hooks == null)
            {
                return FailWith(out mss, out error, $"When trying to create a node {name} we were unable to find a hook for type {type.FullName}!");
            }
            mss = new Node(name, type, boundary, hooks, point)
            {
                Location = point,
                Description = description,
                ParameterValue = parameter,
                IsDisabled = disabled
            };
            nodes.Add(index, mss);
            return true;
        }

        internal static Node? Create(ModuleRepository modules, string name, Type type, Boundary boundary, Rectangle location)
        {
            (_, _, var hooks) = modules[type];
            if (hooks == null)
            {
                return null;
            }
            return new Node(name, type, boundary, hooks, location);
        }
    }
}
