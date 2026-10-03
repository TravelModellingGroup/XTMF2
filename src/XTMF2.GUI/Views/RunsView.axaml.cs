/*
    Copyright 2026 University of Toronto

    This file is part of XTMF2.

    XTMF2 is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    XTMF2 is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with XTMF2.  If not, see <http://www.gnu.org/licenses/>.
*/
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using XTMF2.GUI;
using XTMF2.GUI.ViewModels;

namespace XTMF2.GUI.Views;

public partial class RunsView : UserControl
{
    private RunsViewModel? _vm;
    private RemoteEstimationWorkersWindow? _workerWindow;

    public RunsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        _vm?.PropertyChanged -= OnVmPropertyChanged;
        _vm = DataContext as RunsViewModel;
        _vm?.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RunsViewModel.SelectedRun))
            WireSelectedRunMessages();
    }

    private INotifyCollectionChanged? _subscribedMessages;

    private void WireSelectedRunMessages()
    {
        _subscribedMessages?.CollectionChanged -= OnMessagesChanged;
        _subscribedMessages = _vm?.SelectedRun?.Messages;
        _subscribedMessages?.CollectionChanged += OnMessagesChanged;
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Keep the newest message visible at the top.
        MessageScrollViewer.ScrollToHome();
    }

    private void ViewProgressButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm?.SelectedRun is not { IsOptimizationRun: true } run) return;
        var window = new OptimizationProgressWindow(run);
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is not null)
            window.Show(owner);
        else
            window.Show();
    }

    private void EstimationWorkersButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm?.SelectedRun is not { IsRemoteSharedEstimation: true } run)
            return;
        if (_workerWindow is { } existingWindow)
        {
            existingWindow.Activate();
            return;
        }

        var window = new RemoteEstimationWorkersWindow(run);
        _workerWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_workerWindow, window))
                _workerWindow = null;
        };
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is not null)
            window.Show(owner);
        else
            window.Show();
    }

    private void OpenErrorTargetButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm?.SelectedRun is not { } run) return;
        if (!run.TryGetErrorNavigationTarget(out var session, out var user, out var elementId))
            return;

        if (TopLevel.GetTopLevel(this) is not MainWindow mainWindow)
            return;

        var editor = mainWindow.OpenModelSystemTabAndGet(session, user);
        mainWindow.FocusEditorTab(editor);
        editor.NavigateToElementById(elementId);
    }

    private void BindRecoveredRunButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm?.SelectedRun is not { IsRecoveredRunUnbound: true } run)
            return;
        if (TopLevel.GetTopLevel(this) is not MainWindow mainWindow)
            return;
        if (!mainWindow.BindRecoveredRunToActiveModelSystem(run.RunId, out var error))
        {
            run.AppendStatus(error ?? "Unable to bind the recovered run.");
            return;
        }
        run.AppendStatus("Bound to the active model system.");
    }

    private void RetryRemoteReceiptButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm?.SelectedRun is not { } run || TopLevel.GetTopLevel(this) is not MainWindow mainWindow)
            return;
        if (!mainWindow.RetryRemoteRunReceipt(run.RunId, out var error))
            run.AppendStatus(error ?? "Unable to retry the remote output receipt.");
    }

    private void TransferRemoteOutputButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm?.SelectedRun is not { } run || TopLevel.GetTopLevel(this) is not MainWindow mainWindow)
            return;
        if (!mainWindow.RequestRemoteRunOutputTransfer(run.RunId, out var error))
            run.AppendStatus(error ?? "Unable to request remote output transfer.");
    }

    private async void RemoveRunButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: RunViewModel run } || _vm is null)
            return;
        if (!run.IsRemoteRun)
        {
            _vm.RemoveRunCommand.Execute(run);
            return;
        }
        if (TopLevel.GetTopLevel(this) is not MainWindow mainWindow)
        {
            run.AppendStatus("Unable to reach the RunServer controller.");
            return;
        }

        if (!run.ArtifactsAvailable)
        {
            var confirmation = new ConfirmDialog("Delete Remote Run?",
                $"The output for '{run.RunName}' ({run.RunId}) has not been transferred to this computer. " +
                $"Deleting it from '{run.RunServer}' will permanently remove the only copy. Continue?");
            await confirmation.ShowDialog(mainWindow);
            if (!confirmation.Result)
                return;
        }

        var button = sender as Button;
        if (button is not null)
            button.IsEnabled = false;
        try
        {
            var result = await mainWindow.DeleteRemoteRunAsync(run.RunId);
            if (!result.Deleted)
            {
                run.AppendStatus(result.Error ?? "The RunServer did not delete this run.");
                return;
            }
            _vm.RemoveRunCommand.Execute(run);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
        {
            run.AppendStatus($"Unable to delete the remote run: {exception.Message}");
        }
        finally
        {
            if (button is not null)
                button.IsEnabled = run.Status != RunStatus.Running;
        }
    }

    private void ClearCompletedRunsButton_Click(object? sender, RoutedEventArgs e)
        => _ = ClearCompletedRunsAsync(sender);

    private async Task ClearCompletedRunsAsync(object? sender)
    {
        if (_vm is null || sender is not Button button)
            return;

        button.IsEnabled = false;
        try
        {
            var completedRuns = _vm.Runs.Where(run => run.IsCompleted).ToArray();
            var mainWindow = TopLevel.GetTopLevel(this) as MainWindow;
            foreach (var run in completedRuns)
            {
                if (run.IsRemoteRun)
                {
                    if (!run.ArtifactsAvailable)
                    {
                        run.AppendStatus("Kept by Clear Completed: output has not been transferred to this computer.");
                        continue;
                    }
                    if (mainWindow is null)
                    {
                        run.AppendStatus("Unable to clear remote run: the RunServer controller is unavailable.");
                        continue;
                    }

                    try
                    {
                        var result = await mainWindow.DeleteRemoteRunAsync(run.RunId);
                        if (!result.Deleted)
                        {
                            run.AppendStatus(result.Error ?? "The RunServer did not delete this run.");
                            continue;
                        }
                    }
                    catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
                    {
                        run.AppendStatus($"Unable to clear remote run: {exception.Message}");
                        continue;
                    }
                }

                _vm.RemoveRunCommand.Execute(run);
            }
        }
        finally
        {
            button.IsEnabled = true;
        }
    }
}
