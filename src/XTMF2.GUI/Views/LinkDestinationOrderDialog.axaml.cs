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
using System.Linq;
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
            Notify(nameof(CanRemove));
        }
    }

    /// <summary>True when the selected item can be moved up (i.e., it is not first).</summary>
    public bool CanMoveUp   => _selectedItem is not null && Items.IndexOf(_selectedItem) > 0;

    /// <summary>True when the selected item can be moved down (i.e., it is not last).</summary>
    public bool CanMoveDown => _selectedItem is not null && Items.IndexOf(_selectedItem) < Items.Count - 1;

    /// <summary>True when the selected item can be removed (at least two destinations must survive).</summary>
    public bool CanRemove   => _selectedItem is not null && Items.Count > 1;

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
    private IReadOnlyList<DestinationOrderItem> _dragItems = [];

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
        // ListBoxItems handle bubbling pointer presses for selection before the
        // ListBox can start a reorder gesture. Tunnel from the ListBox first so
        // every row can establish drag state reliably.
        DestListBox.AddHandler(InputElement.PointerPressedEvent, OnListPointerPressed,
            RoutingStrategies.Tunnel);
        DestListBox.AddHandler(InputElement.PointerMovedEvent, OnListPointerMoved,
            RoutingStrategies.Tunnel);
        DestListBox.AddHandler(InputElement.PointerReleasedEvent, OnListPointerReleased,
            RoutingStrategies.Tunnel);
        DestListBox.AddHandler(InputElement.PointerCaptureLostEvent, OnListPointerCaptureLost,
            RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, (_, ke) =>
        {
            if (ke.Key != Key.Escape) return;
            Close();
            ke.Handled = true;
        }, RoutingStrategies.Tunnel);

        for (int i = 0; i < destinationNames.Count; i++)
            Items.Add(new DestinationOrderItem(destinationNames[i], originalIndex: i, displayIndex: i + 1));

        Opened += (_, _) =>
        {
            if (Items.Count > 0)
                SelectedItem = Items[0];
            DestListBox.Focus(NavigationMethod.Unspecified, KeyModifiers.None);
        };
    }

    // ── Button handlers ──────────────────────────────────────────────────

    private void MoveUp_Click(object? sender, RoutedEventArgs e)
    {
        var selected = GetSelectedItems();
        if (selected.Count == 0 || Items.IndexOf(selected[0]) <= 0) return;
        MoveSelectedItems(-1, selected);
    }

    private void MoveDown_Click(object? sender, RoutedEventArgs e)
    {
        var selected = GetSelectedItems();
        if (selected.Count == 0 || Items.IndexOf(selected[^1]) >= Items.Count - 1) return;
        MoveSelectedItems(1, selected);
    }

    private void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedItem is null || Items.Count <= 1) return;
        int idx = Items.IndexOf(_selectedItem);
        if (idx < 0) return;
        Items.RemoveAt(idx);
        UpdateDisplayIndices();
        // Select the item that slid into this position, or the new last item.
        SelectedItem = Items.Count > 0
            ? Items[Math.Min(idx, Items.Count - 1)]
            : null;
        Notify(nameof(CanMoveUp));
        Notify(nameof(CanMoveDown));
        Notify(nameof(CanRemove));
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

    // ── Drag-to-reorder ─────────────────────────────────────────────────

    private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(DestListBox).Properties.IsLeftButtonPressed) return;

        var pos  = e.GetCurrentPoint(DestListBox).Position;
        var item = GetItemAtPoint(pos);
        if (item is null) return;

        _dragSource   = item;
        _dragStartPos = pos;
        _isDragging   = false;
        var selected = GetSelectedItems();
        _dragItems = selected.Contains(item) && selected.Count > 1
            ? selected
            : [item];
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
            e.Pointer.Capture(DestListBox);
            DestListBox.Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
        }

        // Find which row the pointer is currently over.
        var target = GetItemAtPoint(pos);
        if (target is null || _dragItems.Contains(target)) return;

        int targetIndex = Items.IndexOf(target);
        int firstSelectedIndex = _dragItems.Min(Items.IndexOf);
        int lastSelectedIndex = _dragItems.Max(Items.IndexOf);
        if (targetIndex < firstSelectedIndex)
            MoveSelectedItems(-1, _dragItems);
        else if (targetIndex > lastSelectedIndex)
            MoveSelectedItems(1, _dragItems);

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
        _dragItems = [];
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the <see cref="DestinationOrderItem"/> whose row contains
    /// <paramref name="pointRelativeToListBox"/>, or <c>null</c> if no row was hit.
    /// </summary>
    private DestinationOrderItem? GetItemAtPoint(Point pointRelativeToListBox)
    {
        // InputHitTest is unreliable when the pointer is captured by the ListBox.
        // Walk all visual descendants instead and use TranslatePoint to convert each
        // ListBoxItem's top-left corner into ListBox-local coordinates.
        foreach (var lbi in DestListBox.GetVisualDescendants().OfType<ListBoxItem>())
        {
            var topLeft = lbi.TranslatePoint(new Point(0, 0), DestListBox);
            if (topLeft is not { } tl) continue;
            var itemRect = new Rect(tl, new Size(lbi.Bounds.Width, lbi.Bounds.Height));
            if (itemRect.Contains(pointRelativeToListBox)
                && lbi.DataContext is DestinationOrderItem item)
                return item;
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

    private List<DestinationOrderItem> GetSelectedItems()
    {
        if (DestListBox.SelectedItems is not { } selectedItems)
            return [];
        return selectedItems
            .OfType<DestinationOrderItem>()
            .OrderBy(item => Items.IndexOf(item))
            .ToList();
    }

    private void MoveSelectedItems(int direction, IReadOnlyList<DestinationOrderItem>? selectedItems = null)
    {
        var selected = selectedItems ?? GetSelectedItems();
        if (selected.Count == 0) return;
        var activeItem = _selectedItem is not null && selected.Contains(_selectedItem)
            ? _selectedItem
            : selected[0];

        if (direction < 0)
        {
            if (Items.IndexOf(selected[0]) == 0) return;
            foreach (var item in selected)
                Items.Move(Items.IndexOf(item), Items.IndexOf(item) - 1);
        }
        else
        {
            if (Items.IndexOf(selected[^1]) == Items.Count - 1) return;
            for (int i = selected.Count - 1; i >= 0; i--)
            {
                var item = selected[i];
                Items.Move(Items.IndexOf(item), Items.IndexOf(item) + 1);
            }
        }

        UpdateDisplayIndices();
        RestoreSelection(selected, activeItem);
        Notify(nameof(CanMoveUp));
        Notify(nameof(CanMoveDown));
    }

    private void RestoreSelection(IReadOnlyList<DestinationOrderItem> selected,
        DestinationOrderItem activeItem)
    {
        if (DestListBox.SelectedItems is { } selectedItems)
        {
            selectedItems.Clear();
            foreach (var item in selected)
                selectedItems.Add(item);
        }
        // Keep the button target locally; assigning the bound SelectedItem after
        // restoring SelectedItems would collapse Avalonia's multi-selection.
        _selectedItem = activeItem;
        Notify(nameof(SelectedItem));
        Notify(nameof(CanMoveUp));
        Notify(nameof(CanMoveDown));
        Notify(nameof(CanRemove));
        DestListBox.Focus(NavigationMethod.Unspecified, KeyModifiers.None);
    }

    private void Notify(string prop)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}
