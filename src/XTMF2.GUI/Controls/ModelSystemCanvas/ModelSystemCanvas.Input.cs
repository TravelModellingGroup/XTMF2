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
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.VisualTree;
using XTMF2.Editing;
using XTMF2.GUI.ViewModels;
using XTMF2.ModelSystemConstruct;
using System.Collections.ObjectModel;

namespace XTMF2.GUI.Controls;

partial class ModelSystemCanvas
{
    // ── Hit testing / mouse interaction ──────────────────────────────────
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_vm is null) 
        {
            return;
        }
        else if (e.Key is Key.Delete or Key.Back)
        {
            if (_multiSelection.Count > 1)
            {
                // Snapshot the set before clearing so deletions don't mutate it mid-loop.
                var toDelete = _multiSelection.ToList();
                ClearMultiSelection();
                _ = _vm.DeleteMultipleAsync(toDelete);
            }
            else
            {
                _vm.DeleteSelectedCommand.Execute(null);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.D0 && (e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            ApplyScale(1.0);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ClearMultiSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.F2 && _vm?.SelectedElement is not null)
        {
            var sel = _vm.SelectedElement;
            if (sel is NodeViewModel or StartViewModel
                     or FunctionTemplateViewModel or FunctionInstanceViewModel
                     or FunctionParameterViewModel)
            {
                BeginNameEdit(sel);
                e.Handled = true;
            }
            else if (sel is CommentBlockViewModel cmt)
            {
                BeginCommentEdit(cmt);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Left && (e.KeyModifiers & KeyModifiers.Alt) != 0)
        {
            // Alt+Left: navigate to parent boundary / exit function template.
            _vm?.NavigateUpCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.C && (e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            _ = CopySelectedElementsAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.V && (e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            // Paste at the centre of the current viewport.
            var sv = GetScrollViewer();
            double vx = ((sv?.Offset.X ?? 0) + (sv?.Viewport.Width  ?? Bounds.Width)  / 2.0) / _scale;
            double vy = ((sv?.Offset.Y ?? 0) + (sv?.Viewport.Height ?? Bounds.Height) / 2.0) / _scale;
            _ = PasteElementsAsync(vx, vy);
            e.Handled = true;
        }
        else if (e.Key == Key.M
                 && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift))
                    == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            // Ctrl+Shift+M: extract selected nodes to a Function Template.
            IReadOnlyList<ICanvasElement> toExtract = _multiSelection.Count > 1
                ? _multiSelection.ToList()
                : _vm?.SelectedElement is { } sel
                    ? new[] { sel }
                    : System.Array.Empty<ICanvasElement>();
            if (toExtract.Count > 0 && _vm is not null)
            {
                _ = _vm.ExtractSelectionToFunctionTemplateAsync(toExtract);
                e.Handled = true;
            }
        }
    }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if ((e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            // Pass the canvas-local mouse position so the zoom is centred on the cursor.
            var pivot = e.GetPosition(this);
            ApplyScale(_scale + (e.Delta.Y > 0 ? ScaleStep : -ScaleStep), pivot);
            e.Handled = true;
            return;
        }
        base.OnPointerWheelChanged(e);
    }

    /// <summary>
    /// Tunneling pointer-press handler — runs before any child TextBox overlay
    /// can consume the event. If the press lands on a resize handle, the active
    /// inline editor is committed and resizing begins immediately, regardless of
    /// which overlay control is visually on top.
    /// </summary>
    private void OnPointerPressedTunnel(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is null) return;
        var point = e.GetCurrentPoint(this);
        // Only intercept plain left-button presses (not right-button link-drags,
        // not Ctrl+left multi-selection).
        if (!point.Properties.IsLeftButtonPressed) return;
        if ((e.KeyModifiers & KeyModifiers.Control) != 0) return;

        var mpos = ToCanvasPos(point.Position);
        var resizeHit = HitTestResizeHandle(mpos);
        if (resizeHit is null) return;

        // Commit any open editor so its LostFocus handler doesn't fire after
        // we capture the pointer, which would interfere with the resize drag.
        if (_editingParamNode is not null) CommitParamEdit();
        if (_editingCommentBlock is not null) CommitCommentEdit();
        if (_editingNameElement is not null) CommitNameEdit();

        ClearMultiSelection();
        _resizing = resizeHit;
        _resizeStartPos = mpos;
        _resizeStartW = ElementRenderWidth(resizeHit);
        _resizeStartH = ElementRenderHeight(resizeHit);
        _vm.SelectElementCommand.Execute(resizeHit);
        e.Pointer.Capture(this);
        Focus();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_vm is null) return;

        // ── Mouse back button (XButton1): navigate to parent scope ───────────────
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.XButton1Pressed)
        {
            _vm.NavigateUpCommand.Execute(null);
            e.Handled = true;
            return;
        }

        var point = e.GetCurrentPoint(this);
        var pos = point.Position;           // screen coords
        var mpos = ToCanvasPos(pos);         // model coords
        bool isRightButton = point.Properties.IsRightButtonPressed;
        bool isCtrlLeft = !isRightButton
                             && point.Properties.IsLeftButtonPressed
                             && (e.KeyModifiers & KeyModifiers.Control) != 0;

        // Right-click begins a link-creation drag.  Ctrl+left-click is reserved for multi-selection.
        bool isLinkDrag = isRightButton;

        // ── Resize handle press (left button) ────────────────────────────
        if (!isLinkDrag && !isCtrlLeft)
        {
            var resizeHit = HitTestResizeHandle(mpos);
            if (resizeHit is not null)
            {
                if (_editingParamNode is not null) CommitParamEdit();
                if (_editingCommentBlock is not null) CommitCommentEdit();
                if (_editingNameElement is not null) CommitNameEdit();
                ClearMultiSelection();
                _resizing = resizeHit;
                _resizeStartPos = mpos;
                _resizeStartW = ElementRenderWidth(resizeHit);
                _resizeStartH = ElementRenderHeight(resizeHit);
                _vm.SelectElementCommand.Execute(resizeHit);
                e.Pointer.Capture(this);
                Focus();
                e.Handled = true;
                return;
            }
        }

        // ── Minimize-to-inline button (BasicParameter header top-left) ───
        if (!isLinkDrag && !isCtrlLeft)
        {
            var minimizeHit = HitTestMinimizeButton(mpos);
            if (minimizeHit is not null)
            {
                if (_editingParamNode is not null) CommitParamEdit();
                if (_editingCommentBlock is not null) CommitCommentEdit();
                if (_editingNameElement is not null) CommitNameEdit();
                minimizeHit.InlineBasicParameter();
                InvalidateAndMeasure();
                e.Handled = true;
                return;
            }
        }

        // ── Inline parameter value edit (single left click on param row) ──
        if (!isLinkDrag && !isCtrlLeft)
        {
            // Regular parameter value row (node is visible on canvas).
            var paramRowHit = HitTestParamValueRow(mpos);
            if (paramRowHit is not null)
            {
                _vm.SelectElementCommand.Execute(paramRowHit);
                BeginParamEdit(paramRowHit);
                e.Handled = true;
                return;
            }
            // Inlined BasicParameter hook row inside the origin node or FunctionInstance.
            var inlinedRowHit = HitTestInlinedParamRow(mpos);
            if (inlinedRowHit is not null)
            {
                var (originEl, _, inlinedParam, rx, ry, rw2) = inlinedRowHit.Value;
                if (originEl is not null)
                    _vm.SelectElementCommand.Execute(originEl);
                BeginParamEdit(inlinedParam, rx, ry, rw2);
                e.Handled = true;
                return;
            }
            // Clicking elsewhere commits any open edit.
            // Guard: if the click was already handled by a child (e.g. the variable
            // autocomplete dropdown's TextBlock items), do not commit the edit.
            if (!e.Handled)
            {
                if (_editingParamNode is not null) CommitParamEdit();
                if (_editingCommentBlock is not null) CommitCommentEdit();
                if (_editingNameElement is not null) CommitNameEdit();
            }
        }

        // ── Hook toggle icon click (left button, any click count) ─────────
        if (!isLinkDrag && !isCtrlLeft)
        {
            var toggleHit = HitTestHookToggleIcon(mpos);
            if (toggleHit is not null)
            {
                toggleHit.ShowHooks = !toggleHit.ShowHooks;
                InvalidateAndMeasure();
                e.Handled = true;
                return;
            }
        }

        // ── Double-click on a hook dot: create + auto-link a new node ─────
        if (!isLinkDrag && !isCtrlLeft && e.ClickCount == 2)
        {
            var hookHit = HitTestHook(mpos);
            if (hookHit is { } hh)
            {
                // If the hook already has a MultiLink, open the reorder dialog instead of
                // creating a new auto-linked node if ctrl is down.
                var hookMultiLink = _vm.Links
                    .Select(lvm => lvm.UnderlyingLink)
                    .OfType<MultiLink>()
                    .FirstOrDefault(ml => ml.OriginHook == hh.hook
                                       && ml.Origin == hh.node.UnderlyingNode);
                if (hookMultiLink is not null && (e.KeyModifiers & KeyModifiers.Control) != 0)
                    _ = _vm.ReorderLinkDestinationsAsync(hookMultiLink);
                else
                    _ = _vm.CreateNodeFromHookAsync(hh.node, hh.hook, hh.anchor.X, hh.anchor.Y);
                e.Handled = true;
                return;
            }

            // ── Double-click on a parameter node: open the value editor ────
            var nodeHit = HitTest(mpos, testComments: false) as NodeViewModel;
            if (nodeHit is { IsParameterNode: true })
            {
                _ = _vm.EditParameterNodeAsync(nodeHit);
                e.Handled = true;
                return;
            }
            // ── Double-click on a regular node: begin inline rename ────────
            if (nodeHit is not null)
            {
                _vm.SelectElementCommand.Execute(nodeHit);
                BeginNameEdit(nodeHit);
                e.Handled = true;
                return;
            }
            // ── Double-click on a start: begin inline rename ──────────────
            var startHit = HitTest(mpos, testComments: false) as StartViewModel;
            if (startHit is not null)
            {
                _vm.SelectElementCommand.Execute(startHit);
                BeginNameEdit(startHit);
                e.Handled = true;
                return;
            }

            // ── Double-click on a comment block: open the inline comment editor ──
            var commentHit = HitTest(mpos, testComments: true) as CommentBlockViewModel;
            if (commentHit is not null)
            {
                _vm.SelectElementCommand.Execute(commentHit);
                BeginCommentEdit(commentHit);
                e.Handled = true;
                return;
            }

            // ── Double-click on a function template: navigate into it ─────
            var ftHit = HitTest(mpos, testComments: false) as FunctionTemplateViewModel;
            if (ftHit is not null)
            {
                _vm.NavigateIntoFunctionTemplate(ftHit);
                e.Handled = true;
                return;
            }

            // ── Double-click on a function instance: begin inline rename ──
            var fiHit = HitTest(mpos, testComments: false) as FunctionInstanceViewModel;
            if (fiHit is not null)
            {
                _vm.SelectElementCommand.Execute(fiHit);
                BeginNameEdit(fiHit);
                e.Handled = true;
                return;
            }

            // ── Double-click on a function parameter: begin inline rename ──
            var fpHit = HitTest(mpos, testComments: false) as FunctionParameterViewModel;
            if (fpHit is not null)
            {
                _vm.SelectElementCommand.Execute(fpHit);
                BeginNameEdit(fpHit);
                e.Handled = true;
                return;
            }

            // ── Double-click on a MultiLink line: open destination-order dialog ──
            var dblLinkHit = HitTestLink(mpos);
            if (dblLinkHit?.UnderlyingLink is MultiLink dblMultiLink)
            {
                _ = _vm.ReorderLinkDestinationsAsync(dblMultiLink);
                e.Handled = true;
                return;
            }
        }

        // For right-click (link creation) we exclude comment blocks; for all other paths we include them.
        ICanvasElement? hit = HitTest(mpos, testComments: !isRightButton);

        if (isLinkDrag)
        {
            // Track right-button press so we can detect a "no-drag" context-menu click on release.
            if (isRightButton)
            {
                _rightClickPending = true;
                _rightClickPressPos = pos;                              // screen coords for distance threshold
                _rightClickElement = HitTest(mpos, testComments: true);
                _rightClickLink = _rightClickElement is null ? HitTestLink(mpos) : null;
                // Also check whether a hook dot was right-clicked on a node.
                var hookHit = HitTestHook(mpos);
                _rightClickHookHit = hookHit.HasValue ? (hookHit.Value.node, hookHit.Value.hook) : null;
                var fiHookHit = HitTestFiHook(mpos);
                _rightClickFiHookHit = fiHookHit;
            }

            // Begin link-creation drag from a node, start, or function instance (via its FunctionParameterHooks).
            // Comment blocks are not valid link origins.
            if (hit is NodeViewModel or StartViewModel
                || (hit is FunctionInstanceViewModel hitFi && hitFi.FunctionParameters.Count > 0))
            {
                _linkOrigin = hit;
                _linkCurrentPos = mpos;
                e.Pointer.Capture(this);
                Focus();
                e.Handled = true;
            }
            return;
        }

        // ── Ctrl+left-click: multi-selection or rubber-band rectangle ─────
        if (isCtrlLeft)
        {
            // Always commit any open inline edit first.
            if (_editingParamNode is not null) CommitParamEdit();
            if (_editingCommentBlock is not null) CommitCommentEdit();
            if (_editingNameElement is not null) CommitNameEdit();

            if (hit is NodeViewModel or CommentBlockViewModel or GhostNodeViewModel or FunctionTemplateViewModel or FunctionInstanceViewModel or FunctionParameterViewModel)
            {
                // On the very first Ctrl+click, absorb the existing primary selection into the set.
                if (_multiSelection.Count == 0 && _vm.SelectedElement is not null
                    && !ReferenceEquals(_vm.SelectedElement, hit))
                {
                    _multiSelection.Add(_vm.SelectedElement);
                    // _vm.SelectedElement.IsSelected is already true — no change needed
                }

                if (_multiSelection.Contains(hit))
                {
                    // Toggle off: remove from multi-selection.
                    _multiSelection.Remove(hit);
                    hit.IsSelected = false;
                    // If the deselected element was the primary, pick the next available.
                    if (ReferenceEquals(_vm.SelectedElement, hit))
                        _vm.SelectedElement = _multiSelection.FirstOrDefault();
                }
                else
                {
                    // Toggle on: add to multi-selection.
                    _multiSelection.Add(hit);
                    hit.IsSelected = true;
                    // Reflect the most recently touched element in the property panel.
                    _vm.SelectedElement = hit;
                }
                InvalidateVisual();
            }
            else
            {
                // Ctrl+drag on empty space → begin a rubber-band selection rectangle.
                ClearMultiSelection();
                _vm.SelectElementCommand.Execute(null);
                _selRectStart = mpos;
                _selRectCurrent = mpos;
                e.Pointer.Capture(this);
            }

            Focus();
            e.Handled = true;
            return;
        }

        // ── Left button: normal select + drag ─────────────────────────────
        if (hit is not null)
        {
            bool hitIsInMultiSel = _multiSelection.Contains(hit);
            if (!hitIsInMultiSel)
            {
                // Clicking an element that is not part of the current group resets the selection.
                ClearMultiSelection();
                _vm.SelectElementCommand.Execute(hit);
            }
            _dragging = hit;
            _dragOffset = new Point(mpos.X - hit.X, mpos.Y - hit.Y);
            _groupDragLastPos = mpos;
            e.Pointer.Capture(this);
        }
        else
        {
            // No element hit — try links.
            var linkHit = HitTestLink(mpos);
            if (linkHit is not null)
            {
                ClearMultiSelection();
                _vm.SelectLinkCommand.Execute(linkHit);
            }
            else
            {
                // Truly empty space — deselect all and begin canvas pan.
                ClearMultiSelection();
                _vm.SelectElementCommand.Execute(null);
                var sv = GetScrollViewer();
                if (sv is not null)
                {
                    _panning = true;
                    _panStartScrollPos = e.GetCurrentPoint(sv).Position;
                    _panStartOffset = sv.Offset;
                    Cursor = new Cursor(StandardCursorType.SizeAll);
                    e.Pointer.Capture(this);
                }
            }
        }

        Focus();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetCurrentPoint(this).Position;  // screen coords
        var mpos = ToCanvasPos(pos);                  // model coords

        // Capture ScrollViewer-local position now for auto-scroll use later.
        var svForScroll = GetScrollViewer();
        var svPos = svForScroll is not null ? e.GetCurrentPoint(svForScroll).Position : pos;

        // Right-drag: update pending link preview.
        if (_linkOrigin is not null)
        {
            _linkCurrentPos = mpos;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        // ── Resize drag ───────────────────────────────────────────────────
        if (_resizing is not null)
        {
            var dw = mpos.X - _resizeStartPos.X;
            var dh = mpos.Y - _resizeStartPos.Y;
            if (_resizing is NodeViewModel resizingNode)
            {
                resizingNode.ResizeToPreview(_resizeStartW + dw, _resizeStartH + dh);
            }
            else if (_resizing is CommentBlockViewModel resizingComment)
            {
                resizingComment.ResizeToPreview(_resizeStartW + dw, _resizeStartH + dh);
            }
            else if (_resizing is GhostNodeViewModel resizingGhost)
            {
                resizingGhost.ResizeToPreview(_resizeStartW + dw, _resizeStartH + dh);
            }
            else if (_resizing is FunctionTemplateViewModel resizingFt)
            {
                resizingFt.ResizeToPreview(_resizeStartW + dw, _resizeStartH + dh);
            }
            else if (_resizing is FunctionInstanceViewModel resizingFi)
            {
                resizingFi.ResizeToPreview(_resizeStartW + dw, _resizeStartH + dh);
            }
            else if (_resizing is FunctionParameterViewModel resizingFp)
            {
                resizingFp.ResizeToPreview(_resizeStartW + dw, _resizeStartH + dh);
            }
            InvalidateAndMeasure();
            e.Handled = true;
            return;
        }

        // ── Canvas pan drag ───────────────────────────────────────────────
        if (_panning)
        {
            var sv = GetScrollViewer();
            if (sv is not null)
            {
                var currentScrollPos = e.GetCurrentPoint(sv).Position;
                var dx = currentScrollPos.X - _panStartScrollPos.X;
                var dy = currentScrollPos.Y - _panStartScrollPos.Y;
                sv.Offset = new Vector(
                    Math.Max(0, _panStartOffset.X - dx),
                    Math.Max(0, _panStartOffset.Y - dy));
            }
            e.Handled = true;
            return;
        }

        // ── Rubber-band selection rectangle (Ctrl+drag on empty space) ────
        if (_selRectStart is not null)
        {
            _selRectCurrent = mpos;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        // ── Cursor feedback while idle ────────────────────────────────────
        if (_dragging is null)
        {
            Cursor = HitTestResizeHandle(mpos) is not null
                ? new Cursor(StandardCursorType.SizeAll)
                : Cursor.Default;
        }

        if (_dragging is null) return;

        // ── Element drag (single or group) ────────────────────────────────
        if (_multiSelection.Count > 1 && _multiSelection.Contains(_dragging))
        {
            // Group drag: preview every element in the multi-selection by the per-frame delta.
            var dx = mpos.X - _groupDragLastPos.X;
            var dy = mpos.Y - _groupDragLastPos.Y;
            _groupDragLastPos = mpos;
            foreach (var el in _multiSelection)
            {
                double nx = Math.Max(0, el.X + dx);
                double ny = Math.Max(0, el.Y + dy);
                if (el is NodeViewModel gnvm) gnvm.MoveToPreview(nx, ny);
                else if (el is StartViewModel gsvm) gsvm.MoveToPreview(nx, ny);
                else if (el is CommentBlockViewModel gcvm) gcvm.MoveToPreview(nx, ny);
                else if (el is GhostNodeViewModel ggvm) ggvm.MoveToPreview(nx, ny);
                else if (el is FunctionTemplateViewModel gftvm) gftvm.MoveToPreview(nx, ny);
                else if (el is FunctionInstanceViewModel gfivm) gfivm.MoveToPreview(nx, ny);
                else if (el is FunctionParameterViewModel gfpvm) gfpvm.MoveToPreview(nx, ny);
            }
        }
        else
        {
            // Single-element drag: preview only, no session command issued yet.
            var newX = Math.Max(0, mpos.X - _dragOffset.X);
            var newY = Math.Max(0, mpos.Y - _dragOffset.Y);
            if (_dragging is NodeViewModel nvm) nvm.MoveToPreview(newX, newY);
            if (_dragging is StartViewModel svm) svm.MoveToPreview(newX, newY);
            if (_dragging is CommentBlockViewModel cvm) cvm.MoveToPreview(newX, newY);
            if (_dragging is GhostNodeViewModel gvm) gvm.MoveToPreview(newX, newY);
            if (_dragging is FunctionTemplateViewModel ftvm) ftvm.MoveToPreview(newX, newY);
            if (_dragging is FunctionInstanceViewModel fivm) fivm.MoveToPreview(newX, newY);
            if (_dragging is FunctionParameterViewModel fpvm2) fpvm2.MoveToPreview(newX, newY);
        }

        InvalidateAndMeasure();
        TryAutoScrollForDrag(svPos);
        e.Handled = true;
    }

    /// <summary>
    /// Suppress Avalonia's automatic context-menu opening on right-click release.
    /// The canvas shows the context menu manually in <see cref="OnPointerReleased"/>
    /// only when the pointer has moved less than the drag threshold, so we must
    /// prevent the framework from opening it independently.
    /// </summary>
    private void SuppressContextRequested(object? sender, ContextRequestedEventArgs e)
        => e.Handled = true;

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        // ── Right-click with minimal movement: show context menu ──────────
        if (_rightClickPending)
        {
            _rightClickPending = false;
            var rPos = e.GetCurrentPoint(this).Position;
            var rdx = rPos.X - _rightClickPressPos.X;
            var rdy = rPos.Y - _rightClickPressPos.Y;

            if (Math.Sqrt(rdx * rdx + rdy * rdy) < 3.0)
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
                var pos = e.GetCurrentPoint(this).Position;
                var hit = HitTest(ToCanvasPos(pos), testComments: false);
                // Accepts NodeViewModel or FunctionInstanceViewModel as a link destination.
                if (hit is NodeViewModel dest && !ReferenceEquals(dest, origin))
                    _ = _vm.CreateLinkAsync(origin, dest);
                else if (hit is FunctionInstanceViewModel fiDest
                    && !ReferenceEquals(fiDest, origin)
                    && fiDest.UnderlyingInstance.Template.EntryNode is not null)
                    _ = _vm.CreateLinkAsync(origin, fiDest);
                else if (hit is FunctionParameterViewModel fpDest
                    && !ReferenceEquals(fpDest, origin))
                    _ = _vm.CreateLinkAsync(origin, fpDest);
            }

            e.Handled = true;
            return;
        }

        // ── Left-button release: end resize drag ─────────────────────────
        if (_resizing is not null)
        {
            // Commit the final size to the session (single undo entry).
            _resizing.CommitResize();
            _resizing = null;
            e.Pointer.Capture(null);
            Cursor = Cursor.Default;
            InvalidateAndMeasure();
            e.Handled = true;
            return;
        }

        // ── Left-button release: end canvas pan ──────────────────────────
        if (_panning)
        {
            _panning = false;
            e.Pointer.Capture(null);
            Cursor = Cursor.Default;
            e.Handled = true;
            return;
        }

        // ── Left-button release: finalize rubber-band selection ───────────
        if (_selRectStart is not null)
        {
            var finalRect = NormalizeRect(_selRectStart.Value, _selRectCurrent);
            _selRectStart = null;
            e.Pointer.Capture(null);

            if (_vm is not null && (finalRect.Width > 2 || finalRect.Height > 2))
            {
                ICanvasElement? firstHit = null;
                foreach (var node in _vm.Nodes)
                {
                    if (node.IsInlined) continue;
                    var nr = new Rect(node.X, node.Y, NodeRenderWidth(node), NodeRenderHeight(node));
                    if (finalRect.Intersects(nr))
                    {
                        _multiSelection.Add(node);
                        node.IsSelected = true;
                        firstHit ??= node;
                    }
                }
                foreach (var comment in _vm.CommentBlocks)
                {
                    var cr = new Rect(comment.X, comment.Y, comment.Width, comment.Height);
                    if (finalRect.Intersects(cr))
                    {
                        _multiSelection.Add(comment);
                        comment.IsSelected = true;
                        firstHit ??= comment;
                    }
                }
                foreach (var ghost in _vm.GhostNodes)
                {
                    var gr = new Rect(ghost.X, ghost.Y, ghost.Width, ghost.Height);
                    if (finalRect.Intersects(gr))
                    {
                        _multiSelection.Add(ghost);
                        ghost.IsSelected = true;
                        firstHit ??= ghost;
                    }
                }
                foreach (var start in _vm.Starts)
                {
                    var sr = new Rect(start.X, start.Y, start.Diameter, start.Diameter);
                    if (finalRect.Intersects(sr))
                    {
                        _multiSelection.Add(start);
                        start.IsSelected = true;
                        firstHit ??= start;
                    }
                }
                foreach (var ft in _vm.FunctionTemplates)
                {
                    var ftr = new Rect(ft.X, ft.Y, ft.Width, ft.Height);
                    if (finalRect.Intersects(ftr))
                    {
                        _multiSelection.Add(ft);
                        ft.IsSelected = true;
                        firstHit ??= ft;
                    }
                }
                foreach (var fi in _vm.FunctionInstances)
                {
                    var fir = new Rect(fi.X, fi.Y, fi.Width, fi.Height);
                    if (finalRect.Intersects(fir))
                    {
                        _multiSelection.Add(fi);
                        fi.IsSelected = true;
                        firstHit ??= fi;
                    }
                }
                foreach (var fp in _vm.FunctionParameterVMs)
                {
                    var fpr = new Rect(fp.X, fp.Y, fp.Width, fp.Height);
                    if (finalRect.Intersects(fpr))
                    {
                        _multiSelection.Add(fp);
                        fp.IsSelected = true;
                        firstHit ??= fp;
                    }
                }
                if (firstHit is not null)
                    _vm.SelectedElement = firstHit;
            }

            InvalidateVisual();
            e.Handled = true;
            return;
        }

        // ── Left-button release: end element drag ─────────────────────────
        if (_dragging is null) return;
        if (_vm is null) return;

        // Commit the preview position as a single session command (one undo entry).
        if (_multiSelection.Count > 1 && _multiSelection.Contains(_dragging))
        {
            // Collect the pending move rectangles from all selected elements and push them
            // as one CommandBatch so the entire group drag is undone with a single Ctrl+Z.
            var nodeMoves = new List<(Node, Rectangle)>();
            var commentMoves = new List<(CommentBlock, Rectangle)>();
            var templateMoves = new List<(FunctionTemplate, Rectangle)>();
            var instanceMoves = new List<(FunctionInstance, Rectangle)>();

            foreach (var el in _multiSelection)
            {
                if (el is NodeViewModel gnvm)
                {
                    var r = gnvm.TakePendingMoveRect();
                    if (r.HasValue) 
                    {
                        nodeMoves.Add((gnvm.UnderlyingNode, r.Value));
                    }
                }
                else if (el is StartViewModel gsvm)
                {
                    var r = gsvm.TakePendingMoveRect();
                    if (r.HasValue) 
                    {
                        nodeMoves.Add((gsvm.UnderlyingStart, r.Value));
                    }
                }
                else if (el is CommentBlockViewModel gcvm)
                {
                    var r = gcvm.TakePendingMoveRect();
                    if (r.HasValue) 
                    {
                        commentMoves.Add((gcvm.UnderlyingBlock, r.Value));
                    }
                }
                else if (el is GhostNodeViewModel ggvm)
                {
                    var r = ggvm.TakePendingMoveRect();
                    if (r.HasValue)
                    {
                        nodeMoves.Add((ggvm.UnderlyingGhostNode, r.Value));
                    } 
                }
                else if (el is FunctionTemplateViewModel gftvm)
                {
                    var r = gftvm.TakePendingMoveRect();
                    if (r.HasValue) 
                    {
                        templateMoves.Add((gftvm.UnderlyingTemplate, r.Value));
                    }
                }
                else if (el is FunctionInstanceViewModel gfivm)
                {
                    var r = gfivm.TakePendingMoveRect();
                    if (r.HasValue) 
                    {
                        instanceMoves.Add((gfivm.UnderlyingInstance, r.Value));
                    }
                }
                else if (el is FunctionParameterViewModel gfpvm)
                {
                    var r = gfpvm.TakePendingMoveRect();
                    if (r.HasValue) 
                    {
                        nodeMoves.Add((gfpvm.UnderlyingParameter, r.Value));
                    }
                }
            }

            _vm.Session.MoveElements(
                _vm.User,
                nodeMoves.Count > 0 ? nodeMoves : null,
                commentMoves.Count > 0 ? commentMoves : null,
                templateMoves.Count > 0 ? templateMoves : null,
                instanceMoves.Count > 0 ? instanceMoves : null,
                out _);
        }
        else
        {
            _dragging?.CommitMove();
        }

        _dragging = null;
        _autoScrollTimer.Stop();
        e.Pointer.Capture(null);
        InvalidateAndMeasure();
        e.Handled = true;
    }

    /// <summary>
    /// Opens a context menu for the given canvas element or link.
    /// The element/link is selected on the VM before the menu opens so that
    /// the Delete command operates on the correct target.
    /// </summary>
}
