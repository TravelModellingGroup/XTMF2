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
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using XTMF2;
using XTMF2.GUI.ViewModels;

namespace XTMF2.GUI.Controls;

/// <summary>
/// A custom Avalonia <see cref="Control"/> that renders the model system canvas
/// using a <see cref="DrawingContext"/>.
/// <para>
/// Nodes are drawn as rounded rectangles, Starts as circles, and Links as lines.
/// Click a node or start to select it; click empty space to deselect.
/// </para>
/// </summary>
public sealed class ModelSystemCanvas : Control
{
    // ── Brushes / pens (shared, immutable) ───────────────────────────────
    private static readonly IBrush CanvasBackground   = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E));
    private static readonly IBrush NodeFill           = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50));
    private static readonly IBrush NodeBorderBrush    = new SolidColorBrush(Color.FromRgb(0x77, 0x88, 0x99));
    private static readonly IBrush NodeSelBrush       = Brushes.DodgerBlue;
    private static readonly IBrush NodeTextBrush      = Brushes.White;
    private static readonly IBrush StartFill          = new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22));
    private static readonly IBrush StartSelFill       = Brushes.DodgerBlue;
    private static readonly IBrush StartTextBrush     = Brushes.White;
    private static readonly IBrush LinkBrush          = new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D));
    private static readonly IBrush LinkSelBrush       = Brushes.OrangeRed;
    private static readonly IBrush PendingLinkBrush   = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71));
    private static readonly DashStyle PendingLinkDash = new DashStyle([6, 4], 0);

    // Parameter value row
    private static readonly IBrush ParamValueTextBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x82));
    private static readonly IBrush ParamValueBg        = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));

    // Comment block colours (sticky-note style)
    private static readonly IBrush CommentFill        = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xF0, 0x96));
    private static readonly IBrush CommentSelFill     = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xE0, 0x50));
    private static readonly IBrush CommentBorderBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0xA0, 0x00));
    private static readonly IBrush CommentSelBorder   = Brushes.DodgerBlue;
    private static readonly IBrush CommentTextBrush   = new SolidColorBrush(Color.FromRgb(0x22, 0x1E, 0x00));
    // Hook colours
    private static readonly IBrush HookConnectedBrush   = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71));
    private static readonly IBrush HookUnconnectedBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x66, 0x77));
    private static readonly IBrush HookDividerBrush     = new SolidColorBrush(Color.FromRgb(0x44, 0x55, 0x66));
    private static readonly IBrush HookTextConnBrush    = new SolidColorBrush(Color.FromRgb(0xAA, 0xEE, 0xBB));
    private static readonly IBrush HookTextDimBrush     = new SolidColorBrush(Color.FromRgb(0x77, 0x88, 0x99));
    // Hook toggle icon
    private static readonly IBrush HookToggleBg         = new SolidColorBrush(Color.FromArgb(0x60, 0x55, 0x88, 0xCC));
    private static readonly IBrush HookToggleActiveBg   = new SolidColorBrush(Color.FromArgb(0x90, 0x33, 0x99, 0xFF));
    private static readonly IBrush HookToggleText       = new SolidColorBrush(Color.FromRgb(0xBB, 0xCC, 0xEE));
    // Resize handle
    private static readonly IBrush ResizeHandleBrush    = new SolidColorBrush(Color.FromArgb(0x80, 0xAA, 0xBB, 0xCC));
    // Inline parameter hook row tint
    private static readonly IBrush InlineParamRowBg     = new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xE0, 0x80));
    // Minimize-to-inline button on BasicParameter nodes
    private static readonly IBrush MinimizeBtnBg        = new SolidColorBrush(Color.FromArgb(0x60, 0x88, 0xCC, 0x55));
    private static readonly IBrush MinimizeBtnText      = new SolidColorBrush(Color.FromRgb(0xCC, 0xFF, 0xAA));

    // ── Drawing constants ─────────────────────────────────────────────────
    private const double NodeCornerRadius    = 4.0;
    private const double NodeBorderThickness = 2.0;
    private const double LinkThickness       = 2.0;
    private const double ArrowSize           = 10.0;
    private const double NodeFontSize        = 12.0;
    private const double StartFontSize       = 11.0;
    private const double CommentFontSize     = 11.5;
    private const double CommentPadding      = 6.0;
    // Hook layout
    private const double NodeHeaderHeight   = 28.0;
    private const double HookRowHeight      = 16.0;
    private const double HookDotRadius      = 3.5;
    private const double HookFontSize       = 10.0;
    private const double NodeMinWidth       = 120.0;
    // Hook toggle icon button in the node header top-right
    private const double HookToggleIconSize = NodeHeaderHeight - 8.0;
    // Resize handle: square target area at node bottom-right corner
    private const double ResizeHandleSize         = 14.0;
    // Minimize-to-inline button on BasicParameter node header top-left
    private const double InlineMinimizeButtonSize = NodeHeaderHeight - 8.0;
    // Elbow routing
    private const double ElbowMinOffset   = 32.0;
    private const double LinkHitTolerance = 6.0;

    private static readonly Typeface DefaultTypeface = new Typeface("Segoe UI, Arial, sans-serif");

    // ── ViewModel ─────────────────────────────────────────────────────────
    private ModelSystemEditorViewModel? _vm;

    // ── Per-frame hook anchor cache (rebuilt in BuildHookAnchorCache) ─────
    private readonly Dictionary<(NodeViewModel, NodeHook), Point>
        _hookAnchors = new();
    private readonly Dictionary<NodeViewModel, IReadOnlyList<NodeHook>>
        _nodeVisibleHooks = new();
    private readonly Dictionary<NodeViewModel, HashSet<NodeHook>>
        _nodeConnectedHooks = new();

    // ── Inline parameter editor ───────────────────────────────────────────
    /// <summary>Overlay TextBox used for in-canvas parameter value editing.</summary>
    private readonly TextBox _inlineEditor;
    /// <summary>The node whose parameter value row is currently being edited, or <c>null</c> when idle.</summary>
    private NodeViewModel? _editingParamNode;
    /// <summary>Screen position and width of the inline editor overlay (set in <see cref="BeginParamEdit"/>).</summary>
    private double _editingParamEditorX, _editingParamEditorY, _editingParamEditorW;

    // ── Inlined BasicParameter caches (rebuilt by BuildHookAnchorCache) ───
    /// <summary>
    /// Maps (origin node, hook) → the BasicParameter node that is currently inlined
    /// into that hook row (node location is <see cref="Rectangle.Hidden"/>).
    /// </summary>
    private readonly Dictionary<(NodeViewModel, NodeHook), NodeViewModel>
        _hookInlinedParam = new();
    /// <summary>
    /// BasicParameter nodes that are visible on the canvas AND connected via a Single hook,
    /// so they can offer a "minimize to inline" button.
    /// </summary>
    private readonly HashSet<NodeViewModel> _canInlineNodes = new();

    public ModelSystemCanvas()
    {
        Focusable = true;

        // Build the inline editor once; it lives as a visual child of this canvas.
        _inlineEditor = new TextBox
        {
            FontFamily        = new Avalonia.Media.FontFamily("Segoe UI, Arial, sans-serif"),
            FontSize          = HookFontSize,
            Foreground        = ParamValueTextBrush,
            Background        = new SolidColorBrush(Color.FromRgb(0x18, 0x28, 0x38)),
            BorderThickness   = new Thickness(1),
            BorderBrush       = new SolidColorBrush(Color.FromRgb(0x44, 0x88, 0xCC)),
            Padding           = new Thickness(4, 0, 4, 0),
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            IsVisible         = false,
        };
        _inlineEditor.KeyDown   += OnInlineEditorKeyDown;
        _inlineEditor.LostFocus += OnInlineEditorLostFocus;

        LogicalChildren.Add(_inlineEditor);
        VisualChildren.Add(_inlineEditor);
    }

    // ── Drag state ────────────────────────────────────────────────────────
    /// <summary>The element currently being dragged (left-button), or <c>null</c> when idle.</summary>
    private ICanvasElement? _dragging;
    /// <summary>Offset from the element's top-left corner to the pointer position at drag start.</summary>
    private Point _dragOffset;

    // ── Resize drag state ─────────────────────────────────────────────────
    /// <summary>The node being resized, or <c>null</c> when not resizing.</summary>
    private ICanvasElement? _resizing;
    /// <summary>Pointer position at the start of the resize drag.</summary>
    private Point _resizeStartPos;
    /// <summary>Node rendered width at the start of the resize drag.</summary>
    private double _resizeStartW;
    /// <summary>Node rendered height at the start of the resize drag.</summary>
    private double _resizeStartH;

    // ── Link-creation drag state (right-button) ────────────────────────────
    /// <summary>The origin element for a pending link, or <c>null</c> when not drawing.</summary>
    private ICanvasElement? _linkOrigin;
    /// <summary>Current cursor position while drawing a pending link.</summary>
    private Point _linkCurrentPos;

    // ── Right-click context-menu tracking ────────────────────────────────
    /// <summary>Set when a right-button press is outstanding, so release can compare displacement.</summary>
    private bool            _rightClickPending;
    /// <summary>Canvas position where the right button was pressed.</summary>
    private Point           _rightClickPressPos;
    /// <summary>Canvas element (node / start / comment) under the right-button press, if any.</summary>
    private ICanvasElement? _rightClickElement;
    /// <summary>Link under the right-button press when no element was hit.</summary>
    private LinkViewModel?  _rightClickLink;
    /// <summary>Hook dot under the right-button press, if any (may be set alongside <see cref="_rightClickElement"/>).</summary>
    private (NodeViewModel Node, NodeHook Hook)? _rightClickHookHit;

    // ── DataContext wiring ────────────────────────────────────────────────
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Detach();
        _vm = DataContext as ModelSystemEditorViewModel;
        Attach();
        InvalidateAndMeasure();
    }

    private void Attach()
    {
        if (_vm is null) return;
        _vm.Nodes.CollectionChanged        += OnCollectionChanged;
        _vm.Starts.CollectionChanged       += OnCollectionChanged;
        _vm.Links.CollectionChanged        += OnCollectionChanged;
        _vm.CommentBlocks.CollectionChanged += OnCollectionChanged;
        _vm.PropertyChanged                += OnViewModelPropertyChanged;

        foreach (var n in _vm.Nodes)         ((INotifyPropertyChanged)n).PropertyChanged += OnElementPropertyChanged;
        foreach (var s in _vm.Starts)        ((INotifyPropertyChanged)s).PropertyChanged += OnElementPropertyChanged;
        foreach (var l in _vm.Links)         ((INotifyPropertyChanged)l).PropertyChanged += OnElementPropertyChanged;
        foreach (var c in _vm.CommentBlocks) ((INotifyPropertyChanged)c).PropertyChanged += OnElementPropertyChanged;
    }

    private void Detach()
    {
        if (_vm is null) return;
        _vm.Nodes.CollectionChanged        -= OnCollectionChanged;
        _vm.Starts.CollectionChanged       -= OnCollectionChanged;
        _vm.Links.CollectionChanged        -= OnCollectionChanged;
        _vm.CommentBlocks.CollectionChanged -= OnCollectionChanged;
        _vm.PropertyChanged                -= OnViewModelPropertyChanged;

        foreach (var n in _vm.Nodes)         ((INotifyPropertyChanged)n).PropertyChanged -= OnElementPropertyChanged;
        foreach (var s in _vm.Starts)        ((INotifyPropertyChanged)s).PropertyChanged -= OnElementPropertyChanged;
        foreach (var l in _vm.Links)         ((INotifyPropertyChanged)l).PropertyChanged -= OnElementPropertyChanged;
        foreach (var c in _vm.CommentBlocks) ((INotifyPropertyChanged)c).PropertyChanged -= OnElementPropertyChanged;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (INotifyPropertyChanged item in e.NewItems)
                item.PropertyChanged += OnElementPropertyChanged;
        if (e.OldItems is not null)
            foreach (INotifyPropertyChanged item in e.OldItems)
                item.PropertyChanged -= OnElementPropertyChanged;

        Avalonia.Threading.Dispatcher.UIThread.Post(InvalidateAndMeasure);
    }

    private void OnElementPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(InvalidateVisual);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ModelSystemEditorViewModel.SelectedElement)
                           or nameof(ModelSystemEditorViewModel.SelectedLink)
                           or nameof(ModelSystemEditorViewModel.ShowAllHooks))
            Avalonia.Threading.Dispatcher.UIThread.Post(InvalidateAndMeasure);
    }

    private void InvalidateAndMeasure()
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    // ── Layout ────────────────────────────────────────────────────────────
    protected override Size MeasureOverride(Size availableSize)
    {
        BuildHookAnchorCache();
        double maxX = 1600, maxY = 900;
        if (_vm is not null)
        {
            foreach (var n in _vm.Nodes)
            {
                if (n.IsInlined) continue;  // hidden nodes don't contribute to canvas extents
                maxX = Math.Max(maxX, n.X + NodeRenderWidth(n)  + 80);
                maxY = Math.Max(maxY, n.Y + NodeRenderHeight(n) + 80);
            }
            foreach (var s in _vm.Starts)
            {
                maxX = Math.Max(maxX, s.X + s.Diameter + 80);
                maxY = Math.Max(maxY, s.Y + s.Diameter + 40);
            }
            foreach (var c in _vm.CommentBlocks)
            {
                maxX = Math.Max(maxX, c.X + c.Width  + 80);
                maxY = Math.Max(maxY, c.Y + c.Height + 40);
            }
        }
        // Measure the inline editor so Avalonia knows its desired size.
        if (_editingParamNode is not null)
        {
            _inlineEditor.Measure(new Size(_editingParamEditorW > 0 ? _editingParamEditorW
                                                                     : NodeRenderWidth(_editingParamNode),
                                           HookRowHeight));
        }
        return new Size(maxX, maxY);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Position the inline editor at the stored row location.
        if (_editingParamNode is not null && _editingParamEditorW > 0)
        {
            _inlineEditor.Arrange(new Rect(
                _editingParamEditorX,
                _editingParamEditorY,
                _editingParamEditorW,
                HookRowHeight));
        }
        return finalSize;
    }

    // ── Rendering ─────────────────────────────────────────────────────────
    public override void Render(DrawingContext ctx)
    {
        BuildHookAnchorCache();
        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        ctx.DrawRectangle(CanvasBackground, null, bounds);

        if (_vm is null) return;

        RenderCommentBlocks(ctx);
        RenderLinks(ctx);
        RenderNodes(ctx);
        RenderStarts(ctx);
        RenderPendingLink(ctx);
    }

    private void RenderCommentBlocks(DrawingContext ctx)
    {
        foreach (var comment in _vm!.CommentBlocks)
        {
            var rect   = new Rect(comment.X, comment.Y, comment.Width, comment.Height);
            var fill   = comment.IsSelected ? CommentSelFill   : CommentFill;
            var border = new Pen(comment.IsSelected ? CommentSelBorder : CommentBorderBrush, NodeBorderThickness, dashStyle: DashStyle.Dash);

            ctx.DrawRectangle(fill, border, rect, NodeCornerRadius, NodeCornerRadius);

            // Render wrapped comment text inside the block with clipping
            var textArea = rect.Deflate(CommentPadding);
            if (textArea.Width > 4 && textArea.Height > 4)
            {
                using var _ = ctx.PushClip(textArea);
                var layout = new TextLayout(
                    comment.Name,
                    DefaultTypeface,
                    CommentFontSize,
                    CommentTextBrush,
                    textAlignment: TextAlignment.Left,
                    textWrapping: TextWrapping.Wrap,
                    maxWidth: textArea.Width,
                    maxHeight: textArea.Height);
                layout.Draw(ctx, new Point(textArea.X, textArea.Y));
            }

            // Resize grip dots (bottom-right corner)
            {
                double dotR = 2.0;
                double bx   = comment.X + comment.Width;
                double by   = comment.Y + comment.Height;
                for (int d = 0; d < 3; d++)
                {
                    double offset = 4.0 + d * 4.0;
                    ctx.DrawEllipse(ResizeHandleBrush, null,
                        new Point(bx - offset + dotR, by - dotR), dotR, dotR);
                    ctx.DrawEllipse(ResizeHandleBrush, null,
                        new Point(bx - dotR, by - offset + dotR), dotR, dotR);
                }
            }
        }
    }

    private void RenderLinks(DrawingContext ctx)
    {
        foreach (var link in _vm!.Links)
        {
            // Don't render links whose destination is in a different boundary
            // or whose destination node is inlined (value shown in hook row instead).
            if (link.Destination is null) continue;
            if (link.Destination is NodeViewModel destNvm && destNvm.IsInlined) continue;

            var brush = link.IsSelected ? LinkSelBrush : LinkBrush;
            var pen   = new Pen(brush, LinkThickness);
            var (p1, mid1, mid2, p2) = ComputeElbow(link);
            var shaftEnd = DrawArrow(ctx, brush, mid2, p2);
            ctx.DrawLine(pen, p1,   mid1);
            ctx.DrawLine(pen, mid1, mid2);
            ctx.DrawLine(pen, mid2, shaftEnd);
        }
    }

    /// <summary>
    /// Computes the four elbow points (start, bend1, bend2, end) for a link's
    /// three-segment orthogonal routing. Uses the hook anchor cache and border-clip logic.
    /// </summary>
    private (Point p1, Point mid1, Point mid2, Point p2) ComputeElbow(LinkViewModel link)
    {
        var originCenter = new Point(link.X1, link.Y1);
        var destCenter   = new Point(link.X2, link.Y2);

        // p1: hook anchor when origin is a Node, else border-clip.
        Point p1;
        bool  hookOrigin = false;
        if (link.Origin is NodeViewModel originNvm
            && _hookAnchors.TryGetValue((originNvm, link.UnderlyingLink.OriginHook), out var hookPt))
        {
            p1         = hookPt;
            hookOrigin = true;
        }
        else
        {
            p1 = BorderPoint(link.Origin, destCenter) ?? originCenter;
        }

        // H-V-H when the link exits a hook (rightward) or horizontal span >= vertical span.
        // V-H-V otherwise.
        bool hvh = hookOrigin ||
                   Math.Abs(destCenter.X - p1.X) >= Math.Abs(destCenter.Y - p1.Y);

        Point mid1, mid2, p2;
        if (hvh)
        {
            double midX    = Math.Max(p1.X + ElbowMinOffset, (p1.X + destCenter.X) / 2.0);
            mid1           = new Point(midX, p1.Y);
            var approachPt = new Point(midX, destCenter.Y);
            p2             = BorderPoint(link.Destination, approachPt) ?? destCenter;
            mid2           = new Point(midX, p2.Y);
        }
        else
        {
            double midY    = (p1.Y + destCenter.Y) / 2.0;
            mid1           = new Point(p1.X, midY);
            var approachPt = new Point(destCenter.X, midY);
            p2             = BorderPoint(link.Destination, approachPt) ?? destCenter;
            mid2           = new Point(p2.X, midY);
        }
        return (p1, mid1, mid2, p2);
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
            if (horizontal && other >= rect.X && other <= rect.Right)  tBest = t;
            if (!horizontal && other >= rect.Y && other <= rect.Bottom) tBest = t;
        }

        if (Math.Abs(dx) > 1e-10)
        {
            TryT((rect.X       - outside.X) / dx, horizontal: false, 0);
            TryT((rect.Right   - outside.X) / dx, horizontal: false, 0);
        }
        if (Math.Abs(dy) > 1e-10)
        {
            TryT((rect.Y       - outside.Y) / dy, horizontal: true, 0);
            TryT((rect.Bottom  - outside.Y) / dy, horizontal: true, 0);
        }

        if (tBest == double.MaxValue) return center;
        return new Point(outside.X + tBest * dx, outside.Y + tBest * dy);
    }

    /// <summary>
    /// Draws a filled triangular arrowhead at <paramref name="to"/> pointing away from
    /// <paramref name="from"/>, and returns the base-centre of the triangle so the
    /// caller can terminate the shaft line there without overlapping the head.
    /// </summary>
    private static Point DrawArrow(DrawingContext ctx, IBrush brush, Point from, Point to)
    {
        var dx  = to.X - from.X;
        var dy  = to.Y - from.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return to;

        // Unit direction and perpendicular
        var ux = dx / len;
        var uy = dy / len;
        var px = -uy * (ArrowSize * 0.45);
        var py =  ux * (ArrowSize * 0.45);

        var tip   = to;
        var left  = new Point(to.X - ux * ArrowSize + px, to.Y - uy * ArrowSize + py);
        var right = new Point(to.X - ux * ArrowSize - px, to.Y - uy * ArrowSize - py);
        var shaftEnd = new Point(to.X - ux * ArrowSize, to.Y - uy * ArrowSize);

        // Filled triangle for the head
        var geo = new StreamGeometry();
        using (var gc = geo.Open())
        {
            gc.BeginFigure(tip, isFilled: true);
            gc.LineTo(left);
            gc.LineTo(right);
            gc.EndFigure(isClosed: true);
        }
        ctx.DrawGeometry(brush, null, geo);

        return shaftEnd;
    }

    private void RenderPendingLink(DrawingContext ctx)
    {
        if (_linkOrigin is null) return;
        var pen    = new Pen(PendingLinkBrush, LinkThickness, dashStyle: PendingLinkDash);
        var origin = new Point(_linkOrigin.CenterX, _linkOrigin.CenterY);
        var shaftEnd = DrawArrow(ctx, PendingLinkBrush, origin, _linkCurrentPos);
        ctx.DrawLine(pen, origin, shaftEnd);
    }

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
            // Skip inter-boundary links — they are not rendered.
            if (link.Destination is null) continue;
            // Skip links to inlined nodes — no line is drawn for them.
            if (link.Destination is NodeViewModel dlNvm && dlNvm.IsInlined) continue;

            var (p1, mid1, mid2, p2) = ComputeElbow(link);
            if (DistToSeg(pos, p1,   mid1) <= LinkHitTolerance ||
                DistToSeg(pos, mid1, mid2) <= LinkHitTolerance ||
                DistToSeg(pos, mid2, p2)   <= LinkHitTolerance)
                return link;
        }
        return null;
    }

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

    /// <summary>Minimum distance from point <paramref name="p"/> to segment AB.</summary>
    private static double DistToSeg(Point p, Point a, Point b)
    {
        var dx    = b.X - a.X;
        var dy    = b.Y - a.Y;
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
        _canInlineNodes.Clear();
        if (_vm is null) return;

        // Which hooks on each node have a live link?
        foreach (var link in _vm.Links)
        {
            if (link.Origin is NodeViewModel originVm)
            {
                if (!_nodeConnectedHooks.TryGetValue(originVm, out var set))
                    _nodeConnectedHooks[originVm] = set = new HashSet<NodeHook>();
                set.Add(link.UnderlyingLink.OriginHook);
            }
        }

        // Identify inlined BasicParameter nodes and which hook rows they occupy.
        // Also identify canvas-visible BasicParameter nodes eligible for the minimize button.
        foreach (var link in _vm.Links)
        {
            if (link.Origin is NodeViewModel originVm2
                && link.Destination is NodeViewModel destVm
                && destVm.IsParameterNode
                && link.UnderlyingLink.OriginHook.Cardinality == HookCardinality.Single)
            {
                if (destVm.IsInlined)
                    _hookInlinedParam[(originVm2, link.UnderlyingLink.OriginHook)] = destVm;
                else
                    _canInlineNodes.Add(destVm);
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
                visible = node.UnderlyingNode.Hooks
                    .Where(h =>
                        h.Cardinality == HookCardinality.Single ||
                        h.Cardinality == HookCardinality.AtLeastOne ||
                        node.ShowHooks ||
                        connected.Contains(h))
                    .ToList();
            }

            _nodeVisibleHooks[node] = visible;

            double rw = NodeRenderWidth(node);
            for (int i = 0; i < visible.Count; i++)
            {
                double ay = node.Y + NodeHeaderHeight + i * HookRowHeight + HookRowHeight / 2.0;
                _hookAnchors[(node, visible[i])] = new Point(node.X + rw, ay);
            }
        }
    }

    private static double NodeRenderWidth(NodeViewModel node) =>
        Math.Max(node.Width, NodeMinWidth);

    private double NodeRenderHeight(NodeViewModel node)
    {
        bool hasParamRow = node.IsParameterNode;
        int extraRows    = hasParamRow ? 1 : 0;
        if (_nodeVisibleHooks.TryGetValue(node, out var hooks) && hooks.Count > 0)
            return Math.Max(node.Height, NodeHeaderHeight + (hooks.Count + extraRows) * HookRowHeight);
        if (hasParamRow)
            return Math.Max(node.Height, NodeHeaderHeight + HookRowHeight);
        // No visible hooks — keep at least NodeHeaderHeight so the name always fits.
        return Math.Max(node.Height, NodeHeaderHeight);
    }

    /// <summary>Returns the rendered width of any resizable canvas element.</summary>
    private double ElementRenderWidth(ICanvasElement el) =>
        el is NodeViewModel nvm ? NodeRenderWidth(nvm)
        : el is CommentBlockViewModel cvm ? cvm.Width
        : 0;

    /// <summary>Returns the rendered height of any resizable canvas element.</summary>
    private double ElementRenderHeight(ICanvasElement el) =>
        el is NodeViewModel nvm ? NodeRenderHeight(nvm)
        : el is CommentBlockViewModel cvm ? cvm.Height
        : 0;

    private void RenderNodes(DrawingContext ctx)
    {
        foreach (var node in _vm!.Nodes)
        {
            // Inlined nodes are hidden — skip canvas rendering entirely.
            if (node.IsInlined) continue;

            double rw = NodeRenderWidth(node);
            double rh = NodeRenderHeight(node);
            var rect   = new Rect(node.X, node.Y, rw, rh);
            var border = new Pen(node.IsSelected ? NodeSelBrush : NodeBorderBrush, NodeBorderThickness);

            // Node background + border
            ctx.DrawRectangle(NodeFill, border, rect, NodeCornerRadius, NodeCornerRadius);

            // ── Header: node name centred in the header band ──────────────
            double headerBottom = node.Y + NodeHeaderHeight;
            var ft = MakeText(node.Name, NodeFontSize, NodeTextBrush);
            var tx = node.X + (rw       - ft.Width)  / 2;
            var ty = node.Y + (NodeHeaderHeight - ft.Height) / 2;
            ctx.DrawText(ft, new Point(tx, ty));

            // ── Resize handle (bottom-right corner) ───────────────────────
            // Three small diagonal dots — standard grip indicator.
            {
                double dotR = 2.0;
                double bx   = node.X + rw;
                double by   = node.Y + rh;
                for (int d = 0; d < 3; d++)
                {
                    double offset = 4.0 + d * 4.0;
                    ctx.DrawEllipse(ResizeHandleBrush, null,
                        new Point(bx - offset + dotR, by - dotR), dotR, dotR);
                    ctx.DrawEllipse(ResizeHandleBrush, null,
                        new Point(bx - dotR,           by - offset + dotR), dotR, dotR);
                }
            }

            // ── Hook toggle icon (top-right of header) ─────────────────────
            // Only shown when the node has at least one hook and there is no
            // global ShowAllHooks override (per-node toggle would be redundant).
            if (!_vm.ShowAllHooks && node.UnderlyingNode.Hooks.Count > 0)
            {
                var iconRect  = HookToggleIconRect(node, rw);
                var iconBg    = node.ShowHooks ? HookToggleActiveBg : HookToggleBg;
                ctx.DrawRectangle(iconBg, null, iconRect, 3.0, 3.0);
                var glyph     = node.ShowHooks ? "\u25BE" : "\u25B8";  // ▾ or ▸
                var iconFt    = MakeText(glyph, HookFontSize + 1.0, HookToggleText);
                var glyphX    = iconRect.X + (iconRect.Width  - iconFt.Width)  / 2.0;
                var glyphY    = iconRect.Y + (iconRect.Height - iconFt.Height) / 2.0;
                ctx.DrawText(iconFt, new Point(glyphX, glyphY));
            }

            // ── Minimize-to-inline button (top-left of header) ───────────
            // Only on BasicParameter nodes that are wired to a Single hook
            // and can therefore be folded into the parent's hook row.
            if (_canInlineNodes.Contains(node))
            {
                var minRect  = InlineMinimizeButtonRect(node);
                ctx.DrawRectangle(MinimizeBtnBg, null, minRect, 3.0, 3.0);
                var minFt    = MakeText("\u229f", HookFontSize, MinimizeBtnText);  // ⊟ minus-in-box
                var minGlX   = minRect.X + (minRect.Width  - minFt.Width)  / 2.0;
                var minGlY   = minRect.Y + (minRect.Height - minFt.Height) / 2.0;
                ctx.DrawText(minFt, new Point(minGlX, minGlY));
            }

            // ── Hook rows ─────────────────────────────────────────────────
            bool hasParamRow = node.IsParameterNode;
            bool hasHooks    = _nodeVisibleHooks.TryGetValue(node, out var hooks) && hooks.Count > 0;

            if (!hasParamRow && !hasHooks)
                continue;

            // Divider line separating header from content rows
            var dividerPen = new Pen(HookDividerBrush, 1.0);
            ctx.DrawLine(dividerPen,
                new Point(node.X + 1,      headerBottom),
                new Point(node.X + rw - 1, headerBottom));

            int rowOffset = 0;

            // ── Parameter value row (first, for parameter nodes) ──────────
            if (hasParamRow)
            {
                var paramValue = node.ParameterValueRepresentation;
                double rowMidY = node.Y + NodeHeaderHeight + HookRowHeight / 2.0;

                // Subtle tinted background for readability
                ctx.DrawRectangle(ParamValueBg, null,
                    new Rect(node.X + 1, node.Y + NodeHeaderHeight, rw - 2, HookRowHeight));

                const double textPad = 6.0;
                var display  = string.IsNullOrEmpty(paramValue) ? "(no value)" : paramValue;
                var paramFt  = MakeText(display, HookFontSize, ParamValueTextBrush);
                double maxW  = rw - textPad * 2;
                double paramTy = rowMidY - paramFt.Height / 2.0;
                using (ctx.PushClip(new Rect(node.X + textPad, paramTy, Math.Max(0, maxW), paramFt.Height + 1)))
                    ctx.DrawText(paramFt, new Point(node.X + textPad, paramTy));

                rowOffset = 1;

                // Separator below the value row when hooks follow
                if (hasHooks)
                {
                    double sepY = node.Y + NodeHeaderHeight + HookRowHeight;
                    ctx.DrawLine(new Pen(HookDividerBrush, 0.5),
                        new Point(node.X + 1,      sepY),
                        new Point(node.X + rw - 1, sepY));
                }
            }

            if (!hasHooks)
                continue;

            _nodeConnectedHooks.TryGetValue(node, out var connected);

            for (int i = 0; i < hooks!.Count; i++)
            {
                var hook  = hooks[i];
                bool conn = connected is not null && connected.Contains(hook);
                // Is this hook occupied by an inlined BasicParameter?
                bool hasInlined = _hookInlinedParam.TryGetValue((node, hook), out var inlinedParam);

                double rowMidY = node.Y + NodeHeaderHeight + (rowOffset + i) * HookRowHeight + HookRowHeight / 2.0;
                double rowTopY = node.Y + NodeHeaderHeight + (rowOffset + i) * HookRowHeight;

                // Tinted background for inlined-param hook rows
                if (hasInlined)
                    ctx.DrawRectangle(InlineParamRowBg, null,
                        new Rect(node.X + 1, rowTopY, rw - 2, HookRowHeight));

                // Dot on the right edge (the link anchor)
                // Green for connected or inlined (both mean the hook is bound).
                var dotBrush = (conn || hasInlined) ? HookConnectedBrush : HookUnconnectedBrush;
                ctx.DrawEllipse(dotBrush, null,
                    new Point(node.X + rw, rowMidY),
                    HookDotRadius, HookDotRadius);

                // Hook name + optional inlined value
                const double textPad = 6.0;
                string hookLabel = hasInlined && inlinedParam is not null
                    ? $"{hook.Name}: {(string.IsNullOrEmpty(inlinedParam.ParameterValueRepresentation) ? "(no value)" : inlinedParam.ParameterValueRepresentation)}"
                    : hook.Name;
                IBrush hookTextBrush = hasInlined     ? ParamValueTextBrush
                                     : conn           ? HookTextConnBrush
                                     :                  HookTextDimBrush;
                var hookFt   = MakeText(hookLabel, HookFontSize, hookTextBrush);
                double maxW  = rw - textPad * 2 - HookDotRadius * 2;
                double hookTy = rowMidY - hookFt.Height / 2.0;
                using (ctx.PushClip(new Rect(node.X + textPad, hookTy, Math.Max(0, maxW), hookFt.Height + 1)))
                    ctx.DrawText(hookFt, new Point(node.X + textPad, hookTy));

                // Row separator (skip after last row)
                if (i < hooks.Count - 1)
                {
                    double sepY = node.Y + NodeHeaderHeight + (rowOffset + i + 1) * HookRowHeight;
                    ctx.DrawLine(new Pen(HookDividerBrush, 0.5),
                        new Point(node.X + 1,      sepY),
                        new Point(node.X + rw - 1, sepY));
                }
            }
        }
    }

    /// <summary>
    /// Updates <see cref="_hoveredParameterNode"/> based on which node (if any) the
    /// pointer currently sits over, and invalidates the visual when the value changes.
    /// </summary>
    private void RenderStarts(DrawingContext ctx)
    {
        foreach (var start in _vm!.Starts)
        {
            var fill   = start.IsSelected ? StartSelFill : StartFill;
            var center = new Point(start.CenterX, start.CenterY);
            var r      = StartViewModel.Radius;
            var border = new Pen(start.IsSelected ? NodeSelBrush : NodeBorderBrush, NodeBorderThickness);

            ctx.DrawEllipse(fill, border, center, r, r);

            // Label below the circle
            var ft = MakeText(start.Name, StartFontSize, StartTextBrush);
            var lx = start.X + (start.Diameter - ft.Width) / 2;
            var ly = start.Y + start.Diameter + 3;
            ctx.DrawText(ft, new Point(lx, ly));
        }
    }

    private static FormattedText MakeText(string text, double size, IBrush foreground) =>
        new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            DefaultTypeface,
            size,
            foreground);

    // ── Hit testing / mouse interaction ──────────────────────────────────
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_vm is null) return;
        if (e.Key is Key.Delete or Key.Back)
        {
            _vm.DeleteSelectedCommand.Execute(null);
            e.Handled = true;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_vm is null) return;

        var point = e.GetCurrentPoint(this);
        var pos   = point.Position;
        bool isRightButton = point.Properties.IsRightButtonPressed;
        bool isCtrlLeft    = !isRightButton
                             && point.Properties.IsLeftButtonPressed
                             && (e.KeyModifiers & KeyModifiers.Control) != 0;

        // Both right-click and Ctrl+left-click begin a link-creation drag.
        bool isLinkDrag = isRightButton || isCtrlLeft;

        // ── Resize handle press (left button) ────────────────────────────
        if (!isLinkDrag)
        {
            var resizeHit = HitTestResizeHandle(pos);
            if (resizeHit is not null)
            {
                // If editing, commit before selecting/dragging anything else.
                if (_editingParamNode is not null) CommitParamEdit();
                _resizing       = resizeHit;
                _resizeStartPos = pos;
                _resizeStartW   = ElementRenderWidth(resizeHit);
                _resizeStartH   = ElementRenderHeight(resizeHit);
                _vm.SelectElementCommand.Execute(resizeHit);
                e.Pointer.Capture(this);
                Focus();
                e.Handled = true;
                return;
            }
        }

        // ── Minimize-to-inline button (BasicParameter header top-left) ───
        if (!isLinkDrag)
        {
            var minimizeHit = HitTestMinimizeButton(pos);
            if (minimizeHit is not null)
            {
                if (_editingParamNode is not null) CommitParamEdit();
                minimizeHit.InlineBasicParameter();
                InvalidateAndMeasure();
                e.Handled = true;
                return;
            }
        }

        // ── Inline parameter value edit (single left click on param row) ──
        if (!isLinkDrag)
        {
            // Regular parameter value row (node is visible on canvas).
            var paramRowHit = HitTestParamValueRow(pos);
            if (paramRowHit is not null)
            {
                _vm.SelectElementCommand.Execute(paramRowHit);
                BeginParamEdit(paramRowHit);
                e.Handled = true;
                return;
            }
            // Inlined BasicParameter hook row inside the origin node.
            var inlinedRowHit = HitTestInlinedParamRow(pos);
            if (inlinedRowHit is not null)
            {
                var (originNode, _, inlinedParam, rx, ry, rw2) = inlinedRowHit.Value;
                _vm.SelectElementCommand.Execute(originNode);
                BeginParamEdit(inlinedParam, rx, ry, rw2);
                e.Handled = true;
                return;
            }
            // Clicking elsewhere commits any open edit.
            if (_editingParamNode is not null) CommitParamEdit();
        }

        // ── Hook toggle icon click (left button, any click count) ─────────
        if (!isLinkDrag)
        {
            var toggleHit = HitTestHookToggleIcon(pos);
            if (toggleHit is not null)
            {
                toggleHit.ShowHooks = !toggleHit.ShowHooks;
                InvalidateAndMeasure();
                e.Handled = true;
                return;
            }
        }

        // ── Double-click on a hook dot: create + auto-link a new node ─────
        if (!isLinkDrag && e.ClickCount == 2)
        {
            var hookHit = HitTestHook(pos);
            if (hookHit is { } hh)
            {
                _ = _vm.CreateNodeFromHookAsync(hh.node, hh.hook, hh.anchor.X, hh.anchor.Y);
                e.Handled = true;
                return;
            }

            // ── Double-click on a parameter node: open the value editor ────
            var nodeHit = HitTest(pos, testComments: false) as NodeViewModel;
            if (nodeHit is { IsParameterNode: true })
            {
                _ = _vm.EditParameterNodeAsync(nodeHit);
                e.Handled = true;
                return;
            }
        }

        ICanvasElement? hit = HitTest(pos, testComments: !isLinkDrag);

        if (isLinkDrag)
        {
            // Track right-button press so we can detect a "no-drag" context-menu click on release.
            if (isRightButton)
            {
                _rightClickPending  = true;
                _rightClickPressPos = pos;
                _rightClickElement  = HitTest(pos, testComments: true);
                _rightClickLink     = _rightClickElement is null ? HitTestLink(pos) : null;
                // Also check whether a hook dot was right-clicked on a node.
                var hookHit = HitTestHook(pos);
                _rightClickHookHit  = hookHit.HasValue ? (hookHit.Value.node, hookHit.Value.hook) : null;
            }

            // Begin link-creation drag from a node or start.
            // Comment blocks are not valid link origins.
            if (hit is NodeViewModel or StartViewModel)
            {
                _linkOrigin     = hit;
                _linkCurrentPos = pos;
                e.Pointer.Capture(this);
                Focus();
                e.Handled = true;
            }
            return;
        }

        // ── Left button: normal select + drag ─────────────────────────────
        if (hit is not null)
        {
            _vm.SelectElementCommand.Execute(hit);
            _dragging   = hit;
            _dragOffset = new Point(pos.X - hit.X, pos.Y - hit.Y);
            e.Pointer.Capture(this);
        }
        else
        {
            // No element hit — try links.
            var linkHit = HitTestLink(pos);
            if (linkHit is not null)
                _vm.SelectLinkCommand.Execute(linkHit);
            else
                _vm.SelectElementCommand.Execute(null);
        }

        Focus();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetCurrentPoint(this).Position;

        // Right-drag: update pending link preview.
        if (_linkOrigin is not null)
        {
            _linkCurrentPos = pos;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        // ── Resize drag ───────────────────────────────────────────────────
        if (_resizing is not null)
        {
            var dw = pos.X - _resizeStartPos.X;
            var dh = pos.Y - _resizeStartPos.Y;
            if (_resizing is NodeViewModel resizingNode)
                resizingNode.ResizeTo(_resizeStartW + dw, _resizeStartH + dh);
            else if (_resizing is CommentBlockViewModel resizingComment)
                resizingComment.ResizeTo(_resizeStartW + dw, _resizeStartH + dh);
            InvalidateAndMeasure();
            e.Handled = true;
            return;
        }

        // ── Cursor feedback while idle ────────────────────────────────────
        if (_dragging is null)
        {
            Cursor = HitTestResizeHandle(pos) is not null
                ? new Cursor(StandardCursorType.SizeAll)
                : Cursor.Default;
        }

        if (_dragging is null) return;

        var newX = Math.Max(0, pos.X - _dragOffset.X);
        var newY = Math.Max(0, pos.Y - _dragOffset.Y);

        if (_dragging is NodeViewModel         nvm) nvm.MoveTo(newX, newY);
        if (_dragging is StartViewModel         svm) svm.MoveTo(newX, newY);
        if (_dragging is CommentBlockViewModel  cvm) cvm.MoveTo(newX, newY);

        InvalidateAndMeasure();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        // ── Right-click with minimal movement: show context menu ──────────
        if (_rightClickPending)
        {
            _rightClickPending = false;
            var rPos = e.GetCurrentPoint(this).Position;
            var rdx  = rPos.X - _rightClickPressPos.X;
            var rdy  = rPos.Y - _rightClickPressPos.Y;

            if (Math.Sqrt(rdx * rdx + rdy * rdy) < 3.0
                && (_rightClickElement is not null || _rightClickLink is not null))
            {
                _linkOrigin = null;
                e.Pointer.Capture(null);
                InvalidateVisual();
                ShowContextMenu(_rightClickElement, _rightClickLink);
                e.Handled = true;
                return;
            }
        }

        // ── Right-button release: complete link creation ──────────────────
        if (_linkOrigin is not null)
        {
            var origin = _linkOrigin;
            _linkOrigin = null;
            e.Pointer.Capture(null);
            InvalidateVisual();

            if (_vm is not null)
            {
                var pos  = e.GetCurrentPoint(this).Position;
                var dest = HitTest(pos, testComments: false) as NodeViewModel;
                // A start is never a valid destination; dest must be a NodeViewModel.
                if (dest is not null && !ReferenceEquals(dest, origin))
                    _ = _vm.CreateLinkAsync(origin, dest);
            }

            e.Handled = true;
            return;
        }

        // ── Left-button release: end resize drag ─────────────────────────
        if (_resizing is not null)
        {
            _resizing = null;
            e.Pointer.Capture(null);
            Cursor = Cursor.Default;
            InvalidateAndMeasure();
            e.Handled = true;
            return;
        }

        // ── Left-button release: end element drag ─────────────────────────
        if (_dragging is null) return;
        _dragging = null;
        e.Pointer.Capture(null);
        InvalidateAndMeasure();
        e.Handled = true;
    }

    /// <summary>
    /// Opens a context menu for the given canvas element or link.
    /// The element/link is selected on the VM before the menu opens so that
    /// the Delete command operates on the correct target.
    /// </summary>
    private void ShowContextMenu(ICanvasElement? element, LinkViewModel? link)
    {
        if (_vm is null || (element is null && link is null)) return;

        var vm = _vm; // capture for closure

        var menu = new ContextMenu();

        // ── Hook-specific items ───────────────────────────────────────────
        if (_rightClickHookHit is { } hookEntry)
        {
            var capturedNode = hookEntry.Node;
            var capturedHook = hookEntry.Hook;

            // "Expand parameter to its own module" when the hook has an inlined BasicParam.
            if (_hookInlinedParam.TryGetValue((capturedNode, capturedHook), out var inlinedParamNode))
            {
                var capturedParam = inlinedParamNode;
                var expandItem = new MenuItem { Header = "Expand parameter to its own module" };
                expandItem.Click += (_, _) =>
                {
                    double rw = NodeRenderWidth(capturedNode);
                    capturedParam.ExpandToCanvas(capturedNode.X + rw + 30.0, capturedNode.Y);
                };
                menu.Items.Add(expandItem);
                menu.Items.Add(new Separator());
            }

            var interBoundaryItem = new MenuItem { Header = "Link to node in another boundary…" };
            interBoundaryItem.Click += (_, _) =>
                _ = vm.CreateInterBoundaryLinkAsync(capturedNode, capturedHook);
            menu.Items.Add(interBoundaryItem);
            menu.Items.Add(new Separator());
        }

        // ── Standard Delete ───────────────────────────────────────────────
        var deleteItem = new MenuItem { Header = "Delete" };
        deleteItem.Click += (_, _) =>
        {
            if (element is not null)
                vm.SelectElementCommand.Execute(element);
            else
                vm.SelectLinkCommand.Execute(link);
            _ = vm.DeleteSelectedCommand.ExecuteAsync(null);
        };

        // ── Variable list management + inline option ────────────────────
        if (element is NodeViewModel paramNode && paramNode.IsParameterNode)
        {
            // "Inline parameter" — only when this BasicParam is wired to a Single hook.
            if (_canInlineNodes.Contains(paramNode))
            {
                var inlineItem = new MenuItem { Header = "Inline parameter into parent hook" };
                inlineItem.Click += (_, _) => paramNode.InlineBasicParameter();
                menu.Items.Add(inlineItem);
                menu.Items.Add(new Separator());
            }

            bool alreadyVar = vm.IsNodeInVariables(paramNode);
            var varHeader = alreadyVar
                ? "Remove from Model System Variables"
                : "Add to Model System Variables";
            var varItem = new MenuItem { Header = varHeader };
            varItem.Click += (_, _) =>
            {
                if (vm.IsNodeInVariables(paramNode))
                    _ = vm.RemoveNodeFromVariablesAsync(paramNode);
                else
                    _ = vm.AddNodeToVariablesAsync(paramNode);
            };
            menu.Items.Add(varItem);
            menu.Items.Add(new Separator());
        }

        menu.Items.Add(deleteItem);

        ContextMenu = menu;
        ContextMenu.Open(this);
    }

    // ── Resize handle hit-testing ─────────────────────────────────────────

    // ── Inline parameter editor ────────────────────────────────────────────────

    /// <summary>
    /// Returns the <see cref="NodeViewModel"/> whose parameter value row contains
    /// <paramref name="pos"/>, or <c>null</c> if none.
    /// </summary>
    private NodeViewModel? HitTestParamValueRow(Point pos)
    {
        if (_vm is null) return null;
        foreach (var node in _vm.Nodes)
        {
            if (!node.IsParameterNode || node.IsInlined) continue;
            double rw = NodeRenderWidth(node);
            var rowRect = new Rect(node.X, node.Y + NodeHeaderHeight, rw, HookRowHeight);
            if (rowRect.Contains(pos))
                return node;
        }
        return null;
    }

    /// <summary>Shows the inline editor over the parameter row of <paramref name="node"/>.</summary>
    /// <param name="node">The BasicParameter node whose value is being edited.</param>
    /// <param name="rowX">Override X position of the editor overlay (use -1 to auto-derive).</param>
    /// <param name="rowY">Override Y position of the editor overlay (use -1 to auto-derive).</param>
    /// <param name="rowW">Override width of the editor overlay (use -1 to auto-derive).</param>
    private void BeginParamEdit(NodeViewModel node, double rowX = -1, double rowY = -1, double rowW = -1)
    {
        _editingParamNode = node;
        _editingParamEditorX = rowX >= 0 ? rowX : node.X;
        _editingParamEditorY = rowY >= 0 ? rowY : node.Y + NodeHeaderHeight;
        _editingParamEditorW = rowW >= 0 ? rowW : NodeRenderWidth(node);
        _inlineEditor.Text  = node.ParameterValueRepresentation;
        _inlineEditor.IsVisible = true;
        // Re-layout so ArrangeOverride positions the TextBox at the right row.
        InvalidateMeasure();
        // Focus + select-all after the layout pass completes.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _inlineEditor.Focus();
            _inlineEditor.SelectAll();
        }, Avalonia.Threading.DispatcherPriority.Render);
    }

    /// <summary>Commits the current editor text as the new parameter value.</summary>
    private void CommitParamEdit()
    {
        if (_editingParamNode is null) return;
        var node  = _editingParamNode;
        var value = _inlineEditor.Text ?? string.Empty;
        // Clear first so LostFocus re-entry is guarded.
        _editingParamNode       = null;
        _inlineEditor.IsVisible = false;
        if (!node.SetParameterValue(value, out var error))
            _vm?.ShowToast(error?.Message ?? "Failed to set parameter value.",
                           isError: true, durationMs: 5000);
        InvalidateAndMeasure();
    }

    /// <summary>Discards the current edit without saving.</summary>
    private void CancelParamEdit()
    {
        _editingParamNode       = null;
        _inlineEditor.IsVisible = false;
        InvalidateAndMeasure();
        Focus();
    }

    private void OnInlineEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Return or Key.Enter)
        {
            CommitParamEdit();
            Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelParamEdit();
            e.Handled = true;
        }
    }

    private void OnInlineEditorLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Commit on focus loss (e.g. user clicks away to another element).
        if (_editingParamNode is not null)
            CommitParamEdit();
    }

    /// <summary>
    /// Returns the <see cref="NodeViewModel"/> whose resize handle (bottom-right
    /// corner square) contains <paramref name="pos"/>, or <c>null</c> if none.
    /// </summary>
    /// <summary>
    /// Returns the <see cref="ICanvasElement"/> whose resize handle (bottom-right
    /// corner square) contains <paramref name="pos"/>, or <c>null</c> if none.
    /// Checks nodes first, then comment blocks.
    /// </summary>
    private ICanvasElement? HitTestResizeHandle(Point pos)
    {
        if (_vm is null) return null;
        foreach (var node in _vm.Nodes)
        {
            double rw = NodeRenderWidth(node);
            double rh = NodeRenderHeight(node);
            var handle = new Rect(
                node.X + rw - ResizeHandleSize,
                node.Y + rh - ResizeHandleSize,
                ResizeHandleSize,
                ResizeHandleSize);
            if (handle.Contains(pos))
                return node;
        }
        foreach (var comment in _vm.CommentBlocks)
        {
            var handle = new Rect(
                comment.X + comment.Width  - ResizeHandleSize,
                comment.Y + comment.Height - ResizeHandleSize,
                ResizeHandleSize,
                ResizeHandleSize);
            if (handle.Contains(pos))
                return comment;
        }
        return null;
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
            double rw    = NodeRenderWidth(node);
            var iconRect = HookToggleIconRect(node, rw);
            if (iconRect.Contains(pos))
                return node;
        }
        return null;
    }

    /// <summary>
    /// Returns the bounding rectangle of the hook-toggle icon button for
    /// <paramref name="node"/> given its rendered width <paramref name="rw"/>.
    /// </summary>
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

    /// <summary>
    /// Returns the bounding rectangle of the "minimize to inline" button that appears
    /// in the top-left header of a <see cref="_canInlineNodes"/> BasicParameter node.
    /// </summary>
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
                return node;
        }
        return null;
    }

    /// <summary>
    /// Returns information about an inlined-param hook row that contains
    /// <paramref name="pos"/>, or <c>null</c> when no such row is hit.
    /// </summary>
    private (NodeViewModel originNode, NodeHook hook, NodeViewModel paramNode,
             double rowX, double rowY, double rowW)?
        HitTestInlinedParamRow(Point pos)
    {
        foreach (var ((originNode, hook), paramNode) in _hookInlinedParam)
        {
            if (!_nodeVisibleHooks.TryGetValue(originNode, out var hooks)) continue;

            int hookIdx = -1;
            for (int j = 0; j < hooks.Count; j++)
                if (ReferenceEquals(hooks[j], hook)) { hookIdx = j; break; }
            if (hookIdx < 0) continue;

            int rowOffset = originNode.IsParameterNode ? 1 : 0;
            double rw     = NodeRenderWidth(originNode);
            double rowTop = originNode.Y + NodeHeaderHeight + (rowOffset + hookIdx) * HookRowHeight;
            var rowRect   = new Rect(originNode.X, rowTop, rw, HookRowHeight);

            if (rowRect.Contains(pos))
                return (originNode, hook, paramNode, originNode.X, rowTop, rw);
        }
        return null;
    }

    /// <summary>Finds the topmost canvas element under <paramref name="pos"/>.</summary>
    /// <param name="testComments">When <c>false</c>, comment blocks are excluded (link creation).</param>
    private ICanvasElement? HitTest(Point pos, bool testComments)
    {
        if (_vm is null) return null;

        // Starts (highest z-order)
        foreach (var start in _vm.Starts)
        {
            var dx = pos.X - start.CenterX;
            var dy = pos.Y - start.CenterY;
            if (Math.Sqrt(dx * dx + dy * dy) <= StartViewModel.Radius)
                return start;
        }

        // Nodes
        foreach (var node in _vm.Nodes)
        {
            if (node.IsInlined) continue;  // hidden — not clickable directly
            if (new Rect(node.X, node.Y, NodeRenderWidth(node), NodeRenderHeight(node)).Contains(pos))
                return node;
        }

        // Comment blocks (background layer)
        if (testComments)
        {
            foreach (var comment in _vm.CommentBlocks)
            {
                if (new Rect(comment.X, comment.Y, comment.Width, comment.Height).Contains(pos))
                    return comment;
            }
        }

        return null;
    }
}
