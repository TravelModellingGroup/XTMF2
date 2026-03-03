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
using Avalonia.Threading;
using System.Linq;
using XTMF2;
using XTMF2.GUI.ViewModels;
using XTMF2.GUI.Resources;

namespace XTMF2.GUI.Views;

public partial class ModelSystemsView : UserControl
{
    private ModelSystemsViewModel? _viewModel;

    public ModelSystemsView()
    {
        InitializeComponent();
        
        // Subscribe to language changes
        LocalizationManager.LanguageChanged += OnLanguageChanged;
        
        // Subscribe to DataContext changes for automatic initialization
        DataContextChanged += OnDataContextChanged;
        
        // Subscribe to visual tree attachment to find MainWindow
        AttachedToVisualTree += OnAttachedToVisualTree;

        ModelSystemListBox.KeyDown += OnModelSystemListBoxKeyDown;
        ModelSystemSearchBox.EnterPressed += OnModelSystemSearchBoxEnterPressed;
    }

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        // Try to find the MainWindow when attached to the visual tree
        SwitchFocusToSearchBox();
    }

    private void SwitchFocusToSearchBox()
    {
        Dispatcher.UIThread.Post(() => ModelSystemSearchBox.FocusSearchBox(), DispatcherPriority.Input);
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is ModelSystemsViewModel viewModel)
        {
            _viewModel = viewModel;
            UpdateLocalizedText();
        }
    }

    /// <summary>
    /// Initialize the view with a view model
    /// </summary>
    /// <param name="viewModel">The view model to bind to</param>
    public void Initialize(ModelSystemsViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        
        UpdateLocalizedText();
    }

    private void ModelSystem_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (sender is Border border && border.DataContext is ModelSystemHeader header)
            OpenModelSystem(header);
    }

    private void OnModelSystemListBoxKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key != Avalonia.Input.Key.Enter) return;
        if (_viewModel?.SelectedModelSystem is { } header)
        {
            OpenModelSystem(header);
            e.Handled = true;
        }
    }

    private void OnModelSystemSearchBoxEnterPressed(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var first = _viewModel?.FilteredModelSystems.FirstOrDefault();
        if (first is not null)
            OpenModelSystem(first);
    }

    private void OpenModelSystem_ContextMenu_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is ModelSystemHeader header)
            OpenModelSystem(header);
    }

    private void OpenModelSystem(ModelSystemHeader header)
    {
        var mainWindow = TopLevel.GetTopLevel(this) as MainWindow;
        if (mainWindow is null || _viewModel is null) return;

        if (!_viewModel.TryEditModelSystem(header, out var session, out var error) || session is null)
        {
            // Show an error dialog if we have a parent window
            if (mainWindow is not null)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                {
                    var dialog = new ConfirmDialog(
                        Strings.ModelSystems_OpenFailedTitle,
                        error?.Message ?? Strings.ModelSystems_UnknownError);
                    await dialog.ShowDialog(mainWindow);
                });
            }
            return;
        }

        mainWindow.OpenModelSystemTab(session, _viewModel.CurrentUser);
    }

    private void RenameModelSystem_ContextMenu_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        if (sender is MenuItem menuItem && menuItem.DataContext is ModelSystemHeader header)
            _viewModel.SelectedModelSystem = header;
        _viewModel.RenameModelSystemCommand.Execute(null);
    }

    private void DeleteModelSystem_ContextMenu_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        if (sender is MenuItem menuItem && menuItem.DataContext is ModelSystemHeader header)
            _viewModel.SelectedModelSystem = header;
        _viewModel.DeleteModelSystemCommand.Execute(null);
    }

    private void ExportModelSystem_ContextMenu_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        if (sender is MenuItem menuItem && menuItem.DataContext is ModelSystemHeader header)
            _viewModel.SelectedModelSystem = header;
        _viewModel.ExportModelSystemCommand.Execute(null);
    }

    private void OnLanguageChanged(object? sender, System.EventArgs e)
    {
        UpdateLocalizedText();
    }

    private void UpdateLocalizedText()
    {
        if (_viewModel != null)
        {
            // Set localized formatted strings
            TitleTextBlock.Text = Strings.Format(Strings.ModelSystems_TitleFormat, _viewModel.ProjectName);
            UserTextBlock.Text = Strings.Format(Strings.ModelSystems_UserFormat, _viewModel.CurrentUser.UserName);
        }
    }
}
