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
            return;
        }

        if (e.KeyModifiers != KeyModifiers.None ||
            e.Key is not (Key.Up or Key.Down))
            return;

        var localCount = LocalVariableListBox.ItemCount;
        var globalCount = GlobalVariableListBox.ItemCount;
        if (localCount == 0 && globalCount == 0)
            return;

        var localIndex = LocalVariableListBox.SelectedIndex;
        var globalIndex = GlobalVariableListBox.SelectedIndex;
        if (localIndex >= 0)
        {
            if (e.Key == Key.Down && localIndex == localCount - 1 && globalCount > 0)
                SelectVariable(GlobalVariableListBox, 0, LocalVariableListBox);
            else
                SelectVariable(LocalVariableListBox, MoveIndex(localIndex, localCount, e.Key), GlobalVariableListBox);
        }
        else if (globalIndex >= 0)
        {
            if (e.Key == Key.Up && globalIndex == 0 && localCount > 0)
                SelectVariable(LocalVariableListBox, localCount - 1, GlobalVariableListBox);
            else
                SelectVariable(GlobalVariableListBox, MoveIndex(globalIndex, globalCount, e.Key), LocalVariableListBox);
        }
        else if (localCount > 0)
        {
            SelectVariable(LocalVariableListBox, 0, GlobalVariableListBox);
        }
        else
        {
            SelectVariable(GlobalVariableListBox, 0, LocalVariableListBox);
        }

        e.Handled = true;
    }

    private static int MoveIndex(int index, int count, Key key)
    {
        var offset = key == Key.Down ? 1 : -1;
        return System.Math.Clamp(index + offset, 0, count - 1);
    }

    private static void SelectVariable(ListBox target, int index, ListBox other)
    {
        other.SelectedIndex = -1;
        target.SelectedIndex = index;
        if (target.SelectedItem is { } selectedItem)
            target.ScrollIntoView(selectedItem);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
