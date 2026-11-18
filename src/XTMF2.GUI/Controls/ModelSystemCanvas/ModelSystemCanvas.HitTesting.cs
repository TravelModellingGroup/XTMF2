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
    private const double NestedEmbeddedParameterIndent = 5.0;

    private readonly record struct NestedEmbeddedParameterRow(
        NodeViewModel Parameter,
        string Label,
        int Depth,
        NodeHook RootHook);

    private readonly Dictionary<ICanvasElement, IReadOnlyList<NestedEmbeddedParameterRow>>
        _nestedEmbeddedParameterRows = new(ReferenceEqualityComparer.Instance);

    // ── Link hit-testing ──────────────────────────────────────────────────

    /// <summary>
    /// Returns the first <see cref="LinkViewModel"/> whose rendered segments pass within
    /// <see cref="LinkHitTolerance"/> pixels of <paramref name="pos"/>, or <c>null</c>.
    /// </summary>
    private LinkViewModel? HitTestLink(Point pos)
    {
        if (_vm is null) return null;
        for (int linkIndex = _vm.Links.Count - 1; linkIndex >= 0; linkIndex--)
        {
            var link = _vm.Links[linkIndex];
            if (link.IsDestinationBranchHidden && !_vm.RenderAllHiddenDestinationLinks) continue;

            // Skip inter-boundary links — they are not rendered.
            if (link.Destination is null) continue;
            // Skip links to inlined nodes — no line is drawn for them.
            if (IsCanvasElementInlined(link.Origin)
                || link.Destination is not null && IsCanvasElementInlined(link.Destination))
                continue;

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
        for (int linkIndex = _vm.Links.Count - 1; linkIndex >= 0; linkIndex--)
        {
            var link = _vm.Links[linkIndex];
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
        if (_vm is null) return null;
        for (int nodeIndex = _vm.Nodes.Count - 1; nodeIndex >= 0; nodeIndex--)
        {
            var node = _vm.Nodes[nodeIndex];
            if (!_nodeVisibleHooks.TryGetValue(node, out var hooks)) continue;
            double rw = NodeRenderWidth(node);

            // Quick reject: x must be within the node's horizontal extent.
            if (pos.X < node.X || pos.X > node.X + rw) continue;

            for (int i = 0; i < hooks.Count; i++)
            {
                double rowTop = NodeHookRowTop(node, i);
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
        for (int fiIndex = _vm.FunctionInstances.Count - 1; fiIndex >= 0; fiIndex--)
        {
            var fi = _vm.FunctionInstances[fiIndex];
            if (fi.IsInlined) continue;
            if (pos.X < fi.X || pos.X > fi.X + fi.Width) continue;
            if (!_fiVisibleHooks.TryGetValue(fi, out var visibleHooks)) continue;
            for (int i = 0; i < visibleHooks.Count; i++)
            {
                double rowTop = FunctionInstanceHookRowTop(fi, i);
                if (pos.Y >= rowTop && pos.Y < rowTop + FtHookRowHeight)
                    return (fi, visibleHooks[i]);
            }
        }
        return null;
    }

    private (GhostNodeViewModel ghost, NodeHook hook)? HitTestGhostOriginHook(Point pos)
    {
        if (_vm is null) return null;
        for (int ghostIndex = _vm.GhostNodes.Count - 1; ghostIndex >= 0; ghostIndex--)
        {
            var ghost = _vm.GhostNodes[ghostIndex];
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
        for (int ghostIndex = _vm.GhostNodes.Count - 1; ghostIndex >= 0; ghostIndex--)
        {
            var ghost = _vm.GhostNodes[ghostIndex];
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
        for (int ghostIndex = _vm.GhostNodes.Count - 1; ghostIndex >= 0; ghostIndex--)
        {
            var ghost = _vm.GhostNodes[ghostIndex];
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
        for (int templateIndex = _vm.FunctionTemplates.Count - 1; templateIndex >= 0; templateIndex--)
        {
            var template = _vm.FunctionTemplates[templateIndex];
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
    internal void BuildHookAnchorCache()
    {
        _hookAnchors.Clear();
        _nodeVisibleHooks.Clear();
        _nodeConnectedHooks.Clear();
        _hookEmbeddedElement.Clear();
        _fiHookEmbeddedElement.Clear();
        _fiHookCanEmbedElement.Clear();
        _fiVisibleHooks.Clear();
        _canEmbedElements.Clear();
        _fiHookAnchors.Clear();
        _nestedEmbeddedParameterRows.Clear();
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
                if ((!link.IsDestinationBranchHidden || ShouldRevealHiddenDestinationBranch(link))
                    && link.Destination is not null && link.X2 < originVm.X)
                    _leftGoingHooks.Add((originVm, link.UnderlyingLink.OriginHook));
            }
            if (link.Origin is GhostNodeViewModel ghostOriginVm
                && link.Destination is not null
                && (!link.IsDestinationBranchHidden || ShouldRevealHiddenDestinationBranch(link))
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
                if ((!link.IsDestinationBranchHidden || ShouldRevealHiddenDestinationBranch(link))
                    && link.Destination is not null && link.X2 < fiOriginVm.X)
                    _leftGoingFiHooks.Add((fiOriginVm, fphConnected));
            }
        }

        var incomingLinksByDestination = new Dictionary<ICanvasElement, List<LinkViewModel>>(
            ReferenceEqualityComparer.Instance);
        foreach (var link in _vm.Links)
        {
            if (link.Destination is not { } destination)
                continue;
            if (!incomingLinksByDestination.TryGetValue(destination, out var incomingLinks))
                incomingLinksByDestination[destination] = incomingLinks = [];
            incomingLinks.Add(link);
        }

        var embeddingLinks = _vm.Links
            .Where(link => IsEligibleEmbeddingLink(link)
                && link.Destination is NodeViewModel or FunctionInstanceViewModel)
            .ToArray();

        var embeddedLinksByHost = new Dictionary<ICanvasElement, List<LinkViewModel>>(
            ReferenceEqualityComparer.Instance);
        foreach (var link in embeddingLinks.Where(link =>
                     link.Destination is { } destination && IsCanvasElementInlined(destination)))
        {
            if (!embeddedLinksByHost.TryGetValue(link.Origin, out var children))
                embeddedLinksByHost[link.Origin] = children = [];
            children.Add(link);
        }

        bool removedEmbeddedLink;
        do
        {
            removedEmbeddedLink = false;
            foreach (var link in embeddedLinksByHost.Values.SelectMany(links => links).ToArray())
            {
                if (HasOnlyEmbeddedIncomingLinks(
                        link,
                        incomingLinksByDestination,
                        GetEmbeddedDescendants(link.Origin, embeddedLinksByHost)))
                    continue;
                embeddedLinksByHost[link.Origin].Remove(link);
                removedEmbeddedLink = true;
            }
        } while (removedEmbeddedLink);

        var existingEmbeddedChildren = new Dictionary<ICanvasElement, List<ICanvasElement>>(
            ReferenceEqualityComparer.Instance);
        foreach (var link in embeddedLinksByHost.Values.SelectMany(links => links))
        {
            if (link.Destination is not { } destination)
                continue;
            if (!existingEmbeddedChildren.TryGetValue(link.Origin, out var children))
                existingEmbeddedChildren[link.Origin] = children = [];
            children.Add(destination);

            if (link.Origin is NodeViewModel originNode)
                _hookEmbeddedElement[(originNode, link.UnderlyingLink.OriginHook)] = destination;
            else if (link.Origin is FunctionInstanceViewModel originInstance
                     && link.UnderlyingLink.OriginHook is FunctionParameterHook instanceHook)
                _fiHookEmbeddedElement[(originInstance, instanceHook)] = destination;
        }

        var embeddedDescendantsByHost = new Dictionary<ICanvasElement, HashSet<ICanvasElement>>(
            ReferenceEqualityComparer.Instance);
        var embeddedProvidersByCandidate = new Dictionary<ICanvasElement, HashSet<ICanvasElement>>(
            ReferenceEqualityComparer.Instance);
        foreach (var link in embeddingLinks)
        {
            if (link.Destination is not { } destination
                || IsCanvasElementInlined(destination))
                continue;

            if (!embeddedDescendantsByHost.TryGetValue(link.Origin, out var embeddedDescendants))
            {
                embeddedDescendants = GetEmbeddedDescendants(link.Origin, embeddedLinksByHost);
                embeddedDescendantsByHost.Add(link.Origin, embeddedDescendants);
            }
            if (!embeddedProvidersByCandidate.TryGetValue(destination, out var embeddedProviders))
            {
                embeddedProviders = GetEmbeddedDescendants(destination, embeddedLinksByHost);
                embeddedProvidersByCandidate.Add(destination, embeddedProviders);
            }
            if (!HasOnlyEmbeddedProviders(destination, _vm.Links, embeddedProviders)
                || !HasOnlyEmbeddedIncomingLinks(link, incomingLinksByDestination, embeddedDescendants)
                || WouldCreateEmbeddingCycle(link.Origin, destination, existingEmbeddedChildren))
                continue;

            _canEmbedElements.Add(destination);
            if (link.Origin is FunctionInstanceViewModel originInstance
                && link.UnderlyingLink.OriginHook is FunctionParameterHook instanceHook)
                _fiHookCanEmbedElement[(originInstance, instanceHook)] = destination;
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
            _nestedEmbeddedParameterRows[node] = BuildNestedEmbeddedParameterRows(node, visible);

            double rw = NodeRenderWidth(node);
            for (int i = 0; i < visible.Count; i++)
            {
                double ay = NodeHookRowTop(node, i) + HookRowHeight / 2.0;
                _hookAnchors[(node, visible[i])] = new Point(node.X + rw, ay);
            }
        }

        // Register FunctionInstance FunctionParameterHook anchors (right edge of each hook row).
        foreach (var fi in _vm.FunctionInstances)
        {
            if (fi.IsInlined) continue;
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
            _nestedEmbeddedParameterRows[fi] = BuildNestedEmbeddedParameterRows(fi, visibleFiHooks);

            for (int i = 0; i < visibleFiHooks.Length; i++)
            {
                var fph = visibleFiHooks[i];
                double rowMidY = FunctionInstanceHookRowTop(fi, i) + FtHookRowHeight / 2.0;
                _fiHookAnchors[(fi, fph)] = new Point(fi.X + fi.Width, rowMidY);
            }
        }
    }

    private IReadOnlyList<NestedEmbeddedParameterRow> BuildNestedEmbeddedParameterRows(
        ICanvasElement host,
        IEnumerable<NodeHook> visibleHooks)
    {
        var rows = new List<NestedEmbeddedParameterRow>();
        var ancestry = new HashSet<ICanvasElement>(ReferenceEqualityComparer.Instance) { host };
        foreach (var hook in visibleHooks)
        {
            if (!TryGetEmbeddedChild(host, hook, out var child)
                || child is NodeViewModel { IsParameterNode: true }
                || child is not NodeViewModel and not FunctionInstanceViewModel)
                continue;

            ancestry.Add(child);
            AppendNestedEmbeddedParameterRows(
                child,
                child.Name,
                depth: 1,
                hook,
                ancestry,
                rows);
            ancestry.Remove(child);
        }
        return rows;
    }

    private void AppendNestedEmbeddedParameterRows(
        ICanvasElement parent,
        string parentPath,
        int depth,
        NodeHook rootHook,
        HashSet<ICanvasElement> ancestry,
        List<NestedEmbeddedParameterRow> rows)
    {
        IEnumerable<NodeHook> hooks = parent switch
        {
            NodeViewModel node => node.UnderlyingNode.Hooks ?? [],
            FunctionInstanceViewModel instance => instance.UnderlyingInstance.Hooks,
            _ => []
        };

        foreach (var hook in hooks)
        {
            if (!TryGetEmbeddedChild(parent, hook, out var child))
                continue;

            if (child is NodeViewModel { IsParameterNode: true } parameter)
            {
                rows.Add(new NestedEmbeddedParameterRow(
                    parameter,
                    $"{parentPath} › {hook.Name}",
                    depth,
                    rootHook));
                continue;
            }

            if (child is not NodeViewModel and not FunctionInstanceViewModel
                || !ancestry.Add(child))
                continue;

            AppendNestedEmbeddedParameterRows(
                child,
                $"{parentPath} › {child.Name}",
                depth + 1,
                rootHook,
                ancestry,
                rows);
            ancestry.Remove(child);
        }
    }

    private bool TryGetEmbeddedChild(
        ICanvasElement host,
        NodeHook hook,
        out ICanvasElement child)
    {
        if (host is NodeViewModel node
            && _hookEmbeddedElement.TryGetValue((node, hook), out child!))
            return true;

        if (host is FunctionInstanceViewModel instance
            && hook is FunctionParameterHook functionHook
            && _fiHookEmbeddedElement.TryGetValue((instance, functionHook), out child!))
            return true;

        child = null!;
        return false;
    }

    private int CountNestedParameterRows(ICanvasElement host, NodeHook rootHook)
    {
        if (!_nestedEmbeddedParameterRows.TryGetValue(host, out var rows))
            return 0;
        return rows.Count(row => ReferenceEquals(row.RootHook, rootHook));
    }

    private double NodeHookRowTop(NodeViewModel node, int hookIndex)
    {
        int rowIndex = (node.IsParameterNode ? 1 : 0) + hookIndex;
        if (_nodeVisibleHooks.TryGetValue(node, out var hooks))
        {
            for (int i = 0; i < hookIndex; i++)
                rowIndex += CountNestedParameterRows(node, hooks[i]);
        }
        return node.Y + NodeHeaderHeight + rowIndex * HookRowHeight;
    }

    private double FunctionInstanceHookRowTop(FunctionInstanceViewModel instance, int hookIndex)
    {
        int rowIndex = hookIndex;
        if (_fiVisibleHooks.TryGetValue(instance, out var hooks))
        {
            for (int i = 0; i < hookIndex; i++)
                rowIndex += CountNestedParameterRows(instance, hooks[i]);
        }
        return instance.Y + FtHeaderHeight + rowIndex * FtHookRowHeight;
    }

    private double? NestedEmbeddedParameterRowTop(ICanvasElement host, int targetIndex)
    {
        if (!_nestedEmbeddedParameterRows.TryGetValue(host, out var nestedRows))
            return null;

        IEnumerable<NodeHook> hooks;
        double y;
        double rowHeight;
        int rowIndex;
        if (host is NodeViewModel node && _nodeVisibleHooks.TryGetValue(node, out var nodeHooks))
        {
            hooks = nodeHooks;
            y = node.Y + NodeHeaderHeight;
            rowHeight = HookRowHeight;
            rowIndex = node.IsParameterNode ? 1 : 0;
        }
        else if (host is FunctionInstanceViewModel instance
                 && _fiVisibleHooks.TryGetValue(instance, out var instanceHooks))
        {
            hooks = instanceHooks;
            y = instance.Y + FtHeaderHeight;
            rowHeight = FtHookRowHeight;
            rowIndex = 0;
        }
        else
        {
            return null;
        }

        int nestedIndex = 0;
        foreach (var hook in hooks)
        {
            rowIndex++;
            foreach (var row in nestedRows)
            {
                if (!ReferenceEquals(row.RootHook, hook))
                    continue;
                if (nestedIndex++ == targetIndex)
                    return y + rowIndex * rowHeight;
                rowIndex++;
            }
        }
        return null;
    }

    private static bool IsEligibleEmbeddingLink(LinkViewModel link)
        => link.Origin switch
        {
            NodeViewModel => link.UnderlyingLink.OriginHook.Cardinality == HookCardinality.Single,
            FunctionInstanceViewModel => link.UnderlyingLink.OriginHook is FunctionParameterHook
                && link.UnderlyingLink.OriginHook.Cardinality is HookCardinality.Single or HookCardinality.SingleOptional,
            _ => false
        };

    private static bool IsCanvasElementInlined(ICanvasElement element)
        => element switch
        {
            NodeViewModel node => node.IsInlined,
            FunctionInstanceViewModel instance => instance.IsInlined,
            _ => false
        };

    internal bool CanEmbedElement(ICanvasElement element) => _canEmbedElements.Contains(element);

    internal bool IsEmbeddedInHost(ICanvasElement host, ICanvasElement child)
        => _hookEmbeddedElement.Any(pair =>
               ReferenceEquals(pair.Key.Item1, host) && ReferenceEquals(pair.Value, child))
           || _fiHookEmbeddedElement.Any(pair =>
               ReferenceEquals(pair.Key.Item1, host) && ReferenceEquals(pair.Value, child));

    internal IReadOnlyList<string> GetNestedEmbeddedParameterLabels(ICanvasElement host)
        => _nestedEmbeddedParameterRows.TryGetValue(host, out var rows)
            ? rows.Select(row => FormatHookLabel(
                row.Label,
                hasInlinedParameter: true,
                row.Parameter.IsBasicParameter,
                row.Parameter.ParameterValueRepresentation)).ToArray()
            : [];

    private static bool HasOnlyEmbeddedProviders(
        ICanvasElement candidate,
        IEnumerable<LinkViewModel> links,
        IReadOnlySet<ICanvasElement> embeddedProviders)
    {
        return links
            .Where(link => ReferenceEquals(link.Origin, candidate))
            .All(link => link.Destination is { } destination
                && embeddedProviders.Contains(destination));
    }

    private static bool HasOnlyEmbeddedIncomingLinks(
        LinkViewModel candidateLink,
        IReadOnlyDictionary<ICanvasElement, List<LinkViewModel>> incomingLinksByDestination,
        IReadOnlySet<ICanvasElement> embeddedDescendants)
    {
        if (candidateLink.Destination is not { } destination
            || !incomingLinksByDestination.TryGetValue(destination, out var incomingLinks))
            return false;

        return incomingLinks.All(link =>
            ReferenceEquals(link, candidateLink) || embeddedDescendants.Contains(link.Origin));
    }

    private static HashSet<ICanvasElement> GetEmbeddedDescendants(
        ICanvasElement host,
        IReadOnlyDictionary<ICanvasElement, List<LinkViewModel>> embeddedLinksByHost)
    {
        var descendants = new HashSet<ICanvasElement>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<ICanvasElement>();
        pending.Push(host);
        while (pending.TryPop(out var current))
        {
            if (!embeddedLinksByHost.TryGetValue(current, out var links))
                continue;
            foreach (var link in links)
            {
                if (link.Destination is not { } child
                    || ReferenceEquals(child, host)
                    || !descendants.Add(child))
                    continue;
                pending.Push(child);
            }
        }
        return descendants;
    }

    private static bool WouldCreateEmbeddingCycle(
        ICanvasElement host,
        ICanvasElement child,
        IReadOnlyDictionary<ICanvasElement, List<ICanvasElement>> embeddedChildren)
    {
        var pending = new Stack<ICanvasElement>();
        var visited = new HashSet<ICanvasElement>(ReferenceEqualityComparer.Instance);
        pending.Push(child);
        while (pending.TryPop(out var current))
        {
            if (ReferenceEquals(current, host))
                return true;
            if (!visited.Add(current)
                || !embeddedChildren.TryGetValue(current, out var children))
                continue;
            foreach (var nested in children)
                pending.Push(nested);
        }
        return false;
    }

    private static void InlineCanvasElement(ICanvasElement element)
    {
        switch (element)
        {
            case NodeViewModel node:
                node.Inline();
                break;
            case FunctionInstanceViewModel instance:
                instance.Inline();
                break;
        }
    }

    private static void ExpandCanvasElement(ICanvasElement element, double x, double y)
    {
        switch (element)
        {
            case NodeViewModel node:
                node.ExpandToCanvas(x, y);
                break;
            case FunctionInstanceViewModel instance:
                instance.ExpandToCanvas(x, y);
                break;
        }
    }

    private static double NodeRenderWidth(NodeViewModel node) =>
        Math.Max(node.Width, NodeMinWidth);

    private double NodeRenderHeight(NodeViewModel node)
    {
        bool hasParamRow = node.IsParameterNode;
        int extraRows = hasParamRow ? 1 : 0;
        int embeddedRows = _nestedEmbeddedParameterRows.TryGetValue(node, out var nestedRows)
            ? nestedRows.Count
            : 0;
        if (_nodeVisibleHooks.TryGetValue(node, out var hooks) && hooks.Count > 0)
        {
            return Math.Max(node.Height,
                NodeHeaderHeight + (hooks.Count + extraRows + embeddedRows) * HookRowHeight);
        }
        if (hasParamRow)
        {
            return Math.Max(node.Height, NodeHeaderHeight + (1 + embeddedRows) * HookRowHeight);
        }
        // No visible hooks — keep at least NodeHeaderHeight so the name always fits.
        return Math.Max(node.Height, NodeHeaderHeight + embeddedRows * HookRowHeight);
    }

    private double FunctionInstanceRenderHeight(FunctionInstanceViewModel fi)
    {
        if (!_fiVisibleHooks.TryGetValue(fi, out var visibleHooks))
            return fi.Height;

        var storedHeight = fi.UnderlyingInstance.Location.Height is 0
            ? 50.0
            : fi.UnderlyingInstance.Location.Height;
        int embeddedRows = _nestedEmbeddedParameterRows.TryGetValue(fi, out var nestedRows)
            ? nestedRows.Count
            : 0;
        return Math.Max(storedHeight,
            FtHeaderHeight + (visibleHooks.Count + embeddedRows) * FtHookRowHeight);
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
        for (int nodeIndex = _vm.Nodes.Count - 1; nodeIndex >= 0; nodeIndex--)
        {
            var node = _vm.Nodes[nodeIndex];
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
            for (int index = collection.Count - 1; index >= 0; index--)
            {
                var element = collection[index];
                if (IsCanvasElementInlined(element)) continue;
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
        ICanvasElement? hit = CheckHandle(_vm.Starts)
                    ?? CheckHandle(_vm.GhostNodes)
                    ?? CheckHandle(_vm.Nodes)
                    ?? CheckHandle(_vm.FunctionParameterVMs)
                    ?? CheckHandle(_vm.FunctionInstances)
                    ?? CheckHandle(_vm.FunctionTemplates)
                    ?? CheckHandle(_vm.CommentBlocks);
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
        for (int nodeIndex = _vm.Nodes.Count - 1; nodeIndex >= 0; nodeIndex--)
        {
            var node = _vm.Nodes[nodeIndex];
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
        for (int fiIndex = _vm.FunctionInstances.Count - 1; fiIndex >= 0; fiIndex--)
        {
            var fi = _vm.FunctionInstances[fiIndex];
            if (fi.IsInlined) continue;
            if (fi.FunctionParameters.Count > 0
                && FunctionInstanceHookToggleIconRect(fi).Contains(pos))
                return fi;
        }
        return null;
    }

    /// <summary>
    /// Returns the bounding rectangle of the embed button in a Node header.
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

    private static Rect InlineMinimizeButtonRect(FunctionInstanceViewModel fi)
    {
        const double margin = 4.0;
        double size = InlineMinimizeButtonSize;
        return new Rect(
            fi.X + margin,
            fi.Y + (FtHeaderHeight - size) / 2.0,
            size,
            size);
    }

    private Rect InlineFiMinimizeButtonRect(FunctionInstanceViewModel fi, int hookIndex)
    {
        const double margin = 4.0;
        double size = InlineMinimizeButtonSize;
        return new Rect(
            fi.X + margin,
            FunctionInstanceHookRowTop(fi, hookIndex)
                + (FtHookRowHeight - size) / 2.0,
            size,
            size);
    }

    /// <summary>
    /// Returns the element whose embed button contains <paramref name="pos"/>, or <c>null</c>.
    /// </summary>
    private ICanvasElement? HitTestMinimizeButton(Point pos)
    {
        if (_vm is null) return null;
        for (int nodeIndex = _vm.Nodes.Count - 1; nodeIndex >= 0; nodeIndex--)
        {
            var node = _vm.Nodes[nodeIndex];
            if (!_canEmbedElements.Contains(node)) continue;
            if (InlineMinimizeButtonRect(node).Contains(pos))
                return node;
        }
        for (int fiIndex = _vm.FunctionInstances.Count - 1; fiIndex >= 0; fiIndex--)
        {
            var fi = _vm.FunctionInstances[fiIndex];
            if (!_canEmbedElements.Contains(fi)) continue;
            if (InlineMinimizeButtonRect(fi).Contains(pos))
                return fi;
        }
        return null;
    }

    private (FunctionInstanceViewModel Fi, FunctionParameterHook Hook, ICanvasElement Element)?
        HitTestFiMinimizeButton(Point pos)
    {
        if (_vm is null) return null;
        for (int fiIndex = _vm.FunctionInstances.Count - 1; fiIndex >= 0; fiIndex--)
        {
            var fi = _vm.FunctionInstances[fiIndex];
            foreach (var ((mappedFi, hook), element) in _fiHookCanEmbedElement)
            {
                if (!ReferenceEquals(fi, mappedFi)) continue;
                int hookIndex = -1;
                if (!_fiVisibleHooks.TryGetValue(fi, out var hooks)) continue;
                for (int i = 0; i < hooks.Count; i++)
                {
                    if (ReferenceEquals(hooks[i], hook))
                    {
                        hookIndex = i;
                        break;
                    }
                }
                if (hookIndex >= 0 && InlineFiMinimizeButtonRect(fi, hookIndex).Contains(pos))
                    return (fi, hook, element);
            }
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
    internal (ICanvasElement? originEl, object hook, NodeViewModel paramNode, double rowX, double rowY, double rowW)? HitTestInlinedParamRow(Point pos)
    {
        if (_vm is null) return null;

        for (int ghostIndex = _vm.GhostNodes.Count - 1; ghostIndex >= 0; ghostIndex--)
        {
            var ghost = _vm.GhostNodes[ghostIndex];
            if (!ghost.IsFunctionInstance)
            {
                for (int i = 0; i < ghost.Hooks.Count; i++)
                {
                    var hook = ghost.Hooks[i];
                    var embedded = ghost.GetEmbeddedParameter(hook);
                    if (embedded is null) continue;
                    var rowTop = ghost.Y + NodeHeaderHeight
                        + (ghost.IsParameterNode ? 1 : 0) * HookRowHeight
                        + i * HookRowHeight;
                    if (!new Rect(ghost.X, rowTop, ghost.Width, HookRowHeight).Contains(pos)) continue;
                    var paramNode = _vm.Nodes.FirstOrDefault(node => node.UnderlyingNode == embedded)
                        ?? new NodeViewModel(embedded, _vm.Session, _vm.User);
                    return (ghost, hook, paramNode, ghost.X, rowTop, ghost.Width);
                }
            }

            if (!ghost.IsFunctionInstance) continue;
            for (int i = 0; i < ghost.ReferencedNode.Hooks.Count; i++)
            {
                if (ghost.ReferencedNode.Hooks[i] is not FunctionParameterHook hook) continue;
                var embedded = ghost.GetEmbeddedParameter(hook);
                if (embedded is null) continue;
                double rowTop = ghost.Y + NodeHeaderHeight + i * HookRowHeight;
                if (!new Rect(ghost.X, rowTop, ghost.Width, HookRowHeight).Contains(pos)) continue;
                var paramNode = _vm.Nodes.FirstOrDefault(node => node.UnderlyingNode == embedded)
                    ?? new NodeViewModel(embedded, _vm.Session, _vm.User);
                return (ghost, hook, paramNode, ghost.X, rowTop, ghost.Width);
            }
        }

        for (int nodeIndex = _vm.Nodes.Count - 1; nodeIndex >= 0; nodeIndex--)
        {
            var originNode = _vm.Nodes[nodeIndex];
            if (!_nodeVisibleHooks.TryGetValue(originNode, out var hooks)) continue;
            foreach (var ((mappedNode, hook), embeddedElement) in _hookEmbeddedElement)
            {
                if (!ReferenceEquals(originNode, mappedNode)) continue;
                if (embeddedElement is not NodeViewModel paramNode || !paramNode.IsParameterNode)
                    continue;
                int hookIdx = -1;
                for (int j = 0; j < hooks.Count; j++)
                    if (ReferenceEquals(hooks[j], hook)) { hookIdx = j; break; }
                if (hookIdx < 0) continue;

                double rw = NodeRenderWidth(originNode);
                double rowTop = NodeHookRowTop(originNode, hookIdx);
                var rowRect = new Rect(originNode.X, rowTop, rw, HookRowHeight);

                if (rowRect.Contains(pos))
                    return (originNode, hook, paramNode, originNode.X, rowTop, rw);
            }
        }

        for (int nodeIndex = _vm.Nodes.Count - 1; nodeIndex >= 0; nodeIndex--)
        {
            var originNode = _vm.Nodes[nodeIndex];
            if (!_nodeVisibleHooks.TryGetValue(originNode, out var hooks)
                || !_nestedEmbeddedParameterRows.TryGetValue(originNode, out var rows))
                continue;

            int rowOffset = originNode.IsParameterNode ? 1 : 0;
            double rw = NodeRenderWidth(originNode);
            for (int i = 0; i < rows.Count; i++)
            {
                var nested = rows[i];
                double indent = nested.Depth * NestedEmbeddedParameterIndent;
                var rowTopValue = NestedEmbeddedParameterRowTop(originNode, i);
                if (rowTopValue is not double rowTop) continue;
                var rowRect = new Rect(
                    originNode.X + indent,
                    rowTop,
                    Math.Max(0, rw - indent),
                    HookRowHeight);
                if (rowRect.Contains(pos))
                    return (originNode, nested, nested.Parameter,
                        originNode.X + indent, rowTop, rw - indent);
            }
        }

        // Function instances render behind nodes and ghosts, so check them after those layers.
        for (int fiIndex = _vm.FunctionInstances.Count - 1; fiIndex >= 0; fiIndex--)
        {
            var fi = _vm.FunctionInstances[fiIndex];
            if (!_fiVisibleHooks.TryGetValue(fi, out var fiHooks)) continue;
            foreach (var ((mappedFi, fpHook), embeddedElement) in _fiHookEmbeddedElement)
            {
                if (!ReferenceEquals(fi, mappedFi)) continue;
                if (embeddedElement is not NodeViewModel paramNode || !paramNode.IsParameterNode)
                    continue;
                int hookIdx = -1;
                for (int j = 0; j < fiHooks.Count; j++)
                    if (ReferenceEquals(fiHooks[j], fpHook)) { hookIdx = j; break; }
                if (hookIdx < 0) continue;

                double rw = fi.Width;
                double rowTop = FunctionInstanceHookRowTop(fi, hookIdx);
                var rowRect = new Rect(fi.X, rowTop, rw, FtHookRowHeight);

                if (rowRect.Contains(pos))
                    return (fi, fpHook, paramNode, fi.X, rowTop, rw);
            }
        }

        for (int fiIndex = _vm.FunctionInstances.Count - 1; fiIndex >= 0; fiIndex--)
        {
            var fi = _vm.FunctionInstances[fiIndex];
            if (!_fiVisibleHooks.TryGetValue(fi, out var hooks)
                || !_nestedEmbeddedParameterRows.TryGetValue(fi, out var rows))
                continue;

            double rw = fi.Width;
            for (int i = 0; i < rows.Count; i++)
            {
                var nested = rows[i];
                double indent = nested.Depth * NestedEmbeddedParameterIndent;
                var rowTopValue = NestedEmbeddedParameterRowTop(fi, i);
                if (rowTopValue is not double rowTop) continue;
                var rowRect = new Rect(
                    fi.X + indent,
                    rowTop,
                    Math.Max(0, rw - indent),
                    FtHookRowHeight);
                if (rowRect.Contains(pos))
                    return (fi, nested, nested.Parameter,
                        fi.X + indent, rowTop, rw - indent);
            }
        }

        return null;
    }

    /// <summary>Finds the topmost canvas element under <paramref name="pos"/>.</summary>
    /// <param name="testComments">When <c>false</c>, comment blocks are excluded (link creation).</param>
    private ICanvasElement? HitTest(Point pos, bool testComments)
    {
        if (_vm is null) return null;

        bool IsPointWithinRenderedBounds(ICanvasElement element, Point pos)
        {
            if (element is StartViewModel)
                return element.IsPointWithin(pos);

            return new Rect(element.X, element.Y, ElementRenderWidth(element), ElementRenderHeight(element))
                .Contains(pos);
        }

        ICanvasElement? TestHitsElement<T>(ObservableCollection<T> collection, Point pos) where T : ICanvasElement
        {
            for (int index = collection.Count - 1; index >= 0; index--)
            {
                var element = collection[index];
                if (IsCanvasElementInlined(element)) continue;
                if (IsPointWithinRenderedBounds(element, pos))
                    return element;
            }
            return null;
        }

        // Keep this layer order synchronized with Render(): starts, ghosts, nodes,
        // function parameters, instances, templates, then comments.
        ICanvasElement? hit = TestHitsElement(_vm.Starts, pos)
                ?? TestHitsElement(_vm.GhostNodes, pos)
                ?? TestHitsElement(_vm.Nodes, pos)
                ?? TestHitsElement(_vm.FunctionParameterVMs, pos)
                ?? TestHitsElement(_vm.FunctionInstances, pos)
                ?? TestHitsElement(_vm.FunctionTemplates, pos);

        
        if (hit is not null)
        {
            return hit;
        }

        return testComments ? TestHitsElement(_vm.CommentBlocks, pos) : null;
    }

}
