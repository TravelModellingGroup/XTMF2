using CommunityToolkit.Mvvm.ComponentModel;
using XTMF2.GUI.Properties;

namespace XTMF2.GUI.ViewModels;

public sealed partial class RemoteEstimationWorkerViewModel : ObservableObject
{
    public RunServerEndpoint Endpoint { get; }
    public string WorkerId => Endpoint.Id;
    public string Name => Endpoint.Name;

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

    partial void OnIsBusyChanged(bool value)
        => OnPropertyChanged(nameof(IsNotBusy));

    public RemoteEstimationWorkerViewModel(RunServerEndpoint endpoint, bool isActive)
    {
        Endpoint = endpoint;
        IsActive = isActive;
    }
}
