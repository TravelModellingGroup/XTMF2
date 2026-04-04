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
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using XTMF2;
using XTMF2.Editing;
using XTMF2.ModelSystemConstruct;

namespace XTMF2.GUI.ViewModels;

/// <summary>
/// Wraps a <see cref="FunctionTemplate"/> for display on the model system canvas.
/// <para>
/// Function templates are rendered as named container boxes (indigo/violet) within the
/// boundary where they are defined. Double-clicking them drills into the template's
/// <see cref="FunctionTemplate.InternalModules"/> boundary.  Nodes within
/// InternalModules that were marked as exposed–hooks appear as hook rows on the
/// container box when viewed from the parent boundary.
/// </para>
/// </summary>
public sealed partial class FunctionTemplateViewModel : ObservableObject, ICanvasElement
{
    /// <summary>The underlying model object.</summary>
    public FunctionTemplate UnderlyingTemplate { get; }

    private readonly ModelSystemSession _session;
    private readonly User _user;

    // ── Drag-preview offsets ──────────────────────────────────────────────
    private double? _previewX;
    private double? _previewY;
    private double? _previewW;
    private double? _previewH;

    // ── ICanvasElement coordinates ────────────────────────────────────────
    /// <inheritdoc/>
    public double X => _previewX ?? (double)UnderlyingTemplate.Location.X;

    /// <inheritdoc/>
    public double Y => _previewY ?? (double)UnderlyingTemplate.Location.Y;

    /// <summary>Rendered width; defaults to 200 when the stored value is 0.</summary>
    public double Width  => _previewW ?? (UnderlyingTemplate.Location.Width  is 0 ? 200.0 : (double)UnderlyingTemplate.Location.Width);

    /// <summary>Rendered height; defaults to 120 when the stored value is 0.</summary>
    public double Height => _previewH ?? (UnderlyingTemplate.Location.Height is 0 ? 120.0 : (double)UnderlyingTemplate.Location.Height);

    /// <inheritdoc/>
    public double CenterX => X + Width  / 2.0;

    /// <inheritdoc/>
    public double CenterY => Y + Height / 2.0;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private bool   _isSelected;

    /// <summary>
    /// The short name of the entry-node type, or an empty string when no entry node is set.
    /// Displayed on the template's canvas container box.
    /// </summary>
    public string EntryNodeTypeName
        => UnderlyingTemplate.Type?.Name ?? string.Empty;

    // ── FunctionParameter mirrors (synced from model) ─────────────────────
    /// <summary>
    /// Live list of <see cref="FunctionParameter"/> objects belonging to this template.
    /// Kept in sync with <see cref="FunctionTemplate.FunctionParameters"/>.
    /// </summary>
    public ObservableCollection<FunctionParameter> FunctionParameters { get; } = new();

    public FunctionTemplateViewModel(FunctionTemplate template, ModelSystemSession session, User user)
    {
        UnderlyingTemplate = template;
        _session           = session;
        _user              = user;
        _name              = template.Name;

        // Sync from the model on property changes.
        ((INotifyPropertyChanged)template).PropertyChanged += OnModelPropertyChanged;

        // Sync FunctionParameters collection.
        SyncFunctionParameters();
        ((INotifyCollectionChanged)template.FunctionParameters).CollectionChanged += OnFunctionParametersChanged;
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(FunctionTemplate.Name):
                Name = UnderlyingTemplate.Name;
                break;
            case nameof(FunctionTemplate.Type):
                OnPropertyChanged(nameof(EntryNodeTypeName));
                break;
            case nameof(FunctionTemplate.Location):
                OnPropertyChanged(nameof(X));
                OnPropertyChanged(nameof(Y));
                OnPropertyChanged(nameof(Width));
                OnPropertyChanged(nameof(Height));
                OnPropertyChanged(nameof(CenterX));
                OnPropertyChanged(nameof(CenterY));
                break;
        }
    }

    private void OnFunctionParametersChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => SyncFunctionParameters();

    private void SyncFunctionParameters()
    {
        FunctionParameters.Clear();
        foreach (var fp in UnderlyingTemplate.FunctionParameters)
            FunctionParameters.Add(fp);
    }

    // ── Drag support ──────────────────────────────────────────────────────

    /// <summary>
    /// Updates the visual position without persisting to the session (for drag preview).
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
    /// Commits the current preview position to the session and clears the preview.
    /// Does nothing when no preview is active.
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
    /// Moves the template container to a new canvas position, persisting via the session
    /// (supports undo/redo).
    /// </summary>
    public void MoveTo(double x, double y)
    {
        var loc = UnderlyingTemplate.Location;
        var w = loc.Width  is 0 ? 200f : loc.Width;
        var h = loc.Height is 0 ? 120f : loc.Height;
        _session.SetFunctionTemplateLocation(_user, UnderlyingTemplate,
            new Rectangle((float)x, (float)y, w, h), out _);
    }

    // ── Resize support ────────────────────────────────────────────────────

    /// <summary>
    /// Updates the visual size without persisting to the session (for resize-drag preview).
    /// Call <see cref="CommitResize"/> on mouse-up to persist.
    /// Width is clamped to ≥ 140; height to ≥ 60.
    /// </summary>
    public void ResizeToPreview(double w, double h)
    {
        _previewW = Math.Max(140.0, w);
        _previewH = Math.Max(60.0,  h);
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(CenterX));
        OnPropertyChanged(nameof(CenterY));
    }

    /// <summary>
    /// Commits the current preview size to the session and clears the preview.
    /// Does nothing when no preview is active.
    /// </summary>
    public void CommitResize()
    {
        if (_previewW is null) return;
        var w = _previewW.Value;
        var h = _previewH!.Value;
        _previewW = null;
        _previewH = null;
        var loc = UnderlyingTemplate.Location;
        _session.SetFunctionTemplateLocation(_user, UnderlyingTemplate,
            new Rectangle(loc.X, loc.Y, (float)w, (float)h), out _);
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
    }

    // ── ICanvasElement: not needed for FunctionTemplates but satisfies the interface ─
    // (Name is already an ObservableProperty above, IsSelected likewise.)

    /// <summary>
    /// Renames the template via the session (supports undo/redo).
    /// Returns <c>false</c> on failure and populates <paramref name="error"/>.
    /// </summary>
    public bool SetName(string name, out CommandError? error)
        => _session.RenameFunctionTemplate(_user, UnderlyingTemplate, name, out error);

    /// <summary>Detaches model event subscriptions (call before discarding this VM).</summary>
    public void Detach()
    {
        ((INotifyPropertyChanged)UnderlyingTemplate).PropertyChanged -= OnModelPropertyChanged;
        ((INotifyCollectionChanged)UnderlyingTemplate.FunctionParameters).CollectionChanged -= OnFunctionParametersChanged;
    }
}
