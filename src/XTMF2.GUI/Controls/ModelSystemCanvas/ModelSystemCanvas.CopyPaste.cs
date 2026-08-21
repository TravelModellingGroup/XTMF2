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
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using XTMF2.GUI.ViewModels;
using XTMF2.ModelSystemConstruct;

namespace XTMF2.GUI.Controls;

partial class ModelSystemCanvas
{

    // ── Copy / Paste helpers ──────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="CanvasElementDto"/> for a <see cref="NodeViewModel"/>,
    /// including any inlined child parameter nodes.
    /// </summary>
    private CanvasElementDto BuildNodeDto(NodeViewModel nvm)
    {
        var node = nvm.UnderlyingNode;
        string? paramValue = null;
        bool isScriptedParam = false;
        if (nvm.IsParameterNode && node.ParameterValue is { } pv)
        {
            paramValue = pv.Representation;
            isScriptedParam = nvm.IsScriptedParameter;
        }

        // Collect inlined (hidden) child parameter nodes.
        List<InlinedChildDto>? inlined = null;
        foreach (var kvp in _hookInlinedParam)
        {
            if (!ReferenceEquals(kvp.Key.Item1, nvm)) continue;
            inlined ??= [];
            var childDto = BuildNodeDto(kvp.Value) with
            {
                X = kvp.Value.UnderlyingNode.Location.X,
                Y = kvp.Value.UnderlyingNode.Location.Y,
                W = (float)NodeRenderWidth(kvp.Value),
                H = (float)NodeRenderHeight(kvp.Value),
            };
            inlined.Add(new InlinedChildDto(kvp.Key.Item2.Name, childDto));
        }

        return new CanvasElementDto(
            CanvasElementKind.Node,
            node.Location.X,
            node.Location.Y,
            (float)NodeRenderWidth(nvm),
            (float)NodeRenderHeight(nvm),
            Name:            node.Name,
            TypeName:        node.Type?.AssemblyQualifiedName,
            ParameterValue:  paramValue,
            IsScriptedParam: isScriptedParam,
            InlinedChildren: inlined);
    }

    /// <summary>
    /// Captures all selected canvas elements (nodes, comment blocks, function templates,
    /// function instances, ghost nodes) and writes them to the system clipboard as JSON.
    /// </summary>
    private async Task CopySelectedElementsAsync()
    {
        if (_vm is null) return;

        // Build the set of top-level elements to copy.
        // Prefer explicit selected element when it is not part of the current multi-selection;
        // this avoids stale multi-select state causing an unexpected copy target.
        IEnumerable<ICanvasElement> source;
        if (_vm.SelectedElement is { } selected
            && _multiSelection.Count > 0
            && !_multiSelection.Contains(selected))
        {
            source = [selected];
        }
        else
        {
            source = _multiSelection.Count > 0
                ? _multiSelection
                : (_vm.SelectedElement is { } sel ? [sel] : []);
        }

        var dtos = new List<CanvasElementDto>();
        // Tracks which selected real node maps to which index in dtos, so we can add cross-links.
        var nodeToDtoIndex = new Dictionary<Node, int>();

        foreach (var el in source)
        {
            try
            {
                CanvasElementDto dto;
                switch (el)
                {
                    case NodeViewModel nvm when !nvm.IsInlined:
                        nodeToDtoIndex[nvm.UnderlyingNode] = dtos.Count;
                        dto = BuildNodeDto(nvm);
                        break;

                    case CommentBlockViewModel cb:
                        dto = new CanvasElementDto(
                            CanvasElementKind.CommentBlock,
                            (float)cb.X, (float)cb.Y,
                            (float)cb.Width, (float)cb.Height,
                            Name: null,
                            CommentBody: cb.Name,
                            CommentHeader: cb.Header);
                        break;

                    case FunctionTemplateViewModel ft:
                        _vm.TryExportFunctionTemplateSnapshot(ft.UnderlyingTemplate, out var templateSnapshot);
                        var fpDtos = ft.FunctionParameters
                            .Select(fp => new FunctionParameterDto(
                                fp.Name,
                                fp.Type?.AssemblyQualifiedName))
                            .ToList();
                        dto = new CanvasElementDto(
                            CanvasElementKind.FunctionTemplate,
                            (float)ft.X, (float)ft.Y,
                            (float)ft.Width, (float)ft.Height,
                            Name: ft.Name,
                            EmbeddedTemplateSnapshot: templateSnapshot,
                            FunctionParameters: fpDtos.Count > 0 ? fpDtos : null);

                        // If this template snapshot is already present (for example as an
                        // auto-added FunctionInstance companion), keep a single entry.
                        if (!string.IsNullOrWhiteSpace(templateSnapshot))
                        {
                            var existingTemplateIndex = dtos.FindIndex(existing =>
                                existing.Kind == CanvasElementKind.FunctionTemplate
                                && string.Equals(existing.EmbeddedTemplateSnapshot, templateSnapshot, System.StringComparison.Ordinal));
                            if (existingTemplateIndex >= 0)
                            {
                                // Prefer the explicit user-copied FunctionTemplate over a companion stub.
                                if (dtos[existingTemplateIndex].IsTemplateCompanion)
                                    dtos[existingTemplateIndex] = dto;
                                continue;
                            }
                        }
                        break;

                    case FunctionInstanceViewModel fi:
                        _vm.TryExportFunctionTemplateSnapshot(fi.UnderlyingInstance.Template, out var instanceTemplateSnapshot);
                        dto = new CanvasElementDto(
                            CanvasElementKind.FunctionInstance,
                            (float)fi.X, (float)fi.Y,
                            (float)fi.Width, (float)fi.Height,
                            Name: fi.Name,
                            TemplateName: fi.TemplateName,
                            EmbeddedTemplateSnapshot: instanceTemplateSnapshot);
                        break;

                    case GhostNodeViewModel ghost:
                        dto = new CanvasElementDto(
                            CanvasElementKind.GhostNode,
                            (float)ghost.X, (float)ghost.Y,
                            (float)ghost.Width, (float)ghost.Height,
                            Name: ghost.Name,
                            ReferencedNodeName: ghost.UnderlyingGhostNode.ReferencedNode.Name);
                        break;

                    default:
                        continue; // StartViewModel and others are not copyable.
                }
                dtos.Add(dto);

                // Ensure cross-model-system paste has a concrete FunctionTemplate element to materialize
                // before FunctionInstance resolution. This carries full internals via the snapshot.
                if (el is FunctionInstanceViewModel fivm
                    && !string.IsNullOrWhiteSpace(dto.EmbeddedTemplateSnapshot)
                    && !dtos.Any(existing => existing.Kind == CanvasElementKind.FunctionTemplate
                        && string.Equals(existing.EmbeddedTemplateSnapshot, dto.EmbeddedTemplateSnapshot, System.StringComparison.Ordinal)))
                {
                    var t = fivm.UnderlyingInstance.Template;
                    dtos.Add(new CanvasElementDto(
                        CanvasElementKind.FunctionTemplate,
                        t.Location.X,
                        t.Location.Y,
                        t.Location.Width,
                        t.Location.Height,
                        Name: t.Name,
                        EmbeddedTemplateSnapshot: dto.EmbeddedTemplateSnapshot,
                        IsTemplateCompanion: true));
                }
            }
            catch
            {
                // Best-effort copy: skip malformed elements rather than aborting the whole copy action.
                continue;
            }
        }

        // Second pass: detect links where both origin and destination are in the copied set
        // and record them as cross-node links on the origin's DTO.
        if (nodeToDtoIndex.Count > 1 && _vm is not null)
        {
            foreach (var lvm in _vm.Links)
            {
                if (lvm.Origin is not NodeViewModel originNvm) continue;
                if (!nodeToDtoIndex.TryGetValue(originNvm.UnderlyingNode, out var originIdx)) continue;

                if (lvm.Destination is not NodeViewModel destNvm) continue;
                if (destNvm.IsInlined) continue;
                if (!nodeToDtoIndex.ContainsKey(destNvm.UnderlyingNode)) continue;

                // Both ends are in the selection — record a cross-link.
                var hookName = lvm.UnderlyingLink.OriginHook.Name;
                var destName = destNvm.UnderlyingNode.Name;
                var origDto  = dtos[originIdx];
                var crossLinks = origDto.CrossLinks ?? new System.Collections.Generic.List<CrossNodeLinkDto>();
                if (origDto.CrossLinks is null)
                {
                    origDto = origDto with { CrossLinks = crossLinks };
                    dtos[originIdx] = origDto;
                }
                crossLinks.Add(new CrossNodeLinkDto(hookName, destName));
            }
        }

        if (dtos.Count == 0) return;

        var payload = CanvasClipboardSerializer.CreatePayload(dtos);
        var json    = CanvasClipboardSerializer.Serialize(payload);

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(json);
    }

    /// <summary>
    /// Reads XTMF2 canvas JSON from the system clipboard and pastes the elements
    /// into the current boundary, anchored at the given model coordinates.
    /// </summary>
    private async Task PasteElementsAsync(double anchorX, double anchorY)
    {
        if (_vm is null) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        var text    = await ClipboardExtensions.TryGetTextAsync(clipboard);
        var payload = CanvasClipboardSerializer.TryDeserialize(text);
        if (payload is null) return;
        await _vm.PasteElementsAsync(payload, anchorX, anchorY);
    }

}
