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
using System.Collections.ObjectModel;
using System.Linq;
using XTMF2.Editing;
using System.Diagnostics.CodeAnalysis;

namespace XTMF2.ModelSystemConstruct
{
    public sealed class MultiLink : Link
    {
        private readonly ObservableCollection<Node> _Destinations;
        private readonly ReadOnlyObservableCollection<Node> _destinationsView;

        public MultiLink(Node origin, NodeHook hook, List<Node> destinations, bool disabled)
            : base(origin, hook, disabled)
        {
            _Destinations     = new ObservableCollection<Node>(destinations);
            _destinationsView = new ReadOnlyObservableCollection<Node>(_Destinations);
        }

        /// <summary>
        /// A stable, observable read-only view of this link's destinations.
        /// Subscribing to <see cref="ReadOnlyObservableCollection{T}.CollectionChanged"/>
        /// on this property is safe — the same instance is always returned.
        /// </summary>
        public ReadOnlyObservableCollection<Node> Destinations => _destinationsView;

        internal bool AddDestination(Node destination, [NotNullWhen(false)] out CommandError? error)
        {
            _Destinations.Add(destination);
            error = null;
            return true;
        }

        internal bool AddDestination(Node destination, int index, [NotNullWhen(false)] out CommandError? error)
        {
            _Destinations.Insert(index, destination);
            error = null;   
            return true;
        }

        internal override void Save(Dictionary<Node, int> moduleDictionary, Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteNumber(OriginProperty, moduleDictionary[Origin!]);
            writer.WriteString(HookProperty, OriginHook!.Name);
            writer.WritePropertyName(DestinationProperty);
            writer.WriteStartArray();
            foreach (var dest in _Destinations)
            {
                writer.WriteNumberValue(moduleDictionary[dest]);
            }
            writer.WriteEndArray();
            if (IsDisabled)
            {
                writer.WriteBoolean(DisabledProperty, true);
            }
            writer.WriteEndObject();
        }

        internal override bool Construct(ref string? error)
        {
            // Resolves a destination to its effective Node and per-instance IModule,
            // handling both GhostNode cross-boundary references and FunctionInstance clones.
            static (Node effectiveNode, IModule? destModule) ResolveDest(Node d)
            {
                var r = d is GhostNode gn ? gn.ReferencedNode : d;
                if (r is FunctionInstance fi)
                {
                    var entry = fi.Template.EntryNode;
                    return (entry ?? r, entry is not null ? fi.GetRuntimeModule(entry) : null);
                }
                return (r, r.Module);
            }

            var moduleCount = _Destinations.Count(d =>
            {
                var (node, _) = ResolveDest(d);
                return !node.IsDisabled;
            });
            if(OriginHook!.Cardinality == HookCardinality.AtLeastOne)
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
                OriginHook!.CreateArray(Origin!.Module!, moduleCount);
                int index = 0;
                for (int i = 0; i < _Destinations.Count; i++)
                {
                    var (effectiveDest, destModule) = ResolveDest(_Destinations[i]);
                    if (!effectiveDest.IsDisabled && destModule is not null)
                    {
                        OriginHook.Install(Origin!.Module!, destModule, index++);
                    }
                }
            }
            else
            {
                OriginHook!.CreateArray(Origin!.Module!, 0);
            }
            return true;
        }

        internal void RemoveDestination(int i)
        {
            _Destinations.RemoveAt(i);
        }

        /// <summary>Moves the destination at <paramref name="fromIndex"/> to <paramref name="toIndex"/>.</summary>
        internal void MoveDestination(int fromIndex, int toIndex)
        {
            if (fromIndex == toIndex) return;
            var node = _Destinations[fromIndex];
            _Destinations.RemoveAt(fromIndex);
            _Destinations.Insert(toIndex, node);
        }

        internal override bool HasDestination(Node destNode)
        {
            return _Destinations.Contains(destNode);
        }
    }
}
