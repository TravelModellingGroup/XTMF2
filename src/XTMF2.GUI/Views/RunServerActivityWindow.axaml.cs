using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTMF2.Bus;
using XTMF2.Bus.Optimization;

namespace XTMF2.GUI.Views;

public partial class RunServerActivityWindow : Window
{
    private readonly RunController? _runController;
    private readonly DispatcherTimer _refreshTimer;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _refreshing;

    public RunServerActivityWindow()
        : this(null)
    {
    }

    public RunServerActivityWindow(RunController? runController)
    {
        _runController = runController;
        InitializeComponent();
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _refreshTimer.Tick += OnRefreshTimerTick;
        Opened += OnWindowOpened;
    }

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        _refreshTimer.Start();
        await RefreshAsync();
    }

    private async void OnRefreshTimerTick(object? sender, EventArgs e)
        => await RefreshAsync();

    private async void Refresh_Click(object? sender, RoutedEventArgs e)
        => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_refreshing || _lifetime.IsCancellationRequested)
            return;
        _refreshing = true;
        RefreshButton.IsEnabled = false;
        StatusText.Text = "Querying connected RunServers...";
        try
        {
            if (_runController is null)
            {
                ServerList.Children.Clear();
                StatusText.Text = "Run controller is unavailable.";
                return;
            }

            var servers = await _runController.QueryRunServerActivityAsync(_lifetime.Token);
            if (_lifetime.IsCancellationRequested)
                return;
            RenderServers(servers);
            UpdatedText.Text = $"Updated {DateTime.Now.ToString("HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture)}";
            var connectedCount = servers.Count(server => server.ConnectionState == RunServerConnectionState.Available);
            StatusText.Text = $"{connectedCount} of {servers.Count} configured RunServers connected.";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Activity refresh failed: {exception.Message}";
        }
        finally
        {
            _refreshing = false;
            if (!_lifetime.IsCancellationRequested)
                RefreshButton.IsEnabled = true;
        }
    }

    internal void RenderServers(System.Collections.Generic.IReadOnlyList<RunServerActivityServerSnapshot> servers)
    {
        ServerList.Children.Clear();
        if (servers.Count == 0)
        {
            ServerList.Children.Add(new TextBlock { Text = "No RunServers are configured.", Opacity = 0.7 });
            return;
        }

        foreach (var server in servers.OrderBy(item => item.Endpoint.Name, StringComparer.OrdinalIgnoreCase))
        {
            var content = new StackPanel { Spacing = 8 };
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            header.Children.Add(new TextBlock
            {
                Text = server.Endpoint.Name,
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            var connectionStatus = new TextBlock
            {
                Text = server.ConnectionState switch
                {
                    RunServerConnectionState.Available when server.Error is null => "Connected",
                    RunServerConnectionState.Connecting => "Connecting",
                    RunServerConnectionState.Disconnected => "Disconnected",
                    _ => "Query error"
                },
                Foreground = server.ConnectionState == RunServerConnectionState.Available && server.Error is null
                    ? Brushes.LimeGreen
                    : Brushes.OrangeRed,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(connectionStatus, 1);
            header.Children.Add(connectionStatus);
            content.Children.Add(header);

            var endpointAddress = server.Endpoint.IsLocal
                ? "Local RunServer"
                : $"{server.Endpoint.Address}:{server.Endpoint.Port}";
            content.Children.Add(new TextBlock { Text = endpointAddress, FontSize = 11, Opacity = 0.58 });

            if (!string.IsNullOrWhiteSpace(server.Error))
            {
                content.Children.Add(new TextBlock
                {
                    Text = server.Error,
                    Foreground = Brushes.OrangeRed,
                    TextWrapping = TextWrapping.Wrap
                });
            }
            else if (server.ConnectionState == RunServerConnectionState.Available)
            {
                var activities = server.Activities
                    .OrderBy(item => item.State == RunServerActivityState.Running ? 0 : 1)
                    .ThenBy(item => item.QueuePosition)
                    .ToArray();
                if (activities.Length == 0)
                {
                    content.Children.Add(new TextBlock { Text = "No active or queued jobs.", Opacity = 0.65 });
                }
                else
                {
                    foreach (var activity in activities)
                        content.Children.Add(CreateActivityRow(activity, server.Endpoint.Id, server.Endpoint.Name));
                }
            }

            ServerList.Children.Add(new Border
            {
                BorderBrush = (IBrush?)Resources["DlgSep"] ?? Brushes.Gray,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 0, 12),
                Child = content
            });
        }
    }

    private Control CreateActivityRow(RunServerActivity activity, string endpointId, string serverName)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("100,190,*,Auto,Auto"),
            ColumnSpacing = 12,
            Margin = new Thickness(0, 4, 0, 4)
        };
        row.Classes.Add("activity-row");
        var state = activity.State == RunServerActivityState.Running
            ? "Running"
            : $"Queued #{activity.QueuePosition}";
        row.Children.Add(new TextBlock
        {
            Text = state,
            Foreground = activity.State == RunServerActivityState.Running ? Brushes.LimeGreen : Brushes.DarkOrange,
            VerticalAlignment = VerticalAlignment.Top
        });
        var identity = new StackPanel { Spacing = 2 };
        identity.Children.Add(new TextBlock
        {
            Text = activity.RunName,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        identity.Children.Add(new TextBlock { Text = activity.RunId, FontSize = 10, Opacity = 0.55 });
        Grid.SetColumn(identity, 1);
        row.Children.Add(identity);
        var detail = new StackPanel { Spacing = 2 };
        detail.Children.Add(new TextBlock { Text = activity.Kind, FontSize = 11, Opacity = 0.7 });
        detail.Children.Add(new TextBlock { Text = activity.Status, TextWrapping = TextWrapping.Wrap });
        if (activity.Iteration > 0 || activity.EvaluationsCompleted > 0)
        {
            var progress = activity.Iteration > 0 ? $"Iteration {activity.Iteration}" : string.Empty;
            if (activity.EvaluationsCompleted > 0)
                progress += $"  |  {activity.EvaluationsCompleted} evaluated";
            if (activity.ActiveWorkers > 0)
                progress += $"  |  {activity.ActiveWorkers} worker(s)";
            detail.Children.Add(new TextBlock { Text = progress, FontSize = 10, Opacity = 0.6 });
        }
        Grid.SetColumn(detail, 2);
        row.Children.Add(detail);
        if (double.IsFinite(activity.Fitness))
        {
            var fitness = new TextBlock
            {
                Text = $"Fitness {activity.Fitness:G6}",
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(fitness, 3);
            row.Children.Add(fitness);
        }
        ToolTip.SetTip(identity, activity.RunId);
        var killButton = new Button
        {
            Name = "KillActivityButton",
            Content = "✕",
            Width = 30,
            Height = 30,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center
        };
        killButton.Classes.Add("dlg-danger");
        ToolTip.SetTip(killButton, "Kill this run on the RunServer.");
        killButton.Click += async (_, _) => await KillActivityAsync(endpointId, serverName, activity, killButton);
        Grid.SetColumn(killButton, 4);
        row.Children.Add(killButton);
        return row;
    }

    private async Task KillActivityAsync(string endpointId, string serverName,
        RunServerActivity activity, Button button)
    {
        if (_runController is null)
            return;

        var confirmation = new ConfirmDialog("Stop RunServer Job?",
            $"Stop '{activity.RunName}' ({activity.RunId}) on '{serverName}'? Active work may be interrupted.");
        await confirmation.ShowDialog(this);
        if (!confirmation.Result)
            return;

        button.IsEnabled = false;
        StatusText.Text = $"Sending stop request for '{activity.RunName}'...";
        if (!_runController.KillRunServerActivity(endpointId, activity, out var error))
        {
            StatusText.Text = $"Unable to stop '{activity.RunName}': {error}";
            button.IsEnabled = true;
            return;
        }

        StatusText.Text = $"Stop requested for '{activity.RunName}'.";
        await RefreshAsync();
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _refreshTimer.Stop();
        _lifetime.Cancel();
        _lifetime.Dispose();
        base.OnClosed(e);
    }
}