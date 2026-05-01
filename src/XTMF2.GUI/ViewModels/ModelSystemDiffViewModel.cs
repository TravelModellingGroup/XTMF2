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
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using XTMF2.Diff;
using XTMF2;

namespace XTMF2.GUI.ViewModels;

/// <summary>
/// View model for the model system diff tab.
/// Computes the diff on a background thread and exposes a tree of display items.
/// </summary>
public sealed partial class ModelSystemDiffViewModel : ObservableObject
{
    private readonly Func<bool, ModelSystemDiff> _diffFactory;
    private ModelSystemDiff? _diff;
    private bool _isSwapped;

    // ── Navigation ────────────────────────────────────────────────────────
    /// <summary>
    /// Header for the left (base) model system. Null when loaded from a file path.
    /// </summary>
    public ModelSystemHeader? LeftHeader { get; private set; }

    /// <summary>
    /// Header for the right (compare) model system. Null when loaded from a file path.
    /// </summary>
    public ModelSystemHeader? RightHeader { get; private set; }

    /// <summary>
    /// The header that is currently the <em>base</em> side, accounting for swaps.
    /// Equals <see cref="LeftHeader"/> when not swapped, <see cref="RightHeader"/> when swapped.
    /// </summary>
    private ModelSystemHeader? EffectiveLeftHeader  => _isSwapped ? RightHeader : LeftHeader;

    /// <summary>
    /// The header that is currently the <em>compare</em> side, accounting for swaps.
    /// Equals <see cref="RightHeader"/> when not swapped, <see cref="LeftHeader"/> when swapped.
    /// </summary>
    private ModelSystemHeader? EffectiveRightHeader => _isSwapped ? LeftHeader  : RightHeader;

    /// <summary>True when the base side can be navigated (i.e. it was not loaded from file).</summary>
    public bool LeftCanNavigate  => EffectiveLeftHeader  is not null;

    /// <summary>True when the compare side can be navigated (i.e. it was not loaded from file).</summary>
    public bool RightCanNavigate => EffectiveRightHeader is not null;

    /// <summary>
    /// Callback set by the host window to open or focus a model-system editor tab.
    /// Returns the <see cref="ModelSystemEditorViewModel"/> for the opened/focused tab,
    /// or null if the operation failed.
    /// </summary>
    public Func<ModelSystemHeader, Task<ModelSystemEditorViewModel?>>? OpenOrFocusEditorCallback { get; set; }

    // ── Dock integration ─────────────────────────────────────────────────
    /// <summary>Tab title shown in the dock.</summary>
    public string Title => _diff is not null ? $"⟷  {_diff.LeftName} vs {_diff.RightName}" : "⟷  Loading…";
    /// <summary>This tab is always closable.</summary>
    public bool CanClose => true;

    // ── Header display ────────────────────────────────────────────────────
    /// <summary>Name of the left (base) model system.</summary>
    public string LeftName => _diff?.LeftName ?? "…";
    /// <summary>Name of the right (comparison) model system.</summary>
    public string RightName => _diff?.RightName ?? "…";

    /// <summary>True when there are any differences between the two model systems.</summary>
    public bool HasChanges => _diff?.HasChanges ?? false;

    /// <summary>True when the two model systems are identical (no differences). Used for IsVisible bindings that cannot use negation.</summary>
    public bool IsIdentical => _diff is not null && !_diff.HasChanges;

    // ── Loading / error state ─────────────────────────────────────────────
    /// <summary>True while the diff is being computed on the background thread.</summary>
    [ObservableProperty]
    private bool _isLoading = true;

    /// <summary>Set to a non-null string when the diff factory throws. Null on success.</summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>True when <see cref="ErrorMessage"/> is non-null.</summary>
    public bool HasError => ErrorMessage is not null;

    // ── Filter toggle + text filter ────────────────────────────────────────
    /// <summary>
    /// When true, unchanged elements are included in the tree.
    /// Changing this value rebuilds <see cref="RootItems"/>.
    /// </summary>
    [ObservableProperty]
    private bool _showUnchanged = false;

    /// <summary>
    /// Case-insensitive substring filter applied to the diff tree.
    /// Empty or null means no filtering.
    /// Changing this value rebuilds <see cref="RootItems"/>.
    /// </summary>
    [ObservableProperty]
    private string? _filterText;

    // ── Tree ──────────────────────────────────────────────────────────────
    /// <summary>
    /// Root items for the diff tree view.  Always contains a single
    /// <see cref="DiffBoundaryItem"/> wrapping the global boundary diff.
    /// </summary>
    public ObservableCollection<DiffBoundaryItem> RootItems { get; } = [];

    /// <summary>
    /// The task representing the current (or most recent) load operation.
    /// Useful for testing — await this before asserting on properties.
    /// </summary>
    public Task LoadTask { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Initialises the view model with a factory that computes the diff.
    /// The factory receives a Boolean indicating whether the sides are swapped;
    /// it is invoked on a background thread immediately and on every refresh or swap.
    /// </summary>
    public ModelSystemDiffViewModel(Func<bool, ModelSystemDiff> diffFactory,
        ModelSystemHeader? leftHeader = null, ModelSystemHeader? rightHeader = null)
    {
        _diffFactory = diffFactory;
        LeftHeader = leftHeader;
        RightHeader = rightHeader;
        LoadTask = LoadAsync();
    }

    /// <summary>
    /// Test-only constructor: pre-loads a diff synchronously without spawning
    /// a background thread.  Not intended for production use.
    /// </summary>
    internal ModelSystemDiffViewModel(ModelSystemDiff diff)
    {
        _diffFactory = (_) => diff;
        _diff = diff;
        IsLoading = false;
        RebuildTree();
        LoadTask = Task.CompletedTask;
    }

    // ── Commands ──────────────────────────────────────────────────────────

    /// <summary>
    /// Navigates to the element in the base model system.
    /// <paramref name="item"/> should be a <see cref="DiffBoundaryItem"/>,
    /// <see cref="DiffFunctionTemplateItem"/>, or <see cref="DiffElementItem"/>.
    /// </summary>
    [RelayCommand(CanExecute = nameof(LeftCanNavigate))]
    private async Task GoToInBase(object? item)
    {
        if (OpenOrFocusEditorCallback is null) return;
        var header = EffectiveLeftHeader;
        if (header is null) return;
        var id = GetItemId(item, left: true);
        if (id is null) return;
        var editorVm = await OpenOrFocusEditorCallback(header);
        editorVm?.NavigateToElementById(id.Value);
    }

    /// <summary>
    /// Navigates to the element in the compare model system.
    /// <paramref name="item"/> should be a <see cref="DiffBoundaryItem"/>,
    /// <see cref="DiffFunctionTemplateItem"/>, or <see cref="DiffElementItem"/>.
    /// </summary>
    [RelayCommand(CanExecute = nameof(RightCanNavigate))]
    private async Task GoToInCompare(object? item)
    {
        if (OpenOrFocusEditorCallback is null) return;
        var header = EffectiveRightHeader;
        if (header is null) return;
        var id = GetItemId(item, left: false);
        if (id is null) return;
        var editorVm = await OpenOrFocusEditorCallback(header);
        editorVm?.NavigateToElementById(id.Value);
    }

    /// <summary>Extracts the navigation GUID from a diff tree item.</summary>
    private static Guid? GetItemId(object? item, bool left) => item switch
    {
        DiffBoundaryItem b          => left ? b.LeftId : b.RightId,
        DiffFunctionTemplateItem ft => ft.TemplateId,
        DiffElementItem e           => e.ElementId,
        _                           => null
    };

    /// <summary>Re-runs the diff factory and refreshes the tree.</summary>
    [RelayCommand]
    private async Task Refresh()
    {
        LoadTask = LoadAsync();
        await LoadTask;
    }

    /// <summary>Swaps base and comparison sides and reloads the diff.</summary>
    [RelayCommand]
    private async Task Swap()
    {
        _isSwapped = !_isSwapped;
        // CanNavigate properties depend on which side is effective after the swap.
        OnPropertyChanged(nameof(LeftCanNavigate));
        OnPropertyChanged(nameof(RightCanNavigate));
        GoToInBaseCommand.NotifyCanExecuteChanged();
        GoToInCompareCommand.NotifyCanExecuteChanged();
        LoadTask = LoadAsync();
        await LoadTask;
    }

    // ── Internal ──────────────────────────────────────────────────────────
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        OnPropertyChanged(nameof(HasError));
        RootItems.Clear();
        try
        {
            var swapped = _isSwapped;
            _diff = await Task.Run(() => _diffFactory(swapped));
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(LeftName));
            OnPropertyChanged(nameof(RightName));
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(IsIdentical));
            RebuildTree();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            OnPropertyChanged(nameof(HasError));
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnShowUnchangedChanged(bool value)
    {
        if (_diff is not null) RebuildTree();
    }

    partial void OnFilterTextChanged(string? value)
    {
        if (_diff is not null) RebuildTree();
    }

    private void RebuildTree()
    {
        RootItems.Clear();
        if (_diff is not null)
        {
            var filter = string.IsNullOrEmpty(FilterText) ? null : FilterText;
            RootItems.Add(new DiffBoundaryItem(_diff.GlobalBoundary, ShowUnchanged, filter));
        }
    }
}
