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
using System;

namespace XTMF2.GUI.Views;

internal static class FilterBoxSelectionNavigation
{
    public static bool MoveSelection(ListBox listBox, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None ||
            e.Key is not (Key.Up or Key.Down) ||
            listBox.ItemCount == 0)
        {
            return false;
        }

        var selectedIndex = listBox.SelectedIndex;
        if (selectedIndex < 0)
        {
            selectedIndex = 0;
        }
        else
        {
            var offset = e.Key == Key.Down ? 1 : -1;
            selectedIndex = Math.Clamp(selectedIndex + offset, 0, listBox.ItemCount - 1);
        }

        listBox.SelectedIndex = selectedIndex;
        if (listBox.SelectedItem is { } selectedItem)
            listBox.ScrollIntoView(selectedItem);

        e.Handled = true;
        return true;
    }
}
