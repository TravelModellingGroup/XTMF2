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
/// Applies an additive log update to the current value using
/// <c>current + ln(targetOutput / modelOutput)</c>, then clamps to bounds.
///
/// This is commonly used for calibrating alternative-specific constants (ASCs)
/// in logit choice models.
/// </summary>
public sealed class LogOddsAlgorithm : CalibrationAlgorithmBase
{
    internal const string Id = "LogOdds";

    public override string AlgorithmId => Id;
    public override string DisplayName => "Log Odds";

    public override double Apply(double currentValue, double modelled, double target, double min, double max, double weight = 1.0)
    {

        if (target <= 0 || target >= 1 || modelled <= 0 || modelled >= 1)
        {
            throw new ArgumentOutOfRangeException("LogOddsAlgorithm requires target and modelled values to be in the open interval (0, 1).");
        }

        // Guard against invalid probabilities
        const double eps = 1e-12;
        var pObserved = Math.Clamp(target, eps, 1 - eps);
        var pModelled = Math.Clamp(modelled, eps, 1 - eps);

        // Compute log-odds
        double logOddsObs = Math.Log(pObserved / (1.0 - pObserved));
        double logOddsModel = Math.Log(pModelled / (1.0 - pModelled));

        // Update ASC
        double ascNew = currentValue + weight * (logOddsObs - logOddsModel);
        return Math.Clamp(ascNew, min, max);
    }

    protected override void SaveProperties(Utf8JsonWriter writer) { /* no extra properties */ }

    protected override void LoadProperty(string propertyName, ref Utf8JsonReader reader)
        => reader.Skip(); // no extra properties; ignore unknown
}