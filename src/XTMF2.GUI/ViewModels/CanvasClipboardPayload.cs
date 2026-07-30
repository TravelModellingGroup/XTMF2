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
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XTMF2.GUI.ViewModels;

// ── Clipboard element kinds ───────────────────────────────────────────────────
// The "kind" string stored in CanvasElementDto discriminates between element types.
// Values are stable and used as JSON literals — do not rename.
internal static class CanvasElementKind
{
    public const string Node             = "Node";
    public const string CommentBlock     = "CommentBlock";
    public const string FunctionTemplate = "FunctionTemplate";
    public const string FunctionInstance = "FunctionInstance";
    public const string GhostNode        = "GhostNode";
}

// ── Data-transfer types ───────────────────────────────────────────────────────

/// <summary>
/// Root envelope written to the system clipboard.
/// </summary>
/// <param name="Source">Always <c>"XTMF2Canvas"</c>.  Used to detect that the clipboard text
/// was produced by this application.</param>
/// <param name="Version">Payload schema version (currently <c>1</c>).</param>
/// <param name="Elements">The copied canvas elements.</param>
internal sealed record CanvasClipboardPayload(
    [property: JsonPropertyName("source")]   string Source,
    [property: JsonPropertyName("version")]  int    Version,
    [property: JsonPropertyName("elements")] List<CanvasElementDto> Elements);

/// <summary>
/// A single canvas element captured during a Copy operation.
/// All element types share this record; unused fields are omitted from JSON.
/// </summary>
/// <param name="Kind">Discriminator — one of the <see cref="CanvasElementKind"/> constants.</param>
/// <param name="Name">Display name of the element.</param>
/// <param name="X">Canvas X coordinate.</param>
/// <param name="Y">Canvas Y coordinate.</param>
/// <param name="W">Canvas width.</param>
/// <param name="H">Canvas height.</param>
/// <param name="TypeName">Assembly-qualified CLR type name. Applies to <see cref="CanvasElementKind.Node"/>.</param>
/// <param name="ParameterValue">Serialised parameter value. Applies to <see cref="CanvasElementKind.Node"/>.</param>
/// <param name="IsScriptedParam">
/// <c>true</c> when the node's parameter is a <c>ScriptedParameter</c>.
/// Applies to <see cref="CanvasElementKind.Node"/>.
/// </param>
/// <param name="InlinedChildren">
/// Hidden (inlined) child parameter nodes attached to this node's hooks.
/// Applies to <see cref="CanvasElementKind.Node"/>.
/// </param>
/// <param name="TemplateName">
/// Name of the referenced function template.
/// Applies to <see cref="CanvasElementKind.FunctionInstance"/>.
/// </param>
/// <param name="EmbeddedTemplateSnapshot">
/// Full snapshot of the referenced <see cref="CanvasElementKind.FunctionTemplate"/>,
/// including internal nodes/links/entry-node/local variables. Used for cross-model-system
/// paste and dedupe checks.
/// Applies to <see cref="CanvasElementKind.FunctionTemplate"/> and
/// <see cref="CanvasElementKind.FunctionInstance"/>.
/// </param>
/// <param name="ReferencedNodeName">
/// Name of the real node that the ghost represents.
/// Applies to <see cref="CanvasElementKind.GhostNode"/>.
/// </param>
/// <param name="FunctionParameters">
/// Ordered list of function-parameter names.
/// Applies to <see cref="CanvasElementKind.FunctionTemplate"/>.
/// </param>
/// <param name="CrossLinks">
/// Links from this <see cref="CanvasElementKind.Node"/> to other nodes that were also
/// part of the same copy operation.  Destination is identified by the element's
/// <see cref="CanvasElementDto.Name"/> within the same payload.
/// </param>
/// <param name="IsTemplateCompanion">
/// <c>true</c> when this <see cref="CanvasElementKind.FunctionTemplate"/> entry was
/// auto-inserted as a companion payload for a copied
/// <see cref="CanvasElementKind.FunctionInstance"/>, rather than explicitly copied by the user.
/// Companion templates should prefer reusing an equivalent existing template on paste.
/// </param>
internal sealed record CanvasElementDto(
    [property: JsonPropertyName("kind")]               string  Kind,
    [property: JsonPropertyName("name")]               string  Name,
    [property: JsonPropertyName("x")]                  float   X,
    [property: JsonPropertyName("y")]                  float   Y,
    [property: JsonPropertyName("w")]                  float   W,
    [property: JsonPropertyName("h")]                  float   H,
    [property: JsonPropertyName("typeName")]           string?              TypeName            = null,
    [property: JsonPropertyName("paramValue")]         string?              ParameterValue      = null,
    [property: JsonPropertyName("isScriptedParam")]    bool                 IsScriptedParam     = false,
    [property: JsonPropertyName("inlinedChildren")]    List<InlinedChildDto>? InlinedChildren   = null,
    [property: JsonPropertyName("templateName")]       string?              TemplateName        = null,
    [property: JsonPropertyName("embeddedTemplateSnapshot")] string?         EmbeddedTemplateSnapshot = null,
    [property: JsonPropertyName("referencedNodeName")] string?             ReferencedNodeName  = null,
    [property: JsonPropertyName("functionParameters")] List<FunctionParameterDto>? FunctionParameters = null,
    [property: JsonPropertyName("crossLinks")]         List<CrossNodeLinkDto>? CrossLinks        = null,
    [property: JsonPropertyName("isTemplateCompanion")] bool               IsTemplateCompanion = false
);

/// <summary>
/// A link from one copied <see cref="CanvasElementKind.Node"/> to another node that
/// was in the same copy selection.  Stored per-origin so it can be recreated on paste
/// once all nodes have been constructed.
/// </summary>
/// <param name="HookName">The name of the hook on the <em>origin</em> node.</param>
/// <param name="DestName">
/// The <see cref="CanvasElementDto.Name"/> of the destination node within the same
/// <see cref="CanvasClipboardPayload"/>.
/// </param>
internal sealed record CrossNodeLinkDto(
    [property: JsonPropertyName("hookName")] string HookName,
    [property: JsonPropertyName("destName")] string DestName);

/// <summary>
/// A single function-parameter slot captured from a <see cref="CanvasElementKind.FunctionTemplate"/>.
/// </summary>
/// <param name="Name">Display name of the parameter.</param>
/// <param name="TypeName">Assembly-qualified CLR type name, used to recreate the parameter on paste.</param>
internal sealed record FunctionParameterDto(
    [property: JsonPropertyName("name")]     string  Name,
    [property: JsonPropertyName("typeName")] string? TypeName = null);

/// <summary>
/// An inlined (hidden) child parameter node that was connected to one of the copied
/// node's hooks at copy-time.
/// </summary>
internal sealed record InlinedChildDto(
    [property: JsonPropertyName("hookName")] string         HookName,
    [property: JsonPropertyName("child")]    CanvasElementDto Child);

// ── Serialisation helpers ─────────────────────────────────────────────────────

/// <summary>
/// Serialises / deserialises a <see cref="CanvasClipboardPayload"/> to and from
/// the system clipboard text format.
/// <para>
/// The clipboard text is a compact JSON object whose first field is <c>"source":"XTMF2Canvas"</c>.
/// Only text that starts with this marker is considered XTMF2 clipboard data;
/// all other clipboard text is silently ignored by <see cref="TryDeserialize"/>.
/// </para>
/// </summary>
internal static class CanvasClipboardSerializer
{
    // Marker prefix embedded in the JSON itself (checked before full deserialisation).
    private const string SourceValue = "XTMF2Canvas";
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Creates a new <see cref="CanvasClipboardPayload"/> with the supplied elements
    /// and the current schema version.
    /// </summary>
    public static CanvasClipboardPayload CreatePayload(List<CanvasElementDto> elements)
        => new(SourceValue, CurrentVersion, elements);

    /// <summary>
    /// Serialises <paramref name="payload"/> to a JSON string suitable for
    /// <see cref="Avalonia.Input.Platform.IClipboard.SetTextAsync"/>.
    /// </summary>
    public static string Serialize(CanvasClipboardPayload payload)
        => JsonSerializer.Serialize(payload, SerializerOptions);

    /// <summary>
    /// Attempts to deserialise the clipboard text returned by
    /// <see cref="Avalonia.Input.Platform.IClipboard.GetTextAsync"/>.
    /// Returns <c>null</c> when:
    /// <list type="bullet">
    ///   <item><paramref name="text"/> is <c>null</c> or blank.</item>
    ///   <item>The JSON does not contain the XTMF2 source marker.</item>
    ///   <item>Deserialisation throws any exception.</item>
    /// </list>
    /// </summary>
    public static CanvasClipboardPayload? TryDeserialize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        // Quick reject without full deserialisation.
        if (!text.Contains(SourceValue, StringComparison.Ordinal)) return null;
        try
        {
            var payload = JsonSerializer.Deserialize<CanvasClipboardPayload>(text, SerializerOptions);
            if (payload?.Source != SourceValue) return null;
            return payload;
        }
        catch
        {
            return null;
        }
    }
}
