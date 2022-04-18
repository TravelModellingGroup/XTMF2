/*
    Copyright 2021, Travel Modelling Group, University of Toronto

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
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using XTMF2.Editing;
using XTMF2.Repository;

namespace XTMF2.ModelSystemConstruct
{
    /// <summary>
    /// This class provides the logic for creating a function template.
    /// Function templates are then used in a model system by instantiating all of the
    /// needed references.
    /// </summary>
    public sealed class FunctionTemplate : INotifyPropertyChanged
    {
        private string _name = String.Empty;

        /// <summary>
        /// The unique name of the function template within containing boundary
        /// </summary>
        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }

        /// <summary>
        /// This boundary provides the location for modules that are contained within the function template.
        /// These modules can not be referenced from outside of the function template.
        /// </summary>
        public Boundary InternalModules { get; }

        /// <summary>
        /// The boundary that this function template belongs to.
        /// </summary>
        public Boundary Parent { get; }

        /// <summary>
        /// Construct a new function template
        /// </summary>
        /// <param name="name">The name of the function template.</param>
        /// <param name="parent">The boundary that owns this function template.</param>
        public FunctionTemplate(string name, Boundary parent)
        {
            _name = name;
            Parent = parent;
            InternalModules = new Boundary("InternalModules", parent);
        }

        /// <summary>
        /// Save the function template to the stream.
        /// </summary>
        /// <param name="index">A counting for module indexes.</param>
        /// <param name="nodeDictionary">An lookup given an index of contained nodes.</param>
        /// <param name="typeDictionary">The known types and indexes for them.</param>
        /// <param name="writer">The stream that is being written to.</param>
        internal void Save(ref int index, Dictionary<Node, int> nodeDictionary, Dictionary<Type, int> typeDictionary, Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("Name", Name);
            writer.WritePropertyName(nameof(InternalModules));
            InternalModules.Save(ref index, nodeDictionary, typeDictionary, writer);
            writer.WriteEndObject();
        }

        /// <summary>
        /// Load the Function template from the given stream.
        /// </summary>
        /// <param name="modules">The repository of modules.</param>
        /// <param name="typeLookup">A lookup from index to type.</param>
        /// <param name="node">A reference from node index to node object.</param>
        /// <param name="reader">The reader to use for parsing the FunctionTemplate and its children.</param>
        /// <param name="parent">The boundary that contains this function template.</param>
        /// <param name="template">The function template that was created by loading the file.</param>
        /// <param name="error">An error message if we failed to load the function template or its children.</param>
        /// <returns>True if the operation succeeded, false otherwise with an error message.</returns>
        internal static bool Load(ModuleRepository modules, Dictionary<int, Type> typeLookup, Dictionary<int, Node> node, List<(Node toAssignTo, string parameterExpression)> scriptedParameters,
            ref Utf8JsonReader reader, Boundary parent, [NotNullWhen(true)] out FunctionTemplate? template, [NotNullWhen(false)] ref string? error)
        {
            template = null;
            string? name = null;
            var innerModules = new Boundary(parent);
            if(reader.TokenType != JsonTokenType.StartObject)
            {
                return Helper.FailWith(out error, "Unexpected token when reading FunctionTemplate!");
            }
            while(reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if(reader.TokenType != JsonTokenType.PropertyName)
                {
                    continue;
                }
                if(reader.ValueTextEquals(nameof(Name)))
                {
                    reader.Read();
                    name = reader.GetString();
                }
                else if(reader.ValueTextEquals(nameof(InternalModules)))
                {
                    reader.Read();
                    if(!innerModules.Load(modules, typeLookup, node, scriptedParameters, ref reader, ref error))
                    {
                        return false;
                    }
                }
            }
            if(name is null)
            {
                return Helper.FailWith(out error, "Function template did not include a name!");
            }
            template = new FunctionTemplate(name, parent);
            return true;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
