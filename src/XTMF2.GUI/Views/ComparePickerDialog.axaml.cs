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
using Avalonia.Platform.Storage;
using System.Collections.Generic;
using System.ComponentModel;
using XTMF2;

namespace XTMF2.GUI.Views;

/// <summary>
/// Dialog that lets the user pick a second model system for diff comparison.
/// The user may select another model system from the same project (<see cref="SelectedModelSystem"/>)
/// or browse for an exported <c>.xmsf</c> file (<see cref="SelectedFilePath"/>).
/// </summary>
public partial class ComparePickerDialog : Window, INotifyPropertyChanged
{
    private ModelSystemHeader? _selectedModelSystem;

    /// <summary>The other model systems in the project (the current one is excluded).</summary>
    public IReadOnlyList<ModelSystemHeader> OtherModelSystems { get; }

    // Required for AXAML compilation.
    public ComparePickerDialog()
    {
        OtherModelSystems = [];
        InitializeComponent();
        DataContext = this;
    }

    /// <summary>The model system header selected from the list, or null.</summary>
    public ModelSystemHeader? SelectedModelSystem
    {
        get => _selectedModelSystem;
        set
        {
            _selectedModelSystem = value;
            // Clear file selection when a project MS is picked.
            if (value is not null)
                SetSelectedFile(null);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedModelSystem)));
        }
    }

    /// <summary>The path to the exported file selected via Browse, or null.</summary>
    public string? SelectedFilePath { get; private set; }

    /// <summary>True if the user confirmed the selection; false if cancelled.</summary>
    public bool WasCancelled { get; private set; } = true;

    public new event PropertyChangedEventHandler? PropertyChanged;

    public ComparePickerDialog(IReadOnlyList<ModelSystemHeader> otherModelSystems)
    {
        OtherModelSystems = otherModelSystems;
        InitializeComponent();
        DataContext = this;
    }

    private async void BrowseFile_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Exported Model System",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Exported Model System (*.xmsys)") { Patterns = ["*.xmsys"] },
                new FilePickerFileType("All Files") { Patterns = ["*"] }
            ]
        });

        if (files.Count == 0) return;

        var path = files[0].TryGetLocalPath();
        if (path is null) return;

        // Clear the list selection when a file is chosen.
        _selectedModelSystem = null;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedModelSystem)));

        SetSelectedFile(path);
    }

    private void SetSelectedFile(string? path)
    {
        SelectedFilePath = path;
        if (SelectedFileText is not null)
        {
            SelectedFileText.Text = path is not null
                ? System.IO.Path.GetFileName(path)
                : "— or browse for an exported .xmsys file —";
            SelectedFileText.Opacity = path is not null ? 1.0 : 0.55;
        }
    }

    private void Compare_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedModelSystem is null && SelectedFilePath is null)
            return; // nothing selected — ignore

        WasCancelled = false;
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        WasCancelled = true;
        Close();
    }
}
