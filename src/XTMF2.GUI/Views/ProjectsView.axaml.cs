/*
    Copyright 2019 University of Toronto

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
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using XTMF2.GUI.ViewModels;
using XTMF2.GUI.Resources;

namespace XTMF2.GUI.Views;

public partial class ProjectsView : UserControl
{
    private ProjectsViewModel? _viewModel;
    private MainWindow? _parentWindow;

    public ProjectsView()
    {
        InitializeComponent();
        
        // Subscribe to language changes
        LocalizationManager.LanguageChanged += OnLanguageChanged;
        
        // Subscribe to DataContext changes for automatic initialization
        DataContextChanged += OnDataContextChanged;
        
        // Subscribe to visual tree attachment to find MainWindow
        AttachedToVisualTree += OnAttachedToVisualTree;
        KeyDown += OnViewKeyDown;
        ProjectListBox.KeyDown += OnProjectListBoxKeyDown;
        ProjectSearchBox.KeyDown += OnProjectSearchBoxKeyDown;
    }


    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        // Try to find the MainWindow when attached to the visual tree
        _parentWindow = FindParentWindow();
        SwitchFocusToSearchBox();
    }

    private void SwitchFocusToSearchBox()
    {
        Dispatcher.UIThread.Post(() => ProjectSearchBox.FocusSearchBox(), DispatcherPriority.Input);
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is ProjectsViewModel viewModel)
        {
            _viewModel = viewModel;
            if (_parentWindow == null)
            {
                _parentWindow = FindParentWindow();
            }
            UpdateLocalizedText();
        }
    }

    /// <summary>
    /// Find the MainWindow via TopLevel (works inside Dock panels)
    /// </summary>
    private MainWindow? FindParentWindow()
    {
        return TopLevel.GetTopLevel(this) as MainWindow;
    }

    /// <summary>
    /// Initialize the view with a view model
    /// </summary>
    /// <param name="viewModel">The view model to bind to</param>
    /// <param name="mainWindow">Reference to the main window for opening project tabs</param>
    public void Initialize(ProjectsViewModel viewModel, MainWindow mainWindow)
    {
        _viewModel = viewModel;
        _parentWindow = mainWindow;
        DataContext = viewModel;
        
        UpdateLocalizedText();
    }

    private void OnLanguageChanged(object? sender, System.EventArgs e)
    {
        UpdateLocalizedText();
    }

    private void UpdateLocalizedText()
    {
        // Set localized user text
        if (_viewModel?.CurrentUser is not null)
        {
            UserTextBlock.Text = Strings.Format(Strings.Projects_UserFormat, _viewModel.CurrentUser.UserName);
        }

        ToolTip.SetTip(NewProjectButton, $"{Strings.Projects_NewProject} (Ctrl+N)");
        ToolTip.SetTip(ImportProjectButton, $"{Strings.Projects_Import} (Ctrl+Shift+O)");
        ToolTip.SetTip(ProjectSearchBox, "Search (Ctrl+E)");
    }

    private void OnViewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Control)
        {
            if (e.Key == Key.N)
            {
                NewProject_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.E)
            {
                ProjectSearchBox.FocusSearchBox();
                e.Handled = true;
            }
        }
        else if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.O)
        {
            ImportProject_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private async void OnProjectListBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel?.SelectedProject is not { } project)
        {
            return;
        }

        if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.Enter)
        {
            if (_parentWindow is { } parentWindow)
                await parentWindow.OpenProjectTab(project);
            e.Handled = true;
        }
        else if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.F2)
        {
            await RenameProjectAsync(project);
            e.Handled = true;
        }
        else if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.Delete)
        {
            await DeleteProjectAsync(project);
            e.Handled = true;
        }
        else if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.E)
        {
            await ExportProjectAsync(project);
            e.Handled = true;
        }
    }

    private void OnProjectSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is null || e.KeyModifiers != KeyModifiers.None ||
            e.Key is not (Key.Up or Key.Down))
        {
            return;
        }

        var projects = _viewModel.FilteredProjects;
        if (projects.Count == 0)
        {
            return;
        }

        var selectedIndex = ProjectListBox.SelectedIndex;
        if (selectedIndex < 0)
        {
            selectedIndex = 0;
        }
        else
        {
            var offset = e.Key == Key.Down ? 1 : -1;
            selectedIndex = System.Math.Clamp(selectedIndex + offset, 0, projects.Count - 1);
        }

        var project = projects[selectedIndex];
        _viewModel.SelectedProject = project;
        ProjectListBox.ScrollIntoView(project);
        e.Handled = true;
    }

    private async void NewProject_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.CurrentUser == null || _parentWindow == null)
        {
            return;
        }

        // Prompt for project name
        var dialog = new InputDialog(
            Strings.Projects_CreateTitle,
            Strings.Projects_CreatePrompt,
            "");
        
        await dialog.ShowDialog(_parentWindow);

        if (dialog.WasCancelled || string.IsNullOrWhiteSpace(dialog.InputText))
        {
            return;
        }

        // Create the new project
        if (_viewModel.Runtime.ProjectController.CreateNewProject(
            _viewModel.CurrentUser,
            dialog.InputText,
            out var session,
            out var error))
        {
            // Success - dispose the session since we're not editing it yet
            session?.Dispose();
            // The observable collection will automatically update with the new project
        }
        else
        {
            // Show error dialog
            var errorDialog = new ConfirmDialog(
                Strings.Projects_CreateError,
                error?.Message ?? Strings.ModelSystems_UnknownError);
            await errorDialog.ShowDialog(_parentWindow);
        }
    }

    private async void ImportProject_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.CurrentUser == null || _parentWindow == null) return;

        // Pick the project file
        var files = await _parentWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.Projects_ImportTitle,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Strings.Projects_ImportTitle) { Patterns = ["*.xprj"] },
                new FilePickerFileType("All Files") { Patterns = ["*"] }
            ]
        });

        if (files.Count == 0) return;

        var filePath = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(filePath)) return;

        // Prompt for project name
        var suggestedName = Path.GetFileNameWithoutExtension(filePath);
        var nameDialog = new InputDialog(
            Strings.Projects_ImportTitle,
            Strings.Projects_ImportNamePrompt,
            suggestedName);
        await nameDialog.ShowDialog(_parentWindow);

        if (nameDialog.WasCancelled || string.IsNullOrWhiteSpace(nameDialog.InputText)) return;

        // Import the project
        if (_viewModel.Runtime.ProjectController.ImportProjectFile(
            _viewModel.CurrentUser,
            nameDialog.InputText,
            filePath,
            out var session,
            out var error))
        {
            // Success - dispose the session since we're not editing it immediately
            session?.Dispose();
            // The observable collection will automatically update
        }
        else
        {
            var errorDialog = new ConfirmDialog(
                Strings.Projects_ImportError,
                error?.Message ?? Strings.ModelSystems_UnknownError);
            await errorDialog.ShowDialog(_parentWindow);
        }
    }

    private async void ExportProject_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.DataContext is not Project project)
            return;

        await ExportProjectAsync(project);
    }

    private async Task ExportProjectAsync(Project project)
    {
        if (_viewModel?.CurrentUser == null || _parentWindow == null)
            return;

        // Pick where to save the exported project file
        var file = await _parentWindow.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = Strings.Projects_ExportTitle,
            SuggestedFileName = project.Name ?? "project",
            DefaultExtension = "xprj",
            FileTypeChoices =
            [
                new Avalonia.Platform.Storage.FilePickerFileType(Strings.Projects_ExportTitle) { Patterns = ["*.xprj"] },
                new Avalonia.Platform.Storage.FilePickerFileType("All Files") { Patterns = ["*"] }
            ]
        });

        if (file is null) return;

        var exportPath = file.TryGetLocalPath();
        if (string.IsNullOrEmpty(exportPath)) return;

        // We need a project session to call ExportProject
        if (!_viewModel.Runtime.ProjectController.GetProjectSession(
            _viewModel.CurrentUser, project, out var session, out var sessionError))
        {
            var errorDialog = new ConfirmDialog(
                Strings.Projects_ExportError,
                sessionError?.Message ?? Strings.ModelSystems_UnknownError);
            await errorDialog.ShowDialog(_parentWindow);
            return;
        }

        using (session)
        {
            if (!session.ExportProject(_viewModel.CurrentUser, exportPath, out var exportError))
            {
                var errorDialog = new ConfirmDialog(
                    Strings.Projects_ExportError,
                    exportError?.Message ?? Strings.ModelSystems_UnknownError);
                await errorDialog.ShowDialog(_parentWindow);
            }
        }
    }

    private async void Border_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (sender is Border border && border.DataContext is Project project)
        {
            if (_parentWindow is { } parentWindow)
                await parentWindow.OpenProjectTab(project);
        }
    }

    private async void OpenProject_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is Project project)
        {
            if (_parentWindow is { } parentWindow)
                await parentWindow.OpenProjectTab(project);
        }
    }

    private async void RenameProject_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.DataContext is not Project project)
            return;

        await RenameProjectAsync(project);
    }

    private async Task RenameProjectAsync(Project project)
    {
        if (_viewModel?.CurrentUser == null || _parentWindow == null)
            return;

        var dialog = new InputDialog(
            Strings.Projects_RenameTitle,
            Strings.Projects_CreatePrompt,
            project.Name ?? "");

        await dialog.ShowDialog(_parentWindow);

        if (dialog.WasCancelled || string.IsNullOrWhiteSpace(dialog.InputText) || dialog.InputText == project.Name)
            return;

        if (!_viewModel.Runtime.ProjectController.RenameProject(
            _viewModel.CurrentUser,
            project,
            dialog.InputText,
            out var error))
        {
            var errorDialog = new ConfirmDialog(
                Strings.Projects_RenameFailedTitle,
                error?.Message ?? Strings.ModelSystems_UnknownError);
            await errorDialog.ShowDialog(_parentWindow);
        }
    }

    private async void SearchBox_EnterPressed(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var project = _viewModel?.SelectedProject ?? _viewModel?.FilteredProjects.FirstOrDefault();
        if (project is not null)
        {
            if (_parentWindow is { } parentWindow)
                await parentWindow.OpenProjectTab(project);
        }
    }

    private async void DeleteProject_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.DataContext is not Project project)
            return;

        await DeleteProjectAsync(project);
    }

    private async Task DeleteProjectAsync(Project project)
    {
        if (_viewModel?.CurrentUser == null || _parentWindow == null)
        {
            return;
        }

        // Confirm deletion
        var confirmDialog = new ConfirmDialog(
            Strings.Projects_DeleteConfirmTitle,
            Strings.Format(Strings.Projects_DeleteConfirmMessage, project.Name));
        
        await confirmDialog.ShowDialog(_parentWindow);

        if (!confirmDialog.Result)
        {
            return;
        }

        // Attempt to delete the project
        if (_viewModel.Runtime.ProjectController.DeleteProject(
            _viewModel.CurrentUser,
            project,
            out var error))
        {
            // Success - check if the project tab is open and close it
            _parentWindow.CloseProjectTab(project);
            // The observable collection will automatically update
        }
        else
        {
            // Show error dialog
            var errorDialog = new ConfirmDialog(
                Strings.Projects_DeleteFailedTitle,
                error?.Message ?? Strings.ModelSystems_UnknownError);
            await errorDialog.ShowDialog(_parentWindow);
        }
    }
}
