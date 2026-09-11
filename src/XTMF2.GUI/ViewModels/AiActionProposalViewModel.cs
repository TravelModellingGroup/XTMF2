using CommunityToolkit.Mvvm.ComponentModel;
using XTMF2.AI;

namespace XTMF2.GUI.ViewModels;

public sealed partial class AiActionProposalViewModel : ObservableObject
{
    public AiActionProposalViewModel(AiActionProposal proposal, bool isDestructive)
    {
        Proposal = proposal with { IsDestructive = isDestructive };
        IsSelected = true;
    }

    public AiActionProposal Proposal { get; }

    public string DisplaySummary => Proposal.IsDestructive
        ? $"{Proposal.Summary} (destructive)"
        : Proposal.Summary;

    [ObservableProperty]
    private bool _isSelected;
}
