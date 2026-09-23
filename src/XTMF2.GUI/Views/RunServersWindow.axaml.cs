using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using XTMF2.Bus;
using XTMF2.GUI.Properties;

namespace XTMF2.GUI.Views;

public partial class RunServersWindow : Window
{
    private sealed class RunServerEditor
    {
        public required RunServerEndpoint Endpoint { get; init; }
        public required ListBoxItem SelectorItem { get; init; }
        public required TextBox Name { get; init; }
        public required CheckBox Enabled { get; init; }
        public required TextBox Address { get; init; }
        public required TextBox Port { get; init; }
        public required TextBox Token { get; init; }
        public required TextBox CertificateFingerprint { get; init; }
        public required TextBlock Status { get; init; }
        public required Button Reconnect { get; init; }
        public required Button Remove { get; init; }
    }

    private readonly RunController? _runController;
    private readonly List<RunServerEditor> _editors = new();

    public event Action? RunServersSaved;

    public RunServersWindow()
        : this(null)
    {
    }

    public RunServersWindow(RunController? runController)
    {
        _runController = runController;
        InitializeComponent();
        if (_runController is not null)
            _runController.RunServerStateChanged += OnRunServerStateChanged;
        LoadSettings();
    }


    private void LoadSettings()
    {
        EndpointSelector.Items.Clear();
        _editors.Clear();
        foreach (var endpoint in Properties.Settings.Default.RunServers)
            AddRunServerEditor(endpoint.Clone());
        foreach (var state in _runController?.GetRunServerStates() ?? Array.Empty<RunServerConnectionInfo>())
            ApplyRunServerState(state);
        if (_editors.Count > 0)
            EndpointSelector.SelectedIndex = 0;
        else
            ShowSelectedEditor(null);
    }

    private void AddRunServer_Click(object? sender, RoutedEventArgs e)
    {
        AddRunServerEditor(new RunServerEndpoint { Name = "RunServer" });
        EndpointSelector.SelectedIndex = _editors.Count - 1;
    }

    private void AddRunServerEditor(RunServerEndpoint endpoint)
    {
        var name = new TextBox { Text = endpoint.Name, Watermark = "Name", HorizontalAlignment = HorizontalAlignment.Stretch };
        var enabled = new CheckBox { Content = "Enabled", IsChecked = endpoint.Enabled, IsEnabled = !endpoint.IsLocal };
        var address = new TextBox { Text = endpoint.Address, Watermark = "Address", HorizontalAlignment = HorizontalAlignment.Stretch };
        var port = new TextBox { Text = endpoint.Port.ToString(), Watermark = "Port", HorizontalAlignment = HorizontalAlignment.Stretch };
        var token = new TextBox { Text = endpoint.Token, Watermark = "Token", PasswordChar = '*', IsEnabled = !endpoint.IsLocal, HorizontalAlignment = HorizontalAlignment.Stretch };
        var certificateFingerprint = new TextBox { Text = endpoint.CertificateFingerprint, Watermark = "SHA-256 certificate fingerprint", IsEnabled = !endpoint.IsLocal, HorizontalAlignment = HorizontalAlignment.Stretch };
        var status = new TextBlock
        {
            Text = endpoint.IsLocal ? "Available" : endpoint.Enabled ? "Disconnected" : "Disabled",
            Opacity = 0.7,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Foreground = endpoint.IsLocal ? Brushes.LimeGreen : endpoint.Enabled ? Brushes.Red : Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 48
        };
        var reconnect = new Button { Content = "Reconnect", Width = 130, IsEnabled = !endpoint.IsLocal && endpoint.Enabled };
        var remove = new Button { Content = "Remove", Width = 100, IsEnabled = !endpoint.IsLocal };
        var selectorName = new TextBlock
        {
            Text = endpoint.Name,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        var selectorStatus = new TextBlock
        {
            Text = endpoint.IsLocal ? "Available" : endpoint.Enabled ? "Disconnected" : "Disabled",
            Foreground = endpoint.IsLocal ? Brushes.LimeGreen : endpoint.Enabled ? Brushes.Red : Brushes.Gray,
            FontSize = 11,
            Opacity = 0.9,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        var selectorContent = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        Grid.SetColumn(selectorStatus, 1);
        selectorContent.Children.Add(selectorName);
        selectorContent.Children.Add(selectorStatus);
        var selectorItem = new ListBoxItem { Content = selectorContent, Tag = endpoint };
        var editor = new RunServerEditor
        {
            Endpoint = endpoint,
            SelectorItem = selectorItem,
            Name = name,
            Enabled = enabled,
            Address = address,
            Port = port,
            Token = token,
            CertificateFingerprint = certificateFingerprint,
            Status = status,
            Reconnect = reconnect,
            Remove = remove
        };

        name.TextChanged += (_, _) => selectorName.Text = string.IsNullOrWhiteSpace(name.Text) ? "RunServer" : name.Text;
        reconnect.Click += (_, _) => ReconnectRunServer(editor);
        remove.Click += (_, _) =>
        {
            var index = _editors.IndexOf(editor);
            _editors.Remove(editor);
            EndpointSelector.Items.Remove(selectorItem);
            if (_editors.Count == 0)
                ShowSelectedEditor(null);
            else if (EndpointSelector.SelectedItem == selectorItem)
                EndpointSelector.SelectedIndex = Math.Min(index, _editors.Count - 1);
        };
        EndpointSelector.Items.Add(selectorItem);
        _editors.Add(editor);
    }

    private void OnRunServerStateChanged(RunServerConnectionInfo state)
        => Dispatcher.UIThread.Post(() => ApplyRunServerState(state));

    private void ApplyRunServerState(RunServerConnectionInfo state)
    {
        var editor = _editors.FirstOrDefault(item => item.Endpoint.Id == state.Endpoint.Id);
        if (editor is null)
            return;

        editor.Status.Text = state.State switch
        {
            RunServerConnectionState.Available => "Available",
            RunServerConnectionState.Connecting => "Connecting...",
            _ => string.IsNullOrWhiteSpace(state.Error) ? "Disconnected" : $"Disconnected: {state.Error}"
        };
        editor.Status.Foreground = state.State switch
        {
            RunServerConnectionState.Available => Brushes.LimeGreen,
            RunServerConnectionState.Connecting => Brushes.Cyan,
            _ => Brushes.Red
        };
        if (editor.SelectorItem.Content is Grid selectorContent && selectorContent.Children.Count > 1 &&
            selectorContent.Children[1] is TextBlock selectorStatus)
        {
            selectorStatus.Text = editor.Status.Text;
            selectorStatus.Foreground = editor.Status.Foreground;
        }
        editor.Reconnect.IsEnabled = state.State == RunServerConnectionState.Disconnected && !editor.Endpoint.IsLocal;
        ToolTip.SetTip(editor.Status, state.Error);
    }

    private void ReconnectRunServer(RunServerEditor editor)
    {
        if (_runController is null)
        {
            SetConnectionError(editor, "RunServer connections are not available in this window.");
            return;
        }

        if (!int.TryParse(editor.Port.Text, out var portNumber) || portNumber < 1 || portNumber > 65535)
        {
            SetConnectionError(editor, "Invalid TCP port.");
            return;
        }

        var currentEndpoint = editor.Endpoint.Clone();
        currentEndpoint.Address = editor.Address.Text?.Trim() ?? string.Empty;
        currentEndpoint.Port = portNumber;
        currentEndpoint.Token = editor.Token.Text?.Trim() ?? string.Empty;
        currentEndpoint.CertificateFingerprint = editor.CertificateFingerprint.Text?.Trim() ?? string.Empty;
        editor.Reconnect.IsEnabled = false;
        editor.Status.Text = $"Connecting to {currentEndpoint.Address}:{currentEndpoint.Port}...";
        editor.Status.Foreground = Brushes.Cyan;
        ToolTip.SetTip(editor.Status, null);
        Console.WriteLine($"RunServer GUI connection attempt to {currentEndpoint.Address}:{currentEndpoint.Port}");
        Console.Out.Flush();
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var connected = _runController.ConnectRunServer(currentEndpoint, out var error);
                Console.WriteLine(connected
                    ? $"RunServer GUI connection succeeded to {currentEndpoint.Address}:{currentEndpoint.Port}"
                    : $"RunServer GUI connection failed to {currentEndpoint.Address}:{currentEndpoint.Port}: {error ?? "unknown error"}");
                Console.Out.Flush();
                if (!connected && !string.IsNullOrWhiteSpace(error))
                    Dispatcher.UIThread.Post(() => SetConnectionError(editor, error));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"RunServer GUI connection threw for {currentEndpoint.Address}:{currentEndpoint.Port}: {ex.Message}");
                Console.Out.Flush();
                Dispatcher.UIThread.Post(() => SetConnectionError(editor, ex.Message));
            }
        });
    }

    private static void SetConnectionError(RunServerEditor editor, string error)
    {
        editor.Status.Text = $"Disconnected: {error}";
        editor.Status.Foreground = Brushes.Red;
        editor.Reconnect.IsEnabled = !editor.Endpoint.IsLocal;
        ToolTip.SetTip(editor.Status, error);
        ToolTip.SetTip(editor.Reconnect, error);
    }

    private void EndpointSelector_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        => ShowSelectedEditor(_editors.FirstOrDefault(editor => ReferenceEquals(editor.SelectorItem, EndpointSelector.SelectedItem)));

    private void ShowSelectedEditor(RunServerEditor? editor)
    {
        foreach (var currentEditor in _editors)
        {
            DetachFromParent(currentEditor.Name);
            DetachFromParent(currentEditor.Enabled);
            DetachFromParent(currentEditor.Address);
            DetachFromParent(currentEditor.Port);
            DetachFromParent(currentEditor.Token);
            DetachFromParent(currentEditor.CertificateFingerprint);
            DetachFromParent(currentEditor.Status);
            DetachFromParent(currentEditor.Reconnect);
            DetachFromParent(currentEditor.Remove);
        }
        EndpointDetailsPanel.Children.Clear();
        if (editor is null)
            return;

        EndpointDetailsPanel.Children.Add(new TextBlock
        {
            Text = "RunServer details",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold
        });
        EndpointDetailsPanel.Children.Add(CreateField("Name", editor.Name));
        EndpointDetailsPanel.Children.Add(editor.Enabled);
        EndpointDetailsPanel.Children.Add(CreateField("Address", editor.Address));
        EndpointDetailsPanel.Children.Add(CreateField("Port", editor.Port));
        EndpointDetailsPanel.Children.Add(CreateField("Token", editor.Token));
        EndpointDetailsPanel.Children.Add(CreateField("Certificate", editor.CertificateFingerprint));
        EndpointDetailsPanel.Children.Add(CreateField("Status", editor.Status));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 8, 0, 0) };
        actions.Children.Add(editor.Reconnect);
        actions.Children.Add(editor.Remove);
        EndpointDetailsPanel.Children.Add(actions);
    }

    private static void DetachFromParent(Control control)
    {
        if (control.Parent is Panel panel)
            panel.Children.Remove(control);
    }

    private static Grid CreateField(string label, Control control)
    {
        Grid.SetColumn(control, 1);
        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("100,*"),
            ColumnSpacing = 12,
            Children =
            {
                new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center },
                control
            }
        };
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender == this && e.Handled == false && e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var endpoints = new List<RunServerEndpoint>();
            foreach (var editor in _editors)
            {
                if (string.IsNullOrWhiteSpace(editor.Address.Text) ||
                    !int.TryParse(editor.Port.Text, out var port) ||
                    port < (editor.Endpoint.IsLocal ? 0 : 1) || port > 65535)
                    throw new InvalidOperationException("Each RunServer must have a valid address and port.");

                var endpoint = editor.Endpoint.Clone();
                endpoint.Name = string.IsNullOrWhiteSpace(editor.Name.Text) ? "RunServer" : editor.Name.Text.Trim();
                endpoint.Enabled = editor.Enabled.IsChecked != false;
                endpoint.Address = editor.Address.Text.Trim();
                endpoint.Port = port;
                endpoint.Token = editor.Token.Text?.Trim() ?? string.Empty;
                endpoint.CertificateFingerprint = editor.CertificateFingerprint.Text?.Trim() ?? string.Empty;
                if (!endpoint.IsLocal && (endpoint.Token.Length < RunServerSecurity.TokenMinimumLength || endpoint.CertificateFingerprint.Length == 0))
                    throw new InvalidOperationException("Each remote RunServer must have a token and certificate fingerprint.");
                if (endpoints.Any(existing => string.Equals(existing.Address, endpoint.Address, StringComparison.OrdinalIgnoreCase) && existing.Port == endpoint.Port))
                    throw new InvalidOperationException("RunServer addresses and ports must be unique.");
                endpoints.Add(endpoint);
            }

            if (!endpoints.Any(endpoint => endpoint.IsLocal))
                throw new InvalidOperationException("The local RunServer cannot be removed.");

            Properties.Settings.Default.RunServers = endpoints;
            Properties.Settings.Default.Save();
            RunServersSaved?.Invoke();
            Close();
        }
        catch (InvalidOperationException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Unable to save RunServer settings: {ex.Message}");
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
        => Close();

    protected override void OnClosed(EventArgs e)
    {
        if (_runController is not null)
            _runController.RunServerStateChanged -= OnRunServerStateChanged;
        base.OnClosed(e);
    }
}
