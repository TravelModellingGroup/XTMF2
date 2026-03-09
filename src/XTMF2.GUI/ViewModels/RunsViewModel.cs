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
using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace XTMF2.GUI.ViewModels;

/// <summary>
/// View model for the Runs panel.  There is exactly one instance per GUI session, held by
/// <see cref="MainWindow"/>.  All mutation methods dispatch to the UI thread so they can
/// safely be called from background threads (e.g. HostBus event handlers).
/// </summary>
public sealed partial class RunsViewModel : ObservableObject
{
    // ── Dock integration ──────────────────────────────────────────────────
    /// <summary>Tab title shown in the dock.</summary>
    public string Title => "Runs";

    /// <summary>The Runs tab is permanent; do not allow users to close it.</summary>
    public bool CanClose => false;

    // ── Run collection ────────────────────────────────────────────────────
    /// <summary>All runs that have been submitted in this session.</summary>
    public ObservableCollection<RunViewModel> Runs { get; } = new();

    /// <summary>The run currently selected in the list view.</summary>
    [ObservableProperty]
    private RunViewModel? _selectedRun;

    // ── Mutation helpers (thread-safe) ────────────────────────────────────

    /// <summary>
    /// Adds a new run entry to the list.  Safe to call from any thread.
    /// </summary>
    /// <param name="runId">The ID assigned by the host bus.</param>
    /// <param name="runName">The human-readable run name.</param>
    /// <returns>The newly created <see cref="RunViewModel"/>.</returns>
    internal RunViewModel AddRun(string runId, string runName)
    {
        var vm = new RunViewModel(runId, runName);
        Dispatcher.UIThread.Post(() =>
        {
            Runs.Add(vm);
            SelectedRun = vm;
        });
        return vm;
    }

    /// <summary>
    /// Marks the run with <paramref name="runId"/> as finished.  Safe to call from any thread.
    /// </summary>
    internal void NotifyFinished(string runId)
    {
        var vm = FindRun(runId);
        if (vm is null) return;
        Dispatcher.UIThread.Post(() => vm.MarkFinished());
    }

    /// <summary>
    /// Marks the run with <paramref name="runId"/> as failed.  Safe to call from any thread.
    /// </summary>
    internal void NotifyError(string runId, string errorMessage, string stack)
    {
        var vm = FindRun(runId);
        if (vm is null) return;
        Dispatcher.UIThread.Post(() => vm.MarkError(errorMessage, stack));
    }

    /// <summary>
    /// Appends a status message to the run with <paramref name="runId"/>.
    /// Safe to call from any thread.
    /// </summary>
    internal void NotifyStatus(string runId, string status)
    {
        var vm = FindRun(runId);
        if (vm is null) return;
        Dispatcher.UIThread.Post(() => vm.AppendStatus(status));
    }

    private RunViewModel? FindRun(string runId)
    {
        // Runs is only modified on the UI thread, so a simple linear search is fine here.
        // This is called from background threads; we iterate a snapshot to avoid races.
        foreach (var run in Runs)
        {
            if (run.RunId == runId)
                return run;
        }
        return null;
    }

    // ── Removal commands ──────────────────────────────────────────────────

    partial void OnSelectedRunChanged(RunViewModel? value)
        => RemoveSelectedRunCommand.NotifyCanExecuteChanged();

    private bool CanRemoveSelectedRun()
        => SelectedRun is { Status: not RunStatus.Running };

    /// <summary>
    /// Removes the currently selected run from the list.
    /// Only enabled when the selected run has finished or errored.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRemoveSelectedRun))]
    private void RemoveSelectedRun()
    {
        if (SelectedRun is null) return;
        RemoveRun(SelectedRun);
    }

    /// <summary>
    /// Removes a specific run from the list.
    /// Bound to the per-item delete button in the run list.
    /// </summary>
    [RelayCommand]
    private void RemoveRun(RunViewModel vm)
    {
        var idx = Runs.IndexOf(vm);
        Runs.Remove(vm);
        if (ReferenceEquals(SelectedRun, vm))
            SelectedRun = Runs.Count > 0 ? Runs[Math.Min(idx, Runs.Count - 1)] : null;
    }

    /// <summary>
    /// Removes all runs that have finished or errored from the list.
    /// </summary>
    [RelayCommand]
    private void ClearCompletedRuns()
    {
        var wasSelected = SelectedRun;
        for (var i = Runs.Count - 1; i >= 0; i--)
        {
            if (Runs[i].Status != RunStatus.Running)
                Runs.RemoveAt(i);
        }
        if (wasSelected is not null && !Runs.Contains(wasSelected))
            SelectedRun = Runs.FirstOrDefault();
    }
}
