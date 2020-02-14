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
using System.Text.Json;
using XTMF2.Editing;
using System.ComponentModel;

namespace XTMF2.ModelSystemConstruct
{
    public sealed class SingleLink : Link
    {
        public Node Destination { get; internal set; }
        public bool SetDestination(ModelSystemSession session, Node destination, ref string error)
        {
            Destination = destination;
            Notify(nameof(Destination));
            return true;
        }

        internal override void Save(Dictionary<Node, int> moduleDictionary, Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteNumber(OriginProperty, moduleDictionary[Origin]);
            writer.WriteString(HookProperty, OriginHook.Name);
            writer.WriteNumber(DestinationProperty, moduleDictionary[Destination]);
            if (IsDisabled)
            {
                writer.WriteBoolean(DisabledProperty, true);
            }
            writer.WriteEndObject();
        }

        internal override bool Construct(ref string error)
        {
            // if not optional
            if (OriginHook.Cardinality == HookCardinality.Single)
            {
                if (Destination.IsDisabled)
                {
                    error = "A link destined for a disabled module was not optional.";
                    return false;
                }
                if (IsDisabled)
                {
                    error = "A non optional link is disabled!";
                    return false;
                }
            }
            if (!IsDisabled)
            {
                // The index doesn't matter for this type
                OriginHook.Install(Origin, Destination, 0);
            }
            return true;
        }

        internal override bool HasDestination(Node destNode)
        {
            return Destination == destNode;
        }
    }
}
