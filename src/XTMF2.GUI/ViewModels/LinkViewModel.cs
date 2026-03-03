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
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace XTMF2.GUI.ViewModels;

/// <summary>
/// Wraps a <see cref="Link"/> for display on the model system canvas as a line.
/// Endpoints are automatically updated when the connected <see cref="ICanvasElement"/> items move.
/// </summary>
public sealed partial class LinkViewModel : ObservableObject
{
    /// <summary>The underlying model link.</summary>
    public XTMF2.Link UnderlyingLink { get; }

    /// <summary>The canvas element that is the source of the link.</summary>
    public ICanvasElement Origin { get; }

    /// <summary>
    /// The canvas element that is the single target of the link, or <c>null</c>
    /// when the destination has not been resolved (e.g. a multi-link).
    /// </summary>
    public ICanvasElement? Destination { get; }

    [ObservableProperty] private double _x1;
    [ObservableProperty] private double _y1;
    [ObservableProperty] private double _x2;
    [ObservableProperty] private double _y2;

    [ObservableProperty] private bool _isSelected;

    public LinkViewModel(XTMF2.Link link, ICanvasElement origin, ICanvasElement? destination)
    {
        UnderlyingLink  = link;
        Origin          = origin;
        Destination     = destination;

        // Compute initial endpoints
        RefreshEndpoints();

        // Subscribe to movements of connected elements
        origin.PropertyChanged      += OnConnectedElementChanged;
        if (destination is not null)
            destination.PropertyChanged += OnConnectedElementChanged;
    }

    private void OnConnectedElementChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ICanvasElement.CenterX) or nameof(ICanvasElement.CenterY))
            RefreshEndpoints();
    }

    private void RefreshEndpoints()
    {
        X1 = Origin.CenterX;
        Y1 = Origin.CenterY;
        X2 = Destination?.CenterX ?? (Origin.CenterX + 60);
        Y2 = Destination?.CenterY ?? (Origin.CenterY + 60);
    }

    /// <summary>
    /// Detach property-change subscriptions.  Call when removing this link from the canvas.
    /// </summary>
    public void Detach()
    {
        Origin.PropertyChanged                       -= OnConnectedElementChanged;
        if (Destination is not null)
            Destination.PropertyChanged              -= OnConnectedElementChanged;
    }
}
