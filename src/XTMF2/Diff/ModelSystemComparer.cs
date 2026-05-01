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
using System.Collections.ObjectModel;
using System.Linq;
using XTMF2.ModelSystemConstruct;

namespace XTMF2.Diff;

/// <summary>
/// Computes a <see cref="ModelSystemDiff"/> between two loaded <see cref="ModelSystem"/> instances.
/// Elements are matched by their stable <see cref="Node.Id"/> / <see cref="Boundary.Id"/> /
/// <see cref="Link.Id"/> / <see cref="CommentBlock.Id"/> GUIDs.
/// Links are also matched by (origin node GUID, hook name) as a natural key when the link GUID
/// is not available (e.g. documents saved before stable IDs were introduced).
/// </summary>
public static class ModelSystemComparer
{
    /// <summary>
    /// Compares two model systems and returns the diff.
    /// </summary>
    public static ModelSystemDiff Compare(ModelSystem left, ModelSystem right)
    {
        var globalDiff = CompareBoundary(left.GlobalBoundary, right.GlobalBoundary);
        return new ModelSystemDiff(left.Name, right.Name, globalDiff);
    }

    private static BoundaryDiff CompareBoundary(Boundary left, Boundary right)
    {
        // Build nodeId → parent-node-name lookups from parameter-hook links in each boundary.
        var leftParents  = BuildParentNameLookup(left);
        var rightParents = BuildParentNameLookup(right);

        var starts       = CompareNodeSets(left.Starts.Cast<Node>(), right.Starts.Cast<Node>());
        var nodes        = CompareNodeSets(left.Modules, right.Modules, leftParents, rightParents);
        var links        = CompareLinkSets(left.Links,                right.Links);
        var comments     = CompareCommentSets(left.CommentBlocks,     right.CommentBlocks);
        var subBoundaries = CompareSubBoundaries(left.Boundaries,     right.Boundaries);
        var functionTemplates = CompareFunctionTemplates(left.FunctionTemplates, right.FunctionTemplates);
        var functionInstances = CompareFunctionInstanceSets(left.FunctionInstances, right.FunctionInstances);

        var kind = DetermineKind(starts, nodes, links, comments, functionTemplates, functionInstances, subBoundaries,
            left.Name != right.Name);

        return new BoundaryDiff(
            left.Id, right.Id, kind,
            left.Name, right.Name,
            starts, nodes, links, comments, functionTemplates, subBoundaries, functionInstances);
    }

    /// <summary>
    /// Builds a map of nodeId → parent node name for all nodes that are the destination
    /// of a parameter-hook link within the boundary.
    /// </summary>
    private static Dictionary<Guid, string> BuildParentNameLookup(Boundary boundary)
    {
        var result = new Dictionary<Guid, string>();
        foreach (var link in boundary.Links)
        {
            if (!link.OriginHook.IsParameter) continue;
            var parentName = link.Origin.Name;
            switch (link)
            {
                case SingleLink sl:
                    result[sl.Destination.Id] = parentName;
                    break;
                case MultiLink ml:
                    foreach (var dest in ml.Destinations)
                        result[dest.Id] = parentName;
                    break;
            }
        }
        return result;
    }

    // ── Node comparison ────────────────────────────────────────────────────

    private static IReadOnlyList<NodeDiff> CompareNodeSets(
        IEnumerable<Node> leftNodes, IEnumerable<Node> rightNodes,
        Dictionary<Guid, string>? leftParents = null,
        Dictionary<Guid, string>? rightParents = null)
    {
        var leftById  = leftNodes.ToDictionary(n => n.Id);
        var rightById = rightNodes.ToDictionary(n => n.Id);

        var result = new List<NodeDiff>();

        // Nodes present on left
        foreach (var (id, left) in leftById)
        {
            if (rightById.TryGetValue(id, out var right))
                result.Add(CompareNode(left, right, leftParents, rightParents));
            else
            {
                string? leftParent = null;
                leftParents?.TryGetValue(id, out leftParent);
                result.Add(NodeRemoved(left, leftParent));
            }
        }

        // Nodes present only on right (added)
        foreach (var (id, right) in rightById)
        {
            if (!leftById.ContainsKey(id))
            {
                string? rightParent = null;
                rightParents?.TryGetValue(id, out rightParent);
                result.Add(NodeAdded(right, rightParent));
            }
        }

        return result;
    }

    private static NodeDiff CompareNode(Node left, Node right,
        Dictionary<Guid, string>? leftParents = null,
        Dictionary<Guid, string>? rightParents = null)
    {
        var leftType  = left.Type?.AssemblyQualifiedName;
        var rightType = right.Type?.AssemblyQualifiedName;
        var leftParam  = left.ParameterValue?.Representation;
        var rightParam = right.ParameterValue?.Representation;
        string? leftParent = null;
        string? rightParent = null;
        leftParents?.TryGetValue(left.Id, out leftParent);
        rightParents?.TryGetValue(right.Id, out rightParent);
        var containingNodeName = rightParent ?? leftParent;

        // Only report a canvas move when both sides are actually placed on the canvas
        // (Hidden location means the node is embedded as a parameter, handled separately).
        bool locationChanged = left.Location != right.Location
            && left.Location != Rectangle.Hidden
            && right.Location != Rectangle.Hidden;

        bool changed = left.Name != right.Name
            || leftType != rightType
            || leftParam != rightParam
            || left.IsDisabled != right.IsDisabled
            || locationChanged;

        return new NodeDiff(
            left.Id,
            changed ? ElementDiffKind.Modified : ElementDiffKind.Unchanged,
            left.Name, right.Name,
            leftType, rightType,
            leftParam, rightParam,
            left.IsDisabled, right.IsDisabled,
            containingNodeName,
            left.Location, right.Location);
    }

    private static NodeDiff NodeAdded(Node n, string? containingNodeName = null) => new NodeDiff(
        n.Id, ElementDiffKind.Added,
        null, n.Name, null, n.Type?.AssemblyQualifiedName,
        null, n.ParameterValue?.Representation,
        false, n.IsDisabled, containingNodeName,
        null, n.Location);

    private static NodeDiff NodeRemoved(Node n, string? containingNodeName = null) => new NodeDiff(
        n.Id, ElementDiffKind.Removed,
        n.Name, null, n.Type?.AssemblyQualifiedName, null,
        n.ParameterValue?.Representation, null,
        n.IsDisabled, false, containingNodeName,
        n.Location, null);

    // ── Link comparison ────────────────────────────────────────────────────

    private static IReadOnlyList<LinkDiff> CompareLinkSets(
        IEnumerable<Link> leftLinks, IEnumerable<Link> rightLinks)
    {
        // Exclude parameter-hook links: those connect a parent module to an embedded
        // parameter sub-module and are already represented as "Parameter" node diffs.
        // Natural key: (origin node guid, hook name)  — stable even across renames
        var leftByKey  = leftLinks.Where(l => !l.OriginHook.IsParameter).ToDictionary(l  => (l.Origin.Id, l.OriginHook.Name));
        var rightByKey = rightLinks.Where(l => !l.OriginHook.IsParameter).ToDictionary(l => (l.Origin.Id, l.OriginHook.Name));

        var result = new List<LinkDiff>();

        foreach (var (key, left) in leftByKey)
        {
            if (rightByKey.TryGetValue(key, out var right))
                result.Add(CompareLink(left, right));
            else
                result.Add(LinkRemoved(left));
        }

        foreach (var (key, right) in rightByKey)
        {
            if (!leftByKey.ContainsKey(key))
                result.Add(LinkAdded(right));
        }

        return result;
    }

    private static IReadOnlyList<Guid> GetDestinationIds(Link link) => link switch
    {
        SingleLink sl => new[] { sl.Destination.Id },
        MultiLink  ml => ml.Destinations.Select(n => n.Id).ToArray(),
        _             => Array.Empty<Guid>()
    };

    private static LinkDiff CompareLink(Link left, Link right)
    {
        var leftDests  = GetDestinationIds(left);
        var rightDests = GetDestinationIds(right);
        bool changed = !leftDests.SequenceEqual(rightDests)
            || left.IsDisabled != right.IsDisabled
            || left.IsOrthogonal != right.IsOrthogonal;

        return new LinkDiff(
            left.Id, changed ? ElementDiffKind.Modified : ElementDiffKind.Unchanged,
            left.Origin.Id, left.OriginHook.Name, left.Origin.Name,
            leftDests, rightDests,
            left.IsDisabled, right.IsDisabled);
    }

    private static LinkDiff LinkAdded(Link l) => new LinkDiff(
        l.Id, ElementDiffKind.Added,
        l.Origin.Id, l.OriginHook.Name, l.Origin.Name,
        Array.Empty<Guid>(), GetDestinationIds(l),
        false, l.IsDisabled);

    private static LinkDiff LinkRemoved(Link l) => new LinkDiff(
        l.Id, ElementDiffKind.Removed,
        l.Origin.Id, l.OriginHook.Name, l.Origin.Name,
        GetDestinationIds(l), Array.Empty<Guid>(),
        l.IsDisabled, false);

    // ── CommentBlock comparison ────────────────────────────────────────────

    private static IReadOnlyList<CommentBlockDiff> CompareCommentSets(
        IEnumerable<CommentBlock> leftComments, IEnumerable<CommentBlock> rightComments)
    {
        var leftById  = leftComments.ToDictionary(c => c.Id);
        var rightById = rightComments.ToDictionary(c => c.Id);
        var result    = new List<CommentBlockDiff>();

        foreach (var (id, left) in leftById)
        {
            if (rightById.TryGetValue(id, out var right))
            {
                bool changed = left.Comment != right.Comment || left.Location != right.Location;
                result.Add(new CommentBlockDiff(id,
                    changed ? ElementDiffKind.Modified : ElementDiffKind.Unchanged,
                    left.Comment, right.Comment, left.Location, right.Location));
            }
            else
                result.Add(new CommentBlockDiff(id, ElementDiffKind.Removed, left.Comment, null, left.Location, null));
        }

        foreach (var (id, right) in rightById)
        {
            if (!leftById.ContainsKey(id))
                result.Add(new CommentBlockDiff(id, ElementDiffKind.Added, null, right.Comment, null, right.Location));
        }

        return result;
    }

    // ── Sub-boundary comparison ────────────────────────────────────────────

    private static IReadOnlyList<BoundaryDiff> CompareSubBoundaries(
        IEnumerable<Boundary> leftBoundaries, IEnumerable<Boundary> rightBoundaries)
    {
        var leftById  = leftBoundaries.ToDictionary(b => b.Id);
        var rightById = rightBoundaries.ToDictionary(b => b.Id);
        var result    = new List<BoundaryDiff>();

        foreach (var (id, left) in leftById)
        {
            if (rightById.TryGetValue(id, out var right))
                result.Add(CompareBoundary(left, right));
            else
                result.Add(BoundaryRemoved(left));
        }

        foreach (var (id, right) in rightById)
        {
            if (!leftById.ContainsKey(id))
                result.Add(BoundaryAdded(right));
        }

        return result;
    }

    private static BoundaryDiff BoundaryAdded(Boundary b) => new BoundaryDiff(
        Guid.Empty, b.Id, ElementDiffKind.Added, null, b.Name,
        b.Starts.Select(n => NodeAdded(n)).ToList(),
        b.Modules.Select(n => NodeAdded(n)).ToList(),
        b.Links.Select(l => LinkAdded(l)).ToList(),
        b.CommentBlocks.Select(c => new CommentBlockDiff(c.Id, ElementDiffKind.Added, null, c.Comment)).ToList(),
        b.FunctionTemplates.Select(ft => FunctionTemplateAdded(ft)).ToList(),
        b.Boundaries.Select(BoundaryAdded).ToList(),
        b.FunctionInstances.Select(fi => FunctionInstanceAdded(fi)).ToList());

    private static BoundaryDiff BoundaryRemoved(Boundary b) => new BoundaryDiff(
        b.Id, Guid.Empty, ElementDiffKind.Removed, b.Name, null,
        b.Starts.Select(n => NodeRemoved(n)).ToList(),
        b.Modules.Select(n => NodeRemoved(n)).ToList(),
        b.Links.Select(l => LinkRemoved(l)).ToList(),
        b.CommentBlocks.Select(c => new CommentBlockDiff(c.Id, ElementDiffKind.Removed, c.Comment, null)).ToList(),
        b.FunctionTemplates.Select(ft => FunctionTemplateRemoved(ft)).ToList(),
        b.Boundaries.Select(BoundaryRemoved).ToList(),
        b.FunctionInstances.Select(fi => FunctionInstanceRemoved(fi)).ToList());

    // ── Function template comparison ────────────────────────────────────────────

    private static IReadOnlyList<FunctionTemplateDiff> CompareFunctionTemplates(
        IEnumerable<FunctionTemplate> leftTemplates, IEnumerable<FunctionTemplate> rightTemplates)
    {
        var leftById  = leftTemplates.ToDictionary(ft => ft.Id);
        var rightById = rightTemplates.ToDictionary(ft => ft.Id);
        var result    = new List<FunctionTemplateDiff>();

        foreach (var (id, left) in leftById)
        {
            if (rightById.TryGetValue(id, out var right))
            {
                var subBoundary = CompareBoundary(left.InternalModules, right.InternalModules);

                // All element types inside InternalModules — including Starts — have stable
                // persisted GUIDs, so we can safely include them in the changed check.
                bool contentChanged =
                    left.Name != right.Name
                    || left.Location != right.Location
                    || subBoundary.Starts.Any(s => s.Kind != ElementDiffKind.Unchanged)
                    || subBoundary.Nodes.Any(n => n.Kind != ElementDiffKind.Unchanged)
                    || subBoundary.Links.Any(l => l.Kind != ElementDiffKind.Unchanged)
                    || subBoundary.CommentBlocks.Any(c => c.Kind != ElementDiffKind.Unchanged)
                    || subBoundary.FunctionInstances.Any(fi => fi.Kind != ElementDiffKind.Unchanged)
                    || subBoundary.FunctionTemplates.Any(ft => ft.HasChanges)
                    || subBoundary.SubBoundaries.Any(b => b.HasChanges);

                result.Add(new FunctionTemplateDiff(id,
                    contentChanged ? ElementDiffKind.Modified : ElementDiffKind.Unchanged,
                    left.Name, right.Name,
                    left.Location, right.Location,
                    subBoundary));
            }
            else
                result.Add(FunctionTemplateRemoved(left));
        }

        foreach (var (id, right) in rightById)
        {
            if (!leftById.ContainsKey(id))
                result.Add(FunctionTemplateAdded(right));
        }

        return result;
    }

    private static FunctionTemplateDiff FunctionTemplateAdded(FunctionTemplate ft) => new FunctionTemplateDiff(
        ft.Id, ElementDiffKind.Added, null, ft.Name,
        null, ft.Location,
        BoundaryAdded(ft.InternalModules));

    private static FunctionTemplateDiff FunctionTemplateRemoved(FunctionTemplate ft) => new FunctionTemplateDiff(
        ft.Id, ElementDiffKind.Removed, ft.Name, null,
        ft.Location, null,
        BoundaryRemoved(ft.InternalModules));

    // ── FunctionInstance comparison ────────────────────────────────────────

    private static IReadOnlyList<FunctionInstanceDiff> CompareFunctionInstanceSets(
        IEnumerable<FunctionInstance> leftInstances, IEnumerable<FunctionInstance> rightInstances)
    {
        var leftById  = leftInstances.ToDictionary(fi => fi.Id);
        var rightById = rightInstances.ToDictionary(fi => fi.Id);
        var result    = new List<FunctionInstanceDiff>();

        foreach (var (id, left) in leftById)
        {
            if (rightById.TryGetValue(id, out var right))
            {
                bool locationChanged = left.Location != right.Location
                    && left.Location != Rectangle.Hidden
                    && right.Location != Rectangle.Hidden;

                bool changed = left.Name != right.Name
                    || left.Template.Name != right.Template.Name
                    || left.IsDisabled != right.IsDisabled
                    || locationChanged;

                result.Add(new FunctionInstanceDiff(id,
                    changed ? ElementDiffKind.Modified : ElementDiffKind.Unchanged,
                    left.Name, right.Name,
                    left.Template.Name, right.Template.Name,
                    left.IsDisabled, right.IsDisabled,
                    left.Location, right.Location));
            }
            else
                result.Add(FunctionInstanceRemoved(left));
        }

        foreach (var (id, right) in rightById)
        {
            if (!leftById.ContainsKey(id))
                result.Add(FunctionInstanceAdded(right));
        }

        return result;
    }

    private static FunctionInstanceDiff FunctionInstanceAdded(FunctionInstance fi) => new FunctionInstanceDiff(
        fi.Id, ElementDiffKind.Added,
        null, fi.Name,
        null, fi.Template.Name,
        false, fi.IsDisabled,
        null, fi.Location);

    private static FunctionInstanceDiff FunctionInstanceRemoved(FunctionInstance fi) => new FunctionInstanceDiff(
        fi.Id, ElementDiffKind.Removed,
        fi.Name, null,
        fi.Template.Name, null,
        fi.IsDisabled, false,
        fi.Location, null);

    // ── Kind computation ───────────────────────────────────────────────────

    private static ElementDiffKind DetermineKind(
        IReadOnlyList<NodeDiff> starts,
        IReadOnlyList<NodeDiff> nodes,
        IReadOnlyList<LinkDiff> links,
        IReadOnlyList<CommentBlockDiff> comments,
        IReadOnlyList<FunctionTemplateDiff> functionTemplates,
        IReadOnlyList<FunctionInstanceDiff> functionInstances,
        IReadOnlyList<BoundaryDiff> subBoundaries,
        bool nameChanged)
    {
        if (nameChanged) return ElementDiffKind.Modified;
        if (starts.Any(s => s.Kind != ElementDiffKind.Unchanged)) return ElementDiffKind.Modified;
        if (nodes.Any(n => n.Kind != ElementDiffKind.Unchanged))  return ElementDiffKind.Modified;
        if (links.Any(l => l.Kind != ElementDiffKind.Unchanged))  return ElementDiffKind.Modified;
        if (comments.Any(c => c.Kind != ElementDiffKind.Unchanged)) return ElementDiffKind.Modified;
        if (functionInstances.Any(fi => fi.Kind != ElementDiffKind.Unchanged)) return ElementDiffKind.Modified;
        if (functionTemplates.Any(ft => ft.HasChanges)) return ElementDiffKind.Modified;
        if (subBoundaries.Any(b => b.HasChanges))       return ElementDiffKind.Modified;
        return ElementDiffKind.Unchanged;
    }
}
