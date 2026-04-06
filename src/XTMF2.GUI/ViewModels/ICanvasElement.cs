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
using Avalonia;

namespace XTMF2.GUI.ViewModels;

/// <summary>
/// Common interface for elements that can appear on the model system canvas.
/// Implemented by <see cref="NodeViewModel"/> and <see cref="StartViewModel"/>.
/// </summary>
public interface ICanvasElement : INotifyPropertyChanged
{
    /// <summary>
    /// The name of the element, used for display and debugging purposes.
    /// </summary>
    string Name { get; }
    /// <summary>
    /// The X and Y coordinates of the element on the canvas, in pixels. The center of the element is at (X, Y).
    /// </summary>
    double X { get; }
    /// <summary>
    /// The X and Y coordinates of the element on the canvas, in pixels. The center of the element is at (X, Y).
    /// </summary>
    double Y { get; }
    /// <summary>
    /// The X and Y coordinates of the center of the element on the canvas, in pixels.
    /// </summary>
    double CenterX { get; }
    /// <summary>
    /// The X and Y coordinates of the center of the element on the canvas, in pixels.
    /// </summary>
    double CenterY { get; }

    /// <summary>
    /// The width of the element on the canvas, in pixels. Used for layout and hit-testing.
    /// </summary>
    double Width { get; }

    /// <summary>
    /// The height of the element on the canvas, in pixels. Used for layout and hit-testing.
    /// </summary>
    double Height { get; }

    /// <summary>
    /// Whether the element is currently selected.
    /// </summary>
    bool IsSelected { get; set; }

    /// <summary>
    /// Commits any pending move of this element to the underlying model. Should be called after a drag operation completes.
    /// </summary>
    void CommitMove();
    
    /// <summary>
    /// Commits any pending resize of this element to the underlying model. Should be called after a resize operation completes.
    /// </summary>
    void CommitResize();

    bool IsPointWithin(Point point)
    {
        return new Rect(X, Y, Width, Height).Contains(point);
    }

}
