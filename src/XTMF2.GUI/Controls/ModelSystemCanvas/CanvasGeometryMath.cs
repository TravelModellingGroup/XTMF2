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

namespace XTMF2.GUI.Controls;

/// <summary>
/// Pure-math geometry helpers extracted from <see cref="ModelSystemCanvas"/> so they can be
/// unit-tested without instantiating the full canvas control.
/// </summary>
internal static class CanvasGeometryMath
{
    /// <summary>Evaluates a cubic Bézier curve at parameter <paramref name="t"/> ∈ [0, 1].</summary>
    internal static Point SampleCubicBezier(Point p1, Point c1, Point c2, Point p2, double t)
    {
        double u = 1 - t;
        return new Point(
            u * u * u * p1.X + 3 * u * u * t * c1.X + 3 * u * t * t * c2.X + t * t * t * p2.X,
            u * u * u * p1.Y + 3 * u * u * t * c1.Y + 3 * u * t * t * c2.Y + t * t * t * p2.Y);
    }

    /// <summary>
    /// Given a line from an external point <paramref name="outside"/> toward the
    /// centre of <paramref name="rect"/>, returns the first intersection with the
    /// rectangle border (i.e. the entry edge point closest to <paramref name="outside"/>).
    /// Falls back to the rect centre when no intersection is found.
    /// </summary>
    internal static Point ClipLineToRect(Point outside, Rect rect)
    {
        var center = new Point(rect.X + rect.Width / 2.0, rect.Y + rect.Height / 2.0);
        double dx = center.X - outside.X;
        double dy = center.Y - outside.Y;

        double tBest = double.MaxValue;

        void TryT(double t, bool horizontal, double coord)
        {
            if (t <= 0 || t >= tBest) return;
            double other = horizontal
                ? outside.X + t * dx   // x coordinate when checking horizontal side
                : outside.Y + t * dy;  // y coordinate when checking vertical side
            if (horizontal && other >= rect.X && other <= rect.Right) tBest = t;
            if (!horizontal && other >= rect.Y && other <= rect.Bottom) tBest = t;
        }

        if (Math.Abs(dx) > 1e-10)
        {
            TryT((rect.X - outside.X) / dx, horizontal: false, 0);
            TryT((rect.Right - outside.X) / dx, horizontal: false, 0);
        }
        if (Math.Abs(dy) > 1e-10)
        {
            TryT((rect.Y - outside.Y) / dy, horizontal: true, 0);
            TryT((rect.Bottom - outside.Y) / dy, horizontal: true, 0);
        }

        if (tBest == double.MaxValue) return center;
        return new Point(outside.X + tBest * dx, outside.Y + tBest * dy);
    }

    /// <summary>Minimum distance from point <paramref name="p"/> to line segment AB.</summary>
    internal static double DistToSeg(Point p, Point a, Point b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lenSq = dx * dx + dy * dy;
        double nx, ny;
        if (lenSq < 1e-10)
        {
            nx = p.X - a.X;
            ny = p.Y - a.Y;
        }
        else
        {
            var t = Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq));
            nx = a.X + t * dx - p.X;
            ny = a.Y + t * dy - p.Y;
        }
        return Math.Sqrt(nx * nx + ny * ny);
    }
}
