/*
    Copyright 2017-2019 University of Toronto

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
using System.Text.Json;
using XTMF2.Editing;
using XTMF2.Controllers;
using System.IO;


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

        private readonly Project Project;
        internal string ModelSystemPath => Path.Combine(Project.ProjectDirectory, "ModelSystems", Name + ".xmsys");


        internal ModelSystemHeader(Project project, string name, string description = null)
        {
            Project = project;
            Name = name;
            Description = description;
        }

        public bool SetName(string name, out CommandError error)
        {
            try
            {
                var file = new FileInfo(ModelSystemPath);
                if (file.Exists)
                {
                    file.MoveTo(Path.Combine(file.DirectoryName, name + ".xmsys"));
                }
                Name = name;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
                error = null;
                return true;
            }
            catch(IOException e)
            {
                error = new CommandError(e.Message);
                return false;
            }
        }

        public bool SetDescription(ProjectSession session, string description, out CommandError error)
        {
            Description = description;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description)));
            error = null;
            return true;
        }

        internal void Save(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("Name", Name);
            writer.WriteString("Description", Description);
            writer.WriteEndObject();
        }

        /// <summary>
        /// Load a model system header from the project file.
        /// </summary>
        /// <param name="reader">The reader mid project load</param>
        /// <returns>The parsed model system header</returns>
        internal static ModelSystemHeader Load(Project project, ref Utf8JsonReader reader)
        {
            if(reader.TokenType != JsonTokenType.StartObject)
            {
                throw new ArgumentException(nameof(reader), "Is not processing a model system header!");
            }
            string name = null, description = null;
            while(reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if(reader.TokenType == JsonTokenType.PropertyName)
                {
                    if(reader.ValueTextEquals("Name"))
                    {
                        reader.Read();
                        name = reader.GetString();
                    }
                    else if(reader.ValueTextEquals("Description"))
                    {
                        reader.Read();
                        description = reader.GetString();
                    }
                }
            }
            return new ModelSystemHeader(project, name, description);
        }

        internal static ModelSystemHeader CreateRunHeader(XTMFRuntime runtime)
        {
            return new ModelSystemHeader(null, "Run")
            {
                
            };
        }
    }
}
