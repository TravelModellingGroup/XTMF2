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
namespace XTMF2.Diff;

/// <summary>
/// Describes how an element differs between two model systems being compared.
/// </summary>
public enum ElementDiffKind
{
    /// <summary>The element is identical in both model systems.</summary>
    Unchanged,
    /// <summary>The element exists only in the right (newer) model system.</summary>
    Added,
    /// <summary>The element exists only in the left (base) model system.</summary>
    Removed,
    /// <summary>The element exists in both but one or more properties differ.</summary>
    Modified
}
