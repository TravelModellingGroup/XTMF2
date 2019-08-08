/*
    Copyright 2019 University of Toronto

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
using XTMF2.Editing;

namespace XTMF2.ModelSystemConstruct
{
    /// <summary>
    /// This class is used to add documentation inside of a boundary
    /// </summary>
    public sealed class DocumentationBlock
    {
        /// <summary>
        /// Construct a new documentation block
        /// </summary>
        /// <param name="documentation"></param>
        /// <param name="location"></param>
        public DocumentationBlock(string documentation, Point location)
        {
            Documentation = documentation;
            Location = location;
        }

        /// <summary>
        /// The location to place the Documentation Block within the boundary
        /// </summary>
        public Point Location { get; set; }

        /// <summary>
        /// The documentation string to display
        /// </summary>
        public string Documentation { get; private set; }

        internal void Save(JsonTextWriter writer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("X");
            writer.WriteValue(Location.X);
            writer.WritePropertyName("Y");
            writer.WriteValue(Location.Y);
            writer.WritePropertyName("Documentation");
            writer.WriteValue(Documentation);
            writer.WriteEnd();
        }

        internal static bool Load(JsonTextReader reader, out DocumentationBlock block, ref string error)
        {
            float x = 0, y = 0;
            string documentation = "No documentation";
            while (reader.Read() && reader.TokenType != JsonToken.EndObject)
            {
                if(reader.TokenType == JsonToken.Comment)
                {
                    continue;
                }
                if(reader.TokenType == JsonToken.PropertyName)
                {
                    switch(reader.Value)
                    {
                        case "X":
                            {
                                var temp = reader.ReadAsDouble();
                                if (temp.HasValue)
                                {
                                    x = (float)temp;
                                }
                            }
                            break;
                        case "Y":
                            {
                                var temp = reader.ReadAsDouble();
                                if (temp.HasValue)
                                {
                                    y = (float)temp;
                                }
                            }
                            break;
                        case "Documentation":
                            {
                                var temp = reader.ReadAsString();
                                if(!String.IsNullOrEmpty(temp))
                                {
                                    documentation = temp;
                                }
                            }
                            break;
                    }
                }
            }
            block = new DocumentationBlock(documentation, new Point(x, y));
            return true;
        }
    }
}
