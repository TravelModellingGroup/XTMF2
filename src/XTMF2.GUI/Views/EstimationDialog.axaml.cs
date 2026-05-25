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
    along with XTMF2.  If not, see <http://www.gnu.org/licenses/\>.
*/
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using XTMF2.Bus.Optimization;
using XTMF2.GUI;
using XTMF2.GUI.ViewModels;
using XTMF2.ModelSystemConstruct;

namespace XTMF2.GUI.Views;

/// <summary>
/// Dialog for viewing and editing estimation parameter groups.
/// Groups contain parameter entries; a single fitness function node
/// (IFunction&lt;float&gt; or IFunction&lt;double&gt;) is shared across all groups.
/// </summary>
public partial class EstimationDialog : Window, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>All available estimation algorithms, for binding to the algorithm ComboBox.</summary>
    public static IReadOnlyList<EstimationAlgorithmConfig> AvailableAlgorithms { get; } =
        EstimationAlgorithmConfig.AvailableAlgorithms;

    /// <summary>All available estimation objectives, for binding to the objective ComboBox.</summary>
    public static IReadOnlyList<EstimationObjective> AvailableObjectives { get; } =
        (EstimationObjective[])Enum.GetValues(typeof(EstimationObjective));

    public ModelSystemEditorViewModel EditorVm { get; }

    private bool _allowClose;
    private readonly ObservableCollection<EstimationGroupViewModel> _filteredGroupVms = [];
    private readonly ObservableCollection<EstimationEntryViewModel> _filteredEntryVms = [];
    private readonly HashSet<EstimationGroupViewModel> _hookedGroups = [];
    private readonly HashSet<EstimationEntryViewModel> _hookedEntries = [];
    private INotifyCollectionChanged? _selectedGroupParamsNotify;

    private string _groupSearchText = string.Empty;
    public string GroupSearchText
    {
        get => _groupSearchText;
        set
        {
            if (_groupSearchText == value) return;
            _groupSearchText = value;
            Raise(nameof(GroupSearchText));
            RefreshFilteredGroups();
        }
    }

    private string _targetSearchText = string.Empty;
    public string TargetSearchText
    {
        get => _targetSearchText;
        set
        {
            if (_targetSearchText == value) return;
            _targetSearchText = value;
            Raise(nameof(TargetSearchText));
            RefreshFilteredEntries();
        }
    }

    // ── Groups ────────────────────────────────────────────────────────────

    public ObservableCollection<EstimationGroupViewModel> GroupVms =>
        EditorVm?.EstimationGroups ?? new ObservableCollection<EstimationGroupViewModel>();

    public ObservableCollection<EstimationGroupViewModel> FilteredGroupVms => _filteredGroupVms;

    private EstimationGroupViewModel? _selectedGroup;
    public EstimationGroupViewModel? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            _selectedGroup = value;
            RebindSelectedGroupEntries();
            Raise(nameof(SelectedGroup));
            Raise(nameof(HasSelectedGroup));
            Raise(nameof(GroupIsEnabled));
            Raise(nameof(SelectedGroupEntries));
            Raise(nameof(FilteredGroupEntries));
            RefreshFilteredEntries();
        }
    }

    public bool HasSelectedGroup => _selectedGroup is not null;

    // ── Fitness node (model-system level) ─────────────────────────────────

    // ── Estimation algorithm and objective ────────────────────────────────

    private bool _suppressAlgorithmCommand;

    public EstimationAlgorithmConfig SelectedAlgorithmConfig
    {
        get => EditorVm.Session.ModelSystem.EstimationAlgorithmConfig;
        set
        {
            if (value is null || _suppressAlgorithmCommand) return;
            if (EditorVm.Session.ModelSystem.EstimationAlgorithmConfig.AlgorithmId == value.AlgorithmId) return;
            EditorVm.Session.SetEstimationAlgorithmConfig(EditorVm.User, value, out _);
            Raise(nameof(SelectedAlgorithmConfig));
        }
    }

    public EstimationObjective SelectedObjective
    {
        get => EditorVm.Session.ModelSystem.EstimationObjective;
        set
        {
            if (EditorVm.Session.ModelSystem.EstimationObjective == value) return;
            EditorVm.Session.SetEstimationObjective(EditorVm.User, value, out _);
            Raise(nameof(SelectedObjective));
        }
    }

    private Node? _selectedFitnessNode;
    private bool _suppressFitnessCommand;

    public Node? SelectedFitnessNode
    {
        get => _selectedFitnessNode;
        set
        {
            if (_selectedFitnessNode == value) return;
            _selectedFitnessNode = value;
            Raise(nameof(SelectedFitnessNode));
            if (!_suppressFitnessCommand)
                EditorVm.Session.SetEstimationFitnessNode(EditorVm.User, value, out _);
        }
    }

    public List<Node> FunctionNodes => EditorVm?.GetFunctionNodes() ?? new();

    // ── Group enabled toggle ──────────────────────────────────────────────

    public bool GroupIsEnabled
    {
        get => _selectedGroup?.UnderlyingGroup.IsEnabled ?? false;
        set
        {
            if (_selectedGroup is null) return;
            if (_selectedGroup.UnderlyingGroup.IsEnabled == value) return;
            EditorVm.Session.SetEstimationGroupEnabled(
                EditorVm.User, _selectedGroup.UnderlyingGroup, value, out _);
        }
    }

    // ── Entries ───────────────────────────────────────────────────────────

    private static readonly ObservableCollection<EstimationEntryViewModel> _emptyEntries = new();

    public ObservableCollection<EstimationEntryViewModel> SelectedGroupEntries =>
        _selectedGroup?.Parameters ?? _emptyEntries;

    public ObservableCollection<EstimationEntryViewModel> FilteredGroupEntries => _filteredEntryVms;



    // ── Edit fields ───────────────────────────────────────────────────────



    // ── Construction ──────────────────────────────────────────────────────

    /// <summary>Required by the Avalonia AXAML compiler (design-time).</summary>
    public EstimationDialog()
    {
        EditorVm = null!;
        DataContext = this;
        InitializeComponent();
    }

    public EstimationDialog(ModelSystemEditorViewModel editorVm)
    {
        EditorVm = editorVm;
        DataContext = this;
        InitializeComponent();
        AttachGroupFilterHandlers();
        RefreshFilteredGroups();
        SelectedGroup = GroupVms.FirstOrDefault();
        _suppressFitnessCommand = true;
        SelectedFitnessNode = EditorVm.Session.ModelSystem.EstimationFitnessNode;
        _suppressFitnessCommand = false;
        // Sync algorithm selection without firing a command change.
        _suppressAlgorithmCommand = true;
        Raise(nameof(SelectedAlgorithmConfig));
        Raise(nameof(SelectedObjective));
        _suppressAlgorithmCommand = false;
        RefreshFilteredEntries();
    }

    private void AttachGroupFilterHandlers()
    {
        GroupVms.CollectionChanged += (_, _) =>
        {
            foreach (var g in GroupVms)
            {
                if (_hookedGroups.Add(g))
                    g.PropertyChanged += GroupVm_PropertyChanged;
            }
            _hookedGroups.RemoveWhere(g => !GroupVms.Contains(g));
            RefreshFilteredGroups();
        };

        foreach (var g in GroupVms)
        {
            if (_hookedGroups.Add(g))
                g.PropertyChanged += GroupVm_PropertyChanged;
        }
    }

    private void GroupVm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EstimationGroupViewModel.Name))
            RefreshFilteredGroups();
    }

    private void RebindSelectedGroupEntries()
    {
        if (_selectedGroupParamsNotify is not null)
            _selectedGroupParamsNotify.CollectionChanged -= SelectedGroupEntries_CollectionChanged;

        foreach (var e in _hookedEntries)
            e.PropertyChanged -= EntryVm_PropertyChanged;
        _hookedEntries.Clear();

        _selectedGroupParamsNotify = _selectedGroup?.Parameters;
        if (_selectedGroupParamsNotify is not null)
        {
            _selectedGroupParamsNotify.CollectionChanged += SelectedGroupEntries_CollectionChanged;
            foreach (var e in SelectedGroupEntries)
            {
                if (_hookedEntries.Add(e))
                    e.PropertyChanged += EntryVm_PropertyChanged;
            }
        }
    }

    private void SelectedGroupEntries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var vm in SelectedGroupEntries)
        {
            if (_hookedEntries.Add(vm))
                vm.PropertyChanged += EntryVm_PropertyChanged;
        }
        _hookedEntries.RemoveWhere(vm => !SelectedGroupEntries.Contains(vm));
        RefreshFilteredEntries();
    }

    private void EntryVm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EstimationEntryViewModel.NodeName))
            RefreshFilteredEntries();
    }

    private void RefreshFilteredGroups()
    {
        var f = GroupSearchText.Trim();
        _filteredGroupVms.Clear();
        foreach (var g in GroupVms)
        {
            if (f.Length == 0 || g.Name.Contains(f, StringComparison.OrdinalIgnoreCase))
                _filteredGroupVms.Add(g);
        }
        Raise(nameof(FilteredGroupVms));
    }

    private void RefreshFilteredEntries()
    {
        var f = TargetSearchText.Trim();
        _filteredEntryVms.Clear();
        foreach (var e in SelectedGroupEntries)
        {
            if (f.Length == 0 || e.NodeName.Contains(f, StringComparison.OrdinalIgnoreCase))
                _filteredEntryVms.Add(e);
        }
        Raise(nameof(FilteredGroupEntries));
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_allowClose)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;

        if (await FinalizeEntryEditsAsync())
        {
            _allowClose = true;
            base.OnClosing(e);
            Close();
        }
    }

    // ── Group event handlers ──────────────────────────────────────────────

    private async void AddGroup_Click(object? sender, RoutedEventArgs e)
    {
        if (!EditorVm.Session.AddEstimationGroup(EditorVm.User, "New Group",
                out var newGroup, out var error))
        {
            await new MessageDialog("Add Group Failed", error.Message).ShowDialog(this);
            return;
        }
        SelectedGroup = EditorVm.EstimationGroups.FirstOrDefault(g => g.UnderlyingGroup == newGroup);
    }

    private async void RemoveGroup_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not EstimationGroupViewModel vm) return;
        var group = vm.UnderlyingGroup;
        if (_selectedGroup?.UnderlyingGroup == group) SelectedGroup = null;
        if (!EditorVm.Session.RemoveEstimationGroup(EditorVm.User, group, out var error))
            await new MessageDialog("Remove Group Failed", error.Message).ShowDialog(this);
    }

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || sender is not TextBox tb) return;
        if (!string.IsNullOrEmpty(tb.Text))
        {
            tb.Text = string.Empty;
            e.Handled = true;
        }
    }

    private async void GroupEnabled_Changed(object? sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox cb) return;
        if (cb.DataContext is not EstimationGroupViewModel vm) return;
        var isEnabled = cb.IsChecked ?? true;
        if (!EditorVm.Session.SetEstimationGroupEnabled(
                EditorVm.User, vm.UnderlyingGroup, isEnabled, out var error))
            await new MessageDialog("Update Failed", error.Message).ShowDialog(this);
    }

    private void BeginGroupRename(EstimationGroupViewModel vm)
    {
        // Cancel editing on any other group first
        foreach (var g in GroupVms)
            if (g != vm && g.IsEditing) g.IsEditing = false;
        vm.EditName = vm.Name;
        vm.IsEditing = true;
        // Focus the TextBox after the layout pass
        Dispatcher.UIThread.Post(() =>
        {
            var lb = this.FindControl<ListBox>("GroupListBox");
            if (lb is null) return;
            var target = lb.GetVisualDescendants().OfType<TextBox>()
                           .FirstOrDefault(tb => tb.DataContext == vm && tb.IsVisible);
            target?.Focus();
            target?.SelectAll();
        }, DispatcherPriority.Render);
    }

    private void CommitGroupRenameVm(EstimationGroupViewModel vm)
    {
        vm.IsEditing = false;
        var name = vm.EditName.Trim();
        if (string.IsNullOrEmpty(name) || name == vm.Name) return;
        EditorVm.Session.RenameEstimationGroup(EditorVm.User, vm.UnderlyingGroup, name, out _);
    }

    private void GroupList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2 && _selectedGroup is not null)
        {
            BeginGroupRename(_selectedGroup);
            e.Handled = true;
        }
    }

    private void GroupRenameBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb || tb.DataContext is not EstimationGroupViewModel vm) return;
        if (e.Key == Key.Return || e.Key == Key.Enter)
        {
            CommitGroupRenameVm(vm);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.IsEditing = false;
            e.Handled = true;
        }
    }

    private void GroupRenameBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is EstimationGroupViewModel vm)
            CommitGroupRenameVm(vm);
    }

    private void RenameGroup_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is EstimationGroupViewModel vm)
            BeginGroupRename(vm);
    }

    private void GoToNode_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.Tag is not Node node) return;
        EditorVm.NavigateToElementById(node.Id);
    }

    private void ClearFitnessNode_Click(object? sender, RoutedEventArgs e)
    {
        EditorVm.Session.SetEstimationFitnessNode(EditorVm.User, null, out _);
        _suppressFitnessCommand = true;
        _selectedFitnessNode = null;
        _suppressFitnessCommand = false;
        Raise(nameof(SelectedFitnessNode));
    }

    private async void ConfigureAlgorithm_Click(object? sender, RoutedEventArgs e)
    {
        var configDialog = new EstimationAlgorithmConfigDialog(
            EditorVm, EditorVm.Session.ModelSystem.EstimationAlgorithmConfig);
        await configDialog.ShowDialog(this);
        // The dialog applies the change via session.SetEstimationAlgorithmConfig,
        // so the model is already updated. Raise the property so the ComboBox
        // reflects any type change that may have been triggered externally.
        Raise(nameof(SelectedAlgorithmConfig));
    }

    // ── Entry event handlers ──────────────────────────────────────────────

    private async void RemoveEntry_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is EstimationEntryViewModel vm && _selectedGroup is not null)
        {
            if (!EditorVm.Session.RemoveEstimationParameter(
                    EditorVm.User, _selectedGroup.UnderlyingGroup, vm.Entry, out var error))
                await new MessageDialog("Remove Failed", error.Message).ShowDialog(this);
        }
    }

    private async void EntryTextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb) return;
        if (tb.DataContext is not EstimationEntryViewModel vm) return;
        await CommitEntryAsync(vm);
    }

    private void EntryNumericBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb)
            tb.Classes.Set("invalid-number", !IsValidOrPartialDouble(tb.Text ?? string.Empty));
    }

    private static bool IsValidOrPartialDouble(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return true;
        var t = s.Trim();
        if (double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out _)) return true;
        // Allow a bare sign while the user begins typing
        if (t is "+" or "-") return true;
        // Allow trailing decimal point: "1."
        if (t.EndsWith('.') && double.TryParse(t[..^1], NumberStyles.Any, CultureInfo.InvariantCulture, out _)) return true;
        // Allow incomplete exponent: "1e", "1e+", "1e-"
        var eIdx = t.LastIndexOfAny(['e', 'E']);
        if (eIdx > 0)
        {
            var exp = t[(eIdx + 1)..];
            if ((exp is "" or "+" or "-") &&
                double.TryParse(t[..eIdx], NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                return true;
        }
        return false;
    }

    private async void EntryTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb) return;
        if (e.Key is Key.Return or Key.Enter)
        {
            if (tb.DataContext is not EstimationEntryViewModel vm) return;
            await CommitEntryAsync(vm);
            e.Handled = true;
        }
        else if (e.Key is Key.Tab or Key.Up or Key.Down)
        {
            bool backward = e.KeyModifiers.HasFlag(KeyModifiers.Shift)
                         || e.KeyModifiers.HasFlag(KeyModifiers.Control);
            NavigateEntryTextBox(tb, e.Key, backward);
            e.Handled = true;
        }
    }

    private static void NavigateEntryTextBox(TextBox current, Key key, bool shiftHeld = false)
    {
        var lbi = current.FindAncestorOfType<ListBoxItem>();
        if (lbi is null) return;
        var lb = lbi.FindAncestorOfType<ListBox>();
        if (lb is null) return;

        var rows = lb.GetVisualDescendants().OfType<ListBoxItem>().ToList();
        int rowIdx = rows.IndexOf(lbi);
        if (rowIdx < 0) return;

        var cols = lbi.GetVisualDescendants().OfType<TextBox>().ToList();
        int colIdx = cols.IndexOf(current);
        if (colIdx < 0) return;

        TextBox? target = null;
        if (key == Key.Tab && !shiftHeld)
            target = colIdx < cols.Count - 1 ? cols[colIdx + 1]
                   : rowIdx < rows.Count - 1 ? rows[rowIdx + 1].GetVisualDescendants().OfType<TextBox>().FirstOrDefault()
                   : null;
        else if (key == Key.Tab && shiftHeld)
            target = colIdx > 0 ? cols[colIdx - 1]
                   : rowIdx > 0 ? rows[rowIdx - 1].GetVisualDescendants().OfType<TextBox>().LastOrDefault()
                   : null;
        else if (key == Key.Up && rowIdx > 0)
            target = rows[rowIdx - 1].GetVisualDescendants().OfType<TextBox>().ElementAtOrDefault(colIdx);
        else if (key == Key.Down && rowIdx < rows.Count - 1)
            target = rows[rowIdx + 1].GetVisualDescendants().OfType<TextBox>().ElementAtOrDefault(colIdx);

        if (target is not null)
        {
            target.Focus();
            target.SelectAll();
        }
    }

    private async void EntryEnabled_Changed(object? sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox cb) return;
        if (cb.DataContext is not EstimationEntryViewModel vm) return;
        // Use cb.IsChecked directly — the TwoWay binding may not yet have propagated
        // the new state to vm.IsEnabled when IsCheckedChanged fires.
        var isEnabled = cb.IsChecked ?? true;
        if (!double.TryParse(vm.EditMin,            NumberStyles.Any, CultureInfo.InvariantCulture, out var min) ||
            !double.TryParse(vm.EditMax,            NumberStyles.Any, CultureInfo.InvariantCulture, out var max) ||
            !double.TryParse(vm.EditNullHypothesis, NumberStyles.Any, CultureInfo.InvariantCulture, out var nullHyp))
            return;
        if (!EditorVm.Session.UpdateEstimationParameter(
                EditorVm.User, vm.Entry, min, max, nullHyp, isEnabled, out var error))
            await new MessageDialog("Update Failed", error.Message).ShowDialog(this);
    }

    private async Task<bool> FinalizeEntryEditsAsync()
    {
        bool hadInvalid = false;
        foreach (var vm in GroupVms.SelectMany(g => g.Parameters))
        {
            if (!await TryCommitEntryAsync(vm, revertOnInvalid: true))
                hadInvalid = true;
        }

        if (hadInvalid)
        {
            SystemAlert.PlayError();
            var confirm = new ValidationIssueDialog(
                "Validation Issues",
                "One or more values were invalid and have been reset to their previous values. Press OK to close anyway, or Cancel to keep editing.");
            await confirm.ShowDialog(this);
            return confirm.Result;
        }

        return true;
    }

    private Task CommitEntryAsync(EstimationEntryViewModel vm) => TryCommitEntryAsync(vm, revertOnInvalid: false);

    private async Task<bool> TryCommitEntryAsync(EstimationEntryViewModel vm, bool revertOnInvalid)
    {
        if (!double.TryParse(vm.EditMin,            NumberStyles.Any, CultureInfo.InvariantCulture, out var min) ||
            !double.TryParse(vm.EditMax,            NumberStyles.Any, CultureInfo.InvariantCulture, out var max) ||
            !double.TryParse(vm.EditNullHypothesis, NumberStyles.Any, CultureInfo.InvariantCulture, out var nullHyp))
        {
            if (revertOnInvalid)
            {
                vm.EditMin            = vm.Min.ToString("G6", CultureInfo.InvariantCulture);
                vm.EditMax            = vm.Max.ToString("G6", CultureInfo.InvariantCulture);
                vm.EditNullHypothesis = vm.NullHypothesis.ToString("G6", CultureInfo.InvariantCulture);
            }
            return false;
        }
        if (!EditorVm.Session.UpdateEstimationParameter(
                EditorVm.User, vm.Entry, min, max, nullHyp, vm.IsEnabled, out var error))
        {
            if (revertOnInvalid)
            {
                vm.EditMin            = vm.Min.ToString("G6", CultureInfo.InvariantCulture);
                vm.EditMax            = vm.Max.ToString("G6", CultureInfo.InvariantCulture);
                vm.EditNullHypothesis = vm.NullHypothesis.ToString("G6", CultureInfo.InvariantCulture);
            }
            await new MessageDialog("Update Failed", error.Message).ShowDialog(this);
            return false;
        }
        return true;
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
