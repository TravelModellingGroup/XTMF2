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
        /// Create the hooks for the model system structure
        /// </summary>
        private void CreateModelSystemStructureHooks(ModelSystemSession session)
        {
            Hooks = session.GetModuleRepository()[_Type].Hooks;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Hooks)));
        }

        public ModelSystemStructureHook[] Hooks;

        /// <summary>
        /// The name of the model system structure
        /// </summary>
        public string Name { get; protected set; }

        public Point Location { get; protected set; }

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

        internal virtual ModelSystemStructure Clone()
        {
            return (ModelSystemStructure)MemberwiseClone();
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
                error = "A name cannot be whitespace.";
                return false;
            }
            Name = name;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            return true;
        }

        /// <summary>
        /// An optional description for this model system structure
        /// </summary>
        public string Description { get; protected set; }

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
            if(type == null)
            {
                error = "The given type was null!";
                return false;
            }
            if (_Type != type)
            {
                _Type = type;
                CreateModelSystemStructureHooks(session);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Type)));
            }
            return true;
        }

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
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }
            return type.Name;
        }


        private static bool FailWith(out ModelSystemStructure mss, ref string error, string message)
        {
            mss = null;
            error = message;
            return false;
        }

        internal virtual void Save(ref int index, Dictionary<Type, int> typeDictionary, JsonTextWriter writer)
        {
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
            writer.WriteEndObject();
        }

        internal static bool Load(Dictionary<int, Type> typeLookup, Dictionary<int, ModelSystemStructure> structures,
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
                        point.X = (float)reader.ReadAsDouble();
                        break;
                    case "Y":
                        point.Y = (float)reader.ReadAsDouble();
                        break;
                    case "Index":
                        index = (int)reader.ReadAsInt32();
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
                    default:
                        return FailWith(out mss, ref error, $"Undefined parameter type {reader.Value} when loading a start!");
                }
            }
            if (name == null)
            {
                return FailWith(out mss, ref error, "Undefined name for a start in boundary " + boundary.FullPath);
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
            };
            structures.Add(index, mss);
            return true;
        }

        internal static ModelSystemStructure Create(ModelSystemSession session, string name, Type type)
        {
            string error = null;
            var ret = new ModelSystemStructure(name);
            if(!ret.SetType(session, type, ref error))
            {
                return null;
            }
            return ret;
        }
    }
}
