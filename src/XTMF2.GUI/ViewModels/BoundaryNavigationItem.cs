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
using Avalonia;
using XTMF2.ModelSystemConstruct;

namespace XTMF2.GUI.ViewModels;

/// <summary>
/// Represents a single entry in the boundary navigation dropdown shown in the
/// <see cref="Views.ModelSystemEditorView"/> header.
/// </summary>
public sealed class BoundaryNavigationItem
{
    /// <summary>Human-readable label displayed in the dropdown.</summary>
    public string Label { get; }

    /// <summary>
    /// The boundary this item navigates to, or <c>null</c> when <see cref="IsBrowse"/> is <c>true</c>.
    /// </summary>
    public Boundary? Boundary { get; }

    /// <summary>
    /// When <c>true</c> this is the "Browse / New…" sentinel that opens the full picker dialog.
    /// </summary>
    public bool IsBrowse { get; }

    private BoundaryNavigationItem(string label, Boundary? boundary, bool isBrowse)
    {
        Label    = label;
        Boundary = boundary;
        IsBrowse = isBrowse;
    }

    /// <summary>Creates a navigation item that jumps to <paramref name="boundary"/>.</summary>
    public static BoundaryNavigationItem ForBoundary(string label, Boundary boundary) =>
        new(label, boundary, isBrowse: false);

    /// <summary>Singleton "Browse / New Boundary…" item.</summary>
    public static BoundaryNavigationItem Browse { get; } =
        new("Browse / New Boundary…", null, isBrowse: true);

    /// <inheritdoc/>
    public override string ToString() => Label;
}

/// <summary>
/// Row item used by <see cref="Views.BoundaryPickerDialog"/> to show a boundary in a
/// flat indented list.
/// </summary>
public sealed class BoundaryBrowseItem
{
    /// <summary>The model-layer boundary this row represents.</summary>
    public Boundary Boundary { get; }

    /// <summary>Nesting depth (0 = root).</summary>
    public int Depth { get; }

    /// <summary>Left margin that indents the row by <see cref="Depth"/> levels.</summary>
    public Avalonia.Thickness ItemMargin => new(Depth * 16, 0, 0, 0);

    public BoundaryBrowseItem(Boundary boundary, int depth)
    {
        Boundary = boundary;
        Depth    = depth;
    }
}
