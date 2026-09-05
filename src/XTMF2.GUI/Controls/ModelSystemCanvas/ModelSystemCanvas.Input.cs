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

    private bool AltCommendIssued = false;

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (AltCommendIssued && (e.Key == Key.LeftAlt || e.Key == Key.RightAlt))
        {
            AltCommendIssued = false;
            Focus();
            e.Handled = true;
        }
        base.OnKeyUp(e);
    }

    // ── Hit testing / mouse interaction ──────────────────────────────────
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_vm is null)
        {
            base.OnKeyDown(e);
            return;
        }
        else if (e.Key is Key.Delete or Key.Back)
        {
            if (_multiLinkSelection.Count > 1)
            {
                var linksToDelete = _multiLinkSelection.ToList();
                ClearMultiSelection();
                _ = _vm.DeleteMultipleLinksAsync(linksToDelete);
            }
            else if (_multiSelection.Count > 1)
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
        else if (TryHandleAddShortcut(e))
        {
            e.Handled = true;
        }
        else if ((e.Key is Key.Return or Key.Enter) && (e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            if (_vm.SelectedElement is FunctionTemplateViewModel ftvm)
            {
                _vm.NavigateIntoFunctionTemplate(ftvm);
                e.Handled = true;
            }
            else if (_vm.SelectedElement is FunctionInstanceViewModel fivm)
            {
                _vm.OpenFunctionTemplateOfInstance(fivm);
                e.Handled = true;
            }
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
        else if (e.Key == Key.Up && (e.KeyModifiers & KeyModifiers.Alt) != 0)
        {
            // Alt+Up: navigate to parent boundary / exit function template.
            AltCommendIssued = true;
            _vm?.NavigateUpCommand.Execute(null);
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Focus();
            }, Avalonia.Threading.DispatcherPriority.Render);
            e.Handled = true;
        }
        else if (e.Key == Key.C && (e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            _ = CopySelectedElementsAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.D && (e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            bool toggledModules = TryToggleSelectedModulesDisabled();
            bool toggledLinks = TryToggleSelectedLinkDisabled();
            bool didToggle = toggledModules || toggledLinks;
            if (didToggle) InvalidateVisual();

            e.Handled = didToggle;
        }
        else if (e.Key == Key.V && (e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            if (_lastCanvasMousePos is { } mousePos)
            {
                _ = PasteElementsAsync(mousePos.X, mousePos.Y);
            }
            else
            {
                // Fallback: paste at the centre of the current viewport.
                var sv = GetScrollViewer();
                double vx = ((sv?.Offset.X ?? 0) + (sv?.Viewport.Width ?? Bounds.Width) / 2.0) / _scale;
                double vy = ((sv?.Offset.Y ?? 0) + (sv?.Viewport.Height ?? Bounds.Height) / 2.0) / _scale;
                _ = PasteElementsAsync(vx, vy);
            }
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
        else if ((e.Key is Key.O) && (e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            if (_vm?.SelectedElement is NodeViewModel nvm
                && (nvm.IsParameterNode || nvm.UnderlyingNode.Type == typeof(XTMF2.RuntimeModules.OpenReadStreamFromFile)))
            {
                _ = TryOpenOpenReadStreamFromFileParameterAsync();
                e.Handled = true;
            }
        }
        else if( e.Key == Key.F && (e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            if (_vm?.SelectedElement is NodeViewModel nvm
                && (nvm.IsParameterNode || nvm.UnderlyingNode.Type == typeof(XTMF2.RuntimeModules.OpenReadStreamFromFile)))
            {
                _ = TryUpdateOpenReadStreamFromFileParameterAsync(false);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Up
            && !IsParameterOrCommentEditing
            && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift)) == 0)
        {
            // Arrow Up: navigate to nearest element above current selection.
            NavigateToNextElement(NavigationDirection.Up);
            e.Handled = true;
        }
        else if (e.Key == Key.Down
            && !IsParameterOrCommentEditing
            && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift)) == 0)
        {
            // Arrow Down: navigate to nearest element below current selection.
            NavigateToNextElement(NavigationDirection.Down);
            e.Handled = true;
        }
        else if (e.Key == Key.Left
            && !IsParameterOrCommentEditing
            && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift)) == 0)
        {
            // Arrow Left: navigate to nearest element to the left of current selection.
            NavigateToNextElement(NavigationDirection.Left);
            e.Handled = true;
        }
        else if (e.Key == Key.Right
            && !IsParameterOrCommentEditing
            && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift)) == 0)
        {
            // Arrow Right: navigate to nearest element to the right of current selection.
            NavigateToNextElement(NavigationDirection.Right);
            e.Handled = true;
        }
        else if (e.Key == Key.Tab && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt)) == 0)
        {
            if ((e.KeyModifiers & KeyModifiers.Shift) == 0
                && _vm?.SelectedElement is FunctionParameterViewModel functionParameter)
            {
                BeginDescriptionEdit(functionParameter);
                e.Handled = true;
                return;
            }

            // For comment blocks, Tab toggles header/body editing.
            bool commentTabContext = _editingCommentBlock is not null
                                  || _editingCommentHeaderBlock is not null
                                  || _vm?.SelectedElement is CommentBlockViewModel;
            if (commentTabContext && TryCycleCommentEditOnTab())
            {
                e.Handled = true;
                return;
            }

            // Otherwise, Tab or Shift+Tab: navigate between parameters within a node or function instance.
            bool isShiftTab = (e.KeyModifiers & KeyModifiers.Shift) != 0;
            if (NavigateToNextParameter(isShiftTab))
            {
                e.Handled = true;
            }
            else if (_vm?.SelectedElement is not null)
            {
                // Keep focus on the canvas when an element is selected even if
                // there is no editable target for this Tab key press.
                e.Handled = true;
            }
        }
        base.OnKeyDown(e);
    }

    private bool IsParameterOrCommentEditing =>
        _editingParamNode is not null
        || _editingDescriptionParameter is not null
        || _editingNameElement is not null
        || _editingCommentBlock is not null
        || _editingCommentHeaderBlock is not null;

    private bool TryHandleAddShortcut(KeyEventArgs e)
    {
        if (_vm is null
            || _editingParamNode is not null
            || _editingNameElement is not null
            || _editingCommentBlock is not null
            || _editingCommentHeaderBlock is not null
            || (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift)) != KeyModifiers.Control)
        {
            return false;
        }

        var spawnPt = GetKeyboardSpawnPoint();
        switch (e.Key)
        {
            case Key.M:
                _ = _vm.AddModuleAtAsync(spawnPt.X, spawnPt.Y);
                return true;
            case Key.I:
                _ = _vm.AddFunctionInstanceAtAsync(spawnPt.X, spawnPt.Y);
                return true;
            case Key.T:
                _ = _vm.AddFunctionTemplateAtAsync(spawnPt.X, spawnPt.Y);
                return true;
            case Key.N:
                _vm.AddCommentBlockAt(spawnPt.X, spawnPt.Y);
                return true;
            default:
                return false;
        }
    }

    private Point GetKeyboardSpawnPoint()
    {
        var sv = GetScrollViewer();
        double viewportWidth = sv?.Viewport.Width ?? Bounds.Width;
        double viewportHeight = sv?.Viewport.Height ?? Bounds.Height;
        double x = ((sv?.Offset.X ?? 0) + viewportWidth / 2.0) / _scale;
        double y = ((sv?.Offset.Y ?? 0) + viewportHeight / 2.0) / _scale;
        return new Point(x, y);
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
        // If the pointer is over a comment block, scroll its comment body text.
        if (_vm is not null)
        {
            var mpos = ToCanvasPos(e.GetPosition(this));
            var commentHit = HitTest(mpos, testComments: true) as CommentBlockViewModel;
            if (commentHit is not null)
            {
                const double scrollStep = 24.0;
                double newOffset = commentHit.CommentScrollOffset - e.Delta.Y * scrollStep;
                // Clamp to [0, maxScroll] using the last-rendered max (0 if not yet rendered).
                _commentMaxScrollOffsets.TryGetValue(commentHit, out double maxScroll);
                commentHit.CommentScrollOffset = Math.Min(Math.Max(0.0, newOffset), maxScroll);
                InvalidateVisual();
                e.Handled = true;
                return;
            }
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

        // Don't commit open editors if we're resizing the element being edited.
        // The _inDragOrResize flag will prevent LostFocus handlers from closing them,
        // and SyncEditingElementPositions() will keep them synchronized during the resize.
        bool isResizingEditedElement =
            ReferenceEquals(resizeHit, _editingParamNode) ||
            ReferenceEquals(resizeHit, _editingNameElement) ||
            ReferenceEquals(resizeHit, _editingCommentBlock) ||
            ReferenceEquals(resizeHit, _editingCommentHeaderBlock);
        
        if (!isResizingEditedElement)
        {
            if (_editingParamNode is not null) CommitParamEdit();
            if (_editingCommentBlock is not null) CommitCommentEdit();
            if (_editingCommentHeaderBlock is not null) CommitCommentHeaderEdit();
            if (_editingNameElement is not null) CommitNameEdit();
        }

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
        _lastCanvasMousePos = mpos;
        bool isRightButton = point.Properties.IsRightButtonPressed;
        bool isCtrlLeft = !isRightButton
                             && point.Properties.IsLeftButtonPressed
                             && (e.KeyModifiers & KeyModifiers.Control) != 0;

        // Right-click begins a link-creation drag.  Ctrl+left-click is reserved for multi-selection.
        bool isLinkDrag = isRightButton;

        if (!isLinkDrag && !isCtrlLeft && point.Properties.IsLeftButtonPressed)
        {
            var breakpointHit = HitTestOrthogonalBreakpoint(mpos);
            if (breakpointHit is not null)
            {
                bool preserveMultiLinkSelection = _multiLinkSelection.Count > 1
                    && _multiLinkSelection.Contains(breakpointHit.UnderlyingLink);
                var selectedLinks = preserveMultiLinkSelection
                    ? _multiLinkSelection.ToList()
                    : new List<XTMF2.Link> { breakpointHit.UnderlyingLink };
                if (!preserveMultiLinkSelection)
                    ClearMultiSelection();
                _vm.SelectLinkCommand.Execute(breakpointHit);
                if (preserveMultiLinkSelection)
                    RefreshMultiLinkSelectionVisuals();
                _orthogonalBreakpointDragLink = breakpointHit;
                _orthogonalBreakpointDragLinks.Clear();
                foreach (var selectedLink in selectedLinks.Where(link => link.IsOrthogonal))
                    _orthogonalBreakpointDragLinks.Add(selectedLink);
                _orthogonalBreakpointPreviewX = ComputeOrthogonalPath(breakpointHit,
                    GetSharedSpineX(breakpointHit))[1].X;
                e.Pointer.Capture(this);
                Focus();
                e.Handled = true;
                return;
            }
        }

        // ── Resize handle press (left button) ────────────────────────────
        if (!isLinkDrag && !isCtrlLeft)
        {
            var resizeHit = HitTestResizeHandle(mpos);
            if (resizeHit is not null)
            {
                // Don't commit editors if we're resizing the element being edited.
                bool isResizingEditedElement =
                    ReferenceEquals(resizeHit, _editingParamNode) ||
                    ReferenceEquals(resizeHit, _editingNameElement) ||
                    ReferenceEquals(resizeHit, _editingCommentBlock) ||
                    ReferenceEquals(resizeHit, _editingCommentHeaderBlock);
                
                if (!isResizingEditedElement)
                {
                    if (_editingParamNode is not null) CommitParamEdit();
                    if (_editingCommentBlock is not null) CommitCommentEdit();
                    if (_editingCommentHeaderBlock is not null) CommitCommentHeaderEdit();
                    if (_editingNameElement is not null) CommitNameEdit();
                }
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
                if (_editingCommentHeaderBlock is not null) CommitCommentHeaderEdit();
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
            var functionParameterHit = HitTest(mpos, testComments: false) as FunctionParameterViewModel;
            if (functionParameterHit is not null
                && mpos.Y >= functionParameterHit.Y + FtHeaderHeight + FpTypeRowHeight
                && mpos.Y < functionParameterHit.Y + FtHeaderHeight + FpTypeRowHeight + FpDescriptionRowHeight)
            {
                _vm.SelectElementCommand.Execute(functionParameterHit);
                BeginDescriptionEdit(functionParameterHit);
                e.Handled = true;
                return;
            }

            // Regular parameter value row (node is visible on canvas).
            var ghostParamRowHit = HitTestGhostParamValueRow(mpos);
            if (ghostParamRowHit is { } ghostParam)
            {
                _vm.SelectElementCommand.Execute(ghostParam.ghost);
                BeginParamEdit(ghostParam.node, ghostParam.ghost.X,
                    ghostParam.ghost.Y + NodeHeaderHeight, ghostParam.ghost.Width,
                    ghostParam.ghost, SelfParameterNavigationKey);
                e.Handled = true;
                return;
            }
            var paramRowHit = HitTestParamValueRow(mpos);
            if (paramRowHit is not null)
            {
                _vm.SelectElementCommand.Execute(paramRowHit);
                BeginParamEdit(paramRowHit, parentElement: paramRowHit, hook: SelfParameterNavigationKey);
                e.Handled = true;
                return;
            }
            // Inlined BasicParameter hook row inside the origin node or FunctionInstance.
            var inlinedRowHit = HitTestInlinedParamRow(mpos);
            if (inlinedRowHit is not null)
            {
                var (originEl, hook, inlinedParam, rx, ry, rw2) = inlinedRowHit.Value;
                if (originEl is not null)
                    _vm.SelectElementCommand.Execute(originEl);
                BeginParamEdit(inlinedParam, rx, ry, rw2, originEl, hook);
                e.Handled = true;
                return;
            }
            // Clicking elsewhere commits any open edit.
            // Guard: if the click was already handled by a child (e.g. the variable
            // autocomplete dropdown's TextBlock items), do not commit the edit.
            // Also: if we're about to drag one of the edited elements, keep the editor open.
            if (!e.Handled)
            {
                var clickedElement = HitTest(mpos, testComments: false);
                bool isDraggingEditedElement = 
                    ReferenceEquals(clickedElement, _editingParamNode) ||
                    ReferenceEquals(clickedElement, _editingNameElement) ||
                    ReferenceEquals(clickedElement, _editingCommentBlock) ||
                    ReferenceEquals(clickedElement, _editingCommentHeaderBlock) ||
                    (clickedElement is not null && _multiSelection.Contains(clickedElement) &&
                     ((_editingParamNode is not null && _multiSelection.Contains(_editingParamNode)) || 
                      (_editingNameElement is not null && _multiSelection.Contains(_editingNameElement)) ||
                      (_editingCommentBlock is not null && _multiSelection.Contains(_editingCommentBlock))));
                
                // Only commit if we're not about to drag one of the edited elements.
                if (!isDraggingEditedElement)
                {
                    if (_editingParamNode is not null) CommitParamEdit();
                    if (_editingCommentBlock is not null) CommitCommentEdit();
                    if (_editingCommentHeaderBlock is not null) CommitCommentHeaderEdit();
                    if (_editingNameElement is not null) CommitNameEdit();
                }
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
            if (HitTest(mpos, testComments: false) is GhostNodeViewModel ghostHit)
            {
                if (ghostHit.IsParameterNode)
                    _ = _vm.EditParameterNodeAsync(new NodeViewModel(ghostHit.ReferencedNode, _vm.Session, _vm.User));
                else
                    BeginNameEdit(ghostHit);
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

            // ── Double-click on a comment block: open the inline comment or header editor ──
            var commentHit = HitTest(mpos, testComments: true) as CommentBlockViewModel;
            if (commentHit is not null)
            {
                _vm.SelectElementCommand.Execute(commentHit);
                // Open the header editor when the click lands in the header band; otherwise the comment editor.
                if (mpos.Y < commentHit.Y + CommentHeaderHeight)
                    BeginCommentHeaderEdit(commentHit);
                else
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
            if (hit is NodeViewModel or StartViewModel or GhostNodeViewModel
                || (hit is FunctionInstanceViewModel hitFi && hitFi.FunctionParameters.Count > 0)
                || hit is FunctionParameterViewModel)
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
            if (_editingCommentHeaderBlock is not null) CommitCommentHeaderEdit();
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
                var linkHit = HitTestLink(mpos);
                if (linkHit is not null)
                {
                    if (_multiLinkSelection.Count == 0
                        && _vm.SelectedLink is { } existingLink
                        && existingLink.UnderlyingLink != linkHit.UnderlyingLink)
                    {
                        _multiLinkSelection.Add(existingLink.UnderlyingLink);
                    }

                    if (_multiLinkSelection.Contains(linkHit.UnderlyingLink))
                    {
                        _multiLinkSelection.Remove(linkHit.UnderlyingLink);
                    }
                    else
                    {
                        _multiLinkSelection.Add(linkHit.UnderlyingLink);
                    }

                    LinkViewModel? primary = _vm.Links
                        .FirstOrDefault(l => _multiLinkSelection.Contains(l.UnderlyingLink));
                    _vm.SelectLinkCommand.Execute(primary);
                    RefreshMultiElementSelectionVisuals();
                    RefreshMultiLinkSelectionVisuals();
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
        _lastCanvasMousePos = mpos;
        UpdateHookTooltip(mpos);

        if (_orthogonalBreakpointDragLink is not null)
        {
            _orthogonalBreakpointPreviewX = mpos.X;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        // Capture ScrollViewer-local position now for auto-scroll use later.
        var svForScroll = GetScrollViewer();
        var svPos = svForScroll is not null ? e.GetCurrentPoint(svForScroll).Position : pos;

        // Right-drag: update pending link preview.
        if (_linkOrigin is not null)
        {
            _linkCurrentPos = mpos;
            TryAutoScrollForDrag(svPos);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        // ── Resize drag ───────────────────────────────────────────────────
        if (_resizing is not null)
        {
            _inDragOrResize = true;
            var dw = mpos.X - _resizeStartPos.X;
            var dh = mpos.Y - _resizeStartPos.Y;
            _resizing.ResizeToPreview(_resizeStartW + dw, _resizeStartH + dh);
            // Only sync inline editor if it's the element being resized
            if (ReferenceEquals(_resizing, _editingParamNode) || 
                ReferenceEquals(_resizing, _editingNameElement) || 
                ReferenceEquals(_resizing, _editingCommentBlock) ||
                ReferenceEquals(_resizing, _editingCommentHeaderBlock))
            {
                SyncEditingElementPositions();
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
            TryAutoScrollForDrag(svPos);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        // ── Cursor feedback while idle ────────────────────────────────────
        if (_dragging is null)
        {
            if (HitTestOrthogonalBreakpoint(mpos) is not null)
            {
                Cursor = new Cursor(StandardCursorType.SizeWestEast);
            }
            else if (HitTestResizeHandle(mpos) is not null)
            {
                Cursor = new Cursor(StandardCursorType.SizeAll);
            }
            else
            {
                var functionParameterHit = HitTest(mpos, testComments: false) as FunctionParameterViewModel;
                bool overDescription = functionParameterHit is not null
                    && mpos.Y >= functionParameterHit.Y + FtHeaderHeight + FpTypeRowHeight
                    && mpos.Y < functionParameterHit.Y + FtHeaderHeight
                        + FpTypeRowHeight + FpDescriptionRowHeight;
                bool overEditableParameter = overDescription
                    || HitTestParamValueRow(mpos) is not null
                    || HitTestInlinedParamRow(mpos) is not null;
                Cursor = overEditableParameter
                    ? new Cursor(StandardCursorType.Ibeam)
                    : Cursor.Default;
            }
        }

        if (_dragging is null) return;

        // ── Element drag (single or group) ────────────────────────────────
        _inDragOrResize = true;
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

        // Only sync inline editor if it's being dragged as part of the current drag operation
        if (_multiSelection.Count > 1)
        {
            if ((_editingParamNode is not null && _multiSelection.Contains(_editingParamNode)) || 
                (_editingNameElement is not null && _multiSelection.Contains(_editingNameElement)) || 
                (_editingCommentBlock is not null && _multiSelection.Contains(_editingCommentBlock)) ||
                (_editingCommentHeaderBlock is not null && _multiSelection.Contains(_editingCommentHeaderBlock)))
            {
                SyncEditingElementPositions();
            }
        }
        else if (ReferenceEquals(_dragging, _editingParamNode) || 
                 ReferenceEquals(_dragging, _editingNameElement) || 
                 ReferenceEquals(_dragging, _editingCommentBlock) ||
                 ReferenceEquals(_dragging, _editingCommentHeaderBlock))
        {
            SyncEditingElementPositions();
        }
        InvalidateAndMeasure();
        TryAutoScrollForDrag(svPos);
        e.Handled = true;
    }

    private void UpdateHookTooltip(Point canvasPosition)
    {
        var hookDescription = HitTestHook(canvasPosition)?.hook.Description;
        if (hookDescription is null)
        {
            var functionInstanceHook = HitTestFiHook(canvasPosition)?.hook;
            hookDescription = functionInstanceHook?.Parameter.Description;
        }
        if (hookDescription is null)
        {
            var ghostHook = HitTestGhostOriginHook(canvasPosition)?.hook;
            hookDescription = ghostHook is FunctionParameterHook functionParameterHook
                ? functionParameterHook.Parameter.Description
                : ghostHook?.Description;
        }
        if (hookDescription is null)
            hookDescription = HitTestFunctionTemplateHook(canvasPosition)?.Description;
        if (hookDescription is null)
        {
            hookDescription = HitTest(canvasPosition, testComments: false) switch
            {
                FunctionTemplateViewModel functionTemplate => functionTemplate.Description,
                FunctionInstanceViewModel functionInstance => functionInstance.UnderlyingInstance.Template.Description,
                _ => null
            };
        }
        ToolTip.SetTip(this, string.IsNullOrWhiteSpace(hookDescription) ? null : hookDescription);
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

        // ── Drag/resize release ──────────────────────────────────────────────
        _inDragOrResize = false;

        // ── Right-drag release: complete link creation ────────────────────
        if (_linkOrigin is not null)
        {
            var origin = _linkOrigin;
            _linkOrigin = null;
            _linkCurrentPos = default;
            e.Pointer.Capture(null);

            if (_vm is not null)
            {
                var releasePos = ToCanvasPos(e.GetCurrentPoint(this).Position);
                var destHit = HitTest(releasePos, testComments: false);
                if (origin is FunctionParameterViewModel originFp
                    && destHit is NodeViewModel targetNode
                    && !ReferenceEquals(targetNode, origin))
                    _ = _vm.CreateLinkAsync(originFp, targetNode);
                else if (origin is FunctionParameterViewModel originGhostFp
                         && destHit is GhostNodeViewModel targetGhostForFp)
                    _ = _vm.CreateLinkAsync(originGhostFp,
                        targetGhostForFp.ReferencedNodeViewModel);
                else if (destHit is NodeViewModel destNode && !ReferenceEquals(destNode, origin))
                    _ = _vm.CreateLinkAsync(origin, destNode);
                else if (destHit is FunctionInstanceViewModel destFi && !ReferenceEquals(destFi, origin))
                    _ = _vm.CreateLinkAsync(origin, destFi);
                else if (destHit is FunctionParameterViewModel destFp && !ReferenceEquals(destFp, origin))
                    _ = _vm.CreateLinkAsync(origin, destFp);
                else if (destHit is GhostNodeViewModel destGhost)
                {
                    if (destGhost.ReferencedFunctionInstanceViewModel is { } ghostFi)
                        _ = _vm.CreateLinkAsync(origin, ghostFi);
                    else
                        _ = _vm.CreateLinkAsync(origin, destGhost.ReferencedNodeViewModel);
                }
            }

            InvalidateVisual();
            e.Handled = true;
            return;
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

                if (firstHit is null)
                {
                    LinkViewModel? firstLinkHit = null;
                    foreach (var link in _vm.Links)
                    {
                        if (link.IsDestinationBranchHidden && !_vm.RenderAllHiddenDestinationLinks)
                            continue;
                        if (link.Destination is null
                            || link.Destination is NodeViewModel destinationNode && destinationNode.IsInlined)
                            continue;
                        if (!LinkIntersectsSelectionRect(link, finalRect))
                            continue;

                        _multiLinkSelection.Add(link.UnderlyingLink);
                        link.IsSelected = true;
                        firstLinkHit ??= link;
                    }

                    if (firstLinkHit is not null)
                        _vm.SelectLinkCommand.Execute(firstLinkHit);
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

        // ── Left-button release: end element resize ──────────────────────
        if (_orthogonalBreakpointDragLink is not null)
        {
            var links = _orthogonalBreakpointDragLinks.ToList();
            var breakpointX = _orthogonalBreakpointPreviewX;
            _orthogonalBreakpointDragLink = null;
            _orthogonalBreakpointDragLinks.Clear();
            e.Pointer.Capture(null);
            if (_vm is not null)
                _vm.Session.SetLinksOrthogonalBreakpointX(_vm.User, links, breakpointX, out _);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        // ── Left-button release: end element resize ──────────────────────
        if (_resizing is not null)
        {
            _resizing.CommitResize();
            _resizing = null;
            e.Pointer.Capture(null);
            InvalidateAndMeasure();
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

    private bool LinkIntersectsSelectionRect(LinkViewModel link, Rect selectionRect)
    {
        if (link.UnderlyingLink.IsOrthogonal)
        {
            _orthogonalSpineX.TryGetValue(link.UnderlyingLink, out var spineX);
            var points = ComputeOrthogonalPath(link, spineX > 0 ? spineX : (double?)null);
            for (int i = 1; i < points.Length; i++)
            {
                if (SegmentIntersectsRect(points[i - 1], points[i], selectionRect))
                    return true;
            }
            return false;
        }

        var (p1, c1, c2, p2) = ComputeSCurve(link);
        const int SelectionSamples = 16;
        var previous = p1;
        for (int sample = 1; sample <= SelectionSamples; sample++)
        {
            var next = SampleCubicBezier(p1, c1, c2, p2, sample / (double)SelectionSamples);
            if (SegmentIntersectsRect(previous, next, selectionRect))
                return true;
            previous = next;
        }
        return false;
    }

    private static bool SegmentIntersectsRect(Point start, Point end, Rect rect)
    {
        if (rect.Contains(start) || rect.Contains(end))
            return true;

        var topLeft = new Point(rect.Left, rect.Top);
        var topRight = new Point(rect.Right, rect.Top);
        var bottomRight = new Point(rect.Right, rect.Bottom);
        var bottomLeft = new Point(rect.Left, rect.Bottom);
        return SegmentsIntersect(start, end, topLeft, topRight)
            || SegmentsIntersect(start, end, topRight, bottomRight)
            || SegmentsIntersect(start, end, bottomRight, bottomLeft)
            || SegmentsIntersect(start, end, bottomLeft, topLeft);
    }

    private static bool SegmentsIntersect(Point firstStart, Point firstEnd, Point secondStart, Point secondEnd)
    {
        static double Cross(Point a, Point b, Point c) =>
            (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

        var first = Cross(firstStart, firstEnd, secondStart);
        var second = Cross(firstStart, firstEnd, secondEnd);
        var third = Cross(secondStart, secondEnd, firstStart);
        var fourth = Cross(secondStart, secondEnd, firstEnd);
        const double Epsilon = 0.000001;

        return ((first > Epsilon && second < -Epsilon) || (first < -Epsilon && second > Epsilon))
            && ((third > Epsilon && fourth < -Epsilon) || (third < -Epsilon && fourth > Epsilon));
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndPointerInteraction(e.Pointer);
        InvalidateVisual();
    }

    private void EndPointerInteraction(IPointer? pointer)
    {
        _orthogonalBreakpointDragLink = null;
        _orthogonalBreakpointDragLinks.Clear();
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

    /// <summary>Sentinel key used for direct value-row editing on parameter nodes.</summary>
    private static readonly object SelfParameterNavigationKey = new();

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
        ICanvasElement? nextElement = FindNextElement(current, allElements, direction);

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
    private ICanvasElement? FindNextElement(ICanvasElement current, List<ICanvasElement> candidates, NavigationDirection direction)
    {
        ICanvasElement? nearest = null;
        double nearestScore = double.MinValue;

        foreach (var candidate in candidates)
        {
            // Skip the current element.
            if (candidate == current) continue;

            // Score this candidate using the best corner-to-corner directional vector.
            double score = FindBestNavigationScore(current, candidate, direction);

            // Higher score is better. Only consider elements with a valid score
            // (i.e., in the target direction).
            if (!double.IsNaN(score) && score > nearestScore)
            {
                nearestScore = score;
                nearest = candidate;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Returns the best (highest) navigation score across all corner-to-corner vectors
    /// between <paramref name="current"/> and <paramref name="candidate"/>.
    /// Returns -1 when no corner pair falls within the requested direction cone.
    /// </summary>
    private double FindBestNavigationScore(ICanvasElement current, ICanvasElement candidate, NavigationDirection direction)
    {
        double bestScore = double.MinValue;
        bool found = false;

        double dx = candidate.CenterX - current.CenterX;
        double dy = candidate.CenterY - current.CenterY;
        double distance = Math.Sqrt(dx * dx + dy * dy);
        double score = CalculateNavigationScore(dx, dy, distance, direction, HasLinkBetween(current, candidate));
        if (!double.IsNaN(score) && score > bestScore)
        {
            bestScore = score;
            found = true;
        }

        return found ? bestScore : double.NaN;
    }

    /// <summary>
    /// Returns center + four corners for directional navigation scoring.
    /// </summary>
    private static Point[] GetElementNavigationPoints(ICanvasElement element)
    {
        double left = element.X;
        double top = element.Y;
        double right = element.X + element.Width;
        double bottom = element.Y + element.Height;

        return new[]
        {
            new Point(element.CenterX, element.CenterY),
            new Point(left, top),
            new Point(right, top),
            new Point(left, bottom),
            new Point(right, bottom)
        };
    }

    /// <summary>
    /// Calculates a navigation score for an element relative to the current position and direction.
    /// Returns <see cref="double.NaN"/> if the element is not in the target direction.
    /// Higher scores indicate more attractive matches.
    /// </summary>
    private double CalculateNavigationScore(double dx, double dy, double distance, NavigationDirection direction, bool isLinked)
    {
        // Minimum distance threshold: don't skip very close elements.
        const double MinDistance = 10.0;
        if (distance < MinDistance) return double.NaN;

        double selectedGeometryScore = GetDirectionalGeometryScore(dx, dy, distance, direction);
        if (selectedGeometryScore <= 0.0) return double.NaN;

        double bestGeometryScore = selectedGeometryScore;
        foreach (NavigationDirection candidateDirection in Enum.GetValues<NavigationDirection>())
        {
            double candidateScore = GetDirectionalGeometryScore(dx, dy, distance, candidateDirection);
            if (candidateScore > bestGeometryScore)
            {
                bestGeometryScore = candidateScore;
            }
        }

        const double GeometryScale = 100.0;
        const double WrongDirectionPenalty = 2048.0;
        const double LinkedElementBonus = 200.0;
        double score = selectedGeometryScore * GeometryScale;
        if (bestGeometryScore > selectedGeometryScore)
        {
            score -= WrongDirectionPenalty;
        }
        if (isLinked)
        {
            score += LinkedElementBonus;
        }
        return score;
    }

    /// <summary>
    /// Returns the raw geometry attractiveness for a particular direction, independent of link bonuses.
    /// A score of 0 means the vector is a better fit for some other direction or lies outside the direction cone.
    /// </summary>
    private static double GetDirectionalGeometryScore(double dx, double dy, double distance, NavigationDirection direction)
    {
        // Calculate angle from current element to candidate (in degrees, 0° = right, 90° = down, etc.).
        double angle = Math.Atan2(dy, dx) * 180 / Math.PI;
        if (angle < 0) angle += 360;

        const int directionCone = 85; // degrees of tolerance on either side of the ideal direction (e.g. for Right, ideal is 0°, so valid range is [360-70, 0+70] = [290, 70])

        double angleDeviation = direction switch
        {
            NavigationDirection.Right when angle <= directionCone || angle >= 360 - directionCone
                => Math.Abs(NormalizeSignedAngle(angle)),
            NavigationDirection.Down when angle >= 90 - directionCone && angle <= 90 + directionCone
                => Math.Abs(angle - 90),
            NavigationDirection.Left when angle >= 180 - directionCone && angle <= 180 + directionCone
                => Math.Abs(angle - 180),
            NavigationDirection.Up when angle >= 270 - directionCone && angle <= 270 + directionCone
                => Math.Abs(angle - 270),
            _ => double.NaN,
        };

        if (double.IsNaN(angleDeviation)) return 0.0;

        // TODO: calibrate these parameters based on user testing to find a good balance between directional fidelity and distance sensitivity.
        const double DistanceWeight = 100.0;
        const double AnglePenaltyAt45 = 0.20;
        const double ReferenceAngle = 45.0;
        // Calibrated angular decay: 45° off-axis yields a 20% penalty
        // (i.e., 80% of the in-direction utility at the same distance).
        double angleCloseness = Math.Pow(1.0 - AnglePenaltyAt45, angleDeviation / ReferenceAngle);
        double distanceCloseness = 1.0 / (1.0 + distance * DistanceWeight);
        var utility = angleCloseness * distanceCloseness;
        return utility;
    }

    private static double NormalizeSignedAngle(double angle)
    {
        if (angle > 180.0)
        {
            angle -= 360.0;
        }
        return angle;
    }

    /// <summary>
    /// Returns whether two canvas elements are directly connected by a link.
    /// </summary>
    private bool HasLinkBetween(ICanvasElement first, ICanvasElement second)
    {
        if (_vm is null) return false;

        return _vm.Links.Any(link =>
            (ReferenceEquals(link.Origin, first) && ReferenceEquals(link.Destination, second)) ||
            (ReferenceEquals(link.Origin, second) && ReferenceEquals(link.Destination, first)));
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

    // ── Parameter navigation (Tab/Shift+Tab) ──────────────────────────────
    /// <summary>
    /// Navigates to the next or previous parameter within the currently selected element.
    /// If no parameter is being edited, starts with the first (or last if backward) parameter.
    /// Returns <c>true</c> if navigation was successful, <c>false</c> if no parameters available.
    /// </summary>
    private bool NavigateToNextParameter(bool backward)
    {
        if (_vm is null) return false;

        ICanvasElement? parentElement = _editingParamParentElement;
        object? currentHook = _editingParamHook;

        // If no parameter is currently being edited, determine the parent from the selected element.
        if (_editingParamNode is null)
        {
            parentElement = _vm.SelectedElement;
            currentHook = null;
        }
        else if (parentElement is null || currentHook is null)
        {
            // Infer the parent from the parameter node if not already set.
            InferParameterContext(_editingParamNode, out parentElement, out currentHook);
        }

        if (parentElement is null) return false;

        // Get all editable parameter targets for this parent element.
        var hooks = GetParameterHooksForElement(parentElement);
        if (hooks.Count == 0) return false;

        // Find the index of the current parameter hook.
        int currentIndex = currentHook is not null
            ? hooks.FindIndex(h => h.Hook == currentHook)
            : -1;

        // For backward, if starting fresh, begin at the end; otherwise go backward.
        int nextIndex = backward
            ? (currentIndex < 0 ? hooks.Count - 1 : currentIndex - 1)
            : (currentIndex < 0 ? 0 : currentIndex + 1);

        // Wrap around.
        if (nextIndex < 0) nextIndex = hooks.Count - 1;
        else if (nextIndex >= hooks.Count) nextIndex = 0;

        // Get the next hook and its inlined parameter.
        var nextHookInfo = hooks[nextIndex];
        var nextHook = nextHookInfo.Hook;
        var nextParam = nextHookInfo.InlinedParam;

        if (nextParam is null) return false;

        // Commit the current edit if one is active.
        if (_editingParamNode is not null)
        {
            CommitParamEdit();
        }

        // Calculate the proper row position for this parameter.
        var rowPosition = CalculateParameterRowPosition(parentElement, nextHook);
        if (rowPosition is null) return false;

        BeginParamEdit(nextParam, rowPosition.Value.X, rowPosition.Value.Y, rowPosition.Value.W, parentElement, nextHook);
        InvalidateVisual();
        return true;
    }

    /// <summary>
    /// Calculates the row position (X, Y, Width) for a parameter on the given parent element and hook.
    /// Returns null if the position cannot be calculated.
    /// </summary>
    private (double X, double Y, double W)? CalculateParameterRowPosition(ICanvasElement element, object hook)
    {
        if (element is NodeViewModel nodeVm)
        {
            if (ReferenceEquals(hook, SelfParameterNavigationKey) && nodeVm.IsParameterNode)
            {
                return (nodeVm.X, nodeVm.Y + NodeHeaderHeight, NodeRenderWidth(nodeVm));
            }

            var nodeHooks = GetVisibleHooksForNode(nodeVm);
            if (nodeHooks is null) return null;

            int hookIdx = -1;
            for (int j = 0; j < nodeHooks.Count; j++)
            {
                if (ReferenceEquals(nodeHooks[j], hook)) { hookIdx = j; break; }
            }
            if (hookIdx < 0) return null;

            int rowOffset = nodeVm.IsParameterNode ? 1 : 0;
            double rw = NodeRenderWidth(nodeVm);
            double rowTop = nodeVm.Y + NodeHeaderHeight + (rowOffset + hookIdx) * HookRowHeight;
            return (nodeVm.X, rowTop, rw);
        }
        else if (element is FunctionInstanceViewModel fiVm)
        {
            var fiHooks = fiVm.UnderlyingInstance.Hooks;
            if (fiHooks is null) return null;

            int hookIdx = -1;
            for (int j = 0; j < fiHooks.Count; j++)
            {
                if (ReferenceEquals(fiHooks[j], hook)) { hookIdx = j; break; }
            }
            if (hookIdx < 0) return null;

            double rw = fiVm.Width;
            double rowTop = fiVm.Y + FtHeaderHeight + hookIdx * FtHookRowHeight;
            return (fiVm.X, rowTop, rw);
        }

        return null;
    }

    /// <summary>
    /// Infers the parent element and hook of a given parameter node by searching through
    /// inlined parameter mappings.
    /// </summary>
    private void InferParameterContext(NodeViewModel paramNode, out ICanvasElement? parentElement, out object? hook)
    {
        parentElement = null;
        hook = null;

        if (paramNode.IsParameterNode)
        {
            parentElement = paramNode;
            hook = SelfParameterNavigationKey;
            return;
        }

        if (_vm is null) return;

        // Search through node-based inlined parameters.
        foreach (var (key, value) in _hookInlinedParam)
        {
            if (value == paramNode)
            {
                parentElement = key.Item1;
                hook = key.Item2;
                return;
            }
        }

        // Search through function-instance-based inlined parameters.
        foreach (var (key, value) in _fiHookInlinedParam)
        {
            if (value == paramNode)
            {
                parentElement = key.Item1;
                hook = key.Item2;
                return;
            }
        }
    }

    /// <summary>
    /// Represents an editable parameter target and its associated navigation key.
    /// </summary>
    private struct ParameterHookInfo
    {
        public object Hook { get; set; }
        public NodeViewModel? InlinedParam { get; set; }
    }

    private IReadOnlyList<NodeHook>? GetVisibleHooksForNode(NodeViewModel nodeVm)
    {
        if (_nodeVisibleHooks.TryGetValue(nodeVm, out var visibleHooks))
        {
            return visibleHooks;
        }

        var nodeHooks = nodeVm.UnderlyingNode.Hooks;
        if (nodeHooks is null) return null;
        if (_vm?.ShowAllHooks == true || nodeVm.ShowHooks)
        {
            return nodeHooks;
        }

        var connected = new HashSet<NodeHook>();
        if (_vm is not null)
        {
            foreach (var link in _vm.Links)
            {
                if (ReferenceEquals(link.Origin, nodeVm))
                {
                    connected.Add(link.UnderlyingLink.OriginHook);
                }
            }
        }

        return [.. nodeHooks.Where(h =>
            h.Cardinality == HookCardinality.Single ||
            h.Cardinality == HookCardinality.AtLeastOne ||
            connected.Contains(h))];
    }

    /// <summary>
    /// Gets all editable parameter targets on the given element, in order.
    /// Includes direct value-row editing for parameter nodes and inlined parameters for hooks.
    /// </summary>
    private List<ParameterHookInfo> GetParameterHooksForElement(ICanvasElement element)
    {
        var hooks = new List<ParameterHookInfo>();

        if (element is NodeViewModel nodeVm)
        {
            if (nodeVm.IsParameterNode)
            {
                hooks.Add(new ParameterHookInfo
                {
                    Hook = SelfParameterNavigationKey,
                    InlinedParam = nodeVm
                });
            }

            // Regular nodes can have parameter hooks.
            var nodeHooks = nodeVm.UnderlyingNode.Hooks;
            if (nodeHooks is not null)
            {
                foreach (var hook in nodeHooks)
                {
                    var inlinedParam = _hookInlinedParam.TryGetValue((nodeVm, hook), out var param) ? param : null;
                    if (inlinedParam is not null)
                    {
                        hooks.Add(new ParameterHookInfo { Hook = hook, InlinedParam = inlinedParam });
                    }
                }
            }
        }
        else if (element is FunctionInstanceViewModel fiVm)
        {
            // Function instances have function parameter hooks.
            var fpHooks = fiVm.UnderlyingInstance.Hooks;
            if (fpHooks is not null)
            {
                foreach (var hook in fpHooks)
                {
                    if (hook is FunctionParameterHook fpHook)
                    {
                        var inlinedParam = _fiHookInlinedParam.TryGetValue((fiVm, fpHook), out var param) ? param : null;
                        if (inlinedParam is not null)
                        {
                            hooks.Add(new ParameterHookInfo { Hook = fpHook, InlinedParam = inlinedParam });
                        }
                    }
                }
            }
        }

        return hooks;
    }
}
