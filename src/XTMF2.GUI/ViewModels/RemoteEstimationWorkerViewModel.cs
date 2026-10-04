using CommunityToolkit.Mvvm.ComponentModel;
using System;
using XTMF2.GUI.Properties;

namespace XTMF2.GUI.ViewModels;

public sealed partial class RemoteEstimationWorkerViewModel : ObservableObject
{
    public RunServerEndpoint Endpoint { get; }
    public string WorkerId => Endpoint.Id;
    public string Name => Endpoint.Name;
    public int ConfiguredWorkerCount { get; }

    [ObservableProperty]
    private int _activeWorkerCount;

    public string ActiveWorkerCountDisplay
        => $"{ActiveWorkerCount} / {ConfiguredWorkerCount} workers";

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isBusy;

    public bool CanRemove => IsActive;
    public bool IsInactive => !IsActive;
    public bool IsNotBusy => !IsBusy;

    partial void OnIsActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRemove));
        OnPropertyChanged(nameof(IsInactive));
    }

    partial void OnActiveWorkerCountChanged(int value)
        => OnPropertyChanged(nameof(ActiveWorkerCountDisplay));

    partial void OnIsBusyChanged(bool value)
        => OnPropertyChanged(nameof(IsNotBusy));

    public RemoteEstimationWorkerViewModel(RunServerEndpoint endpoint, bool isActive,
        int configuredWorkerCount = 1)
    {
        Endpoint = endpoint;
        ConfiguredWorkerCount = Math.Clamp(configuredWorkerCount, 1, 32);
        IsActive = isActive;
    }

    internal void SetActiveWorkerCount(int count)
    {
        ActiveWorkerCount = Math.Max(0, count);
        IsActive = ActiveWorkerCount > 0;
    }
}
