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
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using XTMF2.Bus.Optimization;
using XTMF2.GUI.ViewModels;

namespace XTMF2.GUI.Views;

/// <summary>
/// Dialog for editing the hyperparameters of the currently-selected estimation algorithm.
/// Opened from <see cref="EstimationDialog"/> via the "Configure…" button.
/// Parameters are provided dynamically by the algorithm config itself via
/// <see cref="EstimationAlgorithmConfig.GetParameters"/>.
/// </summary>
public partial class EstimationAlgorithmConfigDialog : Window, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private readonly ModelSystemEditorViewModel? _editorVm;
    private readonly EstimationAlgorithmConfig _currentConfig;

    public string DialogTitle => $"Configure {_currentConfig.DisplayName}";

    public string AlgorithmDescription => _currentConfig.AlgorithmDescription;

    public IReadOnlyList<AlgorithmParameterDescriptor> Parameters { get; private set; } = [];

    /// <summary>Required by the Avalonia AXAML compiler (design-time).</summary>
    public EstimationAlgorithmConfigDialog()
    {
        _currentConfig = EstimationAlgorithmConfig.Default;
        DataContext = this;
        InitializeComponent();
    }

    public EstimationAlgorithmConfigDialog(
        ModelSystemEditorViewModel editorVm,
        EstimationAlgorithmConfig currentConfig)
    {
        _editorVm = editorVm;
        _currentConfig = currentConfig;
        Parameters = currentConfig.GetParameters();
        DataContext = this;
        InitializeComponent();
    }

    private async void OK_Click(object? sender, RoutedEventArgs e)
    {
        var error = _currentConfig.ApplyParameters(Parameters);
        if (error is not null)
        {
            await new MessageDialog("Invalid Input", error).ShowDialog(this);
            return;
        }
        _editorVm!.Session.SetEstimationAlgorithmConfig(_editorVm.User, _currentConfig, out _);
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
