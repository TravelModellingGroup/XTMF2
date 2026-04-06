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
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace XTMF2.GUI.Views;

/// <summary>
/// A dialog that lets the user pick which <see cref="NodeHook"/> on the origin node
/// should be used to form a new link to a destination node.
/// </summary>
public partial class HookPickerDialog : Window, INotifyPropertyChanged
{
    private string _prompt = "Select the hook to connect through:";

    public new event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Prompt text shown above the hook list.</summary>
    public string Prompt
    {
        get => _prompt;
        set
        {
            _prompt = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Prompt)));
        }
    }

    /// <summary>The hooks available for selection.</summary>
    public IReadOnlyList<NodeHook> Hooks { get; }

    /// <summary>The hook chosen by the user, or <c>null</c> if cancelled.</summary>
    public NodeHook? SelectedHook { get; private set; }

    /// <summary>True if the user dismissed the dialog without choosing a hook.</summary>
    public bool WasCancelled { get; private set; } = true;

    /// <summary>
    /// Design-time / XMLC-required parameterless constructor.
    /// Use the overload that accepts a hook list at runtime.
    /// </summary>
    public HookPickerDialog() : this([]) { }

    /// <summary>
    /// Creates the dialog pre-populated with the compatible hooks for a link.
    /// </summary>
    /// <param name="hooks">The list of compatible hooks to choose from.</param>
    /// <param name="prompt">Optional custom prompt text.</param>
    public HookPickerDialog(IReadOnlyList<NodeHook> hooks, string? prompt = null)
    {
        Hooks = hooks;
        if (prompt is not null) _prompt = prompt;
        InitializeComponent();
        DataContext = this;
        AddHandler(KeyDownEvent, (_, ke) =>
        {
            if (ke.Key != Key.Escape) return;
            Cancel_Click(null, new RoutedEventArgs());
            ke.Handled = true;
        }, RoutingStrategies.Tunnel);

        // Pre-select the first item and focus the list on open.
        Opened += (_, _) =>
        {
            if (HookListBox.ItemCount > 0)
                HookListBox.SelectedIndex = 0;
            HookListBox.Focus();
        };
    }

    private void OK_Click(object? sender, RoutedEventArgs e)
    {
        SelectedHook = HookListBox.SelectedItem as NodeHook;
        WasCancelled = SelectedHook is null;
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        WasCancelled = true;
        Close();
    }
}
