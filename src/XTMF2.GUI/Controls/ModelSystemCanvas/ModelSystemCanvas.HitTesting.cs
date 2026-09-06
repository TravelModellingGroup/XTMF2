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
using System.Linq;
using Avalonia;
using XTMF2.GUI.ViewModels;
using XTMF2.ModelSystemConstruct;
using System.Collections.ObjectModel;

namespace XTMF2.GUI.Controls;

partial class ModelSystemCanvas
{
    // ── Link hit-testing ──────────────────────────────────────────────────

    /// <summary>
    /// Returns the first <see cref="LinkViewModel"/> whose rendered segments pass within
    /// <see cref="LinkHitTolerance"/> pixels of <paramref name="pos"/>, or <c>null</c>.
    /// </summary>
    private LinkViewModel? HitTestLink(Point pos)
    {
        if (_vm is null) return null;
        foreach (var link in _vm.Links)
        {
            if (link.IsDestinationBranchHidden && !_vm.RenderAllHiddenDestinationLinks) continue;

            // Skip inter-boundary links — they are not rendered.
            if (link.Destination is null) continue;
            // Skip links to inlined nodes — no line is drawn for them.
            if (link.Destination is NodeViewModel dlNvm && dlNvm.IsInlined) continue;

            bool hit;
            if (link.UnderlyingLink.IsOrthogonal)
            {
                // For orthogonal paths, test each straight segment.
                // Use the same shared spine X that was used when rendering.
                _orthogonalSpineX.TryGetValue(link.UnderlyingLink, out var spineX);
                var pts = ComputeOrthogonalPath(link, spineX > 0 ? spineX : (double?)null);
                hit = false;
                for (int i = 1; i < pts.Length && !hit; i++)
                {
                    if (DistToSeg(pos, pts[i - 1], pts[i]) <= LinkHitTolerance)
                    {
                        hit = true;
                    }
                }
            }
            else
            {
                // Sample the S-curve at 12 chords; any chord within tolerance is a hit.
                var (hp1, hc1, hc2, hp2) = ComputeSCurve(link);
                const int HitSamples = 12;
                var prev = hp1;
                hit = false;
                for (int s = 1; s <= HitSamples && !hit; s++)
                {
                    double t = s / (double)HitSamples;
                    var next = SampleCubicBezier(hp1, hc1, hc2, hp2, t);
                    if (DistToSeg(pos, prev, next) <= LinkHitTolerance)
                    {
                        hit = true;
                    }
                    prev = next;
                }
            }
            if (hit) return link;
        }
        return null;
    }

    private LinkViewModel? HitTestOrthogonalBreakpoint(Point pos)
    {
        if (_vm is null) return null;
        const double BreakpointHitTolerance = 8.0;
        foreach (var link in _vm.Links)
        {
            if (link.IsDestinationBranchHidden && !_vm.RenderAllHiddenDestinationLinks)
                continue;
            if (link.Destination is null || !link.UnderlyingLink.IsOrthogonal)
                continue;

            var points = ComputeOrthogonalPath(link,
                ReferenceEquals(link, _orthogonalBreakpointDragLink)
                    ? _orthogonalBreakpointPreviewX
                    : GetSharedSpineX(link));
            var spine = points[1].X;
            var top = Math.Min(points[1].Y, points[2].Y) - BreakpointHitTolerance;
            var bottom = Math.Max(points[1].Y, points[2].Y) + BreakpointHitTolerance;
            if (Math.Abs(pos.X - spine) <= BreakpointHitTolerance
                && pos.Y >= top && pos.Y <= bottom)
                return link;
        }
        return null;
    }

    private double? GetSharedSpineX(LinkViewModel link)
        => _orthogonalSpineX.TryGetValue(link.UnderlyingLink, out var spineX) && spineX > 0
            ? spineX
            : null;

    /// <summary>
    /// Returns the <see cref="NodeViewModel"/> and <see cref="NodeHook"/> whose rendered
    /// row rectangle contains <paramref name="pos"/>, or <c>null</c> when the point lies
    /// outside all hook rows.
    /// <para>
    /// The hit area is the full horizontal extent of the node multiplied by
    /// <see cref="HookRowHeight"/>, so any click anywhere on a hook row registers —
    /// not just the small anchor dot on the right edge.
    /// </para>
    /// </summary>
    private (NodeViewModel node, NodeHook hook, Point anchor)? HitTestHook(Point pos)
    {
        foreach (var (node, hooks) in _nodeVisibleHooks)
        {
            double rw = NodeRenderWidth(node);

            // Quick reject: x must be within the node's horizontal extent.
            if (pos.X < node.X || pos.X > node.X + rw) continue;

            for (int i = 0; i < hooks.Count; i++)
            {
                double rowTop = node.Y + NodeHeaderHeight + i * HookRowHeight;
                if (pos.Y >= rowTop && pos.Y < rowTop + HookRowHeight)
                {
                    var anchor = new Point(node.X + rw, rowTop + HookRowHeight / 2.0);
                    return (node, hooks[i], anchor);
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Returns the <see cref="FunctionInstanceViewModel"/> and its <see cref="FunctionParameterHook"/>
    /// whose hook row contains <paramref name="pos"/>, or <c>null</c> if none.
    /// </summary>
    private (FunctionInstanceViewModel fi, FunctionParameterHook hook)? HitTestFiHook(Point pos)
    {
        if (_vm is null) return null;
        foreach (var fi in _vm.FunctionInstances)
        {
            if (pos.X < fi.X || pos.X > fi.X + fi.Width) continue;
            if (!_fiVisibleHooks.TryGetValue(fi, out var visibleHooks)) continue;
            for (int i = 0; i < visibleHooks.Count; i++)
            {
                double rowTop = fi.Y + FtHeaderHeight + i * FtHookRowHeight;
                if (pos.Y >= rowTop && pos.Y < rowTop + FtHookRowHeight)
                    return (fi, visibleHooks[i]);
            }
        }
        return null;
    }

    private (GhostNodeViewModel ghost, NodeHook hook)? HitTestGhostOriginHook(Point pos)
    {
        if (_vm is null) return null;
        foreach (var ghost in _vm.GhostNodes)
        {
            var hooks = ghost.Hooks;
            for (int i = 0; i < hooks.Count; i++)
            {
                double rowTop = ghost.Y + NodeHeaderHeight
                    + (ghost.IsParameterNode ? HookRowHeight : 0)
                    + i * HookRowHeight;
                if (pos.X >= ghost.X && pos.X <= ghost.X + ghost.Width
                    && pos.Y >= rowTop && pos.Y < rowTop + HookRowHeight)
                    return (ghost, hooks[i]);
            }
        }
        return null;
    }

    private (GhostNodeViewModel ghost, NodeViewModel node)? HitTestGhostParamValueRow(Point pos)
    {
        if (_vm is null) return null;
        foreach (var ghost in _vm.GhostNodes)
        {
            if (!ghost.IsParameterNode) continue;
            var rowRect = new Rect(ghost.X, ghost.Y + NodeHeaderHeight, ghost.Width, HookRowHeight);
            if (rowRect.Contains(pos))
                return (ghost, ghost.ReferencedNodeViewModel);
        }
        return null;
    }

    private (GhostNodeViewModel ghost, FunctionParameterHook hook)? HitTestGhostFunctionInstanceHook(Point pos)
    {
        if (_vm is null) return null;
        foreach (var ghost in _vm.GhostNodes)
        {
            if (!ghost.IsFunctionInstance) continue;
            var hooks = ghost.ReferencedNode.Hooks;
            for (int i = 0; i < hooks.Count; i++)
            {
                if (hooks[i] is not FunctionParameterHook hook) continue;
                var rowTop = ghost.Y + NodeHeaderHeight + i * HookRowHeight;
                if (pos.X >= ghost.X && pos.X <= ghost.X + ghost.Width
                    && pos.Y >= rowTop && pos.Y < rowTop + HookRowHeight)
                    return (ghost, hook);
            }
        }
        return null;
    }

    /// <summary>
    /// Returns the FunctionParameter whose hook row contains <paramref name="pos"/>
    /// on a FunctionTemplate, or <c>null</c> when the point is outside its hook rows.
    /// </summary>
    private FunctionParameter? HitTestFunctionTemplateHook(Point pos)
    {
        if (_vm is null) return null;
        foreach (var template in _vm.FunctionTemplates)
        {
            if (pos.X < template.X || pos.X > template.X + template.Width) continue;
            for (int i = 0; i < template.FunctionParameters.Count; i++)
            {
                double rowTop = template.Y + FtHeaderHeight + i * FtHookRowHeight;
                if (pos.Y >= rowTop && pos.Y < rowTop + FtHookRowHeight)
                    return template.FunctionParameters[i];
            }
        }
        return null;
    }

    /// <summary>Minimum distance from point <paramref name="p"/> to segment AB.</summary>
    private static double DistToSeg(Point p, Point a, Point b)
        => CanvasGeometryMath.DistToSeg(p, a, b);

    // ── Hook anchor cache ──────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds the per-frame lookup tables used by <see cref="RenderNodes"/>,
    /// <see cref="RenderLinks"/>, and hit-testing.
    /// </summary>
    private void BuildHookAnchorCache()
    {
        _hookAnchors.Clear();
        _nodeVisibleHooks.Clear();
        _nodeConnectedHooks.Clear();
        _hookInlinedParam.Clear();
        _fiHookInlinedParam.Clear();
        _fiHookCanInlineParam.Clear();
        _fiVisibleHooks.Clear();
        _canInlineNodes.Clear();
        _fiHookAnchors.Clear();
        _fiConnectedHooks.Clear();
        _leftGoingHooks.Clear();
        _leftGoingFiHooks.Clear();
        _leftGoingGhostHooks.Clear();
        if (_vm is null) return;

        // Which hooks on each node have a live link?
        foreach (var link in _vm.Links)
        {
            if (link.Origin is NodeViewModel originVm)
            {
                if (!_nodeConnectedHooks.TryGetValue(originVm, out var set))
                    _nodeConnectedHooks[originVm] = set = new HashSet<NodeHook>();
                set.Add(link.UnderlyingLink.OriginHook);

                // Track hooks whose link destination lies to the left of the origin node.
                // Such links will exit from the node's left face rather than the right.
                if ((!link.IsDestinationBranchHidden || _vm.RenderAllHiddenDestinationLinks)
                    && link.Destination is not null && link.X2 < originVm.X)
                    _leftGoingHooks.Add((originVm, link.UnderlyingLink.OriginHook));
            }
            if (link.Origin is GhostNodeViewModel ghostOriginVm
                && link.Destination is not null
                && (!link.IsDestinationBranchHidden || _vm.RenderAllHiddenDestinationLinks)
                && link.X2 < ghostOriginVm.X)
            {
                _leftGoingGhostHooks.Add((ghostOriginVm, link.UnderlyingLink.OriginHook));
            }
            // Which FunctionParameterHooks on each FI have a live link?
            if (link.Origin is FunctionInstanceViewModel fiOriginVm
                && link.UnderlyingLink.OriginHook is FunctionParameterHook fphConnected)
            {
                if (!_fiConnectedHooks.TryGetValue(fiOriginVm, out var fiSet))
                    _fiConnectedHooks[fiOriginVm] = fiSet = new HashSet<FunctionParameterHook>();
                fiSet.Add(fphConnected);

                // Same leftward check for FunctionInstance hooks.
                if ((!link.IsDestinationBranchHidden || _vm.RenderAllHiddenDestinationLinks)
                    && link.Destination is not null && link.X2 < fiOriginVm.X)
                    _leftGoingFiHooks.Add((fiOriginVm, fphConnected));
            }
        }

        // Identify inlined BasicParameter nodes and which hook rows they occupy.
        // Also identify canvas-visible BasicParameter nodes eligible for the minimize button.
        // First pass: count qualifying links per destination to enforce the single-destination rule
        // (a parameter node wired to more than one hook cannot be collapsed inline).
        var paramDestCount = new Dictionary<NodeViewModel, int>(ReferenceEqualityComparer.Instance);
        foreach (var link in _vm.Links)
        {
            if (link.Destination is not NodeViewModel dstCount || !dstCount.IsParameterNode) continue;
            var originHookCount = link.UnderlyingLink.OriginHook;
            bool eligible = originHookCount.Cardinality == HookCardinality.Single
                         && (link.Origin is NodeViewModel
                             || link.Origin is FunctionInstanceViewModel && originHookCount is FunctionParameterHook);
            if (!eligible) continue;
            paramDestCount.TryGetValue(dstCount, out var c);
            paramDestCount[dstCount] = c + 1;
        }

        foreach (var link in _vm.Links)
        {
            if (link.Destination is not NodeViewModel destVm || !destVm.IsParameterNode) continue;
            // Nodes reached by more than one qualifying link cannot be inlined.
            if (paramDestCount.TryGetValue(destVm, out var destCnt) && destCnt > 1) continue;

            if (link.Origin is NodeViewModel originVm2
                && link.UnderlyingLink.OriginHook.Cardinality == HookCardinality.Single)
            {
                if (destVm.IsInlined)
                    _hookInlinedParam[(originVm2, link.UnderlyingLink.OriginHook)] = destVm;
                else
                    _canInlineNodes.Add(destVm);
            }
            else if (link.Origin is FunctionInstanceViewModel originFiVm
                     && link.UnderlyingLink.OriginHook is FunctionParameterHook fpHookInline
                     && fpHookInline.Cardinality == HookCardinality.Single)
            {
                if (destVm.IsInlined)
                    _fiHookInlinedParam[(originFiVm, fpHookInline)] = destVm;
                else
                {
                    _canInlineNodes.Add(destVm);
                    _fiHookCanInlineParam[(originFiVm, fpHookInline)] = destVm;
                }
            }
        }

        foreach (var node in _vm.Nodes)
        {
            // Inlined nodes are hidden — no anchor rows needed.
            if (node.IsInlined) continue;

            _nodeConnectedHooks.TryGetValue(node, out var connected);
            connected ??= new HashSet<NodeHook>();

            IReadOnlyList<NodeHook> visible;
            if (_vm.ShowAllHooks)
            {
                visible = node.UnderlyingNode.Hooks;
            }
            else
            {
                // Required hooks (Single / AtLeastOne) are always visible.
                // Optional hooks are shown when the per-node toggle is on.
                // Connected hooks are always shown so live links remain visible.
                visible = [.. node.UnderlyingNode.Hooks
                    .Where(h =>
                        h.Cardinality == HookCardinality.Single ||
                        h.Cardinality == HookCardinality.AtLeastOne ||
                        node.ShowHooks ||
                        connected.Contains(h))];
            }

            _nodeVisibleHooks[node] = visible;

            double rw = NodeRenderWidth(node);
            for (int i = 0; i < visible.Count; i++)
            {
                double ay = node.Y + NodeHeaderHeight + i * HookRowHeight + HookRowHeight / 2.0;
                _hookAnchors[(node, visible[i])] = new Point(node.X + rw, ay);
            }
        }

        // Register FunctionInstance FunctionParameterHook anchors (right edge of each hook row).
        foreach (var fi in _vm.FunctionInstances)
        {
            var fiHooks = fi.UnderlyingInstance.Hooks;
            _fiConnectedHooks.TryGetValue(fi, out var connectedFiHooks);
            var visibleFiHooks = fiHooks
                .OfType<FunctionParameterHook>()
                .Where(hook => _vm.ShowAllHooks
                    || fi.ShowHooks
                    || hook.Cardinality == HookCardinality.Single
                    || hook.Cardinality == HookCardinality.AtLeastOne
                    || (connectedFiHooks is not null && connectedFiHooks.Contains(hook)))
                .ToArray();
            _fiVisibleHooks[fi] = visibleFiHooks;

            for (int i = 0; i < visibleFiHooks.Length; i++)
            {
                var fph = visibleFiHooks[i];
                double rowMidY = fi.Y + FtHeaderHeight + i * FtHookRowHeight + FtHookRowHeight / 2.0;
                _fiHookAnchors[(fi, fph)] = new Point(fi.X + fi.Width, rowMidY);
            }
        }
    }

    private static double NodeRenderWidth(NodeViewModel node) =>
        Math.Max(node.Width, NodeMinWidth);

    private double NodeRenderHeight(NodeViewModel node)
    {
        bool hasParamRow = node.IsParameterNode;
        int extraRows = hasParamRow ? 1 : 0;
        if (_nodeVisibleHooks.TryGetValue(node, out var hooks) && hooks.Count > 0)
        {
            return Math.Max(node.Height, NodeHeaderHeight + (hooks.Count + extraRows) * HookRowHeight);
        }
        if (hasParamRow)
        {
            return Math.Max(node.Height, NodeHeaderHeight + HookRowHeight);
        }
        // No visible hooks — keep at least NodeHeaderHeight so the name always fits.
        return Math.Max(node.Height, NodeHeaderHeight);
    }

    private double FunctionInstanceRenderHeight(FunctionInstanceViewModel fi)
    {
        if (!_fiVisibleHooks.TryGetValue(fi, out var visibleHooks))
            return fi.Height;

        var storedHeight = fi.UnderlyingInstance.Location.Height is 0
            ? 50.0
            : fi.UnderlyingInstance.Location.Height;
        return Math.Max(storedHeight, FtHeaderHeight + visibleHooks.Count * FtHookRowHeight);
    }

    /// <summary>Returns the rendered width of any resizable canvas element.</summary>
    private double ElementRenderWidth(ICanvasElement el) =>
        el is NodeViewModel nvm ? NodeRenderWidth(nvm)
        : el is CommentBlockViewModel cvm ? cvm.Width
        : el is GhostNodeViewModel gnvm ? gnvm.Width
        : el is FunctionTemplateViewModel ftvm ? ftvm.Width
        : el is FunctionInstanceViewModel fivm ? fivm.Width
        : el is FunctionParameterViewModel fpvm ? fpvm.Width
        : 0;

    /// <summary>Returns the rendered height of any resizable canvas element.</summary>
    private double ElementRenderHeight(ICanvasElement el) =>
        el is NodeViewModel nvm ? NodeRenderHeight(nvm)
        : el is CommentBlockViewModel cvm ? cvm.Height
        : el is GhostNodeViewModel gnvm ? gnvm.Height
        : el is FunctionTemplateViewModel ftvm ? ftvm.Height
        : el is FunctionInstanceViewModel fivm ? FunctionInstanceRenderHeight(fivm)
        : el is FunctionParameterViewModel fpvm ? fpvm.Height
        : 0;

    private NodeViewModel? HitTestParamValueRow(Point pos)
    {
        if (_vm is null) return null;
        foreach (var node in _vm.Nodes)
        {
            if (!node.IsParameterNode || node.IsInlined) continue;
            double rw = NodeRenderWidth(node);
            var rowRect = new Rect(node.X, node.Y + NodeHeaderHeight, rw, HookRowHeight);
            if (rowRect.Contains(pos))
            {
                return node;
            }
        }
        return null;
    }

    /// <summary>
    /// Returns the <see cref="ICanvasElement"/> whose resize handle (bottom-right corner square)
    /// contains <paramref name="pos"/>, or <c>null</c> if none.
    /// </summary>
    /// <param name="pos">The position to test.</param>
    /// <returns>The <see cref="ICanvasElement"/> whose resize handle contains <paramref name="pos"/>, or <c>null</c> if none.</returns>
    private ICanvasElement? HitTestResizeHandle(Point pos)
    {
        if (_vm is null) return null;

        ICanvasElement? CheckHandle<T>(ObservableCollection<T> collection) where T : ICanvasElement
        {
            foreach(var element in collection)
            {
                // Use the same rendered dimensions used when drawing the resize handle,
                // so that the hit-test rectangle matches the visual even when a node's
                // stored Height is smaller than its actual rendered height (e.g. when
                // the node has hook rows that push the bottom edge down).
                double w = ElementRenderWidth(element);
                double h = ElementRenderHeight(element);
                double x = element.X;
                double y = element.Y;
                var handle = new Rect(x + w - ResizeHandleSize, y + h - ResizeHandleSize,
                                  ResizeHandleSize, ResizeHandleSize);
                if (handle.Contains(pos))
                {
                    return element;
                }
            }
            return null;
        }
        ICanvasElement? hit = CheckHandle(_vm.Nodes) 
                            ?? CheckHandle(_vm.CommentBlocks)
                            ?? CheckHandle(_vm.GhostNodes)
                            ?? CheckHandle(_vm.FunctionTemplates)
                            ?? CheckHandle(_vm.FunctionInstances)
                            ?? CheckHandle(_vm.FunctionParameterVMs);
        return hit;
    }

    // ── Hook toggle icon hit-testing ─────────────────────────────────────

    /// <summary>
    /// Returns the <see cref="NodeViewModel"/> whose hook-toggle icon button contains
    /// <paramref name="pos"/>, or <c>null</c> if none.
    /// </summary>
    private NodeViewModel? HitTestHookToggleIcon(Point pos)
    {
        if (_vm is null || _vm.ShowAllHooks) return null;
        foreach (var node in _vm.Nodes)
        {
            if (node.UnderlyingNode.Hooks.Count == 0) continue;
            double rw = NodeRenderWidth(node);
            var iconRect = HookToggleIconRect(node, rw);
            if (iconRect.Contains(pos))
            {
                return node;
            }
        }
        return null;
    }

    /// <summary>
    /// Returns the bounding rectangle of the hook-toggle icon button for
    /// <paramref name="node"/> given its rendered width <paramref name="rw"/>.
    /// </summary>
    /// <param name="node">The node whose hook-toggle icon button rectangle is being calculated.</param>
    /// <param name="rw">The rendered width of the node, used to position the icon at the right edge.</param>
    /// <returns>The bounding rectangle of the hook-toggle icon button.</returns>
    private static Rect HookToggleIconRect(NodeViewModel node, double rw)
    {
        const double margin = 4.0;
        double size = HookToggleIconSize;
        return new Rect(
            node.X + rw - size - margin,
            node.Y + (NodeHeaderHeight - size) / 2.0,
            size,
            size);
    }

    private static Rect FunctionInstanceHookToggleIconRect(FunctionInstanceViewModel fi)
    {
        const double margin = 4.0;
        double size = HookToggleIconSize;
        return new Rect(
            fi.X + fi.Width - size - margin,
            fi.Y + (FtHeaderHeight - size) / 2.0,
            size,
            size);
    }

    private FunctionInstanceViewModel? HitTestFunctionInstanceHookToggleIcon(Point pos)
    {
        if (_vm is null || _vm.ShowAllHooks) return null;
        foreach (var fi in _vm.FunctionInstances)
        {
            if (fi.FunctionParameters.Count > 0
                && FunctionInstanceHookToggleIconRect(fi).Contains(pos))
                return fi;
        }
        return null;
    }

    /// <summary>
    /// Returns the bounding rectangle of the "minimize to inline" button that appears
    /// in the top-left header of a <see cref="_canInlineNodes"/> BasicParameter node.
    /// </summary>
    /// <param name="node">The node whose minimize-to-inline button rectangle is being calculated.</param>
    /// <returns>The bounding rectangle of the minimize-to-inline button.</returns>
    private static Rect InlineMinimizeButtonRect(NodeViewModel node)
    {
        const double margin = 4.0;
        double size = InlineMinimizeButtonSize;
        return new Rect(
            node.X + margin,
            node.Y + (NodeHeaderHeight - size) / 2.0,
            size,
            size);
    }

    private static Rect InlineFiMinimizeButtonRect(FunctionInstanceViewModel fi, int hookIndex)
    {
        const double margin = 4.0;
        double size = InlineMinimizeButtonSize;
        return new Rect(
            fi.X + margin,
            fi.Y + FtHeaderHeight + hookIndex * FtHookRowHeight
                + (FtHookRowHeight - size) / 2.0,
            size,
            size);
    }

    /// <summary>
    /// Returns the <see cref="NodeViewModel"/> whose minimize-to-inline button
    /// (top-left of header) contains <paramref name="pos"/>, or <c>null</c>.
    /// Only nodes in <see cref="_canInlineNodes"/> have this button.
    /// </summary>
    private NodeViewModel? HitTestMinimizeButton(Point pos)
    {
        if (_vm is null) return null;
        foreach (var node in _canInlineNodes)
        {
            if (InlineMinimizeButtonRect(node).Contains(pos))
            {
                return node;
            }
        }
        return null;
    }

    private (FunctionInstanceViewModel Fi, FunctionParameterHook Hook, NodeViewModel Parameter)?
        HitTestFiMinimizeButton(Point pos)
    {
        foreach (var ((fi, hook), parameter) in _fiHookCanInlineParam)
        {
            int hookIndex = -1;
            var hooks = fi.UnderlyingInstance.Hooks;
            for (int i = 0; i < hooks.Count; i++)
            {
                if (ReferenceEquals(hooks[i], hook))
                {
                    hookIndex = i;
                    break;
                }
            }
            if (hookIndex >= 0 && InlineFiMinimizeButtonRect(fi, hookIndex).Contains(pos))
                return (fi, hook, parameter);
        }
        return null;
    }

    /// <summary>
    /// Returns information about an inlined-param hook row that contains
    /// <paramref name="pos"/>, or <c>null</c> when no such row is hit.
    /// The first element of the returned tuple is the canvas origin (either a
    /// <see cref="NodeViewModel"/> or a <see cref="FunctionInstanceViewModel"/>),
    /// or <c>null</c> when the origin could not be determined.
    /// </summary>
    private (ICanvasElement? originEl, NodeHook hook, NodeViewModel paramNode, double rowX, double rowY, double rowW)? HitTestInlinedParamRow(Point pos)
    {
        if (_vm is null) return null;

        foreach (var ((originNode, hook), paramNode) in _hookInlinedParam)
        {
            if (!_nodeVisibleHooks.TryGetValue(originNode, out var hooks)) continue;

            int hookIdx = -1;
            for (int j = 0; j < hooks.Count; j++)
                if (ReferenceEquals(hooks[j], hook)) { hookIdx = j; break; }
            if (hookIdx < 0) continue;

            int rowOffset = originNode.IsParameterNode ? 1 : 0;
            double rw = NodeRenderWidth(originNode);
            double rowTop = originNode.Y + NodeHeaderHeight + (rowOffset + hookIdx) * HookRowHeight;
            var rowRect = new Rect(originNode.X, rowTop, rw, HookRowHeight);

            if (rowRect.Contains(pos))
                return (originNode, hook, paramNode, originNode.X, rowTop, rw);
        }

        // Also check FunctionInstance-originated inlined params.
        foreach (var ((fi, fpHook), paramNode) in _fiHookInlinedParam)
        {
            var fiHooks = fi.UnderlyingInstance.Hooks;
            int hookIdx = -1;
            for (int j = 0; j < fiHooks.Count; j++)
                if (ReferenceEquals(fiHooks[j], fpHook)) { hookIdx = j; break; }
            if (hookIdx < 0) continue;

            double rw = fi.Width;
            double rowTop = fi.Y + FtHeaderHeight + hookIdx * FtHookRowHeight;
            var rowRect = new Rect(fi.X, rowTop, rw, FtHookRowHeight);

            if (rowRect.Contains(pos))
                return (fi, fpHook, paramNode, fi.X, rowTop, rw);
        }

        foreach (var ghost in _vm.GhostNodes)
        {
            if (!ghost.IsFunctionInstance)
            {
                for (int i = 0; i < ghost.Hooks.Count; i++)
                {
                    var hook = ghost.Hooks[i];
                    var embedded = ghost.GetEmbeddedParameter(hook);
                    if (embedded is null) continue;
                    var paramNode = _vm.Nodes.FirstOrDefault(node => node.UnderlyingNode == embedded)
                        ?? new NodeViewModel(embedded, _vm.Session, _vm.User);
                    var rowTop = ghost.Y + NodeHeaderHeight
                        + (ghost.IsParameterNode ? 1 : 0) * HookRowHeight
                        + i * HookRowHeight;
                    if (new Rect(ghost.X, rowTop, ghost.Width, HookRowHeight).Contains(pos))
                        return (ghost, hook, paramNode, ghost.X, rowTop, ghost.Width);
                }
            }

            if (!ghost.IsFunctionInstance) continue;
            for (int i = 0; i < ghost.ReferencedNode.Hooks.Count; i++)
            {
                if (ghost.ReferencedNode.Hooks[i] is not FunctionParameterHook hook) continue;
                var embedded = ghost.GetEmbeddedParameter(hook);
                if (embedded is null) continue;
                var paramNode = _vm.Nodes.FirstOrDefault(node => node.UnderlyingNode == embedded)
                    ?? new NodeViewModel(embedded, _vm.Session, _vm.User);

                double rowTop = ghost.Y + NodeHeaderHeight + i * HookRowHeight;
                if (new Rect(ghost.X, rowTop, ghost.Width, HookRowHeight).Contains(pos))
                    return (ghost, hook, paramNode, ghost.X, rowTop, ghost.Width);
            }
        }

        return null;
    }

    /// <summary>Finds the topmost canvas element under <paramref name="pos"/>.</summary>
    /// <param name="testComments">When <c>false</c>, comment blocks are excluded (link creation).</param>
    private ICanvasElement? HitTest(Point pos, bool testComments)
    {
        if (_vm is null) return null;

        static ICanvasElement? TestHitsElement<T> (ObservableCollection<T> collection, Point pos) where T : ICanvasElement
        {
            foreach (var element in collection)
            {
                if (element.IsPointWithin(pos))
                    return element;
            }
            return null;
        }

        ICanvasElement? hit = TestHitsElement(_vm.Starts, pos)
                        ??  TestHitsElement(_vm.Nodes, pos)
                        ?? TestHitsElement(_vm.FunctionParameterVMs, pos)
                        ?? TestHitsElement(_vm.GhostNodes, pos)
                        // Function instances render above function-template boxes and must win
                        // hit-testing when they overlap.
                        ?? TestHitsElement(_vm.FunctionInstances, pos)
                        ?? TestHitsElement(_vm.FunctionTemplates, pos);

        
        if (hit is not null)
        {
            return hit;
        }

        return testComments ? TestHitsElement(_vm.CommentBlocks, pos) : null;
    }

}
