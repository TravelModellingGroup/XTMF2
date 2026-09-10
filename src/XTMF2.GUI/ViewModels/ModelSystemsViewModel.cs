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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XTMF2;
using XTMF2.Controllers;
using XTMF2.Editing;
using XTMF2.GUI.Views;
using XTMF2.GUI.Resources;

namespace XTMF2.GUI.ViewModels;

public partial class ModelSystemsViewModel : ObservableObject, IDisposable
{
    private bool _disposed;
    private readonly XTMFRuntime _runtime;
    private readonly User _user;
    private readonly Project _project;
    private readonly ProjectSession _session;
    private Window? _parentWindow;

    // Used by Dock ItemsSource for the tab title and close behaviour
    public string Title => $"⊟  {_project.Name ?? "Untitled Project"}";
    public bool CanClose => true;

    /// <summary>Gets the project this view model is for.</summary>
    public Project Project => _project;

    /// <summary>The currently logged-in user.</summary>
    public User CurrentUser => _user;

    [ObservableProperty]
    private ModelSystemHeader? _selectedModelSystem;

    [ObservableProperty]
    private string _searchText = "";

    /// <summary>
    /// Model systems filtered by <see cref="SearchText"/>.
    /// </summary>
    public ObservableCollection<ModelSystemHeader> FilteredModelSystems { get; } = new();

    public ModelSystemsViewModel(XTMFRuntime runtime, User user, Project project, Window? parentWindow = null)
    {
        _runtime = runtime;
        _user = user;
        _project = project;
        _parentWindow = parentWindow;
        
        // Get the project session
        if (!runtime.ProjectController.GetProjectSession(user, project, out var session, out var error))
        {
            throw new InvalidOperationException($"Failed to get project session: {error?.Message}");
        }
        _session = session;

        // Keep FilteredModelSystems in sync with the underlying collection
        ((INotifyCollectionChanged)_project.ModelSystems).CollectionChanged += (_, _) => RebuildFilteredModelSystems();
        RebuildFilteredModelSystems();
    }

    partial void OnSearchTextChanged(string value) => RebuildFilteredModelSystems();

    private void RebuildFilteredModelSystems()
    {
        FilteredModelSystems.Clear();
        var filter = SearchText.Trim();
        foreach (var ms in _project.ModelSystems)
        {
            if (string.IsNullOrEmpty(filter) ||
                (ms.Name?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                FilteredModelSystems.Add(ms);
            }
        }
    }

    /// <summary>
    /// Gets the observable collection of model systems in the project
    /// </summary>
    public ReadOnlyObservableCollection<ModelSystemHeader> ModelSystems => _project.ModelSystems;

    /// <summary>
    /// Gets the project name
    /// </summary>
    public string ProjectName => _project.Name ?? Strings.ModelSystems_UnknownProject;

    [RelayCommand]
    private async Task CreateNewModelSystem()
    {
        var modelSystemName = await PromptForModelSystemName(Strings.ModelSystems_CreateTitle, "");
        if (string.IsNullOrWhiteSpace(modelSystemName))
        {
            return;
        }

        if (_session.CreateNewModelSystem(_user, modelSystemName, out var modelSystem, out var error))
        {
            // Success - the observable collection will automatically update
            SelectedModelSystem = modelSystem;
        }
        else
        {
            // Show error
            await ShowError(Strings.ModelSystems_CreateFailedTitle, error?.Message ?? Strings.ModelSystems_UnknownError);
        }
    }

    [RelayCommand]
    private async Task ImportModelSystem()
    {
        if (_parentWindow is null) return;

        var files = await _parentWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.ModelSystems_ImportTitle,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Strings.ModelSystems_ImportTitle) { Patterns = ["*.xmsys"] },
                new FilePickerFileType("All Files") { Patterns = ["*"] }
            ]
        });

        if (files.Count == 0) return;

        var filePath = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(filePath)) return;

        var suggestedName = Path.GetFileNameWithoutExtension(filePath);
        var name = await PromptForModelSystemName(Strings.ModelSystems_ImportTitle, suggestedName);
        if (string.IsNullOrWhiteSpace(name)) return;

        if (_session.ImportModelSystem(_user, filePath, name, out _, out var error))
        {
            // Success - the observable collection will automatically update
        }
        else
        {
            await ShowError(Strings.ModelSystems_ImportError, error?.Message ?? Strings.ModelSystems_UnknownError);
        }
    }

    [RelayCommand]
    private async Task OpenProjectDirectory()
    {
        var projectDirectory = _project.ProjectDirectory;
        if (string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory))
        {
            await ShowError(
                Strings.ModelSystems_OpenProjectDirectoryFailedTitle,
                Strings.Format(Strings.ModelSystems_OpenProjectDirectoryFailedMessage, projectDirectory ?? "(null)"));
            return;
        }

        if (TryOpenDirectoryInFileExplorer(projectDirectory))
        {
            return;
        }

        await ShowError(
            Strings.ModelSystems_OpenProjectDirectoryFailedTitle,
            Strings.Format(Strings.ModelSystems_OpenProjectDirectoryFailedMessage, projectDirectory));
    }

    private static bool TryOpenDirectoryInFileExplorer(string directoryPath)
    {
        try
        {
            ProcessStartInfo startInfo;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                startInfo = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    UseShellExecute = true
                };
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                startInfo = new ProcessStartInfo
                {
                    FileName = "open",
                    UseShellExecute = false
                };
            }
            else
            {
                startInfo = new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    UseShellExecute = false
                };
            }

            startInfo.ArgumentList.Add(directoryPath);
            return Process.Start(startInfo) is not null;
        }
        catch
        {
            return false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRenameModelSystem))]
    private async Task RenameModelSystem()
    {
        if (SelectedModelSystem == null)
        {
            return;
        }

        var newName = await PromptForModelSystemName(Strings.ModelSystems_RenameTitle, SelectedModelSystem.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == SelectedModelSystem.Name)
        {
            return;
        }

        if (RenameSelectedModelSystem(newName, out var error))
        {
            // Success
        }
        else
        {
            // Show error
            await ShowError(Strings.ModelSystems_RenameFailedTitle, error?.Message ?? Strings.ModelSystems_UnknownError);
        }
    }

    internal bool RenameSelectedModelSystem(string newName, out CommandError? error)
    {
        if (SelectedModelSystem is null)
        {
            error = new CommandError("No model system is selected.");
            return false;
        }

        if (!_session.RenameModelSystem(_user, SelectedModelSystem, newName, out error))
        {
            return false;
        }

        return _session.Save(out error);
    }

    private bool CanRenameModelSystem() => SelectedModelSystem != null;

    [RelayCommand(CanExecute = nameof(CanDeleteModelSystem))]
    private async Task DeleteModelSystem()
    {
        if (SelectedModelSystem == null)
        {
            return;
        }

        var confirm = await ConfirmDelete(SelectedModelSystem.Name);
        if (!confirm)
        {
            return;
        }

        if (_session.RemoveModelSystem(_user, SelectedModelSystem, out var error))
        {
            // Success - the observable collection will automatically update
            SelectedModelSystem = null;
        }
        else
        {
            // Show error
            await ShowError(Strings.ModelSystems_DeleteFailedTitle, error?.Message ?? Strings.ModelSystems_UnknownError);
        }
    }

    private bool CanDeleteModelSystem() => SelectedModelSystem != null;

    [RelayCommand(CanExecute = nameof(CanExportModelSystem))]
    private async Task ExportModelSystem()
    {
        if (SelectedModelSystem is null || _parentWindow is null) return;

        var file = await _parentWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.ModelSystems_ExportTitle,
            SuggestedFileName = SelectedModelSystem.Name ?? "model-system",
            DefaultExtension = "xmsys",
            FileTypeChoices =
            [
                new FilePickerFileType(Strings.ModelSystems_ExportTitle) { Patterns = ["*.xmsys"] },
                new FilePickerFileType("All Files") { Patterns = ["*"] }
            ]
        });

        if (file is null) return;

        var exportPath = file.TryGetLocalPath();
        if (string.IsNullOrEmpty(exportPath)) return;

        if (!_session.ExportModelSystem(_user, SelectedModelSystem, exportPath, out var error))
        {
            await ShowError(Strings.ModelSystems_ExportError, error?.Message ?? Strings.ModelSystems_UnknownError);
        }
    }

    private bool CanExportModelSystem() => SelectedModelSystem != null;

    /// <summary>
    /// Attempts to open an editing session for the given model system header.
    /// </summary>
    /// <param name="header">The model system to edit.</param>
    /// <param name="session">The new session, or <see langword="null"/> on failure.</param>
    /// <param name="error">Error information on failure.</param>
    /// <param name="warnings">Non-fatal warnings generated while loading the model system.</param>
    /// <returns><see langword="true"/> if the session was created successfully.</returns>
    public bool TryEditModelSystem(ModelSystemHeader header,
        out ModelSystemSession? session,
        out CommandError? error,
        out List<string>? warnings)
        => _session.EditModelSystem(_user, header, out session, out error, out warnings);

    /// <summary>
    /// Loads a read-only snapshot of a model system for diff comparison.
    /// </summary>
    public bool TryLoadModelSystemSnapshot(ModelSystemHeader header,
        out ModelSystem? ms,
        out CommandError? error)
        => _session.LoadModelSystemForDiff(_user, header, out ms, out error);

    /// <summary>
    /// Loads a read-only snapshot of an exported model system file for diff comparison.
    /// </summary>
    public bool TryLoadModelSystemFromFile(string filePath,
        out ModelSystem? ms,
        out CommandError? error)
        {
            return _session.LoadModelSystemFromExportedFile(_user, filePath, out ms, out error);
        }

    /// <summary>
    /// Returns all model systems in this project except <paramref name="exclude"/>.
    /// </summary>
    public IReadOnlyList<ModelSystemHeader> GetOtherModelSystems(ModelSystemHeader exclude)
    {
        var result = new List<ModelSystemHeader>(_project.ModelSystems.Count);
        foreach (var ms in _project.ModelSystems)
        {
            if (!ReferenceEquals(ms, exclude))
                result.Add(ms);
        }
        return result;
    }

    partial void OnSelectedModelSystemChanged(ModelSystemHeader? value)
    {
        // Update command can-execute states
        RenameModelSystemCommand.NotifyCanExecuteChanged();
        DeleteModelSystemCommand.NotifyCanExecuteChanged();
        ExportModelSystemCommand.NotifyCanExecuteChanged();
    }

    // These methods show dialogs to the user
    private async Task<string?> PromptForModelSystemName(string title, string defaultValue)
    {
        if (_parentWindow == null)
        {
            return null;
        }

        var dialog = new InputDialog(title, Strings.ModelSystems_CreatePrompt, defaultValue);
        await dialog.ShowDialog(_parentWindow);
        
        return dialog.WasCancelled ? null : dialog.InputText;
    }

    private async Task ShowError(string title, string message)
    {
        if (_parentWindow == null)
        {
            return;
        }

        var dialog = new ConfirmDialog(title, message);
        await dialog.ShowDialog(_parentWindow);
    }

    private async Task<bool> ConfirmDelete(string modelSystemName)
    {
        if (_parentWindow == null)
        {
            return false;
        }

        var dialog = new ConfirmDialog(
            Strings.ModelSystems_DeleteConfirmTitle,
            Strings.Format(Strings.ModelSystems_DeleteConfirmMessage, modelSystemName));
        await dialog.ShowDialog(_parentWindow);
        
        return dialog.Result;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.Dispose();
    }
}
