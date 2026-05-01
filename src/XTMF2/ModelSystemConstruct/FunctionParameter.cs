/*
    Copyright 2026, Travel Modelling Group, University of Toronto

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
using System.Text.Json;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using XTMF2.Editing;

namespace XTMF2.ModelSystemConstruct
{
    /// <summary>
    /// Represents a typed parameter slot on a <see cref="FunctionTemplate"/>.
    /// <para>
    /// A <see cref="FunctionParameter"/> is a virtual "placeholder" node that lives inside
    /// a function template's <see cref="FunctionTemplate.InternalModules"/>. Internal nodes
    /// within that template can draw links <em>to</em> a <see cref="FunctionParameter"/>,
    /// signalling that at run-time the concrete module will be supplied from outside via the
    /// corresponding <see cref="FunctionInstance"/> hook.
    /// </para>
    /// <para>
    /// <see cref="FunctionParameter"/> objects are <b>not</b> stored in
    /// <see cref="Boundary.Modules"/> of <c>InternalModules</c>; they are owned exclusively
    /// by <see cref="FunctionTemplate.FunctionParameters"/> and receive node-dictionary
    /// indices during save so that cross-boundary links can reference them.
    /// </para>
    /// </summary>
    public sealed class FunctionParameter : Node
    {
        // ── JSON property names ───────────────────────────────────────────
        internal const string FpIdProperty    = "Id";
        internal const string FpNameProperty  = "Name";
        internal const string FpTypeProperty  = "Type";
        internal const string FpIndexProperty = "Index";
        internal const string FpXProperty     = "X";
        internal const string FpYProperty     = "Y";
        internal const string FpWProperty     = "Width";
        internal const string FpHProperty     = "Height";

        /// <summary>
        /// The <see cref="FunctionTemplate"/> that owns this parameter.
        /// </summary>
        public FunctionTemplate Template { get; }

        /// <summary>
        /// Constructs a new <see cref="FunctionParameter"/> owned by <paramref name="template"/>.
        /// </summary>
        /// <param name="name">The parameter name; must be unique within the template.</param>
        /// <param name="type">The required module type for this parameter slot.</param>
        /// <param name="template">The owning <see cref="FunctionTemplate"/>.</param>
        /// <param name="location">Canvas position inside <c>InternalModules</c>.</param>
        public FunctionParameter(string name, Type type, FunctionTemplate template, Rectangle location, Guid id = default)
            : base(name, type, template.InternalModules, Array.Empty<NodeHook>(), location, id)
        {
            Template = template;
        }

        // ── Persistence ───────────────────────────────────────────────────

        /// <summary>
        /// Writes this parameter to <paramref name="writer"/> and records its index in
        /// <paramref name="nodeDictionary"/> so that links can reference it later.
        /// </summary>
        internal new void Save(ref int index, Dictionary<Node, int> nodeDictionary,
            Dictionary<Type, int> typeDictionary, Utf8JsonWriter writer)
        {
            if (!nodeDictionary.TryGetValue(this, out int myIndex))
            {
                myIndex = index++;
                nodeDictionary[this] = myIndex;
            }
            writer.WriteStartObject();
            writer.WriteString(FpIdProperty, Id);
            writer.WriteString(FpNameProperty, Name);
            writer.WriteNumber(FpTypeProperty, typeDictionary[Type!]);
            writer.WriteNumber(FpIndexProperty, myIndex);
            writer.WriteNumber(FpXProperty, Location.X);
            writer.WriteNumber(FpYProperty, Location.Y);
            writer.WriteNumber(FpWProperty, Location.Width);
            writer.WriteNumber(FpHProperty, Location.Height);
            writer.WriteEndObject();
        }

        /// <summary>
        /// Reads a <see cref="FunctionParameter"/> from <paramref name="reader"/> and
        /// registers it in <paramref name="nodeDictionary"/>.
        /// </summary>
        internal static bool Load(
            Dictionary<int, Type> typeLookup,
            Dictionary<int, Node> nodeDictionary,
            ref Utf8JsonReader reader,
            FunctionTemplate owningTemplate,
            [NotNullWhen(true)] out FunctionParameter? parameter,
            [NotNullWhen(false)] ref string? error)
        {
            parameter = null;

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                error = "Expected StartObject when loading FunctionParameter.";
                return false;
            }

            string? name     = null;
            Type?   type     = null;
            int     fpIndex  = -1;
            Guid    id       = Guid.Empty;
            float   x = 80f, y = 80f, w = 140f, h = 40f;

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName) continue;

                if (reader.ValueTextEquals(FpIdProperty))
                {
                    reader.Read();
                    reader.TryGetGuid(out id);
                }
                else if (reader.ValueTextEquals(FpNameProperty))
                {
                    reader.Read();
                    name = reader.GetString();
                }
                else if (reader.ValueTextEquals(FpTypeProperty))
                {
                    reader.Read();
                    var ti = reader.GetInt32();
                    if (!typeLookup.TryGetValue(ti, out type))
                    {
                        error = $"FunctionParameter: unknown type index {ti}.";
                        return false;
                    }
                }
                else if (reader.ValueTextEquals(FpIndexProperty))
                {
                    reader.Read();
                    fpIndex = reader.GetInt32();
                }
                else if (reader.ValueTextEquals(FpXProperty)) { reader.Read(); x = reader.GetSingle(); }
                else if (reader.ValueTextEquals(FpYProperty)) { reader.Read(); y = reader.GetSingle(); }
                else if (reader.ValueTextEquals(FpWProperty)) { reader.Read(); w = reader.GetSingle(); }
                else if (reader.ValueTextEquals(FpHProperty)) { reader.Read(); h = reader.GetSingle(); }
                else reader.Skip();
            }

            if (name is null)
            {
                error = "FunctionParameter is missing its Name.";
                return false;
            }
            if (type is null)
            {
                error = $"FunctionParameter '{name}' is missing its Type.";
                return false;
            }
            if (fpIndex < 0)
            {
                error = $"FunctionParameter '{name}' is missing a valid Index.";
                return false;
            }

            parameter = new FunctionParameter(name, type, owningTemplate, new Rectangle(x, y, w, h), id);
            nodeDictionary[fpIndex] = parameter;
            error = null;
            return true;
        }
    }
}
