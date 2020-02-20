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
        protected const string DisabledProperty = "Disabled";

        public Node Origin { get; }
        public NodeHook OriginHook { get; }

        public bool IsDisabled { get; private set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected Link(Node origin, NodeHook hook, bool disabled)
        {
            Origin = origin;
            OriginHook = hook;
            IsDisabled = disabled;
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

        internal static bool Create(ModuleRepository modules, Dictionary<int, Node> nodes, ref Utf8JsonReader reader, out Link? link, ref string? error)
        {
            if(reader.TokenType != JsonTokenType.StartObject)
            {
                return FailWith(out link, out error, "Expected a start object when loading a link.");
            }
            Node? origin = null, destination = null;
            List<Node>? destinations = null;
            string? hookName = null;
            bool disabled = false;
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
                if(reader.ValueTextEquals(OriginProperty))
                {
                    reader.Read();
                    var index = reader.GetInt32();
                    origin = nodes[index];
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
                                destination = nodes[index];
                            }
                            break;
                        case JsonTokenType.StartArray:
                            {
                                destinations = new List<Node>();
                                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                                {
                                    destinations.Add(nodes[reader.GetInt32()]);
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
                else
                {
                    return FailWith(out link, out error, "Unknown parameter type when loading link " + reader.GetString());
                }
            }
            // ensure all of the types were filled out
            if(origin == null)
            {
                return FailWith(out link, out error, "No origin specified on link!");
            }
            if (hookName == null)
            {
                return FailWith(out link, out error, "No origin hook specified on link!");
            }
            if (destination == null && destinations == null)
            {
                return FailWith(out link, out error, "No destination specified on link!");
            }
            var hook = modules[origin!.Type!].Hooks?.FirstOrDefault(h => h.Name.Equals(hookName, StringComparison.OrdinalIgnoreCase));
            if(hook == null)
            {
                return FailWith(out link, out error, "Unable to find a hook with the name " + hookName);
            }
            if (destination != null)
            {
                link = new SingleLink(origin, hook, destination, disabled);
            }
            else
            {
                // destinations can not be null if destination was.
                link = new MultiLink(origin, hook, destinations!, disabled);
            }
            return true;
        }

        internal abstract bool Construct(ref string? error);

        internal bool SetDisabled(ModelSystemSession modelSystemSession, bool disabled, out CommandError? error)
        {
            IsDisabled = disabled;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDisabled)));
            error = null;
            return true;
        }

        internal abstract bool HasDestination(Node destNode);
    }
}
