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
using System.Collections.ObjectModel;
using XTMF2.Controllers;

namespace XTMF2.GUI.ViewModels;

public class ProjectsViewModel
{
    private readonly XTMFRuntime _runtime;
    private readonly User? _currentUser;

    public ProjectsViewModel(XTMFRuntime runtime)
    {
        _runtime = runtime;
        // TODO: If we get to the point of having multiple users,
        // we would need to implement user switching and update the projects
        // list accordingly.
        // Get the first user or create a default user
        var users = _runtime.UserController.Users;
        if (users.Count > 0)
        {
            _currentUser = users[0];
        }
        else
        {
            // Create a default user if none exists
            if (_runtime.UserController.CreateOrGet("DefaultUser", false, out var user, out var error))
            {
                _currentUser = user;
            }
        }
    }

    /// <summary>
    /// Gets the observable collection of projects available to the current user
    /// </summary>
    public ReadOnlyObservableCollection<Project>? Projects
    {
        get
        {
            if (_currentUser == null)
            {
                return null;
            }
            return ProjectController.GetProjects(_currentUser);
        }
    }

    /// <summary>
    /// Gets the current user
    /// </summary>
    public User? CurrentUser => _currentUser;

    /// <summary>
    /// Gets the runtime instance
    /// </summary>
    public XTMFRuntime Runtime => _runtime;
}
