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
using System;
using System.Linq;
using XTMF2;
using XTMF2.Diff;
using XTMF2.Editing;
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

        if (!_viewModel.TryEditModelSystem(header, out var session, out var error, out var warnings) || session is null)
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

        if (warnings is { Count: > 0 })
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                var dialog = new ValidationIssueDialog(
                    Strings.ModelSystems_OpenFailedTitle,
                    string.Join(Environment.NewLine, warnings));
                await dialog.ShowDialog(mainWindow);
                if (!dialog.Result)
                {
                    session.Dispose();
                    return;
                }

                mainWindow.OpenModelSystemTab(session, _viewModel.CurrentUser);
            });
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

    private void CompareModelSystem_ContextMenu_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        ModelSystemHeader? header = null;
        if (sender is MenuItem menuItem && menuItem.DataContext is ModelSystemHeader h)
            header = h;
        header ??= _viewModel.SelectedModelSystem;
        if (header is null) return;

        Dispatcher.UIThread.Post(async () =>
        {
            var mainWindow = TopLevel.GetTopLevel(this) as MainWindow;
            if (mainWindow is null) return;

            // Build the list of other model systems in this project.
            var others = _viewModel.GetOtherModelSystems(header);

            var picker = new ComparePickerDialog(others);
            await picker.ShowDialog(mainWindow);

            if (picker.WasCancelled) return;

            // Capture values needed by the factory (picker is disposed after this method returns).
            var capturedViewModel = _viewModel;
            var capturedHeader = header;
            var capturedRightHeader = picker.SelectedModelSystem;
            var capturedRightFilePath = picker.SelectedFilePath;

            // Open the tab immediately; the diff is computed on a background thread.
            // Errors from loading are displayed inside the diff tab itself.
            mainWindow.OpenDiffTab(
                diffFactory: isSwapped =>
                {
                    if (!capturedViewModel.TryLoadModelSystemSnapshot(capturedHeader, out var leftMs, out var leftError))
                        throw new InvalidOperationException(leftError?.Message ?? "Failed to load base model system.");

                    ModelSystem? rightMs;
                    if (capturedRightHeader is not null)
                    {
                        if (!capturedViewModel.TryLoadModelSystemSnapshot(capturedRightHeader, out rightMs, out var rightError))
                            throw new InvalidOperationException(rightError?.Message ?? "Failed to load comparison model system.");
                    }
                    else
                    {
                        if (!capturedViewModel.TryLoadModelSystemFromFile(capturedRightFilePath!, out rightMs, out var rightError))
                            throw new InvalidOperationException(rightError?.Message ?? "Failed to load comparison model system from file.");
                    }

                    return isSwapped
                        ? ModelSystemComparer.Compare(rightMs!, leftMs!)
                        : ModelSystemComparer.Compare(leftMs!, rightMs!);
                },
                leftHeader: capturedHeader,
                rightHeader: capturedRightHeader,   // null when loaded from file
                openOrFocusEditor: async header =>
                {
                    // If the editor is already open, focus it.
                    var existing = mainWindow.Documents
                        .OfType<ViewModels.ModelSystemEditorViewModel>()
                        .FirstOrDefault(vm => vm.ModelSystemHeader == header);
                    if (existing is not null)
                    {
                        mainWindow.FocusEditorTab(existing);
                        // Yield to the dispatcher so any layout triggered by the tab
                        // switch completes before the caller calls NavigateToElementById.
                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                            () => { }, Avalonia.Threading.DispatcherPriority.Loaded);
                        return existing;
                    }
                    // Otherwise open a new editing session.
                    if (!capturedViewModel.TryEditModelSystem(header, out var session, out var error, out var warnings))
                        return null;

                    if (warnings is { Count: > 0 })
                    {
                        var warningDialog = new ValidationIssueDialog(
                            Strings.ModelSystems_OpenFailedTitle,
                            string.Join(Environment.NewLine, warnings));
                        await warningDialog.ShowDialog(mainWindow);
                        if (!warningDialog.Result)
                        {
                            session?.Dispose();
                            return null;
                        }
                    }

                    return mainWindow.OpenModelSystemTabAndGet(session!, capturedViewModel.CurrentUser);
                });
        });
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
