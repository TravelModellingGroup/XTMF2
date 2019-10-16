﻿/*
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
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Linq;
using System.ComponentModel;
using XTMF2.Editing;
using System.IO;

namespace XTMF2
{
    /// <summary>
    /// Provides detailed access to the model system and control it.
    /// </summary>
    public sealed class ModelSystem : INotifyPropertyChanged
    {
        /// <summary>
        /// Create a new model system with the given header information.
        /// </summary>
        /// <param name="header">The header to create a new model system for.</param>
        public ModelSystem(ModelSystemHeader header)
        {
            Header = header;
            GlobalBoundary = new Boundary("global");
        }

        /// <summary>
        /// The name of the model system
        /// </summary>
        public string Name => Header.Name;

        /// <summary>
        /// A description of the model system
        /// </summary>
        public string Description => Header.Description;

        /// <summary>
        /// A reference to the top level boundary of the model system.
        /// </summary>
        public Boundary GlobalBoundary { get; private set; }

        /// <summary>
        /// A reference to the information stored at the project level.
        /// </summary>
        internal ModelSystemHeader Header { get; private set; }

        private const string IndexProperty = "Index";
        private const string TypeProperty = "Type";
        private const string TypesProperty = "Types";
        private const string BoundariesProperty = "Boundaries";

        /// <summary>
        /// The lock that must be acquired before editing the model system's attributes.
        /// </summary>
        private readonly object _modelSystemLock = new object();

        public event PropertyChangedEventHandler PropertyChanged;

        internal bool Save(ref string error)
        {
            lock (_modelSystemLock)
            {
                try
                {
                    var fileInfo = new FileInfo(Header.ModelSystemPath);
                    var dir = fileInfo.Directory;
                    if (!dir.Exists)
                    {
                        dir.Create();
                    }
                    using var stream = fileInfo.Create();
                    return Save(ref error, stream, false);
                }
                catch (IOException e)
                {
                    error = e.Message;
                }
                return false;
            }
        }

        private bool Save(ref string error, Stream saveTo, bool leaveOpen)
        {
            try
            {
                using (var writer = new Utf8JsonWriter(saveTo))
                {
                    var typeDictionary = GlobalBoundary.GetUsedTypes();
                    writer.WriteStartObject();
                    WriteTypes(writer, typeDictionary);
                    WriteBoundaries(writer, typeDictionary);
                    writer.WriteEndObject();
                    return true;
                }
            }
            catch (IOException e)
            {
                error = e.Message;
            }
            return false;
        }

        /// <summary>
        /// Generate the concrete model system for execution.
        /// </summary>
        /// <param name="error">An error message if it can not be constructed.</param>
        /// <returns>True if it was created, false with message otherwise.</returns>
        internal bool Construct(XTMFRuntime runtime, ref string error)
        {
            return GlobalBoundary.ConstructModules(runtime, ref error)
                && GlobalBoundary.ConstructLinks(ref error);
        }

        /// <summary>
        /// Save the data to the given stream.
        /// This will not close the stream.
        /// </summary>
        /// <param name="error">An error message if the save fails.</param>
        /// <param name="saveTo">The stream to save the data to.</param>
        /// <returns></returns>
        internal bool Save(ref string error, Stream saveTo)
        {
            return Save(ref error, saveTo, true);
        }

        private void WriteBoundaries(Utf8JsonWriter writer, Dictionary<Type, int> typeDictionary)
        {
            int index = 0;
            writer.WritePropertyName(BoundariesProperty);
            writer.WriteStartArray();
            Dictionary<Node, int> nodeDictionary = new Dictionary<Node, int>();
            GlobalBoundary.Save(ref index, nodeDictionary, typeDictionary, writer);
            writer.WriteEndArray();
        }

        private static void WriteTypes(Utf8JsonWriter writer, Dictionary<Type, int> typeDictionary)
        {
            writer.WritePropertyName(TypesProperty);
            writer.WriteStartArray();
            foreach (var type in typeDictionary)
            {
                writer.WriteStartObject();
                writer.WriteNumber(IndexProperty, type.Value);
                writer.WriteString(TypeProperty, type.Key.AssemblyQualifiedName);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        /// <summary>
        /// Check to see if the given boundary is within the model system.
        /// </summary>
        /// <param name="boundary"></param>
        /// <returns></returns>
        internal bool Contains(Boundary boundary)
        {
            lock (_modelSystemLock)
            {
                if (GlobalBoundary == boundary)
                {
                    return true;
                }
                return GlobalBoundary.Contains(boundary);
            }
        }

        internal static bool Load(ProjectSession session, ModelSystemHeader modelSystemHeader, out ModelSystemSession msSession, ref string error)
        {
            // the parameters are have already been vetted
            var path = modelSystemHeader.ModelSystemPath;
            var info = new FileInfo(path);
            msSession = new ModelSystemSession(session, modelSystemHeader);
            try
            {
                ModelSystem ms;
                if(info.Exists)
                {
                    using var rawStream = File.OpenRead(modelSystemHeader.ModelSystemPath);
                    ms = Load(rawStream, msSession, modelSystemHeader, ref error);
                }
                else
                {
                    ms = new ModelSystem(modelSystemHeader);
                }
                if (ms == null)
                {
                    msSession = null;
                    return false;
                }
                msSession.ModelSystem = ms;
                return msSession != null;
            }
            catch (IOException e)
            {
                error = e.Message;
                return false;
            }
        }

        internal static bool Load(string modelSystem, XTMFRuntime runtime, out ModelSystem ms, ref string error)
        {
            using (MemoryStream stream = new MemoryStream(Encoding.Unicode.GetBytes(modelSystem)))
            {
                var header = ModelSystemHeader.CreateRunHeader(runtime);
                using var session = ModelSystemSession.CreateRunSession(ProjectSession.CreateRunSession(runtime), header);
                ms = Load(stream, session, header, ref error);
                return ms != null;
            }
        }

        private static ModelSystem Load(Stream rawStream, ModelSystemSession session, ModelSystemHeader modelSystemHeader, ref string error)
        {
            try
            {
                var modelSystem = new ModelSystem(modelSystemHeader);
                using (var stream = new MemoryStream())
                {
                    rawStream.CopyTo(stream);
                    var reader = new Utf8JsonReader(stream.GetBuffer().AsSpan());
                    var typeLookup = new Dictionary<int, Type>();
                    var nodes = new Dictionary<int, Node>();
                    while (reader.Read())
                    {
                        if (reader.TokenType == JsonTokenType.PropertyName)
                        {
                            if(reader.ValueTextEquals(TypesProperty))
                            {
                                if (!LoadTypes(typeLookup, ref reader, ref error))
                                {
                                    return null;
                                }
                            }
                            else if(reader.ValueTextEquals(BoundariesProperty))
                            {
                                if (!LoadBoundaries(session, typeLookup, nodes, ref reader, modelSystem.GlobalBoundary, ref error))
                                {
                                    return null;
                                }
                                break;
                            }
                            else
                            {
                                error = $"Unknown token found '{reader.GetString()}'";
                                return null;
                            }
                        }
                    }
                    return modelSystem;
                }
            }
            catch (JsonException e)
            {
                error = e.Message;
            }
            catch (IOException e)
            {
                error = e.Message;
            }
            return null;
        }

        private static bool FailWith(ref string error, string message)
        {
            error = message;
            return false;
        }

        private static bool LoadTypes(Dictionary<int, Type> typeLookup, ref Utf8JsonReader reader, ref string error)
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
            {
                return FailWith(ref error, "Expected to read in an array of types!");
            }
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                string type = null;
                int index = -1;
                if (reader.TokenType != JsonTokenType.StartObject)
                {
                    return FailWith(ref error, "Expected a start object token when starting to read in a type.");
                }
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        return FailWith(ref error, "Invalid index!");
                    }
                    if(reader.ValueTextEquals(IndexProperty))
                    {
                        reader.Read();
                        if (reader.TokenType != JsonTokenType.Number)
                        {
                            return FailWith(ref error, "While reading types we encountered an invalid index!");
                        }
                        index = reader.GetInt32();
                    }
                    else if(reader.ValueTextEquals(TypeProperty))
                    {
                        reader.Read();
                        type = reader.GetString();
                    }
                }
                if (type == null || index < 0)
                {
                    return FailWith(ref error, $"An invalid type entry was found!");
                }
                var trueType = Type.GetType(type);
                if (trueType == null)
                {
                    return FailWith(ref error, $"Unable to find type {type}!");
                }
                if (typeLookup.ContainsKey(index))
                {
                    return FailWith(ref error, $"While reading types the index {index} was previously defined!");
                }
                typeLookup.Add(index, trueType);
            }
            return true;
        }

        private static bool LoadBoundaries(ModelSystemSession session, Dictionary<int, Type> typeLookup, Dictionary<int, Node> nodes,
            ref Utf8JsonReader reader, Boundary global, ref string error)
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
            {
                return FailWith(ref error, "Expected to read an array when loading boundaries!");
            }
            if (!global.Load(session, typeLookup, nodes, ref reader, ref error))
            {
                return false;
            }

            if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
            {
                return FailWith(ref error, "Expected to only have one boundary defined in the root!");
            }
            return true;
        }
    }
}
