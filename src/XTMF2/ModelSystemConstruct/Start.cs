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
using System.Text.Json;
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

        internal override void Save(ref int index, Dictionary<Node, int> moduleDictionary, Dictionary<Type, int> typeDictionary, Utf8JsonWriter writer)
        {
            moduleDictionary.Add(this, index);
            writer.WriteStartObject();
            writer.WriteString(NameProperty, Name);
            writer.WriteString(DescriptionProperty, Description);
            writer.WriteNumber(IndexProperty, index++);
            writer.WriteNumber(XProperty, Location.X);
            writer.WriteNumber(YProperty, Location.Y);
            writer.WriteEndObject();
        }

        private static bool FailWith(out Start start, ref string error, string message)
        {
            start = null;
            error = message;
            return false;
        }

        internal static bool Load(ModelSystemSession session, Dictionary<int, Node> nodes,
            Boundary boundary, ref Utf8JsonReader reader, out Start start, ref string error)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                return FailWith(out start, ref error, "Invalid token when loading a start!");
            }
            string name = null;
            int index = -1;
            Point point = new Point();
            string description = null;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType == JsonTokenType.Comment) continue;
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    return FailWith(out start, ref error, "Invalid token when loading start");
                }
                if(reader.ValueTextEquals(NameProperty))
                {
                    reader.Read();
                    name = reader.GetString();
                }
                else if(reader.ValueTextEquals(DescriptionProperty))
                {
                    reader.Read();
                    description = reader.GetString();
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
                else
                {
                    return FailWith(out start, ref error, $"Undefined parameter type {reader.GetString()} when loading a start!");
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
