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
using XTMF2.ModelSystemConstruct;

namespace XTMF2.GUI.ViewModels;

/// <summary>
/// View-model wrapper for an <see cref="EstimationEntry"/> in the estimation parameters list.
/// </summary>
public sealed partial class EstimationEntryViewModel : ObservableObject
{
    public EstimationEntry Entry { get; }

    /// <summary>Display name of the nominated parameter node.</summary>
    public string NodeName => Entry.Node.Name ?? string.Empty;

    [ObservableProperty] private double _min;
    [ObservableProperty] private double _max;
    [ObservableProperty] private double _nullHypothesis;
    [ObservableProperty] private bool _isEnabled;

    /// <summary>Editable text buffer for <see cref="Min"/> used by the inline TextBox.</summary>
    [ObservableProperty] private string _editMin = "0";
    /// <summary>Editable text buffer for <see cref="Max"/> used by the inline TextBox.</summary>
    [ObservableProperty] private string _editMax = "1";
    /// <summary>Editable text buffer for <see cref="NullHypothesis"/> used by the inline TextBox.</summary>
    [ObservableProperty] private string _editNullHypothesis = "0";

    public EstimationEntryViewModel(EstimationEntry entry)
    {
        Entry = entry;
        _min = entry.Min;
        _max = entry.Max;
        _nullHypothesis = entry.NullHypothesis;
        _isEnabled = entry.IsEnabled;
        _editMin            = entry.Min.ToString("G6", CultureInfo.InvariantCulture);
        _editMax            = entry.Max.ToString("G6", CultureInfo.InvariantCulture);
        _editNullHypothesis = entry.NullHypothesis.ToString("G6", CultureInfo.InvariantCulture);

        ((INotifyPropertyChanged)entry).PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(EstimationEntry.Min):
                    Min     = entry.Min;
                    EditMin = entry.Min.ToString("G6", CultureInfo.InvariantCulture);
                    break;
                case nameof(EstimationEntry.Max):
                    Max     = entry.Max;
                    EditMax = entry.Max.ToString("G6", CultureInfo.InvariantCulture);
                    break;
                case nameof(EstimationEntry.NullHypothesis):
                    NullHypothesis     = entry.NullHypothesis;
                    EditNullHypothesis = entry.NullHypothesis.ToString("G6", CultureInfo.InvariantCulture);
                    break;
                case nameof(EstimationEntry.IsEnabled): IsEnabled = entry.IsEnabled; break;
            }
        };
    }
}
