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
using System.ComponentModel;
using System.Text;
using Newtonsoft.Json;
using XTMF2.Editing;
using XTMF2.Controller;

namespace XTMF2
{
    /// <summary>
    /// The model system header is the information about the model system contained within the
    /// project file and provides access to manipulate it.
    /// </summary>
    public sealed class ModelSystemHeader : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public string Name { get; private set; }
        public string Description { get; private set; }

        public bool SetName(ProjectSession session, string name, ref string error)
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

        public bool SetDescription(ProjectSession session, string description, ref string error)
        {
            Description = description;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description)));
            return true;
        }

        public bool Load(out ModelSystemSession session, ref string error)
        {
            session = null;
            return false;
        }

        internal void Save(JsonTextWriter writer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("Name");
            writer.WriteValue(Name);
            writer.WritePropertyName("Description");
            writer.WriteValue(Description);
            writer.WriteEndObject();
        }

        /// <summary>
        /// Load a model system header from the project file.
        /// </summary>
        /// <param name="reader">The reader mid project load</param>
        /// <returns>The parsed model system header</returns>
        internal static ModelSystemHeader Load(JsonTextReader reader)
        {
            if(reader.TokenType != JsonToken.StartObject)
            {
                throw new ArgumentException(nameof(reader), "Is not processing a model system header!");
            }
            ModelSystemHeader ret = new ModelSystemHeader();
            while(reader.Read() && reader.TokenType != JsonToken.EndObject)
            {
                if(reader.TokenType == JsonToken.PropertyName)
                {
                    switch(reader.Value)
                    {
                        case "Name":
                            ret.Name = reader.ReadAsString();
                            break;
                        case "Description":
                            ret.Description = reader.ReadAsString();
                            break;
                    }
                }
            }
            return ret;
        }
    }
}
