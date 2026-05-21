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
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using XTMF2.Bus.Optimization;
using XTMF2.ModelSystemConstruct;

namespace XTMF2.GUI.ViewModels;

/// <summary>
/// View-model wrapper for a <see cref="CalibrationEntry"/> in the calibration parameters list.
/// </summary>
public sealed partial class CalibrationEntryViewModel : ObservableObject
{
    public CalibrationEntry Entry { get; }

    /// <summary>Display name of the nominated parameter node.</summary>
    public string NodeName => Entry.Node.Name ?? string.Empty;

    /// <summary>Display name of the model output function node, or "(none)" when not yet assigned.</summary>
    public string ModelOutputNodeName => Entry.ModelOutputNode?.Name ?? "(none)";

    /// <summary>Display name of the target output function node, or "(none)" when not yet assigned.</summary>
    public string TargetOutputNodeName => Entry.TargetOutputNode?.Name ?? "(none)";

    [ObservableProperty] private double _min;
    [ObservableProperty] private double _max;
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private double _errorTolerance;
    [ObservableProperty] private double _stepSize;
    [ObservableProperty] private CalibrationAlgorithmBase _algorithm;

    /// <summary>Editable text buffer for <see cref="Min"/> used by the inline TextBox.</summary>
    [ObservableProperty] private string _editMin = "0";
    /// <summary>Editable text buffer for <see cref="Max"/> used by the inline TextBox.</summary>
    [ObservableProperty] private string _editMax = "1";
    /// <summary>Editable text buffer for <see cref="ErrorTolerance"/> used by the inline TextBox.</summary>
    [ObservableProperty] private string _editErrorTolerance = "0.0001";
    /// <summary>Editable text buffer for <see cref="StepSize"/> used by the inline TextBox.</summary>
    [ObservableProperty] private string _editStepSize = "1";

    public CalibrationEntryViewModel(CalibrationEntry entry)
    {
        Entry = entry;
        _min = entry.Min;
        _max = entry.Max;
        _isEnabled = entry.IsEnabled;
        _errorTolerance = entry.ErrorTolerance;
        _stepSize = entry.StepSize;
        _algorithm = entry.Algorithm;
        _editMin = entry.Min.ToString("G6", CultureInfo.InvariantCulture);
        _editMax = entry.Max.ToString("G6", CultureInfo.InvariantCulture);
        _editErrorTolerance = entry.ErrorTolerance.ToString("G4", CultureInfo.InvariantCulture);
        _editStepSize = entry.StepSize.ToString("G4", CultureInfo.InvariantCulture);

        ((INotifyPropertyChanged)entry).PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(CalibrationEntry.Min):
                    Min     = entry.Min;
                    EditMin = entry.Min.ToString("G6", CultureInfo.InvariantCulture);
                    break;
                case nameof(CalibrationEntry.Max):
                    Max     = entry.Max;
                    EditMax = entry.Max.ToString("G6", CultureInfo.InvariantCulture);
                    break;
                case nameof(CalibrationEntry.IsEnabled):         IsEnabled = entry.IsEnabled; break;
                case nameof(CalibrationEntry.ErrorTolerance):
                    ErrorTolerance     = entry.ErrorTolerance;
                    EditErrorTolerance = entry.ErrorTolerance.ToString("G4", CultureInfo.InvariantCulture);
                    break;
                case nameof(CalibrationEntry.StepSize):
                    StepSize     = entry.StepSize;
                    EditStepSize = entry.StepSize.ToString("G4", CultureInfo.InvariantCulture);
                    break;
                case nameof(CalibrationEntry.Algorithm):         Algorithm = entry.Algorithm; break;
                case nameof(CalibrationEntry.ModelOutputNode):   OnPropertyChanged(nameof(ModelOutputNodeName)); break;
                case nameof(CalibrationEntry.TargetOutputNode):  OnPropertyChanged(nameof(TargetOutputNodeName)); break;
            }
        };
    }
}
