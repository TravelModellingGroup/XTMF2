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
using System.Text;
using XTMF2.Diff;

namespace XTMF2.GUI.ViewModels;

/// <summary>
/// Display item representing a boundary node in the diff tree.
/// Contains children which may be further <see cref="DiffBoundaryItem"/> instances
/// or <see cref="DiffElementItem"/> leaf items.
/// </summary>
public sealed class DiffBoundaryItem
{
    /// <summary>The diff kind for this boundary itself (Added / Removed / Unchanged).</summary>
    public ElementDiffKind Kind { get; }

    /// <summary>Display name for this boundary.</summary>
    public string DisplayName { get; }

    /// <summary>Count of directly-changed elements (nodes, links, comments) within this boundary.</summary>
    public int ChangeCount { get; }

    /// <summary>True if this boundary or any descendant has changes.</summary>
    public bool HasChanges { get; }

    /// <summary>The stable boundary GUID from the left (base) model system.</summary>
    public Guid LeftId { get; }

    /// <summary>The stable boundary GUID from the right (compare) model system.</summary>
    public Guid RightId { get; }

    /// <summary>True when this boundary exists in the left (base) model system.</summary>
    public bool ExistsInLeft => Kind != ElementDiffKind.Added;

    /// <summary>True when this boundary exists in the right (compare) model system.</summary>
    public bool ExistsInRight => Kind != ElementDiffKind.Removed;

    /// <summary>
    /// Tooltip text shown on hover. Null for unchanged boundaries with no child changes.
    /// </summary>
    public string? Tooltip { get; }

    /// <summary>
    /// The child items to display under this boundary.
    /// Filtered based on the <c>showUnchanged</c> option passed to the constructor.
    /// Contains <see cref="DiffElementItem"/> and nested <see cref="DiffBoundaryItem"/> instances.
    /// </summary>
    public IReadOnlyList<object> Children { get; }

    /// <summary>
    /// Initialises a <see cref="DiffBoundaryItem"/> from a <see cref="BoundaryDiff"/>.
    /// </summary>
    /// <param name="diff">The diff data.</param>
    /// <param name="showUnchanged">When true, unchanged elements are included in <see cref="Children"/>.</param>
    /// <param name="filter">Case-insensitive substring filter; null or empty means no filtering.</param>
    public DiffBoundaryItem(BoundaryDiff diff, bool showUnchanged, string? filter = null)
    {
        Kind = diff.Kind;
        DisplayName = diff.DisplayName;
        HasChanges = diff.HasChanges;
        ChangeCount = diff.DirectChangeCount;
        LeftId = diff.LeftId;
        RightId = diff.RightId;

        if (HasChanges || Kind != ElementDiffKind.Unchanged)
        {
            var kindLabel = Kind switch
            {
                ElementDiffKind.Added   => "Added",
                ElementDiffKind.Removed => "Removed",
                _                       => "Modified"
            };
            var sb = new StringBuilder();
            sb.Append($"{kindLabel} Boundary: \"{DisplayName}\"");
            if (ChangeCount > 0)
                sb.Append($"\n{ChangeCount} direct change(s)");
            Tooltip = sb.ToString();
        }

        bool hasFilter = !string.IsNullOrEmpty(filter);
        var children = new List<object>();

        foreach (var start in diff.Starts)
        {
            if (showUnchanged || start.Kind != ElementDiffKind.Unchanged)
            {
                var item = new DiffElementItem(start, isStart: true);
                if (!hasFilter || item.MatchesFilter(filter!))
                    children.Add(item);
            }
        }

        foreach (var node in diff.Nodes)
        {
            if (showUnchanged || node.Kind != ElementDiffKind.Unchanged)
            {
                var item = new DiffElementItem(node, isStart: false);
                if (!hasFilter || item.MatchesFilter(filter!))
                    children.Add(item);
            }
        }

        foreach (var link in diff.Links)
        {
            // Only surface structural changes (added/removed); rewires are noise for visual diff.
            if (link.Kind is ElementDiffKind.Added or ElementDiffKind.Removed)
            {
                var item = new DiffElementItem(link);
                if (!hasFilter || item.MatchesFilter(filter!))
                    children.Add(item);
            }
        }

        foreach (var comment in diff.CommentBlocks)
        {
            if (showUnchanged || comment.Kind != ElementDiffKind.Unchanged)
            {
                var item = new DiffElementItem(comment);
                if (!hasFilter || item.MatchesFilter(filter!))
                    children.Add(item);
            }
        }

        foreach(var ft in diff.FunctionTemplates)
        {
            if (showUnchanged || ft.HasChanges)
            {
                var ftSubItem = new DiffFunctionTemplateItem(ft, showUnchanged, filter);
                if (!hasFilter || ftSubItem.MatchesFilter(filter!))
                    children.Add(ftSubItem);
            }
        }

        foreach (var fi in diff.FunctionInstances)
        {
            if (showUnchanged || fi.Kind != ElementDiffKind.Unchanged)
            {
                var item = new DiffElementItem(fi);
                if (!hasFilter || item.MatchesFilter(filter!))
                    children.Add(item);
            }
        }

        foreach (var sub in diff.SubBoundaries)
        {
            if (showUnchanged || sub.HasChanges)
            {
                var subItem = new DiffBoundaryItem(sub, showUnchanged, filter);
                // Include the sub-boundary if its name matches OR it has matching children.
                if (!hasFilter || subItem.Children.Count > 0
                    || sub.DisplayName.Contains(filter!, StringComparison.OrdinalIgnoreCase))
                    children.Add(subItem);
            }
        }

        Children = children;
    }

}


/// <summary>
/// Display item representing a function template node in the diff tree.
/// Contains children which may be further <see cref="DiffFunctionTemplateItem"/> instances
/// or <see cref="DiffElementItem"/> leaf items.
/// </summary>
public sealed class DiffFunctionTemplateItem
{
    /// <summary>The diff kind for this function template itself (Added / Removed / Unchanged).</summary>
    public ElementDiffKind Kind { get; }

    /// <summary>Category tag for display — always "Function Template".</summary>
    public string TypeTag => "Function Template";

    /// <summary>Display name for this function template.</summary>
    public string DisplayName { get; }

    /// <summary>Count of directly-changed elements (nodes, links, comments) within this function template.</summary>
    public int ChangeCount { get; }

    /// <summary>True if this function template or any descendant has changes.</summary>
    public bool HasChanges { get; }

    /// <summary>The stable GUID of this function template.</summary>
    public Guid TemplateId { get; }

    /// <summary>True when this function template exists in the left (base) model system.</summary>
    public bool ExistsInLeft => Kind != ElementDiffKind.Added;

    /// <summary>True when this function template exists in the right (compare) model system.</summary>
    public bool ExistsInRight => Kind != ElementDiffKind.Removed;

    /// <summary>
    /// Tooltip text shown on hover. Null for unchanged templates with no child changes.
    /// </summary>
    public string? Tooltip { get; }

    /// <summary>
    /// The child items to display under this function template.
    /// Filtered based on the <c>showUnchanged</c> option passed to the constructor.
    /// Contains <see cref="DiffElementItem"/> and nested <see cref="DiffFunctionTemplateItem"/> instances.
    /// </summary>
    public IReadOnlyList<object> Children { get; }

    /// <summary>
    /// Initialises a <see cref="DiffBoundaryItem"/> from a <see cref="BoundaryDiff"/>.
    /// </summary>
    /// <param name="diff">The diff data.</param>
    /// <param name="showUnchanged">When true, unchanged elements are included in <see cref="Children"/>.</param>
    /// <param name="filter">Case-insensitive substring filter; null or empty means no filtering.</param>
    public DiffFunctionTemplateItem(FunctionTemplateDiff diff, bool showUnchanged, string? filter = null)
    {
        Kind = diff.Kind;
        DisplayName = diff.DisplayName;
        HasChanges = diff.HasChanges;
        ChangeCount = diff.DirectChangeCount;
        TemplateId = diff.Id;

        if (HasChanges || Kind != ElementDiffKind.Unchanged)
        {
            var kindLabel = Kind switch
            {
                ElementDiffKind.Added   => "Added",
                ElementDiffKind.Removed => "Removed",
                _                       => "Modified"
            };
            var sb = new StringBuilder();
            sb.Append($"{kindLabel} Function Template: \"{DisplayName}\"");
            if (ChangeCount > 0)
                sb.Append($"\n{ChangeCount} change(s) inside");
            if (diff.LeftName != diff.RightName && diff.LeftName is not null && diff.RightName is not null)
                sb.Append($"\nName: \"{diff.LeftName}\" \u2192 \"{diff.RightName}\"");
            if (diff.LeftLocation is Rectangle ftL && diff.RightLocation is Rectangle ftR && ftL != ftR)
                sb.Append($"\nMoved: ({ftL.X},{ftL.Y}) \u2192 ({ftR.X},{ftR.Y})");
            Tooltip = sb.ToString();
        }

        bool hasFilter = !string.IsNullOrEmpty(filter);
        var children = new List<object>();
        var sub = diff.SubBoundary;

        // Flatten the InternalModules boundary layer — every FunctionTemplate has exactly one,
        // so wrapping it in another tree node adds no information. Emit its children directly.
        foreach (var start in sub.Starts)
        {
            if (showUnchanged || start.Kind != ElementDiffKind.Unchanged)
            {
                var item = new DiffElementItem(start, isStart: true);
                if (!hasFilter || item.MatchesFilter(filter!))
                    children.Add(item);
            }
        }
        foreach (var node in sub.Nodes)
        {
            if (showUnchanged || node.Kind != ElementDiffKind.Unchanged)
            {
                var item = new DiffElementItem(node, isStart: false);
                if (!hasFilter || item.MatchesFilter(filter!))
                    children.Add(item);
            }
        }
        foreach (var link in sub.Links)
        {
            if (link.Kind is ElementDiffKind.Added or ElementDiffKind.Removed)
            {
                var item = new DiffElementItem(link);
                if (!hasFilter || item.MatchesFilter(filter!))
                    children.Add(item);
            }
        }
        foreach (var comment in sub.CommentBlocks)
        {
            if (showUnchanged || comment.Kind != ElementDiffKind.Unchanged)
            {
                var item = new DiffElementItem(comment);
                if (!hasFilter || item.MatchesFilter(filter!))
                    children.Add(item);
            }
        }

        Children = children;
    }
    
    /// <summary>
    /// Returns true if the element's label, type tag, or change description
    /// contains <paramref name="filter"/> (case-insensitive).
    /// </summary>
    internal bool MatchesFilter(string filter) =>
        DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase);

}

/// <summary>
/// Display item representing an individual element (node, link, or comment block) in the diff tree.
/// These are the leaf items within a <see cref="DiffBoundaryItem"/>.
/// </summary>
public sealed class DiffElementItem
{
    /// <summary>The diff kind (Added, Removed, Modified, Unchanged).</summary>
    public ElementDiffKind Kind { get; }

    /// <summary>
    /// Element category tag for display: "Start", "Node", "Link", or "Comment".
    /// </summary>
    public string TypeTag { get; }

    /// <summary>Primary display label for this element.</summary>
    public string Label { get; }

    /// <summary>
    /// A secondary description showing what changed (for <see cref="ElementDiffKind.Modified"/> items),
    /// or null when there is nothing extra to show.
    /// </summary>
    public string? ChangeDescription { get; }

    /// <summary>
    /// Full untruncated tooltip text for this element, or null for unchanged items.
    /// </summary>
    public string? Tooltip { get; }

    /// <summary>
    /// The stable element GUID used for "Go To" navigation.
    /// For links this is the origin node's ID; for all other elements it is the element's own ID.
    /// </summary>
    public Guid ElementId { get; }

    /// <summary>True when this element exists in the left (base) model system.</summary>
    public bool ExistsInLeft => Kind != ElementDiffKind.Added;

    /// <summary>True when this element exists in the right (compare) model system.</summary>
    public bool ExistsInRight => Kind != ElementDiffKind.Removed;

    /// <summary>Empty — leaf items have no children in the tree.</summary>
    public IReadOnlyList<object> Children { get; } = [];

    /// <summary>Constructs a display item from a <see cref="NodeDiff"/>.</summary>
    internal DiffElementItem(NodeDiff node, bool isStart)
    {
        Kind = node.Kind;
        if (isStart)
        {
            TypeTag = "Start";
            Label = node.DisplayName;
        }
        else if (node.ContainingNodeName is not null
                 || node.LeftLocation == Rectangle.Hidden
                 || node.RightLocation == Rectangle.Hidden)
        {
            TypeTag = "Parameter";
            Label = node.ContainingNodeName is not null
                ? $"{node.ContainingNodeName} › {node.DisplayName}"
                : node.DisplayName;
        }
        else
        {
            TypeTag = "Node";
            Label = node.DisplayName;
        }
        ChangeDescription = BuildNodeChangeDescription(node);
        Tooltip = BuildNodeTooltip(node, TypeTag);
        ElementId = node.Id;
    }

    /// <summary>Constructs a display item from a <see cref="LinkDiff"/>.</summary>
    internal DiffElementItem(LinkDiff link)
    {
        Kind = link.Kind;
        TypeTag = "Link";
        Label = $"{link.OriginNodeName} › {link.HookName}";
        ChangeDescription = null;
        Tooltip = BuildLinkTooltip(link);
        ElementId = link.OriginNodeId;
    }

    /// <summary>Constructs a display item from a <see cref="CommentBlockDiff"/>.</summary>
    internal DiffElementItem(CommentBlockDiff comment)
    {
        Kind = comment.Kind;
        TypeTag = "Comment";
        Label = comment.DisplayText;
        if (comment.Kind != ElementDiffKind.Modified)
        {
            ChangeDescription = null;
        }
        else
        {
            var parts = new List<string>(2);
            if (comment.LeftComment != comment.RightComment)
                parts.Add($"{Truncate(comment.LeftComment)} \u2192 {Truncate(comment.RightComment)}");
            if (comment.LeftLocation is Rectangle clL && comment.RightLocation is Rectangle crL && clL != crL)
                parts.Add("moved");
            ChangeDescription = parts.Count > 0 ? string.Join("; ", parts) : null;
        }
        Tooltip = BuildCommentTooltip(comment);
        ElementId = comment.Id;
    }

    internal DiffElementItem(FunctionInstanceDiff fi)
    {
        Kind = fi.Kind;
        TypeTag = "FunctionInstance";
        Label = fi.DisplayName;
        if (fi.Kind != ElementDiffKind.Modified)
        {
            ChangeDescription = null;
        }
        else
        {
            var parts = new List<string>(4);
            if (fi.LeftName != fi.RightName)
                parts.Add($"\"{fi.LeftName}\" \u2192 \"{fi.RightName}\"");
            if (fi.LeftTemplateName != fi.RightTemplateName)
                parts.Add($"template: {fi.LeftTemplateName} \u2192 {fi.RightTemplateName}");
            if (fi.LeftDisabled != fi.RightDisabled)
                parts.Add(fi.RightDisabled ? "disabled" : "re-enabled");
            if (fi.LeftLocation is Rectangle fiL && fi.RightLocation is Rectangle fiR
                && fiL != Rectangle.Hidden && fiR != Rectangle.Hidden && fiL != fiR)
                parts.Add("moved");
            ChangeDescription = parts.Count > 0 ? string.Join("; ", parts) : null;
        }
        Tooltip = BuildFunctionInstanceTooltip(fi);
        ElementId = fi.Id;
    }

    // ── Tooltip helpers ───────────────────────────────────────────────────

    private static string? BuildNodeTooltip(NodeDiff node, string typeTag)
    {
        if (node.Kind == ElementDiffKind.Unchanged) return null;

        var kindLabel = node.Kind switch
        {
            ElementDiffKind.Added   => "Added",
            ElementDiffKind.Removed => "Removed",
            _                       => "Modified"
        };
        var sb = new StringBuilder();
        sb.Append($"{kindLabel} {typeTag}: \"{node.DisplayName}\"");

        if (node.Kind == ElementDiffKind.Added)
        {
            if (node.RightType is not null)
                sb.Append($"\nType: {ShortTypeName(node.RightType)}");
            if (node.RightParameter is not null)
                sb.Append($"\nValue: {node.RightParameter}");
            if (node.RightDisabled)
                sb.Append("\nDisabled: yes");
        }
        else if (node.Kind == ElementDiffKind.Removed)
        {
            if (node.LeftType is not null)
                sb.Append($"\nType: {ShortTypeName(node.LeftType)}");
            if (node.LeftParameter is not null)
                sb.Append($"\nValue: {node.LeftParameter}");
            if (node.LeftDisabled)
                sb.Append("\nDisabled: yes");
        }
        else // Modified
        {
            if (node.LeftName != node.RightName)
                sb.Append($"\nName: \"{node.LeftName}\" \u2192 \"{node.RightName}\"");
            if (node.LeftType != node.RightType)
                sb.Append($"\nType: {ShortTypeName(node.LeftType)} \u2192 {ShortTypeName(node.RightType)}");
            if (node.LeftParameter != node.RightParameter)
                sb.Append($"\nValue (base):    {node.LeftParameter ?? "(none)"}\nValue (compare): {node.RightParameter ?? "(none)"}");
            if (node.LeftDisabled != node.RightDisabled)
                sb.Append($"\nDisabled: {node.LeftDisabled} \u2192 {node.RightDisabled}");
            if (node.LeftLocation is Rectangle ll && node.RightLocation is Rectangle rl
                && ll != Rectangle.Hidden && rl != Rectangle.Hidden && ll != rl)
                sb.Append($"\nMoved: ({ll.X},{ll.Y}) \u2192 ({rl.X},{rl.Y})");
        }
        return sb.ToString();
    }

    private static string? BuildLinkTooltip(LinkDiff link)
    {
        if (link.Kind == ElementDiffKind.Unchanged) return null;
        var kindLabel = link.Kind == ElementDiffKind.Added ? "Added" : "Removed";
        return $"{kindLabel} Link\nOrigin: \"{link.OriginNodeName}\"\nHook:   \"{link.HookName}\"";
    }

    private static string? BuildCommentTooltip(CommentBlockDiff comment)
    {
        if (comment.Kind == ElementDiffKind.Unchanged) return null;

        var sb = new StringBuilder();
        if (comment.Kind == ElementDiffKind.Added)
        {
            sb.Append("Added Comment:\n");
            sb.Append(comment.RightComment ?? string.Empty);
        }
        else if (comment.Kind == ElementDiffKind.Removed)
        {
            sb.Append("Removed Comment:\n");
            sb.Append(comment.LeftComment ?? string.Empty);
        }
        else
        {
            sb.Append("Modified Comment");
            if (comment.LeftComment != comment.RightComment)
            {
                sb.Append("\nBase:\n    ");
                sb.Append(comment.LeftComment ?? "(empty)");
                sb.Append("\nCompare:\n    ");
                sb.Append(comment.RightComment ?? "(empty)");
            }
            if (comment.LeftLocation is Rectangle cL && comment.RightLocation is Rectangle cR && cL != cR)
                sb.Append($"\nMoved: ({cL.X},{cL.Y}) \u2192 ({cR.X},{cR.Y})");
        }
        return sb.ToString();
    }

    private static string? BuildFunctionInstanceTooltip(FunctionInstanceDiff fi)
    {
        if (fi.Kind == ElementDiffKind.Unchanged) return null;

        var kindLabel = fi.Kind switch
        {
            ElementDiffKind.Added   => "Added",
            ElementDiffKind.Removed => "Removed",
            _                       => "Modified"
        };
        var sb = new StringBuilder();
        sb.Append($"{kindLabel} FunctionInstance: \"{fi.DisplayName}\"");

        if (fi.Kind == ElementDiffKind.Added)
        {
            if (fi.RightTemplateName is not null)
                sb.Append($"\nTemplate: \"{fi.RightTemplateName}\"");
            if (fi.RightDisabled)
                sb.Append("\nDisabled: yes");
        }
        else if (fi.Kind == ElementDiffKind.Removed)
        {
            if (fi.LeftTemplateName is not null)
                sb.Append($"\nTemplate: \"{fi.LeftTemplateName}\"");
            if (fi.LeftDisabled)
                sb.Append("\nDisabled: yes");
        }
        else
        {
            if (fi.LeftName != fi.RightName)
                sb.Append($"\nName: \"{fi.LeftName}\" \u2192 \"{fi.RightName}\"");
            if (fi.LeftTemplateName != fi.RightTemplateName)
                sb.Append($"\nTemplate: \"{fi.LeftTemplateName}\" \u2192 \"{fi.RightTemplateName}\"");
            if (fi.LeftDisabled != fi.RightDisabled)
                sb.Append($"\nDisabled: {fi.LeftDisabled} \u2192 {fi.RightDisabled}");
            if (fi.LeftLocation is Rectangle fiL && fi.RightLocation is Rectangle fiR
                && fiL != Rectangle.Hidden && fiR != Rectangle.Hidden && fiL != fiR)
                sb.Append($"\nMoved: ({fiL.X},{fiL.Y}) \u2192 ({fiR.X},{fiR.Y})");
        }
        return sb.ToString();
    }

    private static string? ShortTypeName(string? fullTypeName)
    {
        if (fullTypeName is null) return null;
        // Strip assembly info and take only the short class name.
        var comma = fullTypeName.IndexOf(',');
        var name = comma > 0 ? fullTypeName[..comma] : fullTypeName;
        var dot = name.LastIndexOf('.');
        return dot >= 0 ? name[(dot + 1)..] : name;
    }

    private static string? BuildNodeChangeDescription(NodeDiff node)
    {
        // For Added/Removed nodes show the parameter value if this is a parameter module.
        if (node.Kind == ElementDiffKind.Added)
        {
            var val = node.RightParameter;
            return val is not null ? $"value: {Truncate(val)}" : null;
        }
        if (node.Kind == ElementDiffKind.Removed)
        {
            var val = node.LeftParameter;
            return val is not null ? $"value: {Truncate(val)}" : null;
        }

        if (node.Kind != ElementDiffKind.Modified) return null;

        var parts = new List<string>(4);

        if (node.LeftName != node.RightName)
            parts.Add($"\"{node.LeftName}\" → \"{node.RightName}\"");

        if (node.LeftType != node.RightType)
            parts.Add("type changed");

        if (node.LeftParameter != node.RightParameter)
        {
            var leftVal = Truncate(node.LeftParameter) ?? "(none)";
            var rightVal = Truncate(node.RightParameter) ?? "(none)";
            parts.Add($"value: {leftVal} → {rightVal}");
        }

        if (node.LeftDisabled != node.RightDisabled)
            parts.Add(node.RightDisabled ? "disabled" : "re-enabled");

        if (node.LeftLocation is Rectangle leftLoc && node.RightLocation is Rectangle rightLoc
            && leftLoc != Rectangle.Hidden && rightLoc != Rectangle.Hidden
            && leftLoc != rightLoc)
            parts.Add("moved");

        return parts.Count > 0 ? string.Join("; ", parts) : null;
    }

    private static string? Truncate(string? s, int max = 40) =>
        s is null ? null : s.Length <= max ? s : s[..max] + "…";

    /// <summary>
    /// Returns true if the element's label, type tag, or change description
    /// contains <paramref name="filter"/> (case-insensitive).
    /// </summary>
    internal bool MatchesFilter(string filter) =>
        Label.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || TypeTag.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || (ChangeDescription?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);
}
