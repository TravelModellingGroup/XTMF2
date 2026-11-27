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
using XTMF2.GUI.Controls;
using XTMF2.ModelSystemConstruct;

namespace XTMF2.GUI.Views;

public partial class FunctionNodePickerDialog : Window, INotifyPropertyChanged
{
    private readonly List<FunctionNodePickerItem> _allItems;
    private readonly List<FunctionNodePickerItem> _visibleNodeItems = [];
    private string _filterText = string.Empty;
    private object? _selectedItem;

    public new event PropertyChangedEventHandler? PropertyChanged;

    public string Prompt { get; }
    public ObservableCollection<FunctionNodePickerItem> FilteredItems { get; } = [];

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (_filterText == value) return;
            _filterText = value;
            Raise(nameof(FilterText));
            RefreshFilteredItems();
        }
    }

    public object? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (ReferenceEquals(_selectedItem, value)) return;
            _selectedItem = value;
            Raise(nameof(SelectedItem));
            Raise(nameof(CanSelect));
        }
    }

    public bool CanSelect => SelectedItem is FunctionNodePickerItem { Node: not null };
    public bool HasResults => FilteredItems.Count > 0;
    public bool Confirmed { get; private set; }
    public Node? SelectedNode { get; private set; }

    public FunctionNodePickerDialog()
        : this([], null, "function output")
    {
    }

    public FunctionNodePickerDialog(IEnumerable<Node> nodes, Node? selectedNode, string outputLabel)
    {
        Title = $"Select {outputLabel}";
        Prompt = $"Choose a function node for the {outputLabel}. Nodes are grouped by boundary; each node shows its module type.";
        _allItems = BuildItems(nodes);

        DataContext = this;
        InitializeComponent();
        RefreshFilteredItems();
        FilterBox.KeyDown += OnFilterBoxKeyDown;
        Opened += (_, _) => FilterBox.FocusSearchBox();

        if (selectedNode is not null)
            SelectedItem = FindNodeItem(FilteredItems, selectedNode);

        AddHandler(KeyDownEvent, OnDialogKeyDown, RoutingStrategies.Tunnel);
    }

    private void Raise(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static List<FunctionNodePickerItem> BuildItems(IEnumerable<Node> nodes)
    {
        var roots = new List<FunctionNodePickerItem>();
        var boundaryItems = new Dictionary<Boundary, FunctionNodePickerItem>();

        foreach (var node in nodes
                     .OrderBy(n => n.ContainedWithin.FullPath, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase))
        {
            var boundaryPath = new Stack<Boundary>();
            for (var boundary = node.ContainedWithin; boundary is not null; boundary = boundary.Parent)
                boundaryPath.Push(boundary);

            FunctionNodePickerItem? parent = null;
            while (boundaryPath.TryPop(out var boundary))
            {
                if (!boundaryItems.TryGetValue(boundary, out var boundaryItem))
                {
                    boundaryItem = FunctionNodePickerItem.ForBoundary(boundary);
                    boundaryItems.Add(boundary, boundaryItem);
                    if (parent is null)
                        roots.Add(boundaryItem);
                    else
                        parent.Children.Add(boundaryItem);
                }

                parent = boundaryItem;
            }

            parent!.Children.Add(FunctionNodePickerItem.ForNode(node));
        }

        return roots;
    }

    private void RefreshFilteredItems()
    {
        var selectedNode = (SelectedItem as FunctionNodePickerItem)?.Node;
        var filter = FilterText.Trim();
        FilteredItems.Clear();

        foreach (var item in _allItems)
        {
            var filtered = FilterItem(item, filter);
            if (filtered is not null)
                FilteredItems.Add(filtered);
        }

        _visibleNodeItems.Clear();
        AddNodeItems(FilteredItems, _visibleNodeItems);
        Raise(nameof(HasResults));
        SelectedItem = selectedNode is null ? null : FindNodeItem(FilteredItems, selectedNode);
    }

    private static void AddNodeItems(
        IEnumerable<FunctionNodePickerItem> items,
        List<FunctionNodePickerItem> nodeItems)
    {
        foreach (var item in items)
        {
            if (item.Node is not null)
                nodeItems.Add(item);
            else
                AddNodeItems(item.Children, nodeItems);
        }
    }

    private static FunctionNodePickerItem? FilterItem(FunctionNodePickerItem item, string filter)
    {
        if (item.Node is not null)
        {
            return filter.Length == 0
                || item.SearchText.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    ? item
                    : null;
        }

        var children = item.Children
            .Select(child => FilterItem(child, filter))
            .Where(child => child is not null)
            .Cast<FunctionNodePickerItem>()
            .ToList();
        if (children.Count == 0) return null;

        var filtered = FunctionNodePickerItem.ForBoundary(item.Boundary!);
        foreach (var child in children)
            filtered.Children.Add(child);
        return filtered;
    }

    private static FunctionNodePickerItem? FindNodeItem(
        IEnumerable<FunctionNodePickerItem> items,
        Node node)
    {
        foreach (var item in items)
        {
            if (ReferenceEquals(item.Node, node))
                return item;

            var match = FindNodeItem(item.Children, node);
            if (match is not null)
                return match;
        }

        return null;
    }

    private void FilterBox_EnterPressed(object? sender, RoutedEventArgs e)
    {
        if (CanSelect)
            ConfirmSelection();
        else
            CandidateTree.Focus(NavigationMethod.Tab);
    }

    private void OnFilterBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None ||
            e.Key is not (Key.Up or Key.Down) ||
            _visibleNodeItems.Count == 0)
        {
            return;
        }

        var currentIndex = SelectedItem is FunctionNodePickerItem { Node: not null } selectedItem
            ? _visibleNodeItems.IndexOf(selectedItem)
            : -1;
        var nextIndex = currentIndex < 0
            ? 0
            : Math.Clamp(currentIndex + (e.Key == Key.Down ? 1 : -1), 0, _visibleNodeItems.Count - 1);

        SelectedItem = _visibleNodeItems[nextIndex];
        e.Handled = true;
    }

    private void CandidateTree_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (CanSelect)
            ConfirmSelection();
    }

    private void CandidateTree_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && CanSelect)
        {
            ConfirmSelection();
            e.Handled = true;
        }
    }

    private void OnDialogKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && string.IsNullOrEmpty(FilterText))
        {
            Cancel_Click(null, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void Select_Click(object? sender, RoutedEventArgs e) => ConfirmSelection();

    private void ConfirmSelection()
    {
        if (SelectedItem is not FunctionNodePickerItem { Node: { } node })
            return;

        SelectedNode = node;
        Confirmed = true;
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close();
    }
}

public sealed class FunctionNodePickerItem
{
    private FunctionNodePickerItem(
        string displayName,
        string detail,
        bool isNode,
        Node? node,
        Boundary? boundary,
        string searchText)
    {
        DisplayName = displayName;
        Detail = detail;
        IsNode = isNode;
        Node = node;
        Boundary = boundary;
        SearchText = searchText;
    }

    public string DisplayName { get; }
    public string Detail { get; }
    public bool IsNode { get; }
    public Node? Node { get; }
    public Boundary? Boundary { get; }
    public string SearchText { get; }
    public bool IsExpanded => !IsNode;
    public ObservableCollection<FunctionNodePickerItem> Children { get; } = [];

    public static FunctionNodePickerItem ForBoundary(Boundary boundary) =>
        new(boundary.Name, string.Empty, false, null, boundary, string.Empty);

    public static FunctionNodePickerItem ForNode(Node node) =>
        new(
            node.Name,
            node.Type.Name,
            true,
            node,
            null,
            $"{node.Name} {node.Type.Name} {node.ContainedWithin.FullPath}");
}
