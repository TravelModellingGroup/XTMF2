using CommunityToolkit.Mvvm.ComponentModel;
using XTMF2.AI;

namespace XTMF2.GUI.ViewModels;

public sealed partial class AiToolInvocationViewModel : ObservableObject
{
    public AiToolInvocationViewModel(AiActionProposal proposal)
    {
        Proposal = proposal;
        Status = "Proposed";
    }

    public AiActionProposal Proposal { get; }

    public string ToolName => Proposal.Kind.ToString();

    public string DisplaySummary => Proposal.Summary;

    [ObservableProperty]
    private string _status;

    public string DisplayStatus => $"{ToolName}: {Status}";

    public void SetStatus(string status)
    {
        Status = status;
        OnPropertyChanged(nameof(DisplayStatus));
    }
}
