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
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using XTMF2;
using XTMF2.ModelSystemConstruct;
using XTMF2.GUI.ViewModels;

namespace XTMF2.GUI.Views;

/// <summary>
/// A two-phase wizard dialog for creating a link from a node hook to a module
/// that lives in a different boundary.
/// <para>
/// Phase 1: the user selects the target boundary from a flat, indented list of all
///          boundaries in the model system.
/// Phase 2: the user selects a compatible module from that boundary. Both regular
///          nodes and function instances are considered; only targets whose type is
///          assignable to the hook's element type are shown.
/// </para>
/// </summary>
public partial class InterBoundaryLinkDialog : Window, INotifyPropertyChanged
{
    // ── Nested display item for Phase 2 ──────────────────────────────────
    /// <summary>A row in the compatible-node list shown during Phase 2.</summary>
    public sealed class NodePickEntry
    {
        /// <summary>The node's name as displayed in the list.</summary>
        public string DisplayName { get; }

        /// <summary>The short name of the module's CLR type.</summary>
        public string TypeDisplayName { get; }

        /// <summary>The underlying model node.</summary>
        public Node UnderlyingNode { get; }

        internal NodePickEntry(Node node)
        {
            DisplayName     = node.Name;
            TypeDisplayName = node.Type?.Name ?? "(unknown type)";
            UnderlyingNode  = node;
        }
    }

    // ── State ─────────────────────────────────────────────────────────────
    private bool _isPhase1 = true;
    private BoundaryBrowseItem? _selectedBoundaryItem;
    private NodePickEntry? _selectedNode;
    private string _boundarySearchText = string.Empty;
    private string _nodeSearchText     = string.Empty;

    private readonly Type _hookElementType;

    // ── Bindable Phase 1 data ─────────────────────────────────────────────
    /// <summary>Flat indented list of all boundaries available for selection.</summary>
    public IReadOnlyList<BoundaryBrowseItem> BoundaryItems { get; }

    /// <summary>Subset of <see cref="BoundaryItems"/> that match the current search text.</summary>
    public ObservableCollection<BoundaryBrowseItem> FilteredBoundaryItems { get; } = new();

    /// <summary>The boundary row currently highlighted in Phase 1.</summary>
    public BoundaryBrowseItem? SelectedBoundaryItem
    {
        get => _selectedBoundaryItem;
        set { _selectedBoundaryItem = value; Notify(nameof(SelectedBoundaryItem)); }
    }

    /// <summary>Live search text for the boundary list (drives <see cref="FilteredBoundaryItems"/>).</summary>
    public string BoundarySearchText
    {
        get => _boundarySearchText;
        set
        {
            _boundarySearchText = value;
            Notify(nameof(BoundarySearchText));
            ApplyBoundaryFilter(value);
        }
    }

    // ── Bindable Phase 2 data ─────────────────────────────────────────────
    /// <summary>All type-compatible nodes for the chosen boundary (full unfiltered set).</summary>
    public ObservableCollection<NodePickEntry> CompatibleNodes { get; } = new();

    /// <summary>Subset of <see cref="CompatibleNodes"/> that match the current search text.</summary>
    public ObservableCollection<NodePickEntry> FilteredNodes { get; } = new();

    /// <summary>The node currently highlighted in Phase 2.</summary>
    public NodePickEntry? SelectedNode
    {
        get => _selectedNode;
        set { _selectedNode = value; Notify(nameof(SelectedNode)); }
    }

    /// <summary>Live search text for the node list (drives <see cref="FilteredNodes"/>).</summary>
    public string NodeSearchText
    {
        get => _nodeSearchText;
        set
        {
            _nodeSearchText = value;
            Notify(nameof(NodeSearchText));
            ApplyNodeFilter(value);
        }
    }

    /// <summary><c>true</c> when the chosen boundary has no compatible nodes at all (type check).</summary>
    public bool NoCompatibleNodes  => CompatibleNodes.Count == 0;

    /// <summary><c>true</c> when the chosen boundary has at least one compatible node.</summary>
    public bool HasCompatibleNodes => CompatibleNodes.Count > 0;

    // ── Phase visibility ──────────────────────────────────────────────────
    /// <summary><c>true</c> while the boundary-selection phase is active.</summary>
    public bool IsPhase1 =>  _isPhase1;

    /// <summary><c>true</c> while the node-selection phase is active.</summary>
    public bool IsPhase2 => !_isPhase1;

    // ── Display strings ───────────────────────────────────────────────────
    /// <summary>Heading shown in Phase 1.</summary>
    public string Phase1Title { get; }

    /// <summary>Heading shown in Phase 2 (includes the chosen boundary name).</summary>
    public string Phase2Title =>
        $"Select a compatible module from '{_selectedBoundaryItem?.Boundary.Name ?? "?"}'";

    /// <summary>Subtitle in Phase 2 reminding the user of the type constraint.</summary>
    public string Phase2Subtitle { get; }

    // ── Result ────────────────────────────────────────────────────────────
    /// <summary><c>true</c> when the dialog was closed without confirming a selection.</summary>
    public bool WasCancelled { get; private set; } = true;

    /// <summary>The node chosen by the user, or <c>null</c> when the dialog was cancelled.</summary>
    public Node? ChosenNode { get; private set; }

    public new event PropertyChangedEventHandler? PropertyChanged;

    private void Notify(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    // ── Constructors ──────────────────────────────────────────────────────

    /// <summary>Design-time / XMLC-required parameterless constructor.</summary>
    public InterBoundaryLinkDialog() : this([], null!, "node") { }

    /// <summary>
    /// Creates the dialog ready for a two-phase link-creation workflow.
    /// </summary>
    /// <param name="allBoundaries">
    ///     All boundaries in the model system, each paired with its nesting depth (0 = root).
    ///     Typically obtained via <c>GetAllBoundaries(GlobalBoundary)</c>.
    /// </param>
    /// <param name="hook">
    ///     The origin hook from which the link will be created.  Used to derive the
    ///     element type and to build descriptive prompt strings.
    /// </param>
    /// <param name="originNodeName">Name of the origin node (used in prompts).</param>
    public InterBoundaryLinkDialog(
        IReadOnlyList<(Boundary Boundary, int Depth)> allBoundaries,
        NodeHook hook,
        string originNodeName)
    {
        // Compute the assignable element type for this hook.
        _hookElementType =
            hook is not null &&
            (hook.Cardinality is HookCardinality.AtLeastOne or HookCardinality.AnyNumber)
                ? (hook.Type.GetElementType() ?? hook.Type)
                : (hook?.Type ?? typeof(object));

        var hookName   = hook?.Name ?? "hook";
        Phase1Title    = $"Link '{originNodeName}.{hookName}' → select a boundary:";
        Phase2Subtitle = $"Showing modules compatible with '{hookName}' ({_hookElementType.Name}).";

        BoundaryItems = allBoundaries
            .Select(t => new BoundaryBrowseItem(t.Boundary, t.Depth))
            .ToList();

        InitializeComponent();
        DataContext = this;
        AddHandler(KeyDownEvent, (_, ke) =>
        {
            if (ke.Key != Key.Escape) return;
            Cancel_Click(null, new RoutedEventArgs());
            ke.Handled = true;
        }, RoutingStrategies.Tunnel);

        // Initialise the filtered boundary list with everything.
        foreach (var item in BoundaryItems)
            FilteredBoundaryItems.Add(item);

        // Wire up the search boxes.
        BoundarySearchBox.EnterPressed += OnBoundarySearchEnterPressed;
        BoundarySearchBox.KeyDown      += OnBoundarySearchKeyDown;
        NodeSearchBox.EnterPressed     += OnNodeSearchEnterPressed;
        NodeSearchBox.KeyDown          += OnNodeSearchKeyDown;

        // Pre-select the first boundary and focus the search box when the window opens.
        Opened += (_, _) =>
        {
            if (BoundaryListBox.ItemCount > 0)
                BoundaryListBox.SelectedIndex = 0;
            BoundarySearchBox.FocusSearchBox();
        };
    }

    // ── Search filtering ──────────────────────────────────────────────────

    private void ApplyBoundaryFilter(string query)
    {
        var trimmed = query.Trim();
        FilteredBoundaryItems.Clear();
        foreach (var item in BoundaryItems)
        {
            if (trimmed.Length == 0 ||
                item.Boundary.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
                FilteredBoundaryItems.Add(item);
        }
        SelectedBoundaryItem = FilteredBoundaryItems.FirstOrDefault();
        if (FilteredBoundaryItems.Count > 0)
            BoundaryListBox.SelectedIndex = 0;
    }

    private void ApplyNodeFilter(string query)
    {
        var trimmed = query.Trim();
        FilteredNodes.Clear();
        foreach (var item in CompatibleNodes)
        {
            if (trimmed.Length == 0 ||
                item.DisplayName.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                item.TypeDisplayName.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
                FilteredNodes.Add(item);
        }
        SelectedNode = FilteredNodes.FirstOrDefault();
        if (FilteredNodes.Count > 0)
            NodeListBox.SelectedIndex = 0;
    }

    private void OnBoundarySearchEnterPressed(object? sender, RoutedEventArgs e)
    {
        SelectedBoundaryItem = FilteredBoundaryItems.FirstOrDefault();
        if (SelectedBoundaryItem is not null)
        {
            BoundaryListBox.SelectedIndex = 0;
            Next_Click(sender, e);
        }
    }

    private void OnBoundarySearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            BoundaryListBox.Focus();
            e.Handled = true;
        }
    }

    private void OnNodeSearchEnterPressed(object? sender, RoutedEventArgs e)
    {
        SelectedNode = FilteredNodes.FirstOrDefault();
        if (SelectedNode is not null)
            Link_Click(sender, e);
    }

    private void OnNodeSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            NodeListBox.Focus();
            e.Handled = true;
        }
    }

    // ── Button handlers ───────────────────────────────────────────────────

    private void Next_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedBoundaryItem is null) return;

        // Build the full type-compatible node list for the chosen boundary.
        CompatibleNodes.Clear();
        FilteredNodes.Clear();
        foreach (var node in _selectedBoundaryItem.Boundary.Modules)
        {
            if (node.Type is not null && _hookElementType.IsAssignableFrom(node.Type))
            {
                CompatibleNodes.Add(new NodePickEntry(node));
            }
        }

        foreach (var functionInstance in _selectedBoundaryItem.Boundary.FunctionInstances)
        {
            if (functionInstance.Type is not null && _hookElementType.IsAssignableFrom(functionInstance.Type))
            {
                CompatibleNodes.Add(new NodePickEntry(functionInstance));
            }
        }

        // Start with no search text so all compatible nodes are visible.
        NodeSearchText = string.Empty;  // resets FilteredNodes via property setter

        SelectedNode = null;
        _isPhase1    = false;

        Notify(nameof(IsPhase1));
        Notify(nameof(IsPhase2));
        Notify(nameof(Phase2Title));
        Notify(nameof(NoCompatibleNodes));
        Notify(nameof(HasCompatibleNodes));

        // Focus the node search box (or the window when the list is empty).
        Dispatcher.UIThread.Post(() =>
        {
            if (NodeListBox.ItemCount > 0)
            {
                NodeListBox.SelectedIndex = 0;
                SelectedNode = FilteredNodes.FirstOrDefault();
            }
            NodeSearchBox.FocusSearchBox();
        });
    }

    private void Back_Click(object? sender, RoutedEventArgs e)
    {
        _isPhase1 = true;
        Notify(nameof(IsPhase1));
        Notify(nameof(IsPhase2));

        Dispatcher.UIThread.Post(() =>
        {
            BoundarySearchBox.Focus();

        });
    }

    private void Link_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedNode is null) return;
        ChosenNode   = _selectedNode.UnderlyingNode;
        WasCancelled = false;
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        WasCancelled = true;
        Close();
    }
}
