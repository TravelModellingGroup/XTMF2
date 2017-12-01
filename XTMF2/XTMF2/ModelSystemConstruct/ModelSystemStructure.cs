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
using System.ComponentModel;
using System.Collections.Generic;
using XTMF2.Editing;
using Newtonsoft.Json;
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
    public class ModelSystemStructure : INotifyPropertyChanged
    {
        /// <summary>
        /// The boundary that this model system structure is contained within
        /// </summary>
        public Boundary ContainedWithin { get; protected set; }

        /// <summary>
        /// Don't use this field as the setter
        /// will properly create the model system structure hooks.
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
        /// Create the hooks for the model system structure
        /// </summary>
        private void CreateModelSystemStructureHooks(ModelSystemSession session)
        {
            Hooks = session.GetModuleRepository()[_Type].Hooks;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Hooks)));
        }

        /// <summary>
        /// Get a readonly list of possible hooks to use to interface with other model system structures.
        /// </summary>
        public IReadOnlyList<ModelSystemStructureHook> Hooks { get; private set; }

        /// <summary>
        /// The name of the model system structure
        /// </summary>
        public string Name { get; protected set; }

        /// <summary>
        /// The location to graphically place this structure within a boundary
        /// </summary>
        public Point Location { get; protected set; }

        /// <summary>
        /// The link to the executing object.
        /// This is only set during a run.
        /// </summary>
        public IModule Module { get; private set; }

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Set the location of the model system structure
        /// </summary>
        /// <param name="x">The horizontal offset</param>
        /// <param name="y">The vertical offset</param>
        internal void SetLocation(float x, float y)
        {
            Location = new Point()
            {
                X = x,
                Y = y
            };
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Location)));
        }

        /// <summary>
        /// Change the name of the model system structure
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
                return FailWith(ref error, "Unable to set the parameter value of a model system structure that lacks a type!");
            }
            if (!ArbitraryParameterParser.Check(Type.GenericTypeArguments[0], value, ref error))
            {
                return FailWith(ref error, $"Unable to create a parse the value {value} for type {Type.GenericTypeArguments[0].FullName}!");
            }
            ParameterValue = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ParameterValue)));
            return true;
        }

        /// <summary>
        /// An optional description for this model system structure
        /// </summary>
        public string Description { get; protected set; }

        private static Type[] EmptyConstructor = new Type[] { };

        private static Type[] RuntimeConstructor = new Type[] { typeof(XTMFRuntime) };

        private static Type GenericParameter = typeof(BasicParameter<>);

        private static ConcurrentDictionary<Type, FieldInfo> GenericValue = new ConcurrentDictionary<Type, FieldInfo>();

        /// <summary>
        /// Setup the module as defind in this model system structure.
        /// </summary>
        /// <param name="error">A description of the error if one occurs</param>
        /// <returns>True if the operation was successful, false otherwise</returns>
        internal bool ConstructModule(XTMFRuntime runtime, ref string error)
        {
            Module = (IModule)(
                Type.GetTypeInfo().GetConstructor(RuntimeConstructor)?.Invoke(new[] { runtime })
                ?? Type.GetTypeInfo().GetConstructor(EmptyConstructor).Invoke(EmptyConstructor));
            if (Type.IsConstructedGenericType && Type.GetGenericTypeDefinition() == GenericParameter)
            {
                var paramType = Type.GenericTypeArguments[0];
                var vals = ArbitraryParameterParser.ArbitraryParameterParse(paramType, ParameterValue, ref error);
                if (!vals.Sucess)
                {
                    return FailWith(ref error, $"Unable to assign the value of {ParameterValue} to type {paramType.FullName}!");
                }
                if (!GenericValue.TryGetValue(_Type, out var info))
                {
                    info = _Type.GetRuntimeField("Value");
                    GenericValue[paramType] = info;
                }
                info.SetValue(Module, vals.Value);
            }
            return true;
        }

        /// <summary>
        /// Change the name of the model system structure
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
        /// Change the type of the model system structure.
        /// </summary>
        /// <param name="session">The current editing session.</param>
        /// <param name="type">The type to set this structure to</param>
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
                CreateModelSystemStructureHooks(session);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Type)));
            }
            return true;
        }

        /// <summary>
        /// Create a new model system struture with name only.
        /// Only invoke this if you are going to set the type explicitly right after.
        /// </summary>
        /// <param name="name">The name of the model system structure</param>
        protected ModelSystemStructure(string name)
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
        /// <param name="mss">The module system structure to null out.</param>
        /// <param name="error">The place to store the message</param>
        /// <param name="message">The message to fail with.</param>
        /// <returns>Always false</returns>
        private static bool FailWith(out ModelSystemStructure mss, ref string error, string message)
        {
            mss = null;
            error = message;
            return false;
        }

        internal bool GetLink(ModelSystemStructureHook hook, out Link link)
        {
            return (link = (from l in ContainedWithin.Links
                            where l.Origin == this && l.OriginHook == hook
                            select l).FirstOrDefault()) != null;
        }

        internal virtual void Save(ref int index, Dictionary<ModelSystemStructure, int> moduleDictionary, Dictionary<Type, int> typeDictionary, JsonTextWriter writer)
        {
            moduleDictionary.Add(this, index);
            writer.WriteStartObject();
            writer.WritePropertyName("Name");
            writer.WriteValue(Name);
            writer.WritePropertyName("Description");
            writer.WriteValue(Description);
            writer.WritePropertyName("Type");
            writer.WriteValue(typeDictionary[Type]);
            writer.WritePropertyName("X");
            writer.WriteValue(Location.X);
            writer.WritePropertyName("Y");
            writer.WriteValue(Location.Y);
            writer.WritePropertyName("Index");
            writer.WriteValue(index++);
            if (!String.IsNullOrEmpty(ParameterValue))
            {
                writer.WritePropertyName("Parameter");
                writer.WriteValue(ParameterValue);
            }
            writer.WriteEndObject();
        }

        internal static bool Load(ModelSystemSession session, Dictionary<int, Type> typeLookup, Dictionary<int, ModelSystemStructure> structures,
            Boundary boundary, JsonTextReader reader, out ModelSystemStructure mss, ref string error)
        {
            if (reader.TokenType != JsonToken.StartObject)
            {
                return FailWith(out mss, ref error, "Invalid token when loading a start!");
            }
            Type type = null;
            string name = null;
            int index = -1;
            Point point = new Point();
            string description = null;
            string parameter = null;
            while (reader.Read() && reader.TokenType != JsonToken.EndObject)
            {
                if (reader.TokenType == JsonToken.Comment) continue;
                if (reader.TokenType != JsonToken.PropertyName)
                {
                    return FailWith(out mss, ref error, "Invalid token when loading start");
                }
                switch (reader.Value)
                {
                    case "Name":
                        name = reader.ReadAsString();
                        break;
                    case "Description":
                        description = reader.ReadAsString();
                        break;
                    case "X":
                        point.X = (float)(reader.ReadAsDouble() ?? 0.0f);
                        break;
                    case "Y":
                        point.Y = (float)(reader.ReadAsDouble() ?? 0.0f);
                        break;
                    case "Index":
                        index = (int)(reader.ReadAsInt32() ?? -1);
                        break;
                    case "Type":
                        {
                            var typeIndex = (int)reader.ReadAsInt32();
                            if (!typeLookup.TryGetValue(typeIndex, out type))
                            {
                                return FailWith(out mss, ref error, $"Invalid type index {typeIndex}!");
                            }
                        }
                        break;
                    case "Parameter":
                        {
                            parameter = reader.ReadAsString();
                        }
                        break;
                    default:
                        return FailWith(out mss, ref error, $"Undefined parameter type {reader.Value} when loading a start!");
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
            if (structures.ContainsKey(index))
            {
                return FailWith(out mss, ref error, $"Index {index} already exists!");
            }
            mss = new ModelSystemStructure(name)
            {
                Description = description,
                Location = point,
                ContainedWithin = boundary,
                ParameterValue = parameter
            };
            if (!mss.SetType(session, type, ref error))
            {
                return false;
            }
            structures.Add(index, mss);
            return true;
        }

        internal static ModelSystemStructure Create(ModelSystemSession session, string name, Type type, Boundary boundary)
        {
            string error = null;
            var ret = new ModelSystemStructure(name)
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
