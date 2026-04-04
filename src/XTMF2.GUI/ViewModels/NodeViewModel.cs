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
using XTMF2;
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

    // ── Coordinates read directly from the underlying model (or preview during drag) ─
    private double? _previewX;
    private double? _previewY;
    private double? _previewW;
    private double? _previewH;

    /// <inheritdoc/>
    public double X => _previewX ?? (double)UnderlyingNode.Location.X;

    /// <inheritdoc/>
    public double Y => _previewY ?? (double)UnderlyingNode.Location.Y;

    /// <summary>Rendered width; falls back to 120 when the model value is 0.</summary>
    public double Width  => _previewW ?? (UnderlyingNode.Location.Width  is 0 ? 120.0 : (double)UnderlyingNode.Location.Width);

    /// <summary>Rendered height; falls back to 50 when the model value is 0.</summary>
    public double Height => _previewH ?? (UnderlyingNode.Location.Height is 0 ? 50.0  : (double)UnderlyingNode.Location.Height);

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
                OnPropertyChanged(nameof(IsInlined));
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
    /// Use this together with <see cref="ModelSystemSession.MoveElements"/> for group-drag commits.
    /// </summary>
    internal Rectangle? TakePendingMoveRect()
    {
        if (_previewX is null) return null;
        var x = _previewX.Value;
        var y = _previewY!.Value;
        _previewX = null;
        _previewY = null;
        var loc = UnderlyingNode.Location;
        var w = loc.Width  is 0 ? 120f : loc.Width;
        var h = loc.Height is 0 ? 50f  : loc.Height;
        return new Rectangle((float)x, (float)y, w, h);
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

    /// <summary>Rename the node, persisting the change via the session (supports undo/redo).</summary>
    public bool SetName(string name, out CommandError? error)
        => _session.SetNodeName(_user, UnderlyingNode, name, out error);

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

    /// <summary>
    /// Applies <paramref name="value"/> as the parameter value for this (Basic/Scripted) parameter
    /// node, using the session so the change is undo-able.
    /// <para>
    /// If the underlying node type is <see cref="ScriptedParameter{T}"/>, the value is treated as
    /// an expression string and routed through
    /// <see cref="ModelSystemSession.SetParameterExpression"/> so that a <c>ScriptedParameter</c>
    /// instance is kept rather than being silently replaced with a <c>BasicParameter</c>.
    /// </para>
    /// Returns <c>false</c> and populates <paramref name="error"/> when the value is invalid.
    /// </summary>
    public bool SetParameterValue(string value, out CommandError? error)
    {
        var t = UnderlyingNode.Type;
        bool isScripted = t is not null && t.IsGenericType
                          && t.GetGenericTypeDefinition() == typeof(ScriptedParameter<>);

        if (isScripted)
            return _session.SetParameterExpression(_user, UnderlyingNode, value, out error);

        return _session.SetParameterValue(_user, UnderlyingNode, value, out error);
    }

    /// <summary>
    /// <c>true</c> when this node's location is <see cref="Rectangle.Hidden"/>, meaning it is
    /// rendered inline inside another node's hook row rather than as a standalone canvas element.
    /// </summary>
    public bool IsInlined => UnderlyingNode.Location.Equals(Rectangle.Hidden);

    /// <summary>True when this node's type is <see cref="BasicParameter{T}"/>.</summary>
    public bool IsBasicParameter
    {
        get
        {
            var t = UnderlyingNode.Type;
            return t is not null && t.IsGenericType
                   && t.GetGenericTypeDefinition() == typeof(BasicParameter<>);
        }
    }

    /// <summary>True when this node's type is <see cref="ScriptedParameter{T}"/>.</summary>
    public bool IsScriptedParameter
    {
        get
        {
            var t = UnderlyingNode.Type;
            return t is not null && t.IsGenericType
                   && t.GetGenericTypeDefinition() == typeof(ScriptedParameter<>);
        }
    }

    /// <summary>
    /// Attempts to switch this node between <see cref="BasicParameter{T}"/> and
    /// <see cref="ScriptedParameter{T}"/>, carrying the current value over.
    /// <para>
    /// When switching from <c>ScriptedParameter</c> to <c>BasicParameter</c>, the current
    /// expression string is validated with <see cref="ArbitraryParameterParser"/> to ensure
    /// it can be represented as a plain value of type <c>T</c>.  If validation fails the
    /// method returns <c>false</c> and <paramref name="error"/> describes the problem.
    /// </para>
    /// </summary>
    public bool SwitchParameterType(out CommandError? error)
    {
        var t = UnderlyingNode.Type;
        if (t is null || !t.IsGenericType)
        {
            error = new CommandError("Node has no generic type assigned.");
            return false;
        }

        var td      = t.GetGenericTypeDefinition();
        var typeArg = t.GetGenericArguments()[0];
        // Capture the current value before the type change.
        var currentValue = UnderlyingNode.ParameterValue?.Representation ?? string.Empty;

        bool toBasic;
        Type targetOpenGeneric;
        if (td == typeof(BasicParameter<>))
        {
            targetOpenGeneric = typeof(ScriptedParameter<>);
            toBasic           = false;
        }
        else if (td == typeof(ScriptedParameter<>))
        {
            targetOpenGeneric = typeof(BasicParameter<>);
            toBasic           = true;
        }
        else
        {
            error = new CommandError("Node is not a BasicParameter or ScriptedParameter.");
            return false;
        }

        // When switching to BasicParameter, verify the expression string is parseable as T.
        if (toBasic)
        {
            string? parseError = null;
            var (success, _) = ArbitraryParameterParser.ArbitraryParameterParse(typeArg, currentValue, ref parseError);
            if (!success)
            {
                error = new CommandError(
                    $"The value \u2018{currentValue}\u2019 cannot be represented as a {typeArg.Name} "
                    + $"in a Basic Parameter: {parseError}");
                return false;
            }
        }

        var targetType = targetOpenGeneric.MakeGenericType(typeArg);

        // Step 1 – change the node type.
        if (!_session.SetNodeType(_user, UnderlyingNode, targetType, out error))
            return false;

        // Step 2 – re-apply the value in the new type's format.
        if (toBasic)
            return _session.SetParameterValue(_user, UnderlyingNode, currentValue, out error);
        else
            return _session.SetParameterExpression(_user, UnderlyingNode, currentValue, out error);
    }

    /// <summary>
    /// Hides this node from the canvas by setting its location to <see cref="Rectangle.Hidden"/>.
    /// The node's value continues to be displayed inline within the connected origin node's hook row.
    /// </summary>
    public void InlineBasicParameter()
    {
        _session.SetNodeLocation(_user, UnderlyingNode, Rectangle.Hidden, out _);
    }

    /// <summary>
    /// Expands this previously-inlined parameter node back onto the canvas at
    /// (<paramref name="x"/>, <paramref name="y"/>), restoring its previous width/height
    /// (or sensible defaults when the stored dimensions are invalid).
    /// </summary>
    public void ExpandToCanvas(double x, double y)
    {
        var loc = UnderlyingNode.Location;
        var w = loc.Width  > 0 ? loc.Width  : 120f;
        var h = loc.Height > 0 ? loc.Height : 50f;
        _session.SetNodeLocation(_user, UnderlyingNode,
            new Rectangle((float)x, (float)y, w, h), out _);
    }
}
