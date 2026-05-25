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
using CommunityToolkit.Mvvm.ComponentModel;

namespace XTMF2.GUI.ViewModels;

/// <summary>
/// Represents a single optimisation parameter in the live progress dialog.
/// </summary>
public sealed partial class OptimizationParameterViewModel : ObservableObject
{
    /// <summary>The node serialisation index used to match incoming progress data.</summary>
    public int NodeIndex { get; }

    /// <summary>Human-readable name of the parameter node.</summary>
    public string Name { get; }

    /// <summary>Lower bound for this parameter.</summary>
    public double MinBound { get; }

    /// <summary>Upper bound for this parameter.</summary>
    public double MaxBound { get; }

    /// <summary>The most-recently received value for this parameter.</summary>
    [ObservableProperty]
    private double _currentValue;

    public OptimizationParameterViewModel(int nodeIndex, string name, double min, double max)
    {
        NodeIndex    = nodeIndex;
        Name         = name;
        MinBound     = min;
        MaxBound     = max;
        _currentValue = min;
    }
}
