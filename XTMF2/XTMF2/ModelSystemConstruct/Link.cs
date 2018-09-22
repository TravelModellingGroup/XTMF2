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
using System.Linq;
using XTMF2.ModelSystemConstruct;

namespace XTMF2
{
    /// <summary>
    /// Defines a directional connection between two model system structures
    /// </summary>
    public abstract class Link : INotifyPropertyChanged
    {
        public ModelSystemStructure Origin { get; internal set; }
        public ModelSystemStructureHook OriginHook { get; internal set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public bool SetOrigin(ModelSystemSession session, ModelSystemStructure origin, ModelSystemStructureHook originHook, ref string error)
        {
            Origin = origin;
            OriginHook = originHook;
            Notify(nameof(Origin));
            Notify(nameof(OriginHook));
            return true;
        }

        /// <summary>
        /// Invoke this when a property is changed
        /// </summary>
        /// <param name="propertyName">The name of the property that was changed</param>
        protected void Notify(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        internal abstract void Save(Dictionary<ModelSystemStructure, int> moduleDictionary, JsonTextWriter writer);
        
        private static bool FailWith(out Link link, ref string error, string message)
        {
            link = null;
            error = message;
            return false;
        }

        internal static bool Create(ModelSystemSession session, Dictionary<int, ModelSystemStructure> structures, JsonTextReader reader, out Link link, ref string error)
        {
            if(reader.TokenType != JsonToken.StartObject)
            {
                return FailWith(out link, ref error, "Expected a start object when loading a link.");
            }
            ModelSystemStructure origin = null, destination = null;
            List<ModelSystemStructure> destinations = null;
            string hookName = null;
            int listIndex = 0;
            // read in the values
            while(reader.Read() && reader.TokenType != JsonToken.EndObject)
            {
                if(reader.TokenType == JsonToken.Comment)
                {
                    continue;
                }
                if(reader.TokenType != JsonToken.PropertyName)
                {
                    return FailWith(out link, ref error, "Invalid token when loading a link.");
                }
                switch(reader.Value)
                {
                    case "Origin":
                        {
                            var index = (int)reader.ReadAsInt32();
                            origin = structures[index];
                        }
                        break;
                    case "Hook":
                        {
                            hookName = reader.ReadAsString();
                        }
                        break;
                    case "Destination":
                        {
                            if (!reader.Read())
                            {
                                return FailWith(out link, ref error, "No destination specified when loading a link!");
                            }
                            switch (reader.TokenType)
                            {
                                case JsonToken.Integer:
                                    {
                                        var index = (int)(long)reader.Value;
                                        destination = structures[index];
                                    }
                                    break;
                                case JsonToken.StartArray:
                                    {
                                        destinations = new List<ModelSystemStructure>();
                                        while(reader.Read() && reader.TokenType != JsonToken.EndArray)
                                        {
                                            destinations.Add(structures[(int)(long)reader.Value]);
                                        }
                                    }
                                    break;
                            }
                        }
                        break;
                    case "Index":
                        {
                            listIndex = (int)reader.ReadAsInt32();
                        }
                        break;
                    default:
                        return FailWith(out link, ref error, "Unknown parameter type when loading link " + reader.Value);
                }
            }
            // ensure all of the types were filled out
            if(origin == null)
            {
                return FailWith(out link, ref error, "No origin specified on link!");
            }
            if (hookName == null)
            {
                return FailWith(out link, ref error, "No origin hook specified on link!");
            }
            if (destination == null && destinations == null)
            {
                return FailWith(out link, ref error, "No destination specified on link!");
            }
            var hook = session.GetModuleRepository()[origin.Type].Hooks?.FirstOrDefault(h => h.Name.Equals(hookName, StringComparison.OrdinalIgnoreCase));
            if(hook == null)
            {
                return FailWith(out link, ref error, "Unable to find a hook with the name " + hookName);
            }
            if (destination != null)
            {
                link = new SingleLink()
                {
                    Origin = origin,
                    OriginHook = hook,
                    Destination = destination
                };
            }
            else
            {
                link = new MultiLink(destinations)
                {
                    Origin = origin,
                    OriginHook = hook
                };
            }
            return true;
        }

        internal abstract bool Construct(ref string error);
        
    }
}
