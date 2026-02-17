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
using XTMF2;
using XTMF2.GUI.ViewModels;
using XTMF2.GUI.Views;

namespace XTMF2.GUI;

public partial class MainWindow : Window
{
    private XTMFRuntime? _runtime;
    private bool _isLoading = true;
    private SettingsWindow? _settingsWindow;

    // Parameterless constructor for designer support
    public MainWindow()
    {
        InitializeComponent();
        UpdateLoadingState();
    }

    /// <summary>
    /// Initialize the window with the runtime once it's loaded
    /// </summary>
    /// <param name="runtime">The XTMF runtime instance</param>
    public void InitializeWithRuntime(XTMFRuntime runtime)
    {
        _runtime = runtime;
        _isLoading = false;
        UpdateLoadingState();
        InitializeViews();
    }

    private void UpdateLoadingState()
    {
        // Update the visibility of loading indicator and enable/disable tabs
        LoadingPanel?.IsVisible = _isLoading;
        StartTab?.IsEnabled = !_isLoading;
    }

    private void InitializeViews()
    {
        if (_runtime is null)
        {
            return;
        }

        // Initialize the Projects view with the runtime
        var projectsViewModel = new ProjectsViewModel(_runtime);
        ProjectsViewControl.Initialize(projectsViewModel);
    }

    private void Settings_Click(object? sender, RoutedEventArgs e)
    {
        // Only open settings if there isn't already an instance open
        if (_settingsWindow == null || !_settingsWindow.IsVisible)
        {
            _settingsWindow = new SettingsWindow();
            
            // Clean up the reference when the window closes
            _settingsWindow.Closed += (s, e) => _settingsWindow = null;
            
            // Show as a dialog (modal)
            _settingsWindow.ShowDialog(this);
        }
        else
        {
            // Bring the existing window to front
            _settingsWindow.Activate();
        }
    }
}
