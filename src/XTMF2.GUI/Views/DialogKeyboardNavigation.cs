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
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using XTMF2.GUI.Controls;

namespace XTMF2.GUI.Views;

internal static class DialogKeyboardNavigation
{
    public static bool TrySwitchPane(
        Window window,
        Control groupPane,
        Control targetPane,
        SearchBox groupSearchBox,
        SearchBox targetSearchBox,
        bool hasSelectedGroup,
        KeyEventArgs e)
    {
        if (e.Key != Key.F6 ||
            e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift))
            return false;

        var focused = TopLevel.GetTopLevel(window)?.FocusManager?.GetFocusedElement() as Visual;
        var isInGroupPane = IsWithin(focused, groupPane);
        var isInTargetPane = IsWithin(focused, targetPane);
        var focusTargetPane = isInGroupPane ||
                              (!isInTargetPane && e.KeyModifiers == KeyModifiers.Shift);

        if (focusTargetPane)
        {
            if (!hasSelectedGroup)
                return false;
            targetSearchBox.FocusSearchBox();
        }
        else
        {
            groupSearchBox.FocusSearchBox();
        }

        e.Handled = true;
        return true;
    }

    public static bool TryNavigateTargetTab(Window window, ListBox listBox, KeyEventArgs e)
    {
        if (e.Key != Key.Tab ||
            e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift))
            return false;

        var focused = TopLevel.GetTopLevel(window)?.FocusManager?.GetFocusedElement() as Visual;
        if (focused is null)
            return false;

        var currentRow = focused.GetVisualAncestors()
            .OfType<ListBoxItem>()
            .FirstOrDefault(row => ReferenceEquals(row.FindAncestorOfType<ListBox>(), listBox));
        if (currentRow is null)
            return false;

        var controls = GetRowControls(currentRow);
        var focusedControl = focused.GetVisualAncestors()
            .Prepend(focused)
            .OfType<Control>()
            .FirstOrDefault(controls.Contains);
        var controlIndex = focusedControl is null ? -1 : controls.IndexOf(focusedControl);
        var items = listBox.Items.ToList();
        var rowIndex = items.IndexOf(currentRow.DataContext);
        if (controlIndex < 0 || rowIndex < 0 || items.Count == 0)
            return false;

        var direction = e.KeyModifiers == KeyModifiers.Shift ? -1 : 1;
        var nextControlIndex = controlIndex + direction;
        if (nextControlIndex >= 0 && nextControlIndex < controls.Count)
        {
            if (!FocusControl(controls[nextControlIndex]))
                return false;
            listBox.SelectedIndex = rowIndex;
            e.Handled = true;
            return true;
        }

        var nextRowIndex = (rowIndex + direction + items.Count) % items.Count;
        var nextItem = items[nextRowIndex];
        if (nextItem is null)
            return false;

        listBox.SelectedIndex = nextRowIndex;
        listBox.ScrollIntoView(nextItem);
        Dispatcher.UIThread.Post(() =>
        {
            var nextRow = listBox.GetVisualDescendants()
                .OfType<ListBoxItem>()
                .FirstOrDefault(row => ReferenceEquals(row.DataContext, nextItem));
            var nextControls = nextRow is null ? [] : GetRowControls(nextRow);
            var targetIndex = direction > 0 ? 0 : nextControls.Count - 1;
            if (targetIndex >= 0 && FocusControl(nextControls[targetIndex]))
                return;
            listBox.Focus();
        }, DispatcherPriority.Render);
        e.Handled = true;
        return true;
    }

    public static bool TryNavigateEntryEditor(TextBox current, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down)
        {
            if (e.KeyModifiers != KeyModifiers.None)
                return false;
        }
        else
        {
            return false;
        }

        var currentRow = current.FindAncestorOfType<ListBoxItem>();
        var listBox = currentRow?.FindAncestorOfType<ListBox>();
        if (currentRow is null || listBox is null)
            return false;

        var currentControls = GetRowControls(currentRow);
        var currentTextBoxes = currentControls.OfType<TextBox>().ToList();
        var textBoxIndex = currentTextBoxes.IndexOf(current);
        var rowIndex = listBox.Items.ToList().IndexOf(currentRow.DataContext);
        if (textBoxIndex < 0 || rowIndex < 0)
            return false;

        var nextRowIndex = rowIndex + (e.Key == Key.Down ? 1 : -1);
        if (nextRowIndex < 0 || nextRowIndex >= listBox.ItemCount)
            return false;

        var nextItem = listBox.Items[nextRowIndex];
        if (nextItem is null)
            return false;
        listBox.SelectedIndex = nextRowIndex;
        listBox.ScrollIntoView(nextItem);
        Dispatcher.UIThread.Post(() =>
        {
            var nextRow = listBox.GetVisualDescendants()
                .OfType<ListBoxItem>()
                .FirstOrDefault(item => ReferenceEquals(item.DataContext, nextItem));
            var nextTextBox = nextRow is null
                ? null
                : GetRowControls(nextRow).OfType<TextBox>().ElementAtOrDefault(textBoxIndex);
            if (nextTextBox is not null && nextTextBox.Focus())
                nextTextBox.SelectAll();
        }, DispatcherPriority.Render);
        return true;
    }

    public static bool FocusFirstEntryEditor(ListBox listBox)
    {
        if (listBox.ItemCount == 0)
            return false;
        if (listBox.SelectedIndex < 0)
            listBox.SelectedIndex = 0;
        if (listBox.SelectedItem is not { } selectedItem)
            return false;

        listBox.ScrollIntoView(selectedItem);
        Dispatcher.UIThread.Post(() =>
        {
            var row = listBox.GetVisualDescendants()
                .OfType<ListBoxItem>()
                .FirstOrDefault(item => ReferenceEquals(item.DataContext, selectedItem));
            var editor = row is null
                ? null
                : GetRowControls(row).FirstOrDefault(control => control is ComboBox or TextBox);
            if (editor is TextBox textBox && textBox.Focus())
                textBox.SelectAll();
            else if (editor is not null)
                editor.Focus();
            else
                listBox.Focus();
        }, DispatcherPriority.Render);
        return true;
    }

    private static bool IsWithin(Visual? element, Visual ancestor) =>
        element is not null &&
        (ReferenceEquals(element, ancestor) ||
         element.GetVisualAncestors().Any(parent => ReferenceEquals(parent, ancestor)));

    private static System.Collections.Generic.List<Control> GetRowControls(ListBoxItem row)
    {
        var rowGrid = row.GetVisualDescendants()
            .OfType<Grid>()
            .FirstOrDefault(grid =>
                ReferenceEquals(grid.DataContext, row.DataContext) &&
                grid.Children.Any(IsEntryControl));

        return rowGrid?.Children
            .OfType<Control>()
            .Where(control => IsEntryControl(control) && control.IsVisible && control.IsEnabled)
            .ToList() ?? [];
    }

    private static bool IsEntryControl(Control control) =>
        control is CheckBox or ComboBox or TextBox or Button;

    private static bool FocusControl(Control control)
    {
        if (!control.Focus())
            return false;
        if (control is TextBox textBox)
            textBox.SelectAll();
        return true;
    }
}
