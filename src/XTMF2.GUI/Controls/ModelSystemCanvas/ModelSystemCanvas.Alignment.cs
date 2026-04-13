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
using Avalonia.Media;
using XTMF2.Editing;
using XTMF2.GUI.ViewModels;
using XTMF2.ModelSystemConstruct;

namespace XTMF2.GUI.Controls;

partial class ModelSystemCanvas
{

    // ── Alignment ─────────────────────────────────────────────────────────────

    private enum AlignMode
    {
        Left, Right, Top, Bottom, CenterHorizontal, CenterVertical
    }

    /// <summary>
    /// Moves all elements in <see cref="_multiSelection"/> so that their edges (or centres)
    /// are aligned according to <paramref name="mode"/>.
    /// The operation is committed as a single undoable batch via
    /// <see cref="ModelSystemSession.MoveElements"/>.
    /// </summary>
    private void AlignSelectedElements(AlignMode mode)
    {
        if (_vm is null || _multiSelection.Count < 2) return;

        // Compute the shared reference coordinate.
        double refCoord = mode switch
        {
            AlignMode.Left             => _multiSelection.Min(e => e.X),
            AlignMode.Right            => _multiSelection.Max(e => e.X + e.Width),
            AlignMode.Top              => _multiSelection.Min(e => e.Y),
            AlignMode.Bottom           => _multiSelection.Max(e => e.Y + e.Height),
            AlignMode.CenterHorizontal => _multiSelection.Average(e => e.Y + e.Height / 2.0),
            AlignMode.CenterVertical   => _multiSelection.Average(e => e.X + e.Width  / 2.0),
            _                          => 0.0
        };

        var nodeMoves     = new List<(Node, Rectangle)>();
        var commentMoves  = new List<(CommentBlock, Rectangle)>();
        var templateMoves = new List<(FunctionTemplate, Rectangle)>();
        var instanceMoves = new List<(FunctionInstance, Rectangle)>();

        foreach (var el in _multiSelection)
        {
            float newX = (float)el.X;
            float newY = (float)el.Y;

            switch (mode)
            {
                case AlignMode.Left:             newX = (float)refCoord;                      break;
                case AlignMode.Right:            newX = (float)(refCoord - el.Width);          break;
                case AlignMode.Top:              newY = (float)refCoord;                      break;
                case AlignMode.Bottom:           newY = (float)(refCoord - el.Height);        break;
                case AlignMode.CenterHorizontal: newY = (float)(refCoord - el.Height / 2.0); break;
                case AlignMode.CenterVertical:   newX = (float)(refCoord - el.Width  / 2.0); break;
            }
            newX = Math.Max(0f, newX);
            newY = Math.Max(0f, newY);

            switch (el)
            {
                case NodeViewModel nvm:
                {
                    var loc = nvm.UnderlyingNode.Location;
                    float w = loc.Width  is 0 ? 120f : loc.Width;
                    float h = loc.Height is 0 ? 50f  : loc.Height;
                    nodeMoves.Add((nvm.UnderlyingNode, new Rectangle(newX, newY, w, h)));
                    break;
                }
                case StartViewModel svm:
                    nodeMoves.Add((svm.UnderlyingStart,
                        new Rectangle(newX, newY, (float)svm.Diameter, (float)svm.Diameter)));
                    break;
                case CommentBlockViewModel cvm:
                    commentMoves.Add((cvm.UnderlyingBlock,
                        new Rectangle(newX, newY, (float)cvm.Width, (float)cvm.Height)));
                    break;
                case GhostNodeViewModel gvm:
                {
                    var loc = gvm.UnderlyingGhostNode.Location;
                    float w = loc.Width  is 0 ? 120f : loc.Width;
                    float h = loc.Height is 0 ? 50f  : loc.Height;
                    nodeMoves.Add((gvm.UnderlyingGhostNode, new Rectangle(newX, newY, w, h)));
                    break;
                }
                case FunctionTemplateViewModel ftvm:
                    templateMoves.Add((ftvm.UnderlyingTemplate,
                        new Rectangle(newX, newY, (float)ftvm.Width, (float)ftvm.Height)));
                    break;
                case FunctionInstanceViewModel fivm:
                    instanceMoves.Add((fivm.UnderlyingInstance,
                        new Rectangle(newX, newY, (float)fivm.Width, (float)fivm.Height)));
                    break;
                case FunctionParameterViewModel fpvm:
                {
                    var loc = fpvm.UnderlyingParameter.Location;
                    float w = loc.Width  is 0 ? 120f : loc.Width;
                    float h = loc.Height is 0 ? 50f  : loc.Height;
                    nodeMoves.Add((fpvm.UnderlyingParameter, new Rectangle(newX, newY, w, h)));
                    break;
                }
            }
        }

        _vm.Session.MoveElements(
            _vm.User,
            nodeMoves.Count     > 0 ? nodeMoves     : null,
            commentMoves.Count  > 0 ? commentMoves  : null,
            templateMoves.Count > 0 ? templateMoves : null,
            instanceMoves.Count > 0 ? instanceMoves : null,
            out _);
        InvalidateAndMeasure();
    }

    /// <summary>
    /// Spaces all elements in <see cref="_multiSelection"/> evenly along the horizontal
    /// (when <paramref name="horizontal"/> is <c>true</c>) or vertical axis, preserving the
    /// positions of the outermost elements and distributing the gap equally between the rest.
    /// The operation is committed as a single undoable batch via
    /// <see cref="ModelSystemSession.MoveElements"/>.
    /// Requires at least 3 selected elements to have any visible effect.
    /// </summary>
    private void DistributeSelectedElements(bool horizontal)
    {
        if (_vm is null || _multiSelection.Count < 3) return;

        // Helper that returns the element's dimension used for this axis.
        double Lead(ICanvasElement e)  => horizontal ? e.X        : e.Y;
        double Trail(ICanvasElement e) => horizontal ? e.X + e.Width : e.Y + e.Height;
        double Size(ICanvasElement e)  => horizontal ? e.Width    : e.Height;

        // Sort by leading edge along the chosen axis.
        var sorted = _multiSelection.OrderBy(Lead).ToList();

        // The outermost elements stay fixed; we distribute the inner ones.
        double totalSpan   = Trail(sorted[^1]) - Lead(sorted[0]);
        double totalSizes  = sorted.Sum(Size);
        double totalGaps   = totalSpan - totalSizes;
        double gapBetween  = totalGaps / (sorted.Count - 1);

        // Build new positions: first element unchanged, each subsequent one
        // placed directly after the previous with the uniform gap.
        var positions = new double[sorted.Count];
        positions[0] = Lead(sorted[0]);
        for (int i = 1; i < sorted.Count; i++)
            positions[i] = positions[i - 1] + Size(sorted[i - 1]) + gapBetween;

        var nodeMoves     = new List<(Node, Rectangle)>();
        var commentMoves  = new List<(CommentBlock, Rectangle)>();
        var templateMoves = new List<(FunctionTemplate, Rectangle)>();
        var instanceMoves = new List<(FunctionInstance, Rectangle)>();

        for (int i = 0; i < sorted.Count; i++)
        {
            var el     = sorted[i];
            float newX = horizontal ? (float)Math.Max(0, positions[i]) : (float)el.X;
            float newY = horizontal ? (float)el.Y : (float)Math.Max(0, positions[i]);

            switch (el)
            {
                case NodeViewModel nvm:
                {
                    var loc = nvm.UnderlyingNode.Location;
                    float w = loc.Width  is 0 ? 120f : loc.Width;
                    float h = loc.Height is 0 ? 50f  : loc.Height;
                    nodeMoves.Add((nvm.UnderlyingNode, new Rectangle(newX, newY, w, h)));
                    break;
                }
                case StartViewModel svm:
                    nodeMoves.Add((svm.UnderlyingStart,
                        new Rectangle(newX, newY, (float)svm.Diameter, (float)svm.Diameter)));
                    break;
                case CommentBlockViewModel cvm:
                    commentMoves.Add((cvm.UnderlyingBlock,
                        new Rectangle(newX, newY, (float)cvm.Width, (float)cvm.Height)));
                    break;
                case GhostNodeViewModel gvm:
                {
                    var loc = gvm.UnderlyingGhostNode.Location;
                    float w = loc.Width  is 0 ? 120f : loc.Width;
                    float h = loc.Height is 0 ? 50f  : loc.Height;
                    nodeMoves.Add((gvm.UnderlyingGhostNode, new Rectangle(newX, newY, w, h)));
                    break;
                }
                case FunctionTemplateViewModel ftvm:
                    templateMoves.Add((ftvm.UnderlyingTemplate,
                        new Rectangle(newX, newY, (float)ftvm.Width, (float)ftvm.Height)));
                    break;
                case FunctionInstanceViewModel fivm:
                    instanceMoves.Add((fivm.UnderlyingInstance,
                        new Rectangle(newX, newY, (float)fivm.Width, (float)fivm.Height)));
                    break;
                case FunctionParameterViewModel fpvm:
                {
                    var loc = fpvm.UnderlyingParameter.Location;
                    float w = loc.Width  is 0 ? 120f : loc.Width;
                    float h = loc.Height is 0 ? 50f  : loc.Height;
                    nodeMoves.Add((fpvm.UnderlyingParameter, new Rectangle(newX, newY, w, h)));
                    break;
                }
            }
        }

        _vm.Session.MoveElements(
            _vm.User,
            nodeMoves.Count     > 0 ? nodeMoves     : null,
            commentMoves.Count  > 0 ? commentMoves  : null,
            templateMoves.Count > 0 ? templateMoves : null,
            instanceMoves.Count > 0 ? instanceMoves : null,
            out _);
        InvalidateAndMeasure();
    }
    /// <summary>
    /// Clears the multi-selection set, restoring <see cref="ICanvasElement.IsSelected"/> to
    /// <c>false</c> on every element that was in the set.
    /// Call this before initiating a new single-element or empty-space selection.
    /// </summary>
    private void ClearMultiSelection()
    {
        foreach (var el in _multiSelection)
        {
            el.IsSelected = false;
        }
        _multiSelection.Clear();
        // Also clear IsSelected on the primary selected element (which may not be in
        // _multiSelection when using single-select).  This must happen before zeroing
        // SelectedElement so callers that immediately invoke SelectElementCommand or
        // SelectLinkCommand don't skip the IsSelected reset (those commands guard on
        // SelectedElement being non-null, but it will already be null after this method).
        if (_vm?.SelectedElement is { } primary)
        {
            primary.IsSelected = false;
        }
        
        _vm?.SelectedElement = null;
    }

    /// <summary>Returns a <see cref="Rect"/> that always has non-negative width and height,
    /// regardless of the relative order of <paramref name="p1"/> and <paramref name="p2"/>.</summary>
    private static Rect NormalizeRect(Point p1, Point p2) =>
        new(Math.Min(p1.X, p2.X), Math.Min(p1.Y, p2.Y),
            Math.Abs(p2.X - p1.X), Math.Abs(p2.Y - p1.Y));

    /// <summary>
    /// Draws the in-progress rubber-band selection rectangle (Ctrl+drag).
    /// Uses a dashed blue outline with a semi-transparent fill tint.
    /// Must be called inside a <see cref="DrawingContext.PushTransform"/> that maps model coords to
    /// screen coords (i.e. the existing scale transform in <see cref="Render"/>).
    /// </summary>
    private void RenderSelectionRect(DrawingContext ctx)
    {
        if (_selRectStart is not { } start) return;
        var rect = NormalizeRect(start, _selRectCurrent);
        var pen = new Pen(Brushes.CornflowerBlue, 1.5 / _scale, SelectionRectDash);
        ctx.DrawRectangle(SelectionRectFill, pen, rect, 2 / _scale, 2 / _scale);
    }

}
