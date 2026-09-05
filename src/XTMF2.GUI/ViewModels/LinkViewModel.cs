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

    /// <summary>
    /// Destination slot index for this rendered branch. For <see cref="SingleLink"/> this is always 0.
    /// For <see cref="MultiLink"/> this maps to the destination index in <see cref="MultiLink.Destinations"/>.
    /// </summary>
    public int DestinationIndex { get; }

    [ObservableProperty] private double _x1;
    [ObservableProperty] private double _y1;
    [ObservableProperty] private double _x2;
    [ObservableProperty] private double _y2;

    [ObservableProperty] private bool _isSelected;

    /// <summary>
    /// Whether this link should be rendered using orthogonal (right-angle) routing.
    /// Mirrors <see cref="XTMF2.Link.IsOrthogonal"/> and updates automatically when it changes.
    /// </summary>
    public bool IsOrthogonal => UnderlyingLink.IsOrthogonal;

    public double? OrthogonalBreakpointX => UnderlyingLink.OrthogonalBreakpointX;

    /// <summary>
    /// Whether this rendered destination branch should be hidden on the canvas.
    /// </summary>
    public bool IsDestinationBranchHidden => UnderlyingLink.IsDestinationHidden(DestinationIndex);

    public LinkViewModel(XTMF2.Link link, ICanvasElement origin, ICanvasElement? destination, int destinationIndex = 0)
    {
        UnderlyingLink  = link;
        Origin          = origin;
        Destination     = destination;
        DestinationIndex = destinationIndex;

        // Compute initial endpoints
        RefreshEndpoints();

        // Subscribe to movements of connected elements
        origin.PropertyChanged      += OnConnectedElementChanged;
        if (destination is not null)
            destination.PropertyChanged += OnConnectedElementChanged;

        // Forward link model property changes (e.g. IsOrthogonal) to the UI.
        UnderlyingLink.PropertyChanged += OnUnderlyingLinkPropertyChanged;
    }

    private void OnUnderlyingLinkPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(XTMF2.Link.IsOrthogonal))
            OnPropertyChanged(nameof(IsOrthogonal));

        if (e.PropertyName is nameof(XTMF2.Link.OrthogonalBreakpointX))
            OnPropertyChanged(nameof(OrthogonalBreakpointX));

        if (e.PropertyName is nameof(XTMF2.ModelSystemConstruct.SingleLink.DestinationHidden)
            or nameof(XTMF2.ModelSystemConstruct.MultiLink.Destinations))
            OnPropertyChanged(nameof(IsDestinationBranchHidden));
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
        UnderlyingLink.PropertyChanged               -= OnUnderlyingLinkPropertyChanged;
    }
}
