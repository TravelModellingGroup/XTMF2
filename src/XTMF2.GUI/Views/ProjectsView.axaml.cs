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
using Avalonia.Interactivity;
using XTMF2.GUI.ViewModels;

namespace XTMF2.GUI.Views;

public partial class ProjectsView : UserControl
{
    private ProjectsViewModel? _viewModel;

    public ProjectsView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initialize the view with a view model
    /// </summary>
    /// <param name="viewModel">The view model to bind to</param>
    public void Initialize(ProjectsViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void NewProject_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: Implement New Project functionality
        // This would open a dialog to create a new project
    }

    private void Refresh_Click(object? sender, RoutedEventArgs e)
    {
        // The observable collection should automatically update,
        // but this could force a refresh if needed
    }
}
