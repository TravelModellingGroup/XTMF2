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
namespace XTMF2.GUI.ViewModels;

/// <summary>
/// Represents a single destination within a <see cref="XTMF2.ModelSystemConstruct.MultiLink"/>
/// for display in the properties panel destination list.
/// </summary>
public sealed class LinkDestinationViewModel
{
    /// <summary>Display name of the destination node.</summary>
    public string Name { get; }

    /// <summary>The underlying canvas node view-model.</summary>
    public NodeViewModel NodeVm { get; }

    public LinkDestinationViewModel(string name, NodeViewModel nodeVm)
    {
        Name   = name;
        NodeVm = nodeVm;
    }
}
