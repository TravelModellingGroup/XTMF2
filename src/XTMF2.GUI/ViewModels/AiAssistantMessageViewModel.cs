using CommunityToolkit.Mvvm.ComponentModel;

namespace XTMF2.GUI.ViewModels;

public sealed partial class AiAssistantMessageViewModel : ObservableObject
{
    public AiAssistantMessageViewModel(bool isUser, string content)
    {
        IsUser = isUser;
        Content = content;
    }

    public bool IsUser { get; }

    public bool IsAssistant => !IsUser;

    [ObservableProperty]
    private string _content;

    [ObservableProperty]
    private string _thinking = string.Empty;

    [ObservableProperty]
    private bool _isStreaming;

    public bool HasThinking => !string.IsNullOrWhiteSpace(Thinking);

    public bool IsMarkdownVisible => IsAssistant;

    partial void OnThinkingChanged(string value)
    {
        OnPropertyChanged(nameof(HasThinking));
    }

}