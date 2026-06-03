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
/// Wraps a <see cref="FunctionInstance"/> for display on the model system canvas.
/// A FunctionInstance is rendered as a rectangular node whose header shows the instance
/// name and whose hook rows correspond to the referenced
/// <see cref="FunctionTemplate.FunctionParameters"/>.
/// </summary>
public sealed partial class FunctionInstanceViewModel : ObservableObject, ICanvasElement
{
    // Keep these in sync with ModelSystemCanvas function-instance layout constants.
    private const double DefaultWidth = 120.0;
    private const double DefaultHeight = 50.0;
    private const double HeaderHeight = 28.0;
    private const double HookRowHeight = 16.0;

    /// <summary>The underlying model object.</summary>
    public FunctionInstance UnderlyingInstance { get; }

    private readonly ModelSystemSession _session;
    private readonly User _user;

    // ── Drag / resize preview offsets ─────────────────────────────────────
    private double? _previewX;
    private double? _previewY;
    private double? _previewW;
    private double? _previewH;

    // ── ICanvasElement ────────────────────────────────────────────────────
    public double X => _previewX ?? (double)UnderlyingInstance.Location.X;
    public double Y => _previewY ?? (double)UnderlyingInstance.Location.Y;

    /// <summary>Rendered width; defaults to 120 when the stored value is 0.</summary>
    public double Width  => _previewW ?? (UnderlyingInstance.Location.Width is 0 ? DefaultWidth : (double)UnderlyingInstance.Location.Width);

    /// <summary>
    /// Minimum height needed to show the FI header and all function-parameter hook rows.
    /// </summary>
    public double MinimumHeight => Math.Max(DefaultHeight, HeaderHeight + FunctionParameters.Count * HookRowHeight);

    /// <summary>Rendered height; never below <see cref="MinimumHeight"/>.</summary>
    public double Height
    {
        get
        {
            var h = _previewH ?? (UnderlyingInstance.Location.Height is 0 ? DefaultHeight : (double)UnderlyingInstance.Location.Height);
            return Math.Max(h, MinimumHeight);
        }
    }

    public double CenterX => X + Width  / 2.0;
    public double CenterY => Y + Height / 2.0;

    [ObservableProperty] private string _name        = string.Empty;
    [ObservableProperty] private bool   _isSelected;

    /// <summary>
    /// The short display name of the referenced <see cref="FunctionTemplate"/>
    /// shown beneath the instance name in the header band.
    /// </summary>
    public string TemplateName => UnderlyingInstance.Template.Name;

    /// <summary>
    /// The short name of the entry-node type for the referenced template,
    /// or an empty string when the template has no entry node.
    /// Displayed alongside the template name in the instance header.
    /// </summary>
    public string EntryNodeTypeName
        => UnderlyingInstance.Template.Type?.Name ?? string.Empty;

    /// <summary>
    /// Live-synced list of <see cref="FunctionParameter"/> objects derived from the referenced template.
    /// Kept in sync with <see cref="FunctionTemplate.FunctionParameters"/>.
    /// </summary>
    public ObservableCollection<FunctionParameter> FunctionParameters { get; } = new();

    public FunctionInstanceViewModel(FunctionInstance instance, ModelSystemSession session, User user)
    {
        UnderlyingInstance = instance;
        _session           = session;
        _user              = user;
        _name              = instance.Name;

        ((INotifyPropertyChanged)instance).PropertyChanged += OnModelPropertyChanged;
        ((INotifyPropertyChanged)instance.Template).PropertyChanged += OnTemplatePropertyChanged;

        // Track the template's FunctionParameters so hook rows stay current.
        SyncFunctionParameters();
        ((INotifyCollectionChanged)instance.Template.FunctionParameters).CollectionChanged += OnFunctionParametersChanged;
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(FunctionInstance.Name):
                Name = UnderlyingInstance.Name;
                break;
            case nameof(FunctionInstance.Hooks):
                // A FunctionParameter was renamed (or added/removed); re-sync the hook label list
                // and notify the canvas to redraw FI hook rows.
                SyncFunctionParameters();
                OnPropertyChanged(nameof(FunctionParameters));
                break;
            case nameof(FunctionInstance.Location):
                OnPropertyChanged(nameof(X));
                OnPropertyChanged(nameof(Y));
                OnPropertyChanged(nameof(Width));
                OnPropertyChanged(nameof(Height));
                OnPropertyChanged(nameof(CenterX));
                OnPropertyChanged(nameof(CenterY));
                break;
        }
    }

    private void OnTemplatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FunctionTemplate.Type))
            OnPropertyChanged(nameof(EntryNodeTypeName));
    }

    private void OnFunctionParametersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncFunctionParameters();
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(MinimumHeight));
        OnPropertyChanged(nameof(CenterY));
    }

    private void SyncFunctionParameters()
    {
        FunctionParameters.Clear();
        foreach (var fp in UnderlyingInstance.Template.FunctionParameters)
            FunctionParameters.Add(fp);
    }

    // ── Drag support ──────────────────────────────────────────────────────

    public void MoveToPreview(double x, double y)
    {
        _previewX = x;
        _previewY = y;
        OnPropertyChanged(nameof(X));
        OnPropertyChanged(nameof(Y));
        OnPropertyChanged(nameof(CenterX));
        OnPropertyChanged(nameof(CenterY));
    }

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
        var loc = UnderlyingInstance.Location;
        var w = loc.Width is 0 ? (float)DefaultWidth : loc.Width;
        var h = loc.Height is 0 ? (float)DefaultHeight : loc.Height;
        if (h < (float)MinimumHeight) h = (float)MinimumHeight;
        return new Rectangle((float)x, (float)y, w, h);
    }

    public void MoveTo(double x, double y)
    {
        var loc = UnderlyingInstance.Location;
        var w = loc.Width is 0 ? (float)DefaultWidth : loc.Width;
        var h = loc.Height is 0 ? (float)DefaultHeight : loc.Height;
        if (h < (float)MinimumHeight) h = (float)MinimumHeight;
        _session.SetFunctionInstanceLocation(_user, UnderlyingInstance,
            new Rectangle((float)x, (float)y, w, h), out _);
    }

    // ── Resize support ────────────────────────────────────────────────────

    public void ResizeToPreview(double w, double h)
    {
        _previewW = Math.Max(DefaultWidth, w);
        _previewH = Math.Max(MinimumHeight, h);
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(CenterX));
        OnPropertyChanged(nameof(CenterY));
    }

    public void CommitResize()
    {
        if (_previewW is null) return;
        var w = _previewW.Value;
        var h = _previewH!.Value;
        _previewW = null;
        _previewH = null;
        var loc = UnderlyingInstance.Location;
        _session.SetFunctionInstanceLocation(_user, UnderlyingInstance,
            new Rectangle(loc.X, loc.Y, (float)w, (float)h), out _);
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
    }

    /// <summary>
    /// Renames the instance via the session (supports undo/redo).
    /// Returns <c>false</c> and populates <paramref name="error"/> on failure.
    /// </summary>
    public bool SetName(string name, out CommandError? error)
        => _session.RenameFunctionInstance(_user, UnderlyingInstance, name, out error);

    /// <summary>Detaches model event subscriptions (call before discarding this VM).</summary>
    public void Detach()
    {
        ((INotifyPropertyChanged)UnderlyingInstance).PropertyChanged -= OnModelPropertyChanged;
        ((INotifyPropertyChanged)UnderlyingInstance.Template).PropertyChanged -= OnTemplatePropertyChanged;
        ((INotifyCollectionChanged)UnderlyingInstance.Template.FunctionParameters).CollectionChanged -= OnFunctionParametersChanged;
    }
}
