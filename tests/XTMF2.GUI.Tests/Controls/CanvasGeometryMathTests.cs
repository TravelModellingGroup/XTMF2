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

using Avalonia;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.GUI.Controls;

namespace XTMF2.GUI.Tests.Controls;

[TestClass]
public class CanvasGeometryMathTests
{
    // ── SampleCubicBezier ─────────────────────────────────────────────────

    [TestMethod]
    public void SampleCubicBezier_AtT0_ReturnsP1()
    {
        var p1 = new Point(0, 0);
        var c1 = new Point(10, 0);
        var c2 = new Point(90, 100);
        var p2 = new Point(100, 100);

        var result = CanvasGeometryMath.SampleCubicBezier(p1, c1, c2, p2, 0.0);

        Assert.AreEqual(p1.X, result.X, 1e-9);
        Assert.AreEqual(p1.Y, result.Y, 1e-9);
    }

    [TestMethod]
    public void SampleCubicBezier_AtT1_ReturnsP2()
    {
        var p1 = new Point(0, 0);
        var c1 = new Point(10, 0);
        var c2 = new Point(90, 100);
        var p2 = new Point(100, 100);

        var result = CanvasGeometryMath.SampleCubicBezier(p1, c1, c2, p2, 1.0);

        Assert.AreEqual(p2.X, result.X, 1e-9);
        Assert.AreEqual(p2.Y, result.Y, 1e-9);
    }

    [TestMethod]
    public void SampleCubicBezier_Degenerate_StraightLine_MidpointIsCenter()
    {
        // When p1==c1 and c2==p2, the curve degenerates to a straight line.
        var p1 = new Point(0, 0);
        var p2 = new Point(100, 0);

        var result = CanvasGeometryMath.SampleCubicBezier(p1, p1, p2, p2, 0.5);

        Assert.AreEqual(50.0, result.X, 1e-9);
        Assert.AreEqual(0.0, result.Y, 1e-9);
    }

    [TestMethod]
    public void SampleCubicBezier_KnownControlPoints_MatchesFormula()
    {
        // Hand-computed: p1=(0,0) c1=(1,0) c2=(0,1) p2=(1,1)  at t=0.5
        // B(0.5) = (1/8)(0) + 3(1/4)(1/2)(1) + 3(1/2)(1/4)(0) + (1/8)(1) = 3/8 + 0 + 1/8 = 0.5 for X
        // Similarly Y = 0 + 3/8*(0) + 3/8*(1) + 1/8 = 0.5
        var p1 = new Point(0, 0);
        var c1 = new Point(1, 0);
        var c2 = new Point(0, 1);
        var p2 = new Point(1, 1);

        var result = CanvasGeometryMath.SampleCubicBezier(p1, c1, c2, p2, 0.5);

        Assert.AreEqual(0.5, result.X, 1e-9);
        Assert.AreEqual(0.5, result.Y, 1e-9);
    }

    // ── ClipLineToRect ────────────────────────────────────────────────────

    [TestMethod]
    public void ClipLineToRect_PointDirectlyLeft_ReturnsLeftEdgeMidpoint()
    {
        var outside = new Point(-50, 50);
        var rect = new Rect(0, 0, 100, 100);

        var result = CanvasGeometryMath.ClipLineToRect(outside, rect);

        // Line from (-50, 50) toward center (50, 50) hits the left edge at x=0, y=50
        Assert.AreEqual(0.0, result.X, 1e-6);
        Assert.AreEqual(50.0, result.Y, 1e-6);
    }

    [TestMethod]
    public void ClipLineToRect_PointDirectlyAbove_ReturnsTopEdgeMidpoint()
    {
        var outside = new Point(50, -50);
        var rect = new Rect(0, 0, 100, 100);

        var result = CanvasGeometryMath.ClipLineToRect(outside, rect);

        // Line from (50, -50) toward center (50, 50) hits the top edge at x=50, y=0
        Assert.AreEqual(50.0, result.X, 1e-6);
        Assert.AreEqual(0.0, result.Y, 1e-6);
    }

    [TestMethod]
    public void ClipLineToRect_PointInsideRect_FallsBackToCenter()
    {
        // When the "outside" point is actually inside, t values are negative → no hit → return center.
        var inside = new Point(50, 50);
        var rect = new Rect(0, 0, 100, 100);

        var result = CanvasGeometryMath.ClipLineToRect(inside, rect);

        Assert.AreEqual(50.0, result.X, 1e-6);
        Assert.AreEqual(50.0, result.Y, 1e-6);
    }

    // ── DistToSeg ─────────────────────────────────────────────────────────

    [TestMethod]
    public void DistToSeg_PointOnSegment_ReturnsZero()
    {
        var p = new Point(50, 0);
        var a = new Point(0, 0);
        var b = new Point(100, 0);

        var dist = CanvasGeometryMath.DistToSeg(p, a, b);

        Assert.AreEqual(0.0, dist, 1e-9);
    }

    [TestMethod]
    public void DistToSeg_PointAtEndpointA_ReturnsZero()
    {
        var p = new Point(0, 0);
        var b = new Point(100, 0);

        var dist = CanvasGeometryMath.DistToSeg(p, p, b);

        Assert.AreEqual(0.0, dist, 1e-9);
    }

    [TestMethod]
    public void DistToSeg_PointPerpendicularToMidpoint_ReturnsCorrectDistance()
    {
        // Segment along X axis from (0,0) to (100,0); point above at (50,30)
        var p = new Point(50, 30);
        var a = new Point(0, 0);
        var b = new Point(100, 0);

        var dist = CanvasGeometryMath.DistToSeg(p, a, b);

        Assert.AreEqual(30.0, dist, 1e-9);
    }

    [TestMethod]
    public void DistToSeg_PointBeyondEndpointB_ReturnsDistanceToB()
    {
        var p = new Point(200, 0);
        var a = new Point(0, 0);
        var b = new Point(100, 0);

        var dist = CanvasGeometryMath.DistToSeg(p, a, b);

        Assert.AreEqual(100.0, dist, 1e-9);
    }

    [TestMethod]
    public void DistToSeg_DegenerateSegment_ReturnsDistanceToPoint()
    {
        // a == b → degenerate segment; distance should be |p - a|
        var p = new Point(3, 4);
        var a = new Point(0, 0);

        var dist = CanvasGeometryMath.DistToSeg(p, a, a);

        Assert.AreEqual(5.0, dist, 1e-9);
    }
}
