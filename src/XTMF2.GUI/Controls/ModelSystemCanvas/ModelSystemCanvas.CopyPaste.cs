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
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.VisualTree;
using XTMF2.Editing;
using XTMF2.GUI.ViewModels;
using XTMF2.ModelSystemConstruct;
using System.Collections.ObjectModel;

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
            node.Name,
            node.Location.X,
            node.Location.Y,
            (float)NodeRenderWidth(nvm),
            (float)NodeRenderHeight(nvm),
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
        IEnumerable<ICanvasElement> source = _multiSelection.Count > 0
            ? _multiSelection
            : (_vm.SelectedElement is { } sel ? [sel] : []);

        var dtos = new List<CanvasElementDto>();
        // Tracks which selected real node maps to which index in dtos, so we can add cross-links.
        var nodeToDtoIndex = new Dictionary<Node, int>();

        foreach (var el in source)
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
                        cb.Name,
                        (float)cb.X, (float)cb.Y,
                        (float)cb.Width, (float)cb.Height);
                    break;

                case FunctionTemplateViewModel ft:
                    var fpDtos = ft.FunctionParameters
                        .Select(fp => new FunctionParameterDto(
                            fp.Name,
                            fp.Type?.AssemblyQualifiedName))
                        .ToList();
                    dto = new CanvasElementDto(
                        CanvasElementKind.FunctionTemplate,
                        ft.Name,
                        (float)ft.X, (float)ft.Y,
                        (float)ft.Width, (float)ft.Height,
                        FunctionParameters: fpDtos.Count > 0 ? fpDtos : null);
                    break;

                case FunctionInstanceViewModel fi:
                    dto = new CanvasElementDto(
                        CanvasElementKind.FunctionInstance,
                        fi.Name,
                        (float)fi.X, (float)fi.Y,
                        (float)fi.Width, (float)fi.Height,
                        TemplateName: fi.TemplateName);
                    break;

                case GhostNodeViewModel ghost:
                    dto = new CanvasElementDto(
                        CanvasElementKind.GhostNode,
                        ghost.Name,
                        (float)ghost.X, (float)ghost.Y,
                        (float)ghost.Width, (float)ghost.Height,
                        ReferencedNodeName: ghost.UnderlyingGhostNode.ReferencedNode.Name);
                    break;

                default:
                    continue; // StartViewModel and others are not copyable.
            }
            dtos.Add(dto);
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
