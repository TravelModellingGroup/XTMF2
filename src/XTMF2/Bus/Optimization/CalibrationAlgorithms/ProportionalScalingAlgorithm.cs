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
using System.Text.Json;

namespace XTMF2.Bus.Optimization;

/// <summary>
/// Multiplies the current parameter value by <c>targetOutput / modelOutput</c>
/// each calibration iteration, then clamps to the specified bounds.
/// This is the baseline calibration update rule.
/// </summary>
public sealed class ProportionalScalingAlgorithm : CalibrationAlgorithmBase
{
    internal const string Id = "ProportionalScaling";

    public override string AlgorithmId  => Id;
    public override string DisplayName  => "Proportional Scaling";

    public override double Apply(double currentValue, double modelled, double target, double min, double max, double weight = 1.0)
    {
        var ratio = target / modelled;
        var delta = ((currentValue * ratio) - currentValue) * weight;
        return Math.Clamp(currentValue + delta, min, max);
    }

    protected override void SaveProperties(Utf8JsonWriter writer) { /* no extra properties */ }

    protected override void LoadProperty(string propertyName, ref Utf8JsonReader reader)
        => reader.Skip(); // no extra properties; ignore unknown
}