using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using System;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Windows.Input;
using XTMF2.GUI.ViewModels;

namespace XTMF2.GUI.Views;

public partial class AiAssistantWindow : Window
{
    private AiAssistantViewModel? _subscribedViewModel;
    private readonly Action<Guid>? _navigateToElement;

    public ICommand HyperlinkCommand { get; }

    public AiAssistantWindow()
    {
        InitializeComponent();
        HyperlinkCommand = new RelayCommand<object?>(HandleHyperlink);
        DataContextChanged += OnDataContextChanged;
        RegisterKeyboardShortcuts();
    }

    public AiAssistantWindow(AiAssistantViewModel viewModel, Action<Guid>? navigateToElement = null)
    {
        InitializeComponent();
        _navigateToElement = navigateToElement;
        HyperlinkCommand = new RelayCommand<object?>(HandleHyperlink);
        DataContextChanged += OnDataContextChanged;
        DataContext = viewModel;
        RegisterKeyboardShortcuts();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedViewModel.Conversation.CollectionChanged -= OnConversationChanged;
        }

        _subscribedViewModel = DataContext as AiAssistantViewModel;
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
            _subscribedViewModel.Conversation.CollectionChanged += OnConversationChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AiAssistantViewModel.Response) or
            nameof(AiAssistantViewModel.Thinking))
        {
            ScrollConversationToEnd();
        }
    }

    private void OnConversationChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ScrollConversationToEnd();
    }

    private void MarkdownViewer_Loaded(object? sender, RoutedEventArgs e)
    {
        var engine = sender?.GetType().GetProperty("Engine")?.GetValue(sender);
        var property = engine?.GetType().GetProperty(nameof(HyperlinkCommand));
        if (property?.CanWrite == true)
        {
            property.SetValue(engine, HyperlinkCommand);
        }
    }

    private void HandleHyperlink(object? parameter)
    {
        var address = parameter switch
        {
            Uri uri => uri,
            string text when Uri.TryCreate(text, UriKind.Absolute, out var uri) => uri,
            _ => null
        };
        if (address is null)
        {
            return;
        }

        if (string.Equals(address.Scheme, "xtmf", StringComparison.OrdinalIgnoreCase))
        {
            if ((string.Equals(address.Host, "element", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(address.Host, "comment", StringComparison.OrdinalIgnoreCase)) &&
                Guid.TryParse(address.AbsolutePath.Trim('/'), out var elementId))
            {
                _navigateToElement?.Invoke(elementId);
            }

            return;
        }

        if (address.Scheme is "http" or "https")
        {
            Process.Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true });
        }
    }

    private void ScrollConversationToEnd()
    {
        Dispatcher.UIThread.Post(
            () => ConversationScrollViewer.ScrollToEnd(),
            DispatcherPriority.Render);
    }

    private void RegisterKeyboardShortcuts()
    {
        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
    }

    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter ||
            e.KeyModifiers.HasFlag(KeyModifiers.Shift) ||
            e.Source is not TextBox { Name: "PromptTextBox" } ||
            DataContext is not AiAssistantViewModel viewModel ||
            !viewModel.SendCommand.CanExecute(null))
        {
            return;
        }

        viewModel.SendCommand.Execute(null);
        e.Handled = true;
    }

    private async void CopyResponse_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AiAssistantViewModel viewModel ||
            string.IsNullOrWhiteSpace(viewModel.Response) ||
            TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        await clipboard.SetTextAsync(viewModel.Response);
    }
}
