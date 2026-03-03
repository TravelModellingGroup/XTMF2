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
using XTMF2.GUI.Resources;
using XTMF2.GUI.Views;
using XTMF2.ModelSystemConstruct;

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
            return string.Join(" › ", parts);
        }
    }

    /// <summary><c>true</c> when the current boundary is the global (root) boundary.</summary>
    public bool IsAtRootBoundary => ReferenceEquals(_currentBoundary, GlobalBoundary);

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

    /// <summary>The currently selected link, if any. Mutually exclusive with <see cref="SelectedElement"/>.</summary>
    [ObservableProperty]
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

    /// <summary>
    /// Human-readable type string shown in the property panel's "Type:" row.
    /// Automatically updates when the node's type changes.
    /// </summary>
    public string SelectedElementTypeName => SelectedElement switch
    {
        StartViewModel             => "Start (entry point)",
        NodeViewModel nvm          => $"Module\n{nvm.TypeName}",
        CommentBlockViewModel      => "Comment Block",
        _                          => string.Empty
    };

    /// <summary>All module types currently registered in the runtime.</summary>
    public System.Collections.ObjectModel.ReadOnlyObservableCollection<Type> AvailableModuleTypes
        => Session.LoadedModuleTypes;

    /// <summary>
    /// When <c>true</c> the canvas renders every hook on every node.
    /// When <c>false</c> only hooks that have an active link are shown.
    /// </summary>
    [ObservableProperty]
    private bool _showAllHooks;

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
    }

    private void OnSelectedElementPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NodeViewModel.TypeName))
            OnPropertyChanged(nameof(SelectedElementTypeName));
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
    public ModelSystemEditorViewModel(ModelSystemSession session, User user)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(user);
        Session = session;
        User = user;

        // Build initial VM collections from the active boundary.
        _currentBoundary = GlobalBoundary;
        BuildFromBoundary(_currentBoundary);
        SubscribeToBoundary(_currentBoundary);
        RebuildBoundaryNavItems();
    }

    // ── Collection sync ───────────────────────────────────────────────────
    private void BuildFromBoundary(Boundary boundary)
    {
        foreach (var node in boundary.Modules)        Nodes.Add(new NodeViewModel(node, Session, User));
        foreach (var start in boundary.Starts)        Starts.Add(new StartViewModel(start, Session, User));
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

        _currentBoundary = boundary;
        OnPropertyChanged(nameof(CurrentBoundary));
        OnPropertyChanged(nameof(CurrentBoundaryLabel));
        OnPropertyChanged(nameof(IsAtRootBoundary));

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
                Nodes.Add(new NodeViewModel(n, Session, User));

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
                Starts.Add(new StartViewModel(s, Session, User));

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
                CommentBlocks.Add(new CommentBlockViewModel(cb, Session, User));

        if (e.OldItems is not null)
            foreach (CommentBlock cb in e.OldItems)
            {
                var vm = CommentBlocks.FirstOrDefault(v => v.UnderlyingBlock == cb);
                if (vm is not null) CommentBlocks.Remove(vm);
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
        if (node is Start start)
            return Starts.FirstOrDefault(s => s.UnderlyingStart == start);
        return Nodes.FirstOrDefault(n => n.UnderlyingNode == node);
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
        Session.AddNode(User, _currentBoundary, name, selectedType, location, out _, out _);
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
        var compatibleList = new ObservableCollection<Type>(
            Session.LoadedModuleTypes.Where(t => hookElementType.IsAssignableFrom(t)));
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

        if (!Session.AddNode(User, _currentBoundary, name, selectedType, location, out var newNode, out var nodeError))
        {
            await ShowError("Add Module Failed", nodeError);
            return;
        }

        // Wire the hook to the new node.
        if (!Session.AddLink(User, originNode.UnderlyingNode, hook, newNode!, out _, out var linkError))
            await ShowError("Create Link Failed", linkError);
    }

    /// <summary>
    /// Attempt to create a link from <paramref name="originElement"/> to <paramref name="destVm"/>.
    /// If any compatible hooks are found, either uses the sole hook automatically or
    /// presents a <see cref="HookPickerDialog"/> when there are multiple options.
    /// Called directly by the canvas (not a RelayCommand because it requires typed parameters).
    /// </summary>
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
    private void ShowToast(string message, bool isError = false, int durationMs = 3000)
    {
        // Cancel any existing dismiss timer.
        _toastCts?.Cancel();
        _toastCts?.Dispose();
        _toastCts = new CancellationTokenSource();
        var token = _toastCts.Token;

        ToastIsError  = isError;
        ToastMessage  = message;

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

    // ── IDisposable ───────────────────────────────────────────────────────
    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        UnsubscribeFromBoundary(_currentBoundary);

        foreach (var lvm in Links) lvm.Detach();

        Session.Dispose();
    }
}
