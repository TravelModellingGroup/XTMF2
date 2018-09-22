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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Linq;
using System.ComponentModel;
using XTMF2.Editing;
using System.IO;
using Newtonsoft.Json;

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
                    return Save(ref error, fileInfo.Create(), false);
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
                using (var stream = new StreamWriter(saveTo, Encoding.Unicode, 0x1000, leaveOpen))
                {
                    using (var writer = new JsonTextWriter(stream))
                    {
                        var typeDictionary = GlobalBoundary.GetUsedTypes();
                        writer.WriteStartObject();
                        WriteTypes(writer, typeDictionary);
                        WriteBoundaries(writer, typeDictionary);
                        writer.WriteEndObject();
                        return true;
                    }
                }
            }
            catch (JsonWriterException e)
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

        private void WriteBoundaries(JsonTextWriter writer, Dictionary<Type, int> typeDictionary)
        {
            int index = 0;
            writer.WritePropertyName("Boundaries");
            writer.WriteStartArray();
            Dictionary<ModelSystemStructure, int> moduleDictionary = new Dictionary<ModelSystemStructure, int>();
            GlobalBoundary.Save(ref index, moduleDictionary, typeDictionary, writer);
            writer.WriteEndArray();
        }

        private static void WriteTypes(JsonTextWriter writer, Dictionary<Type, int> typeDictionary)
        {
            writer.WritePropertyName("Types");
            writer.WriteStartArray();
            foreach (var type in typeDictionary)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("Index");
                writer.WriteValue(type.Value);
                writer.WritePropertyName("Type");
                writer.WriteValue(type.Key.AssemblyQualifiedName);
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
                var ms = info.Exists ?
                    Load(File.OpenRead(modelSystemHeader.ModelSystemPath), msSession, modelSystemHeader, ref error)
                    : new ModelSystem(modelSystemHeader);
                if (ms == null)
                {
                    msSession = null;
                    return false;
                }
                msSession.ModelSystem = ms;
                return msSession != null;
            }
            catch(IOException e)
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
                ms = Load(stream, ModelSystemSession.CreateRunSession(ProjectSession.CreateRunSession(runtime), header), header, ref error);
                return ms != null;
            }
        }

        private static ModelSystem Load(Stream rawStream, ModelSystemSession session, ModelSystemHeader modelSystemHeader, ref string error)
        {
            try
            {
                var modelSystem = new ModelSystem(modelSystemHeader);
                using (var stream = new StreamReader(rawStream))
                using (var reader = new JsonTextReader(stream))
                {
                    var typeLookup = new Dictionary<int, Type>();
                    var structures = new Dictionary<int, ModelSystemStructure>();
                    while (reader.Read())
                    {
                        if (reader.TokenType == JsonToken.PropertyName)
                        {
                            switch (reader.Value)
                            {
                                case "Types":
                                    if (!LoadTypes(typeLookup, reader, ref error))
                                    {
                                        return null;
                                    }
                                    break;
                                case "Boundaries":
                                    if (!LoadBoundaries(session, typeLookup, structures, reader, modelSystem.GlobalBoundary, ref error))
                                    {
                                        return null;
                                    }
                                    break;
                                default:
                                    error = $"Unknown token found '{reader.Value}'";
                                    return null;
                            }
                        }
                    }
                    return modelSystem;
                }
            }
            catch (JsonWriterException e)
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

        private static bool LoadTypes(Dictionary<int, Type> typeLookup, JsonTextReader reader, ref string error)
        {
            if (!reader.Read() || reader.TokenType != JsonToken.StartArray)
            {
                return FailWith(ref error, "Expected to read in an array of types!");
            }
            while (reader.Read() && reader.TokenType != JsonToken.EndArray)
            {
                string type = null;
                int index = -1;
                if (reader.TokenType != JsonToken.StartObject)
                {
                    return FailWith(ref error, "Expected a start object token when starting to read in a type.");
                }
                while (reader.Read() && reader.TokenType != JsonToken.EndObject)
                {
                    if (reader.TokenType != JsonToken.PropertyName)
                    {
                        return FailWith(ref error, "Invalid index!");
                    }
                    switch (reader.Value)
                    {
                        case "Index":
                            {
                                var ret = reader.ReadAsInt32();
                                if (ret == null)
                                {
                                    return FailWith(ref error, "While reading types we encountered an invalid index!");
                                }
                                index = (int)ret;
                            }
                            break;
                        case "Type":
                            type = reader.ReadAsString();
                            break;
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

        private static bool LoadBoundaries(ModelSystemSession session, Dictionary<int, Type> typeLookup, Dictionary<int, ModelSystemStructure> structures,
            JsonTextReader reader, Boundary global, ref string error)
        {
            if (!reader.Read() || reader.TokenType != JsonToken.StartArray)
            {
                return FailWith(ref error, "Expected to read an array when loading boundaries!");
            }
            if (!global.Load(session, typeLookup, structures, reader, ref error))
            {
                return false;
            }

            if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
            {
                return FailWith(ref error, "Expected to only have one boundary defined in the root!");
            }
            return true;
        }
    }
}
