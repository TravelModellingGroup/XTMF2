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
using XTMF2.GUI.ViewModels;

namespace XTMF2.GUI.Views;

/// <summary>
/// Modeless window that shows live per-iteration progress for an estimation or calibration run.
/// </summary>
public partial class OptimizationProgressWindow : Window
{
    public OptimizationProgressWindow()
    {
        InitializeComponent();
    }

    public OptimizationProgressWindow(RunViewModel vm) : this()
    {
        DataContext = vm;
        Title = $"Optimization Progress — {vm.RunName}";
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}
