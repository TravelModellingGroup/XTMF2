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
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using XTMF2.GUI.ViewModels;
using XTMF2.ModelSystemConstruct;

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
        else if (e.Key == Key.D && (e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            bool didToggle = false;
            int failed = 0;

            if (_multiSelection.Count > 1)
            {
                foreach (var element in _multiSelection)
                {
                    Node? target = element switch
                    {
                        NodeViewModel nvm => nvm.UnderlyingNode,
                        FunctionInstanceViewModel fivm => fivm.UnderlyingInstance,
                        _ => null,
                    };
                    if (target is null) continue;

                    didToggle = true;
                    bool nextDisabled = !target.IsDisabled;
                    if (_vm?.Session is not null && _vm?.User is not null)
                    {
                        if (!_vm.Session.SetNodeDisabled(_vm.User, target, nextDisabled, out var err))
                        {
                            failed++;
                            _vm.ShowToast(err?.Message ?? "Unable to change disabled state.",
                                isError: true, durationMs: 6000);
                        }
                    }
                }
            }
            else
            {
                Node? target = _vm?.SelectedElement switch
                {
                    NodeViewModel nvm => nvm.UnderlyingNode,
                    FunctionInstanceViewModel fivm => fivm.UnderlyingInstance,
                    _ => null,
                };

                if (target is not null && _vm?.Session is not null && _vm?.User is not null)
                {
                    didToggle = true;
                    bool nextDisabled = !target.IsDisabled;
                    if (!_vm.Session.SetNodeDisabled(_vm.User, target, nextDisabled, out var err))
                    {
                        failed++;
                        _vm.ShowToast(err?.Message ?? "Unable to change disabled state.",
                            isError: true, durationMs: 6000);
                    }
                }
            }

            if (didToggle && failed == 0)
            {
                InvalidateVisual();
            }

            if (!didToggle && _vm?.SelectedLink is { } selectedLink && _vm?.Session is not null && _vm?.User is not null)
            {
                didToggle = true;
                bool nextDisabled = !selectedLink.UnderlyingLink.IsDisabled;
                if (!_vm.Session.SetLinkDisabled(_vm.User, selectedLink.UnderlyingLink, nextDisabled, out var err))
                {
                    failed++;
                    _vm.ShowToast(err?.Message ?? "Unable to change link disabled state.",
                        isError: true, durationMs: 6000);
                }
                else
                {
                    InvalidateVisual();
                }
            }

            e.Handled = didToggle;
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
        else if (e.Key == Key.Up && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift)) == 0)
        {
            // Arrow Up: navigate to nearest element above current selection.
            NavigateToNextElement(NavigationDirection.Up);
            e.Handled = true;
        }
        else if (e.Key == Key.Down && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift)) == 0)
        {
            // Arrow Down: navigate to nearest element below current selection.
            NavigateToNextElement(NavigationDirection.Down);
            e.Handled = true;
        }
        else if (e.Key == Key.Left && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift)) == 0)
        {
            // Arrow Left: navigate to nearest element to the left of current selection.
            NavigateToNextElement(NavigationDirection.Left);
            e.Handled = true;
        }
        else if (e.Key == Key.Right && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift)) == 0)
        {
            // Arrow Right: navigate to nearest element to the right of current selection.
            NavigateToNextElement(NavigationDirection.Right);
            e.Handled = true;
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

            if (hit is not null)
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
                // Truly empty space — begin canvas pan without changing selection.
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
            _resizing.ResizeToPreview(_resizeStartW + dw, _resizeStartH + dh);
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
            Cursor = HitTestResizeHandle(mpos) is not null
                ? new Cursor(StandardCursorType.SizeAll)
                : Cursor.Default;

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
                el.MoveToPreview(nx, ny);
            }
        }
        else
        {
            // Single-element drag: preview only, no session command issued yet.
            var newX = Math.Max(0, mpos.X - _dragOffset.X);
            var newY = Math.Max(0, mpos.Y - _dragOffset.Y);
            _dragging.MoveToPreview(newX, newY);
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

        // ── Rubber-band selection rectangle release ──────────────────────
        if (_selRectStart is not null)
        {
            var start = _selRectStart.Value;
            var end = _selRectCurrent;
            _selRectStart = null;
            _selRectCurrent = default;

            var finalRect = new Rect(
                Math.Min(start.X, end.X),
                Math.Min(start.Y, end.Y),
                Math.Abs(end.X - start.X),
                Math.Abs(end.Y - start.Y));

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
                foreach (var startVm in _vm.Starts)
                {
                    var sr = new Rect(startVm.X, startVm.Y, startVm.Diameter, startVm.Diameter);
                    if (finalRect.Intersects(sr))
                    {
                        _multiSelection.Add(startVm);
                        startVm.IsSelected = true;
                        firstHit ??= startVm;
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
                        nodeMoves.Add((gnvm.UnderlyingNode, r.Value));
                }
                else if (el is StartViewModel gsvm)
                {
                    var r = gsvm.TakePendingMoveRect();
                    if (r.HasValue)
                        nodeMoves.Add((gsvm.UnderlyingStart, r.Value));
                }
                else if (el is CommentBlockViewModel gcvm)
                {
                    var r = gcvm.TakePendingMoveRect();
                    if (r.HasValue)
                        commentMoves.Add((gcvm.UnderlyingBlock, r.Value));
                }
                else if (el is GhostNodeViewModel ggvm)
                {
                    var r = ggvm.TakePendingMoveRect();
                    if (r.HasValue)
                        nodeMoves.Add((ggvm.UnderlyingGhostNode, r.Value));
                }
                else if (el is FunctionTemplateViewModel gftvm)
                {
                    var r = gftvm.TakePendingMoveRect();
                    if (r.HasValue)
                        templateMoves.Add((gftvm.UnderlyingTemplate, r.Value));
                }
                else if (el is FunctionInstanceViewModel gfivm)
                {
                    var r = gfivm.TakePendingMoveRect();
                    if (r.HasValue)
                        instanceMoves.Add((gfivm.UnderlyingInstance, r.Value));
                }
                else if (el is FunctionParameterViewModel gfpvm)
                {
                    var r = gfpvm.TakePendingMoveRect();
                    if (r.HasValue)
                        nodeMoves.Add((gfpvm.UnderlyingParameter, r.Value));
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

        EndPointerInteraction(e.Pointer);
        InvalidateAndMeasure();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndPointerInteraction(e.Pointer);
    }

    private void EndPointerInteraction(IPointer? pointer)
    {
        _dragging = null;
        _resizing = null;
        _panning = false;
        _rightClickPending = false;
        _selRectStart = null;
        _selRectCurrent = default;
        _linkOrigin = null;
        _linkCurrentPos = default;
        _autoScrollTimer.Stop();
        pointer?.Capture(null);
    }

    // ── Keyboard navigation (arrow keys) ──────────────────────────────────
    /// <summary>Enumeration for navigation directions used in arrow key navigation.</summary>
    private enum NavigationDirection
    {
        Up,
        Down,
        Left,
        Right
    }

    /// <summary>
    /// Navigates to the next canvas element in the specified direction from the currently selected element.
    /// Finds the closest element that lies in the target direction and selects it.
    /// Automatically scrolls to center the element in the viewport.
    /// </summary>
    private void NavigateToNextElement(NavigationDirection direction)
    {
        if (_vm is null) return;

        // Get current selected element.
        var current = _vm.SelectedElement;
        if (current is null) 
        {
            // If nothing is selected, select the first available element.
            SelectFirstElement();
            return;
        }

        // Collect all navigable elements.
        var allElements = CollectAllCanvasElements();
        if (allElements.Count == 0) return;

        // Find the best candidate element in the given direction.
        ICanvasElement? nextElement = FindNearestElement(current, allElements, direction);

        if (nextElement is not null && nextElement != current)
        {
            // Clear multi-selection and select the next element.
            ClearMultiSelection();
            _vm.SelectElementCommand.Execute(nextElement);
            
            // Scroll to center the element in the viewport.
            CenterElementInViewport(nextElement);
            
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Collects all navigable canvas elements from the current boundary view.
    /// </summary>
    private List<ICanvasElement> CollectAllCanvasElements()
    {
        if (_vm is null) return new List<ICanvasElement>();

        var elements = new List<ICanvasElement>();

        // Collect all types of canvas elements visible on the current boundary.
        elements.AddRange(_vm.Starts);
        elements.AddRange(_vm.Nodes.Where(n => !n.IsInlined));
        elements.AddRange(_vm.CommentBlocks);
        elements.AddRange(_vm.GhostNodes);
        elements.AddRange(_vm.FunctionTemplates);
        elements.AddRange(_vm.FunctionInstances);
        elements.AddRange(_vm.FunctionParameterVMs);

        return elements;
    }

    /// <summary>
    /// Finds the nearest element to <paramref name="current"/> in the specified <paramref name="direction"/>.
    /// Elements are scored based on their position relative to the current element.
    /// </summary>
    private ICanvasElement? FindNearestElement(ICanvasElement current, List<ICanvasElement> candidates, NavigationDirection direction)
    {
        double currentX = current.CenterX;
        double currentY = current.CenterY;

        ICanvasElement? nearest = null;
        double nearestScore = double.MaxValue;

        foreach (var candidate in candidates)
        {
            // Skip the current element.
            if (candidate == current) continue;

            double dx = candidate.CenterX - currentX;
            double dy = candidate.CenterY - currentY;
            double distance = Math.Sqrt(dx * dx + dy * dy);

            // Score is based on the distance and whether the element is in the target direction.
            double score = CalculateNavigationScore(dx, dy, distance, direction);

            // Lower score is better. Only consider elements with a positive score
            // (i.e., in the target direction).
            if (score >= 0 && score < nearestScore)
            {
                nearestScore = score;
                nearest = candidate;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Calculates a navigation score for an element relative to the current position and direction.
    /// Returns a negative score if the element is not in the target direction, or a positive score
    /// indicating the distance-weighted angle deviation if the element is in the target direction.
    /// Lower positive scores indicate better matches.
    /// </summary>
    private double CalculateNavigationScore(double dx, double dy, double distance, NavigationDirection direction)
    {
        // Minimum distance threshold: don't skip very close elements.
        const double MinDistance = 10.0;
        if (distance < MinDistance) return double.MaxValue;

        // Calculate angle from current element to candidate (in degrees, 0° = right, 90° = down, etc.).
        double angle = Math.Atan2(dy, dx) * 180 / Math.PI;
        // Normalize to 0-360 range.
        if (angle < 0) angle += 360;

        // Define acceptable angle ranges for each direction (with a 45° tolerance).
        // The score penalizes both angular deviation and distance.
        double angleDeviation = 0;
        bool isInDirection = false;

        switch (direction)
        {
            case NavigationDirection.Right:  // 0° (±45°)
                if (angle <= 45 || angle >= 315)
                {
                    isInDirection = true;
                    angleDeviation = angle <= 45 ? angle : angle - 360;
                }
                break;

            case NavigationDirection.Down:   // 90° (±45°)
                if (angle >= 45 && angle <= 135)
                {
                    isInDirection = true;
                    angleDeviation = Math.Abs(angle - 90);
                }
                break;

            case NavigationDirection.Left:   // 180° (±45°)
                if (angle >= 135 && angle <= 225)
                {
                    isInDirection = true;
                    angleDeviation = Math.Abs(angle - 180);
                }
                break;

            case NavigationDirection.Up:     // 270° (±45°)
                if (angle >= 225 && angle <= 315)
                {
                    isInDirection = true;
                    angleDeviation = Math.Abs(angle - 270);
                }
                break;
        }

        if (!isInDirection) return -1; // Not in target direction.

        // Combine angle deviation and distance for the final score.
        // Prioritize elements more aligned with the direction (lower angle deviation).
        double score = angleDeviation + (distance * 0.1);
        return score;
    }

    /// <summary>
    /// Selects the first available canvas element when no element is currently selected.
    /// Prioritizes by: Starts → Nodes → CommentBlocks → GhostNodes → FunctionTemplates → FunctionInstances → FunctionParameters.
    /// Automatically scrolls to center the element in the viewport.
    /// </summary>
    private void SelectFirstElement()
    {
        if (_vm is null) return;

        ICanvasElement? firstElement = null;

        // Check each collection in priority order
        if (_vm.Starts.Count > 0) firstElement = _vm.Starts[0];
        else if (_vm.Nodes.Any(n => !n.IsInlined)) firstElement = _vm.Nodes.First(n => !n.IsInlined);
        else if (_vm.CommentBlocks.Count > 0) firstElement = _vm.CommentBlocks[0];
        else if (_vm.GhostNodes.Count > 0) firstElement = _vm.GhostNodes[0];
        else if (_vm.FunctionTemplates.Count > 0) firstElement = _vm.FunctionTemplates[0];
        else if (_vm.FunctionInstances.Count > 0) firstElement = _vm.FunctionInstances[0];
        else if (_vm.FunctionParameterVMs.Count > 0) firstElement = _vm.FunctionParameterVMs[0];

        if (firstElement is not null)
        {
            _vm.SelectElementCommand.Execute(firstElement);
            
            // Scroll to center the element in the viewport.
            CenterElementInViewport(firstElement);
            
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Scrolls the canvas to center the given element in the current viewport.
    /// Calculates the required scroll offset to place the element's center at the viewport center.
    /// </summary>
    private void CenterElementInViewport(ICanvasElement element)
    {
        var sv = GetScrollViewer();
        if (sv is null) return;

        // Get the element's center in canvas/model coordinates.
        double elementCenterX = element.CenterX;
        double elementCenterY = element.CenterY;

        // Get the current viewport dimensions in screen coordinates.
        double viewportWidth = sv.Viewport.Width;
        double viewportHeight = sv.Viewport.Height;

        // The scroll offset is in screen coordinates (device pixels).
        // To center the element at the viewport center:
        // ScrollOffset = (ElementCenterInScreenCoords) - (ViewportCenter)
        // Where ElementCenterInScreenCoords = ElementCenterX * _scale
        
        double desiredScrollX = (elementCenterX * _scale) - (viewportWidth / 2.0);
        double desiredScrollY = (elementCenterY * _scale) - (viewportHeight / 2.0);

        // Clamp to valid range (minimum 0). ScrollViewer will clamp to max extent.
        desiredScrollX = Math.Max(0, desiredScrollX);
        desiredScrollY = Math.Max(0, desiredScrollY);

        // Apply the new scroll offset.
        sv.Offset = new Vector(desiredScrollX, desiredScrollY);
    }
}
