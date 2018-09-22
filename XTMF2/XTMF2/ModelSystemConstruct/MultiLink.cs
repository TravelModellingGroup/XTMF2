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
using System.Text;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.Linq;

namespace XTMF2.ModelSystemConstruct
{
    public sealed class MultiLink : Link
    {
        private ObservableCollection<ModelSystemStructure> _Destinations;

        public MultiLink(List<ModelSystemStructure> destinations)
        {
            _Destinations = new ObservableCollection<ModelSystemStructure>(destinations);
        }

        public MultiLink()
        {
            _Destinations = new ObservableCollection<ModelSystemStructure>();
        }

        public ReadOnlyObservableCollection<ModelSystemStructure> Destinations =>
            new ReadOnlyObservableCollection<ModelSystemStructure>(_Destinations);

        internal bool AddDestination(ModelSystemStructure destination, ref string error)
        {
            _Destinations.Add(destination);
            return true;
        }

        internal bool AddDestination(ModelSystemStructure destination, int index)
        {
            _Destinations.Insert(index, destination);
            return true;
        }

        internal override void Save(Dictionary<ModelSystemStructure, int> moduleDictionary, JsonTextWriter writer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("Origin");
            writer.WriteValue(moduleDictionary[Origin]);
            writer.WritePropertyName("Hook");
            writer.WriteValue(OriginHook.Name);
            writer.WritePropertyName("Destination");
            writer.WriteStartArray();
            foreach (var dest in _Destinations)
            {
                writer.WriteValue(moduleDictionary[dest]);
            }
            writer.WriteEndArray();
            if (IsDisabled)
            {
                writer.WritePropertyName("Disabled");
                writer.WriteValue(true);
            }
            writer.WriteEndObject();
        }

        internal override bool Construct(ref string error)
        {
            var moduleCount = _Destinations.Count(d => !d.IsDisabled);
            if(OriginHook.Cardinality == HookCardinality.AtLeastOne)
            {
                if (moduleCount <= 0)
                {
                    error = "At least one module is required as a destination.";
                    return false;
                }
                if(IsDisabled)
                {
                    error = "A required MultiLink is disabled!";
                    return false;
                }
            }
            if(!IsDisabled)
            {
                OriginHook.CreateArray(Origin.Module, moduleCount);
                int index = 0;
                for (int i = 0; i < _Destinations.Count; i++)
                {
                    if (!_Destinations[i].IsDisabled)
                    {
                        OriginHook.Install(Origin, _Destinations[i], index++);
                    }
                }
            }
            else
            {
                OriginHook.CreateArray(Origin.Module, 0);
            }
            return true;
        }

        internal void RemoveDestination(int i)
        {
            _Destinations.RemoveAt(i);
        }
    }
}
