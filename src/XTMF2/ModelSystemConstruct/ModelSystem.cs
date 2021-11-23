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
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Linq;
using System.ComponentModel;
using XTMF2.Editing;
using System.IO;
using XTMF2.Repository;
using XTMF2.ModelSystemConstruct;
using System.Diagnostics.CodeAnalysis;

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
            header.PropertyChanged += Header_PropertyChanged;
            GlobalBoundary = new Boundary(GlobalBoundaryName);
        }

        private void Header_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is ModelSystemHeader header)
            {
                switch (e.PropertyName)
                {
                    case nameof(Name):
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
                        break;
                    case nameof(Description):
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description)));
                        break;
                }
            }
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

        private const string GlobalBoundaryName = "global";
        private const string IndexProperty = "Index";
        private const string TypeProperty = "Type";
        private const string TypesProperty = "Types";
        private const string BoundariesProperty = "Boundaries";

        /// <summary>
        /// The lock that must be acquired before editing the model system's attributes.
        /// </summary>
        private readonly object _modelSystemLock = new object();

        public event PropertyChangedEventHandler? PropertyChanged;

        internal bool Save(ref string? error)
        {
            lock (_modelSystemLock)
            {
                try
                {
                    var fileInfo = new FileInfo(Header.ModelSystemPath);
                    var dir = fileInfo.Directory;
                    if (dir is null)
                    {
                        error = $"The provided path '{Header.ModelSystemPath}' was invalid!";
                        return false;
                    }
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
                    return false;
                }
            }
        }

        private bool Save(ref string? error, Stream saveTo, bool leaveOpen)
        {
            try
            {
                using var writer = new Utf8JsonWriter(saveTo);
                var typeDictionary = GlobalBoundary.GetUsedTypes();
                writer.WriteStartObject();
                WriteTypes(writer, typeDictionary);
                WriteBoundaries(writer, typeDictionary);
                writer.WriteEndObject();
                return true;
            }
            catch (IOException e)
            {
                error = e.Message;
                return false;
            }
            finally
            {
                if (!leaveOpen)
                {
                    saveTo.Dispose();
                }
            }
        }

        /// <summary>
        /// Check that all requirements have been met when constructing the model system.
        /// </summary>
        /// <param name="moduleName">The name of the module that is causing the validation error.</param>
        /// <param name="error">An error message if the validation fails.</param>
        /// <returns>True if the validation passes, false otherwise with an error message.</returns>
        internal bool Validate(ref string? moduleName, ref string? error)
        {
            return GlobalBoundary.Validate(ref moduleName, ref error);
        }

        /// <summary>
        /// Generate the concrete model system for execution.
        /// </summary>
        /// <param name="runtime">The XTMF run time that we are executing within.</param>
        /// <param name="error">An error message if it can not be constructed.</param>
        /// <returns>True if it was created, false with message otherwise.</returns>
        internal bool Construct(XTMFRuntime runtime, ref string? error)
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
        internal bool Save(ref string? error, Stream saveTo)
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

        internal static bool Load(string modelSystem, XTMFRuntime runtime, [NotNullWhen(true)] out ModelSystem? ms, [NotNullWhen(false)] ref string? error)
        {
            using var stream = new MemoryStream(Encoding.Unicode.GetBytes(modelSystem));
            var header = ModelSystemHeader.CreateRunHeader(runtime);
            ms = Load(stream, runtime.Modules, header, ref error);
            return ms != null;
        }

        internal static bool Load(ProjectSession session, ModelSystemHeader modelSystemHeader, [NotNullWhen(true)] out ModelSystemSession? msSession, [NotNullWhen(false)] out CommandError? error)
        {
            // the parameters are have already been vetted
            var path = modelSystemHeader.ModelSystemPath;
            var info = new FileInfo(path);
            string? errorString = null;
            error = null;

            try
            {
                ModelSystem? ms;
                if (info.Exists)
                {
                    using var rawStream = File.OpenRead(modelSystemHeader.ModelSystemPath);
                    ms = Load(rawStream, session.GetModuleRepository(), modelSystemHeader, ref errorString);
                }
                else
                {
                    ms = new ModelSystem(modelSystemHeader);
                }
                if (ms == null)
                {
                    msSession = null;
                    // Give a generic error message if one was not already supplied.
                    error = new CommandError(errorString ?? "Unable to create a model system session for the given header.");
                    return false;
                }
                msSession = new ModelSystemSession(session, ms);
                error = null;
                return true;
            }
            catch (IOException e)
            {
                error = new CommandError(e.Message);
                msSession = null;
                return false;
            }
        }

        private static ModelSystem? Load(Stream rawStream, ModuleRepository modules, ModelSystemHeader modelSystemHeader, [NotNullWhen(false)] ref string? error)
        {
            try
            {
                var modelSystem = new ModelSystem(modelSystemHeader);
                using var stream = new MemoryStream();
                rawStream.CopyTo(stream);
                var reader = new Utf8JsonReader(stream.GetBuffer().AsSpan());
                var typeLookup = new Dictionary<int, Type>();
                var nodes = new Dictionary<int, Node>();
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        if (reader.ValueTextEquals(TypesProperty))
                        {
                            if (!LoadTypes(typeLookup, ref reader, ref error))
                            {
                                return null;
                            }
                        }
                        else if (reader.ValueTextEquals(BoundariesProperty))
                        {
                            if (!LoadBoundaries(modules, typeLookup, nodes, ref reader, modelSystem.GlobalBoundary, ref error))
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

        private static bool FailWith(out string error, string message)
        {
            error = message;
            return false;
        }

        private static bool LoadTypes(Dictionary<int, Type> typeLookup, ref Utf8JsonReader reader, [NotNullWhen(false)] ref string? error)
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
            {
                return FailWith(out error, "Expected to read in an array of types!");
            }
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                string? type = null;
                int index = -1;
                if (reader.TokenType != JsonTokenType.StartObject)
                {
                    return FailWith(out error, "Expected a start object token when starting to read in a type.");
                }
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        return FailWith(out error, "Invalid index!");
                    }
                    if (reader.ValueTextEquals(IndexProperty))
                    {
                        reader.Read();
                        if (reader.TokenType != JsonTokenType.Number)
                        {
                            return FailWith(out error, "While reading types we encountered an invalid index!");
                        }
                        index = reader.GetInt32();
                    }
                    else if (reader.ValueTextEquals(TypeProperty))
                    {
                        reader.Read();
                        type = reader.GetString();
                    }
                }
                if (type == null || index < 0)
                {
                    return FailWith(out error, $"An invalid type entry was found!");
                }
                var trueType = Type.GetType(type);
                if (trueType == null)
                {
                    return FailWith(out error, $"Unable to find type {type}!");
                }
                if (typeLookup.ContainsKey(index))
                {
                    return FailWith(out error, $"While reading types the index {index} was previously defined!");
                }
                typeLookup.Add(index, trueType);
            }
            return true;
        }

        private static bool LoadBoundaries(ModuleRepository modules, Dictionary<int, Type> typeLookup, Dictionary<int, Node> nodes,
            ref Utf8JsonReader reader, Boundary global, [NotNullWhen(false)] ref string? error)
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
            {
                return FailWith(out error, "Expected to read an array when loading boundaries!");
            }

            if (!reader.Read())
            {
                return FailWith(out error, "Unexpected end of file when loading boundaries!");
            }

            if(!global.Load(modules, typeLookup, nodes, ref reader, ref error))
            {
                return false;
            }

            if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
            {
                return FailWith(out error, "Expected to only have one boundary defined in the root!");
            }
            return true;
        }
    }
}
