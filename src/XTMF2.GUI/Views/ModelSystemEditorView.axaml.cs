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
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using XTMF2.GUI.ViewModels;

namespace XTMF2.GUI.Views;

public partial class ModelSystemEditorView : UserControl
{
    private ModelSystemEditorViewModel? _vm;

    public ModelSystemEditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;

        // F2 anywhere in this view fires inline rename on the canvas.
        KeyDown += OnViewKeyDown;

        // Enter / dropdown-closed in the node search box navigates the canvas.
        NodeSearchBox.KeyDown += OnNodeSearchBoxKeyDown;
        NodeSearchBox.DropDownClosed += OnNodeSearchBoxDropDownClosed;

        // Boundary navigation dropdown.
        BoundaryNavComboBox.SelectionChanged += OnBoundaryNavSelectionChanged;

        // Track theme changes so the BoxShadow style class stays in sync.
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        UpdateThemeClass();
    }

    private void UpdateThemeClass()
    {
        var isLight = ActualThemeVariant == ThemeVariant.Light;
        if (isLight)
            Classes.Add("light-mode");
        else
            Classes.Remove("light-mode");
        DockBar.BoxShadow = BoxShadows.Parse(isLight
            ? "0 2 10 2 #500066CC, 0 4 20 0 #28000088"
            : "0 0 14 3 #8000D4FF, 0 0 40 10 #4000A0FF, 0 6 24 0 #80000000");
    }

    // -- Keyboard ---------------------------------------------------------------

    private void OnViewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2 && _vm?.SelectedElement is not null)
        {
            if (_vm.SelectedElement is CommentBlockViewModel)
                TheCanvas.BeginCommentEditForSelected();
            else
                TheCanvas.BeginNameEditForSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _vm?.SaveModelSystemCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.E && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            NodeSearchBox.Text = string.Empty;
            NodeSearchBox.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            _vm?.RunModelSystemCommand.Execute(null);
            e.Handled = true;
        }
    }

    // -- Visual-tree / DataContext lifecycle ------------------------------------

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_vm is not null)
            _vm.ParentWindow = TopLevel.GetTopLevel(this) as Window;
        UpdateThemeClass();
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.ScrollToElementRequested -= OnScrollToElementRequested;
        }

        _vm = DataContext as ModelSystemEditorViewModel;

        if (_vm is not null)
        {
            _vm.ParentWindow = TopLevel.GetTopLevel(this) as Window;
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.ScrollToElementRequested += OnScrollToElementRequested;
        }
    }

    private void OnScrollToElementRequested(ICanvasElement element)
    {
        var viewport = CanvasScrollViewer.Viewport;
        var offsetX = element.X + element.Width / 2.0 - viewport.Width / 2.0;
        var offsetY = element.Y + element.Height / 2.0 - viewport.Height / 2.0;
        CanvasScrollViewer.Offset = new Vector(
            Math.Max(0, offsetX),
            Math.Max(0, offsetY));
        TheCanvas.Focus();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e) { }

    // -- Node search box --------------------------------------------------------

    private bool _suppressNextDropDownClose;

    private void OnNodeSearchBoxDropDownClosed(object? sender, EventArgs e)
    {
        if (_suppressNextDropDownClose) { _suppressNextDropDownClose = false; return; }
        if (NodeSearchBox.SelectedItem is ICanvasElement element && _vm is not null)
            _vm.CanvasSearchSelection = element;
    }

    private void OnNodeSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _vm is null) return;

        var element = NodeSearchBox.SelectedItem as ICanvasElement
                      ?? _vm.SearchItems.FirstOrDefault(n =>
                             n.Name.Contains(NodeSearchBox.Text ?? string.Empty,
                                             StringComparison.OrdinalIgnoreCase));

        _suppressNextDropDownClose = true;

        if (element is not null)
            _vm.CanvasSearchSelection = element;
        else
            TheCanvas.Focus();

        e.Handled = true;
    }

    // -- Boundary navigation dropdown -------------------------------------------

    private void OnBoundaryNavSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox cb) return;
        if (cb.SelectedItem is not BoundaryNavigationItem item)
        {
            cb.SelectedIndex = -1;
            return;
        }

        cb.SelectedIndex = -1;

        if (item.IsBrowse)
            _vm?.BrowseBoundariesCommand.Execute(null);
        else if (item.Boundary is { } boundary)
            _vm?.SwitchToBoundary(boundary);
    }

    // -- Floating dock: Variables button ----------------------------------------

    private ModelSystemVariablesDialog? _variablesDialog;

    private void OnShowVariablesClick(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;

        if (_variablesDialog is null || !_variablesDialog.IsVisible)
        {
            _variablesDialog = new ModelSystemVariablesDialog(_vm);

            var owner = TopLevel.GetTopLevel(this) as Window;
            if (owner is not null)
                _variablesDialog.Show(owner);
            else
                _variablesDialog.Show();

            _variablesDialog.Closed += (_, _) => _variablesDialog = null;
        }
        else
        {
            _variablesDialog.Activate();
        }
    }
}
