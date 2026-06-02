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
using System.Text.Json;
using XTMF2.Editing;
using System.Linq;
using XTMF2.ModelSystemConstruct;
using XTMF2.Repository;
using System.Diagnostics.CodeAnalysis;

namespace XTMF2
{
    /// <summary>
    /// Defines a directional connection between two nodes
    /// </summary>
    public abstract class Link : INotifyPropertyChanged
    {
        protected const string OriginProperty = "Origin";
        protected const string HookProperty = "Hook";
        protected const string DestinationProperty = "Destination";
        protected const string IndexProperty = "Index";
        protected const string IdProperty = "Id";
        protected const string DisabledProperty = "Disabled";
        protected const string OrthogonalProperty = "Orthogonal";

        public Node Origin { get; }
        public NodeHook OriginHook { get; }

        /// <summary>
        /// A stable identifier for this link, preserved across save/load cycles.
        /// </summary>
        public Guid Id { get; }

        public bool IsDisabled { get; private set; }

        /// <summary>
        /// When <c>true</c> this link is rendered using orthogonal (right-angle)
        /// routing instead of the default smooth cubic Bézier curve.
        /// </summary>
        public bool IsOrthogonal { get; private set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected Link(Node origin, NodeHook hook, bool disabled, bool orthogonal = false, Guid id = default)
        {
            Id = id == default ? Guid.NewGuid() : id;
            Origin = origin;
            OriginHook = hook;
            IsDisabled = disabled;
            IsOrthogonal = orthogonal;
        }

        /// <summary>
        /// Invoke this when a property is changed
        /// </summary>
        /// <param name="propertyName">The name of the property that was changed</param>
        protected void Notify(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        internal abstract void Save(Dictionary<Node, int> moduleDictionary, Utf8JsonWriter writer);
        
        private static bool FailWith(out Link? link, out string error, string message)
        {
            link = null;
            error = message;
            return false;
        }

        /// <summary>
        /// Creates a new link from the given JSON reader.
        /// </summary>
        /// <param name="modules">The module repository.</param>
        /// <param name="nodes">The dictionary of nodes.</param>
        /// <param name="reader">The JSON reader.</param>
        /// <param name="link">The created link, null if there is a warning!</param>
        /// <param name="error">The error message if creation fails.</param>
        /// <param name="warnings">Optional list of warnings.</param>
        /// <returns>True if the link was created successfully or if there was only a warning; otherwise, false.</returns>
        internal static bool Create(ModuleRepository modules, Dictionary<int, Node> nodes, ref Utf8JsonReader reader, out Link? link, [NotNullWhen(false)] ref string? error, List<string>? warnings = null)
        {
            if(reader.TokenType != JsonTokenType.StartObject)
            {
                return FailWith(out link, out error, "Expected a start object when loading a link.");
            }
            Node? origin = null, destination = null;
            List<Node>? destinations = null;
            string? hookName = null;
            bool disabled = false;
            bool orthogonal = false;
            Guid linkId = Guid.Empty;
            int listIndex = 0;
            // read in the values
            while(reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if(reader.TokenType == JsonTokenType.Comment)
                {
                    continue;
                }
                if(reader.TokenType != JsonTokenType.PropertyName)
                {
                    return FailWith(out link, out error, "Invalid token when loading a link.");
                }
                if(reader.ValueTextEquals(IdProperty))
                {
                    reader.Read();
                    reader.TryGetGuid(out linkId);
                }
                else if(reader.ValueTextEquals(OriginProperty))
                {
                    reader.Read();
                    var index = reader.GetInt32();
                    nodes.TryGetValue(index, out origin);
                }
                else if(reader.ValueTextEquals(HookProperty))
                {
                    reader.Read();
                    hookName = reader.GetString();
                }
                else if(reader.ValueTextEquals(DestinationProperty))
                {
                    if (!reader.Read())
                    {
                        return FailWith(out link, out error, "No destination specified when loading a link!");
                    }
                    switch (reader.TokenType)
                    {
                        case JsonTokenType.Number:
                            {
                                var index = reader.GetInt32();
                                nodes.TryGetValue(index, out destination);
                            }
                            break;
                        case JsonTokenType.StartArray:
                            {
                                destinations = new List<Node>();
                                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                                {
                                    if (nodes.TryGetValue(reader.GetInt32(), out var dest))
                                        destinations.Add(dest);
                                }
                            }
                            break;
                    }
                }
                else if(reader.ValueTextEquals(IndexProperty))
                {
                    reader.Read();
                    listIndex = reader.GetInt32();

                }
                else if(reader.ValueTextEquals(DisabledProperty))
                {
                    reader.Read();
                    disabled = reader.GetBoolean();
                }
                else if(reader.ValueTextEquals(OrthogonalProperty))
                {
                    reader.Read();
                    orthogonal = reader.GetBoolean();
                }
                else
                {
                    return FailWith(out link, out error, "Unknown parameter type when loading link " + reader.GetString());
                }
            }
            // ensure all of the types were filled out
            if(origin == null)
            {
                // Origin node was not found – likely skipped because its type was missing.
                link = null;
                warnings?.Add("A link could not be loaded because its origin node was not found (possibly skipped due to a missing type) and will be skipped.");
                return true;
            }
            if (hookName == null)
            {
                return FailWith(out link, out error, "No origin hook specified on link!");
            }
            if (destination == null && (destinations == null || destinations.Count == 0))
            {
                // Destination node(s) not found – likely skipped because their type was missing.
                link = null;
                warnings?.Add($"A link from '{origin.Name}' via hook '{hookName}' could not be loaded because its destination node(s) were not found (possibly skipped due to a missing type) and will be skipped.");
                return true;
            }
            var hook = origin is FunctionInstance fi
                ? fi.Hooks.FirstOrDefault(h => h.Name.Equals(hookName, StringComparison.OrdinalIgnoreCase))
                : modules[origin!.Type!].Hooks?.FirstOrDefault(h => h.Name.Equals(hookName, StringComparison.OrdinalIgnoreCase));
            if(hook == null)
            {
                link = null;
                warnings?.Add($"A link from '{origin.Name}' could not be loaded because the hook '{hookName}' was not found on the module and will be skipped.");
                return true;
            }
            if (destination != null)
            {
                link = new SingleLink(origin, hook, destination, disabled, orthogonal, linkId);
            }
            else
            {
                // destinations can not be null if destination was.
                link = new MultiLink(origin, hook, destinations!, disabled, orthogonal, linkId);
            }
            return true;
        }

        internal abstract bool Construct(ref string? error);

        internal bool SetDisabled(bool disabled, [NotNullWhen(false)] out CommandError? error)
        {
            IsDisabled = disabled;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDisabled)));
            error = null;
            return true;
        }

        internal bool SetOrthogonal(bool orthogonal, [NotNullWhen(false)] out CommandError? error)
        {
            IsOrthogonal = orthogonal;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOrthogonal)));
            error = null;
            return true;
        }

        internal abstract bool HasDestination(Node destNode);
    }
}
