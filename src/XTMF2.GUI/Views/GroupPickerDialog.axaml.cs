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
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace XTMF2.GUI.Views;

/// <summary>
/// A lightweight dialog that prompts the user to choose one item from a named list.
/// Used when a node is added to estimation or calibration and more than one group exists.
/// </summary>
public partial class GroupPickerDialog : Window, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // ── Result ─────────────────────────────────────────────────────────
    /// <summary>True when the user clicked "Select"; false when cancelled.</summary>
    public bool Confirmed { get; private set; }

    /// <summary>The zero-based index of the chosen group, or <c>null</c> when cancelled.</summary>
    public int? PickedIndex => Confirmed && _selectedIndex >= 0 ? _selectedIndex : null;

    // ── Bindable state ──────────────────────────────────────────────────
    /// <summary>Message shown above the list.</summary>
    public string Prompt { get; }

    /// <summary>Display names of the groups offered to the user.</summary>
    public List<string> GroupNames { get; }

    private int _selectedIndex = 0;

    /// <summary>Index of the currently highlighted list item.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            _selectedIndex = value;
            Raise(nameof(SelectedIndex));
            Raise(nameof(HasSelection));
        }
    }

    /// <summary>True when a valid list item is selected; drives the "Select" button.</summary>
    public bool HasSelection => _selectedIndex >= 0;

    // ── Constructors ────────────────────────────────────────────────────

    /// <summary>Design-time constructor; do not call in production code.</summary>
    public GroupPickerDialog()
    {
        Prompt = "Select a group:";
        GroupNames = [];
        DataContext = this;
        InitializeComponent();
    }

    /// <param name="prompt">Instructional text shown above the list.</param>
    /// <param name="groupNames">Ordered names of the groups to display.</param>
    public GroupPickerDialog(string prompt, IEnumerable<string> groupNames)
    {
        Prompt = prompt;
        GroupNames = [.. groupNames];
        DataContext = this;
        InitializeComponent();

        // Allow keyboard cancellation.
        AddHandler(KeyDownEvent, (_, key) =>
        {
            if (key.Key != Key.Escape) return;
            Cancel_Click(null, new RoutedEventArgs());
            key.Handled = true;
        }, RoutingStrategies.Tunnel);
    }

    // ── Handlers ────────────────────────────────────────────────────────

    private void OK_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedIndex < 0) return;
        Confirmed = true;
        Close();
    }

    /// <summary>Handles the "Select" button being double tapped.</summary>
    private void OK_Click(object? sender, TappedEventArgs e)
    {
        if (_selectedIndex < 0) return;
        Confirmed = true;
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close();
    }

}
