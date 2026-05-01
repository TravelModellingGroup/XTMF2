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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using XTMF2.GUI.ViewModels;

namespace XTMF2.GUI.Views;

/// <summary>
/// Code-behind for the model system diff view.
/// F5 is handled via a tunnel handler so it always fires regardless of which child control is focused.
/// The filter box is focused automatically whenever the view receives keyboard focus.
/// </summary>
public partial class ModelSystemDiffView : UserControl
{
    public ModelSystemDiffView()
    {
        InitializeComponent();

        AttachedToVisualTree += OnAttachedToVisualTree;

        // Tunnel fires before any focused child sees the event, so F5 works
        // whether the TreeView, a toolbar button, or nothing in particular is focused.
        AddHandler(KeyDownEvent, OnKeyDownTunnel, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        // Try to find the MainWindow when attached to the visual tree
        Dispatcher.UIThread.Post(() => FilterTextBox.FocusSearchBox(), DispatcherPriority.Input);
    }

    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ViewModels.ModelSystemDiffViewModel vm) return;
        if (e.Key == Key.F5)
        {
            vm.RefreshCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && !string.IsNullOrEmpty(vm.FilterText))
        {
            vm.FilterText = null;
            e.Handled = true;
        }
    }

    /// <summary>
    /// Builds a context menu for the right-clicked diff tree item.
    /// Walks up the visual tree from the event source to find a
    /// <see cref="TreeViewItem"/> whose DataContext is a known diff VM type,
    /// then presents "Go To in Base" / "Go To in Compare" menu items.
    /// </summary>
    private void OnDiffTreeContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not ModelSystemDiffViewModel diffVm) return;

        // Walk up from the event source to find the nearest TreeViewItem.
        var source = e.Source as Visual;
        TreeViewItem? treeItem = null;
        var current = source;
        while (current is not null)
        {
            if (current is TreeViewItem tvi) { treeItem = tvi; break; }
            current = current.GetVisualParent();
        }
        if (treeItem is null) return;

        var item = treeItem.DataContext;
        bool existsInLeft  = item is DiffBoundaryItem b1        ? b1.ExistsInLeft
                           : item is DiffFunctionTemplateItem f1 ? f1.ExistsInLeft
                           : item is DiffElementItem el1         ? el1.ExistsInLeft
                           : false;
        bool existsInRight = item is DiffBoundaryItem b2        ? b2.ExistsInRight
                           : item is DiffFunctionTemplateItem f2 ? f2.ExistsInRight
                           : item is DiffElementItem el2         ? el2.ExistsInRight
                           : false;

        if (!existsInLeft && !existsInRight) return;

        var menu = new ContextMenu();

        var goToBase = new MenuItem { Header = "Go To in Base" };
        goToBase.IsEnabled = existsInLeft && diffVm.LeftCanNavigate;
        goToBase.Click += (_, _) => diffVm.GoToInBaseCommand.Execute(item);
        menu.Items.Add(goToBase);

        var goToCompare = new MenuItem { Header = "Go To in Compare" };
        goToCompare.IsEnabled = existsInRight && diffVm.RightCanNavigate;
        goToCompare.Click += (_, _) => diffVm.GoToInCompareCommand.Execute(item);
        menu.Items.Add(goToCompare);

        menu.Open(treeItem);
        e.Handled = true;
    }
}
