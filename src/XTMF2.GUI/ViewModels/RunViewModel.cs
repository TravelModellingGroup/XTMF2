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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using XTMF2.Configuration;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XTMF2.Bus;
using XTMF2.Editing;

namespace XTMF2.GUI.ViewModels;

/// <summary>
/// Represents the execution state of a single model system run.
/// </summary>
public enum RunStatus
{
    /// <summary>The run has been submitted and is executing.</summary>
    Running,
    /// <summary>The run completed successfully.</summary>
    Finished,
    /// <summary>The run encountered an error.</summary>
    Error
}

/// <summary>
/// View model for a single model system run.
/// Tracks its ID, name, current status, and any status messages received from the client.
/// </summary>
public sealed partial class RunViewModel : ObservableObject
{
    /// <summary>The unique identifier assigned to this run by the host bus.</summary>
    public string RunId { get; }

    /// <summary>The human-readable name given to this run.</summary>
    public string RunName { get; }

    /// <summary>Current execution status.</summary>
    [ObservableProperty]
    private RunStatus _status = RunStatus.Running;

    /// <summary>
    /// The most-recently received status text from the client, or a summary of the terminal state.
    /// </summary>
    [ObservableProperty]
    private string _statusText = "Running…";

    /// <summary>All status messages received during this run (chronological order).</summary>
    public ObservableCollection<string> Messages { get; } = new();

    /// <summary>
    /// A short badge label derived from <see cref="Status"/>; shown in the run list.
    /// </summary>
    public string StatusBadge => Status switch
    {
        RunStatus.Running  => "⏳",
        RunStatus.Finished => "✅",
        RunStatus.Error    => "❌",
        _                  => "?"
    };

    private readonly ModelSystemSession _session;
    private readonly User _user;

    public RunViewModel(string runId, string runName, ModelSystemSession session, User user)
    {
        RunId   = runId;
        RunName = runName;
        _session = session;
        _user = user;
    }

    /// <summary>
    /// The resolved failing module name for the current error, if available.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ErrorNavigationLinkText))]
    private string? _errorModuleName;

    /// <summary>
    /// The canvas element ID (node/function instance/etc.) associated with the current error.
    /// </summary>
    private Guid? _errorElementId;

    /// <summary>
    /// True when this run error includes a navigable model element target.
    /// </summary>
    [ObservableProperty]
    private bool _hasErrorNavigationTarget;

    /// <summary>
    /// Link text shown in the Runs view for navigating to the failing module.
    /// </summary>
    public string ErrorNavigationLinkText =>
        string.IsNullOrWhiteSpace(ErrorModuleName)
            ? "Open failing module"
            : $"Open failing module: {ErrorModuleName}";

    // ── Optimization run support ──────────────────────────────────────────

    /// <summary>The run mode (Normal, Estimation, Calibration).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelRunCommand))]
    private RunMode _runType = RunMode.Normal;

    /// <summary>True when this is an estimation or calibration run.</summary>
    public bool IsOptimizationRun => RunType != RunMode.Normal;

    /// <summary>Current iteration number (1-based) received from the client.</summary>
    [ObservableProperty]
    private int _currentIteration;

    /// <summary>Latest fitness value received from the client.</summary>
    [ObservableProperty]
    private double _currentFitness;

    /// <summary>Live parameter values for each optimisation parameter.</summary>
    public ObservableCollection<OptimizationParameterViewModel> OptimizationParameters { get; } = new();

    /// <summary>Complete history of all completed iterations (oldest first).</summary>
    public ObservableCollection<OptimizationIterationViewModel> IterationHistory { get; } = new();

    /// <summary>The iteration currently shown in the detail panel; auto-advances with each new iteration.</summary>
    [ObservableProperty]
    private OptimizationIterationViewModel? _selectedIteration;

    /// <summary>Index-keyed lookup for fast update in <see cref="UpdateIterationProgress"/>.</summary>
    private readonly Dictionary<int, OptimizationParameterViewModel> _paramByIndex = new();

    /// <summary>Called by <see cref="RunController"/> after the run is submitted.</summary>
    internal void SetRunMode(RunMode runMode,
        IReadOnlyList<(int nodeIndex, string name, double min, double max)> meta,
        Action cancelAction)
    {
        RunType = runMode;
        _cancelAction = cancelAction;
        OptimizationParameters.Clear();
        _paramByIndex.Clear();
        foreach (var (idx, name, min, max) in meta)
        {
            var vm = new OptimizationParameterViewModel(idx, name, min, max);
            OptimizationParameters.Add(vm);
            _paramByIndex[idx] = vm;
        }
        OnPropertyChanged(nameof(IsOptimizationRun));
        CancelRunCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Called on the UI thread whenever the client sends per-iteration progress.</summary>
    internal void UpdateIterationProgress(int iteration, double fitness, IReadOnlyList<(int nodeIndex, double value)> values)
    {
        CurrentIteration = iteration;
        CurrentFitness   = fitness;
        foreach (var (idx, val) in values)
        {
            if (_paramByIndex.TryGetValue(idx, out var vm))
                vm.CurrentValue = val;
        }
        // Build an immutable snapshot for the history explorer.
        var snapshots = values
            .Select(v => _paramByIndex.TryGetValue(v.nodeIndex, out var pvm)
                ? new ParameterSnapshot(pvm.Name, pvm.MinBound, pvm.MaxBound, v.value)
                : new ParameterSnapshot("?", 0, 0, v.value))
            .ToList();
        var snap = new OptimizationIterationViewModel(iteration, fitness, snapshots);
        IterationHistory.Add(snap);
        SelectedIteration = snap;
    }

    // ── Cancel command ────────────────────────────────────────────────────

    private Action? _cancelAction;

    private bool CanCancelRun() => Status == RunStatus.Running && IsOptimizationRun;

    [RelayCommand(CanExecute = nameof(CanCancelRun))]
    private void CancelRun() => _cancelAction?.Invoke();

    // ── Optimization results ──────────────────────────────────────────────

    /// <summary>
    /// Set when an estimation or calibration run completes with parameter results.
    /// <c>null</c> for plain runs or runs that have not yet completed.
    /// </summary>
    private IReadOnlyList<(int nodeIndex, double value)>? _optimizationResults;

    /// <summary>The session that produced the optimization results, if any.</summary>
    private ModelSystemSession? _optimizationSession;

    /// <summary>The user to use when applying the optimization results.</summary>
    private User? _optimizationUser;

    /// <summary>True when optimization results are available and have not yet been applied.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyOptimizationResultsCommand))]
    private bool _hasOptimizationResults;

    /// <summary>
    /// Stores optimization results ready to be applied by the user.
    /// Called from <see cref="RunsViewModel"/> on the UI thread.
    /// </summary>
    internal void SetOptimizationResults(
        ModelSystemSession session,
        User user,
        IReadOnlyList<(int nodeIndex, double value)> results)
    {
        _optimizationSession = session;
        _optimizationUser = user;
        _optimizationResults = results;
        HasOptimizationResults = true;
        AppendStatus($"[Optimization complete] {results.Count} parameter(s) ready to apply.");
    }

    private bool CanApplyOptimizationResults() => HasOptimizationResults;

    /// <summary>
    /// Applies the optimization results back into the model system session.
    /// If the model system is currently open in the editor the canvas will
    /// update automatically via <c>Node.PropertyChanged</c>.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanApplyOptimizationResults))]
    private void ApplyOptimizationResults()
    {
        if (_optimizationResults is null || _optimizationSession is null || _optimizationUser is null)
            return;
        if (!_optimizationSession.ApplyOptimizationResults(_optimizationUser, _optimizationResults, out var error))
        {
            Messages.Add($"Failed to apply optimization results: {error?.Message}");
            return;
        }
        HasOptimizationResults = false;
        Messages.Add($"Optimization results applied ({_optimizationResults.Count} parameter(s) updated).");
    }

    /// <summary>Records a status message from the client and updates <see cref="StatusText"/>.</summary>
    internal void AppendStatus(string message)
    {
        Messages.Add(message);
        StatusText = message;
    }

    /// <summary>Marks the run as finished successfully.</summary>
    internal void MarkFinished()
    {
        Status     = RunStatus.Finished;
        StatusText = "Finished";
        OnPropertyChanged(nameof(StatusBadge));
        OnPropertyChanged(nameof(IsCompleted));
        CancelRunCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Marks the run as failed with an error message.</summary>
    internal void MarkError(string errorMessage, string stack, string? moduleName, Guid? elementId)
    {
        Status     = RunStatus.Error;
        StatusText = errorMessage;
        ErrorModuleName = moduleName;
        _errorElementId = elementId;
        HasErrorNavigationTarget = elementId.HasValue;
        if (!string.IsNullOrEmpty(stack))
            Messages.Add($"Stack trace:\n{stack}");
        Messages.Add($"Error: {errorMessage}");
        if (!string.IsNullOrWhiteSpace(moduleName))
            Messages.Add($"Failing module: {moduleName}");
        OnPropertyChanged(nameof(StatusBadge));
        OnPropertyChanged(nameof(IsCompleted));
        CancelRunCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Returns navigation context for the current failing element, when available.
    /// </summary>
    internal bool TryGetErrorNavigationTarget(out ModelSystemSession session, out User user, out Guid elementId)
    {
        session = _session;
        user = _user;
        elementId = Guid.Empty;
        if (!_errorElementId.HasValue)
            return false;
        elementId = _errorElementId.Value;
        return true;
    }

    /// <summary>True when the run has finished or errored (i.e. it is safe to remove).</summary>
    public bool IsCompleted => Status != RunStatus.Running;

    partial void OnStatusChanged(RunStatus value)
        => OnPropertyChanged(nameof(StatusBadge));
}
