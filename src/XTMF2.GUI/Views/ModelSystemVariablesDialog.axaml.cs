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
using Avalonia.Input;
using Avalonia.Interactivity;
using XTMF2.GUI.ViewModels;

namespace XTMF2.GUI.Views;

/// <summary>
/// Non-modal floating dialog that displays and manages the model system's
/// variable list. Opened from the floating action bar in ModelSystemEditorView.
/// </summary>
public partial class ModelSystemVariablesDialog : Window
{
    private readonly ModelSystemEditorViewModel _vm = null!;

    /// <summary>Required by the Avalonia AXAML compiler (design-time only).</summary>
    public ModelSystemVariablesDialog()
    {
        InitializeComponent();
    }

    /// <summary>Runtime constructor — pass the editor view-model as DataContext.</summary>
    public ModelSystemVariablesDialog(ModelSystemEditorViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;

        // Escape clears the variable filter; second Escape closes the window.
        FilterBox.KeyDown += OnFilterBoxKeyDown;

        Opened += (_, _) => FilterBox.Focus();
    }

    private void OnFilterBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (!string.IsNullOrEmpty(_vm.VariableFilter))
            {
                _vm.VariableFilter = string.Empty;
                e.Handled = true;
            }
            else
            {
                Close();
                e.Handled = true;
            }
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
