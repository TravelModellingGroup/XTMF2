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

namespace XTMF2.Diff;

/// <summary>Represents the diff of a single node (module or start) between two model systems.</summary>
public sealed class NodeDiff
{
    /// <summary>The stable GUID of this node (from whichever side has it).</summary>
    public Guid Id { get; }

    public ElementDiffKind Kind { get; }

    /// <summary>Node name in the left model system, or null if not present.</summary>
    public string? LeftName { get; }
    /// <summary>Node name in the right model system, or null if not present.</summary>
    public string? RightName { get; }

    /// <summary>Assembly-qualified type name in the left model system, or null if not present.</summary>
    public string? LeftType { get; }
    /// <summary>Assembly-qualified type name in the right model system, or null if not present.</summary>
    public string? RightType { get; }

    /// <summary>String representation of the parameter value in the left model system.</summary>
    public string? LeftParameter { get; }
    /// <summary>String representation of the parameter value in the right model system.</summary>
    public string? RightParameter { get; }

    /// <summary>Whether the node is disabled in the left model system.</summary>
    public bool LeftDisabled { get; }
    /// <summary>Whether the node is disabled in the right model system.</summary>
    public bool RightDisabled { get; }

    /// <summary>
    /// Name of the node this node is embedded within as a parameter sub-module,
    /// or null if it is a top-level node.
    /// </summary>
    public string? ContainingNodeName { get; }

    /// <summary>Canvas position in the left model system (null when not present on that side).</summary>
    public Rectangle? LeftLocation { get; }
    /// <summary>Canvas position in the right model system (null when not present on that side).</summary>
    public Rectangle? RightLocation { get; }

    public NodeDiff(Guid id, ElementDiffKind kind,
        string? leftName, string? rightName,
        string? leftType, string? rightType,
        string? leftParameter, string? rightParameter,
        bool leftDisabled, bool rightDisabled,
        string? containingNodeName = null,
        Rectangle? leftLocation = null, Rectangle? rightLocation = null)
    {
        Id = id;
        Kind = kind;
        LeftName = leftName;
        RightName = rightName;
        LeftType = leftType;
        RightType = rightType;
        LeftParameter = leftParameter;
        RightParameter = rightParameter;
        LeftDisabled = leftDisabled;
        RightDisabled = rightDisabled;
        ContainingNodeName = containingNodeName;
        LeftLocation = leftLocation;
        RightLocation = rightLocation;
    }

    /// <summary>
    /// Human-readable display name using the available side's name.
    /// </summary>
    public string DisplayName => RightName ?? LeftName ?? Id.ToString("N")[..8];
}

/// <summary>Represents the diff of a single link between two model systems.</summary>
public sealed class LinkDiff
{
    /// <summary>The stable GUID of this link.</summary>
    public Guid Id { get; }

    public ElementDiffKind Kind { get; }

    /// <summary>GUID of the origin node (used as the natural key for matching).</summary>
    public Guid OriginNodeId { get; }
    /// <summary>Name of the origin node.</summary>
    public string OriginNodeName { get; }
    /// <summary>Name of the hook on the origin node (used as the natural key for matching).</summary>
    public string HookName { get; }

    /// <summary>Destination node GUIDs in the left model system.</summary>
    public IReadOnlyList<Guid> LeftDestinationIds { get; }
    /// <summary>Destination node GUIDs in the right model system.</summary>
    public IReadOnlyList<Guid> RightDestinationIds { get; }

    public bool LeftDisabled { get; }
    public bool RightDisabled { get; }

    public LinkDiff(Guid id, ElementDiffKind kind,
        Guid originNodeId, string hookName, string originNodeName,
        IReadOnlyList<Guid> leftDestinationIds, IReadOnlyList<Guid> rightDestinationIds,
        bool leftDisabled, bool rightDisabled)
    {
        Id = id;
        Kind = kind;
        OriginNodeId = originNodeId;
        HookName = hookName;
        OriginNodeName = originNodeName;
        LeftDestinationIds = leftDestinationIds;
        RightDestinationIds = rightDestinationIds;
        LeftDisabled = leftDisabled;
        RightDisabled = rightDisabled;
    }

    /// <summary>Short label for display (origin node name + hook name).</summary>
    public string DisplayLabel => $"{OriginNodeName} › {HookName}";
}

/// <summary>Represents the diff of a single comment block between two model systems.</summary>
public sealed class CommentBlockDiff
{
    /// <summary>The stable GUID of this comment block.</summary>
    public Guid Id { get; }

    public ElementDiffKind Kind { get; }

    /// <summary>Comment text in the left model system.</summary>
    public string? LeftComment { get; }
    /// <summary>Comment text in the right model system.</summary>
    public string? RightComment { get; }

    /// <summary>Canvas position in the left model system (null when not present on that side).</summary>
    public Rectangle? LeftLocation { get; }
    /// <summary>Canvas position in the right model system (null when not present on that side).</summary>
    public Rectangle? RightLocation { get; }

    public CommentBlockDiff(Guid id, ElementDiffKind kind, string? leftComment, string? rightComment,
        Rectangle? leftLocation = null, Rectangle? rightLocation = null)
    {
        Id = id;
        Kind = kind;
        LeftComment = leftComment;
        RightComment = rightComment;
        LeftLocation = leftLocation;
        RightLocation = rightLocation;
    }

    /// <summary>Short truncated display text for the comment.</summary>
    public string DisplayText
    {
        get
        {
            var text = RightComment ?? LeftComment ?? string.Empty;
            return text.Length <= 60 ? text : text[..60] + "…";
        }
    }
}

/// <summary>
/// Represents the diff of an entire boundary (recursive) between two model systems.
/// </summary>
public sealed class BoundaryDiff
{
    /// <summary>GUID from the left side (or right side if left is absent).</summary>
    public Guid LeftId { get; }
    /// <summary>GUID from the right side (or left side if right is absent).</summary>
    public Guid RightId { get; }

    public ElementDiffKind Kind { get; }

    public string? LeftName { get; }
    public string? RightName { get; }

    public IReadOnlyList<NodeDiff> Starts { get; }
    public IReadOnlyList<NodeDiff> Nodes { get; }
    public IReadOnlyList<LinkDiff> Links { get; }
    public IReadOnlyList<CommentBlockDiff> CommentBlocks { get; }
    public IReadOnlyList<BoundaryDiff> SubBoundaries { get; }
    public IReadOnlyList<FunctionTemplateDiff> FunctionTemplates { get; }
    public IReadOnlyList<FunctionInstanceDiff> FunctionInstances { get; }

    public BoundaryDiff(Guid leftId, Guid rightId, ElementDiffKind kind,
        string? leftName, string? rightName,
        IReadOnlyList<NodeDiff> starts,
        IReadOnlyList<NodeDiff> nodes,
        IReadOnlyList<LinkDiff> links,
        IReadOnlyList<CommentBlockDiff> commentBlocks,
        IReadOnlyList<FunctionTemplateDiff> functionTemplates,
        IReadOnlyList<BoundaryDiff> subBoundaries,
        IReadOnlyList<FunctionInstanceDiff>? functionInstances = null)
    {
        LeftId = leftId;
        RightId = rightId;
        Kind = kind;
        LeftName = leftName;
        RightName = rightName;
        Starts = starts;
        Nodes = nodes;
        Links = links;
        CommentBlocks = commentBlocks;
        FunctionTemplates = functionTemplates;
        SubBoundaries = subBoundaries;
        FunctionInstances = functionInstances ?? Array.Empty<FunctionInstanceDiff>();
    }

    /// <summary>Human-readable boundary name for display.</summary>
    public string DisplayName => RightName ?? LeftName ?? "(unnamed)";

    /// <summary>True if this boundary or any of its children contain changes.</summary>
    public bool HasChanges =>
        Kind != ElementDiffKind.Unchanged ||
        Starts.Any(s => s.Kind != ElementDiffKind.Unchanged) ||
        Nodes.Any(n => n.Kind != ElementDiffKind.Unchanged) ||
        Links.Any(l => l.Kind != ElementDiffKind.Unchanged) ||
        CommentBlocks.Any(c => c.Kind != ElementDiffKind.Unchanged) ||
        FunctionInstances.Any(fi => fi.Kind != ElementDiffKind.Unchanged) ||
        FunctionTemplates.Any(f => f.HasChanges) ||
        SubBoundaries.Any(b => b.HasChanges);

    /// <summary>Count of directly-changed elements within this boundary (excluding sub-boundaries).</summary>
    public int DirectChangeCount =>
        Starts.Count(s => s.Kind != ElementDiffKind.Unchanged) +
        Nodes.Count(n => n.Kind != ElementDiffKind.Unchanged) +
        Links.Count(l => l.Kind != ElementDiffKind.Unchanged) +
        CommentBlocks.Count(c => c.Kind != ElementDiffKind.Unchanged) +
        FunctionInstances.Count(fi => fi.Kind != ElementDiffKind.Unchanged) +
        FunctionTemplates.Count(f => f.HasChanges);
}

/// <summary>
/// The top-level result of comparing two model systems.
/// </summary>
public sealed class ModelSystemDiff
{
    /// <summary>Name of the left (base) model system.</summary>
    public string LeftName { get; }
    /// <summary>Name of the right (comparison) model system.</summary>
    public string RightName { get; }

    /// <summary>The recursive diff of the global boundary.</summary>
    public BoundaryDiff GlobalBoundary { get; }

    /// <summary>True if there are any differences between the two model systems.</summary>
    public bool HasChanges => GlobalBoundary.HasChanges;

    public ModelSystemDiff(string leftName, string rightName, BoundaryDiff globalBoundary)
    {
        LeftName = leftName;
        RightName = rightName;
        GlobalBoundary = globalBoundary;
    }
}

/// <summary>Represents the diff of a single <see cref="FunctionParameter"/> within a function template.</summary>
public sealed class FunctionParameterDiff
{
    /// <summary>The stable GUID of this function parameter.</summary>
    public Guid Id { get; }

    public ElementDiffKind Kind { get; }

    /// <summary>Parameter name in the left model system, or null if not present.</summary>
    public string? LeftName { get; }
    /// <summary>Parameter name in the right model system, or null if not present.</summary>
    public string? RightName { get; }

    /// <summary>Assembly-qualified type name in the left model system, or null if not present.</summary>
    public string? LeftType { get; }
    /// <summary>Assembly-qualified type name in the right model system, or null if not present.</summary>
    public string? RightType { get; }

    /// <summary>Canvas position in the left model system (null when not present on that side).</summary>
    public Rectangle? LeftLocation { get; }
    /// <summary>Canvas position in the right model system (null when not present on that side).</summary>
    public Rectangle? RightLocation { get; }

    public FunctionParameterDiff(Guid id, ElementDiffKind kind,
        string? leftName, string? rightName,
        string? leftType, string? rightType,
        Rectangle? leftLocation = null, Rectangle? rightLocation = null)
    {
        Id = id;
        Kind = kind;
        LeftName = leftName;
        RightName = rightName;
        LeftType = leftType;
        RightType = rightType;
        LeftLocation = leftLocation;
        RightLocation = rightLocation;
    }

    /// <summary>Human-readable display name using the available side's name.</summary>
    public string DisplayName => RightName ?? LeftName ?? Id.ToString("N")[..8];
}

/// <summary>
/// Represents the diff of an entire function template (recursive) between two model systems.
/// </summary>
public sealed class FunctionTemplateDiff
{
    /// <summary>GUID from the left side (or right side if left is absent).</summary>
    public Guid Id { get; }

    public ElementDiffKind Kind { get; }

    public string? LeftName { get; }
    public string? RightName { get; }

    /// <summary>Canvas position in the left model system (null when not present on that side).</summary>
    public Rectangle? LeftLocation { get; }
    /// <summary>Canvas position in the right model system (null when not present on that side).</summary>
    public Rectangle? RightLocation { get; }

    public BoundaryDiff SubBoundary { get; }

    /// <summary>Diffs for each <see cref="FunctionParameter"/> within this template.</summary>
    public IReadOnlyList<FunctionParameterDiff> FunctionParameters { get; }

    public FunctionTemplateDiff(Guid id, ElementDiffKind kind,
        string? leftName, string? rightName,
        Rectangle? leftLocation, Rectangle? rightLocation,
        BoundaryDiff subBoundary,
        IReadOnlyList<FunctionParameterDiff>? functionParameters = null)
    {
        Id = id;
        Kind = kind;
        LeftName = leftName;
        RightName = rightName;
        LeftLocation = leftLocation;
        RightLocation = rightLocation;
        SubBoundary = subBoundary;
        FunctionParameters = functionParameters ?? Array.Empty<FunctionParameterDiff>();
    }

    /// <summary>Human-readable boundary name for display.</summary>
    public string DisplayName => RightName ?? LeftName ?? "(unnamed)";

    /// <summary>True if this function template has changes (name, location, internal content, or function parameters).</summary>
    public bool HasChanges => Kind != ElementDiffKind.Unchanged;

    /// <summary>Count of directly-changed elements within this template (including function parameters).</summary>
    public int DirectChangeCount =>
        SubBoundary.DirectChangeCount +
        FunctionParameters.Count(fp => fp.Kind != ElementDiffKind.Unchanged);
}

/// <summary>Represents the diff of a single <see cref="FunctionInstance"/> between two model systems.</summary>
public sealed class FunctionInstanceDiff
{
    public Guid Id { get; }

    public ElementDiffKind Kind { get; }

    /// <summary>Instance name in the left model system, or null if not present.</summary>
    public string? LeftName { get; }
    /// <summary>Instance name in the right model system, or null if not present.</summary>
    public string? RightName { get; }

    /// <summary>Name of the template being instantiated, in the left model system.</summary>
    public string? LeftTemplateName { get; }
    /// <summary>Name of the template being instantiated, in the right model system.</summary>
    public string? RightTemplateName { get; }

    public bool LeftDisabled { get; }
    public bool RightDisabled { get; }

    public Rectangle? LeftLocation { get; }
    public Rectangle? RightLocation { get; }

    public FunctionInstanceDiff(Guid id, ElementDiffKind kind,
        string? leftName, string? rightName,
        string? leftTemplateName, string? rightTemplateName,
        bool leftDisabled, bool rightDisabled,
        Rectangle? leftLocation, Rectangle? rightLocation)
    {
        Id = id;
        Kind = kind;
        LeftName = leftName;
        RightName = rightName;
        LeftTemplateName = leftTemplateName;
        RightTemplateName = rightTemplateName;
        LeftDisabled = leftDisabled;
        RightDisabled = rightDisabled;
        LeftLocation = leftLocation;
        RightLocation = rightLocation;
    }

    public string DisplayName => RightName ?? LeftName ?? Id.ToString("N")[..8];
}