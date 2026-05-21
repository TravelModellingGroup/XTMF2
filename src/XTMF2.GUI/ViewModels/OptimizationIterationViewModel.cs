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
using System.Collections.Generic;

namespace XTMF2.GUI.ViewModels;

/// <summary>
/// Immutable snapshot of a single optimisation parameter value at one iteration.
/// </summary>
public sealed class ParameterSnapshot
{
    public string Name     { get; }
    public double Min      { get; }
    public double Max      { get; }
    public double Value    { get; }

    public ParameterSnapshot(string name, double min, double max, double value)
    {
        Name  = name;
        Min   = min;
        Max   = max;
        Value = value;
    }
}

/// <summary>
/// Immutable snapshot of all optimisation parameters at a single completed iteration.
/// Shown in the iteration-history list of the progress window.
/// </summary>
public sealed class OptimizationIterationViewModel
{
    public int    Iteration      { get; }
    public double Fitness        { get; }
    public string FitnessDisplay => Fitness.ToString("G6");

    public IReadOnlyList<ParameterSnapshot> ParameterValues { get; }

    public OptimizationIterationViewModel(
        int iteration,
        double fitness,
        IReadOnlyList<ParameterSnapshot> values)
    {
        Iteration      = iteration;
        Fitness        = fitness;
        ParameterValues = values;
    }
}
