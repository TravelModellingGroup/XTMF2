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
using Newtonsoft.Json;
using XTMF2.RuntimeModules;
using XTMF2.Editing;

namespace XTMF2.ModelSystemConstruct
{
    /// <summary>
    /// A start is a special node where
    /// it has no type and can be used to enter the
    /// model system
    /// </summary>
    public sealed class Start : Node
    {
        public Start(ModelSystemSession session, string startName, Boundary boundary, string description, Point point) : base(startName)
        {
            ContainedWithin = boundary;
            Description = description;
            Location = point;
            string error = null;
            SetType(session, typeof(StartModule), ref error);
        }

        internal override void Save(ref int index, Dictionary<Node, int> moduleDictionary, Dictionary<Type, int> typeDictionary, JsonTextWriter writer)
        {
            moduleDictionary.Add(this, index);
            writer.WriteStartObject();
            writer.WritePropertyName("Name");
            writer.WriteValue(Name);
            writer.WritePropertyName("Description");
            writer.WriteValue(Description);
            writer.WritePropertyName("Index");
            writer.WriteValue(index++);
            writer.WritePropertyName("X");
            writer.WriteValue(Location.X);
            writer.WritePropertyName("Y");
            writer.WriteValue(Location.Y);
            writer.WriteEndObject();
        }

        private static bool FailWith(out Start start, ref string error, string message)
        {
            start = null;
            error = message;
            return false;
        }

        internal static bool Load(ModelSystemSession session, Dictionary<int, Node> nodes,
            Boundary boundary, JsonTextReader reader, out Start start, ref string error)
        {
            if (reader.TokenType != JsonToken.StartObject)
            {
                return FailWith(out start, ref error, "Invalid token when loading a start!");
            }
            string name = null;
            int index = -1;
            Point point = new Point();
            string description = null;
            while (reader.Read() && reader.TokenType != JsonToken.EndObject)
            {
                if (reader.TokenType == JsonToken.Comment) continue;
                if (reader.TokenType != JsonToken.PropertyName)
                {
                    return FailWith(out start, ref error, "Invalid token when loading start");
                }
                switch(reader.Value)
                {
                    case "Name":
                        name = reader.ReadAsString();
                        break;
                    case "Description":
                        description = reader.ReadAsString();
                        break;
                    case "X":
                        point = new Point((float)reader.ReadAsDouble(), point.Y);
                        break;
                    case "Y":
                        point = new Point(point.X, (float)reader.ReadAsDouble());
                        break;
                    case "Index":
                        index = (int)reader.ReadAsInt32();
                        break;
                    default:
                        return FailWith(out start, ref error, $"Undefined parameter type {reader.Value} when loading a start!");
                }
            }
            if (name == null)
            {
                return FailWith(out start, ref error, "Undefined name for a start in boundary " + boundary.FullPath);
            }
            if (nodes.ContainsKey(index))
            {
                return FailWith(out start, ref error, $"Index {index} already exists!");
            }
            start = new Start(session, name, boundary, description, point)
            {
                ContainedWithin = boundary
            };
            nodes.Add(index, start);
            return true;
        }

        /// <summary>
        /// Gets a start path from string
        /// </summary>
        /// <param name="startToExecute">The string containing the start path</param>
        /// <returns>A list of boundaries and the final element being the start</returns>
        internal static List<string> ParseStartString(string startToExecute)
        {
            var ret = new List<string>(4);
            StringBuilder builder = new StringBuilder();
            bool escaped = false;
            foreach (var c in startToExecute)
            {
                if (c == '\\')
                {
                    if (escaped == true)
                    {
                        builder.Append(c);
                    }
                    escaped = !escaped;
                    continue;
                }
                else if (escaped || c != '.')
                {
                    builder.Append(c);
                    escaped = false;
                }
                else
                {
                    ret.Add(builder.ToString());
                    builder.Clear();
                }
            }
            if (builder.Length > 0)
            {
                ret.Add(builder.ToString());
            }
            return ret;
        }
    }
}
