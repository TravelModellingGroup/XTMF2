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
using CommunityToolkit.Mvvm.ComponentModel;
using XTMF2;
using XTMF2.Editing;

namespace XTMF2.GUI.ViewModels;

/// <summary>
/// View model for editing a single model system. Owns the <see cref="ModelSystemSession"/>
/// and disposes it when the tab is closed.
/// </summary>
public sealed partial class ModelSystemEditorViewModel : ObservableObject, IDisposable
{
    private bool _disposed;

    /// <summary>The active editing session for the model system.</summary>
    public ModelSystemSession Session { get; }

    /// <summary>The header of the model system being edited.</summary>
    public ModelSystemHeader ModelSystemHeader => Session.ModelSystemHeader;

    // ----- Dock integration -----

    /// <summary>Tab title shown in the dock.</summary>
    public string Title => ModelSystemHeader.Name ?? "Model System";

    /// <summary>Allow the user to close this tab.</summary>
    public bool CanClose => true;

    public ModelSystemEditorViewModel(ModelSystemSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Session = session;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Session.Dispose();
    }
}
