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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using XTMF2;
using XTMF2.Editing;
using XTMF2.ModelSystemConstruct;
using XTMF2.RuntimeModules;

namespace XTMF2.GUI.ViewModels;

/// <summary>
/// Wraps a <see cref="GhostNode"/> for display on the model system canvas.
/// Ghost nodes are rendered as rectangular boxes with a dashed border and no hooks.
/// They always mirror the name of their referenced node.
/// </summary>
public sealed partial class GhostNodeViewModel : ObservableObject, ICanvasElement
{
    /// <summary>The underlying ghost node model object.</summary>
    public GhostNode UnderlyingGhostNode { get; }
    public NodeViewModel ReferencedNodeViewModel { get; }
    public FunctionInstanceViewModel? ReferencedFunctionInstanceViewModel { get; }

    private readonly ModelSystemSession _session;
    private readonly User _user;

    // ── Coordinates read directly from the underlying model (or preview during drag) ─
    private double? _previewX;
    private double? _previewY;
    private double? _previewW;
    private double? _previewH;

    /// <inheritdoc/>
    public double X => _previewX ?? (double)UnderlyingGhostNode.Location.X;

    /// <inheritdoc/>
    public double Y => _previewY ?? (double)UnderlyingGhostNode.Location.Y;

    /// <summary>Rendered width; falls back to 120 when the model value is 0.</summary>
    public double Width  => _previewW ?? (UnderlyingGhostNode.Location.Width  is 0 ? 120.0 : (double)UnderlyingGhostNode.Location.Width);

    /// <summary>Rendered height; falls back to 50 when the model value is 0.</summary>
    public double Height => _previewH ?? Math.Max(
        UnderlyingGhostNode.Location.Height is 0 ? 50.0 : (double)UnderlyingGhostNode.Location.Height,
        28.0 + (IsFunctionInstance ? FunctionParameters.Count : (IsParameterNode ? 1 : 0) + Hooks.Count) * 16.0);

    /// <inheritdoc/>
    public double CenterX => X + Width / 2.0;

    /// <inheritdoc/>
    public double CenterY => Y + Height / 2.0;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private bool _isSelected;

    public Node ReferencedNode => UnderlyingGhostNode.ReferencedNode;
    public bool IsFunctionInstance => ReferencedNode is FunctionInstance;
    public bool IsParameterNode => ReferencedNode is Node node && IsParameterType(node.Type);
    public bool IsBasicParameter => IsParameterNode && ReferencedNode.Type!.GetGenericTypeDefinition() == typeof(BasicParameter<>);
    public bool IsScriptedParameter => IsParameterNode && ReferencedNode.Type!.GetGenericTypeDefinition() == typeof(ScriptedParameter<>);
    public string TypeName => ReferencedNode.Type?.Name ?? "Unknown";
    public string ParameterValueRepresentation => ReferencedNode.ParameterValue?.Representation ?? string.Empty;
    public IReadOnlyList<NodeHook> Hooks => ReferencedNode.Hooks;
    public ObservableCollection<FunctionParameter> FunctionParameters { get; } = new();

    public string TemplateName => ReferencedNode is FunctionInstance instance ? instance.Template.Name : string.Empty;
    public string EntryNodeTypeName => ReferencedNode is FunctionInstance instance
        ? instance.Template.Type?.Name ?? string.Empty
        : string.Empty;

    private static bool IsParameterType(Type? type)
        => type is not null && type.IsGenericType
            && type.GetGenericTypeDefinition() is Type genericType
            && (genericType == typeof(BasicParameter<>)
                || genericType == typeof(ScriptedParameter<>)
                || genericType == typeof(SetableParameter<>));

    public GhostNodeViewModel(GhostNode ghostNode, ModelSystemSession session, User user)
    {
        UnderlyingGhostNode = ghostNode;
        _session = session;
        _user    = user;
        _name    = ghostNode.Name ?? string.Empty;
        ReferencedNodeViewModel = new NodeViewModel(ReferencedNode, session, user);
        if (ReferencedNode is FunctionInstance instance)
            ReferencedFunctionInstanceViewModel = new FunctionInstanceViewModel(instance, session, user);

        if (ReferencedNode is FunctionInstance functionInstance)
            foreach (var parameter in functionInstance.Template.FunctionParameters)
                FunctionParameters.Add(parameter);

        ((INotifyPropertyChanged)ghostNode).PropertyChanged += OnModelPropertyChanged;
        ((INotifyPropertyChanged)ReferencedNode).PropertyChanged += OnReferencedNodePropertyChanged;
    }

    private void OnReferencedNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Node.Type) or nameof(Node.ParameterValue) or nameof(Node.Hooks))
        {
            OnPropertyChanged(nameof(IsParameterNode));
            OnPropertyChanged(nameof(IsBasicParameter));
            OnPropertyChanged(nameof(IsScriptedParameter));
            OnPropertyChanged(nameof(TypeName));
            OnPropertyChanged(nameof(ParameterValueRepresentation));
            OnPropertyChanged(nameof(Hooks));
        }
        if (e.PropertyName is nameof(FunctionInstance.Name) or nameof(FunctionInstance.Hooks))
        {
            OnPropertyChanged(nameof(TemplateName));
            OnPropertyChanged(nameof(EntryNodeTypeName));
            OnPropertyChanged(nameof(FunctionParameters));
        }
    }

    public bool SetName(string name, out CommandError? error)
        => ReferencedNode is FunctionInstance instance
            ? _session.RenameFunctionInstance(_user, instance, name, out error)
            : _session.SetNodeName(_user, ReferencedNode, name, out error);

    public bool SetParameterValue(string value, out CommandError? error)
        => _session.SetParameterValue(_user, ReferencedNode, value, out error);

    public Node? GetEmbeddedParameter(NodeHook hook)
    {
        foreach (var link in ReferencedNode.ContainedWithin.Links)
        {
            if (link.Origin != ReferencedNode || link.OriginHook != hook)
                continue;

            if (link is SingleLink single && IsEmbeddedParameter(single.Destination))
                return single.Destination;
            if (link is MultiLink multi)
            {
                var destination = multi.Destinations.FirstOrDefault(IsEmbeddedParameter);
                if (destination is not null) return destination;
            }
        }
        return null;
    }

    private static bool IsEmbeddedParameter(Node node)
        => node.Location.Equals(Rectangle.Hidden) && IsParameterType(node.Type);

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(GhostNode.Name):
                Name = UnderlyingGhostNode.Name ?? string.Empty;
                break;
            case nameof(GhostNode.Location):
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
        var loc = UnderlyingGhostNode.Location;
        var w = loc.Width  is 0 ? 120f : loc.Width;
        var h = loc.Height is 0 ? 50f  : loc.Height;
        return new Rectangle((float)x, (float)y, w, h);
    }

    /// <summary>
    /// Move the ghost node to a new canvas position, persisting via the session
    /// (supports undo/redo).
    /// </summary>
    public void MoveTo(double x, double y)
    {
        var loc = UnderlyingGhostNode.Location;
        var w = loc.Width  is 0 ? 120f : loc.Width;
        var h = loc.Height is 0 ? 50f  : loc.Height;
        _session.SetNodeLocation(_user, UnderlyingGhostNode,
            new Rectangle((float)x, (float)y, w, h), out _);
    }

    /// <summary>
    /// Updates the visual size without touching the session (for resize-drag preview).
    /// Call <see cref="CommitResize"/> on mouse-up to persist the change.
    /// Width is clamped to a minimum of 120; height to a minimum of 28.
    /// </summary>
    public void ResizeToPreview(double w, double h)
    {
        _previewW = Math.Max(120.0, w);
        _previewH = Math.Max(28.0,  h);
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(CenterX));
        OnPropertyChanged(nameof(CenterY));
    }

    /// <summary>
    /// Commits the current preview size to the session (call once on mouse-up).
    /// Does nothing if no resize preview is active.
    /// </summary>
    public void CommitResize()
    {
        if (_previewW is null) return;
        var w = _previewW.Value;
        var h = _previewH!.Value;
        _previewW = null;
        _previewH = null;
        ResizeTo(w, h);
    }

    /// <summary>
    /// Resize the ghost node, persisting via the session.
    /// Width is clamped to a minimum of 120; height to a minimum of 28.
    /// </summary>
    public void ResizeTo(double w, double h)
    {
        const float minW = 120f;
        const float minH = 28f;
        var loc = UnderlyingGhostNode.Location;
        _session.SetNodeLocation(_user, UnderlyingGhostNode,
            new Rectangle(loc.X, loc.Y, Math.Max(minW, (float)w), Math.Max(minH, (float)h)),
            out _);
    }
}
