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

namespace XTMF2
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
        protected const string IndexProperty = "Index";
        protected const string ParameterProperty = "Parameter";
        protected const string DisabledProperty = "Disabled";

        /// <summary>
        /// Don't use this field as the setter
        /// will properly create the node hooks.
        /// </summary>
        private Type _Type;

        /// <summary>
        /// The type that this will represent
        /// </summary>
        public Type Type => _Type;

        /// <summary>
        /// A parameter value to use if this is a parameter type
        /// </summary>
        public string ParameterValue { get; private set; }

        /// <summary>
        /// Create the hooks for the node
        /// </summary>
        private void CreateNodeHooks(ModelSystemSession session)
        {
            Hooks = session.GetModuleRepository()[_Type].Hooks;
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
        public Point Location { get; protected set; }

        /// <summary>
        /// The link to the executing object.
        /// This is only set during a run.
        /// </summary>
        public IModule Module { get; private set; }

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Set the location of the node
        /// </summary>
        /// <param name="x">The horizontal offset</param>
        /// <param name="y">The vertical offset</param>
        internal void SetLocation(float x, float y)
        {
            Location = new Point(x, y);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Location)));
        }

        /// <summary>
        /// Change the name of the node
        /// </summary>
        /// <param name="name">The name to change it to</param>
        /// <param name="error">A description of the error if one occurs</param>
        /// <returns>True if the operation was successful, false otherwise</returns>
        public bool SetName(ModelSystemSession session, string name, ref string error)
        {
            if (String.IsNullOrWhiteSpace(name))
            {
                return FailWith(ref error, "A name cannot be whitespace.");
            }
            Name = name;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            return true;
        }

        /// <summary>
        /// Set the value of a parameter
        /// </summary>
        /// <param name="sesson">The session that this will be edited with</param>
        /// <param name="value">The value to change the parameter to.</param>
        /// <param name="error">A description of the error if one occurs</param>
        /// <returns>True if the operation was successful, false otherwise</returns>
        public bool SetParameterValue(ModelSystemSession sesson, string value, ref string error)
        {
            // ensure that the value is allowed
            if (Type == null)
            {
                return FailWith(ref error, "Unable to set the parameter value of a node that lacks a type!");
            }
            if (!ArbitraryParameterParser.Check(Type.GenericTypeArguments[0], value, ref error))
            {
                return FailWith(ref error, $"Unable to create a parse the value {value} for type {Type.GenericTypeArguments[0].FullName}!");
            }
            ParameterValue = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ParameterValue)));
            return true;
        }

        internal bool Validate(ref string moduleName, ref string error)
        {
            foreach(var hook in Hooks)
            {
                // if the 
                if(hook.Cardinality == HookCardinality.Single
                   || hook.Cardinality == HookCardinality.AtLeastOne)
                {
                    if(!ContainedWithin.Links.Any(l=> l.Origin == this && l.OriginHook == hook))
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
        public string Description { get; protected set; }

        /// <summary>
        /// Holds the state if this module should be disabled for a model run.
        /// </summary>
        public bool IsDisabled { get; private set; }

        private static readonly Type[] EmptyConstructor = new Type[] { };

        private static readonly Type[] RuntimeConstructor = new Type[] { typeof(XTMFRuntime) };

        private static readonly Type GenericParameter = typeof(BasicParameter<>);

        private static readonly ConcurrentDictionary<Type, FieldInfo> GenericValue = new ConcurrentDictionary<Type, FieldInfo>();

        /// <summary>
        /// Setup the module as defined in this node.
        /// </summary>
        /// <param name="error">A description of the error if one occurs</param>
        /// <returns>True if the operation was successful, false otherwise</returns>
        internal bool ConstructModule(XTMFRuntime runtime, ref string error)
        {
            Module = (IModule)(
                Type.GetTypeInfo().GetConstructor(RuntimeConstructor)?.Invoke(new[] { runtime })
                ?? Type.GetTypeInfo().GetConstructor(EmptyConstructor).Invoke(EmptyConstructor));
            Module.Name = Name;
            if (Type.IsConstructedGenericType && Type.GetGenericTypeDefinition() == GenericParameter)
            {
                var paramType = Type.GenericTypeArguments[0];
                var (Sucess, Value) = ArbitraryParameterParser.ArbitraryParameterParse(paramType, ParameterValue, ref error);
                if (!Sucess)
                {
                    return FailWith(ref error, $"Unable to assign the value of {ParameterValue} to type {paramType.FullName}!");
                }
                if (!GenericValue.TryGetValue(_Type, out var info))
                {
                    info = _Type.GetRuntimeField("Value");
                    GenericValue[paramType] = info;
                }
                info.SetValue(Module, Value);
            }
            return true;
        }

        /// <summary>
        /// Change the name of the node
        /// </summary>
        /// <param name="description">The description to change to</param>
        /// <param name="error">A description of the error if one occurs</param>
        /// <returns>True if the operation was successful, false otherwise</returns>
        public bool SetDescription(ModelSystemSession session, string description, ref string error)
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
        internal bool SetType(ModelSystemSession session, Type type, ref string error)
        {
            if (type == null)
            {
                return FailWith(ref error, "The given type was null!");
            }
            if (_Type != type)
            {
                _Type = type;
                CreateNodeHooks(session);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Type)));
            }
            return true;
        }

        /// <summary>
        /// Set the module to the given disabled state.
        /// </summary>
        /// <param name="modelSystemSession">The model system session</param>
        /// <param name="disabled"></param>
        /// <returns></returns>
        internal bool SetDisabled(ModelSystemSession modelSystemSession, bool disabled)
        {
            IsDisabled = disabled;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDisabled)));
            return true;
        }

        /// <summary>
        /// Create a new node with name only.
        /// Only invoke this if you are going to set the type explicitly right after.
        /// </summary>
        /// <param name="name">The name of the node</param>
        protected Node(string name)
        {
            Name = name;
        }

        /// <summary>
        /// Get a default name from the type
        /// </summary>
        /// <param name="type">The type to derive the name from</param>
        /// <returns>True if the operation was successful, false otherwise</returns>
        private static string GetName(Type type)
        {
            return type?.Name ?? throw new ArgumentNullException(nameof(type));
        }

        /// <summary>
        /// Fail with the given message.
        /// </summary>
        /// <param name="error">The place to store the message</param>
        /// <param name="message">The message to fail with.</param>
        /// <returns>Always false</returns>
        private static bool FailWith(ref string error, string message)
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
        private static bool FailWith(out Node mss, ref string error, string message)
        {
            mss = null;
            error = message;
            return false;
        }

        internal bool GetLink(NodeHook hook, out Link link)
        {
            return (link = (from l in ContainedWithin.Links
                            where l.Origin == this && l.OriginHook == hook
                            select l).FirstOrDefault()) != null;
        }

        internal virtual void Save(ref int index, Dictionary<Node, int> moduleDictionary, Dictionary<Type, int> typeDictionary, Utf8JsonWriter writer)
        {
            moduleDictionary.Add(this, index);
            writer.WriteStartObject();
            writer.WriteString(NameProperty, Name);
            writer.WriteString(DescriptionProperty, Description);
            writer.WriteNumber(TypeProperty, typeDictionary[Type]);
            writer.WriteNumber(XProperty, Location.X);
            writer.WriteNumber(YProperty, Location.Y);
            writer.WriteNumber(IndexProperty, index++);
            if (!String.IsNullOrEmpty(ParameterValue))
            {
                writer.WriteString(ParameterProperty, ParameterValue);
            }
            if(IsDisabled)
            {
                writer.WriteBoolean(DisabledProperty, true);
            }
            writer.WriteEndObject();
        }

        internal static bool Load(ModelSystemSession session, Dictionary<int, Type> typeLookup, Dictionary<int, Node> nodes,
            Boundary boundary, ref Utf8JsonReader reader, out Node mss, ref string error)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                return FailWith(out mss, ref error, "Invalid token when loading a start!");
            }
            Type type = null;
            string name = null;
            int index = -1;
            bool disabled = false;
            Point point = new Point();
            string description = null;
            string parameter = null;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType == JsonTokenType.Comment) continue;
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    return FailWith(out mss, ref error, "Invalid token when loading start");
                }
                if(reader.ValueTextEquals(NameProperty))
                {
                    reader.Read();
                    name = reader.GetString();
                }
                else if(reader.ValueTextEquals(DescriptionProperty))
                {
                    reader.Read();
                    description = reader.GetString() ?? string.Empty;
                }
                else if(reader.ValueTextEquals(XProperty))
                {
                    reader.Read();
                    point = new Point(reader.GetSingle(), point.Y);
                }
                else if (reader.ValueTextEquals(YProperty))
                {
                    reader.Read();
                    point = new Point(point.X, reader.GetSingle());
                }
                else if(reader.ValueTextEquals(IndexProperty))
                {
                    reader.Read();
                    index = reader.GetInt32();
                }
                else if(reader.ValueTextEquals(TypeProperty))
                {
                    reader.Read();
                    var typeIndex = reader.GetInt32();
                    if (!typeLookup.TryGetValue(typeIndex, out type))
                    {
                        return FailWith(out mss, ref error, $"Invalid type index {typeIndex}!");
                    }
                }
                else if(reader.ValueTextEquals(ParameterProperty))
                {
                    reader.Read();
                    parameter = reader.GetString() ?? string.Empty;
                }
                else if(reader.ValueTextEquals(DisabledProperty))
                {
                    reader.Read();
                    disabled = reader.GetBoolean();
                }
                else
                {
                    return FailWith(out mss, ref error, $"Undefined parameter type {reader.GetString()} when loading a start!");
                }
            }
            if (name == null)
            {
                return FailWith(out mss, ref error, "Undefined name for a start in boundary " + boundary.FullPath);
            }
            if(index < 0)
            {
                return FailWith(out mss, ref error, $"While loading {boundary.FullPath}.{name} we were unable to parse a valid index!");
            }
            if (nodes.ContainsKey(index))
            {
                return FailWith(out mss, ref error, $"Index {index} already exists!");
            }
            mss = new Node(name)
            {
                Description = description,
                Location = point,
                ContainedWithin = boundary,
                ParameterValue = parameter,
                IsDisabled = disabled
            };
            if (!mss.SetType(session, type, ref error))
            {
                return false;
            }
            nodes.Add(index, mss);
            return true;
        }

        internal static Node Create(ModelSystemSession session, string name, Type type, Boundary boundary)
        {
            string error = null;
            var ret = new Node(name)
            {
                ContainedWithin = boundary
            };
            if (!ret.SetType(session, type, ref error))
            {
                return null;
            }
            return ret;
        }
    }
}
