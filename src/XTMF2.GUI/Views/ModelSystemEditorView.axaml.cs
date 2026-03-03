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
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using XTMF2.GUI.ViewModels;

namespace XTMF2.GUI.Views;

public partial class ModelSystemEditorView : UserControl
{
    private ModelSystemEditorViewModel? _vm;

    // ── Destination list drag-and-drop state ────────────────────────────
    private int    _destDragIndex      = -1;   // index captured on pointer-press
    private int    _destActiveDragFrom = -1;   // index that is currently being dragged
    private bool   _destDragging       = false;
    private double _destDragStartY     = 0;   // Y position at press, used for threshold
    private const double DestDragThreshold = 5.0;

    public ModelSystemEditorView()
    {
        InitializeComponent();
        DataContextChanged    += OnDataContextChanged;
        AttachedToVisualTree  += OnAttachedToVisualTree;

        // Pressing Enter in the name box commits the rename without requiring the Rename button.
        NameEditBox.KeyDown += OnNameEditBoxKeyDown;

        // F2 anywhere in this view focuses the rename box (when an element is selected).
        KeyDown += OnViewKeyDown;

        // Enter in the node search box picks the first match and returns focus to the canvas.
        NodeSearchBox.KeyDown        += OnNodeSearchBoxKeyDown;
        NodeSearchBox.DropDownClosed += OnNodeSearchBoxDropDownClosed;

        // Destination list drag-and-drop for re-ordering MultiLink destinations.
        DestinationListBox.PointerPressed  += OnDestListPointerPressed;
        DestinationListBox.PointerMoved    += OnDestListPointerMoved;
        DestinationListBox.PointerReleased += OnDestListPointerReleased;

        // Double-tap a destination entry to navigate the canvas to that node.
        DestinationListBox.DoubleTapped += OnDestListDoubleTapped;

        // Boundary navigation dropdown.
        BoundaryNavComboBox.SelectionChanged += OnBoundaryNavSelectionChanged;
    }

    private void OnViewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2 && _vm?.SelectedElement is not null)
        {
            NameEditBox.Focus();
            NameEditBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _vm?.SaveModelSystemCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.E && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            NodeSearchBox.Text = string.Empty;
            NodeSearchBox.Focus();
            e.Handled = true;
        }
    }

    private void OnNameEditBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            _vm?.CommitRenameCommand.Execute(null);
    }

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        // Give the VM a reference to the top-level window for showing dialogs.
        if (_vm is not null)
            _vm.ParentWindow = TopLevel.GetTopLevel(this) as Window;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        // Unsubscribe from the old VM.
        if (_vm is not null)
        {
            _vm.PropertyChanged       -= OnVmPropertyChanged;
            _vm.ScrollToNodeRequested -= OnScrollToNodeRequested;
        }

        _vm = DataContext as ModelSystemEditorViewModel;

        // Provide the parent window immediately if we are already in the tree.
        if (_vm is not null)
        {
            _vm.ParentWindow = TopLevel.GetTopLevel(this) as Window;
            _vm.PropertyChanged       += OnVmPropertyChanged;
            _vm.ScrollToNodeRequested += OnScrollToNodeRequested;
        }
    }

    private void OnScrollToNodeRequested(NodeViewModel node)
    {
        var viewport = CanvasScrollViewer.Viewport;
        var offsetX  = node.X + node.Width  / 2.0 - viewport.Width  / 2.0;
        var offsetY  = node.Y + node.Height / 2.0 - viewport.Height / 2.0;
        CanvasScrollViewer.Offset = new Avalonia.Vector(
            Math.Max(0, offsetX),
            Math.Max(0, offsetY));
        TheCanvas.Focus();
    }

    // Set when the Enter-key handler has already committed a selection, so that the
    // subsequent DropDownClosed event does not commit it a second time.
    private bool _suppressNextDropDownClose;

    private void OnNodeSearchBoxDropDownClosed(object? sender, EventArgs e)
    {
        if (_suppressNextDropDownClose) { _suppressNextDropDownClose = false; return; }
        if (NodeSearchBox.SelectedItem is NodeViewModel nvm && _vm is not null)
            _vm.NodeSearchSelection = nvm;
    }

    private void OnNodeSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _vm is null) return;

        // Prefer the item explicitly highlighted in the dropdown; fall back to text search.
        var nodeVm = NodeSearchBox.SelectedItem as NodeViewModel
                     ?? _vm.Nodes.FirstOrDefault(n =>
                            n.Name.Contains(NodeSearchBox.Text ?? string.Empty,
                                            StringComparison.OrdinalIgnoreCase));

        _suppressNextDropDownClose = true;   // DropDownClosed will fire after Enter

        if (nodeVm is not null)
            _vm.NodeSearchSelection = nodeVm;
        else
            TheCanvas.Focus();

        e.Handled = true;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Nothing needs code-behind attention at present;
        // all property panel labels are XAML-bound.
    }

    // ── Boundary navigation dropdown ──────────────────────────────────────

    private void OnBoundaryNavSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox cb) return;
        if (cb.SelectedItem is not BoundaryNavigationItem item)
        {
            cb.SelectedIndex = -1;
            return;
        }

        // Always reset immediately — the current boundary name shows as the placeholder text.
        cb.SelectedIndex = -1;

        if (item.IsBrowse)
            _vm?.BrowseBoundariesCommand.Execute(null);
        else if (item.Boundary is { } boundary)
            _vm?.SwitchToBoundary(boundary);
    }

    // ── Destination list drag-and-drop (pointer-based, no DragDrop API) ──

    private void OnDestListDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (_vm is null) return;
        if (e.Source is Control src && src.DataContext is LinkDestinationViewModel item)
            _vm.NavigateToLinkDestination(item);
    }

    private void OnDestListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _destDragging       = false;
        _destActiveDragFrom = -1;
        if (e.Source is Control src && src.DataContext is LinkDestinationViewModel item)
        {
            _destDragIndex  = _vm?.SelectedLinkDestinationEntries.IndexOf(item) ?? -1;
            _destDragStartY = e.GetPosition(DestinationListBox).Y;
            if (_destDragIndex >= 0)
                e.Pointer.Capture(DestinationListBox);
        }
        else
        {
            _destDragIndex = -1;
        }
    }

    private void OnDestListPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_destDragIndex < 0) return;
        var pt = e.GetCurrentPoint(DestinationListBox);
        if (!pt.Properties.IsLeftButtonPressed)
        {
            _destDragIndex = -1;
            HideDragIndicator();
            return;
        }

        // Don't commit to a drag until the pointer has moved enough to be intentional.
        if (!_destDragging)
        {
            if (Math.Abs(pt.Position.Y - _destDragStartY) < DestDragThreshold)
                return;
            _destDragging       = true;
            _destActiveDragFrom = _destDragIndex;
        }

        UpdateDragIndicator(pt.Position);
    }

    private void OnDestListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Pointer.Capture(null);
        HideDragIndicator();

        if (!_destDragging || _destActiveDragFrom < 0)
        {
            _destDragIndex = -1;
            _destDragging  = false;
            return;
        }

        var fromIndex       = _destActiveDragFrom;
        var insertBefore    = GetDropInsertIndex(e.GetPosition(DestinationListBox));

        // MoveDestination(from, to) removes the item first then inserts at 'to', so the
        // effective target index shifts by -1 whenever the source was before the insert point.
        var toIndex = fromIndex < insertBefore ? insertBefore - 1 : insertBefore;
        toIndex = Math.Clamp(toIndex, 0, DestinationListBox.ItemCount - 1);

        if (_vm is not null && fromIndex != toIndex)
            _vm.MoveLinkDestination(fromIndex, toIndex);

        _destDragIndex      = -1;
        _destDragging       = false;
        _destActiveDragFrom = -1;
    }

    /// <summary>
    /// Returns the "insert before" index (0 = before the first item, ItemCount = append after the last).
    /// Used for both computing the drop target and positioning the indicator.
    /// </summary>
    private int GetDropInsertIndex(Avalonia.Point dropPos)
    {
        for (int i = 0; i < DestinationListBox.ItemCount; i++)
        {
            if (DestinationListBox.ContainerFromIndex(i) is not Control container) continue;
            var mid = container.Bounds.Top + container.Bounds.Height / 2.0;
            if (dropPos.Y < mid)
                return i;
        }
        return DestinationListBox.ItemCount;  // append to end
    }

    /// <summary>Show the drop-indicator line at the position implied by the current pointer.</summary>
    private void UpdateDragIndicator(Avalonia.Point posInListBox)
    {
        int insertBefore = GetDropInsertIndex(posInListBox);

        double? indicatorY = null;
        if (insertBefore < DestinationListBox.ItemCount)
        {
            if (DestinationListBox.ContainerFromIndex(insertBefore) is Control c)
                indicatorY = c.Bounds.Top;
        }
        else if (DestinationListBox.ItemCount > 0)
        {
            if (DestinationListBox.ContainerFromIndex(DestinationListBox.ItemCount - 1) is Control last)
                indicatorY = last.Bounds.Bottom;
        }

        if (indicatorY is null) return;

        DestDropIndicator.Width     = DestinationListBox.Bounds.Width;
        Avalonia.Controls.Canvas.SetTop(DestDropIndicator, indicatorY.Value - 1);
        DestDropIndicator.IsVisible = true;
    }

    private void HideDragIndicator() => DestDropIndicator.IsVisible = false;
}

