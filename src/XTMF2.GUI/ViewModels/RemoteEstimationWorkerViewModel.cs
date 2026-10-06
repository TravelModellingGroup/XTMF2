using CommunityToolkit.Mvvm.ComponentModel;
using System;
using XTMF2.GUI.Properties;

namespace XTMF2.GUI.ViewModels;

public sealed partial class RemoteEstimationWorkerViewModel : ObservableObject
{
    public const string CoordinatorWorkerId = "coordinator";

    public RunServerEndpoint Endpoint { get; }
    public string WorkerId => Endpoint.Id;
    public string Name => Endpoint.Name;
    public bool IsCoordinator => string.Equals(WorkerId, CoordinatorWorkerId, StringComparison.Ordinal);
    public int ConfiguredWorkerCount { get; }

    [ObservableProperty]
    private int _activeWorkerCount;

    public string ActiveWorkerCountDisplay
        => $"{ActiveWorkerCount} / {ConfiguredWorkerCount} workers";

    [ObservableProperty]
    private int _completedEvaluations;

    public string EvaluationCountDisplay => $"Tests: {CompletedEvaluations}";

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isBusy;

    public bool CanAdd => !IsCoordinator && IsInactive && IsNotBusy;
    public bool CanRemove => !IsCoordinator && IsActive && IsNotBusy;
    public bool IsInactive => !IsActive;
    public bool IsNotBusy => !IsBusy;

    partial void OnIsActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRemove));
        OnPropertyChanged(nameof(IsInactive));
        OnPropertyChanged(nameof(CanAdd));
    }

    partial void OnActiveWorkerCountChanged(int value)
        => OnPropertyChanged(nameof(ActiveWorkerCountDisplay));

    partial void OnCompletedEvaluationsChanged(int value)
        => OnPropertyChanged(nameof(EvaluationCountDisplay));

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotBusy));
        OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(CanRemove));
    }

    public RemoteEstimationWorkerViewModel(RunServerEndpoint endpoint, bool isActive,
        int configuredWorkerCount = 1)
    {
        Endpoint = endpoint;
        ConfiguredWorkerCount = Math.Clamp(configuredWorkerCount, 1, 32);
        IsActive = isActive || IsCoordinator;
    }

    internal void SetActiveWorkerCount(int count)
    {
        ActiveWorkerCount = Math.Max(0, count);
        IsActive = IsCoordinator || ActiveWorkerCount > 0;
    }

    internal void SetCompletedEvaluations(int count)
        => CompletedEvaluations = Math.Max(0, count);
}
