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
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XTMF2;
using XTMF2.Editing;
using XTMF2.GUI.Controls;
using XTMF2.GUI.Resources;
using XTMF2.GUI.Views;
using XTMF2.ModelSystemConstruct;
using XTMF2.RuntimeModules;

namespace XTMF2.GUI.ViewModels;

/// <summary>
/// Holds the data captured for a single node (and its inlined parameter children)
/// at the moment it is copied, ready to be replicated by a paste operation.
/// </summary>
/// <param name="Name">The node's name at copy time.</param>
/// <param name="Type">The CLR type of the node's module.</param>
/// <param name="OriginalLocation">The canvas rectangle of the node at copy time.</param>
/// <param name="ParameterValue">
/// The string representation of the parameter value, or <c>null</c> when the node
/// is not a parameter node.
/// </param>
/// <param name="IsScriptedParam">
/// <c>true</c> when the parameter is a <c>ScriptedParameter</c>; <c>false</c>
/// for a <c>BasicParameter</c>.  Ignored when <paramref name="ParameterValue"/> is <c>null</c>.
/// </param>
/// <param name="InlinedChildren">
/// One entry per hidden (inlined) child node that was connected to this node's hook
/// at copy time.  The tuple stores the hook name and the child's own copy data.
/// </param>
internal sealed record NodePasteEntry(
    string Name,
    Type Type,
    Rectangle OriginalLocation,
    string? ParameterValue,
    bool IsScriptedParam,
    IReadOnlyList<(string HookName, NodePasteEntry Child)> InlinedChildren);

/// <summary>
/// View model for editing a single model system. Owns the <see cref="ModelSystemSession"/>
/// and disposes it when the tab is closed.
/// </summary>
public sealed partial class ModelSystemEditorViewModel : ObservableObject, IDisposable
{
    private bool _disposed;

    // ── Session / model ────────────────────────────────────────────────────
    /// <summary>The active editing session for the model system.</summary>
    public ModelSystemSession Session { get; }

    // ── Run support ───────────────────────────────────────────────────────
    /// <summary>
    /// Optional run controller used to submit model system runs.
    /// When null the Run button is disabled.
    /// </summary>
    private readonly RunController? _runController;

    /// <summary>True when a <see cref="RunController"/> is available.</summary>
    public bool CanRun => _runController is not null;

    /// <summary>True when at least one estimation group has at least one parameter configured.</summary>
    public bool HasEstimationTargets =>
        EstimationGroups.Any(g => g.Parameters.Count > 0);

    /// <summary>True when at least one calibration group has at least one parameter configured.</summary>
    public bool HasCalibrationTargets =>
        CalibrationGroups.Any(g => g.Parameters.Count > 0);

    /// <summary>
    /// Optional callback invoked on the UI thread after a run is successfully submitted.
    /// Set by <see cref="MainWindow"/> to switch the active document to the Runs view.
    /// </summary>
    public Action? RunStarted { get; set; }

    /// <summary>The user who owns this editing session.</summary>
    public User User { get; }

    /// <summary>The header of the model system being edited.</summary>
    public ModelSystemHeader ModelSystemHeader => Session.ModelSystemHeader;

    /// <summary>The global boundary of the model system; the root canvas scope.</summary>
    public Boundary GlobalBoundary => Session.ModelSystem.GlobalBoundary;

    // ── Active boundary ───────────────────────────────────────────────────
    // Backing field; initialised in the constructor.
    private Boundary _currentBoundary = null!;

    /// <summary>The boundary currently rendered on the canvas.</summary>
    public Boundary CurrentBoundary => _currentBoundary;

    /// <summary>
    /// Breadcrumb path from the root to the current boundary, e.g. "Root › Sub".
    /// Shown as the placeholder text of the boundary navigation dropdown.
    /// </summary>
    public string CurrentBoundaryLabel
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>();
            var b = _currentBoundary;
            while (b is not null) { parts.Insert(0, b.Name); b = b.Parent; }
            // When inside a function template's InternalModules, append the template indicator.
            if (_currentFunctionTemplate is { } ft)
                parts[^1] = $"[{ft.Name}]";
            return string.Join(" › ", parts);
        }
    }

    /// <summary><c>true</c> when the current boundary is the global (root) boundary.</summary>
    public bool IsAtRootBoundary => ReferenceEquals(_currentBoundary, GlobalBoundary);

    // ── Function-template navigation ──────────────────────────────────────
    /// <summary>
    /// The function template whose <see cref="FunctionTemplate.InternalModules"/> is currently
    /// being shown on the canvas, or <c>null</c> when viewing a regular boundary.
    /// </summary>
    private FunctionTemplateViewModel? _currentFunctionTemplate;

    /// <summary><c>true</c> when the canvas is showing the inside of a function template.</summary>
    public bool IsInsideFunctionTemplate => _currentFunctionTemplate is not null;

    /// <summary>The function template currently being edited inside, or <c>null</c> when on a regular boundary.</summary>
    public FunctionTemplateViewModel? CurrentFunctionTemplate => _currentFunctionTemplate;

    /// <summary>Items shown in the boundary navigation dropdown.</summary>
    public ObservableCollection<BoundaryNavigationItem> BoundaryNavigationItems { get; } = new();

    // ── Dock integration ──────────────────────────────────────────────────
    /// <summary>Tab title shown in the dock.</summary>
    public string Title => $"✎  {ModelSystemHeader.Name ?? "Model System"}";

    /// <summary>Allow the user to close this tab.</summary>
    public bool CanClose => true;

    // ── Canvas collections ────────────────────────────────────────────────
    /// <summary>Observable wrappers around <see cref="Boundary.Modules"/>.</summary>
    public ObservableCollection<NodeViewModel> Nodes { get; } = new();

    /// <summary>Observable wrappers around <see cref="Boundary.Starts"/>.</summary>
    public ObservableCollection<StartViewModel> Starts { get; } = new();

    /// <summary>Observable wrappers around <see cref="Boundary.Links"/>.</summary>
    public ObservableCollection<LinkViewModel> Links { get; } = new();

    /// <summary>
    /// Tracks the <see cref="INotifyPropertyChanged.PropertyChanged"/> handlers registered
    /// on <see cref="SingleLink"/> objects so they can be properly unsubscribed when the
    /// link is removed from the boundary or the boundary switches.
    /// </summary>
    private readonly Dictionary<SingleLink, System.ComponentModel.PropertyChangedEventHandler> _singleLinkDestHandlers = new();

    /// <summary>Observable wrappers around <see cref="Boundary.CommentBlocks"/>.</summary>
    public ObservableCollection<CommentBlockViewModel> CommentBlocks { get; } = new();

    /// <summary>Observable wrappers around <see cref="Boundary.GhostNodes"/>.</summary>
    public ObservableCollection<GhostNodeViewModel> GhostNodes { get; } = new();

    /// <summary>Observable wrappers around <see cref="Boundary.FunctionTemplates"/>.</summary>
    public ObservableCollection<FunctionTemplateViewModel> FunctionTemplates { get; } = new();

    /// <summary>Observable wrappers around <see cref="Boundary.FunctionInstances"/>.</summary>
    public ObservableCollection<FunctionInstanceViewModel> FunctionInstances { get; } = new();

    /// <summary>
    /// Observable wrappers around <see cref="FunctionTemplate.FunctionParameters"/> of the
    /// currently active function template. Only populated while
    /// <see cref="IsInsideFunctionTemplate"/> is <c>true</c>.
    /// </summary>
    public ObservableCollection<FunctionParameterViewModel> FunctionParameterVMs { get; } = new();

    /// <summary>
    /// Flat, searchable list of all visible canvas elements in the current boundary view:
    /// non-inlined <see cref="NodeViewModel"/>s, <see cref="StartViewModel"/>s,
    /// <see cref="FunctionTemplateViewModel"/>s, <see cref="FunctionInstanceViewModel"/>s,
    /// and <see cref="FunctionParameterViewModel"/>s.
    /// Rebuilt automatically whenever any constituent collection or a node's inline state changes.
    /// </summary>
    public ObservableCollection<ICanvasElement> SearchItems { get; } = new();

    /// <summary>Observable view-models for the model system's variable list.</summary>
    public ObservableCollection<ModelSystemVariableViewModel> ModelSystemVariables { get; } = new();

    /// <summary>Observable view-models for the current FunctionTemplate's local variable list.
    /// Empty when not inside a FunctionTemplate.</summary>
    public ObservableCollection<ModelSystemVariableViewModel> LocalVariables { get; } = new();

    /// <summary>Observable view-models for estimation groups (each containing nominated parameters).</summary>
    public ObservableCollection<EstimationGroupViewModel> EstimationGroups { get; } = new();

    /// <summary>Observable view-models for calibration groups.</summary>
    public ObservableCollection<CalibrationGroupViewModel> CalibrationGroups { get; } = new();

    /// <summary>Text typed into the variables filter box; filters <see cref="FilteredModelSystemVariables"/> and <see cref="FilteredLocalVariables"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredModelSystemVariables))]
    [NotifyPropertyChangedFor(nameof(FilteredLocalVariables))]
    private string _variableFilter = string.Empty;

    /// <summary>
    /// Sorted (by name) and filtered (by <see cref="VariableFilter"/>) view of
    /// <see cref="ModelSystemVariables"/>. Matches on name or boundary path.
    /// </summary>
    public IEnumerable<ModelSystemVariableViewModel> FilteredModelSystemVariables
    {
        get
        {
            var q = ModelSystemVariables.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(VariableFilter))
            {
                var f = VariableFilter.Trim();
                q = q.Where(v =>
                    v.Name.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                    v.BoundaryPath.Contains(f, StringComparison.OrdinalIgnoreCase));
            }
            return q.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Sorted (by name) and filtered (by <see cref="VariableFilter"/>) view of
    /// <see cref="LocalVariables"/> for the current FunctionTemplate.
    /// </summary>
    public IEnumerable<ModelSystemVariableViewModel> FilteredLocalVariables
    {
        get
        {
            var q = LocalVariables.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(VariableFilter))
            {
                var f = VariableFilter.Trim();
                q = q.Where(v => v.Name.Contains(f, StringComparison.OrdinalIgnoreCase));
            }
            return q.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>True when local FunctionTemplate variables are available (drives section visibility).</summary>
    public bool HasLocalVariables => LocalVariables.Count > 0;

    /// <summary>True when neither global nor local variables are defined (drives the empty-state label).</summary>
    public bool HasNoVariablesAtAll => ModelSystemVariables.Count == 0 && LocalVariables.Count == 0;

    /// <summary>The currently selected link, if any. Mutually exclusive with <see cref="SelectedElement"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NothingSelected))]
    private LinkViewModel? _selectedLink;

    /// <summary>In-order list of destination entries for the currently selected Multi-Link.</summary>
    public ObservableCollection<LinkDestinationViewModel> SelectedLinkDestinationEntries { get; } = new();

    /// <summary>The destination entry currently selected in the destinations list.</summary>
    [ObservableProperty]
    private LinkDestinationViewModel? _selectedLinkDestinationEntry;

    /// <summary>True when the selected link is a MultiLink (drives the destinations panel visibility).</summary>
    public bool SelectedLinkIsMulti => SelectedLink?.UnderlyingLink is MultiLink;

    /// <summary>True when neither an element nor a link is selected.</summary>
    public bool NothingSelected => SelectedElement is null && SelectedLink is null;

    // ── Canvas element search ─────────────────────────────────────────────
    /// <summary>Fires when the user picks an element from the search box; the view should scroll to it.</summary>
    public event Action<ICanvasElement>? ScrollToElementRequested;

    /// <summary>
    /// Holds an element that <see cref="NavigateToElementById"/> wanted to scroll to but could
    /// not because no view was subscribed yet (e.g. the tab was just opened).
    /// The view drains this after it attaches and completes its layout pass.
    /// </summary>
    private ICanvasElement? _pendingScrollTarget;

    /// <summary>
    /// The last scroll offset the user was at in the canvas.  Persists across tab switches so
    /// the view can restore the position when the tab is re-activated (the view may be recreated
    /// by Dock.Avalonia on each activation).
    /// </summary>
    public Vector SavedScrollOffset { get; set; }

    /// <summary>
    /// Invokes <see cref="ScrollToElementRequested"/> for any element that was stored while
    /// the view was not yet attached. Called by <see cref="Views.ModelSystemEditorView"/> after
    /// it subscribes and layout has completed.
    /// </summary>
    public void FlushPendingScrollTarget()
    {
        var target = System.Threading.Interlocked.Exchange(ref _pendingScrollTarget, null);
        if (target is not null)
            ScrollToElementRequested?.Invoke(target);
    }

    /// <summary>Bound to the AutoCompleteBox SelectedItem; triggers navigation when set.</summary>
    [ObservableProperty]
    private ICanvasElement? _canvasSearchSelection;

    partial void OnCanvasSearchSelectionChanged(ICanvasElement? value)
    {
        if (value is null) return;
        SelectElement(value);
        ScrollToElementRequested?.Invoke(value);
        CanvasSearchSelection = null; // reset so the box is ready for the next search
    }

    // ── SearchItems tracking ──────────────────────────────────────────────
    private readonly HashSet<NodeViewModel> _searchTrackedNodes = new();

    /// <summary>
    /// Rebuilds <see cref="SearchItems"/> from the current boundary's VM collections.
    /// Non-inlined nodes and all Starts, FunctionTemplates, FunctionInstances,
    /// and FunctionParameters are included.
    /// </summary>
    private void RebuildSearchItems()
    {
        SearchItems.Clear();
        foreach (var nvm in Nodes)
            if (!nvm.IsInlined)
                SearchItems.Add(nvm);
        foreach (var svm in Starts)
            SearchItems.Add(svm);
        foreach (var ftvm in FunctionTemplates)
            SearchItems.Add(ftvm);
        foreach (var fivm in FunctionInstances)
            SearchItems.Add(fivm);
        foreach (var fpvm in FunctionParameterVMs)
            SearchItems.Add(fpvm);
    }

    private void OnNodesCollectionChangedForSearch(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Manage per-node IsInlined subscriptions so SearchItems stays in sync.
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var nvm in _searchTrackedNodes)
                nvm.PropertyChanged -= OnTrackedNodePropertyChanged;
            _searchTrackedNodes.Clear();
        }
        else
        {
            if (e.NewItems is not null)
                foreach (NodeViewModel nvm in e.NewItems)
                    if (_searchTrackedNodes.Add(nvm))
                        nvm.PropertyChanged += OnTrackedNodePropertyChanged;
            if (e.OldItems is not null)
                foreach (NodeViewModel nvm in e.OldItems)
                    if (_searchTrackedNodes.Remove(nvm))
                        nvm.PropertyChanged -= OnTrackedNodePropertyChanged;
        }
        RebuildSearchItems();
    }

    private void OnTrackedNodePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NodeViewModel.IsInlined))
            RebuildSearchItems();
    }

    // Subscription to the live MultiLink.Destinations collection.
    private NotifyCollectionChangedEventHandler? _destChangedHandler;
    private MultiLink? _subscribedMultiLink;

    // ── Selection ─────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NothingSelected))]
    private ICanvasElement? _selectedElement;



    /// <summary>
    /// A mutable copy of <see cref="SelectedElement"/>'s name/text for the properties panel text box.
    /// For nodes/starts this is the name; for comment blocks this is the comment text.
    /// Committed via <see cref="CommitRenameCommand"/>.
    /// </summary>
    [ObservableProperty]
    private string _selectedElementEditName = string.Empty;

    /// <summary>
    /// Label for the edit field in the property panel.
    /// "Name" for nodes and starts; "Comment" for comment blocks.
    /// </summary>
    public string SelectedElementFieldLabel =>
        SelectedElement is CommentBlockViewModel ? "Comment" : "Name";

    /// <summary>
    /// True when the selected element is a <see cref="NodeViewModel"/>,
    /// used to gate the Change Type button in the property panel.
    /// </summary>
    public bool SelectedElementIsNode => SelectedElement is NodeViewModel;

    /// <summary>True when the selected element is a <see cref="CommentBlockViewModel"/>.</summary>
    public bool SelectedElementIsComment => SelectedElement is CommentBlockViewModel;

    /// <summary>True when the selected element is NOT a <see cref="CommentBlockViewModel"/>, used to hide the rename box for comments.</summary>
    public bool SelectedElementIsNotComment => SelectedElement is not CommentBlockViewModel;

    /// <summary>
    /// True when the selected node is a BasicParameter or ScriptedParameter,
    /// used to gate the parameter value editor in the property panel.
    /// </summary>
    public bool SelectedElementIsParameter
        => SelectedElement is NodeViewModel pnvm && pnvm.IsParameterNode;

    /// <summary>
    /// Mutable copy of the selected parameter node's current value string,
    /// bound two-way to the parameter value text box.
    /// </summary>
    [ObservableProperty]
    private string _selectedElementParameterValue = string.Empty;

    /// <summary>
    /// Human-readable type string shown in the property panel's "Type:" row.
    /// Automatically updates when the node's type changes.
    /// </summary>
    public string SelectedElementTypeName => SelectedElement switch
    {
        StartViewModel             => "Start (entry point)",
        NodeViewModel nvm          => $"Module\n{nvm.TypeName}",
        CommentBlockViewModel      => "Comment Block",
        FunctionTemplateViewModel  => "Function Template",
        FunctionInstanceViewModel  => "Function Instance",
        FunctionParameterViewModel => "Function Parameter",
        _                          => string.Empty
    };

    /// <summary>True when the floating side panel should be shown.</summary>
    public bool SelectedSidePanelIsVisible => true;

    /// <summary>Module display name shown in the floating side panel.</summary>
    public string SelectedSidePanelModuleName
        => TryGetSelectedSidePanelMetadata(out var metadata) ? metadata.moduleName : string.Empty;

    /// <summary>Module or template type name shown beneath the title in the floating side panel.</summary>
    public string SelectedSidePanelTypeName
        => TryGetSelectedSidePanelMetadata(out var metadata) ? metadata.typeName : string.Empty;

    /// <summary>Module description shown inside the floating side panel.</summary>
    public string SelectedSidePanelDescription
        => TryGetSelectedSidePanelMetadata(out var metadata) ? metadata.description : string.Empty;

    /// <summary>Documentation URL shown as a link in the floating side panel.</summary>
    public string SelectedSidePanelDocumentationLink
        => TryGetSelectedSidePanelMetadata(out var metadata) ? metadata.documentationLink : string.Empty;

    /// <summary>True when the selected item exposes a documentation link.</summary>
    public bool SelectedSidePanelHasDocumentationLink
        => !string.IsNullOrWhiteSpace(SelectedSidePanelDocumentationLink);

    /// <summary>True when the selected element is a <see cref="FunctionInstanceViewModel"/>.</summary>
    public bool SelectedSidePanelHasTemplateLink => SelectedElement is FunctionInstanceViewModel;

    /// <summary>The button text used to open the selected function instance's template.</summary>
    public string SelectedFunctionInstanceTemplateActionText
        => string.IsNullOrWhiteSpace(SelectedFunctionInstanceTemplateName)
            ? "Open Template"
            : $"Open Template: {SelectedFunctionInstanceTemplateName}";

    /// <summary>The name of the selected function parameter's template, shown as a quick context link.</summary>
    public string SelectedFunctionParameterTemplateName
        => (SelectedElement as FunctionParameterViewModel)?.UnderlyingParameter.Template.Name ?? string.Empty;

    /// <summary>True when the selected element is a <see cref="FunctionParameterViewModel"/>.</summary>
    public bool SelectedSidePanelHasFunctionParameterContext => SelectedElement is FunctionParameterViewModel;

    /// <summary>True when the selected element is a <see cref="FunctionTemplateViewModel"/>.</summary>
    public bool SelectedElementIsFunctionTemplate => SelectedElement is FunctionTemplateViewModel;

    /// <summary>True when the selected element is a <see cref="FunctionInstanceViewModel"/>.</summary>
    public bool SelectedElementIsFunctionInstance => SelectedElement is FunctionInstanceViewModel;

    /// <summary>
    /// The name of the template referenced by the currently selected function instance,
    /// or an empty string when nothing (or a non-instance element) is selected.
    /// </summary>
    public string SelectedFunctionInstanceTemplateName
        => (SelectedElement as FunctionInstanceViewModel)?.TemplateName ?? string.Empty;

    /// <summary>
    /// The <see cref="FunctionParameter"/> list of the template referenced by the currently
    /// selected function instance, or an empty collection.
    /// </summary>
    public System.Collections.Generic.IEnumerable<ModelSystemConstruct.FunctionParameter> SelectedFunctionInstanceFunctionParameters
        => (SelectedElement as FunctionInstanceViewModel)?.FunctionParameters
           ?? System.Linq.Enumerable.Empty<ModelSystemConstruct.FunctionParameter>();

    /// <summary>
    /// <c>true</c> when the selected function instance's template has no function parameters —
    /// drives the "(none)" hint text in the properties panel.
    /// </summary>
    public bool SelectedFunctionInstanceHasNoFunctionParameters
        => SelectedElement is not FunctionInstanceViewModel fi || fi.FunctionParameters.Count == 0;

    /// <summary>
    /// The <see cref="FunctionParameter"/> list of the currently selected function template,
    /// or an empty list when nothing (or a non-template element) is selected.
    /// </summary>
    public IEnumerable<FunctionParameter> SelectedFunctionTemplateFunctionParameters
        => (SelectedElement as FunctionTemplateViewModel)?.FunctionParameters
           ?? System.Linq.Enumerable.Empty<FunctionParameter>();

    /// <summary>
    /// <c>true</c> when the selected function template has no function parameters —
    /// drives the "(none)" hint text in the properties panel.
    /// </summary>
    public bool SelectedFunctionTemplateHasNoFunctionParameters
        => SelectedElement is not FunctionTemplateViewModel ft || ft.FunctionParameters.Count == 0;

    /// <summary>All module types currently registered in the runtime.</summary>
    public System.Collections.ObjectModel.ReadOnlyObservableCollection<Type> AvailableModuleTypes
        => Session.LoadedModuleTypes;

    /// <summary>
    /// When <c>true</c> the canvas renders every hook on every node.
    /// When <c>false</c> only hooks that have an active link are shown.
    /// </summary>
    [ObservableProperty]
    private bool _showAllHooks;

    // ── Undo / Redo state ─────────────────────────────────────────────────
    /// <summary>True when there is at least one undoable command.</summary>
    [ObservableProperty]
    private bool _canUndo;

    /// <summary>True when there is at least one redoable command.</summary>
    [ObservableProperty]
    private bool _canRedo;

    // Unsubscribe from the outgoing element before the field changes.
    partial void OnSelectedElementChanging(ICanvasElement? value)
    {
        if (SelectedElement is System.ComponentModel.INotifyPropertyChanged oldNpc)
            oldNpc.PropertyChanged -= OnSelectedElementPropertyChanged;
    }

    partial void OnSelectedElementChanged(ICanvasElement? value)
    {
        // Subscribe to the new element so TypeName changes propagate.
        if (value is System.ComponentModel.INotifyPropertyChanged newNpc)
            newNpc.PropertyChanged += OnSelectedElementPropertyChanged;

        SelectedElementEditName = value?.Name ?? string.Empty;
        OnPropertyChanged(nameof(SelectedElementFieldLabel));
        OnPropertyChanged(nameof(SelectedElementTypeName));
        OnPropertyChanged(nameof(SelectedElementIsNode));
        OnPropertyChanged(nameof(SelectedElementIsComment));
        OnPropertyChanged(nameof(SelectedElementIsNotComment));
        OnPropertyChanged(nameof(SelectedElementIsParameter));
        OnPropertyChanged(nameof(SelectedElementIsFunctionTemplate));
        OnPropertyChanged(nameof(SelectedFunctionTemplateFunctionParameters));
        OnPropertyChanged(nameof(SelectedFunctionTemplateHasNoFunctionParameters));
        OnPropertyChanged(nameof(SelectedElementIsFunctionInstance));
        OnPropertyChanged(nameof(SelectedFunctionInstanceTemplateName));
        OnPropertyChanged(nameof(SelectedFunctionInstanceTemplateActionText));
        OnPropertyChanged(nameof(SelectedFunctionInstanceFunctionParameters));
        OnPropertyChanged(nameof(SelectedFunctionInstanceHasNoFunctionParameters));
        OnPropertyChanged(nameof(SelectedFunctionParameterTemplateName));
        OnPropertyChanged(nameof(SelectedSidePanelIsVisible));
        OnPropertyChanged(nameof(SelectedSidePanelModuleName));
        OnPropertyChanged(nameof(SelectedSidePanelTypeName));
        OnPropertyChanged(nameof(SelectedSidePanelDescription));
        OnPropertyChanged(nameof(SelectedSidePanelDocumentationLink));
        OnPropertyChanged(nameof(SelectedSidePanelHasDocumentationLink));
        OnPropertyChanged(nameof(SelectedSidePanelHasTemplateLink));
        OnPropertyChanged(nameof(SelectedSidePanelHasFunctionParameterContext));
        SelectedElementParameterValue =
            value is NodeViewModel pnvm && pnvm.IsParameterNode
                ? pnvm.ParameterValueRepresentation
                : string.Empty;
    }

    private void OnSelectedElementPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NodeViewModel.TypeName)
            or nameof(FunctionParameterViewModel.TypeName)
            or nameof(FunctionInstanceViewModel.TemplateName)
            or nameof(ICanvasElement.Name))
        {
            OnPropertyChanged(nameof(SelectedElementTypeName));
            OnPropertyChanged(nameof(SelectedSidePanelIsVisible));
            OnPropertyChanged(nameof(SelectedSidePanelModuleName));
            OnPropertyChanged(nameof(SelectedSidePanelTypeName));
            OnPropertyChanged(nameof(SelectedSidePanelDescription));
            OnPropertyChanged(nameof(SelectedSidePanelDocumentationLink));
            OnPropertyChanged(nameof(SelectedSidePanelHasDocumentationLink));
            OnPropertyChanged(nameof(SelectedSidePanelHasTemplateLink));
            OnPropertyChanged(nameof(SelectedSidePanelHasFunctionParameterContext));
            OnPropertyChanged(nameof(SelectedFunctionInstanceTemplateName));
            OnPropertyChanged(nameof(SelectedFunctionInstanceTemplateActionText));
            OnPropertyChanged(nameof(SelectedFunctionParameterTemplateName));
        }
        if (e.PropertyName == nameof(NodeViewModel.ParameterValueRepresentation))
        {
            if (SelectedElement is NodeViewModel nvm && nvm.IsParameterNode)
                SelectedElementParameterValue = nvm.ParameterValueRepresentation;
        }
    }

    [RelayCommand]
    private void OpenSelectedDocumentation()
    {
        var documentationLink = SelectedSidePanelDocumentationLink;
        if (string.IsNullOrWhiteSpace(documentationLink))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = documentationLink,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore link launch failures.
        }
    }

    [RelayCommand]
    private void OpenSelectedFunctionTemplate()
    {
        if (SelectedElement is FunctionInstanceViewModel fivm)
            OpenFunctionTemplateOfInstance(fivm);
    }

    private bool TryGetSelectedSidePanelMetadata(out (string moduleName, string typeName, string description, string documentationLink) metadata)
    {
        metadata = SelectedElement switch
        {
            null => ("No module selected", string.Empty, string.Empty, string.Empty),
            CommentBlockViewModel comment => ("Comment Block", "Comment", comment.Name, string.Empty),
            NodeViewModel node => BuildModuleMetadata(node.Name, node.UnderlyingNode.Type),
            FunctionTemplateViewModel template => BuildModuleMetadata(template.Name, template.UnderlyingTemplate.Type),
            FunctionInstanceViewModel instance => BuildModuleMetadata(instance.Name, instance.UnderlyingInstance.Template.Type),
            FunctionParameterViewModel parameter => (
                parameter.Name,
                FormatTypeNameWithGenerics(parameter.UnderlyingParameter.Type),
                string.Empty,
                string.Empty),
            _ => (string.Empty, string.Empty, string.Empty, string.Empty)
        };

        return true;
    }

    private static (string moduleName, string typeName, string description, string documentationLink) BuildModuleMetadata(string moduleName, Type? moduleType)
    {
        if (moduleType is null)
            return (moduleName, string.Empty, string.Empty, string.Empty);

        ModuleAttribute? moduleAttribute;
        try
        {
            moduleAttribute = moduleType.GetCustomAttribute<ModuleAttribute>();
        }
        catch
        {
            moduleAttribute = null;
        }

        return (
            moduleName,
            FormatTypeNameWithGenerics(moduleType),
            moduleAttribute?.Description ?? string.Empty,
            moduleAttribute?.DocumentationLink ?? string.Empty);
    }

    /// <summary>
    /// Formats a type name with full namespace, expanding generic parameters.
    /// For example: "BasicParameter`1" becomes "XTMF2.ModelSystemConstruct.BasicParameter&lt;System.Single&gt;".
    /// </summary>
    private static string FormatTypeNameWithGenerics(Type type)
    {
        string typeName = type.FullName ?? type.Name;
        
        if (!type.IsGenericType)
            return typeName;

        var backtickIndex = typeName.IndexOf('`');
        var baseName = backtickIndex >= 0 ? typeName.Substring(0, backtickIndex) : typeName;
        var genericArgs = type.GetGenericArguments();
        var argNames = string.Join(", ", genericArgs.Select(arg => FormatTypeNameWithGenerics(arg)));
        return $"{baseName}<{argNames}>";
    }

    // ── Parent window reference (set by the view) ─────────────────────────
    /// <summary>
    /// The top-level window, used to show dialogs.
    /// Set by <see cref="Views.ModelSystemEditorView"/> once it is attached to the visual tree.
    /// </summary>
    public Window? ParentWindow { get; set; }

    // ── Toast notification ────────────────────────────────────────────────
    /// <summary>Current toast message, or <c>null</c> when the toast is hidden.</summary>
    [ObservableProperty]
    private string? _toastMessage;

    /// <summary>
    /// When <c>true</c> the toast is styled as an error; when <c>false</c> it is informational.
    /// </summary>
    [ObservableProperty]
    private bool _toastIsError;

    private CancellationTokenSource? _toastCts;

    // ── Counters for default names ────────────────────────────────────────
    private int _startCounter;
    private int _commentCounter;

    // ── Default placement step ────────────────────────────────────────────
    private const float PlacementStep = 80f;

    // =====================================================================
    public ModelSystemEditorViewModel(ModelSystemSession session, User user, RunController? runController = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(user);
        Session = session;
        User = user;
        _runController = runController;

        // Build initial VM collections from the active boundary.
        _currentBoundary = GlobalBoundary;
        BuildFromBoundary(_currentBoundary);
        SubscribeToBoundary(_currentBoundary);
        RebuildBoundaryNavItems();

        // Build the variables collection and keep it in sync.
        SyncModelSystemVariables();
        ((System.Collections.Specialized.INotifyCollectionChanged)Session.ModelSystem.Variables)
            .CollectionChanged += OnModelSystemVariablesChanged;

        // Build estimation/calibration group collections and keep them in sync.
        SyncEstimationGroups();
        SyncCalibrationGroups();
        ((System.Collections.Specialized.INotifyCollectionChanged)Session.ModelSystem.EstimationGroups)
            .CollectionChanged += OnEstimationGroupsChanged;
        ((System.Collections.Specialized.INotifyCollectionChanged)Session.ModelSystem.CalibrationGroups)
            .CollectionChanged += OnCalibrationGroupsChanged;

        // Mirror CanUndo/CanRedo from the session reactively.
        _canUndo = Session.CanUndo;
        _canRedo = Session.CanRedo;
        ((System.ComponentModel.INotifyPropertyChanged)Session).PropertyChanged += OnSessionPropertyChanged;

        // Keep SearchItems in sync with the canvas collections.
        // Nodes: also handles per-node IsInlined tracking.
        Nodes.CollectionChanged            += OnNodesCollectionChangedForSearch;
        Starts.CollectionChanged           += (_, _) => RebuildSearchItems();
        FunctionTemplates.CollectionChanged += (_, _) => RebuildSearchItems();
        FunctionInstances.CollectionChanged += (_, _) => RebuildSearchItems();
        FunctionParameterVMs.CollectionChanged += (_, _) => RebuildSearchItems();
        // Subscribe to nodes already populated by BuildFromBoundary.
        foreach (var nvm in Nodes)
            if (_searchTrackedNodes.Add(nvm))
                nvm.PropertyChanged += OnTrackedNodePropertyChanged;
        RebuildSearchItems();
    }

    private void OnSessionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Session.CanUndo))      CanUndo = Session.CanUndo;
        else if (e.PropertyName == nameof(Session.CanRedo)) CanRedo = Session.CanRedo;
    }

    // ── Collection sync ───────────────────────────────────────────────────
    private void BuildFromBoundary(Boundary boundary)
    {
        foreach (var node in boundary.Modules)        Nodes.Add(new NodeViewModel(node, Session, User));
        foreach (var start in boundary.Starts)        Starts.Add(new StartViewModel(start, Session, User));
        // Ghost nodes and FunctionInstances must be populated before links so that
        // ResolveElement can find their view-models when wiring up link destinations.
        foreach (var ghost in boundary.GhostNodes)    GhostNodes.Add(new GhostNodeViewModel(ghost, Session, User));
        foreach (var ft in boundary.FunctionTemplates) FunctionTemplates.Add(new FunctionTemplateViewModel(ft, Session, User));
        foreach (var fi in boundary.FunctionInstances) FunctionInstances.Add(new FunctionInstanceViewModel(fi, Session, User));
        // Populate FunctionParameterVMs when inside a function template's InternalModules.
        if (_currentFunctionTemplate is not null)
            foreach (var fp in _currentFunctionTemplate.UnderlyingTemplate.FunctionParameters)
                FunctionParameterVMs.Add(new FunctionParameterViewModel(fp, Session, User));
        foreach (var link in boundary.Links)          TryAddLinkViewModel(link);
        foreach (var cb in boundary.CommentBlocks)    CommentBlocks.Add(new CommentBlockViewModel(cb, Session, User));
    }

    // Cached reference to the current boundary's Boundaries collection so we can
    // reliably unsubscribe (Boundary.Boundaries returns a new wrapper on every call).
    private INotifyCollectionChanged? _subscribedChildBoundaries;

    private void SubscribeToBoundary(Boundary boundary)
    {
        ((INotifyCollectionChanged)boundary.Modules).CollectionChanged       += OnModulesChanged;
        ((INotifyCollectionChanged)boundary.Starts).CollectionChanged        += OnStartsChanged;
        ((INotifyCollectionChanged)boundary.Links).CollectionChanged         += OnLinksChanged;
        ((INotifyCollectionChanged)boundary.CommentBlocks).CollectionChanged += OnCommentBlocksChanged;
        ((INotifyCollectionChanged)boundary.GhostNodes).CollectionChanged    += OnGhostNodesChanged;
        ((INotifyCollectionChanged)boundary.FunctionTemplates).CollectionChanged += OnFunctionTemplatesChanged;
        ((INotifyCollectionChanged)boundary.FunctionInstances).CollectionChanged += OnFunctionInstancesChanged;

        // Keep the same wrapper instance so we can correctly remove the handler later.
        _subscribedChildBoundaries  = boundary.Boundaries;
        _subscribedChildBoundaries.CollectionChanged += OnChildBoundariesChanged;
    }

    private void UnsubscribeFromBoundary(Boundary boundary)
    {
        ((INotifyCollectionChanged)boundary.Modules).CollectionChanged       -= OnModulesChanged;
        ((INotifyCollectionChanged)boundary.Starts).CollectionChanged        -= OnStartsChanged;
        ((INotifyCollectionChanged)boundary.Links).CollectionChanged         -= OnLinksChanged;
        ((INotifyCollectionChanged)boundary.CommentBlocks).CollectionChanged -= OnCommentBlocksChanged;
        ((INotifyCollectionChanged)boundary.GhostNodes).CollectionChanged    -= OnGhostNodesChanged;
        ((INotifyCollectionChanged)boundary.FunctionTemplates).CollectionChanged -= OnFunctionTemplatesChanged;
        ((INotifyCollectionChanged)boundary.FunctionInstances).CollectionChanged -= OnFunctionInstancesChanged;

        if (_subscribedChildBoundaries is not null)
        {
            _subscribedChildBoundaries.CollectionChanged -= OnChildBoundariesChanged;
            _subscribedChildBoundaries = null;
        }
    }

    private void OnChildBoundariesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RebuildBoundaryNavItems();

    /// <summary>
    /// Switches the canvas to <paramref name="boundary"/>, rebuilding all VM
    /// collections and re-wiring change-event subscriptions.
    /// </summary>
    public void SwitchToBoundary(Boundary boundary)
    {
        if (ReferenceEquals(_currentBoundary, boundary)) return;

        // Clear selection to avoid dangling VM references.
        SelectElement(null);
        SelectLink(null);

        UnsubscribeFromBoundary(_currentBoundary);
        foreach (var lvm in Links) lvm.Detach();
        // Unsubscribe all SingleLink destination-change handlers tracked for the old boundary.
        foreach (var (sl, h) in _singleLinkDestHandlers)
            sl.PropertyChanged -= h;
        _singleLinkDestHandlers.Clear();
        Nodes.Clear();
        Starts.Clear();
        Links.Clear();
        CommentBlocks.Clear();
        GhostNodes.Clear();
        foreach (var ft in FunctionTemplates) ft.Detach();
        FunctionTemplates.Clear();
        foreach (var fi in FunctionInstances) fi.Detach();
        FunctionInstances.Clear();
        foreach (var fp in FunctionParameterVMs) fp.Detach();
        FunctionParameterVMs.Clear();

        _currentBoundary = boundary;
        OnPropertyChanged(nameof(CurrentBoundary));
        OnPropertyChanged(nameof(CurrentBoundaryLabel));
        OnPropertyChanged(nameof(IsAtRootBoundary));
        // If the new boundary is not the InternalModules of the tracked template, leave FT mode.
        if (_currentFunctionTemplate is not null
            && !ReferenceEquals(boundary, _currentFunctionTemplate.UnderlyingTemplate.InternalModules))
        {
            ((INotifyCollectionChanged)_currentFunctionTemplate.UnderlyingTemplate.FunctionParameters).CollectionChanged
                -= OnFunctionParametersChanged;
            ((INotifyCollectionChanged)_currentFunctionTemplate.UnderlyingTemplate.LocalVariables).CollectionChanged
                -= OnLocalVariablesChanged;
            _currentFunctionTemplate = null;
            SyncLocalVariables(null);
            OnPropertyChanged(nameof(IsInsideFunctionTemplate));
            ExitFunctionTemplateCommand.NotifyCanExecuteChanged();
        }

        NavigateUpCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanNavigateUp));
        SubscribeToBoundary(_currentBoundary);
        BuildFromBoundary(_currentBoundary);
        RebuildBoundaryNavItems();
    }

    /// <summary>Rebuilds the dropdown items for the boundary navigation ComboBox.</summary>
    private void RebuildBoundaryNavItems()
    {
        BoundaryNavigationItems.Clear();

        if (_currentBoundary.Parent is { } parent)
            BoundaryNavigationItems.Add(BoundaryNavigationItem.ForBoundary($"↑ {parent.Name}", parent));

        foreach (var child in _currentBoundary.Boundaries)
            BoundaryNavigationItems.Add(BoundaryNavigationItem.ForBoundary(child.Name, child));

        BoundaryNavigationItems.Add(BoundaryNavigationItem.Browse);
    }

    /// <summary>Returns all boundaries in the model system as a depth-first flat list with depth info.</summary>
    private static IReadOnlyList<(Boundary Boundary, int Depth)> GetAllBoundaries(Boundary root)
    {
        var result = new List<(Boundary, int)>();
        var stack  = new Stack<(Boundary, int)>();
        stack.Push((root, 0));
        while (stack.Count > 0)
        {
            var (b, depth) = stack.Pop();
            result.Add((b, depth));
            foreach (var child in b.Boundaries.Reverse())
                stack.Push((child, depth + 1));
        }
        return result;
    }

    private void OnModulesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (Node n in e.NewItems)
            {
                var nvm = new NodeViewModel(n, Session, User);
                nvm.ShowHooks = true;   // expand hooks by default so the user can immediately see all connections
                Nodes.Add(nvm);
                SelectElement(nvm);
            }

        if (e.OldItems is not null)
            foreach (Node n in e.OldItems)
            {
                var vm = Nodes.FirstOrDefault(v => v.UnderlyingNode == n);
                if (vm is not null) Nodes.Remove(vm);
            }
    }

    private void OnStartsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (Start s in e.NewItems)
            {
                var svm = new StartViewModel(s, Session, User);
                Starts.Add(svm);
                SelectElement(svm);
            }

        if (e.OldItems is not null)
            foreach (Start s in e.OldItems)
            {
                var vm = Starts.FirstOrDefault(v => v.UnderlyingStart == s);
                if (vm is not null) Starts.Remove(vm);
            }
    }

    private void OnLinksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (Link link in e.NewItems)
                TryAddLinkViewModel(link);

        if (e.OldItems is not null)
            foreach (Link link in e.OldItems)
            {
                // A MultiLink may have produced multiple LinkViewModels (one per destination).
                var toRemove = Links.Where(v => v.UnderlyingLink == link).ToList();
                foreach (var vm in toRemove)
                {
                    vm.Detach();
                    Links.Remove(vm);
                }

                // Unsubscribe the destination-change handler registered in TryAddLinkViewModel.
                if (link is SingleLink removedSl
                    && _singleLinkDestHandlers.TryGetValue(removedSl, out var h))
                {
                    removedSl.PropertyChanged -= h;
                    _singleLinkDestHandlers.Remove(removedSl);
                }
            }
    }

    private void OnCommentBlocksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (CommentBlock cb in e.NewItems)
            {
                var cvm = new CommentBlockViewModel(cb, Session, User);
                CommentBlocks.Add(cvm);
                SelectElement(cvm);
            }

        if (e.OldItems is not null)
            foreach (CommentBlock cb in e.OldItems)
            {
                var vm = CommentBlocks.FirstOrDefault(v => v.UnderlyingBlock == cb);
                if (vm is not null) CommentBlocks.Remove(vm);
            }
    }

    private void OnGhostNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (GhostNode g in e.NewItems)
                GhostNodes.Add(new GhostNodeViewModel(g, Session, User));

        if (e.OldItems is not null)
            foreach (GhostNode g in e.OldItems)
            {
                var vm = GhostNodes.FirstOrDefault(v => v.UnderlyingGhostNode == g);
                if (vm is not null) GhostNodes.Remove(vm);
            }
    }

    private void OnFunctionTemplatesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (FunctionTemplate ft in e.NewItems)
            {
                var ftvm = new FunctionTemplateViewModel(ft, Session, User);
                FunctionTemplates.Add(ftvm);
                SelectElement(ftvm);
            }

        if (e.OldItems is not null)
            foreach (FunctionTemplate ft in e.OldItems)
            {
                var vm = FunctionTemplates.FirstOrDefault(v => v.UnderlyingTemplate == ft);
                if (vm is not null)
                {
                    vm.Detach();
                    FunctionTemplates.Remove(vm);
                }
            }
    }

    private void OnFunctionInstancesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (FunctionInstance fi in e.NewItems)
            {
                var fivm = new FunctionInstanceViewModel(fi, Session, User);
                FunctionInstances.Add(fivm);
                SelectElement(fivm);
            }

        if (e.OldItems is not null)
            foreach (FunctionInstance fi in e.OldItems)
            {
                var vm = FunctionInstances.FirstOrDefault(v => v.UnderlyingInstance == fi);
                if (vm is not null)
                {
                    vm.Detach();
                    FunctionInstances.Remove(vm);
                }
            }
    }

    private void OnFunctionParametersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (FunctionParameter fp in e.NewItems)
            {
                var fpvm = new FunctionParameterViewModel(fp, Session, User);
                FunctionParameterVMs.Add(fpvm);
                SelectElement(fpvm);
            }

        if (e.OldItems is not null)
            foreach (FunctionParameter fp in e.OldItems)
            {
                var vm = FunctionParameterVMs.FirstOrDefault(v => v.UnderlyingParameter == fp);
                if (vm is not null)
                {
                    vm.Detach();
                    FunctionParameterVMs.Remove(vm);
                }
            }
    }

    private void TryAddLinkViewModel(Link link)
    {
        var originElement = ResolveElement(link.Origin);
        if (originElement is null) return;

        if (link is MultiLink ml)
        {
            // One LinkViewModel per destination so all arrows are drawn.
            foreach (var dest in ml.Destinations)
                Links.Add(new LinkViewModel(link, originElement, ResolveElement(dest)));

            // Subscribe so future AddDestination / RemoveDestination calls update the canvas.
            ((System.Collections.Specialized.INotifyCollectionChanged)ml.Destinations).CollectionChanged += (_, e) =>
                OnMultiLinkDestinationsChanged(ml, originElement, e!);
        }
        else if (link is SingleLink sl)
        {
            Links.Add(new LinkViewModel(link, originElement, ResolveElement(sl.Destination)));

            // When SetDestination() is called on the underlying model link (e.g. by
            // ExtractToFunctionTemplate), rebuild the LinkViewModel so it points at the
            // new destination canvas element rather than continuing to float.
            System.ComponentModel.PropertyChangedEventHandler destHandler = (_, args) =>
            {
                if (args.PropertyName is nameof(SingleLink.Destination))
                    ReplaceSingleLinkViewModel(sl, originElement);
            };
            _singleLinkDestHandlers[sl] = destHandler;
            sl.PropertyChanged += destHandler;
        }
    }

    /// <summary>
    /// Replaces the <see cref="LinkViewModel"/> for a <see cref="SingleLink"/> whose
    /// <see cref="SingleLink.Destination"/> has just changed (e.g. via
    /// <c>ExtractToFunctionTemplate</c>). The stale VM is detached and removed; a fresh
    /// one is created and added so the canvas arrow binds to the correct target element.
    /// </summary>
    private void ReplaceSingleLinkViewModel(SingleLink sl, ICanvasElement originElement)
    {
        var old = Links.FirstOrDefault(lvm => lvm.UnderlyingLink == sl);
        if (old is not null)
        {
            old.Detach();
            Links.Remove(old);
        }
        Links.Add(new LinkViewModel(sl, originElement, ResolveElement(sl.Destination)));
    }

    private void OnMultiLinkDestinationsChanged(
        MultiLink ml, ICanvasElement originElement,
        NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (Node newDest in e.NewItems)
            {
                var destElement = ResolveElement(newDest);
                Links.Add(new LinkViewModel(ml, originElement, destElement));
            }
        }
        if (e.OldItems is not null)
        {
            foreach (Node removedDest in e.OldItems)
            {
                var destElement = ResolveElement(removedDest);
                var toRemove = Links.FirstOrDefault(lvm =>
                    lvm.UnderlyingLink == ml && lvm.Destination == destElement);
                if (toRemove is not null)
                {
                    toRemove.Detach();
                    Links.Remove(toRemove);
                }
            }
        }
    }

    private ICanvasElement? ResolveElement(Node? node)
    {
        if (node is null) return null;
        if (node is GhostNode ghost)
            return GhostNodes.FirstOrDefault(g => g.UnderlyingGhostNode == ghost);
        if (node is Start start)
            return Starts.FirstOrDefault(s => s.UnderlyingStart == start);
        if (node is FunctionInstance fi)
            return FunctionInstances.FirstOrDefault(fivm => fivm.UnderlyingInstance == fi);
        if (node is FunctionParameter fp)
            return FunctionParameterVMs.FirstOrDefault(fpvm => fpvm.UnderlyingParameter == fp);
        var directVm = Nodes.FirstOrDefault(n => n.UnderlyingNode == node);
        if (directVm is not null) return directVm;
        // The real node lives in another boundary — use a ghost referencing it if one is visible here.
        return GhostNodes.FirstOrDefault(g => g.UnderlyingGhostNode.ReferencedNode == node);
    }

    // ── Commands ──────────────────────────────────────────────────────────

    /// <summary>Prompt for a name and add a new Start to the global boundary.</summary>
    [RelayCommand]
    private async Task AddStart()
    {
        if (ParentWindow is null) return;

        var dialog = new InputDialog
        {
            Prompt = "Enter start name:",
            InputText = $"Start {++_startCounter}"
        };
        await dialog.ShowDialog(ParentWindow);

        var name = dialog.InputText?.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var location = NextPlacement(Starts.Count);
        Session.AddModelSystemStart(User, _currentBoundary, name, location, out _, out _);
        // The ObservableCollection event from the boundary automatically adds the StartViewModel.
    }

    /// <summary>
    /// Show a type-picker then a name dialog, and add the new module node to the global boundary.
    /// </summary>
    [RelayCommand]
    private async Task AddModule()
    {
        if (ParentWindow is null) return;

        // Step 1: choose the module type.
        var typePicker = new TypePickerDialog(
            Session.LoadedModuleTypes,
            prompt: "Select the module type to add:",
            openGenericModuleTypes: Session.OpenGenericModuleTypes,
            allAvailableTypes: Session.AllAvailableTypes);
        await typePicker.ShowDialog(ParentWindow);

        if (typePicker.WasCancelled || typePicker.SelectedType is null) return;
        var selectedType = typePicker.SelectedType;

        // Step 2: choose a name (pre-filled from the type's friendly name).
        var nameDialog = new InputDialog(
            title: "Add Module",
            prompt: "Enter module name:",
            defaultText: FriendlyTypeNameConverter.GetFriendlyName(selectedType));
        await nameDialog.ShowDialog(ParentWindow);

        var name = nameDialog.InputText?.Trim();
        if (string.IsNullOrEmpty(name) || nameDialog.WasCancelled) return;

        var location = NextPlacement(Nodes.Count);
        Session.AddNodeGenerateParameters(User, _currentBoundary, name, selectedType, location, out _, out _, out _);
        // The ObservableCollection event from the boundary automatically adds the NodeViewModel.
    }

    /// <summary>Change the type of the currently selected module node.</summary>
    [RelayCommand]
    private async Task ChangeModuleType()
    {
        if (ParentWindow is null || SelectedElement is not NodeViewModel nvm) return;

        var typePicker = new TypePickerDialog(
            Session.LoadedModuleTypes,
            prompt: "Select a new module type:",
            initialType: nvm.UnderlyingNode.Type,
            openGenericModuleTypes: Session.OpenGenericModuleTypes,
            allAvailableTypes: Session.AllAvailableTypes);
        await typePicker.ShowDialog(ParentWindow);

        if (typePicker.WasCancelled || typePicker.SelectedType is null) return;
        if (typePicker.SelectedType == nvm.UnderlyingNode.Type) return;

        if (!Session.SetNodeType(User, nvm.UnderlyingNode, typePicker.SelectedType, out var error))
            await ShowError("Change Type Failed", error);
    }

    /// <summary>
    /// Double-clicking a hook: show a type-picker pre-filtered to hook-compatible types,
    /// create the node, and automatically wire it to the hook.
    /// </summary>
    public async Task CreateNodeFromHookAsync(
        NodeViewModel originNode, NodeHook hook, double hookAnchorX, double hookAnchorY)
    {
        if (ParentWindow is null) return;

        // Determine the assignable type for this hook.
        Type hookElementType =
            (hook.Cardinality is HookCardinality.AtLeastOne or HookCardinality.AnyNumber)
            ? (hook.Type.GetElementType() ?? hook.Type)
            : hook.Type;

        // Build a filtered, read-only collection of compatible types.
        // This includes both closed registered types and constructions of open-generic
        // module types (e.g. BasicParameter<bool> for an IFunction<bool> hook).
        var compatibleList = new ObservableCollection<Type>(
            Session.GetCompatibleModuleTypes(hookElementType).Distinct());
        var filteredTypes = new ReadOnlyObservableCollection<Type>(compatibleList);

        if (filteredTypes.Count == 0)
        {
            await ShowError("No Compatible Types",
                new CommandError(
                    $"No loaded module types are compatible with hook '{hook.Name}' ({hookElementType.Name})."));
            return;
        }

        // Step 1: pick the module type (filtered to hook-compatible types).
        var typePicker = new TypePickerDialog(
            filteredTypes,
            prompt: $"Select a module type for '{hook.Name}' ({hookElementType.Name}):");
        await typePicker.ShowDialog(ParentWindow);
        if (typePicker.WasCancelled || typePicker.SelectedType is null) return;
        var selectedType = typePicker.SelectedType;

        // Step 2: pick a name.
        var nameDialog = new InputDialog(
            title: "Add Module",
            prompt: "Enter module name:",
            defaultText: selectedType.Name);
        await nameDialog.ShowDialog(ParentWindow);
        var name = nameDialog.InputText?.Trim();
        if (string.IsNullOrEmpty(name) || nameDialog.WasCancelled) return;

        // Place the new node to the right of the hook anchor.
        const float NodePlacementOffsetX = 90f;
        const float NodePlacementOffsetY = 14f; // half of NodeHeaderHeight
        var location = new Rectangle(
            (float)hookAnchorX + NodePlacementOffsetX,
            (float)hookAnchorY - NodePlacementOffsetY,
            120f, 50f);

        if (!Session.AddNodeGenerateParameters(User, _currentBoundary, name, selectedType, location, out var newNode, out _, out var nodeError))
        {
            await ShowError("Add Module Failed", nodeError);
            return;
        }

        // Wire the hook to the new node.
        if (!Session.AddLink(User, originNode.UnderlyingNode, hook, newNode!, out _, out var linkError))
            await ShowError("Create Link Failed", linkError);
    }

    /// <summary>
    /// If <paramref name="nodeType"/> implements <c>IAction&lt;Context&gt;</c>, returns
    /// <c>Context</c>; otherwise returns <c>null</c>.
    /// </summary>
    private static Type? GetIActionContextType(Type? nodeType)
    {
        if (nodeType is null) return null;
        var open = typeof(IAction<>);
        foreach (var iface in nodeType.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == open)
                return iface.GetGenericArguments()[0];
        }
        return null;
    }

    /// <summary>
    /// If <paramref name="nodeType"/> implements <c>IFunction&lt;Context, Return&gt;</c>,
    /// returns <c>(contextType, returnType)</c>; otherwise returns <c>null</c>.
    /// Only the first matching interface is returned.
    /// </summary>
    private static (Type Context, Type Return)? GetIFunction2Types(Type? nodeType)
    {
        if (nodeType is null) return null;
        var open = typeof(IFunction<,>);
        foreach (var iface in nodeType.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == open)
            {
                var args = iface.GetGenericArguments();
                return (args[0], args[1]);
            }
        }
        return null;
    }

    /// <summary>
    /// If <paramref name="nodeType"/> implements <c>IFunction&lt;T&gt;</c>, returns <c>T</c>;
    /// otherwise returns <c>null</c>.
    /// </summary>
    private static Type? GetIFunctionReturnType(Type? nodeType)
    {
        if (nodeType is null) return null;
        var iFunctionOpen = typeof(IFunction<>);
        foreach (var iface in nodeType.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == iFunctionOpen)
                return iface.GetGenericArguments()[0];
        }
        return null;
    }

    /// <summary>
    /// Creates a new <see cref="ExecuteWithContext{T}"/> node whose generic parameter
    /// matches the return type <c>T</c> that <paramref name="sourceNode"/>'s module implements
    /// via <c>IFunction&lt;T&gt;</c>.  The new node is positioned to the right of
    /// <paramref name="sourceNode"/> and its <em>Context</em> hook is linked back to the source.
    /// </summary>
    public async Task CreateExecuteWithContextAsync(NodeViewModel sourceNode)
    {
        if (ParentWindow is null) return;

        var returnType = GetIFunctionReturnType(sourceNode.UnderlyingNode.Type);
        if (returnType is null) return;

        var executeWithContextType = typeof(ExecuteWithContext<>).MakeGenericType(returnType);

        var nameDialog = new InputDialog(
            title: $"Add Execute With Context<{returnType.Name}> Module",
            prompt: "Enter module name:",
            defaultText: executeWithContextType.Name);
        await nameDialog.ShowDialog(ParentWindow);
        var name = nameDialog.InputText?.Trim();
        if (string.IsNullOrEmpty(name) || nameDialog.WasCancelled) return;

        const float PlacementGap = 30f;
        var location = new Rectangle(
            sourceNode.UnderlyingNode.Location.X + sourceNode.UnderlyingNode.Location.Width + PlacementGap,
            sourceNode.UnderlyingNode.Location.Y,
            120f, 50f);

        if (!Session.AddNodeGenerateParameters(User, _currentBoundary, name, executeWithContextType,
                location, out var newNode, out _, out var nodeError))
        {
            await ShowError("Add Module Failed", nodeError);
            return;
        }

        // Find the "Context" hook on the new ExecuteWithContext node and link it to sourceNode.
        var contextHook = newNode!.Hooks.FirstOrDefault(h => h.Name == "Get Context");
        if (contextHook is not null)
        {
            if (!Session.AddLink(User, newNode, contextHook, sourceNode.UnderlyingNode, out _, out var linkError))
                await ShowError("Create Link Failed", linkError);
        }
    }

    /// <summary>
    /// Creates a ghost node referencing <paramref name="nvm"/> on the current boundary,
    /// placed at the specified canvas coordinates.
    /// Called directly by the canvas (not a RelayCommand because it requires typed parameters).
    /// </summary>
    internal void CreateGhostNode(NodeViewModel nvm, int x, int y, int w, int h)
    {
        var location = new Rectangle(x, y, w, h);
        if (!Session.AddGhostNode(User, _currentBoundary, nvm.UnderlyingNode, location,
                out _, out var error))
            ShowToast(error?.Message ?? "Failed to create ghost node.", isError: true, durationMs: 4000);
    }

    /// <summary>
    /// Shows a boundary picker and moves the given regular node (and its outgoing links) to the
    /// chosen boundary.
    /// </summary>
    internal async Task MoveNodeToBoundaryAsync(NodeViewModel nvm)
    {
        if (ParentWindow is null) return;
        var dialog = new Views.BoundaryPickerDialog(GetAllBoundaries(GlobalBoundary), _currentBoundary);
        await dialog.ShowDialog(ParentWindow);
        if (dialog.Result != Views.BoundaryPickerResult.Navigate || dialog.SelectedBoundary is null) return;
        if (ReferenceEquals(dialog.SelectedBoundary, nvm.UnderlyingNode.ContainedWithin)) return;
        if (!Session.MoveNodeToBoundary(User, nvm.UnderlyingNode, dialog.SelectedBoundary, out var error))
            ShowToast(error?.Message ?? "Failed to move node.", isError: true, durationMs: 4000);
    }

    /// <summary>
    /// Shows a boundary picker and moves the given ghost node reference to the chosen boundary.
    /// </summary>
    internal async Task MoveGhostNodeToBoundaryAsync(GhostNodeViewModel gvm)
    {
        if (ParentWindow is null) return;
        var dialog = new Views.BoundaryPickerDialog(GetAllBoundaries(GlobalBoundary), _currentBoundary);
        await dialog.ShowDialog(ParentWindow);
        if (dialog.Result != Views.BoundaryPickerResult.Navigate || dialog.SelectedBoundary is null) return;
        if (ReferenceEquals(dialog.SelectedBoundary, gvm.UnderlyingGhostNode.ContainedWithin)) return;
        if (!Session.MoveGhostNodeToBoundary(User, gvm.UnderlyingGhostNode, dialog.SelectedBoundary, out var error))
            ShowToast(error?.Message ?? "Failed to move ghost node.", isError: true, durationMs: 4000);
    }

    /// <summary>
    /// Shows a boundary picker and moves the given function template to the chosen boundary.
    /// </summary>
    internal async Task MoveFunctionTemplateToBoundaryAsync(FunctionTemplateViewModel ftvm)
    {
        if (ParentWindow is null) return;
        var dialog = new Views.BoundaryPickerDialog(GetAllBoundaries(GlobalBoundary), _currentBoundary);
        await dialog.ShowDialog(ParentWindow);
        if (dialog.Result != Views.BoundaryPickerResult.Navigate || dialog.SelectedBoundary is null) return;
        var template = ftvm.UnderlyingTemplate;
        if (ReferenceEquals(dialog.SelectedBoundary, template.Parent)) return;
        if (!Session.MoveFunctionTemplate(User, template, template.Parent, dialog.SelectedBoundary, out var error))
            ShowToast(error?.Message ?? "Failed to move function template.", isError: true, durationMs: 4000);
    }

    /// <summary>
    /// Shows a boundary picker and moves the given function instance to the chosen boundary.
    /// </summary>
    internal async Task MoveFunctionInstanceToBoundaryAsync(FunctionInstanceViewModel fivm)
    {
        if (ParentWindow is null) return;
        var dialog = new Views.BoundaryPickerDialog(GetAllBoundaries(GlobalBoundary), _currentBoundary);
        await dialog.ShowDialog(ParentWindow);
        if (dialog.Result != Views.BoundaryPickerResult.Navigate || dialog.SelectedBoundary is null) return;
        var instance = fivm.UnderlyingInstance;
        if (ReferenceEquals(dialog.SelectedBoundary, instance.ContainedWithin)) return;
        if (!Session.MoveFunctionInstanceToBoundary(User, instance, dialog.SelectedBoundary, out var error))
            ShowToast(error?.Message ?? "Failed to move function instance.", isError: true, durationMs: 4000);
    }

    /// <summary>
    /// Navigates into the InternalModules of the <see cref="FunctionTemplate"/> associated with
    /// <paramref name="fivm"/>, switching to the template's parent boundary first if necessary.
    /// </summary>
    internal void OpenFunctionTemplateOfInstance(FunctionInstanceViewModel fivm)
    {
        var template = fivm.UnderlyingInstance.Template;
        // If the template lives in a different boundary than the one currently shown,
        // navigate there so FunctionTemplates is rebuilt for that boundary.
        if (!ReferenceEquals(template.Parent, _currentBoundary))
            SwitchToBoundary(template.Parent);
        // Locate the corresponding view-model (it must now be in FunctionTemplates).
        var ftvm = FunctionTemplates.FirstOrDefault(ft => ReferenceEquals(ft.UnderlyingTemplate, template));
        if (ftvm is null) return;
        NavigateIntoFunctionTemplate(ftvm);
    }

    /// <summary>
    /// Attempt to create a link from <paramref name="originElement"/> to <paramref name="destVm"/>.
    /// If any compatible hooks are found, either uses the sole hook automatically or
    /// presents a <see cref="HookPickerDialog"/> when there are multiple options.
    /// Called directly by the canvas (not a RelayCommand because it requires typed parameters).
    /// </summary>
    /// <summary>
    /// Creates a link whose destination is a <see cref="FunctionInstanceViewModel"/>.
    /// The FunctionInstance acts as a node whose type is its template's EntryNode type.
    /// </summary>
    public async Task CreateLinkAsync(ICanvasElement originElement, FunctionInstanceViewModel destFi)
    {
        if (ParentWindow is null) return;

        var destNode = destFi.UnderlyingInstance; // FunctionInstance : Node
        var destType = destNode.Type;             // Template.EntryNode?.Type ?? typeof(object)

        if (destType == typeof(object))
        {
            await ShowError("No Entry Node",
                new CommandError($"'{destFi.Name}' has no entry node designated and cannot be used as a link destination."));
            return;
        }

        // Resolve the underlying origin node.
        Node? originNode = originElement switch
        {
            NodeViewModel             nvm  => nvm.UnderlyingNode,
            StartViewModel            svm  => svm.UnderlyingStart,
            FunctionInstanceViewModel fivm => fivm.UnderlyingInstance,
            _                             => null
        };
        if (originNode is null) return;

        var compatible = new List<NodeHook>();
        foreach (var hook in originNode.Hooks)
        {
            Type hookElementType = (hook.Cardinality is HookCardinality.AtLeastOne or HookCardinality.AnyNumber)
                ? (hook.Type.GetElementType() ?? hook.Type)
                : hook.Type;
            if (hookElementType.IsAssignableFrom(destType))
                compatible.Add(hook);
        }

        if (compatible.Count == 0)
        {
            await ShowError("Incompatible Types",
                new CommandError($"No hooks on '{originNode.Name}' are compatible with '{destFi.Name}' (type '{destType.Name}')."));
            return;
        }

        NodeHook selectedHook;
        if (compatible.Count == 1)
        {
            selectedHook = compatible[0];
        }
        else
        {
            var dialog = new HookPickerDialog(compatible,
                $"Select which hook on '{originNode.Name}' to connect to '{destFi.Name}':");
            await dialog.ShowDialog(ParentWindow);
            if (dialog.WasCancelled || dialog.SelectedHook is null) return;
            selectedHook = dialog.SelectedHook;
        }

        if (!Session.AddLink(User, originNode, selectedHook, destNode, out _, out var error))
            await ShowError("Create Link Failed", error);
    }

    /// <summary>
    /// Creates a link whose destination is a <see cref="FunctionParameter"/> inside the current
    /// <see cref="FunctionTemplate"/>.  The hook on <paramref name="originElement"/> must be
    /// type-compatible with <paramref name="destFpVm"/>'s declared parameter type.
    /// </summary>
    public async Task CreateLinkAsync(ICanvasElement originElement, FunctionParameterViewModel destFpVm)
    {
        if (ParentWindow is null || _currentFunctionTemplate is null) return;

        Node? originNode = originElement switch
        {
            NodeViewModel             nvm  => nvm.UnderlyingNode,
            StartViewModel            svm  => svm.UnderlyingStart,
            FunctionInstanceViewModel fivm => fivm.UnderlyingInstance,
            _                             => null
        };
        if (originNode is null) return;

        var fp      = destFpVm.UnderlyingParameter;
        var destType = fp.Type;
        if (destType is null)
        {
            await ShowError("No Type",
                new CommandError($"FunctionParameter '{fp.Name}' has no type assigned and cannot be used as a link destination."));
            return;
        }

        var compatible = new List<NodeHook>();
        foreach (var hook in originNode.Hooks)
        {
            Type hookElementType = (hook.Cardinality is HookCardinality.AtLeastOne or HookCardinality.AnyNumber)
                ? (hook.Type.GetElementType() ?? hook.Type)
                : hook.Type;
            if (hookElementType.IsAssignableFrom(destType))
                compatible.Add(hook);
        }

        if (compatible.Count == 0)
        {
            await ShowError("Incompatible Types",
                new CommandError($"No hooks on '{originNode.Name}' are compatible with FunctionParameter type '{destType.Name}'."));
            return;
        }

        NodeHook selectedHook;
        if (compatible.Count == 1)
        {
            selectedHook = compatible[0];
        }
        else
        {
            var dialog = new HookPickerDialog(compatible,
                $"Select which hook on '{originNode.Name}' to connect to parameter '{fp.Name}':");
            await dialog.ShowDialog(ParentWindow);
            if (dialog.WasCancelled || dialog.SelectedHook is null) return;
            selectedHook = dialog.SelectedHook;
        }

        if (!Session.AddLink(User, originNode, selectedHook, fp, out _, out var error))
            await ShowError("Create Link Failed", error);
    }

    public async Task CreateLinkAsync(ICanvasElement originElement, NodeViewModel destVm)
    {
        if (ParentWindow is null) return;

        // Resolve the underlying origin node (Start is also a Node; FunctionInstance is also a Node).
        Node? originNode = originElement switch
        {
            NodeViewModel             nvm  => nvm.UnderlyingNode,
            StartViewModel            svm  => svm.UnderlyingStart,
            FunctionInstanceViewModel fivm => fivm.UnderlyingInstance,
            _                             => null
        };
        if (originNode is null) return;

        var destType = destVm.UnderlyingNode.Type;

        // Filter hooks on the origin to those whose type is compatible with the destination.
        var compatible = new List<NodeHook>();
        foreach (var hook in originNode.Hooks)
        {
            Type hookElementType = (hook.Cardinality is HookCardinality.AtLeastOne or HookCardinality.AnyNumber)
                ? (hook.Type.GetElementType() ?? hook.Type)
                : hook.Type;
            if (hookElementType.IsAssignableFrom(destType))
                compatible.Add(hook);
        }

        if (compatible.Count == 0)
        {
            // Special case: origin implements IFunction<Context,Return> and destination
            // implements IFunction<Context> → inject a ReturnUsingContext<Context,Return>.
            if (originElement is NodeViewModel originNvm
                && GetIFunction2Types(originNode.Type) is { } f2
                && destType is not null
                && typeof(IFunction<>).MakeGenericType(f2.Context).IsAssignableFrom(destType))
            {
                await InjectReturnUsingContextAsync(originNvm, destVm, f2.Context, f2.Return);
                return;
            }

            // Special case: origin implements IAction<ContextBase> and destination
            // implements IFunction<ContextDerived> where ContextBase.IsAssignableFrom(ContextDerived)
            // → inject a WithContext<ContextDerived, ContextBase>.
            if (originElement is NodeViewModel originNvmAction
                && GetIActionContextType(originNode.Type) is { } contextBase
                && destType is not null
                && GetIFunctionReturnType(destType) is { } contextDerived
                && contextBase.IsAssignableFrom(contextDerived))
            {
                await InjectWithContextAsync(originNvmAction, destVm, contextBase, contextDerived);
                return;
            }

            await ShowError("Incompatible Types",
                new CommandError($"No hooks on '{originNode.Name}' are compatible with type '{destType?.Name ?? "Unknown"}'."));
            return;
        }

        NodeHook selectedHook;
        if (compatible.Count == 1)
        {
            selectedHook = compatible[0];
        }
        else
        {
            var dialog = new HookPickerDialog(compatible,
                $"Select which hook on '{originNode.Name}' to connect to '{destVm.Name}':");
            await dialog.ShowDialog(ParentWindow);
            if (dialog.WasCancelled || dialog.SelectedHook is null) return;
            selectedHook = dialog.SelectedHook;
        }

        if (!Session.AddLink(User, originNode, selectedHook, destVm.UnderlyingNode, out _, out var error))
            await ShowError("Create Link Failed", error);
        // On success the boundary CollectionChanged fires and TryAddLinkViewModel wires up the new link.
    }

    /// <summary>
    /// Injects a <see cref="ReturnUsingContext{Context,Return}"/> node between
    /// <paramref name="originNode"/> (an <c>IFunction&lt;Context,Return&gt;</c> provider)
    /// and <paramref name="contextNode"/> (an <c>IFunction&lt;Context&gt;</c> provider).
    /// The injected node is placed to the right of the origin.
    /// </summary>
    private async Task InjectReturnUsingContextAsync(
        NodeViewModel originNode, NodeViewModel contextNode, Type contextType, Type returnType)
    {
        if (ParentWindow is null) return;

        var injectedType = typeof(ReturnUsingContext<,>).MakeGenericType(contextType, returnType);

        var nameDialog = new InputDialog(
            title: $"Add ReturnUsingContext<{contextType.Name},{returnType.Name}> Module",
            prompt: "Enter module name:",
            defaultText: $"Return {returnType.Name} Using {contextType.Name}");
        await nameDialog.ShowDialog(ParentWindow);
        var name = nameDialog.InputText?.Trim();
        if (string.IsNullOrEmpty(name) || nameDialog.WasCancelled) return;

        const float PlacementGap = 30f;
        var location = new Rectangle(
            originNode.UnderlyingNode.Location.X + originNode.UnderlyingNode.Location.Width + PlacementGap,
            originNode.UnderlyingNode.Location.Y,
            120f, 50f);

        if (!Session.AddNodeGenerateParameters(User, _currentBoundary, name, injectedType,
                location, out var newNode, out _, out var nodeError))
        {
            await ShowError("Add Module Failed", nodeError);
            return;
        }

        // Wire "To Execute" hook → originNode (IFunction<Context,Return>)
        var toExecuteHook = newNode!.Hooks.FirstOrDefault(h => h.Name == "To Execute");
        if (toExecuteHook is not null)
        {
            if (!Session.AddLink(User, newNode, toExecuteHook, originNode.UnderlyingNode, out _, out var le1))
            {
                await ShowError("Create Link Failed", le1);
                return;
            }
        }

        // Wire "Get Context" hook → contextNode (IFunction<Context>)
        var getContextHook = newNode!.Hooks.FirstOrDefault(h => h.Name == "Get Context");
        if (getContextHook is not null)
        {
            if (!Session.AddLink(User, newNode, getContextHook, contextNode.UnderlyingNode, out _, out var le2))
                await ShowError("Create Link Failed", le2);
        }
    }

    /// <summary>
    /// Injects a <see cref="WithContext{ContextDerived, ContextBase}"/> node between
    /// <paramref name="originNode"/> (an <c>IAction&lt;ContextBase&gt;</c> consumer) and
    /// <paramref name="contextNode"/> (an <c>IFunction&lt;ContextDerived&gt;</c> provider
    /// where <c>ContextDerived</c> derives from / implements <c>ContextBase</c>).
    /// The injected node is placed to the right of the origin.
    /// </summary>
    private async Task InjectWithContextAsync(
        NodeViewModel originNode, NodeViewModel contextNode, Type contextBase, Type contextDerived)
    {
        if (ParentWindow is null) return;

        var injectedType = typeof(WithContext<,>).MakeGenericType(contextDerived, contextBase);

        var nameDialog = new InputDialog(
            title: $"Add WithContext<{contextDerived.Name},{contextBase.Name}> Module",
            prompt: "Enter module name:",
            defaultText: $"With {contextDerived.Name} As {contextBase.Name}");
        await nameDialog.ShowDialog(ParentWindow);
        var name = nameDialog.InputText?.Trim();
        if (string.IsNullOrEmpty(name) || nameDialog.WasCancelled) return;

        const float PlacementGap = 30f;
        var location = new Rectangle(
            originNode.UnderlyingNode.Location.X + originNode.UnderlyingNode.Location.Width + PlacementGap,
            originNode.UnderlyingNode.Location.Y,
            120f, 50f);

        if (!Session.AddNodeGenerateParameters(User, _currentBoundary, name, injectedType,
                location, out var newNode, out _, out var nodeError))
        {
            await ShowError("Add Module Failed", nodeError);
            return;
        }

        // Wire "To Execute" hook → originNode (IAction<ContextBase>)
        var toExecuteHook = newNode!.Hooks.FirstOrDefault(h => h.Name == "To Execute");
        if (toExecuteHook is not null)
        {
            if (!Session.AddLink(User, newNode, toExecuteHook, originNode.UnderlyingNode, out _, out var le1))
            {
                await ShowError("Create Link Failed", le1);
                return;
            }
        }

        // Wire "Get Context" hook → contextNode (IFunction<ContextDerived>)
        var getContextHook = newNode!.Hooks.FirstOrDefault(h => h.Name == "Get Context");
        if (getContextHook is not null)
        {
            if (!Session.AddLink(User, newNode, getContextHook, contextNode.UnderlyingNode, out _, out var le2))
                await ShowError("Create Link Failed", le2);
        }
    }

    /// <summary>
    /// Presents a two-phase dialog that lets the user pick a boundary, then a compatible node
    /// within that boundary, and creates a link from <paramref name="originNode"/>'s
    /// <paramref name="hook"/> to the chosen node.
    /// <para>
    /// This supports inter-boundary links: the origin and destination can live in different
    /// boundaries; the link is stored in the origin node's own boundary as usual.
    /// </para>
    /// </summary>
    public async Task CreateInterBoundaryLinkAsync(NodeViewModel originNode, NodeHook hook)
    {
        if (ParentWindow is null) return;

        var allBoundaries = GetAllBoundaries(GlobalBoundary);
        var dialog = new Views.InterBoundaryLinkDialog(allBoundaries, hook, originNode.Name);
        await dialog.ShowDialog(ParentWindow);

        if (dialog.WasCancelled || dialog.ChosenNode is null) return;

        if (!Session.AddLink(User, originNode.UnderlyingNode, hook, dialog.ChosenNode, out _, out var error))
            await ShowError("Create Inter-Boundary Link Failed", error);
        // On success the boundary CollectionChanged fires and TryAddLinkViewModel wires up the new link.
    }

    /// <summary>
    /// Opens the inter-boundary node picker for a <see cref="FunctionInstance"/> origin
    /// using one of its <see cref="FunctionParameterHook"/>s, then creates the link.
    /// </summary>
    public async Task CreateInterBoundaryLinkAsync(FunctionInstanceViewModel fi, FunctionParameterHook hook)
    {
        if (ParentWindow is null) return;

        var allBoundaries = GetAllBoundaries(GlobalBoundary);
        var dialog = new Views.InterBoundaryLinkDialog(allBoundaries, hook, fi.Name);
        await dialog.ShowDialog(ParentWindow);

        if (dialog.WasCancelled || dialog.ChosenNode is null) return;

        if (!Session.AddLink(User, fi.UnderlyingInstance, hook, dialog.ChosenNode, out _, out var error))
            await ShowError("Create Link Failed", error);
    }

    /// <summary>
    /// Removes all links originating from the given <see cref="NodeHook"/> on the given node.
    /// </summary>
    public void ClearHookLinks(NodeViewModel node, NodeHook hook)
    {
        var linksToRemove = Links
            .Where(lvm => lvm.UnderlyingLink.Origin == node.UnderlyingNode
                       && lvm.UnderlyingLink.OriginHook == hook)
            .Select(lvm => lvm.UnderlyingLink)
            .ToList();

        foreach (var link in linksToRemove)
        {
            if (!Session.RemoveLink(User, link, out var error))
                ShowToast(error?.Message ?? "Could not remove link.", isError: true, durationMs: 5000);
        }
    }

    /// <summary>
    /// Removes all links originating from the given <see cref="FunctionParameterHook"/> on the given FunctionInstance.
    /// </summary>
    public void ClearFiHookLinks(FunctionInstanceViewModel fi, FunctionParameterHook hook)
    {
        var linksToRemove = Links
            .Where(lvm => lvm.UnderlyingLink.Origin == fi.UnderlyingInstance
                       && lvm.UnderlyingLink.OriginHook == hook)
            .Select(lvm => lvm.UnderlyingLink)
            .ToList();

        foreach (var link in linksToRemove)
        {
            if (!Session.RemoveLink(User, link, out var error))
                ShowToast(error?.Message ?? "Could not remove link.", isError: true, durationMs: 5000);
        }
    }

    /// <summary>
    /// Apply the value in <see cref="SelectedElementParameterValue"/> to the
    /// selected BasicParameter / ScriptedParameter node.
    /// </summary>
    [RelayCommand]
    private void CommitParameterValue()
    {
        if (SelectedElement is not NodeViewModel nvm) return;
        if (!Session.SetParameterValue(User, nvm.UnderlyingNode,
                SelectedElementParameterValue, out var error))
            ShowToast(error?.Message ?? "Failed to set parameter value.",
                      isError: true, durationMs: 5000);
    }

    /// <summary>
    /// Opens the parameter editor dialog for a BasicParameter or ScriptedParameter node,
    /// allowing the user to set the value and optionally switch between Basic and Scripted modes.
    /// </summary>
    public async Task EditParameterNodeAsync(NodeViewModel nvm)
    {
        if (ParentWindow is null) return;

        var node      = nvm.UnderlyingNode;
        var nodeType  = node.Type;
        if (nodeType is null) return;

        // Determine the inner type T from BasicParameter<T> / ScriptedParameter<T>
        var innerType = nodeType.GetGenericArguments().FirstOrDefault();
        if (innerType is null) return;

        // Resolve the effective enum type for the dialog:
        //   • BasicParameter<SomeEnum>                         → use SomeEnum directly
        //   • BasicParameter<X> where X (or a base/interface) →
        //       implements IFunction<SomeEnum>                 → use SomeEnum
        Type? effectiveEnumType = null;
        if (innerType.IsEnum)
        {
            effectiveEnumType = innerType;
        }
        else
        {
            // Check innerType itself and every interface it carries.
            var candidates = innerType.IsInterface
                ? new[] { innerType }.Concat(innerType.GetInterfaces())
                : (IEnumerable<Type>)innerType.GetInterfaces();
            foreach (var iface in candidates)
            {
                if (iface.IsGenericType
                    && iface.GetGenericTypeDefinition() == typeof(IFunction<>))
                {
                    var arg = iface.GetGenericArguments()[0];
                    if (arg.IsEnum) { effectiveEnumType = arg; break; }
                }
            }
        }

        // effectiveInnerType drives enum detection and basic validation.
        var effectiveInnerType = effectiveEnumType ?? innerType;

        var innerTypeName      = FriendlyTypeNameConverter.GetFriendlyName(innerType);
        var currentValue       = node.ParameterValue?.Representation ?? string.Empty;
        var isCurrentlyScripted =
            nodeType.IsGenericType &&
            nodeType.GetGenericTypeDefinition() == typeof(ScriptedParameter<>);

        // Basic validator: validate against the effective type (enum sub-type when applicable).
        string? BasicValidator(string v)
        {
            string? err = null;
            return ArbitraryParameterParser.Check(effectiveInnerType, v, ref err) ? null : (err ?? $"'{v}' is not valid for type {innerTypeName}.");
        }

        // Scripted validator: accept any non-empty text; the session will catch compile errors.
        string? ScriptedValidator(string v) =>
            string.IsNullOrWhiteSpace(v) ? "Expression cannot be empty." : null;

        var dialog = new ParameterEditorDialog(
            innerTypeName:      innerTypeName,
            currentValue:       currentValue,
            isCurrentlyScripted: isCurrentlyScripted,
            basicValidator:     BasicValidator,
            scriptedValidator:  ScriptedValidator,
            innerType:          effectiveInnerType);

        await dialog.ShowDialog(ParentWindow);

        if (dialog.WasCancelled) return;

        if (dialog.ResultIsScripted)
        {
            if (!Session.SetParameterExpression(User, node, dialog.ResultValue, out var error))
                await ShowError("Set Expression Failed", error);
        }
        else
        {
            if (!Session.SetParameterValue(User, node, dialog.ResultValue, out var error))
                await ShowError("Set Value Failed", error);
        }
    }

    // ── Model system variables ────────────────────────────────────────────

    /// <summary>Returns true when <paramref name="nvm"/> is in the model system variable list.</summary>
    public bool IsNodeInVariables(NodeViewModel nvm) =>
        Session.ModelSystem.Variables.Contains(nvm.UnderlyingNode);

    /// <summary>Returns true when <paramref name="nvm"/> is in the current FunctionTemplate's local variable list.</summary>
    public bool IsNodeInLocalVariables(NodeViewModel nvm) =>
        _currentFunctionTemplate?.UnderlyingTemplate.LocalVariables.Contains(nvm.UnderlyingNode) ?? false;

    /// <summary>True when the model system variable list is empty (drives the empty-state label).</summary>
    public bool HasNoModelSystemVariables => ModelSystemVariables.Count == 0;

    /// <summary>
    /// Adds the given parameter node to the model system's variable list.
    /// Called from the canvas context menu.
    /// </summary>
    public async Task AddNodeToVariablesAsync(NodeViewModel nvm)
    {
        if (!Session.AddVariable(User, nvm.UnderlyingNode, out var error))
            await ShowError("Add Variable Failed", error);
    }

    /// <summary>
    /// Removes the given parameter node from the model system's variable list.
    /// Called from the canvas context menu.
    /// </summary>
    public async Task RemoveNodeFromVariablesAsync(NodeViewModel nvm)
    {
        if (!Session.RemoveVariable(User, nvm.UnderlyingNode, out var error))
            await ShowError("Remove Variable Failed", error);
    }

    /// <summary>
    /// Adds the given node to the current FunctionTemplate's local variable list.
    /// Called from the canvas context menu when inside a FunctionTemplate.
    /// </summary>
    public async Task AddNodeToLocalVariablesAsync(NodeViewModel nvm)
    {
        if (_currentFunctionTemplate is null) return;
        if (!Session.AddFunctionTemplateVariable(User, _currentFunctionTemplate.UnderlyingTemplate,
                nvm.UnderlyingNode, out var error))
            await ShowError("Add Local Variable Failed", error);
    }

    /// <summary>
    /// Removes the given node from the current FunctionTemplate's local variable list.
    /// Called from the canvas context menu when inside a FunctionTemplate.
    /// </summary>
    public async Task RemoveNodeFromLocalVariablesAsync(NodeViewModel nvm)
    {
        if (_currentFunctionTemplate is null) return;
        if (!Session.RemoveFunctionTemplateVariable(User, _currentFunctionTemplate.UnderlyingTemplate,
                nvm.UnderlyingNode, out var error))
            await ShowError("Remove Local Variable Failed", error);
    }

    /// <summary>
    /// Removes the given variable entry from the model system's variable list.
    /// Bound to the "Remove" button in the variables panel.
    /// </summary>
    [RelayCommand]
    private async Task RemoveVariableNode(ModelSystemVariableViewModel varVm)
    {
        if (!Session.RemoveVariable(User, varVm.UnderlyingNode, out var error))
            await ShowError("Remove Variable Failed", error);
    }

    /// <summary>
    /// Removes the given node from the current FunctionTemplate's local variable list.
    /// Bound to the "Remove" button in the local variables section.
    /// </summary>
    [RelayCommand]
    private async Task RemoveLocalVariableNode(ModelSystemVariableViewModel varVm)
    {
        if (_currentFunctionTemplate is null) return;
        if (!Session.RemoveFunctionTemplateVariable(User, _currentFunctionTemplate.UnderlyingTemplate,
                varVm.UnderlyingNode, out var error))
            await ShowError("Remove Local Variable Failed", error);
    }

    // ── Estimation / Calibration – sync helpers ──────────────────────────

    private void SyncEstimationGroups()
    {
        foreach (var gvm in EstimationGroups)
        {
            ((System.Collections.Specialized.INotifyCollectionChanged)gvm.Parameters).CollectionChanged -= OnAnyEstimationGroupParametersChanged;
            gvm.Detach();
        }
        EstimationGroups.Clear();
        foreach (var group in Session.ModelSystem.EstimationGroups)
        {
            var gvm = new EstimationGroupViewModel(group);
            EstimationGroups.Add(gvm);
            ((System.Collections.Specialized.INotifyCollectionChanged)gvm.Parameters).CollectionChanged += OnAnyEstimationGroupParametersChanged;
        }
        OnPropertyChanged(nameof(HasEstimationTargets));
    }

    private void OnEstimationGroupsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        SyncEstimationGroups();
    }

    private void OnAnyEstimationGroupParametersChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(HasEstimationTargets));

    private void SyncCalibrationGroups()
    {
        foreach (var gvm in CalibrationGroups)
        {
            ((System.Collections.Specialized.INotifyCollectionChanged)gvm.Parameters).CollectionChanged -= OnAnyCalibrationGroupParametersChanged;
            gvm.Detach();
        }
        CalibrationGroups.Clear();
        foreach (var group in Session.ModelSystem.CalibrationGroups)
        {
            var gvm = new CalibrationGroupViewModel(group);
            CalibrationGroups.Add(gvm);
            ((System.Collections.Specialized.INotifyCollectionChanged)gvm.Parameters).CollectionChanged += OnAnyCalibrationGroupParametersChanged;
        }
        OnPropertyChanged(nameof(HasCalibrationTargets));
    }

    private void OnCalibrationGroupsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        SyncCalibrationGroups();
    }

    private void OnAnyCalibrationGroupParametersChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(HasCalibrationTargets));

    // ── Estimation – context menu helpers ───────────────────────────────

    /// <summary>Returns true when <paramref name="nvm"/> is already nominated for estimation in any group.</summary>
    public bool IsNodeInEstimation(NodeViewModel nvm) =>
        Session.ModelSystem.EstimationGroups.Any(g => g.Parameters.Any(e => e.Node == nvm.UnderlyingNode));

    /// <summary>Returns true when <paramref name="nvm"/> is already nominated for calibration in any group.</summary>
    public bool IsNodeInCalibration(NodeViewModel nvm) =>
        Session.ModelSystem.CalibrationGroups.Any(g => g.Parameters.Any(e => e.Node == nvm.UnderlyingNode));

    /// <summary>
    /// Adds the given parameter node to an estimation group (creating a Default group
    /// if none exist; prompting the user when more than one group exists) with bounds
    /// derived from the current parameter value.
    /// </summary>
    public async Task AddNodeToEstimationAsync(NodeViewModel nvm)
    {
        if (ParentWindow is null) return;
        var groups = Session.ModelSystem.EstimationGroups;
        EstimationGroup? group;
        if (groups.Count == 0)
        {
            if (!Session.AddEstimationGroup(User, "Default", out group, out var grpErr))
            { await ShowError("Add Estimation Group Failed", grpErr); return; }
        }
        else if (groups.Count == 1)
        {
            group = groups[0];
        }
        else
        {
            var picker = new Views.GroupPickerDialog(
                "Select the estimation group to add this parameter to:",
                groups.Select(g => g.Name));
            await picker.ShowDialog(ParentWindow);
            if (!picker.Confirmed) return;
            group = groups[picker.PickedIndex!.Value];
        }
        if (!TryParseCurrentParameterValue(nvm, out var currentVal)) currentVal = 0.0;
        if (!Session.AddEstimationParameter(User, group!, nvm.UnderlyingNode,
                Math.Min(0.0, currentVal), Math.Max(1.0, currentVal), currentVal,
                out _, out var error))
            await ShowError("Add Estimation Parameter Failed", error);
    }

    /// <summary>
    /// Removes the given parameter node from whichever estimation group contains it.
    /// </summary>
    public async Task RemoveNodeFromEstimationAsync(NodeViewModel nvm)
    {
        foreach (var group in Session.ModelSystem.EstimationGroups)
        {
            var entry = group.Parameters.FirstOrDefault(e => e.Node == nvm.UnderlyingNode);
            if (entry is null) continue;
            if (!Session.RemoveEstimationParameter(User, group, entry, out var error))
                await ShowError("Remove Estimation Parameter Failed", error);
            return;
        }
    }

    /// <summary>
    /// Adds the given parameter node to a calibration group (creating a Default group
    /// if none exist; prompting the user when more than one group exists) with bounds
    /// derived from the current parameter value.
    /// </summary>
    public async Task AddNodeToCalibrationAsync(NodeViewModel nvm)
    {
        if (ParentWindow is null) return;
        var groups = Session.ModelSystem.CalibrationGroups;
        CalibrationGroup? group;
        if (groups.Count == 0)
        {
            if (!Session.AddCalibrationGroup(User, "Default", out group, out var grpErr))
            { await ShowError("Add Calibration Group Failed", grpErr); return; }
        }
        else if (groups.Count == 1)
        {
            group = groups[0];
        }
        else
        {
            var picker = new Views.GroupPickerDialog(
                "Select the calibration group to add this parameter to:",
                groups.Select(g => g.Name));
            await picker.ShowDialog(ParentWindow);
            if (!picker.Confirmed) return;
            group = groups[picker.PickedIndex!.Value];
        }
        if (!TryParseCurrentParameterValue(nvm, out var currentVal)) currentVal = 0.0;
        if (!Session.AddCalibrationParameter(User, group!, nvm.UnderlyingNode,
                Math.Min(0.0, currentVal), Math.Max(1.0, currentVal),
                out _, out var error))
            await ShowError("Add Calibration Parameter Failed", error);
    }

    /// <summary>
    /// Removes the given parameter node from whichever calibration group contains it.
    /// </summary>
    public async Task RemoveNodeFromCalibrationAsync(NodeViewModel nvm)
    {
        foreach (var group in Session.ModelSystem.CalibrationGroups)
        {
            var entry = group.Parameters.FirstOrDefault(e => e.Node == nvm.UnderlyingNode);
            if (entry is null) continue;
            if (!Session.RemoveCalibrationParameter(User, group, entry, out var error))
                await ShowError("Remove Calibration Parameter Failed", error);
            return;
        }
    }

    /// <summary>
    /// Returns all nodes from the whole model system (all boundaries, recursively) whose
    /// runtime type implements <c>IFunction&lt;float&gt;</c> or <c>IFunction&lt;double&gt;</c>.
    /// These are valid candidates for estimation fitness nodes and calibration target nodes.
    /// </summary>
    public System.Collections.Generic.List<XTMF2.ModelSystemConstruct.Node> GetFunctionNodes()
    {
        return CollectFunctionNodes(Session.ModelSystem.GlobalBoundary);
    }

    private static System.Collections.Generic.List<XTMF2.ModelSystemConstruct.Node> CollectFunctionNodes(
        XTMF2.ModelSystemConstruct.Boundary boundary)
    {
        var result = new System.Collections.Generic.List<XTMF2.ModelSystemConstruct.Node>();
        var iFunctionOpen = typeof(IFunction<>);
        foreach (var node in boundary.Modules)
        {
            if (node.Type is { } t &&
                t.GetInterfaces().Any(i =>
                    i.IsGenericType &&
                    i.GetGenericTypeDefinition() == iFunctionOpen &&
                    (i.GetGenericArguments()[0] == typeof(float) || i.GetGenericArguments()[0] == typeof(double))))
                result.Add(node);
        }
        foreach (var child in boundary.Boundaries)
            result.AddRange(CollectFunctionNodes(child));
        return result;
    }

    private static bool TryParseCurrentParameterValue(NodeViewModel nvm, out double value)
    {
        var rep = nvm.ParameterValueRepresentation;
        if (double.TryParse(rep, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out value))
            return true;
        value = 0.0;
        return false;
    }

    /// <summary>Opens the Estimation Parameters dialog.</summary>
    public async Task OpenEstimationDialogAsync()
    {
        if (ParentWindow is null) return;
        var dlg = new Views.EstimationDialog(this);
        await dlg.ShowDialog(ParentWindow);
    }

    /// <summary>Opens the Calibration Parameters dialog.</summary>
    public async Task OpenCalibrationDialogAsync()
    {
        if (ParentWindow is null) return;
        var dlg = new Views.CalibrationDialog(this);
        await dlg.ShowDialog(ParentWindow);
    }

    // ── Variable navigation ──────────────────────────────────────────────

    /// <summary>
    /// Navigates to the boundary containing the variable's node and selects it.
    /// Bound to the "Go To" button in the variables panel.
    /// </summary>
    [RelayCommand]
    private void GoToVariableNode(ModelSystemVariableViewModel varVm)    {
        var targetBoundary = varVm.UnderlyingNode.ContainedWithin;
        if (targetBoundary is not null)
            SwitchToBoundary(targetBoundary);

        // Find the NodeViewModel for this node in the current boundary's Nodes collection.
        var nvm = Nodes.FirstOrDefault(n => ReferenceEquals(n.UnderlyingNode, varVm.UnderlyingNode));
        if (nvm is not null)
        {
            SelectElement(nvm);
            ScrollToElementRequested?.Invoke(nvm);
        }
    }

    /// <summary>
    /// Navigates the canvas to the element with the given <paramref name="id"/>, switching
    /// boundaries (and entering a function template if necessary) as required.
    /// </summary>
    public void NavigateToElementById(Guid id)
    {
        if (!TryFindElementById(id, GlobalBoundary, out var containingBoundary,
                out var ftToEnter, out var element))
            return;

        if (ftToEnter is not null)
        {
            if (!ReferenceEquals(_currentBoundary, ftToEnter.Parent))
                SwitchToBoundary(ftToEnter.Parent!);
            var ftvm = FunctionTemplates.FirstOrDefault(f =>
                ReferenceEquals(f.UnderlyingTemplate, ftToEnter));
            if (ftvm is not null)
                NavigateIntoFunctionTemplate(ftvm);
        }
        else if (containingBoundary is not null &&
                 !ReferenceEquals(_currentBoundary, containingBoundary))
        {
            SwitchToBoundary(containingBoundary);
        }

        ICanvasElement? canvasEl = element switch
        {
            // More-derived Node subtypes must come before the base Node arm.
            Start s              => Starts.FirstOrDefault(svm => ReferenceEquals(svm.UnderlyingStart, s)),
            FunctionInstance  fi => FunctionInstances.FirstOrDefault(f => ReferenceEquals(f.UnderlyingInstance, fi)),
            FunctionParameter fp => FunctionParameterVMs.FirstOrDefault(f => ReferenceEquals(f.UnderlyingParameter, fp)),
            Node n               => Nodes.FirstOrDefault(nvm => ReferenceEquals(nvm.UnderlyingNode, n)),
            FunctionTemplate  ft => FunctionTemplates.FirstOrDefault(f => ReferenceEquals(f.UnderlyingTemplate, ft)),
            CommentBlock      cb => CommentBlocks.FirstOrDefault(c => ReferenceEquals(c.UnderlyingBlock, cb)),
            _                    => null
        };
        if (canvasEl is not null)
        {
            // If the resolved element is inlined (hidden inside a host node's hook row),
            // redirect navigation to the host node so we scroll to something visible.
            if (canvasEl is NodeViewModel inlinedNvm && inlinedNvm.IsInlined)
            {
                var hostLink = Links.FirstOrDefault(lvm => ReferenceEquals(lvm.Destination, inlinedNvm));
                if (hostLink?.Origin is ICanvasElement hostEl)
                    canvasEl = hostEl;
            }

            SelectElement(canvasEl);
            // If the view is already subscribed, scroll immediately.
            // Otherwise park the target so the view can scroll once it attaches and lays out.
            if (ScrollToElementRequested is not null)
                ScrollToElementRequested.Invoke(canvasEl);
            else
                _pendingScrollTarget = canvasEl;
        }
    }

    /// <summary>
    /// Depth-first search for an element by <paramref name="id"/> across all boundaries.
    /// Returns the containing boundary, the function template to enter (if any), and the
    /// raw model object.
    /// </summary>
    private static bool TryFindElementById(Guid id, Boundary root,
        out Boundary? containingBoundary, out FunctionTemplate? ftToEnter, out object? element)
    {
        var queue = new Queue<(Boundary Boundary, FunctionTemplate? SourceFt)>();
        queue.Enqueue((root, null));
        while (queue.Count > 0)
        {
            var (b, sourceFt) = queue.Dequeue();
            foreach (var n in b.Modules)
                if (n.Id == id) { containingBoundary = b; ftToEnter = sourceFt; element = n; return true; }
            foreach (var s in b.Starts)
                if (s.Id == id) { containingBoundary = b; ftToEnter = sourceFt; element = s; return true; }
            foreach (var fi in b.FunctionInstances)
                if (fi.Id == id) { containingBoundary = b; ftToEnter = sourceFt; element = fi; return true; }
            foreach (var cb in b.CommentBlocks)
                if (cb.Id == id) { containingBoundary = b; ftToEnter = sourceFt; element = cb; return true; }
            foreach (var ft in b.FunctionTemplates)
            {
                if (ft.Id == id) { containingBoundary = b; ftToEnter = sourceFt; element = ft; return true; }
                foreach (var fp in ft.FunctionParameters)
                    if (fp.Id == id) { containingBoundary = ft.InternalModules; ftToEnter = ft; element = fp; return true; }
                queue.Enqueue((ft.InternalModules, ft));
            }
            foreach (var sub in b.Boundaries)
                queue.Enqueue((sub, null));
        }
        containingBoundary = null; ftToEnter = null; element = null;
        return false;
    }

    /// <summary>Rebuilds <see cref="ModelSystemVariables"/> from the current Variables list.</summary>
    private void SyncModelSystemVariables()
    {
        foreach (var old in ModelSystemVariables) old.Detach();
        ModelSystemVariables.Clear();
        foreach (var node in Session.ModelSystem.Variables)
            ModelSystemVariables.Add(new ModelSystemVariableViewModel(node));
    }

    private void OnModelSystemVariablesChanged(object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // Full rebuild keeps the code simple; the list is expected to be small.
        SyncModelSystemVariables();
        OnPropertyChanged(nameof(HasNoModelSystemVariables));
        OnPropertyChanged(nameof(HasNoVariablesAtAll));
        OnPropertyChanged(nameof(FilteredModelSystemVariables));
    }

    /// <summary>Rebuilds <see cref="LocalVariables"/> from the given FunctionTemplate's local variable list.
    /// Pass <c>null</c> to clear (when leaving a FunctionTemplate).</summary>
    private void SyncLocalVariables(FunctionTemplate? ft)
    {
        foreach (var old in LocalVariables) old.Detach();
        LocalVariables.Clear();
        if (ft is not null)
            foreach (var node in ft.LocalVariables)
                LocalVariables.Add(new ModelSystemVariableViewModel(node));
        OnPropertyChanged(nameof(HasLocalVariables));
        OnPropertyChanged(nameof(HasNoVariablesAtAll));
        OnPropertyChanged(nameof(FilteredLocalVariables));
    }

    private void OnLocalVariablesChanged(object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        SyncLocalVariables(_currentFunctionTemplate?.UnderlyingTemplate);
    }

    /// <summary>Commit the name/comment currently in <see cref="SelectedElementEditName"/> back to the model.</summary>
    [RelayCommand]
    private async Task CommitRename()
    {
        if (SelectedElement is null) return;

        var newValue = SelectedElementEditName.Trim();
        if (newValue == SelectedElement.Name) return;

        // Comment blocks update their Comment text instead of a node name.
        if (SelectedElement is CommentBlockViewModel cvm)
        {
            if (!Session.SetCommentBlockText(User, cvm.UnderlyingBlock, newValue, out var cbError))
            {
                SelectedElementEditName = SelectedElement.Name;
                await ShowError("Update Failed", cbError);
            }
            return;
        }

        Node? node = SelectedElement switch
        {
            NodeViewModel  nvm => nvm.UnderlyingNode,
            StartViewModel svm => svm.UnderlyingStart,
            _                  => null
        };

        if (node is null) return;

        if (!Session.SetNodeName(User, node, newValue, out var error))
        {
            // Revert the edit box to the current (unchanged) name
            SelectedElementEditName = SelectedElement.Name;
            await ShowError("Rename Failed", error);
        }
        // On success the model fires PropertyChanged(Name/Comment), which the VM
        // picks up and propagates – no manual sync needed here.
    }

    /// <summary>Add a new comment block to the global boundary.</summary>
    [RelayCommand]
    private void AddCommentBlock()
    {
        var offset = CommentBlocks.Count * PlacementStep;
        var x = 50f + offset % 800;
        var y = 50f + (offset / 800) * PlacementStep;
        var location = new Rectangle(x, y,
            (float)CommentBlockViewModel.DefaultWidth,
            (float)CommentBlockViewModel.DefaultHeight);
        Session.AddCommentBlock(User, _currentBoundary, $"Comment {++_commentCounter}", location, out _, out _);
    }

    // ── Function-template commands ─────────────────────────────────────────

    /// <summary>
    /// Prompts for a name and creates a new <see cref="FunctionTemplate"/> in the
    /// current boundary. The template is given a default canvas position near the
    /// top-left of the existing content.
    /// </summary>
    [RelayCommand]
    private async Task AddFunctionTemplate()
    {
        if (ParentWindow is null) return;

        var dialog = new InputDialog(
            title: "Add Function Template",
            prompt: "Enter the function template name:",
            defaultText: $"FunctionTemplate{FunctionTemplates.Count + 1}");
        await dialog.ShowDialog(ParentWindow);

        var name = dialog.InputText?.Trim();
        if (string.IsNullOrEmpty(name) || dialog.WasCancelled) return;

        // Place it in the upper area of the canvas.
        var offset = FunctionTemplates.Count * 60;
        var ftLocation = new Rectangle(60f + offset % 600, 60f + (offset / 600) * 160f, 220f, 140f);

        if (!Session.AddFunctionTemplate(User, _currentBoundary, name,
                out var ft, out var error))
        {
            await ShowError("Add Function Template Failed", error);
            return;
        }

        // Set canvas position (the default location set by the model constructor is fine,
        // but we override with a better-spread position).
        Session.SetFunctionTemplateLocation(User, ft!, ftLocation, out _);
    }

    /// <summary>
    /// Prompts for a new name and renames the given function template.
    /// </summary>
    public async Task RenameFunctionTemplateAsync(FunctionTemplateViewModel ftvm)
    {
        if (ParentWindow is null) return;

        var dialog = new InputDialog(
            title: "Rename Function Template",
            prompt: "Enter the new name:",
            defaultText: ftvm.Name);
        await dialog.ShowDialog(ParentWindow);

        var newName = dialog.InputText?.Trim();
        if (string.IsNullOrEmpty(newName) || dialog.WasCancelled) return;
        if (newName == ftvm.Name) return;

        if (!Session.RenameFunctionTemplate(User, ftvm.UnderlyingTemplate, newName, out var error))
            await ShowError("Rename Function Template Failed", error);
    }

    /// <summary>
    /// Deletes the given function template from the current boundary (with undo support).
    /// </summary>
    public async Task DeleteFunctionTemplateAsync(FunctionTemplateViewModel ftvm)
    {
        if (!Session.RemoveFunctionTemplate(User, _currentBoundary, ftvm.UnderlyingTemplate, out var error))
            await ShowError("Delete Function Template Failed", error);
    }

    // ── Function-instance commands ─────────────────────────────────────────

    /// <summary>
    /// Prompts the user to pick a <see cref="FunctionTemplate"/> from the current boundary
    /// and a name, then places a new <see cref="FunctionInstance"/> on the canvas.
    /// </summary>
    [RelayCommand]
    private async Task AddFunctionInstance()
    {
        if (ParentWindow is null) return;

        var availableTemplates = new List<FunctionTemplate>();
        _currentBoundary.CollectAccessibleFunctionTemplates(availableTemplates);
        if (availableTemplates.Count == 0)
        {
            ShowToast("No function templates are defined in this boundary or its children.", isError: true, durationMs: 4000);
            return;
        }

        // Build display names that include the child-boundary path when needed.
        var templateDisplayNames = availableTemplates
            .Select(ft => Boundary.GetQualifiedTemplateName(_currentBoundary, ft) ?? ft.Name)
            .ToList();

        // Pick a template (skip the picker if only one template exists).
        FunctionTemplate selectedTemplate;
        if (availableTemplates.Count == 1)
        {
            selectedTemplate = availableTemplates[0];
        }
        else
        {
            var picker = new StartPickerDialog(
                title: "Add Function Instance",
                prompt: "Select the function template to instantiate:",
                startNames: templateDisplayNames,
                defaultStart: templateDisplayNames[0]);
            await picker.ShowDialog(ParentWindow);
            if (picker.WasCancelled) return;
            var picked = picker.SelectedStartName ?? templateDisplayNames[0];
            var pickedIdx = templateDisplayNames.IndexOf(picked);
            selectedTemplate = availableTemplates[pickedIdx >= 0 ? pickedIdx : 0];
        }

        // Ask for the instance name.
        var nameDialog = new InputDialog(
            title: "Add Function Instance",
            prompt: $"Enter the instance name ({selectedTemplate.Name}):",
            defaultText: $"{selectedTemplate.Name}{FunctionInstances.Count + 1}");
        await nameDialog.ShowDialog(ParentWindow);

        var name = nameDialog.InputText?.Trim();
        if (string.IsNullOrEmpty(name) || nameDialog.WasCancelled) return;

        var offset = FunctionInstances.Count * 40;
        var location = new Rectangle(80f + offset % 800, 80f + (offset / 800) * 100f, 160f, 70f);

        if (!Session.AddFunctionInstanceGenerateParameters(User, _currentBoundary, selectedTemplate, name, location,
                out _, out var children, out var error))
        {
            await ShowError("Add Function Instance Failed", error);
        }
        else
        {
            // TODO: We might need to deal with the children here.   
        }
    }

    /// <summary>
    /// Renames the given function instance after prompting the user for a new name.
    /// </summary>
    public async Task RenameFunctionInstanceAsync(FunctionInstanceViewModel fivm)
    {
        if (ParentWindow is null) return;

        var dialog = new InputDialog(
            title: "Rename Function Instance",
            prompt: "Enter the new name:",
            defaultText: fivm.Name);
        await dialog.ShowDialog(ParentWindow);

        var newName = dialog.InputText?.Trim();
        if (string.IsNullOrEmpty(newName) || dialog.WasCancelled) return;
        if (newName == fivm.Name) return;

        if (!Session.RenameFunctionInstance(User, fivm.UnderlyingInstance, newName, out var error))
            await ShowError("Rename Function Instance Failed", error);
    }

    /// <summary>
    /// Deletes the given function instance from the current boundary (with undo support).
    /// </summary>
    public async Task DeleteFunctionInstanceAsync(FunctionInstanceViewModel fivm)
    {
        if (!Session.RemoveFunctionInstance(User, fivm.UnderlyingInstance, out var error))
            await ShowError("Delete Function Instance Failed", error);
    }

    /// <summary>
    /// Expands (inlines) the given function instance back into regular boundary elements.
    /// </summary>
    public async Task ExpandFunctionInstanceAsync(FunctionInstanceViewModel fivm)
    {
        if (!Session.ExpandFunctionInstance(User, fivm.UnderlyingInstance, out var error))
            await ShowError("Expand Function Instance Failed", error);
    }

    /// <summary>
    /// Navigates the canvas into <paramref name="ftvm"/>'s
    /// <see cref="FunctionTemplate.InternalModules"/> boundary so the user can
    /// edit the nodes contained within the function template.
    /// </summary>
    public void NavigateIntoFunctionTemplate(FunctionTemplateViewModel ftvm)
    {
        // Subscribe to the incoming template's FunctionParameters so the canvas stays in sync.
        ((INotifyCollectionChanged)ftvm.UnderlyingTemplate.FunctionParameters).CollectionChanged
            += OnFunctionParametersChanged;
        // Subscribe to local variables so the dialog list stays in sync.
        ((INotifyCollectionChanged)ftvm.UnderlyingTemplate.LocalVariables).CollectionChanged
            += OnLocalVariablesChanged;
        _currentFunctionTemplate = ftvm;
        SyncLocalVariables(ftvm.UnderlyingTemplate);
        OnPropertyChanged(nameof(IsInsideFunctionTemplate));
        ExitFunctionTemplateCommand.NotifyCanExecuteChanged();
        NavigateUpCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanNavigateUp));
        SwitchToBoundary(ftvm.UnderlyingTemplate.InternalModules);
    }

    /// <summary>
    /// ICommand wrapper for <see cref="NavigateIntoFunctionTemplate"/> so the AXAML
    /// properties panel can bind to it via <c>CommandParameter="{Binding SelectedElement}"</c>.
    /// </summary>
    [RelayCommand]
    private void NavigateIntoFunctionTemplateBinding(object? parameter)
    {
        if (parameter is FunctionTemplateViewModel ftvm)
            NavigateIntoFunctionTemplate(ftvm);
    }

    /// <summary>
    /// ICommand wrapper for <see cref="RenameFunctionTemplateAsync"/> usable from AXAML
    /// with <c>CommandParameter="{Binding SelectedElement}"</c>.
    /// </summary>
    [RelayCommand]
    private async Task RenameFunctionTemplateBinding(object? parameter)
    {
        if (parameter is FunctionTemplateViewModel ftvm)
            await RenameFunctionTemplateAsync(ftvm);
    }

    /// <summary>
    /// ICommand wrapper for <see cref="DeleteFunctionTemplateAsync"/> usable from AXAML
    /// with <c>CommandParameter="{Binding SelectedElement}"</c>.
    /// </summary>
    [RelayCommand]
    private async Task DeleteFunctionTemplateBinding(object? parameter)
    {
        if (parameter is FunctionTemplateViewModel ftvm)
            await DeleteFunctionTemplateAsync(ftvm);
    }

    /// <summary>
    /// ICommand wrapper for <see cref="RenameFunctionInstanceAsync"/> usable from AXAML
    /// with <c>CommandParameter="{Binding SelectedElement}"</c>.
    /// </summary>
    [RelayCommand]
    private async Task RenameFunctionInstanceBinding(object? parameter)
    {
        if (parameter is FunctionInstanceViewModel fivm)
            await RenameFunctionInstanceAsync(fivm);
    }

    /// <summary>
    /// ICommand wrapper for <see cref="DeleteFunctionInstanceAsync"/> usable from AXAML
    /// with <c>CommandParameter="{Binding SelectedElement}"</c>.
    /// </summary>
    [RelayCommand]
    private async Task DeleteFunctionInstanceBinding(object? parameter)
    {
        if (parameter is FunctionInstanceViewModel fivm)
            await DeleteFunctionInstanceAsync(fivm);
    }

    /// <summary>
    /// Exits the current function template's <see cref="FunctionTemplate.InternalModules"/>
    /// and returns the canvas to the parent boundary.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsInsideFunctionTemplate))]
    private void ExitFunctionTemplate()
    {
        if (_currentFunctionTemplate is null) return;
        var parentBoundary = _currentFunctionTemplate.UnderlyingTemplate.Parent;
        _currentFunctionTemplate = null;
        OnPropertyChanged(nameof(IsInsideFunctionTemplate));
        ExitFunctionTemplateCommand.NotifyCanExecuteChanged();
        NavigateUpCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanNavigateUp));
        SwitchToBoundary(parentBoundary);
    }

    // ── General navigate-up  (function template exit OR parent boundary) ──────

    /// <summary>
    /// <c>true</c> when the user can navigate up: either inside a function template
    /// or viewing a non-root boundary.
    /// </summary>
    public bool CanNavigateUp => IsInsideFunctionTemplate || !IsAtRootBoundary;

    /// <summary>
    /// Navigates up one scope.  If the canvas is inside a function template the
    /// user is returned to that template's parent boundary.  Otherwise the canvas
    /// ascends to <see cref="Boundary.Parent"/>.  No-op when already at the global
    /// root and not inside a function template.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanNavigateUp))]
    private void NavigateUp()
    {
        if (IsInsideFunctionTemplate)
        {
            ExitFunctionTemplate();
            return;
        }
        var parent = _currentBoundary.Parent;
        if (parent is not null)
            SwitchToBoundary(parent);
    }

    /// <summary>
    /// Toggles whether <paramref name="nvm"/> (a node in the current
    /// <see cref="FunctionTemplate.InternalModules"/>) is exposed as an external hook
    /// <summary>
    /// Adds a new <see cref="FunctionParameter"/> to the current function template.
    /// <para>
    /// Only available when the canvas is inside a function template (i.e.
    /// <see cref="IsInsideFunctionTemplate"/> is <c>true</c>).
    /// </para>
    /// </summary>
    public async Task AddFunctionParameterAsync(string name, Type type, Rectangle location)
    {
        if (_currentFunctionTemplate is null) return;

        if (!Session.AddFunctionParameter(
                User, _currentFunctionTemplate.UnderlyingTemplate,
                name, type, location,
                out _, out var error))
            await ShowError("Add Function Parameter Failed", error);
    }

    /// <summary>
    /// Shows the <see cref="FunctionParameterPickerDialog"/>, then adds the new
    /// <see cref="FunctionParameter"/> at the given canvas position.
    /// Only valid while inside a function template.
    /// </summary>
    public async Task AddFunctionParameterDirectAsync(double x, double y)
    {
        if (ParentWindow is null || _currentFunctionTemplate is null) return;

        // Suggest a unique name: "Param", "Param2", "Param3", …
        var baseName = "Param";
        var suggestedName = baseName;
        int idx = 2;
        while (_currentFunctionTemplate.UnderlyingTemplate.FunctionParameters
                   .Any(fp => string.Equals(fp.Name, suggestedName, StringComparison.OrdinalIgnoreCase)))
            suggestedName = $"{baseName}{idx++}";

        var dialog = new FunctionParameterPickerDialog(
            defaultName: suggestedName,
            allAvailableTypes: Session.AllAvailableTypes);
        await dialog.ShowDialog(ParentWindow);

        if (dialog.WasCancelled || dialog.SelectedType is null) return;

        var location = new Rectangle((float)x, (float)y, 250f, 50f);
        await AddFunctionParameterAsync(dialog.ParameterName, dialog.SelectedType, location);
    }

    /// <summary>
    /// Creates a <see cref="FunctionParameter"/> whose type matches <paramref name="hook"/>'s
    /// element type, places it at (<paramref name="x"/>, <paramref name="y"/>) on the canvas,
    /// and immediately creates a link from <paramref name="nodeVm"/> via <paramref name="hook"/>
    /// to the new parameter — all within the current function template.
    /// </summary>
    public async Task AddFunctionParameterFromHookAsync(
        NodeViewModel nodeVm, NodeHook hook, double x, double y)
    {
        if (_currentFunctionTemplate is null) return;

        // For array hooks use the element type; otherwise use the hook type directly.
        var paramType = (hook.Cardinality is HookCardinality.AtLeastOne or HookCardinality.AnyNumber)
            ? (hook.Type.GetElementType() ?? hook.Type)
            : hook.Type;

        // Generate a unique name based on the hook name.
        var baseName = hook.Name;
        var name = baseName;
        int idx = 2;
        while (_currentFunctionTemplate.UnderlyingTemplate.FunctionParameters
                   .Any(fp => string.Equals(fp.Name, name, StringComparison.OrdinalIgnoreCase)))
            name = $"{baseName}{idx++}";

        var location = new Rectangle((float)x, (float)y, 250f, 50f);

        if (!Session.AddFunctionParameter(
                User, _currentFunctionTemplate.UnderlyingTemplate,
                name, paramType, location,
                out var parameter, out var error))
        {
            await ShowError("Add Function Parameter Failed", error);
            return;
        }

        // Wire the hook → FunctionParameter link.
        if (!Session.AddLink(User, nodeVm.UnderlyingNode, hook, parameter!, out _, out var linkError))
            await ShowError("Create Link Failed", linkError);
    }

    /// <summary>
    /// Removes a <see cref="FunctionParameter"/> from the current function template.
    /// </summary>
    /// <summary>
    /// Prompts the user for a new name and renames the given <see cref="FunctionParameter"/>.
    /// </summary>
    public async Task RenameFunctionParameterAsync(FunctionParameterViewModel fpvm)
    {
        if (ParentWindow is null) return;

        var dialog = new InputDialog(
            title: "Rename Function Parameter",
            prompt: "Enter the new name:",
            defaultText: fpvm.Name);
        await dialog.ShowDialog(ParentWindow);

        var newName = dialog.InputText?.Trim();
        if (string.IsNullOrEmpty(newName) || dialog.WasCancelled) return;
        if (newName == fpvm.Name) return;

        if (!Session.RenameFunctionParameter(User, fpvm.UnderlyingParameter.Template,
                fpvm.UnderlyingParameter, newName, out var error))
            await ShowError("Rename Function Parameter Failed", error);
    }

    public bool RemoveFunctionParameterAsync(FunctionParameter parameter)
    {
        if (_currentFunctionTemplate is null) return false;

        if (!Session.RemoveFunctionParameter(
                User, _currentFunctionTemplate.UnderlyingTemplate, parameter, out var error))
        {
            ShowToast(error?.Message ?? "Failed to remove function parameter.", isError: true, durationMs: 4000);
            return false;
        }
        return true;
    }

    /// <summary>
    /// Toggles whether <paramref name="node"/> is a local variable of the current
    /// <see cref="FunctionTemplate"/>. If it is already in <see cref="FunctionTemplate.LocalVariables"/>
    /// it is removed; otherwise it is added. Validation is performed by the session.
    /// </summary>
    public async Task ToggleFunctionTemplateVariableAsync(Node node)
    {
        if (_currentFunctionTemplate is null) return;
        var template = _currentFunctionTemplate.UnderlyingTemplate;
        if (template.LocalVariables.Contains(node))
        {
            if (!Session.RemoveFunctionTemplateVariable(User, template, node, out var error))
                await ShowError("Remove Local Variable Failed", error);
        }
        else
        {
            if (!Session.AddFunctionTemplateVariable(User, template, node, out var error))
                await ShowError("Add Local Variable Failed", error);
        }
    }

    /// <summary>
    /// Designates <paramref name="nvm"/>'s underlying node as the
    /// <see cref="FunctionTemplate.EntryNode"/> of the current function template.
    /// If the node is already the entry node the assignment is cleared instead
    /// (i.e. this method toggles the entry-node designation).
    /// </summary>
    public async Task SetFunctionTemplateEntryNodeAsync(NodeViewModel nvm)
    {
        if (_currentFunctionTemplate is null) return;
        var template = _currentFunctionTemplate.UnderlyingTemplate;
        // Toggle: clear when the node is already the entry node, otherwise assign it.
        var newEntry = ReferenceEquals(template.EntryNode, nvm.UnderlyingNode)
            ? null
            : nvm.UnderlyingNode;
        if (!Session.SetFunctionTemplateEntryNode(User, template, newEntry, out var error))
            await ShowError("Set Entry Node Failed", error);
    }

    /// <summary>
    /// Prompts for a run name and start to execute, then submits the run to the
    /// <see cref="RunController"/>.
    /// </summary>
    [RelayCommand]
    private async Task RunModelSystem()
    {
        if (ParentWindow is null || _runController is null) return;

        // Collect available starts from the global boundary.
        var availableStarts = Session.ModelSystem.GlobalBoundary.Starts.ToList();
        if (availableStarts.Count == 0)
        {
            ShowToast("No starts are defined in this model system.", isError: true, durationMs: 5000);
            return;
        }

        // Prompt for the run name.
        var defaultRunName = $"{ModelSystemHeader.Name ?? "Run"}_{DateTime.Now:yyyyMMdd_HHmmss}";
        var runNameDialog = new InputDialog(
            title: "Run Model System",
            prompt: "Enter a name for this run:",
            defaultText: defaultRunName);
        await runNameDialog.ShowDialog(ParentWindow);
        if (runNameDialog.WasCancelled) return;
        var runName = runNameDialog.InputText?.Trim();
        if (string.IsNullOrEmpty(runName)) return;

        // Determine which start to execute.
        string startToExecute;
        if (availableStarts.Count == 1)
        {
            startToExecute = availableStarts[0].Name;
        }
        else
        {
            // Multiple starts: show a ComboBox so the user can pick one.
            var startNames = availableStarts.Select(s => s.Name).ToList();
            var startDialog = new StartPickerDialog(
                title: "Select Start",
                prompt: "Select the start to execute:",
                startNames: startNames,
                defaultStart: startNames[0]);
            await startDialog.ShowDialog(ParentWindow);
            if (startDialog.WasCancelled) return;
            startToExecute = startDialog.SelectedStartName ?? startNames[0];
            if (string.IsNullOrEmpty(startToExecute)) return;
        }

        var project = Session.Project;
        if (!_runController.SendRun(project, Session, User, startToExecute, runName, out _, out var runError))
        {
            ShowToast($"Failed to start run: {runError?.Message}", isError: true, durationMs: 6000);
            return;
        }

        ShowToast($"Run '{runName}' started.", durationMs: 3000);
        RunStarted?.Invoke();
    }

    /// <summary>Runs the model system in estimation mode using the configured estimation groups.</summary>
    [RelayCommand]
    private async Task RunEstimation()
    {
        if (ParentWindow is null || _runController is null) return;

        var availableStarts = Session.ModelSystem.GlobalBoundary.Starts.ToList();
        if (availableStarts.Count == 0)
        {
            ShowToast("No starts are defined in this model system.", isError: true, durationMs: 5000);
            return;
        }

        var defaultRunName = $"Estimation_{ModelSystemHeader.Name ?? "Run"}_{DateTime.Now:yyyyMMdd_HHmmss}";
        var runNameDialog = new InputDialog(
            title: "Run Estimation",
            prompt: "Enter a name for this estimation run:",
            defaultText: defaultRunName);
        await runNameDialog.ShowDialog(ParentWindow);
        if (runNameDialog.WasCancelled) return;
        var runName = runNameDialog.InputText?.Trim();
        if (string.IsNullOrEmpty(runName)) return;

        string startToExecute;
        if (availableStarts.Count == 1)
        {
            startToExecute = availableStarts[0].Name;
        }
        else
        {
            var startNames = availableStarts.Select(s => s.Name).ToList();
            var startDialog = new StartPickerDialog(
                title: "Select Start",
                prompt: "Select the start to execute:",
                startNames: startNames,
                defaultStart: startNames[0]);
            await startDialog.ShowDialog(ParentWindow);
            if (startDialog.WasCancelled) return;
            startToExecute = startDialog.SelectedStartName ?? startNames[0];
            if (string.IsNullOrEmpty(startToExecute)) return;
        }

        var project = Session.Project;
        if (!_runController.SendEstimationRun(project, Session, User, startToExecute, runName, out _, out var runError))
        {
            ShowToast($"Failed to start estimation run: {runError?.Message}", isError: true, durationMs: 6000);
            return;
        }

        ShowToast($"Estimation run '{runName}' started.", durationMs: 3000);
        RunStarted?.Invoke();
    }

    /// <summary>Runs the model system in calibration mode using the configured calibration groups.</summary>
    [RelayCommand]
    private async Task RunCalibration()
    {
        if (ParentWindow is null || _runController is null) return;

        var availableStarts = Session.ModelSystem.GlobalBoundary.Starts.ToList();
        if (availableStarts.Count == 0)
        {
            ShowToast("No starts are defined in this model system.", isError: true, durationMs: 5000);
            return;
        }

        var defaultRunName = $"Calibration_{ModelSystemHeader.Name ?? "Run"}_{DateTime.Now:yyyyMMdd_HHmmss}";
        var runNameDialog = new InputDialog(
            title: "Run Calibration",
            prompt: "Enter a name for this calibration run:",
            defaultText: defaultRunName);
        await runNameDialog.ShowDialog(ParentWindow);
        if (runNameDialog.WasCancelled) return;
        var runName = runNameDialog.InputText?.Trim();
        if (string.IsNullOrEmpty(runName)) return;

        string startToExecute;
        if (availableStarts.Count == 1)
        {
            startToExecute = availableStarts[0].Name;
        }
        else
        {
            var startNames = availableStarts.Select(s => s.Name).ToList();
            var startDialog = new StartPickerDialog(
                title: "Select Start",
                prompt: "Select the start to execute:",
                startNames: startNames,
                defaultStart: startNames[0]);
            await startDialog.ShowDialog(ParentWindow);
            if (startDialog.WasCancelled) return;
            startToExecute = startDialog.SelectedStartName ?? startNames[0];
            if (string.IsNullOrEmpty(startToExecute)) return;
        }

        var project = Session.Project;
        if (!_runController.SendCalibrationRun(project, Session, User, startToExecute, runName, out _, out var runError))
        {
            ShowToast($"Failed to start calibration run: {runError?.Message}", isError: true, durationMs: 6000);
            return;
        }

        ShowToast($"Calibration run '{runName}' started.", durationMs: 3000);
        RunStarted?.Invoke();
    }

    /// <summary>Save the model system to its project file.</summary>
    [RelayCommand]
    private async Task SaveModelSystem()
    {
        ShowToast(Strings.ModelSystemEditor_ToastSaving);
        // Yield to let the UI render the "saving" toast before the synchronous save runs.
        await Task.Yield();
        if (Session.Save(out var error))
        {
            ShowToast(Strings.ModelSystemEditor_ToastSaved);
        }
        else
        {
            var msg = string.Format(Strings.ModelSystemEditor_ToastSaveFailed,
                                    error?.Message ?? Strings.ModelSystems_UnknownError);
            ShowToast(msg, isError: true, durationMs: 6000);
        }
    }

    /// <summary>Export the model system to a user-chosen file.</summary>
    [RelayCommand]
    private async Task ExportModelSystem()
    {
        if (ParentWindow is null) return;

        var file = await ParentWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title              = "Export Model System",
            SuggestedFileName  = ModelSystemHeader.Name ?? "model-system",
            DefaultExtension   = "xmsys",
            FileTypeChoices    =
            [
                new FilePickerFileType("XTMF Model System") { Patterns = ["*.xmsys"] },
                new FilePickerFileType("All Files")         { Patterns = ["*"] }
            ]
        });

        if (file is null) return;

        if(!Session.ExportModelSystem(User, file.Path.AbsolutePath, out var error))
        {
            await ShowError("Export Failed", error);
        }
    }

    /// <summary>
    /// Revert all unsaved changes by undoing every command in the buffer,
    /// restoring the model to the state it held when the session was opened.
    /// </summary>
    [RelayCommand]
    private async Task RevertModelSystem()
    {
        if (ParentWindow is null) return;

        var confirm = new ConfirmDialog(
            "Revert Model System",
            "Discard all unsaved changes and revert to the last saved version?");
        await confirm.ShowDialog(ParentWindow);
        if (!confirm.Result) return;

        // Undo everything in the buffer to return to the on-disk state.
        while (Session.Undo(User, out _)) { }
    }

    /// <summary>Opens the boundary picker dialog to navigate to or create any boundary.</summary>
    [RelayCommand]
    private async Task BrowseBoundaries()
    {
        if (ParentWindow is null) return;

        var dialog = new BoundaryPickerDialog(GetAllBoundaries(GlobalBoundary), _currentBoundary);
        await dialog.ShowDialog(ParentWindow);

        switch (dialog.Result)
        {
            case BoundaryPickerResult.Navigate:
                if (dialog.SelectedBoundary is { } target)
                    SwitchToBoundary(target);
                break;

            case BoundaryPickerResult.Create:
                if (dialog.NewBoundaryParent is { } newParent && dialog.NewBoundaryName is { } newName)
                {
                    if (Session.AddBoundary(User, newParent, newName, out var newBoundary, out var addError))
                    {
                        RebuildBoundaryNavItems();
                        SwitchToBoundary(newBoundary!);
                    }
                    else
                    {
                        await ShowError("Create Boundary Failed", addError);
                    }
                }
                break;
        }
    }

    // ── Toast helper ──────────────────────────────────────────────────────
    /// <summary>
    /// Display a toast message that automatically dismisses after <paramref name="durationMs"/> ms.
    /// Calling this again before the previous toast has dismissed resets the timer.
    /// </summary>
    internal void ShowToast(string message, bool isError = false, int durationMs = 3000)
    {
        // Cancel any existing dismiss timer.
        _toastCts?.Cancel();
        _toastCts?.Dispose();
        _toastCts = new CancellationTokenSource();
        var token = _toastCts.Token;

        ToastIsError = isError;
        ToastMessage = message;

        if (isError)
            SystemAlert.PlayError();

        _ = Task.Delay(durationMs, token).ContinueWith(_ =>
        {
            if (!token.IsCancellationRequested)
                ToastMessage = null;
        }, TaskScheduler.Default);
    }

    // ── Error helper ──────────────────────────────────────────────────────
    private async Task ShowError(string title, CommandError? error)
    {
        if (ParentWindow is null) return;
        var dialog = new MessageDialog
        {
            Title   = title,
            Message = error?.Message ?? "An unknown error occurred.",
            Type    = MessageDialog.MessageType.Error
        };
        await dialog.ShowDialog(ParentWindow);
    }

    /// <summary>Select (or deselect) a canvas element.</summary>
    [RelayCommand]
    private void SelectElement(ICanvasElement? element)
    {
        // Deselect any active link (including all sibling VMs for multi-links).
        if (SelectedLink is not null)
        {
            SetLinkGroupSelected(SelectedLink.UnderlyingLink, false);
            SelectedLink = null;
        }
        UnsubscribeDestinations();
        RebuildLinkDestinations();
        if (SelectedElement is not null)
            SelectedElement.IsSelected = false;

        SelectedElement = element;

        if (SelectedElement is not null)
            SelectedElement.IsSelected = true;

        OnPropertyChanged(nameof(NothingSelected));
    }

    /// <summary>Select (or deselect) a link. Clears any element selection.</summary>
    [RelayCommand]
    private void SelectLink(LinkViewModel? link)
    {
        if (SelectedElement is not null)
        {
            SelectedElement.IsSelected = false;
            SelectedElement = null;
        }
        if (SelectedLink is not null)
            SetLinkGroupSelected(SelectedLink.UnderlyingLink, false);
        UnsubscribeDestinations();

        SelectedLink = link;
        if (link is not null)
            SetLinkGroupSelected(link.UnderlyingLink, true);

        SubscribeDestinations(link);
        RebuildLinkDestinations();
        OnPropertyChanged(nameof(SelectedLinkIsMulti));
        OnPropertyChanged(nameof(NothingSelected));
    }

    private void SubscribeDestinations(LinkViewModel? link)
    {
        if (link?.UnderlyingLink is not MultiLink ml) return;
        _subscribedMultiLink = ml;
        _destChangedHandler = (_, _) => RebuildLinkDestinations();
        ((INotifyCollectionChanged)ml.Destinations).CollectionChanged += _destChangedHandler;
    }

    private void UnsubscribeDestinations()
    {
        if (_subscribedMultiLink is not null && _destChangedHandler is not null)
        {
            ((INotifyCollectionChanged)_subscribedMultiLink.Destinations).CollectionChanged
                -= _destChangedHandler;
        }
        _subscribedMultiLink = null;
        _destChangedHandler  = null;
    }

    private void RebuildLinkDestinations()
    {
        SelectedLinkDestinationEntry = null;
        SelectedLinkDestinationEntries.Clear();
        if (SelectedLink?.UnderlyingLink is MultiLink ml)
        {
            foreach (var dest in ml.Destinations)
            {
                var nodeVm = Nodes.FirstOrDefault(n => n.UnderlyingNode == dest);
                if (nodeVm is not null)
                {
                    SelectedLinkDestinationEntries.Add(
                        new LinkDestinationViewModel(nodeVm.Name, nodeVm));
                }
                else 
                {
                    // If the link is connecting to a node in another boundary, we won't find it in the current Nodes collection.
                    // In that case we can still show the node's name by looking it up directly from the model.
                    SelectedLinkDestinationEntries.Add(
                        new LinkDestinationViewModel(dest.Name, new NodeViewModel(dest, Session, User)));
                }
            }
        }
    }

    /// <summary>
    /// Navigates the canvas to the node referenced by <paramref name="dest"/>.
    /// If the node lives in a different boundary the view is switched first, then
    /// the canvas is scrolled to centre on the node.
    /// </summary>
    public void NavigateToLinkDestination(LinkDestinationViewModel dest)
    {
        var node     = dest.NodeVm.UnderlyingNode;
        var boundary = node.ContainedWithin;
        if (boundary is null) return;

        if (!ReferenceEquals(boundary, _currentBoundary))
            SwitchToBoundary(boundary);

        // After a possible boundary switch, Nodes has been rebuilt — look up the fresh VM.
        var nodeVm = Nodes.FirstOrDefault(n => n.UnderlyingNode == node);
        if (nodeVm is not null)
            ScrollToElementRequested?.Invoke(nodeVm);
    }

    /// <summary>Move a MultiLink destination from one index to another (called from code-behind).</summary>
    public void MoveLinkDestination(int fromIndex, int toIndex)
    {
        if (SelectedLink?.UnderlyingLink is not MultiLink ml) return;
        if (!Session.MoveLinkDestination(User, ml, fromIndex, toIndex, out var error) && error is not null)
            _ = ShowError("Move Failed", error);
    }

    /// <summary>
    /// Opens the <see cref="LinkDestinationOrderDialog"/> for <paramref name="ml"/> and,
    /// if the user confirms, applies the requested permutation to the session.
    /// Each position swap is recorded as its own undoable command in the buffer.
    /// </summary>
    /// <param name="ml">The multi-destination link whose order should be edited.</param>
    public async Task ReorderLinkDestinationsAsync(MultiLink ml)
    {
        if (ParentWindow is null) return;

        var names = ml.Destinations.Select(d => d.Name).ToList();
        var dlg   = new LinkDestinationOrderDialog(names);
        
        await dlg.ShowDialog(ParentWindow);

        if (dlg.WasCancelled) return;

        var finalOrder    = dlg.FinalOrderIndices;
        int originalCount = names.Count;

        // 1. Remove destinations that were deleted in the dialog.
        //    Iterate original indices from highest to lowest so that removals
        //    do not shift the positions of items still to be removed.
        var finalOrderSet = new HashSet<int>(finalOrder);
        for (int origIdx = originalCount - 1; origIdx >= 0; origIdx--)
        {
            if (finalOrderSet.Contains(origIdx)) continue;
            if (!Session.RemoveLinkDestination(User, ml, origIdx, out var removeError) && removeError is not null)
            {
                _ = ShowError("Remove Failed", removeError);
                return;
            }
        }

        // 2. Apply the permutation on the surviving items.
        //    Remap each original index to its post-removal position
        //    (surviving items retain their relative order).
        var surviving     = finalOrderSet.OrderBy(x => x).ToList();
        var origToRemapped = new Dictionary<int, int>(surviving.Count);
        for (int i = 0; i < surviving.Count; i++)
            origToRemapped[surviving[i]] = i;
        var remappedOrder = finalOrder.Select(origIdx => origToRemapped[origIdx]).ToList();

        ApplyLinkDestinationPermutation(ml, remappedOrder);
    }

    /// <summary>
    /// Applies a permutation to a <see cref="MultiLink"/> via sequential
    /// <see cref="ModelSystemSession.MoveLinkDestination"/> calls,
    /// keeping an internal state array in sync so index arithmetic stays correct
    /// even as earlier moves shift subsequent positions.
    /// </summary>
    private void ApplyLinkDestinationPermutation(MultiLink ml, IReadOnlyList<int> newOrder)
    {
        int n = newOrder.Count;
        // current[i] holds the original index of the item currently sitting at position i.
        var current = new List<int>(Enumerable.Range(0, n));

        for (int targetPos = 0; targetPos < n; targetPos++)
        {
            int desiredOriginalIdx = newOrder[targetPos];
            int currentPos         = current.IndexOf(desiredOriginalIdx);
            if (currentPos == targetPos) continue;

            if (!Session.MoveLinkDestination(User, ml, currentPos, targetPos, out var error) && error is not null)
            {
                _ = ShowError("Reorder Failed", error);
                return;
            }

            // Mirror the move in our local tracking array.
            current.RemoveAt(currentPos);
            current.Insert(targetPos, desiredOriginalIdx);
        }
    }

    /// <summary>
    /// Removes a single <paramref name="dest"/> entry from the currently selected MultiLink.
    /// Bound to the per-row delete button in the destination list.
    /// </summary>
    [RelayCommand]
    private void RemoveLinkDestinationEntry(LinkDestinationViewModel? dest)
    {
        if (dest is null) return;
        if (SelectedLink?.UnderlyingLink is not MultiLink multiLink) return;
        var idx = SelectedLinkDestinationEntries.IndexOf(dest);
        if (idx < 0) return;
        if (SelectedLinkDestinationEntry == dest)
            SelectedLinkDestinationEntry = null;
        if (!Session.RemoveLinkDestination(User, multiLink, idx, out var error) && error is not null)
            _ = ShowError("Remove Failed", error);
    }

    /// <summary>
    /// Toggles the orthogonal-routing flag on the given link (undo-able).
    /// </summary>
    internal void ToggleLinkOrthogonal(Link link)
    {
        if (!Session.SetLinkOrthogonal(User, link, !link.IsOrthogonal, out var error) && error is not null)
            ShowToast(error.Message ?? "Could not change link routing.", isError: true, durationMs: 4000);
    }

    /// <summary>
    /// Sets <see cref="LinkViewModel.IsSelected"/> on every VM whose
    /// <see cref="LinkViewModel.UnderlyingLink"/> equals <paramref name="underlyingLink"/>.
    /// This ensures all destination arrows for a MultiLink are highlighted together.
    /// </summary>
    private void SetLinkGroupSelected(Link underlyingLink, bool selected)
    {
        foreach (var lvm in Links)
            if (lvm.UnderlyingLink == underlyingLink)
                lvm.IsSelected = selected;
    }

    // ── Undo / Redo commands ──────────────────────────────────────────────

    /// <summary>
    /// Raised after every undo or redo attempt (successful or not) so that the canvas
    /// can invalidate itself; this is necessary because the model change may not produce
    /// any observable-property notification that the canvas already listens to.
    /// </summary>
    internal event EventHandler? RenderRequested;

    /// <summary>Undo the last command in the session buffer.</summary>
    [RelayCommand]
    private async Task Undo()
    {
        if (!Session.Undo(User, out var error))
            await ShowError("Undo Failed", error!);
        RenderRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Redo the previously undone command.</summary>
    [RelayCommand]
    private async Task Redo()
    {
        if (!Session.Redo(User, out var error))
            await ShowError("Redo Failed", error!);
        RenderRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Deletes all elements in <paramref name="elements"/> in one batch.
    /// Called by the canvas when the Delete key is pressed with a multi-selection active.
    /// Nodes, Starts, and CommentBlocks are supported; any other element types are silently skipped.
    /// The first error encountered (if any) is shown to the user after all removals are attempted.
    /// </summary>
    public async Task DeleteMultipleAsync(IReadOnlyList<ICanvasElement> elements)
    {
        // Clear the primary selection so the property panel de-focuses immediately.
        SelectElement(null);

        CommandError? firstError = null;
        foreach (var el in elements)
        {
            CommandError? err = null;
            bool ok = el switch
            {
                NodeViewModel         nvm  => Session.RemoveNode(User, nvm.UnderlyingNode, out err),
                StartViewModel        svm  => Session.RemoveStart(User, svm.UnderlyingStart, out err),
                CommentBlockViewModel cvm  => Session.RemoveCommentBlock(User, _currentBoundary, cvm.UnderlyingBlock, out err),
                FunctionTemplateViewModel ftvm => Session.RemoveFunctionTemplate(User, _currentBoundary, ftvm.UnderlyingTemplate, out err),
                FunctionParameterViewModel fpvm when _currentFunctionTemplate is not null
                    => Session.RemoveFunctionParameter(User, _currentFunctionTemplate.UnderlyingTemplate, fpvm.UnderlyingParameter, out err),
                _  => true,
            };
            if (!ok && err is not null)
                firstError ??= err;
        }

        if (firstError is not null)
            ShowToast(firstError.Message ?? "Delete failed.", isError: true, durationMs: 4000);
    }

    /// <summary>Delete whichever element or link is currently selected.</summary>
    [RelayCommand]
    private async Task DeleteSelected()
    {
        CommandError? error = null;
        bool success = false;

        if (SelectedElement is NodeViewModel nvm)
        {
            SelectElement(null);
            success = Session.RemoveNode(User, nvm.UnderlyingNode, out error);
        }
        else if (SelectedElement is StartViewModel svm)
        {
            SelectElement(null);
            success = Session.RemoveStart(User, svm.UnderlyingStart, out error);
        }
        else if (SelectedElement is CommentBlockViewModel cvm)
        {
            SelectElement(null);
            success = Session.RemoveCommentBlock(User, _currentBoundary, cvm.UnderlyingBlock, out error);
        }
        else if (SelectedElement is FunctionTemplateViewModel ftvm)
        {
            SelectElement(null);
            await DeleteFunctionTemplateAsync(ftvm);
            return;
        }
        else if (SelectedElement is FunctionInstanceViewModel fivm)
        {
            SelectElement(null);
            await DeleteFunctionInstanceAsync(fivm);
            return;
        }
        else if (SelectedElement is FunctionParameterViewModel fpvm
                 && _currentFunctionTemplate is not null)
        {
            SelectElement(null);
            success = Session.RemoveFunctionParameter(
                User, _currentFunctionTemplate.UnderlyingTemplate, fpvm.UnderlyingParameter, out error);
        }
        else if (SelectedElement is GhostNodeViewModel ghostVm)
        {
            SelectElement(null);
            success = Session.RemoveGhostNode(User, ghostVm.UnderlyingGhostNode, out error);
        }
        else if (SelectedLink is { } lvm)
        {
            // If a specific destination is selected in the panel, remove just that entry.
            if (SelectedLinkDestinationEntry is { } destEntry
                && lvm.UnderlyingLink is MultiLink multiLink)
            {
                var idx = SelectedLinkDestinationEntries.IndexOf(destEntry);
                if (idx >= 0)
                {
                    SelectedLinkDestinationEntry = null;
                    success = Session.RemoveLinkDestination(User, multiLink, idx, out error);
                    goto done;
                }
            }
            // Otherwise remove the entire link.
            SelectLink(null);
            success = Session.RemoveLink(User, lvm.UnderlyingLink, out error);
        }
        else return;
        done:

        if (!success && error is not null)
            ShowToast(error.Message ?? "Delete failed.", isError: true, durationMs: 4000);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private Rectangle NextPlacement(int existingCount)
    {
        var offset = existingCount * PlacementStep;
        return new Rectangle(50 + offset % 800, 50 + (offset / 800) * PlacementStep);
    }

    // ── Position-aware add helpers (called from the canvas background context menu) ─

    /// <summary>Add a new Start at the specified canvas position using an auto-generated name.</summary>
    public void AddStartAt(double x, double y)
    {
        var location = new Rectangle((float)x, (float)y);
        Session.AddModelSystemStart(User, _currentBoundary, $"Start {++_startCounter}", location, out _, out _);
    }

    /// <summary>Show a type-picker then add a new module node at the specified canvas position, named after the chosen type.</summary>
    public async Task AddModuleAtAsync(double x, double y)
    {
        if (ParentWindow is null) return;

        var typePicker = new TypePickerDialog(
            Session.LoadedModuleTypes,
            prompt: "Select the module type to add:",
            openGenericModuleTypes: Session.OpenGenericModuleTypes,
            allAvailableTypes: Session.AllAvailableTypes);
        await typePicker.ShowDialog(ParentWindow);

        if (typePicker.WasCancelled || typePicker.SelectedType is null) return;
        var selectedType = typePicker.SelectedType;

        var location = new Rectangle((float)x, (float)y);
        Session.AddNodeGenerateParameters(User, _currentBoundary, FriendlyTypeNameConverter.GetFriendlyName(selectedType), selectedType, location, out var addedNode, out _, out _);

        // AddNodeGenerateParameters also adds parameter child nodes, each of which triggers
        // OnModulesChanged → SelectElement. Re-select the root module node so it ends up selected.
        if (addedNode is not null)
        {
            var nvm = Nodes.FirstOrDefault(v => v.UnderlyingNode == addedNode);
            if (nvm is not null) SelectElement(nvm);
        }
    }

    /// <summary>Add a new comment block at the specified canvas position.</summary>
    public void AddCommentBlockAt(double x, double y)
    {
        var location = new Rectangle((float)x, (float)y,
            (float)CommentBlockViewModel.DefaultWidth,
            (float)CommentBlockViewModel.DefaultHeight);
        Session.AddCommentBlock(User, _currentBoundary, $"Comment {++_commentCounter}", location, out _, out _);
    }

    /// <summary>Create a new function template at the specified canvas position with a unique auto-generated name.</summary>
    public async Task AddFunctionTemplateAtAsync(double x, double y)
    {
        // Generate a name that doesn't clash with any existing template in the boundary.
        int idx = FunctionTemplates.Count + 1;
        string name;
        do { name = $"FunctionTemplate{idx++}"; }
        while (FunctionTemplates.Any(ft => string.Equals(ft.Name, name, StringComparison.OrdinalIgnoreCase)));

        var ftLocation = new Rectangle((float)x, (float)y, 220f, 140f);

        if (!Session.AddFunctionTemplate(User, _currentBoundary, name,
                out var ft, out var error))
        {
            await ShowError("Add Function Template Failed", error);
            return;
        }

        Session.SetFunctionTemplateLocation(User, ft!, ftLocation, out _);
    }

    /// <summary>
    /// Prompts for template and instance names then extracts the specified canvas elements
    /// into an in-place <see cref="FunctionTemplate"/> + <see cref="FunctionInstance"/>.
    /// Shows a toast on failure.
    /// </summary>
    public async Task ExtractSelectionToFunctionTemplateAsync(IReadOnlyList<ICanvasElement> elements)
    {
        if (ParentWindow is null) return;

        // Collect the underlying Node objects (regular nodes + function instances).
        var nodes = elements
            .Select(el => el switch
            {
                NodeViewModel             nvm  => (Node?)nvm.UnderlyingNode,
                FunctionInstanceViewModel fivm => fivm.UnderlyingInstance,
                _                             => null,
            })
            .Where(n => n is not null)
            .Select(n => n!)
            .ToList();

        if (nodes.Count == 0)
        {
            ShowToast("No extractable nodes in the selection.", isError: true, durationMs: 4000);
            return;
        }

        // Default template name: first name not already taken.
        int idx = FunctionTemplates.Count + 1;
        string defaultFtName;
        do { defaultFtName = $"FunctionTemplate{idx++}"; }
        while (FunctionTemplates.Any(ft => string.Equals(ft.Name, defaultFtName, StringComparison.OrdinalIgnoreCase)));

        var ftNameDialog = new InputDialog(
            title: "Extract to Function Template",
            prompt: "Enter function template name:",
            defaultText: defaultFtName);
        await ftNameDialog.ShowDialog(ParentWindow);
        if (ftNameDialog.WasCancelled) return;
        var ftName = ftNameDialog.InputText?.Trim() ?? string.Empty;

        var fiNameDialog = new InputDialog(
            title: "Extract to Function Template",
            prompt: "Enter function instance name:",
            defaultText: ftName);
        await fiNameDialog.ShowDialog(ParentWindow);
        if (fiNameDialog.WasCancelled) return;
        var fiName = fiNameDialog.InputText?.Trim() ?? string.Empty;

        // Compute bounding box of selected nodes to derive placement.
        float minX = nodes.Min(n => n.Location.X);
        float minY = nodes.Min(n => n.Location.Y);
        float maxX = nodes.Max(n => n.Location.X + n.Location.Width);
        float maxY = nodes.Max(n => n.Location.Y + n.Location.Height);

        // FunctionInstance inherits the centroid of the bounding box.
        const float FiWidth  = 160f;
        const float FiHeight =  60f;
        var fiLocation = new Rectangle(
            (minX + maxX - FiWidth)  / 2f,
            (minY + maxY - FiHeight) / 2f,
            FiWidth, FiHeight);

        // FunctionTemplate placed below the selection.
        const float FtGap    =  40f;
        const float FtWidth  = 320f;
        const float FtHeight = 220f;
        var ftLocation = new Rectangle(
            (minX + maxX - FtWidth) / 2f,
            maxY + FtGap,
            FtWidth, FtHeight);

        if (!Session.ExtractToFunctionTemplate(
                User, _currentBoundary, nodes,
                ftName, fiName,
                ftLocation, fiLocation,
                out _, out _, out var error))
        {
            ShowToast(error?.Message ?? "Could not extract to function template.",
                      isError: true, durationMs: 6000);
        }
    }

    /// <summary>Pick a template (when more than one exists) then place a new function instance at the specified canvas position with an auto-generated name.</summary>
    public async Task AddFunctionInstanceAtAsync(double x, double y)
    {
        var availableTemplates = new System.Collections.Generic.List<FunctionTemplate>();
        _currentBoundary.CollectAccessibleFunctionTemplates(availableTemplates);
        if (availableTemplates.Count == 0)
        {
            ShowToast("No function templates are defined in this boundary or its children.", isError: true, durationMs: 4000);
            return;
        }

        FunctionTemplate selectedTemplate;
        if (availableTemplates.Count == 1)
        {
            selectedTemplate = availableTemplates[0];
        }
        else
        {
            if (ParentWindow is null) return;
            var templateDisplayNames = availableTemplates
                .Select(ft => Boundary.GetQualifiedTemplateName(_currentBoundary, ft) ?? ft.Name)
                .ToList();
            var picker = new StartPickerDialog(
                title: "Add Function Instance",
                prompt: "Select the function template to instantiate:",
                startNames: templateDisplayNames,
                defaultStart: templateDisplayNames[0]);
            await picker.ShowDialog(ParentWindow);
            if (picker.WasCancelled) return;
            var picked = picker.SelectedStartName ?? templateDisplayNames[0];
            var pickedIdx = templateDisplayNames.IndexOf(picked);
            selectedTemplate = availableTemplates[pickedIdx >= 0 ? pickedIdx : 0];
        }

        // Generate a unique instance name.
        int idx = FunctionInstances.Count + 1;
        string name;
        do { name = $"{selectedTemplate.Name}{idx++}"; }
        while (FunctionInstances.Any(fi => string.Equals(fi.Name, name, StringComparison.OrdinalIgnoreCase)));

        var location = new Rectangle((float)x, (float)y, 160f, 70f);

        if (!Session.AddFunctionInstanceGenerateParameters(User, _currentBoundary, selectedTemplate, name, location,
                out _, out var children, out var addError))
        {
            await ShowError("Add Function Instance Failed", addError);
        }
        else
        {
            // TODO: We might need to do something with the children here.
        }
    }

    // ── Copy / Paste ──────────────────────────────────────────────────────

    /// <summary>
    /// Pastes a batch of previously copied nodes into <see cref="_currentBoundary"/>,
    /// placing them relative to (<paramref name="anchorX"/>, <paramref name="anchorY"/>)
    /// in canvas (model) coordinates.
    /// <para>
    /// For each entry the method:
    /// <list type="number">
    ///   <item>Creates the main node at the offset position.</item>
    ///   <item>If the node is a parameter node, restores its value.</item>
    ///   <item>Re-creates any hidden (inlined) child nodes that were attached to
    ///         parameter hooks and links them back to their hooks.</item>
    /// </list>
    /// </para>
    /// </summary>
    internal async Task PasteNodesAsync(IReadOnlyList<NodePasteEntry> clipboard, double anchorX, double anchorY)
    {
        if (clipboard.Count == 0) return;

        // Compute the translation that maps the top-left corner of the original bounding
        // box onto (anchorX, anchorY) with a small additional offset so duplicate pastes
        // do not stack exactly on top of each other.
        const float StackOffset = 24f;
        float minX = clipboard.Min(e => e.OriginalLocation.X >= 0 ? e.OriginalLocation.X : 0f);
        float minY = clipboard.Min(e => e.OriginalLocation.Y >= 0 ? e.OriginalLocation.Y : 0f);
        float dx = (float)anchorX - minX + StackOffset;
        float dy = (float)anchorY - minY + StackOffset;

        Node? firstPasted = null;

        foreach (var entry in clipboard)
        {
            if (entry.Type is null) continue;

            float w = entry.OriginalLocation.Width > 0 ? entry.OriginalLocation.Width : 120f;
            float h = entry.OriginalLocation.Height > 0 ? entry.OriginalLocation.Height : 50f;
            var newLoc = new Rectangle(
                entry.OriginalLocation.X + dx,
                entry.OriginalLocation.Y + dy,
                w, h);

            if (!Session.AddNode(User, _currentBoundary, entry.Name, entry.Type, newLoc,
                    out var newNode, out var addError))
            {
                await ShowError("Paste Failed", addError);
                return;
            }

            firstPasted ??= newNode;

            // Restore parameter value.
            if (entry.ParameterValue is not null && newNode is not null)
            {
                if (entry.IsScriptedParam)
                    Session.SetParameterExpression(User, newNode, entry.ParameterValue, out _);
                else
                    Session.SetParameterValue(User, newNode, entry.ParameterValue, out _);
            }

            // Re-create each inlined (hidden) child node and link it to its hook.
            foreach (var (hookName, child) in entry.InlinedChildren)
            {
                if (newNode is null || child.Type is null) continue;

                // Find the hook by name on the newly created node.
                var hook = newNode.Hooks?.FirstOrDefault(h => h.Name == hookName);
                if (hook is null) continue;

                if (!Session.AddNode(User, _currentBoundary, child.Name, child.Type,
                        Rectangle.Hidden, out var childNode, out _))
                    continue;

                // Restore child parameter value.
                if (child.ParameterValue is not null && childNode is not null)
                {
                    if (child.IsScriptedParam)
                        Session.SetParameterExpression(User, childNode, child.ParameterValue, out _);
                    else
                        Session.SetParameterValue(User, childNode, child.ParameterValue, out _);
                }

                // Wire hook → child.
                if (childNode is not null)
                    Session.AddLink(User, newNode, hook, childNode, out _, out _);
            }
        }

        // Select the first pasted node so the user can see where the paste landed.
        if (firstPasted is not null)
        {
            var nvm = Nodes.FirstOrDefault(n => n.UnderlyingNode == firstPasted);
            if (nvm is not null) SelectElement(nvm);
        }
    }

    // ── Universal clipboard paste ─────────────────────────────────────────

    /// <summary>
    /// Pastes all elements contained in <paramref name="payload"/> into the current boundary,
    /// translating each element so its top-left corner is placed at
    /// (<paramref name="anchorX"/>, <paramref name="anchorY"/>) plus a small stack offset.
    /// <para>Supported element kinds:</para>
    /// <list type="bullet">
    ///   <item><see cref="CanvasElementKind.Node"/> — creates a new module node and restores parameter values and inlined children.</item>
    ///   <item><see cref="CanvasElementKind.CommentBlock"/> — creates a new comment block.</item>
    ///   <item><see cref="CanvasElementKind.FunctionTemplate"/> — creates an empty function template shell with matching name and function parameters.</item>
    ///   <item><see cref="CanvasElementKind.FunctionInstance"/> — creates an instance referencing a template with the same name; skipped when no matching template is found.</item>
    ///   <item><see cref="CanvasElementKind.GhostNode"/> — creates a ghost referencing a node with the same name in the current boundary; skipped across model systems.</item>
    /// </list>
    /// </summary>
    internal async Task PasteElementsAsync(CanvasClipboardPayload payload, double anchorX, double anchorY)
    {
        if (payload.Elements.Count == 0) return;

        const float StackOffset = 24f;

        // Compute the translation that maps the bounding-box top-left to (anchorX, anchorY).
        float minX = payload.Elements.Min(e => e.X >= 0 ? e.X : 0f);
        float minY = payload.Elements.Min(e => e.Y >= 0 ? e.Y : 0f);
        float dx = (float)anchorX - minX + StackOffset;
        float dy = (float)anchorY - minY + StackOffset;

        // The entire paste is a single undoable operation.
        Session.BeginBatch();
        try
        {
            // Paste FunctionTemplates first so FunctionInstances that reference them can be resolved.
            foreach (var element in payload.Elements)
            {
                if (element.Kind != CanvasElementKind.FunctionTemplate) continue;
                PasteFunctionTemplate(element, dx, dy);
            }

            // Pass 1: create all nodes (without linking inlined children yet).
            var createdNodes = new List<(CanvasElementDto Element, Node Node)>();
            Node? firstNode = null;
            foreach (var element in payload.Elements)
            {
                switch (element.Kind)
                {
                    case CanvasElementKind.Node:
                        var pastedNode = await PasteNodeBodyAsync(element, dx, dy);
                        if (pastedNode is not null)
                        {
                            firstNode ??= pastedNode;
                            createdNodes.Add((element, pastedNode));
                        }
                        break;

                    case CanvasElementKind.CommentBlock:
                        PasteCommentBlockEntry(element, dx, dy);
                        break;

                    case CanvasElementKind.FunctionInstance:
                        PasteFunctionInstance(element, dx, dy);
                        break;

                    case CanvasElementKind.GhostNode:
                        PasteGhostNodeEntry(element, dx, dy);
                        break;

                    // FunctionTemplate was already handled above.
                }
            }

            // Pass 2: now that all nodes exist, restore inlined children and their links.
            foreach (var (element, node) in createdNodes)
                PasteNodeInlinedChildren(element, node);

            // Pass 3: restore links between pasted nodes (cross-node links).
            if (createdNodes.Count > 1)
            {
                var nameToNode = createdNodes.ToDictionary(e => e.Element.Name, e => e.Node);
                foreach (var (element, originNode) in createdNodes)
                {
                    foreach (var crossLink in element.CrossLinks ?? [])
                    {
                        if (!nameToNode.TryGetValue(crossLink.DestName, out var destNode)) continue;
                        var hook = originNode.Hooks?.FirstOrDefault(h => h.Name == crossLink.HookName);
                        if (hook is null) continue;
                        Session.AddLink(User, originNode, hook, destNode, out _, out _);
                    }
                }
            }

            if (firstNode is not null)
            {
                var nvm = Nodes.FirstOrDefault(n => n.UnderlyingNode == firstNode);
                if (nvm is not null) SelectElement(nvm);
            }
        }
        finally
        {
            Session.CommitBatch();
        }
    }

    // ── Per-element paste helpers ─────────────────────────────────────────

    /// <summary>
    /// Creates the node and restores its parameter value, but does NOT yet create
    /// inlined children or links.  Call <see cref="PasteNodeInlinedChildren"/> in a
    /// second pass once all top-level nodes have been constructed.
    /// </summary>
    private async Task<Node?> PasteNodeBodyAsync(
        CanvasElementDto element, float dx, float dy)
    {
        if (element.TypeName is null) return null;
        var type = Type.GetType(element.TypeName);
        if (type is null) return null;

        float w = element.W > 0 ? element.W : 120f;
        float h = element.H > 0 ? element.H : 50f;
        var newLoc = new Rectangle(element.X + dx, element.Y + dy, w, h);

        if (!Session.AddNode(User, _currentBoundary, element.Name, type, newLoc,
                out var newNode, out var addError))
        {
            await ShowError("Paste Failed", addError);
            return null;
        }

        // Restore parameter value.
        if (element.ParameterValue is not null && newNode is not null)
        {
            if (element.IsScriptedParam)
                Session.SetParameterExpression(User, newNode, element.ParameterValue, out _);
            else
                Session.SetParameterValue(User, newNode, element.ParameterValue, out _);
        }

        return newNode;
    }

    /// <summary>
    /// Second-pass helper: re-creates inlined (hidden) child nodes for <paramref name="newNode"/>
    /// and wires them to their hooks.  Must be called after all top-level nodes have been
    /// created by <see cref="PasteNodeBodyAsync"/>.
    /// </summary>
    private void PasteNodeInlinedChildren(CanvasElementDto element, Node newNode)
    {
        // Re-create inlined (hidden) child nodes.
        foreach (var inlined in element.InlinedChildren ?? [])
        {
            if (inlined.Child.TypeName is null) continue;
            var childType = Type.GetType(inlined.Child.TypeName);
            if (childType is null) continue;

            var hook = newNode.Hooks?.FirstOrDefault(h => h.Name == inlined.HookName);
            if (hook is null) continue;

            if (!Session.AddNode(User, _currentBoundary, inlined.Child.Name, childType,
                    Rectangle.Hidden, out var childNode, out _))
                continue;

            if (inlined.Child.ParameterValue is not null && childNode is not null)
            {
                if (inlined.Child.IsScriptedParam)
                    Session.SetParameterExpression(User, childNode, inlined.Child.ParameterValue, out _);
                else
                    Session.SetParameterValue(User, childNode, inlined.Child.ParameterValue, out _);
            }

            if (childNode is not null)
                Session.AddLink(User, newNode, hook, childNode, out _, out _);
        }
    }

    private void PasteCommentBlockEntry(CanvasElementDto element, float dx, float dy)
    {
        float w = element.W > 0 ? element.W : (float)CommentBlockViewModel.DefaultWidth;
        float h = element.H > 0 ? element.H : (float)CommentBlockViewModel.DefaultHeight;
        var loc = new Rectangle(element.X + dx, element.Y + dy, w, h);
        // Name on CommentBlock stores the comment text.
        Session.AddCommentBlock(User, _currentBoundary, element.Name, loc, out _, out _);
    }

    private void PasteFunctionTemplate(CanvasElementDto element, float dx, float dy)
    {
        // Generate a unique name to avoid collision in the target boundary.
        string name = element.Name;
        int suffix = 2;
        while (FunctionTemplates.Any(ft => string.Equals(ft.Name, name, StringComparison.OrdinalIgnoreCase)))
            name = $"{element.Name} ({suffix++})";

        float w = element.W > 0 ? element.W : 220f;
        float h = element.H > 0 ? element.H : 140f;
        var loc = new Rectangle(element.X + dx, element.Y + dy, w, h);

        if (!Session.AddFunctionTemplate(User, _currentBoundary, name, out var ft, out _))
            return;

        Session.SetFunctionTemplateLocation(User, ft!, loc, out _);

        // Re-create function parameters where the CLR type can be resolved.
        foreach (var fp in element.FunctionParameters ?? [])
        {
            if (fp.TypeName is null) continue;
            var fpType = Type.GetType(fp.TypeName);
            if (fpType is null) continue;
            Session.AddFunctionParameter(User, ft!, fp.Name, fpType, Rectangle.Hidden, out _, out _);
        }
    }

    private void PasteFunctionInstance(CanvasElementDto element, float dx, float dy)
    {
        if (element.TemplateName is null) return;

        // Find an accessible function template by that name.
        var candidates = new System.Collections.Generic.List<XTMF2.ModelSystemConstruct.FunctionTemplate>();
        _currentBoundary.CollectAccessibleFunctionTemplates(candidates);
        var template = candidates.FirstOrDefault(ft =>
            string.Equals(ft.Name, element.TemplateName, StringComparison.OrdinalIgnoreCase));
        if (template is null) return;

        // Generate a unique name if needed.
        string name = element.Name;
        int suffix = 2;
        while (FunctionInstances.Any(fi => string.Equals(fi.Name, name, StringComparison.OrdinalIgnoreCase)))
            name = $"{element.Name} ({suffix++})";

        float w = element.W > 0 ? element.W : 160f;
        float h = element.H > 0 ? element.H : 70f;
        var loc = new Rectangle(element.X + dx, element.Y + dy, w, h);

        Session.AddFunctionInstance(User, _currentBoundary, template, name, loc, out _, out _);
    }

    private void PasteGhostNodeEntry(CanvasElementDto element, float dx, float dy)
    {
        if (element.ReferencedNodeName is null) return;

        // Try to find the referenced real node by name in the current boundary's visible nodes.
        var referencedNvm = Nodes.FirstOrDefault(n =>
            string.Equals(n.Name, element.ReferencedNodeName, StringComparison.OrdinalIgnoreCase)
            && !n.IsInlined);
        if (referencedNvm is null) return;

        float w = element.W > 0 ? element.W : 120f;
        float h = element.H > 0 ? element.H : 50f;
        var loc = new Rectangle(element.X + dx, element.Y + dy, w, h);

        Session.AddGhostNode(User, _currentBoundary, referencedNvm.UnderlyingNode, loc, out _, out _);
    }

    // ── IDisposable ───────────────────────────────────────────────────────
    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        ((System.Collections.Specialized.INotifyCollectionChanged)Session.ModelSystem.Variables)
            .CollectionChanged -= OnModelSystemVariablesChanged;

        ((System.Collections.Specialized.INotifyCollectionChanged)Session.ModelSystem.EstimationGroups)
            .CollectionChanged -= OnEstimationGroupsChanged;
        ((System.Collections.Specialized.INotifyCollectionChanged)Session.ModelSystem.CalibrationGroups)
            .CollectionChanged -= OnCalibrationGroupsChanged;

        foreach (var gvm in EstimationGroups) gvm.Detach();
        foreach (var gvm in CalibrationGroups) gvm.Detach();

        ((System.ComponentModel.INotifyPropertyChanged)Session).PropertyChanged -= OnSessionPropertyChanged;

        foreach (var varVm in ModelSystemVariables) varVm.Detach();

        foreach (var ft in FunctionTemplates) ft.Detach();

        UnsubscribeFromBoundary(_currentBoundary);

        foreach (var lvm in Links) lvm.Detach();

        Session.Dispose();
    }
}
