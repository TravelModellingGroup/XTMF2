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
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    public string Title => ModelSystemHeader.Name ?? "Model System";

    /// <summary>Allow the user to close this tab.</summary>
    public bool CanClose => true;

    // ── Canvas collections ────────────────────────────────────────────────
    /// <summary>Observable wrappers around <see cref="Boundary.Modules"/>.</summary>
    public ObservableCollection<NodeViewModel> Nodes { get; } = new();

    /// <summary>Observable wrappers around <see cref="Boundary.Starts"/>.</summary>
    public ObservableCollection<StartViewModel> Starts { get; } = new();

    /// <summary>Observable wrappers around <see cref="Boundary.Links"/>.</summary>
    public ObservableCollection<LinkViewModel> Links { get; } = new();

    /// <summary>Observable wrappers around <see cref="Boundary.CommentBlocks"/>.</summary>
    public ObservableCollection<CommentBlockViewModel> CommentBlocks { get; } = new();

    /// <summary>Observable wrappers around <see cref="Boundary.GhostNodes"/>.</summary>
    public ObservableCollection<GhostNodeViewModel> GhostNodes { get; } = new();

    /// <summary>Observable wrappers around <see cref="Boundary.FunctionTemplates"/>.</summary>
    public ObservableCollection<FunctionTemplateViewModel> FunctionTemplates { get; } = new();

    /// <summary>Observable wrappers around <see cref="Boundary.FunctionInstances"/>.</summary>
    public ObservableCollection<FunctionInstanceViewModel> FunctionInstances { get; } = new();

    /// <summary>Observable view-models for the model system's variable list.</summary>
    public ObservableCollection<ModelSystemVariableViewModel> ModelSystemVariables { get; } = new();

    /// <summary>Text typed into the variables filter box; filters <see cref="FilteredModelSystemVariables"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredModelSystemVariables))]
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

    // ── Node search ───────────────────────────────────────────────────────
    /// <summary>Fires when the user picks a node from the search box; the view should scroll to it.</summary>
    public event Action<NodeViewModel>? ScrollToNodeRequested;

    /// <summary>Bound to the AutoCompleteBox SelectedItem; triggers navigation when set.</summary>
    [ObservableProperty]
    private NodeViewModel? _nodeSearchSelection;

    partial void OnNodeSearchSelectionChanged(NodeViewModel? value)
    {
        if (value is null) return;
        SelectElement(value);
        ScrollToNodeRequested?.Invoke(value);
        NodeSearchSelection = null; // reset so the box is ready for the next search
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
        _                          => string.Empty
    };

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
    /// The exposed hook nodes of the template referenced by the currently selected
    /// function instance, or an empty collection.
    /// </summary>
    public System.Collections.Generic.IEnumerable<ModelSystemConstruct.Node> SelectedFunctionInstanceExposedHooks
        => (SelectedElement as FunctionInstanceViewModel)?.ExposedHooks
           ?? System.Linq.Enumerable.Empty<ModelSystemConstruct.Node>();

    /// <summary>
    /// <c>true</c> when the selected function instance's template has no exposed hook nodes —
    /// drives the "(none)" hint text in the properties panel.
    /// </summary>
    public bool SelectedFunctionInstanceHasNoExposedHooks
        => SelectedElement is not FunctionInstanceViewModel fi || fi.ExposedHooks.Count == 0;

    /// <summary>
    /// The exposed-node list of the currently selected function template, or an empty list
    /// when nothing (or a non-template element) is selected. Bound by the toolbox panel.
    /// </summary>
    public IEnumerable<Node> SelectedFunctionTemplateExposedNodes
        => (SelectedElement as FunctionTemplateViewModel)?.ExposedNodes
           ?? System.Linq.Enumerable.Empty<Node>();

    /// <summary>
    /// <c>true</c> when the selected function template has no exposed hook nodes —
    /// drives the "(none)" hint text in the properties panel.
    /// </summary>
    public bool SelectedFunctionTemplateHasNoExposedNodes
        => SelectedElement is not FunctionTemplateViewModel ft || ft.ExposedNodes.Count == 0;

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
        OnPropertyChanged(nameof(SelectedFunctionTemplateExposedNodes));
        OnPropertyChanged(nameof(SelectedFunctionTemplateHasNoExposedNodes));
        OnPropertyChanged(nameof(SelectedElementIsFunctionInstance));
        OnPropertyChanged(nameof(SelectedFunctionInstanceTemplateName));
        OnPropertyChanged(nameof(SelectedFunctionInstanceExposedHooks));
        OnPropertyChanged(nameof(SelectedFunctionInstanceHasNoExposedHooks));
        SelectedElementParameterValue =
            value is NodeViewModel pnvm && pnvm.IsParameterNode
                ? pnvm.ParameterValueRepresentation
                : string.Empty;
    }

    private void OnSelectedElementPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NodeViewModel.TypeName))
            OnPropertyChanged(nameof(SelectedElementTypeName));
        if (e.PropertyName == nameof(NodeViewModel.ParameterValueRepresentation))
        {
            if (SelectedElement is NodeViewModel nvm && nvm.IsParameterNode)
                SelectedElementParameterValue = nvm.ParameterValueRepresentation;
        }
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

        // Mirror CanUndo/CanRedo from the session reactively.
        _canUndo = Session.CanUndo;
        _canRedo = Session.CanRedo;
        ((System.ComponentModel.INotifyPropertyChanged)Session).PropertyChanged += OnSessionPropertyChanged;
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
        Nodes.Clear();
        Starts.Clear();
        Links.Clear();
        CommentBlocks.Clear();
        GhostNodes.Clear();
        foreach (var ft in FunctionTemplates) ft.Detach();
        FunctionTemplates.Clear();
        foreach (var fi in FunctionInstances) fi.Detach();
        FunctionInstances.Clear();

        _currentBoundary = boundary;
        OnPropertyChanged(nameof(CurrentBoundary));
        OnPropertyChanged(nameof(CurrentBoundaryLabel));
        OnPropertyChanged(nameof(IsAtRootBoundary));
        // If the new boundary is not the InternalModules of the tracked template, leave FT mode.
        if (_currentFunctionTemplate is not null
            && !ReferenceEquals(boundary, _currentFunctionTemplate.UnderlyingTemplate.InternalModules))
        {
            _currentFunctionTemplate = null;
            OnPropertyChanged(nameof(IsInsideFunctionTemplate));
            ExitFunctionTemplateCommand.NotifyCanExecuteChanged();
        }

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
        }
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
            prompt: "Select the module type to add:");
        await typePicker.ShowDialog(ParentWindow);

        if (typePicker.WasCancelled || typePicker.SelectedType is null) return;
        var selectedType = typePicker.SelectedType;

        // Step 2: choose a name (pre-filled from the type's short name).
        var nameDialog = new InputDialog(
            title: "Add Module",
            prompt: "Enter module name:",
            defaultText: selectedType.Name);
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
            initialType: nvm.UnderlyingNode.Type);
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
            NodeViewModel  nvm => nvm.UnderlyingNode,
            StartViewModel svm => svm.UnderlyingStart,
            _                  => null
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

    public async Task CreateLinkAsync(ICanvasElement originElement, NodeViewModel destVm)
    {
        if (ParentWindow is null) return;

        // Resolve the underlying origin node (Start is also a Node).
        Node? originNode = originElement switch
        {
            NodeViewModel  nvm => nvm.UnderlyingNode,
            StartViewModel svm => svm.UnderlyingStart,
            _                  => null
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

        var innerTypeName      = FriendlyTypeNameConverter.GetFriendlyName(innerType);
        var currentValue       = node.ParameterValue?.Representation ?? string.Empty;
        var isCurrentlyScripted =
            nodeType.IsGenericType &&
            nodeType.GetGenericTypeDefinition() == typeof(ScriptedParameter<>);

        // Basic validator: use ArbitraryParameterParser
        string? BasicValidator(string v)
        {
            string? err = null;
            return ArbitraryParameterParser.Check(innerType, v, ref err) ? null : (err ?? $"'{v}' is not valid for type {innerTypeName}.");
        }

        // Scripted validator: accept any non-empty text; the session will catch compile errors.
        string? ScriptedValidator(string v) =>
            string.IsNullOrWhiteSpace(v) ? "Expression cannot be empty." : null;

        var dialog = new ParameterEditorDialog(
            innerTypeName:      innerTypeName,
            currentValue:       currentValue,
            isCurrentlyScripted: isCurrentlyScripted,
            basicValidator:     BasicValidator,
            scriptedValidator:  ScriptedValidator);

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
    /// Navigates to the boundary containing the variable's node and selects it.
    /// Bound to the "Go To" button in the variables panel.
    /// </summary>
    [RelayCommand]
    private void GoToVariableNode(ModelSystemVariableViewModel varVm)
    {
        var targetBoundary = varVm.UnderlyingNode.ContainedWithin;
        if (targetBoundary is not null)
            SwitchToBoundary(targetBoundary);

        // Find the NodeViewModel for this node in the current boundary's Nodes collection.
        var nvm = Nodes.FirstOrDefault(n => ReferenceEquals(n.UnderlyingNode, varVm.UnderlyingNode));
        if (nvm is not null)
        {
            SelectElement(nvm);
            ScrollToNodeRequested?.Invoke(nvm);
        }
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
        OnPropertyChanged(nameof(FilteredModelSystemVariables));
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

        if (!Session.AddFunctionInstance(User, _currentBoundary, selectedTemplate, name, location,
                out _, out var error))
        {
            await ShowError("Add Function Instance Failed", error);
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
    /// Navigates the canvas into <paramref name="ftvm"/>'s
    /// <see cref="FunctionTemplate.InternalModules"/> boundary so the user can
    /// edit the nodes contained within the function template.
    /// </summary>
    public void NavigateIntoFunctionTemplate(FunctionTemplateViewModel ftvm)
    {
        _currentFunctionTemplate = ftvm;
        OnPropertyChanged(nameof(IsInsideFunctionTemplate));
        ExitFunctionTemplateCommand.NotifyCanExecuteChanged();
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
        SwitchToBoundary(parentBoundary);
    }

    /// <summary>
    /// Toggles whether <paramref name="nvm"/> (a node in the current
    /// <see cref="FunctionTemplate.InternalModules"/>) is exposed as an external hook
    /// on the template's canvas container.
    /// <para>
    /// Only available when the canvas is inside a function template (i.e.
    /// <see cref="IsInsideFunctionTemplate"/> is <c>true</c>).
    /// </para>
    /// </summary>
    public async Task ToggleFunctionTemplateExposedNodeAsync(NodeViewModel nvm)
    {
        if (_currentFunctionTemplate is null) return;

        if (!Session.ToggleFunctionTemplateExposedNode(
                User, _currentFunctionTemplate.UnderlyingTemplate, nvm.UnderlyingNode, out var error))
            await ShowError("Toggle Exposed Hook Failed", error);
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
        if (!_runController.SendRun(project, Session, startToExecute, runName, out _, out var runError))
        {
            ShowToast($"Failed to start run: {runError?.Message}", isError: true, durationMs: 6000);
            return;
        }

        ShowToast($"Run '{runName}' started.", durationMs: 3000);
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
            ScrollToNodeRequested?.Invoke(nodeVm);
    }

    /// <summary>Move a MultiLink destination from one index to another (called from code-behind).</summary>
    public void MoveLinkDestination(int fromIndex, int toIndex)
    {
        if (SelectedLink?.UnderlyingLink is not MultiLink ml) return;
        if (!Session.MoveLinkDestination(User, ml, fromIndex, toIndex, out var error) && error is not null)
            _ = ShowError("Move Failed", error);
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
                NodeViewModel         nvm => Session.RemoveNode(User, nvm.UnderlyingNode, out err),
                StartViewModel        svm => Session.RemoveStart(User, svm.UnderlyingStart, out err),
                CommentBlockViewModel cvm => Session.RemoveCommentBlock(User, _currentBoundary, cvm.UnderlyingBlock, out err),
                FunctionTemplateViewModel ftvm => Session.RemoveFunctionTemplate(User, _currentBoundary, ftvm.UnderlyingTemplate, out err),
                _                        => true,
            };
            if (!ok && err is not null)
                firstError ??= err;
        }

        if (firstError is not null)
            await ShowError("Delete Failed", firstError);
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
            await ShowError("Delete Failed", error);
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
            prompt: "Select the module type to add:");
        await typePicker.ShowDialog(ParentWindow);

        if (typePicker.WasCancelled || typePicker.SelectedType is null) return;
        var selectedType = typePicker.SelectedType;

        var location = new Rectangle((float)x, (float)y);
        Session.AddNodeGenerateParameters(User, _currentBoundary, selectedType.Name, selectedType, location, out var addedNode, out _, out _);

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

        if (!Session.AddFunctionInstance(User, _currentBoundary, selectedTemplate, name, location,
                out _, out var addError))
        {
            await ShowError("Add Function Instance Failed", addError);
        }
    }

    // ── IDisposable ───────────────────────────────────────────────────────
    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        ((System.Collections.Specialized.INotifyCollectionChanged)Session.ModelSystem.Variables)
            .CollectionChanged -= OnModelSystemVariablesChanged;

        ((System.ComponentModel.INotifyPropertyChanged)Session).PropertyChanged -= OnSessionPropertyChanged;

        foreach (var varVm in ModelSystemVariables) varVm.Detach();

        foreach (var ft in FunctionTemplates) ft.Detach();

        UnsubscribeFromBoundary(_currentBoundary);

        foreach (var lvm in Links) lvm.Detach();

        Session.Dispose();
    }
}
