using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using XTMF2.AI;

namespace XTMF2.GUI.ViewModels;

public sealed partial class AiPlanTaskViewModel : ObservableObject
{
    public AiPlanTaskViewModel(AiPlanTask task)
    {
        Task = task;
        Status = task.Status;
    }

    public AiPlanTask Task { get; }

    public string DisplayTitle => Task.Title;

    public string DisplayStatus => Status.ToString();

    public bool IsReady => Status == AiPlanTaskStatus.Ready;

    public bool HasActions => Task.ActionIds.Any();

    [ObservableProperty]
    private AiPlanTaskStatus _status;

    public void SetStatus(AiPlanTaskStatus status)
    {
        Status = status;
        OnPropertyChanged(nameof(DisplayStatus));
        OnPropertyChanged(nameof(IsReady));
    }
}