using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace XTMF2.GUI.Views;

public partial class CommandPaletteDropdown : UserControl
{
    private CommandPaletteAction[] allActions = [];

    public ObservableCollection<CommandPaletteAction> FilteredActions { get; } = [];
    public event EventHandler? SearchFocused;
    public event EventHandler? CanvasFocusRequested;

    public CommandPaletteDropdown()
    {
        InitializeComponent();
        DataContext = this;
        ActionPopup.PlacementTarget = this;
        ActionPopup.Placement = PlacementMode.Center;
        ActionPopup.Opened += (_, _) => SearchBox.Focus();
        SearchBox.GotFocus += OnSearchBoxGotFocus;
        SearchBox.PropertyChanged += OnSearchBoxPropertyChanged;
        SearchBox.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        ActionList.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        FilterActions();
    }

    public void SetActions(IEnumerable<CommandPaletteAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        allActions = actions.ToArray();
        if (allActions.Any(action => action is null))
        {
            throw new ArgumentException("The action collection cannot contain null entries.", nameof(actions));
        }

        FilterActions();
    }

    public void FocusSearch()
    {
        SearchBox.Text = string.Empty;
        ActionPopup.IsOpen = true;
        SearchBox.Focus();
    }

    public void CenterPopupOn(Control target)
    {
        ArgumentNullException.ThrowIfNull(target);
        ActionPopup.PlacementTarget = target;
        ActionPopup.Placement = PlacementMode.Center;
    }

    private void OnSearchBoxGotFocus(object? sender, GotFocusEventArgs e)
    {
        SearchFocused?.Invoke(this, EventArgs.Empty);
        ActionPopup.IsOpen = true;
    }

    private void OnSearchBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != TextBox.TextProperty)
            return;

        FilterActions();
        if (SearchBox.IsFocused)
            ActionPopup.IsOpen = true;
    }

    private void FilterActions()
    {
        var query = SearchBox?.Text?.Trim();
        var matchingActions = string.IsNullOrEmpty(query)
            ? allActions
            : allActions.Where(action =>
                action.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (action.CommandType?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (action.Shortcut?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));

        FilteredActions.Clear();
        foreach (var action in matchingActions)
        {
            FilteredActions.Add(action);
        }

        NoResultsText.IsVisible = FilteredActions.Count == 0;
        ActionList.SelectedItem = FilteredActions.FirstOrDefault(action => action.IsEnabled);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape when ActionPopup.IsOpen:
                ActionPopup.IsOpen = false;
                SearchBox.Text = string.Empty;
                ActionPopup.IsOpen = false;
                CanvasFocusRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
            case Key.Up when ActionPopup.IsOpen:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Down when ActionPopup.IsOpen:
                MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Enter when ActionPopup.IsOpen:
                if (ActionList.SelectedItem is CommandPaletteAction { IsEnabled: true } action)
                    ExecuteAction(action);
                e.Handled = true;
                break;
        }
    }

    private void OnActionButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: CommandPaletteAction { IsEnabled: true } action })
            ExecuteAction(action);
    }

    private void ExecuteAction(CommandPaletteAction action)
    {
        SearchBox.Text = string.Empty;
        ActionPopup.IsOpen = false;
        action.Execute();
    }

    private void MoveSelection(int direction)
    {
        if (FilteredActions.Count == 0) return;

        var selectedIndex = ActionList.SelectedItem is CommandPaletteAction selected
            ? FilteredActions.IndexOf(selected)
            : direction > 0 ? -1 : FilteredActions.Count;

        for (var index = selectedIndex + direction;
             index >= 0 && index < FilteredActions.Count;
             index += direction)
        {
            if (!FilteredActions[index].IsEnabled) continue;

            ActionList.SelectedItem = FilteredActions[index];
            ActionList.ScrollIntoView(FilteredActions[index]);
            return;
        }
    }
}
