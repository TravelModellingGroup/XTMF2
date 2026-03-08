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
using CommunityToolkit.Mvvm.ComponentModel;
using XTMF2.Configuration;
using XTMF2.Editing;
using XTMF2.ModelSystemConstruct;
using XTMF2.RuntimeModules;

namespace XTMF2.GUI.ViewModels;

/// <summary>
/// Wraps a <see cref="Node"/> for display on the model system canvas.
/// Modules are rendered as rectangular boxes.
/// </summary>
public sealed partial class NodeViewModel : ObservableObject, ICanvasElement
{
    /// <summary>The underlying model object.</summary>
    public Node UnderlyingNode { get; }

    private readonly ModelSystemSession _session;
    private readonly User _user;

    // ── Coordinates read directly from the underlying model ──────────────
    /// <inheritdoc/>
    public double X => (double)UnderlyingNode.Location.X;

    /// <inheritdoc/>
    public double Y => (double)UnderlyingNode.Location.Y;

    /// <summary>Rendered width; falls back to 120 when the model value is 0.</summary>
    public double Width  => UnderlyingNode.Location.Width  is 0 ? 120.0 : (double)UnderlyingNode.Location.Width;

    /// <summary>Rendered height; falls back to 50 when the model value is 0.</summary>
    public double Height => UnderlyingNode.Location.Height is 0 ? 50.0  : (double)UnderlyingNode.Location.Height;

    /// <summary>Centre X, used to compute link endpoints after a move.</summary>
    public double CenterX => X + Width / 2.0;

    /// <summary>Centre Y, used to compute link endpoints after a move.</summary>
    public double CenterY => Y + Height / 2.0;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private bool _isSelected;

    /// <summary>
    /// When <c>true</c> all hooks (including optional ones) are shown on this node.
    /// Required hooks (cardinality <c>Single</c> or <c>AtLeastOne</c>)
    /// are always visible regardless of this flag.
    /// </summary>
    [ObservableProperty] private bool _showHooks;

    /// <summary>
    /// The short name of the module type currently assigned to this node
    /// (e.g. "BasicParameter`1"). Updates automatically when the type changes.
    /// </summary>
    public string TypeName => UnderlyingNode.Type?.Name ?? "Unknown";

    /// <summary>
    /// True when the node's type is <see cref="BasicParameter{T}"/> or
    /// <see cref="ScriptedParameter{T}"/>, meaning it carries a string
    /// parameter value the user can view and edit.
    /// </summary>
    public bool IsParameterNode
    {
        get
        {
            var t = UnderlyingNode.Type;
            if (t is null || !t.IsGenericType) return false;
            var td = t.GetGenericTypeDefinition();
            return td == typeof(BasicParameter<>) || td == typeof(ScriptedParameter<>)
                || td == typeof(SetableParameter<>);
        }
    }

    /// <summary>
    /// The string representation of the node's current parameter value,
    /// or <see cref="string.Empty"/> when no value has been assigned.
    /// </summary>
    public string ParameterValueRepresentation
        => UnderlyingNode.ParameterValue?.Representation ?? string.Empty;

    public NodeViewModel(Node node, ModelSystemSession session, User user)
    {
        UnderlyingNode = node;
        _session = session;
        _user    = user;
        _name    = node.Name ?? string.Empty;

        // Keep Name and coordinates in sync when the model changes.
        ((INotifyPropertyChanged)node).PropertyChanged += OnModelPropertyChanged;
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Node.Name):
                Name = UnderlyingNode.Name ?? string.Empty;
                break;
            case nameof(Node.Type):
                OnPropertyChanged(nameof(TypeName));
                OnPropertyChanged(nameof(IsParameterNode));
                break;
            case nameof(Node.ParameterValue):
                OnPropertyChanged(nameof(ParameterValueRepresentation));
                break;
            case nameof(Node.Location):
                OnPropertyChanged(nameof(X));
                OnPropertyChanged(nameof(Y));
                OnPropertyChanged(nameof(Width));
                OnPropertyChanged(nameof(Height));
                OnPropertyChanged(nameof(CenterX));
                OnPropertyChanged(nameof(CenterY));
                break;
        }
    }

    /// <summary>
    /// Move the node to a new canvas position, persisting the change to the
    /// underlying model via the session (supports undo/redo).
    /// </summary>
    public void MoveTo(double x, double y)
    {
        var loc = UnderlyingNode.Location;
        var w = loc.Width  is 0 ? 120f : loc.Width;
        var h = loc.Height is 0 ? 50f  : loc.Height;
        _session.SetNodeLocation(_user, UnderlyingNode, new Rectangle((float)x, (float)y, w, h), out _);
        // OnModelPropertyChanged("Location") is fired by the model; it raises
        // PropertyChanged for X, Y, CenterX, CenterY automatically.
    }

    /// <summary>
    /// Resize the node, persisting the change via the session (supports undo/redo).
    /// Width is clamped to a minimum of 120; height to a minimum of 28.
    /// </summary>
    public void ResizeTo(double w, double h)
    {
        const float minW = 120f;
        const float minH = 28f;
        var loc = UnderlyingNode.Location;
        _session.SetNodeLocation(_user, UnderlyingNode,
            new Rectangle(loc.X, loc.Y, Math.Max(minW, (float)w), Math.Max(minH, (float)h)),
            out _);
    }
}
