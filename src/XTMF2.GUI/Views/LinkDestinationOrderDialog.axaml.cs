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
using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace XTMF2.GUI.Views;

/// <summary>
/// Represents a single row in the <see cref="LinkDestinationOrderDialog"/> list.
/// </summary>
public sealed class DestinationOrderItem : INotifyPropertyChanged
{
    private string _displayIndex;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The display name of the destination node.</summary>
    public string Name { get; }

    /// <summary>The index this item had in the original (unmodified) destination list.</summary>
    public int OriginalIndex { get; }

    /// <summary>The 1-based position label shown in the badge ("1", "2", …).
    /// Updated after every move so the badge always reflects the current position.</summary>
    public string DisplayIndex
    {
        get => _displayIndex;
        set
        {
            if (_displayIndex == value) return;
            _displayIndex = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayIndex)));
        }
    }

    public DestinationOrderItem(string name, int originalIndex, int displayIndex)
    {
        Name = name;
        OriginalIndex = originalIndex;
        _displayIndex = displayIndex.ToString();
    }
}

/// <summary>
/// A dialog that allows the user to reorder the destination nodes of a multi-destination link.
/// Items can be reordered by dragging or with the Up / Down buttons.
/// After closing, inspect <see cref="WasCancelled"/> and <see cref="FinalOrderIndices"/>.
/// </summary>
public partial class LinkDestinationOrderDialog : Window, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    // ── Observable collection shown in the ListBox ──────────────────────
    public ObservableCollection<DestinationOrderItem> Items { get; } = new();

    private DestinationOrderItem? _selectedItem;

    /// <summary>The currently selected item in the list.</summary>
    public DestinationOrderItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (ReferenceEquals(_selectedItem, value)) return;
            _selectedItem = value;
            Notify(nameof(SelectedItem));
            Notify(nameof(CanMoveUp));
            Notify(nameof(CanMoveDown));
        }
    }

    /// <summary>True when the selected item can be moved up (i.e., it is not first).</summary>
    public bool CanMoveUp   => _selectedItem is not null && Items.IndexOf(_selectedItem) > 0;

    /// <summary>True when the selected item can be moved down (i.e., it is not last).</summary>
    public bool CanMoveDown => _selectedItem is not null && Items.IndexOf(_selectedItem) < Items.Count - 1;

    /// <summary>True if the user cancelled without confirming.</summary>
    public bool WasCancelled { get; private set; } = true;

    /// <summary>
    /// The original indices in the user's chosen order.
    /// For example, <c>[2, 0, 1]</c> means the item that was originally at index 2
    /// should now be first, the item originally at 0 should be second, and so on.
    /// Only meaningful when <see cref="WasCancelled"/> is <c>false</c>.
    /// </summary>
    public IReadOnlyList<int> FinalOrderIndices { get; private set; } = [];

    // ── Drag state ───────────────────────────────────────────────────────
    private const double DragThreshold = 6.0;
    private DestinationOrderItem? _dragSource;
    private bool                  _isDragging;
    private Point                 _dragStartPos;

    // ── Design-time / XAMLC-required parameterless constructor ──────────
    public LinkDestinationOrderDialog() : this([]) { }

    /// <summary>
    /// Creates the dialog pre-populated with the destination names in their current order.
    /// </summary>
    /// <param name="destinationNames">Destination node names, in current link order (index 0 = first destination).</param>
    public LinkDestinationOrderDialog(IReadOnlyList<string> destinationNames)
    {
        InitializeComponent();
        DataContext = this;

        for (int i = 0; i < destinationNames.Count; i++)
            Items.Add(new DestinationOrderItem(destinationNames[i], originalIndex: i, displayIndex: i + 1));

        Opened += (_, _) =>
        {
            if (Items.Count > 0)
                SelectedItem = Items[0];
            DestListBox.Focus();
        };
    }

    // ── Button handlers ──────────────────────────────────────────────────

    private void MoveUp_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedItem is null) return;
        int idx = Items.IndexOf(_selectedItem);
        if (idx <= 0) return;
        var item = _selectedItem;
        Items.Move(idx, idx - 1);
        UpdateDisplayIndices();
        // Re-assert selection explicitly — the collection-change event can clear it.
        DestListBox.SelectedItem = item;
        Notify(nameof(CanMoveUp));
        Notify(nameof(CanMoveDown));
    }

    private void MoveDown_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedItem is null) return;
        int idx = Items.IndexOf(_selectedItem);
        if (idx < 0 || idx >= Items.Count - 1) return;
        var item = _selectedItem;
        Items.Move(idx, idx + 1);
        UpdateDisplayIndices();
        DestListBox.SelectedItem = item;
        Notify(nameof(CanMoveUp));
        Notify(nameof(CanMoveDown));
    }

    private void OK_Click(object? sender, RoutedEventArgs e)
    {
        WasCancelled = false;
        var order = new int[Items.Count];
        for (int i = 0; i < Items.Count; i++)
            order[i] = Items[i].OriginalIndex;
        FinalOrderIndices = order;
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        WasCancelled = true;
        Close();
    }

    // ── Drag-to-reorder (pointer events wired in AXAML) ─────────────────

    private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(DestListBox).Properties.IsLeftButtonPressed) return;

        var pos  = e.GetCurrentPoint(DestListBox).Position;
        var item = GetItemAtPoint(pos);
        if (item is null) return;

        _dragSource   = item;
        _dragStartPos = pos;
        _isDragging   = false;

        // Select the pressed row immediately.
        SelectedItem = item;

        e.Pointer.Capture(DestListBox);
        e.Handled = true;
    }

    private void OnListPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragSource is null) return;

        var pos = e.GetCurrentPoint(DestListBox).Position;

        // Start drag once the pointer has moved past the threshold.
        if (!_isDragging)
        {
            var dx = pos.X - _dragStartPos.X;
            var dy = pos.Y - _dragStartPos.Y;
            if (Math.Sqrt(dx * dx + dy * dy) < DragThreshold) return;
            _isDragging = true;
            DestListBox.Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
        }

        // Find which row the pointer is currently over.
        var target = GetItemAtPoint(pos);
        if (target is null || ReferenceEquals(target, _dragSource)) return;

        int fromIdx = Items.IndexOf(_dragSource);
        int toIdx   = Items.IndexOf(target);
        if (fromIdx < 0 || toIdx < 0 || fromIdx == toIdx) return;

        // Live-reorder: the drag source physically moves to wherever the pointer is.
        Items.Move(fromIdx, toIdx);
        UpdateDisplayIndices();
        DestListBox.SelectedItem = _dragSource;
        Notify(nameof(CanMoveUp));
        Notify(nameof(CanMoveDown));

        e.Handled = true;
    }

    private void OnListPointerReleased(object? sender, PointerReleasedEventArgs e)
        => EndDrag(e.Pointer);

    private void OnListPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        => EndDrag(null);

    private void EndDrag(IPointer? pointer)
    {
        if (_dragSource is not null)
        {
            DestListBox.Cursor = Cursor.Default;
            pointer?.Capture(null);
        }
        _dragSource = null;
        _isDragging = false;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the <see cref="DestinationOrderItem"/> whose row contains
    /// <paramref name="pointRelativeToListBox"/>, or <c>null</c> if no row was hit.
    /// </summary>
    private DestinationOrderItem? GetItemAtPoint(Point pointRelativeToListBox)
    {
        var hit = DestListBox.InputHitTest(pointRelativeToListBox);
        var element = hit as Visual;
        while (element is not null)
        {
            if (element is ListBoxItem { DataContext: DestinationOrderItem item })
                return item;
            element = element.GetVisualParent();
        }
        return null;
    }

    /// <summary>
    /// Refreshes the 1-based position badge on every item after a reorder.
    /// </summary>
    private void UpdateDisplayIndices()
    {
        for (int i = 0; i < Items.Count; i++)
            Items[i].DisplayIndex = (i + 1).ToString();
    }

    private void Notify(string prop)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}
