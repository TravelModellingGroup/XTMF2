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
    along with XTMF2.  If not, see <http://www.gnu.org/licenses/\>.
*/
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using XTMF2.ModelSystemConstruct;

namespace XTMF2.GUI.ViewModels;

/// <summary>
/// View-model wrapper for an <see cref="EstimationGroup"/>.
/// Exposes the group's name, enabled state, and a synced collection of
/// <see cref="EstimationEntryViewModel"/> items.
/// </summary>
public sealed partial class EstimationGroupViewModel : ObservableObject
{
    public EstimationGroup UnderlyingGroup { get; }

    [ObservableProperty] private string _name;
    [ObservableProperty] private bool   _isEnabled;
    [ObservableProperty] private bool   _isEditing;
    [ObservableProperty] private string _editName = string.Empty;

    /// <summary>Synced entry view-models for this group's parameters.</summary>
    public ObservableCollection<EstimationEntryViewModel> Parameters { get; } = new();

    private readonly INotifyPropertyChanged   _groupPropNotify;
    private readonly INotifyCollectionChanged _groupParamsNotify;

    public EstimationGroupViewModel(EstimationGroup group)
    {
        UnderlyingGroup    = group;
        _name              = group.Name;
        _isEnabled         = group.IsEnabled;
        _groupPropNotify   = group;
        _groupParamsNotify = group.Parameters;

        SyncParameters();
        _groupParamsNotify.CollectionChanged += OnParametersChanged;
        _groupPropNotify.PropertyChanged     += OnGroupPropertyChanged;
    }

    private void SyncParameters()
    {
        Parameters.Clear();
        foreach (var entry in UnderlyingGroup.Parameters)
            Parameters.Add(new EstimationEntryViewModel(entry));
    }

    private void OnParametersChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => SyncParameters();

    private void OnGroupPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(EstimationGroup.Name):
                Name = UnderlyingGroup.Name;
                break;
            case nameof(EstimationGroup.IsEnabled):
                IsEnabled = UnderlyingGroup.IsEnabled;
                break;
        }
    }

    /// <summary>Detaches all event subscriptions (call when removing this VM from the collection).</summary>
    public void Detach()
    {
        _groupParamsNotify.CollectionChanged -= OnParametersChanged;
        _groupPropNotify.PropertyChanged     -= OnGroupPropertyChanged;
    }
}
