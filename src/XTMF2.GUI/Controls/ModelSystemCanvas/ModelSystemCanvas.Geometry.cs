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
using Avalonia;
using Avalonia.Media;
using XTMF2.GUI.ViewModels;

namespace XTMF2.GUI.Controls;

partial class ModelSystemCanvas
{
    private ICanvasElement RenderedOrigin(LinkViewModel link)
    {
        var origin = link.Origin;
        return FindCloserGhost(origin, link.Destination) ?? origin;
    }

    private ICanvasElement? RenderedDestination(LinkViewModel link)
    {
        var destination = link.Destination;
        return destination is null
            ? null
            : FindCloserGhost(destination, link.Origin) ?? destination;
    }

    private GhostNodeViewModel? FindCloserGhost(ICanvasElement element, ICanvasElement? opposite)
    {
        if (opposite is null || _vm is null || !TryGetRepresentedNode(element, out var representedNode))
            return null;

        var originalDistance = DistanceSquared(element.CenterX, element.CenterY,
            opposite.CenterX, opposite.CenterY);
        GhostNodeViewModel? closest = null;
        var closestDistance = originalDistance;
        foreach (var ghost in _vm.GhostNodes)
        {
            if (!ReferenceEquals(ghost.UnderlyingGhostNode.ReferencedNode, representedNode))
                continue;

            var ghostDistance = DistanceSquared(ghost.CenterX, ghost.CenterY,
                opposite.CenterX, opposite.CenterY);
            if (ghostDistance < closestDistance)
            {
                closest = ghost;
                closestDistance = ghostDistance;
            }
        }
        return closest;
    }

    private static bool TryGetRepresentedNode(ICanvasElement element, out XTMF2.ModelSystemConstruct.Node node)
    {
        switch (element)
        {
            case NodeViewModel nodeVm:
                node = nodeVm.UnderlyingNode;
                return true;
            case FunctionInstanceViewModel instanceVm:
                node = instanceVm.UnderlyingInstance;
                return true;
            case GhostNodeViewModel ghostVm:
                node = ghostVm.UnderlyingGhostNode.ReferencedNode;
                return true;
            default:
                node = null!;
                return false;
        }
    }

    private static double DistanceSquared(double x1, double y1, double x2, double y2)
    {
        var dx = x1 - x2;
        var dy = y1 - y2;
        return dx * dx + dy * dy;
    }

    private Point? GhostHookAnchor(GhostNodeViewModel ghost, NodeHook hook, Point opposite)
    {
        var hooks = ghost.Hooks;
        var hookIndex = -1;
        for (int i = 0; i < hooks.Count; i++)
        {
            if (ReferenceEquals(hooks[i], hook) || hooks[i].Name == hook.Name)
            {
                hookIndex = i;
                break;
            }
        }
        if (hookIndex < 0) return null;

        var rowOffset = ghost.IsParameterNode ? 1 : 0;
        var y = ghost.Y + NodeHeaderHeight
            + (rowOffset + hookIndex) * HookRowHeight
            + HookRowHeight / 2.0;
        var goLeft = opposite.X < ghost.X + ghost.Width / 2.0;
        return new Point(goLeft ? ghost.X : ghost.X + ghost.Width, y);
    }

    private Point? RenderedOriginPoint(LinkViewModel link, ICanvasElement origin, Point destination)
        => origin is GhostNodeViewModel ghost
            ? GhostHookAnchor(ghost, link.UnderlyingLink.OriginHook, destination)
            : null;

    private Point? RenderedDestinationPoint(LinkViewModel link, ICanvasElement destination, Point origin)
        => destination is GhostNodeViewModel ghost
            ? GhostHookAnchor(ghost, link.UnderlyingLink.OriginHook, origin)
            : null;

    private Point OrthogonalOriginDirection(LinkViewModel link, Point destination)
    {
        if (_orthogonalBreakpointDragLinks.Contains(link.UnderlyingLink))
            return new Point(_orthogonalBreakpointPreviewX, destination.Y);
        if (link.UnderlyingLink.OrthogonalBreakpointX is { } breakpointX)
            return new Point(breakpointX, destination.Y);
        return destination;
    }

    /// <summary>
    /// Computes cubic Bézier control points for a direction-aware S-curve link.
    /// <para>
    /// <c>c1</c> is placed along the <em>exit</em> tangent at <c>p1</c> (rightward for hook
    /// anchors, radially outward for Start nodes). <c>c2</c> is placed along the
    /// <em>entry</em> tangent at <c>p2</c>, derived from the inward normal of the destination
    /// border face, so the curve arrives smoothly perpendicular to that face. Tension is
    /// proportional to the Euclidean distance between <c>p1</c> and <c>p2</c> so the curve
    /// scales naturally at any zoom and in any direction.
    /// </para>
    /// </summary>
    private (Point p1, Point c1, Point c2, Point p2) ComputeSCurve(LinkViewModel link)
    {
        var origin = RenderedOrigin(link);
        var destination = RenderedDestination(link);
        var destCenter = new Point(destination?.CenterX ?? link.X2, destination?.CenterY ?? link.Y2);

        // p1 and exit direction.
        Point p1;
        Vector exitDir;
        var ghostOriginPoint = RenderedOriginPoint(link, origin, destCenter);
        if (ghostOriginPoint is { } ghostP1)
        {
            p1 = ghostP1;
            exitDir = destCenter.X < origin.X ? new Vector(-1, 0) : new Vector(1, 0);
        }
        else if (origin is NodeViewModel originNvm
            && _hookAnchors.TryGetValue((originNvm, link.UnderlyingLink.OriginHook), out var hookPt))
        {
            // Exit from the left face when the destination centre is to the left of the node.
            bool goLeft = destCenter.X < originNvm.X;
            p1 = goLeft ? new Point(originNvm.X, hookPt.Y) : hookPt;
            exitDir = goLeft ? new Vector(-1, 0) : new Vector(1, 0);
        }
        else if (origin is FunctionInstanceViewModel fiOriginSC
            && link.UnderlyingLink.OriginHook is FunctionParameterHook fphSC
            && _fiHookAnchors.TryGetValue((fiOriginSC, fphSC), out var fiHookPtSC))
        {
            bool goLeft = destCenter.X < fiOriginSC.X;
            p1 = goLeft ? new Point(fiOriginSC.X, fiHookPtSC.Y) : fiHookPtSC;
            exitDir = goLeft ? new Vector(-1, 0) : new Vector(1, 0);
        }
        else if (origin is StartViewModel startOrigin)
        {
            var oc = new Point(startOrigin.CenterX, startOrigin.CenterY);
            p1 = BorderPoint(link.Origin, destCenter) ?? oc;
            var odx = p1.X - oc.X; var ody = p1.Y - oc.Y;
            var ol = Math.Sqrt(odx * odx + ody * ody);
            exitDir = ol < 0.1 ? new Vector(1, 0) : new Vector(odx / ol, ody / ol);
        }
        else
        {
            p1 = BorderPoint(origin, destCenter) ?? new Point(origin.CenterX, origin.CenterY);
            exitDir = new Vector(1, 0);
        }

        // p2: destination border point approached from p1's direction.
        var p2 = RenderedDestinationPoint(link, destination!, p1)
            ?? BorderPoint(destination, p1) ?? destCenter;

        // c1 follows the exit tangent; c2 steps back from p2 along the entry tangent.
        var entryDir = BorderInwardNormal(destination, p2);
        double dx = p2.X - p1.X, dy = p2.Y - p1.Y;
        double tension = Math.Max(Math.Sqrt(dx * dx + dy * dy) * 0.45, 50.0);

        var c1 = new Point(p1.X + exitDir.X * tension, p1.Y + exitDir.Y * tension);
        var c2 = new Point(p2.X - entryDir.X * tension, p2.Y - entryDir.Y * tension);

        return (p1, c1, c2, p2);
    }

    /// <summary>
    /// Computes the orthogonal-routing origin point (p1) for the given link —
    /// the hook-anchor dot for nodes, or the side border midpoint for starts/other elements.
    /// Extracted so both <see cref="ComputeOrthogonalPath"/> and the spine-X precomputation
    /// in <see cref="RenderLinks"/> can call it without duplicating logic.
    /// </summary>
    private Point ComputeOrthogonalOriginPoint(LinkViewModel link)
    {
        var origin = RenderedOrigin(link);
        var destination = RenderedDestination(link);
        var destCenter = new Point(destination?.CenterX ?? link.X2, destination?.CenterY ?? link.Y2);

        var destCenterO = OrthogonalOriginDirection(link, destCenter);

        var ghostOriginPoint = RenderedOriginPoint(link, origin, destCenterO);
        if (ghostOriginPoint is { } ghostP1)
            return ghostP1;

        if (origin is NodeViewModel originNvm
            && _hookAnchors.TryGetValue((originNvm, link.UnderlyingLink.OriginHook), out var hookPt))
        {
            bool goLeft = destCenterO.X < originNvm.X;
            return goLeft ? new Point(originNvm.X, hookPt.Y) : hookPt;
        }

        if (origin is FunctionInstanceViewModel fiOriginO
            && link.UnderlyingLink.OriginHook is FunctionParameterHook fphO
            && _fiHookAnchors.TryGetValue((fiOriginO, fphO), out var fiHookPtO))
        {
            bool goLeft = destCenterO.X < fiOriginO.X;
            return goLeft ? new Point(fiOriginO.X, fiHookPtO.Y) : fiHookPtO;
        }

        if (origin is StartViewModel startOriginO)
        {
            var oc = new Point(startOriginO.CenterX, startOriginO.CenterY);
            var r = StartViewModel.Radius;
            var dir = destCenter.X >= oc.X ? 1.0 : -1.0;
            return new Point(oc.X + r * dir, oc.Y);
        }

         return OrthogonalOriginBorderPoint(origin, destCenter)
             ?? new Point(origin.CenterX, origin.CenterY);
    }

    /// <summary>
    /// Computes a sequence of points forming an orthogonal (right-angle) routed path
    /// from the link origin to the link destination.
    /// <para>
    /// The path exits the origin horizontally, jogs vertically at the horizontal midpoint,
    /// then enters the destination horizontally.  When the destination is to the left of
    /// the origin a small stub extends rightward before doubling back, so that the exit
    /// direction is always respected.
    /// </para>
    /// <para>
    /// Attachment to the destination always prefers the left or right face so that the
    /// final segment enters horizontally rather than from the top or bottom.
    /// </para>
    /// <para>
    /// When <paramref name="sharedSpineX"/> is provided (non-null), it is used as the
    /// shared vertical-trunk X for all siblings of a multi-link group, so they all
    /// overlap on the horizontal exit and trunk segments and only diverge on the final
    /// horizontal branch to their individual destination.
    /// </para>
    /// </summary>
    private Point[] ComputeOrthogonalPath(LinkViewModel link, double? sharedSpineX = null)
    {
        const double MinStub = 24.0; // minimum rightward stub length

        var destination = RenderedDestination(link);
        var destCenter = new Point(destination?.CenterX ?? link.X2, destination?.CenterY ?? link.Y2);

        // p1 — origin hook/border point.
        var p1 = ComputeOrthogonalOriginPoint(link);

        // Determine the vertical trunk X.  For a shared group this is supplied by
        // the caller; otherwise derive it from the midpoint of this link alone.
        double spineX;
        if (sharedSpineX.HasValue)
        {
            spineX = sharedSpineX.Value;
        }
        else if (link.UnderlyingLink.OrthogonalBreakpointX is { } persistedSpineX)
        {
            spineX = persistedSpineX;
        }
        else
        {
            // p2 needs to be estimated with the approach direction from p1's side.
            var p2est = RenderedDestinationPoint(link, destination!,
                            new Point(p1.X + 1, p1.Y))
                        ?? OrthogonalDestBorderPoint(destination,
                            new Point(p1.X + 1, p1.Y)) ?? destCenter;
            spineX = (p1.X + p2est.X) * 0.5;
            spineX = Math.Max(spineX, p1.X + MinStub);
        }

        // p2 — destination side border point chosen based on which side of the
        //       destination the trunk sits on (left face when trunk is to the left,
        //       right face when trunk is to the right).
        var approachPt = new Point(spineX, p1.Y);
        var p2 = RenderedDestinationPoint(link, destination!, approachPt)
            ?? OrthogonalDestBorderPoint(destination, approachPt) ?? destCenter;

        // Three intermediate points: exit stub, corner, entry corner.
        var corner1 = new Point(spineX, p1.Y);  // end of horizontal exit segment
        var corner2 = new Point(spineX, p2.Y);  // end of vertical segment

        return [p1, corner1, corner2, p2];
    }

    /// <summary>
    /// Returns a border point on the rightward (or leftward, when destination is to the left)
    /// face of <paramref name="element"/>, at the element's vertical centre.
    /// Used as the <em>origin</em> departure point for orthogonal links on non-hook elements.
    /// Falls back to <see cref="BorderPoint"/> for non-rectangular origins.
    /// </summary>
    private Point? OrthogonalOriginBorderPoint(ICanvasElement? element, Point destCenter)
    {
        if (element is null) return null;

        Rect? r = element switch
        {
            NodeViewModel nvm => new Rect(nvm.X, nvm.Y, NodeRenderWidth(nvm), NodeRenderHeight(nvm)),
            GhostNodeViewModel gnvm => new Rect(gnvm.X, gnvm.Y, gnvm.Width, gnvm.Height),
            FunctionInstanceViewModel fiv => new Rect(fiv.X, fiv.Y, fiv.Width, fiv.Height),
            FunctionParameterViewModel fp => new Rect(fp.X, fp.Y, fp.Width, fp.Height),
            _ => (Rect?)null
        };

        if (r is { } rect)
        {
            double midY = rect.Y + rect.Height * 0.5;
            bool goRight = destCenter.X >= rect.X + rect.Width * 0.5;
            return new Point(goRight ? rect.Right : rect.X, midY);
        }

        return BorderPoint(element, destCenter);
    }

    /// <summary>
    /// Returns the attachment point on the <em>left</em> or <em>right</em> face of
    /// <paramref name="element"/> (at the element's vertical centre) so that orthogonal
    /// links always arrive horizontally.
    /// For circular elements (Start nodes) the radial border point is returned instead.
    /// </summary>
    private Point? OrthogonalDestBorderPoint(ICanvasElement? element, Point approachFrom)
    {
        if (element is null) return null;

        Rect? r = element switch
        {
            NodeViewModel nvm => new Rect(nvm.X, nvm.Y, NodeRenderWidth(nvm), NodeRenderHeight(nvm)),
            GhostNodeViewModel gnvm => new Rect(gnvm.X, gnvm.Y, gnvm.Width, gnvm.Height),
            FunctionInstanceViewModel fiv => new Rect(fiv.X, fiv.Y, fiv.Width, fiv.Height),
            FunctionParameterViewModel fp => new Rect(fp.X, fp.Y, fp.Width, fp.Height),
            _ => (Rect?)null
        };

        if (r is { } rect)
        {
            double midY = rect.Y + rect.Height * 0.5;
            bool fromLeft = approachFrom.X < rect.X + rect.Width * 0.5;
            return new Point(fromLeft ? rect.X : rect.Right, midY);
        }

        // Circular (Start) or unknown: fall back to the standard radial border point.
        return BorderPoint(element, approachFrom);
    }

    /// <summary>Builds a <see cref="StreamGeometry"/> polyline through <paramref name="pts"/>.</summary>
    private static StreamGeometry MakePolyGeo(Point[] pts)
    {
        var g = new StreamGeometry();
        using var gc = g.Open();
        gc.BeginFigure(pts[0], isFilled: false);
        for (int i = 1; i < pts.Length; i++)
        {
            gc.LineTo(pts[i]);
        }
        gc.EndFigure(isClosed: false);
        return g;
    }

    /// <summary>
    /// Builds a single-segment <see cref="StreamGeometry"/> from <paramref name="a"/> to <paramref name="b"/>.
    /// Used to draw an individual branch segment without allocating a full array.
    /// </summary>
    private static StreamGeometry MakeSegGeo(Point a, Point b)
    {
        var g = new StreamGeometry();
        using var gc = g.Open();
        gc.BeginFigure(a, isFilled: false);
        gc.LineTo(b);
        gc.EndFigure(isClosed: false);
        return g;
    }

    /// <summary>
    /// Returns a new <see cref="StreamGeometry"/> identical to <see cref="MakePolyGeo"/>
    /// but with the final point replaced by <paramref name="newLastPt"/>.
    /// Used to shorten the shaft so it does not overlap a filled arrowhead.
    /// </summary>
    private static StreamGeometry ReplacePolyGeoLastPoint(Point[] pts, Point newLastPt)
    {
        var g = new StreamGeometry();
        using var gc = g.Open();
        gc.BeginFigure(pts[0], isFilled: false);
        for (int i = 1; i < pts.Length - 1; i++)
            gc.LineTo(pts[i]);
        gc.LineTo(newLastPt);
        gc.EndFigure(isClosed: false);
        return g;
    }

    /// <summary>Evaluates a cubic Bézier curve at parameter <paramref name="t"/> ∈ [0, 1].</summary>
    private static Point SampleCubicBezier(Point p1, Point c1, Point c2, Point p2, double t)
        => CanvasGeometryMath.SampleCubicBezier(p1, c1, c2, p2, t);

    /// <summary>
    /// Returns the unit vector pointing <em>into</em> <paramref name="dest"/> through
    /// the border face that <paramref name="borderPt"/> sits on.
    /// For axis-aligned rect borders this is always one of ±X or ±Y.
    /// For circles (Start nodes) it is the inward radius direction.
    /// </summary>
    private Vector BorderInwardNormal(ICanvasElement? dest, Point borderPt)
    {
        const double eps = 1.5;

        Rect? r = dest switch
        {
            NodeViewModel nvm => new Rect(nvm.X, nvm.Y, NodeRenderWidth(nvm), NodeRenderHeight(nvm)),
            GhostNodeViewModel gnvm => new Rect(gnvm.X, gnvm.Y, gnvm.Width, gnvm.Height),
            FunctionInstanceViewModel fiv => new Rect(fiv.X, fiv.Y, fiv.Width, fiv.Height),
            _ => (Rect?)null
        };

        if (r is { } rect)
        {
            if (Math.Abs(borderPt.X - rect.X) < eps) return new Vector(1, 0); // left face  → rightward
            if (Math.Abs(borderPt.X - rect.Right) < eps) return new Vector(-1, 0); // right face → leftward
            if (Math.Abs(borderPt.Y - rect.Y) < eps) return new Vector(0, 1); // top face   → downward
            if (Math.Abs(borderPt.Y - rect.Bottom) < eps) return new Vector(0, -1); // bottom face → upward
        }

        // Circle or unknown: inward radial direction.
        var cx = dest?.CenterX ?? borderPt.X;
        var cy = dest?.CenterY ?? borderPt.Y;
        var ddx = cx - borderPt.X; var ddy = cy - borderPt.Y;
        var len = Math.Sqrt(ddx * ddx + ddy * ddy);
        return len < 0.1 ? new Vector(-1, 0) : new Vector(ddx / len, ddy / len);
    }

    /// <summary>
    /// Returns a point one arrowhead-length back from <paramref name="borderPt"/> along the
    /// inward-facing normal of the destination border face, so the arrowhead always
    /// arrives perpendicular to that face.
    /// </summary>
    private Point BorderArrivalFrom(ICanvasElement? dest, Point borderPt)
    {
        double back = ArrowSize * 1.5;
        var normal = BorderInwardNormal(dest, borderPt);
        return new Point(borderPt.X - normal.X * back, borderPt.Y - normal.Y * back);
    }

    /// <summary>
    /// Returns the point on <paramref name="element"/>'s visual border that lies on
    /// the line between the element's centre and <paramref name="other"/>.
    /// Returns <c>null</c> when <paramref name="element"/> is <c>null</c>.
    /// </summary>
    private Point? BorderPoint(ICanvasElement? element, Point other)
    {
        if (element is null) return null;

        if (element is NodeViewModel nvm)
        {
            var rect = new Rect(nvm.X, nvm.Y, NodeRenderWidth(nvm), NodeRenderHeight(nvm));
            return ClipLineToRect(other, rect);
        }

        if (element is GhostNodeViewModel gnvm)
        {
            var rect = new Rect(gnvm.X, gnvm.Y, gnvm.Width, gnvm.Height);
            return ClipLineToRect(other, rect);
        }

        if (element is FunctionInstanceViewModel fivm)
        {
            var rect = new Rect(fivm.X, fivm.Y, fivm.Width, FunctionInstanceRenderHeight(fivm));
            return ClipLineToRect(other, rect);
        }

        if (element is FunctionParameterViewModel fpvmBP)
        {
            var rect = new Rect(fpvmBP.X, fpvmBP.Y, fpvmBP.Width, fpvmBP.Height);
            return ClipLineToRect(other, rect);
        }

        if (element is StartViewModel)
        {
            var center = new Point(element.CenterX, element.CenterY);
            var dx = other.X - center.X;
            var dy = other.Y - center.Y;
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1) return center;
            var r = StartViewModel.Radius;
            return new Point(center.X + r * dx / len, center.Y + r * dy / len);
        }

        return new Point(element.CenterX, element.CenterY);
    }

    /// <summary>
    /// Given a line from an external point <paramref name="outside"/> toward the
    /// centre of <paramref name="rect"/>, returns the first intersection with the
    /// rectangle border (i.e. the entry edge point closest to <paramref name="outside"/>).
    /// Falls back to the rect centre when no intersection is found.
    /// </summary>
    private static Point ClipLineToRect(Point outside, Rect rect)
        => CanvasGeometryMath.ClipLineToRect(outside, rect);

}
