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
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

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

    public RunViewModel(string runId, string runName)
    {
        RunId   = runId;
        RunName = runName;
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
    }

    /// <summary>Marks the run as failed with an error message.</summary>
    internal void MarkError(string errorMessage, string stack)
    {
        Status     = RunStatus.Error;
        StatusText = errorMessage;
        if (!string.IsNullOrEmpty(stack))
            Messages.Add($"Stack trace:\n{stack}");
        Messages.Add($"Error: {errorMessage}");
        OnPropertyChanged(nameof(StatusBadge));
        OnPropertyChanged(nameof(IsCompleted));
    }

    /// <summary>True when the run has finished or errored (i.e. it is safe to remove).</summary>
    public bool IsCompleted => Status != RunStatus.Running;

    partial void OnStatusChanged(RunStatus value)
        => OnPropertyChanged(nameof(StatusBadge));
}
