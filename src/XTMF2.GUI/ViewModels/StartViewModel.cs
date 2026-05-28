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
using System.ComponentModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using XTMF2.Configuration;
using XTMF2.Editing;
using XTMF2.ModelSystemConstruct;

namespace XTMF2.GUI.ViewModels;

/// <summary>
/// Wraps a <see cref="Start"/> node for display on the model system canvas.
/// Starts are rendered as circles.
/// </summary>
public sealed partial class StartViewModel : ObservableObject, ICanvasElement
{
    /// <summary>The fixed rendering radius for a Start circle.</summary>
    public const double Radius = 30.0;

    /// <summary>The underlying model object.</summary>
    public Start UnderlyingStart { get; }

    private readonly ModelSystemSession _session;
    private readonly User _user;

    // ── Coordinates read directly from the underlying model (or preview during drag) ─
    private double? _previewX;
    private double? _previewY;

    /// <inheritdoc/>
    public double X => _previewX ?? (double)UnderlyingStart.Location.X;

    /// <inheritdoc/>
    public double Y => _previewY ?? (double)UnderlyingStart.Location.Y;

    /// <summary>Centre X of the circle.</summary>
    public double CenterX => X + Radius;

    /// <summary>Centre Y of the circle.</summary>
    public double CenterY => Y + Radius;

    /// <summary>Diameter = 2 * Radius, for Width/Height bindings.</summary>
    public double Diameter => Radius * 2.0;

    public double Width => Diameter;

    public double Height => Diameter;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private bool _isSelected;

    public StartViewModel(Start start, ModelSystemSession session, User user)
    {
        UnderlyingStart = start;
        _session = session;
        _user    = user;
        _name    = start.Name ?? string.Empty;

        // Keep Name and coordinates in sync when the model changes.
        ((INotifyPropertyChanged)start).PropertyChanged += OnModelPropertyChanged;
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Node.Name):
                Name = UnderlyingStart.Name ?? string.Empty;
                break;
            case nameof(Node.Location):
                OnPropertyChanged(nameof(X));
                OnPropertyChanged(nameof(Y));
                OnPropertyChanged(nameof(CenterX));
                OnPropertyChanged(nameof(CenterY));
                break;
        }
    }

    /// <summary>
    /// Updates the visual position without touching the session (for drag preview).
    /// Call <see cref="CommitMove"/> on mouse-up to persist the change.
    /// </summary>
    public void MoveToPreview(double x, double y)
    {
        _previewX = x;
        _previewY = y;
        OnPropertyChanged(nameof(X));
        OnPropertyChanged(nameof(Y));
        OnPropertyChanged(nameof(CenterX));
        OnPropertyChanged(nameof(CenterY));
    }

    /// <summary>
    /// Commits the current preview position to the session (call once on mouse-up).
    /// Does nothing if no preview is active.
    /// </summary>
    public void CommitMove()
    {
        if (_previewX is null) return;
        var x = _previewX.Value;
        var y = _previewY!.Value;
        _previewX = null;
        _previewY = null;
        MoveTo(x, y);
    }

    /// <summary>
    /// Returns the target <see cref="Rectangle"/> for the pending drag preview and clears the
    /// preview state, without making a session call. Returns <c>null</c> when no preview is active.
    /// </summary>
    internal Rectangle? TakePendingMoveRect()
    {
        if (_previewX is null) return null;
        var x = _previewX.Value;
        var y = _previewY!.Value;
        _previewX = null;
        _previewY = null;
        return new Rectangle((float)x, (float)y, (float)Diameter, (float)Diameter);
    }

    /// <summary>
    /// Move the start to a new canvas position, persisting the change to the
    /// underlying model via the session (supports undo/redo).
    /// </summary>
    public void MoveTo(double x, double y)
    {
        _session.SetNodeLocation(_user, UnderlyingStart,
            new Rectangle((float)x, (float)y, (float)Diameter, (float)Diameter), out _);
        // OnModelPropertyChanged("Location") is fired by the model; it raises
        // PropertyChanged for X, Y, CenterX, CenterY automatically.
    }

    /// <summary>Rename the start, persisting the change via the session (supports undo/redo).</summary>
    public bool SetName(string name, out CommandError? error)
        => _session.SetNodeName(_user, UnderlyingStart, name, out error);

    /// <summary>
    /// Starts are fixed-size, so ignore resize attempts. This method is still required to satisfy the <see cref="ICanvasElement"/> interface.
    /// </summary>
    public void CommitResize()
    {
        // Starts are fixed-size, so ignore resize attempts.
    }

    public void ResizeToPreview(double w, double h)
    {
        // Starts are fixed-size, so ignore resize attempts.
    }

    bool IsPointWithin(Point point)
    {
        var dx = X - CenterX;
        var dy = Y - CenterY;
        return (Math.Sqrt(dx * dx + dy * dy) <= StartViewModel.Radius);
    }
}
