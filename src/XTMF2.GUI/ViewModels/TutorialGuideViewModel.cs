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
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XTMF2;
using XTMF2.Editing;
using XTMF2.GUI.Tutorials;

namespace XTMF2.GUI.ViewModels;

public sealed partial class TutorialGuideViewModel : ObservableObject, IDisposable
{
    private readonly ModelSystemEditorViewModel _editor;
    private readonly TutorialDefinition _tutorial;
    private readonly Action<TutorialDefinition>? _onCompleted;
    private readonly HashSet<NodeViewModel> _trackedNodes = new();
    private readonly Dictionary<int, StepBaseline> _stepBaselines = new();
    private int _stepIndex;
    private bool _isWhyThisMattersVisible;
    private bool _isVisible = true;
    private bool _disposed;

    public TutorialGuideViewModel(
        ModelSystemEditorViewModel editor,
        TutorialDefinition tutorial,
        Action<TutorialDefinition>? onCompleted = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _tutorial = tutorial ?? throw new ArgumentNullException(nameof(tutorial));
        _onCompleted = onCompleted;

        editor.Nodes.CollectionChanged += OnNodesChanged;
        editor.FunctionTemplates.CollectionChanged += OnFunctionTemplatesChanged;
        editor.FunctionInstances.CollectionChanged += OnFunctionInstancesChanged;
        editor.CommentBlocks.CollectionChanged += OnCommentsChanged;
        editor.PropertyChanged += OnEditorPropertyChanged;
        ((INotifyPropertyChanged)editor.Session).PropertyChanged += OnSessionChanged;
        editor.ModelSystemRunCompleted += OnModelSystemRunCompleted;
        CaptureStepBaseline(0);
        UpdateWhyThisMattersVisibility();
    }

    private bool IsFinished => _stepIndex >= _tutorial.Steps.Count;
    private TutorialStepDefinition? CurrentStep => IsFinished ? null : _tutorial.Steps[_stepIndex];

    public string Title => CurrentStep?.Title ?? $"{_tutorial.Title} complete";
    public string Instructions => CurrentStep?.Instructions ?? _tutorial.Description;
    public string WhyThisMatters => CurrentStep?.Hint ?? string.Empty;
    public string StepProgress => IsFinished ? "Complete" : $"Step {_stepIndex + 1} of {_tutorial.Steps.Count}";
    public string? TargetId => IsVisible ? CurrentStep?.TargetId : null;
    public string TargetLabel => TargetId switch
    {
        TutorialTargetIds.Canvas => "Try this on the model canvas",
        TutorialTargetIds.SaveButton => "Save from the toolbar or press Ctrl+S",
        TutorialTargetIds.RunButton => "Run from the toolbar or press F5",
        { } targetId => targetId,
        _ => string.Empty
    };

    public string? CompletionConditionWarning
        => CurrentStep is { } step && !IsSupportedCondition(step.Completion.Kind)
            ? $"This tutorial step uses an unsupported completion condition: '{step.Completion.Kind}'."
            : null;

    public bool IsVisible
    {
        get => _isVisible;
        private set
        {
            if (SetProperty(ref _isVisible, value))
            {
                OnPropertyChanged(nameof(TargetId));
                OnPropertyChanged(nameof(IsCanvasTarget));
            }
        }
    }

    public bool IsWhyThisMattersVisible
    {
        get => _isWhyThisMattersVisible;
        private set
        {
            if (SetProperty(ref _isWhyThisMattersVisible, value))
                OnPropertyChanged(nameof(CanRevealWhyThisMatters));
        }
    }

    public bool CanShowWhyThisMatters => !string.IsNullOrWhiteSpace(WhyThisMatters);
    public bool CanRevealWhyThisMatters => CanShowWhyThisMatters && !IsWhyThisMattersVisible;
    public bool IsCanvasTarget => IsVisible && CurrentStep?.TargetId == TutorialTargetIds.Canvas;
    public bool CanGoBack => _stepIndex > 0;
    public bool CanSkip => CurrentStep?.SkipAction is not null;
    public bool IsStepComplete => IsFinished || CurrentStep is { } step && IsConditionComplete(step.Completion);
    public bool CanAdvance => IsFinished || IsStepComplete;
    public string NextButtonText => IsFinished ? "Done"
        : _stepIndex == _tutorial.Steps.Count - 1 ? "Finish"
        : "Next";

    private bool IsConditionComplete(TutorialCompletionCondition condition)
        => condition.Kind switch
        {
            TutorialCompletionKinds.Manual => true,
            TutorialCompletionKinds.ModuleAdded => _editor.Nodes
                .Any(node => !_stepBaselines[_stepIndex].Nodes.Contains(node)
                    && MatchesTarget(node, condition.Target)),
            TutorialCompletionKinds.ModuleRenamed => _stepBaselines[_stepIndex].NodeNames
                .Any(pair => _editor.Nodes.Contains(pair.Key)
                    && MatchesTarget(pair.Key, condition.Target)
                    && !string.Equals(pair.Key.Name, pair.Value, StringComparison.Ordinal)),
            TutorialCompletionKinds.StartAdded => _editor.Starts
                .Any(start => !_stepBaselines[_stepIndex].Starts.Contains(start)),
            TutorialCompletionKinds.LinkAdded => _editor.Links
                .Any(link => !_stepBaselines[_stepIndex].Links.Contains(link)
                    && MatchesTarget(link, condition.Target)),
            TutorialCompletionKinds.FunctionTemplateAdded => _editor.FunctionTemplates
                .Any(template => !_stepBaselines[_stepIndex].FunctionTemplates.Contains(template)
                    && MatchesTarget(template.Name, condition.Target)),
            TutorialCompletionKinds.FunctionInstanceAdded => _editor.FunctionInstances
                .Any(instance => !_stepBaselines[_stepIndex].FunctionInstances.Contains(instance)
                    && MatchesTarget(instance.UnderlyingInstance.Template.Name, condition.Target)),
            TutorialCompletionKinds.FunctionTemplateOpened => !_stepBaselines[_stepIndex].WasInsideFunctionTemplate
                && _editor.IsInsideFunctionTemplate,
            TutorialCompletionKinds.FunctionTemplateClosed => _stepBaselines[_stepIndex].WasInsideFunctionTemplate
                && !_editor.IsInsideFunctionTemplate,
            TutorialCompletionKinds.CommentAdded => _editor.CommentBlocks
                .Any(comment => !_stepBaselines[_stepIndex].Comments.Contains(comment)),
            TutorialCompletionKinds.ModelSystemSaved => _stepBaselines[_stepIndex].HasSeenDirty
                && !_editor.Session.IsDirty,
            TutorialCompletionKinds.ModelSystemRunCompleted => _stepBaselines[_stepIndex].HasCompletedRun,
            _ => false
        };

    private static bool MatchesTarget(NodeViewModel node, string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return true;

        var type = node.UnderlyingNode.Type;
        return string.Equals(type.AssemblyQualifiedName, target, StringComparison.Ordinal)
            || string.Equals(type.FullName, target, StringComparison.Ordinal)
            || string.Equals(type.Name, target, StringComparison.Ordinal);
    }

    private static bool MatchesTarget(LinkViewModel link, string? target)
        => string.IsNullOrWhiteSpace(target)
            || string.Equals(link.UnderlyingLink.OriginHook.Name, target, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesTarget(string name, string? target)
        => string.IsNullOrWhiteSpace(target)
            || string.Equals(name, target, StringComparison.Ordinal);

    private static bool IsSupportedCondition(string kind)
        => kind is TutorialCompletionKinds.Manual
            or TutorialCompletionKinds.ModuleAdded
            or TutorialCompletionKinds.ModuleRenamed
            or TutorialCompletionKinds.StartAdded
            or TutorialCompletionKinds.LinkAdded
            or TutorialCompletionKinds.FunctionTemplateAdded
            or TutorialCompletionKinds.FunctionInstanceAdded
            or TutorialCompletionKinds.FunctionTemplateOpened
            or TutorialCompletionKinds.FunctionTemplateClosed
            or TutorialCompletionKinds.CommentAdded
            or TutorialCompletionKinds.ModelSystemSaved
            or TutorialCompletionKinds.ModelSystemRunCompleted;

    private void CaptureStepBaseline(int stepIndex)
    {
        if (_stepBaselines.ContainsKey(stepIndex))
            return;

        var baseline = new StepBaseline(
            _editor.Nodes.ToHashSet(),
            _editor.Starts.ToHashSet(),
            _editor.Links.ToHashSet(),
            _editor.FunctionTemplates.ToHashSet(),
            _editor.FunctionInstances.ToHashSet(),
            _editor.CommentBlocks.ToHashSet(),
            _editor.Nodes.ToDictionary(node => node, node => node.Name),
            _editor.Session.IsDirty,
            _editor.IsInsideFunctionTemplate);
        _stepBaselines.Add(stepIndex, baseline);

        foreach (var node in baseline.Nodes)
        {
            if (_trackedNodes.Add(node))
                node.PropertyChanged += OnTutorialNodeChanged;
        }
    }

    private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var node in e.OldItems.OfType<NodeViewModel>())
            {
                if (_trackedNodes.Remove(node))
                    node.PropertyChanged -= OnTutorialNodeChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var node in e.NewItems.OfType<NodeViewModel>())
            {
                if (_trackedNodes.Add(node))
                {
                    node.PropertyChanged += OnTutorialNodeChanged;
                }
            }
        }

        if (_stepBaselines.TryGetValue(_stepIndex, out var baseline) && e.NewItems is not null)
        {
            foreach (var node in e.NewItems.OfType<NodeViewModel>())
                baseline.NodeNames.TryAdd(node, node.Name);
        }

        RefreshState();
    }

    private void OnTutorialNodeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NodeViewModel.Name))
            RefreshState();
    }

    private void OnCommentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RefreshState();

    private void OnFunctionTemplatesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RefreshState();

    private void OnFunctionInstancesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RefreshState();

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ModelSystemEditorViewModel.IsInsideFunctionTemplate))
            RefreshState();
    }

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(ModelSystemSession.IsDirty) or nameof(ModelSystemSession.ChangeCount)))
            return;

        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(RefreshState);
            return;
        }

        RefreshState();
    }

    private void OnModelSystemRunCompleted(bool succeeded)
    {
        if (!succeeded)
            return;

        void CompleteOnUiThread()
        {
            if (_disposed || CurrentStep?.Completion.Kind != TutorialCompletionKinds.ModelSystemRunCompleted)
                return;

            _stepBaselines[_stepIndex].HasCompletedRun = true;
            RefreshState();
        }

        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(CompleteOnUiThread);
            return;
        }

        CompleteOnUiThread();
    }

    private void RefreshState()
    {
        if (!IsFinished && _editor.Session.IsDirty)
            _stepBaselines[_stepIndex].HasSeenDirty = true;

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Instructions));
        OnPropertyChanged(nameof(WhyThisMatters));
        OnPropertyChanged(nameof(StepProgress));
        OnPropertyChanged(nameof(TargetId));
        OnPropertyChanged(nameof(TargetLabel));
        OnPropertyChanged(nameof(CompletionConditionWarning));
        OnPropertyChanged(nameof(CanShowWhyThisMatters));
        OnPropertyChanged(nameof(CanRevealWhyThisMatters));
        OnPropertyChanged(nameof(IsCanvasTarget));
        OnPropertyChanged(nameof(IsStepComplete));
        OnPropertyChanged(nameof(CanAdvance));
        OnPropertyChanged(nameof(CanSkip));
        OnPropertyChanged(nameof(NextButtonText));
        NextCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
        SkipCommand.NotifyCanExecuteChanged();
        ShowWhyThisMattersCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanAdvance))]
    private void Next()
    {
        if (IsFinished)
        {
            Exit();
            return;
        }
        if (!IsStepComplete)
            return;

        _stepIndex++;
        if (IsFinished)
            _onCompleted?.Invoke(_tutorial);
        CaptureStepBaseline(_stepIndex);
        UpdateWhyThisMattersVisibility();
        RefreshState();
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        if (!CanGoBack)
            return;

        _stepIndex--;
        CaptureStepBaseline(_stepIndex);
        UpdateWhyThisMattersVisibility();
        RefreshState();
    }

    [RelayCommand(CanExecute = nameof(CanSkip))]
    private void Skip()
    {
        if (CurrentStep?.SkipAction is not { } skipAction)
            return;

        var result = skipAction(new ModelSystemTutorialSkipContext(_editor));
        if (result is null || !result.Succeeded)
        {
            _editor.ShowToast(result?.Error ?? "The tutorial could not prepare the next step.",
                isError: true,
                durationMs: 6000);
            return;
        }

        _stepIndex++;
        if (IsFinished)
            _onCompleted?.Invoke(_tutorial);
        CaptureStepBaseline(_stepIndex);
        UpdateWhyThisMattersVisibility();
        RefreshState();
    }

    private void UpdateWhyThisMattersVisibility()
    {
        IsWhyThisMattersVisible = CanShowWhyThisMatters
            && _tutorial.Id == GettingStartedTutorialProvider.TutorialId;
    }

    [RelayCommand(CanExecute = nameof(CanRevealWhyThisMatters))]
    private void ShowWhyThisMatters()
    {
        IsWhyThisMattersVisible = true;
    }

    [RelayCommand]
    private void Exit()
    {
        IsVisible = false;
        RefreshState();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _editor.Nodes.CollectionChanged -= OnNodesChanged;
        _editor.FunctionTemplates.CollectionChanged -= OnFunctionTemplatesChanged;
        _editor.FunctionInstances.CollectionChanged -= OnFunctionInstancesChanged;
        _editor.CommentBlocks.CollectionChanged -= OnCommentsChanged;
        _editor.PropertyChanged -= OnEditorPropertyChanged;
        ((INotifyPropertyChanged)_editor.Session).PropertyChanged -= OnSessionChanged;
        _editor.ModelSystemRunCompleted -= OnModelSystemRunCompleted;
        foreach (var node in _trackedNodes)
            node.PropertyChanged -= OnTutorialNodeChanged;
        _trackedNodes.Clear();
    }

    private sealed class StepBaseline(
        HashSet<NodeViewModel> nodes,
        HashSet<StartViewModel> starts,
        HashSet<LinkViewModel> links,
        HashSet<FunctionTemplateViewModel> functionTemplates,
        HashSet<FunctionInstanceViewModel> functionInstances,
        HashSet<CommentBlockViewModel> comments,
        Dictionary<NodeViewModel, string> nodeNames,
        bool hasSeenDirty,
        bool wasInsideFunctionTemplate)
    {
        public HashSet<NodeViewModel> Nodes { get; } = nodes;
        public HashSet<StartViewModel> Starts { get; } = starts;
        public HashSet<LinkViewModel> Links { get; } = links;
        public HashSet<FunctionTemplateViewModel> FunctionTemplates { get; } = functionTemplates;
        public HashSet<FunctionInstanceViewModel> FunctionInstances { get; } = functionInstances;
        public HashSet<CommentBlockViewModel> Comments { get; } = comments;
        public Dictionary<NodeViewModel, string> NodeNames { get; } = nodeNames;
        public bool HasSeenDirty { get; set; } = hasSeenDirty;
        public bool HasCompletedRun { get; set; }
        public bool WasInsideFunctionTemplate { get; } = wasInsideFunctionTemplate;
    }
}
