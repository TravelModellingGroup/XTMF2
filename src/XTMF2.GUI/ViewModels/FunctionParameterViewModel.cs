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
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using XTMF2;
using XTMF2.Editing;
using XTMF2.ModelSystemConstruct;

namespace XTMF2.GUI.ViewModels;

/// <summary>
/// Wraps a <see cref="FunctionParameter"/> for display on the model system canvas.
/// FunctionParameters are rendered inside their owning <see cref="FunctionTemplate"/>'s
/// <see cref="FunctionTemplate.InternalModules"/> boundary as special orange/red nodes
/// with a "Parameter:" prefix in the header.
/// </summary>
public sealed partial class FunctionParameterViewModel : ObservableObject, ICanvasElement
{
    /// <summary>The underlying model object.</summary>
    public FunctionParameter UnderlyingParameter { get; }

    private readonly ModelSystemSession _session;
    private readonly User _user;

    // ── Drag-preview offsets ──────────────────────────────────────────────
    private double? _previewX;
    private double? _previewY;
    private double? _previewW;
    private double? _previewH;

    // ── ICanvasElement coordinates ────────────────────────────────────────
    /// <inheritdoc/>
    public double X => _previewX ?? (double)UnderlyingParameter.Location.X;

    /// <inheritdoc/>
    public double Y => _previewY ?? (double)UnderlyingParameter.Location.Y;

    /// <summary>Rendered width; defaults to 180 when the stored value is 0.</summary>
    public double Width  => _previewW ?? (UnderlyingParameter.Location.Width  is 0 ? 180.0 : (double)UnderlyingParameter.Location.Width);

    /// <summary>Rendered height; defaults to 40 when the stored value is 0.</summary>
    public double Height => _previewH ?? (UnderlyingParameter.Location.Height is 0 ? 40.0  : (double)UnderlyingParameter.Location.Height);

    /// <inheritdoc/>
    public double CenterX => X + Width  / 2.0;

    /// <inheritdoc/>
    public double CenterY => Y + Height / 2.0;

    [ObservableProperty] private string _name        = string.Empty;
    [ObservableProperty] private bool   _isSelected;

    /// <summary>
    /// Display name of the parameter type, with generic arguments expanded
    /// (e.g. <c>IModule&lt;int&gt;</c> instead of <c>IModule`1</c>).
    /// </summary>
    public string TypeName => FormatTypeName(UnderlyingParameter.Type);

    private static string FormatTypeName(Type? type)
    {
        if (type is null) return string.Empty;
        if (!type.IsGenericType) return type.Name;
        int tick     = type.Name.IndexOf('`');
        var baseName = tick < 0 ? type.Name : type.Name[..tick];
        var args     = string.Join(", ", type.GetGenericArguments().Select(FormatTypeName));
        return $"{baseName}<{args}>";
    }

    public FunctionParameterViewModel(FunctionParameter parameter, ModelSystemSession session, User user)
    {
        UnderlyingParameter = parameter;
        _session            = session;
        _user               = user;
        _name               = parameter.Name;

        ((INotifyPropertyChanged)parameter).PropertyChanged += OnModelPropertyChanged;
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(FunctionParameter.Name):
                Name = UnderlyingParameter.Name;
                break;
            case nameof(FunctionParameter.Location):
                OnPropertyChanged(nameof(X));
                OnPropertyChanged(nameof(Y));
                OnPropertyChanged(nameof(Width));
                OnPropertyChanged(nameof(Height));
                OnPropertyChanged(nameof(CenterX));
                OnPropertyChanged(nameof(CenterY));
                break;            case nameof(FunctionParameter.Type):
                OnPropertyChanged(nameof(TypeName));
                break;
        }
    }

    /// <summary>
    /// Renames this <see cref="FunctionParameter"/> via the session (with undo).
    /// </summary>
    public bool SetName(string newName, [NotNullWhen(false)] out CommandError? error)
        => _session.RenameFunctionParameter(_user, UnderlyingParameter.Template, UnderlyingParameter, newName, out error);

    // ── Drag / resize support ─────────────────────────────────────────────

    /// <summary>
    /// Updates the visual position without persisting (for drag preview).
    /// Call <see cref="CommitMove"/> on mouse-up to persist.
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
    /// Commits the preview position and clears it. Does nothing when no preview is active.
    /// </summary>
    public void CommitMove()
    {
        if (_previewX is null) return;
        var x = _previewX.Value;
        var y = _previewY!.Value;
        _previewX = null;
        _previewY = null;
        var loc = UnderlyingParameter.Location;
        _session.SetNodeLocation(_user, UnderlyingParameter,
            new Rectangle((float)x, (float)y, loc.Width, loc.Height), out _);
    }

    /// <summary>
    /// Updates the visual size without persisting (for resize preview).
    /// Call <see cref="CommitResize"/> on mouse-up to persist.
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
    /// Commits the preview size and clears it. Does nothing when no preview is active.
    /// </summary>
    public void CommitResize()
    {
        if (_previewW is null) return;
        var w = _previewW.Value;
        var h = _previewH!.Value;
        _previewW = null;
        _previewH = null;
        var loc = UnderlyingParameter.Location;
        _session.SetNodeLocation(_user, UnderlyingParameter,
            new Rectangle(loc.X, loc.Y, (float)w, (float)h), out _);
    }

    /// <summary>Detaches model event subscriptions (call before discarding this VM).</summary>
    public void Detach()
    {
        ((INotifyPropertyChanged)UnderlyingParameter).PropertyChanged -= OnModelPropertyChanged;
    }
}
