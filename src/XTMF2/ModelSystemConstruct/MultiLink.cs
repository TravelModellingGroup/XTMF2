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
        private readonly List<bool> _hiddenDestinations;
        private readonly ReadOnlyObservableCollection<Node> _destinationsView;

        public MultiLink(Node origin, NodeHook hook, List<Node> destinations, bool disabled, bool orthogonal = false,
            List<bool>? hiddenDestinations = null, Guid id = default)
            : base(origin, hook, disabled, orthogonal, id)
        {
            _Destinations     = new ObservableCollection<Node>(destinations);
            _destinationsView = new ReadOnlyObservableCollection<Node>(_Destinations);
            _hiddenDestinations = hiddenDestinations is null
                ? Enumerable.Repeat(false, destinations.Count).ToList()
                : NormalizeHiddenDestinations(hiddenDestinations, destinations.Count);
        }

        private static List<bool> NormalizeHiddenDestinations(IReadOnlyList<bool> source, int requiredCount)
        {
            var normalized = new List<bool>(requiredCount);
            for (int i = 0; i < requiredCount; i++)
            {
                normalized.Add(i < source.Count && source[i]);
            }
            return normalized;
        }

        /// <summary>
        /// A stable, observable read-only view of this link's destinations.
        /// Subscribing to <see cref="ReadOnlyObservableCollection{T}.CollectionChanged"/>
        /// on this property is safe — the same instance is always returned.
        /// </summary>
        public ReadOnlyObservableCollection<Node> Destinations => _destinationsView;

        internal bool AddDestination(Node destination, [NotNullWhen(false)] out CommandError? error)
            => AddDestination(destination, _Destinations.Count, false, out error);

        internal bool AddDestination(Node destination, int index, [NotNullWhen(false)] out CommandError? error)
            => AddDestination(destination, index, false, out error);

        internal bool AddDestination(Node destination, int index, bool hidden, [NotNullWhen(false)] out CommandError? error)
        {
            if (index < 0 || index > _Destinations.Count)
            {
                error = new CommandError("Destination index out of range for MultiLink insert.");
                return false;
            }
            _Destinations.Insert(index, destination);
            _hiddenDestinations.Insert(index, hidden);
            error = null;   
            return true;
        }

        internal override void Save(Dictionary<Node, int> moduleDictionary, Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString(IdProperty, Id);
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
            if (IsOrthogonal)
            {
                writer.WriteBoolean(OrthogonalProperty, true);
            }
            if (_hiddenDestinations.Any(h => h))
            {
                writer.WritePropertyName(HiddenDestinationsProperty);
                writer.WriteStartArray();
                for (int i = 0; i < _Destinations.Count; i++)
                {
                    writer.WriteBooleanValue(i < _hiddenDestinations.Count && _hiddenDestinations[i]);
                }
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
        }

        public override int DestinationCount => _Destinations.Count;

        public override bool IsDestinationHidden(int destinationIndex)
        {
            if (destinationIndex < 0 || destinationIndex >= _hiddenDestinations.Count)
            {
                return false;
            }
            return _hiddenDestinations[destinationIndex];
        }

        internal override bool SetDestinationHidden(int destinationIndex, bool hidden, [NotNullWhen(false)] out CommandError? error)
        {
            if (destinationIndex < 0 || destinationIndex >= _Destinations.Count)
            {
                error = new CommandError("Destination index out of range for MultiLink.");
                return false;
            }

            _hiddenDestinations[destinationIndex] = hidden;
            Notify(nameof(Destinations));
            error = null;
            return true;
        }

        internal override bool SetAllDestinationsHidden(bool hidden, [NotNullWhen(false)] out CommandError? error)
        {
            for (int i = 0; i < _hiddenDestinations.Count; i++)
            {
                _hiddenDestinations[i] = hidden;
            }
            Notify(nameof(Destinations));
            error = null;
            return true;
        }

        internal override bool Construct(ref string? error, ref Guid? elementId)
        {
            if (Origin.IsDisabled)
            {
                error = null;
                return true;
            }

            // Resolves a destination to its effective Node and per-instance IModule,
            // handling both GhostNode cross-boundary references and FunctionInstance clones.
            static (Node effectiveNode, IModule? destModule, bool isDisabled) ResolveDest(Node d)
            {
                var r = d is GhostNode gn ? gn.ReferencedNode : d;
                if (r is FunctionInstance fi)
                {
                    var entry = fi.Template.EntryNode;
                    var effective = entry ?? r;
                    return (effective, entry is not null ? fi.GetRuntimeModule(entry) : null, fi.IsDisabled || effective.IsDisabled);
                }
                return (r, r.Module, r.IsDisabled);
            }

            // FunctionParameter destinations are handled transitively by FunctionInstance at runtime;
            // exclude them from the count and installation entirely.
            var moduleCount = _Destinations.Count(d =>
            {
                if (d is FunctionParameter) return false;
                var (_, _, isDisabled) = ResolveDest(d);
                return !isDisabled;
            });
            if(OriginHook!.Cardinality == HookCardinality.AtLeastOne)
            {
                if (moduleCount <= 0)
                {
                    error = "At least one module is required as a destination.";
                    elementId = Origin.Id;
                    return false;
                }
                if(IsDisabled)
                {
                    error = "A required MultiLink is disabled!";
                    elementId = Origin.Id;
                    return false;
                }
            }
            if(!IsDisabled)
            {
                OriginHook!.CreateArray(Origin!.Module!, moduleCount);
                int index = 0;
                for (int i = 0; i < _Destinations.Count; i++)
                {
                    // Skip FunctionParameter destinations — resolved transitively via FunctionInstance.
                    if (_Destinations[i] is FunctionParameter) continue;
                    var (_, destModule, isDisabled) = ResolveDest(_Destinations[i]);
                    if (!isDisabled && destModule is not null)
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
            _hiddenDestinations.RemoveAt(i);
        }

        internal bool ReplaceDestination(int index, Node destination, [NotNullWhen(false)] out CommandError? error)
        {
            if (index < 0 || index >= _Destinations.Count)
            {
                error = new CommandError("Destination index out of range for MultiLink replacement.");
                return false;
            }

            _Destinations[index] = destination;
            Notify(nameof(Destinations));
            error = null;
            return true;
        }

        /// <summary>Moves the destination at <paramref name="fromIndex"/> to <paramref name="toIndex"/>.</summary>
        internal void MoveDestination(int fromIndex, int toIndex)
        {
            if (fromIndex == toIndex) return;
            var node = _Destinations[fromIndex];
            var hidden = _hiddenDestinations[fromIndex];
            _Destinations.RemoveAt(fromIndex);
            _hiddenDestinations.RemoveAt(fromIndex);
            _Destinations.Insert(toIndex, node);
            _hiddenDestinations.Insert(toIndex, hidden);
        }

        internal override bool HasDestination(Node destNode)
        {
            return _Destinations.Contains(destNode);
        }

        override internal bool TryGetFirstDestination([NotNullWhen(true)] out object? dest)
        {
            if (_Destinations.Count > 0)
            {
                dest = _Destinations[0];
                return true;
            }
            dest = null;
            return false;
        }
    }
}
