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

namespace XTMF2.Bus.Optimization;

/// <summary>
/// Describes a single editable hyperparameter for use in the algorithm configuration dialog.
/// The <see cref="Value"/> property is observable so it can be bound directly to a dialog TextBox.
/// </summary>
public sealed class AlgorithmParameterDescriptor : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Stable key matching the JSON property name used in <see cref="EstimationAlgorithmConfig"/>.</summary>
    public required string Key { get; init; }

    /// <summary>Human-readable parameter name shown as the dialog row label.</summary>
    public required string Label { get; init; }

    /// <summary>Short description shown below the label.</summary>
    public required string Hint { get; init; }

    private string _value = string.Empty;

    /// <summary>Current string value, editable by the dialog's TextBox.</summary>
    public string Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
    }
}
