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
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Collections.Generic;
using System.ComponentModel;

namespace XTMF2.GUI.Views;

/// <summary>
/// A small dialog that lets the user pick a Start from a ComboBox.
/// </summary>
public partial class StartPickerDialog : Window, INotifyPropertyChanged
{
    private string? _prompt;
    private string? _selectedStartName;

    public new event PropertyChangedEventHandler? PropertyChanged;

    public string? Prompt
    {
        get => _prompt;
        set
        {
            _prompt = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Prompt)));
        }
    }

    public IReadOnlyList<string> StartNames { get; }

    public string? SelectedStartName
    {
        get => _selectedStartName;
        set
        {
            _selectedStartName = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedStartName)));
        }
    }

    /// <summary>
    /// <c>true</c> when the user dismissed the dialog without clicking OK.
    /// </summary>
    public bool WasCancelled { get; private set; } = true;

    public StartPickerDialog()
    {
        InitializeComponent();
        StartNames  = [];
        DataContext = this;
    }

    /// <param name="title">Window title.</param>
    /// <param name="prompt">Label shown above the ComboBox.</param>
    /// <param name="startNames">List of available start names to display.</param>
    /// <param name="defaultStart">The start that should be pre-selected.</param>
    public StartPickerDialog(string title, string prompt,
                             IReadOnlyList<string> startNames,
                             string? defaultStart = null)
    {
        InitializeComponent();
        Title             = title;
        Prompt            = prompt;
        StartNames        = startNames;
        SelectedStartName = defaultStart ?? (startNames.Count > 0 ? startNames[0] : null);
        DataContext       = this;

        Opened += (_, _) => StartComboBox.Focus();
    }

    private void OK_Click(object? sender, RoutedEventArgs e)
    {
        WasCancelled = false;
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        WasCancelled = true;
        Close();
    }
}
