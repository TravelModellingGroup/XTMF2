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
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;

namespace XTMF2.Bus.Optimization;

/// <summary>
/// Abstract base class for all calibration update rules.
/// Each subclass encapsulates one algorithm and its per-algorithm parameters.
/// </summary>
public abstract class CalibrationAlgorithmBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Stable identifier used as the JSON type-discriminator.</summary>
    public abstract string AlgorithmId { get; }

    /// <summary>Human-readable name shown in the GUI.</summary>
    public abstract string DisplayName { get; }

    /// <summary>
    /// Applies the calibration update rule to a single parameter value and clamps
    /// the result to <paramref name="min"/> .. <paramref name="max"/>.
    /// </summary>
    /// <param name="current">Current parameter value.</param>
    /// <param name="target">Target parameter value.</param>
    /// <param name="min">Lower bound.</param>
    /// <param name="max">Upper bound.</param>
    /// <param name="stepSize">The fraction of the computed corrective factor to apply.</param>
    public abstract double Apply(double current, double modelled, double target, double min, double max, double stepSize = 1.0);

    /// <summary>Writes algorithm-specific properties into an already-open JSON object.</summary>
    protected abstract void SaveProperties(Utf8JsonWriter writer);

    /// <summary>
    /// Reads and applies algorithm-specific properties from an already-open JSON object.
    /// Implementations must consume exactly one well-formed property per call (or call
    /// <see cref="Utf8JsonReader.Skip"/> to skip unknown properties).
    /// </summary>
    protected abstract void LoadProperty(string propertyName, ref Utf8JsonReader reader);

    private const string TypeProperty = "Type";

    /// <summary>Serialises this algorithm as a self-describing JSON object.</summary>
    internal void Save(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString(TypeProperty, AlgorithmId);
        SaveProperties(writer);
        writer.WriteEndObject();
    }

    /// <summary>
    /// Deserialises a <see cref="CalibrationAlgorithmBase"/> from a JSON object that was
    /// written by <see cref="Save"/>.  The reader must be positioned on the opening
    /// <c>StartObject</c> token.  Returns <c>null</c> on failure (unknown type or corrupt
    /// data); callers should fall back to <see cref="Default"/>.
    /// </summary>
    internal static CalibrationAlgorithmBase? Load(ref Utf8JsonReader reader)
    {
        // The reader is on StartObject.
        string? typeId = null;
        // Peek ahead to find the Type discriminator.
        var readerCopy = reader;
        while (readerCopy.Read() && readerCopy.TokenType != JsonTokenType.EndObject)
        {
            if (readerCopy.TokenType == JsonTokenType.PropertyName
                && readerCopy.ValueTextEquals(TypeProperty))
            {
                readerCopy.Read();
                typeId = readerCopy.GetString();
                break;
            }
            readerCopy.Read(); // skip value
            readerCopy.Skip();
        }

        if (typeId is null) { SkipObject(ref reader); return null; }

        CalibrationAlgorithmBase? algo = typeId switch
        {
            ProportionalScalingAlgorithm.Id => new ProportionalScalingAlgorithm(),
            LogOddsAlgorithm.Id            => new LogOddsAlgorithm(),
            _ => null
        };

        if (algo is null) { SkipObject(ref reader); return null; }

        // Now parse the real reader, calling LoadProperty for each non-Type property.
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) continue;
            var propName = reader.GetString() ?? string.Empty;
            reader.Read(); // position on value
            if (propName == TypeProperty) { /* already used */ continue; }
            algo.LoadProperty(propName, ref reader);
        }
        return algo;
    }

    private static void SkipObject(ref Utf8JsonReader reader)
    {
        // reader is on StartObject – skip to matching EndObject
        int depth = 1;
        while (depth > 0 && reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject) depth++;
            else if (reader.TokenType == JsonTokenType.EndObject) depth--;
        }
    }

    /// <summary>All registered calibration algorithms, used to populate GUI pickers.</summary>
    public static IReadOnlyList<CalibrationAlgorithmBase> AvailableAlgorithms { get; } =
        [new ProportionalScalingAlgorithm(), new LogOddsAlgorithm()];

    /// <summary>The default algorithm (used when none is stored in the file).</summary>
    public static CalibrationAlgorithmBase Default => new LogOddsAlgorithm();

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is CalibrationAlgorithmBase other && AlgorithmId == other.AlgorithmId;

    /// <inheritdoc/>
    public override int GetHashCode() => AlgorithmId.GetHashCode(StringComparison.Ordinal);

    /// <inheritdoc/>
    public override string ToString() => DisplayName;
}