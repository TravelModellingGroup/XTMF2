/*
    Copyright 2026 University of Toronto

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
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace XTMF2.ModelSystemConstruct;

/// <summary>
/// A visual alias for another <see cref="Node"/> that may reside on a different
/// <see cref="Boundary"/>.
/// <para>
/// Ghost nodes always mirror the name of their referenced node, expose no hooks,
/// and are rendered on the canvas with a dashed outline.  They can be link
/// <em>destinations</em> (links drawn to them are displayed on the canvas) but
/// they never act as link origins.  When the real node is deleted all ghost nodes
/// that reference it are automatically removed.
/// </para>
/// </summary>
public sealed class GhostNode : Node
{
    // ── JSON property names (only what is unique to GhostNode) ───────
    internal const string ReferencedNodeProperty = "ReferencedNode";

    /// <summary>The real node that this ghost node visually represents.</summary>
    public Node ReferencedNode { get; }

    /// <summary>
    /// Creates a ghost node that mirrors <paramref name="referencedNode"/>,
    /// placed at <paramref name="location"/> inside <paramref name="containedWithin"/>.
    /// </summary>
    internal GhostNode(Node referencedNode, Boundary containedWithin, Rectangle location, Guid id = default)
        : base(referencedNode.Name, null!, containedWithin, Array.Empty<NodeHook>(), location, id)
    {
        ReferencedNode = referencedNode;
        // Track name changes on the referenced node.
        ((INotifyPropertyChanged)referencedNode).PropertyChanged += OnReferencedNodePropertyChanged;
    }

    private void OnReferencedNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Name))
        {
            Name = ReferencedNode.Name;
            InvokePropertyChanged(nameof(Name));
        }
    }

    /// <summary>
    /// Ghost nodes do not write themselves in a boundary's Nodes array.
    /// They are indexed here (so links can reference them) and serialised
    /// by <see cref="Boundary"/> in a dedicated GhostNodes array.
    /// </summary>
    internal override void Save(ref int index, Dictionary<Node, int> nodeDictionary,
        Dictionary<Type, int> typeDictionary, Utf8JsonWriter writer)
    {
        // Index this ghost node so links can reference it by index.
        // The actual JSON object is written separately by Boundary.SaveGhostNodes.
        if (!nodeDictionary.TryGetValue(this, out _))
            nodeDictionary[this] = index++;
    }

    /// <summary>
    /// Writes the standalone JSON object for this ghost node.
    /// Called by <see cref="Boundary"/> during save after all regular nodes and child
    /// boundaries have been assigned their indices.
    /// </summary>
    internal void SaveObject(Dictionary<Node, int> nodeDictionary, Utf8JsonWriter writer)
    {
        // Guard against a referenced node that was removed without cascade-deleting
        // this ghost (should not happen in a healthy model, but avoids a hard crash).
        if (!nodeDictionary.TryGetValue(ReferencedNode, out int refIdx))
            return;
        writer.WriteStartObject();
        writer.WriteString(IdProperty, Id);
        writer.WriteNumber(ReferencedNodeProperty, refIdx);
        writer.WriteNumber(XProperty, Location.X);
        writer.WriteNumber(YProperty, Location.Y);
        writer.WriteNumber(WidthProperty, Location.Width);
        writer.WriteNumber(HeightProperty, Location.Height);
        writer.WriteNumber(IndexProperty, nodeDictionary[this]);
        writer.WriteEndObject();
    }

    /// <summary>
    /// Reads a ghost node entry from JSON and appends a deferred-resolution record.
    /// The ghost node is fully constructed later by <see cref="Resolve"/> once all
    /// node indices are available.
    /// </summary>
    internal static bool LoadDeferred(
        ref Utf8JsonReader reader,
        Boundary boundary,
        List<(Boundary ContainedIn, int RefIndex, int SelfIndex, Rectangle Location, Guid Id)> deferreds,
        [NotNullWhen(false)] ref string? error)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            error = "Expected a start object when loading a ghost node.";
            return false;
        }

        int refIndex = -1, selfIndex = -1;
        Guid id = Guid.Empty;
        Rectangle location = new Rectangle();

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType == JsonTokenType.Comment) continue;
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                error = "Invalid token when loading a ghost node.";
                return false;
            }

            if (reader.ValueTextEquals(IdProperty))
            {
                reader.Read();
                reader.TryGetGuid(out id);
            }
            else if (reader.ValueTextEquals(ReferencedNodeProperty))
            {
                reader.Read();
                refIndex = reader.GetInt32();
            }
            else if (reader.ValueTextEquals(XProperty))
            {
                reader.Read();
                location = new Rectangle(reader.GetSingle(), location.Y, location.Width, location.Height);
            }
            else if (reader.ValueTextEquals(YProperty))
            {
                reader.Read();
                location = new Rectangle(location.X, reader.GetSingle(), location.Width, location.Height);
            }
            else if (reader.ValueTextEquals(WidthProperty))
            {
                reader.Read();
                location = new Rectangle(location.X, location.Y, reader.GetSingle(), location.Height);
            }
            else if (reader.ValueTextEquals(HeightProperty))
            {
                reader.Read();
                location = new Rectangle(location.X, location.Y, location.Width, reader.GetSingle());
            }
            else if (reader.ValueTextEquals(IndexProperty))
            {
                reader.Read();
                selfIndex = reader.GetInt32();
            }
            else
            {
                // Skip unknown fields for forward compatibility.
                reader.Read();
            }
        }

        if (refIndex < 0)
        {
            error = "Ghost node is missing its referenced node index.";
            return false;
        }
        if (selfIndex < 0)
        {
            error = "Ghost node is missing its own index.";
            return false;
        }

        deferreds.Add((boundary, refIndex, selfIndex, location, id));
        return true;
    }

    /// <summary>
    /// Resolves a previously deferred ghost node, creating the object and
    /// inserting it into the global <paramref name="nodes"/> dictionary.
    /// </summary>
    internal static bool Resolve(
        Dictionary<int, Node> nodes,
        Boundary containedIn,
        int refIndex,
        int selfIndex,
        Rectangle location,
        Guid id,
        [NotNullWhen(true)] out GhostNode? ghost,
        [NotNullWhen(false)] ref string? error)
    {
        if (!nodes.TryGetValue(refIndex, out var refNode))
        {
            ghost = null;
            error = $"Ghost node references unknown node index {refIndex}.";
            return false;
        }

        ghost = new GhostNode(refNode, containedIn, location, id);
        nodes[selfIndex] = ghost;
        return true;
    }
}
