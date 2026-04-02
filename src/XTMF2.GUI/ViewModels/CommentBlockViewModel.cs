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
using XTMF2.Editing;
using XTMF2.ModelSystemConstruct;

namespace XTMF2.GUI.ViewModels;

/// <summary>
/// Wraps a <see cref="CommentBlock"/> for display on the model system canvas.
/// Comment blocks are rendered as sticky-note style rectangles with wrapped text.
/// </summary>
public sealed partial class CommentBlockViewModel : ObservableObject, ICanvasElement
{
    /// <summary>Default width when the model stores 0.</summary>
    public const double DefaultWidth  = 200.0;

    /// <summary>Default height when the model stores 0.</summary>
    public const double DefaultHeight = 80.0;

    /// <summary>The underlying model object.</summary>
    public CommentBlock UnderlyingBlock { get; }

    private readonly ModelSystemSession _session;
    private readonly User _user;

    // ── Coordinates read directly from the underlying model (or preview during drag) ─
    private double? _previewX;
    private double? _previewY;

    /// <inheritdoc/>
    public double X => _previewX ?? (double)UnderlyingBlock.Location.X;

    /// <inheritdoc/>
    public double Y => _previewY ?? (double)UnderlyingBlock.Location.Y;

    /// <summary>Rendered width; falls back to <see cref="DefaultWidth"/> when the model value is 0.</summary>
    public double Width  => UnderlyingBlock.Location.Width  is 0 ? DefaultWidth  : (double)UnderlyingBlock.Location.Width;

    /// <summary>Rendered height; falls back to <see cref="DefaultHeight"/> when the model value is 0.</summary>
    public double Height => UnderlyingBlock.Location.Height is 0 ? DefaultHeight : (double)UnderlyingBlock.Location.Height;

    // ICanvasElement: Name maps to Comment so the property panel can reuse SelectedElementEditName.
    /// <inheritdoc/>
    public string Name => UnderlyingBlock.Comment;

    /// <inheritdoc/>
    public double CenterX => X + Width  / 2.0;

    /// <inheritdoc/>
    public double CenterY => Y + Height / 2.0;

    [ObservableProperty] private bool _isSelected;

    public CommentBlockViewModel(CommentBlock block, ModelSystemSession session, User user)
    {
        UnderlyingBlock = block;
        _session = session;
        _user    = user;

        ((INotifyPropertyChanged)block).PropertyChanged += OnModelPropertyChanged;
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(CommentBlock.Comment):
                OnPropertyChanged(nameof(Name));
                break;
            case nameof(CommentBlock.Location):
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
    /// Move the comment block to a new canvas position, persisting the change via the session
    /// (supports undo/redo).
    /// </summary>
    public void MoveTo(double x, double y)
    {
        var loc = UnderlyingBlock.Location;
        var w = loc.Width  is 0 ? (float)DefaultWidth  : loc.Width;
        var h = loc.Height is 0 ? (float)DefaultHeight : loc.Height;
        _session.SetCommentBlockLocation(_user, UnderlyingBlock,
            new Rectangle((float)x, (float)y, w, h), out _);
        // OnModelPropertyChanged("Location") fires automatically and propagates X/Y changes.
    }

    /// <summary>
    /// Update the comment text, persisting the change via the session (supports undo/redo).
    /// Whitespace-only text is ignored.
    /// </summary>
    public void SetText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        _session.SetCommentBlockText(_user, UnderlyingBlock, text, out _);
    }

    /// <summary>
    /// Resize the comment block, persisting the change via the session (supports undo/redo).
    /// Width is clamped to a minimum of 60; height to a minimum of 30.
    /// </summary>
    public void ResizeTo(double w, double h)
    {
        const float minW = 60f;
        const float minH = 30f;
        var loc = UnderlyingBlock.Location;
        _session.SetCommentBlockLocation(_user, UnderlyingBlock,
            new Rectangle(loc.X, loc.Y, Math.Max(minW, (float)w), Math.Max(minH, (float)h)),
            out _);
    }
}
