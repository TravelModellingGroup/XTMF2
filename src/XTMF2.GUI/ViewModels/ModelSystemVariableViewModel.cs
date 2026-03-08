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
using XTMF2.ModelSystemConstruct;

namespace XTMF2.GUI.ViewModels;

/// <summary>
/// Lightweight view-model wrapping a <see cref="Node"/> that lives in the
/// model system's <see cref="XTMF2.ModelSystem.Variables"/> list.
/// Provides display info and keeps the name live via <see cref="INotifyPropertyChanged"/>.
/// </summary>
public sealed class ModelSystemVariableViewModel : INotifyPropertyChanged
{
    /// <summary>The underlying model node.</summary>
    public Node UnderlyingNode { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ModelSystemVariableViewModel(Node node)
    {
        UnderlyingNode = node;
        node.PropertyChanged += OnNodePropertyChanged;
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Node.Name))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
    }

    /// <summary>The name of the variable node (live, tracks renames).</summary>
    public string Name => UnderlyingNode.Name;

    /// <summary>
    /// The boundary path, e.g. "global › SubBoundary", to help locate the node.
    /// </summary>
    public string BoundaryPath
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>();
            var b = UnderlyingNode.ContainedWithin;
            while (b is not null) { parts.Insert(0, b.Name); b = b.Parent; }
            return string.Join(" › ", parts);
        }
    }

    /// <summary>
    /// Detaches property-change subscription from the underlying node.
    /// Call when the entry is removed from the list.
    /// </summary>
    public void Detach() => UnderlyingNode.PropertyChanged -= OnNodePropertyChanged;
}
