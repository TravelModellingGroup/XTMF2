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
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using XTMF2.ModelSystemConstruct;
using XTMF2.GUI.ViewModels;

namespace XTMF2.GUI.Views;

/// <summary>
/// Outcome of a <see cref="BoundaryPickerDialog"/> interaction.
/// </summary>
public enum BoundaryPickerResult
{
    /// <summary>The user cancelled without making a choice.</summary>
    Cancelled,
    /// <summary>The user chose an existing boundary to navigate to.</summary>
    Navigate,
    /// <summary>The user wants to create a new child boundary.</summary>
    Create
}

/// <summary>
/// Dialog that lets the user navigate to any boundary in the model system or create a new
/// child boundary under a selected parent.
/// </summary>
public partial class BoundaryPickerDialog : Window, INotifyPropertyChanged
{
    private BoundaryBrowseItem? _selectedBrowseItem;
    private List<BoundaryBrowseItem> _allBrowseItems = new();

    // ── Result ────────────────────────────────────────────────────────────
    /// <summary>How the dialog was closed.</summary>
    public BoundaryPickerResult Result { get; private set; } = BoundaryPickerResult.Cancelled;

    /// <summary>Boundary to navigate to when <see cref="Result"/> is <see cref="BoundaryPickerResult.Navigate"/>.</summary>
    public Boundary? SelectedBoundary { get; private set; }

    /// <summary>Parent under which to create the new boundary when <see cref="Result"/> is <see cref="BoundaryPickerResult.Create"/>.</summary>
    public Boundary? NewBoundaryParent { get; private set; }

    /// <summary>Name of the boundary to create when <see cref="Result"/> is <see cref="BoundaryPickerResult.Create"/>.</summary>
    public string? NewBoundaryName { get; private set; }

    // ── Bindable state ────────────────────────────────────────────────────
    /// <summary>Flat indented list of all boundaries shown in the picker.</summary>
    public ObservableCollection<BoundaryBrowseItem> BrowseItems { get; } = new();

    /// <summary>Currently highlighted item in the list.</summary>
    public BoundaryBrowseItem? SelectedBrowseItem
    {
        get => _selectedBrowseItem;
        set
        {
            _selectedBrowseItem = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedBrowseItem)));
        }
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    // The boundary used as parent when no list item is selected.
    private readonly Boundary _defaultParent;

    // ── Construction ──────────────────────────────────────────────────────
    public BoundaryPickerDialog()
    {
        InitializeComponent();
        DataContext  = this;
        _defaultParent = null!; // design-time only
    }

    /// <param name="allBoundaries">All boundaries in the tree, each with their nesting depth.</param>
    /// <param name="currentBoundary">Boundary currently being viewed (pre-selected in the list).</param>
    public BoundaryPickerDialog(
        IReadOnlyList<(Boundary Boundary, int Depth)> allBoundaries,
        Boundary currentBoundary)
    {
        InitializeComponent();
        DataContext    = this;
        _defaultParent = currentBoundary;
        AddHandler(KeyDownEvent, (_, ke) =>
        {
            if (ke.Key != Key.Escape) return;
            Cancel_Click(null, new RoutedEventArgs());
            ke.Handled = true;
        }, RoutingStrategies.Tunnel);

        BoundaryBrowseItem? preSelect = null;
        foreach (var (b, depth) in allBoundaries)
        {
            var item = new BoundaryBrowseItem(b, depth);
            BrowseItems.Add(item);
            if (ReferenceEquals(b, currentBoundary))
                preSelect = item;
        }

        _allBrowseItems = new List<BoundaryBrowseItem>(BrowseItems);

        // Pre-select the current boundary once the window is open and the list is rendered.
        Opened += (_, _) =>
        {
            if (preSelect is not null)
            {
                SelectedBrowseItem = preSelect;
                BoundaryListBox.ScrollIntoView(preSelect);
            }
            SearchBox.Focus();
        };

        Activated += (_, _) => SearchBox.Focus();

        SearchBox.TextChanged += SearchBox_TextChanged;
        SearchBox.KeyDown     += SearchBox_KeyDown;
    }

    // ── Search ────────────────────────────────────────────────────────────

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        BrowseItems.Clear();
        foreach (var item in _allBrowseItems)
        {
            if (string.IsNullOrEmpty(query) ||
                item.Boundary.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                BrowseItems.Add(item);
            }
        }
        // Keep first visible item selected so the user can immediately press Enter.
        if (BrowseItems.Count > 0 && (SelectedBrowseItem is null || !BrowseItems.Contains(SelectedBrowseItem)))
            SelectedBrowseItem = BrowseItems[0];
        else if (BrowseItems.Count == 0)
            SelectedBrowseItem = null;
    }

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && BrowseItems.Count > 0)
        {
            SelectedBrowseItem = BrowseItems[0];
            Result             = BoundaryPickerResult.Navigate;
            SelectedBoundary   = SelectedBrowseItem.Boundary;
            Close();
            e.Handled = true;
        }
    }

    // ── Button handlers ───────────────────────────────────────────────────

    private void BoundaryListBox_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (SelectedBrowseItem is null) return;
        Result           = BoundaryPickerResult.Navigate;
        SelectedBoundary = SelectedBrowseItem.Boundary;
        Close();
    }

    private void Navigate_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedBrowseItem is null) return;
        Result           = BoundaryPickerResult.Navigate;
        SelectedBoundary = SelectedBrowseItem.Boundary;
        Close();
    }

    private void CreateBoundary_Click(object? sender, RoutedEventArgs e)
    {
        var name = NewBoundaryNameBox.Text?.Trim();
        if (string.IsNullOrEmpty(name)) return;

        Result             = BoundaryPickerResult.Create;
        NewBoundaryParent  = SelectedBrowseItem?.Boundary ?? _defaultParent;
        NewBoundaryName    = name;
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Result = BoundaryPickerResult.Cancelled;
        Close();
    }
}
