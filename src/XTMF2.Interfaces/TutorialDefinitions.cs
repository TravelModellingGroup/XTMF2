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
using System.Linq;

namespace XTMF2;

/// <summary>
/// Supplies tutorial definitions that can be consumed by the XTMF2 GUI.
/// Module libraries may implement this contract without depending on the GUI.
/// Implementations must have a public parameterless constructor for discovery.
/// </summary>
public interface ITutorialProvider
{
    IEnumerable<TutorialDefinition> GetTutorials();
}

/// <summary>
/// Describes one tutorial course independently of its GUI presentation.
/// </summary>
public sealed class TutorialDefinition
{
    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public IReadOnlyList<TutorialStepDefinition> Steps { get; }
    /// <summary>
    /// Optional model-state preparation run in the tutorial's isolated model system
    /// before the first step is shown.
    /// </summary>
    public TutorialInitializationAction? InitializeAction { get; }
    /// <summary>
    /// Position within the contributing library. Unspecified order sorts after
    /// explicit orders and is resolved alphabetically by title.
    /// </summary>
    public int Order { get; }

    public TutorialDefinition(
        string id,
        string title,
        string description,
        IEnumerable<TutorialStepDefinition> steps)
        : this(id, title, description, steps, int.MaxValue, null)
    {
    }

    public TutorialDefinition(
        string id,
        string title,
        string description,
        IEnumerable<TutorialStepDefinition> steps,
        int order,
        TutorialInitializationAction? initializeAction = null)
    {
        Id = RequireText(id, nameof(id));
        Title = RequireText(title, nameof(title));
        Description = RequireText(description, nameof(description));
        ArgumentNullException.ThrowIfNull(steps);
        if (order < 1)
            throw new ArgumentOutOfRangeException(nameof(order), "Tutorial order must be a positive number.");
        Order = order;
        InitializeAction = initializeAction;
        Steps = Array.AsReadOnly(steps.ToArray());
        if (Steps.Count == 0)
            throw new ArgumentException("A tutorial must contain at least one step.", nameof(steps));
        if (Steps.Any(step => step is null))
            throw new ArgumentException("Tutorial steps cannot contain null entries.", nameof(steps));
        if (Steps.Select(step => step.Id).Distinct(StringComparer.Ordinal).Count() != Steps.Count)
            throw new ArgumentException("Tutorial step IDs must be unique within a tutorial.", nameof(steps));
    }

    private static string RequireText(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }
}

/// <summary>
/// A tutorial instruction, optional explanatory note, semantic UI target, and
/// state condition used by the GUI to determine when the learner may continue.
/// </summary>
public sealed class TutorialStepDefinition
{
    public string Id { get; }
    public string Title { get; }
    public string Instructions { get; }
    /// <summary>
    /// Optional explanatory text displayed as "Why this matters" in the tutorial guide.
    /// </summary>
    public string? Hint { get; }
    public string? TargetId { get; }
    public TutorialCompletionCondition Completion { get; }
    /// <summary>
    /// Optional action that prepares the model state for the next step when this
    /// step is skipped. When omitted, the GUI does not offer Skip for this step.
    /// </summary>
    public TutorialStepSkipAction? SkipAction { get; init; }

    public TutorialStepDefinition(
        string id,
        string title,
        string instructions,
        TutorialCompletionCondition completion,
        string? hint = null,
        string? targetId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(instructions);
        Id = id;
        Title = title;
        Instructions = instructions;
        Completion = completion ?? throw new ArgumentNullException(nameof(completion));
        Hint = hint;
        TargetId = targetId;
    }
}

/// <summary>
/// Runs a tutorial step's explicit model-state preparation when the learner skips it.
/// </summary>
public delegate TutorialSkipResult TutorialStepSkipAction(ITutorialSkipContext context);

/// <summary>
/// Prepares the isolated model state for a tutorial before its first step is shown.
/// </summary>
public delegate TutorialSkipResult TutorialInitializationAction(ITutorialSkipContext context);

/// <summary>
/// Provides safe, command-pattern model edits for tutorial skip actions.
/// </summary>
public interface ITutorialSkipContext
{
    TutorialSkipResult EnsureStart(string name);
    TutorialSkipResult EnsureModule(Type moduleType, string name);
    TutorialSkipResult EnsureLink(string originName, string hookName, string destinationName);
    TutorialSkipResult EnsureEstimationParameter(
        string parameterNodeName, double min, double max, double nullHypothesis);
    TutorialSkipResult EnsureCalibrationParameter(
        string parameterNodeName,
        string modelOutputNodeName,
        string targetOutputNodeName,
        double min,
        double max)
        => TutorialSkipResult.Failure("This tutorial provider does not support preparing calibration parameters.");
    TutorialSkipResult EnsureParameterValue(string parameterNodeName, string value);
    TutorialSkipResult EnsureModelSystemVariable(string nodeName);
    TutorialSkipResult EnsureScriptedParameter(string parameterNodeName, Type scriptedParameterType, string expression);
    TutorialSkipResult EnsureFunctionInstance(string templateName, string name);
    TutorialSkipResult EnsureComment(string header, string body);
    TutorialSkipResult SaveModelSystem();
}

/// <summary>
/// The outcome of a tutorial skip action or one of its model edits.
/// </summary>
public sealed class TutorialSkipResult
{
    private TutorialSkipResult(bool succeeded, string? error)
    {
        Succeeded = succeeded;
        Error = error;
    }

    public bool Succeeded { get; }
    public string? Error { get; }
    public static TutorialSkipResult Success { get; } = new(true, null);

    public static TutorialSkipResult Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new TutorialSkipResult(false, error);
    }
}

/// <summary>
/// Identifies a state-based completion condition. <see cref="Target"/> is
/// interpreted by the GUI condition handler for the specified kind.
/// </summary>
public sealed class TutorialCompletionCondition
{
    public string Kind { get; }
    public string? Target { get; }

    public TutorialCompletionCondition(string kind, string? target = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        Kind = kind;
        Target = target;
    }
}

/// <summary>
/// Built-in completion condition identifiers supported by the XTMF2 GUI.
/// </summary>
public static class TutorialCompletionKinds
{
    public const string Manual = "manual";
    public const string ModuleAdded = "module-added";
    public const string ModuleRenamed = "module-renamed";
    public const string StartAdded = "start-added";
    public const string LinkAdded = "link-added";
    public const string FunctionTemplateAdded = "function-template-added";
    public const string FunctionInstanceAdded = "function-instance-added";
    public const string FunctionTemplateOpened = "function-template-opened";
    public const string FunctionTemplateClosed = "function-template-closed";
    public const string CommentAdded = "comment-added";
    public const string EstimationParameterAdded = "estimation-parameter-added";
    public const string EstimationConfigured = "estimation-configured";
    public const string EstimationRunCompleted = "estimation-run-completed";
    public const string CalibrationParameterAdded = "calibration-parameter-added";
    public const string CalibrationConfigured = "calibration-configured";
    public const string CalibrationRunCompleted = "calibration-run-completed";
    public const string CalibrationParameterApplied = "calibration-parameter-applied";
    public const string ParameterValueChanged = "parameter-value-changed";
    public const string ModelSystemSaved = "model-system-saved";
    public const string ModelSystemRunCompleted = "model-system-run-completed";
}
