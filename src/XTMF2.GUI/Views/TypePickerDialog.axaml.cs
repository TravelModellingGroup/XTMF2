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
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using XTMF2.GUI.Controls;

namespace XTMF2.GUI.Views;

/// <summary>
/// A searchable type-picker dialog that lets the user choose a module type
/// from the runtime's <see cref="XTMF2.Repository.ModuleRepository.LoadedModuleTypes"/> list.
/// </summary>
public partial class TypePickerDialog : Window, INotifyPropertyChanged
{
    private readonly ReadOnlyObservableCollection<Type> _allTypes;
    private Type? _initialType;

    private string _filterText = string.Empty;
    private ObservableCollection<Type> _filteredTypes = new();
    private string _prompt = "Select a module type:";

    public new event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Prompt text shown at the top of the dialog.</summary>
    public string Prompt
    {
        get => _prompt;
        set
        {
            _prompt = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Prompt)));
        }
    }

    /// <summary>Text in the search / filter box.</summary>
    public string FilterText
    {
        get => _filterText;
        set
        {
            _filterText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FilterText)));
            UpdateFilter();
        }
    }

    /// <summary>The subset of types matching <see cref="FilterText"/>.</summary>
    public ObservableCollection<Type> FilteredTypes
    {
        get => _filteredTypes;
        private set
        {
            _filteredTypes = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FilteredTypes)));
        }
    }

    /// <summary>The type chosen by the user, or <c>null</c> if cancelled.</summary>
    public Type? SelectedType { get; private set; }

    /// <summary>True if the user dismissed the dialog without choosing a type.</summary>
    public bool WasCancelled { get; private set; } = true;

    /// <summary>
    /// True when the current filter string produces no matching types.
    /// Bound to the empty-state message in the view.
    /// </summary>
    public bool HasNoResults => _filteredTypes.Count == 0;

    /// <summary>
    /// Design-time / default constructor required by the Avalonia XAML compiler.
    /// Use the overload that accepts a module-type collection at runtime.
    /// </summary>
    public TypePickerDialog()
        : this(new System.Collections.ObjectModel.ReadOnlyObservableCollection<Type>(
               new System.Collections.ObjectModel.ObservableCollection<Type>()))
    {
    }

    /// <summary>
    /// Creates the dialog pre-populated with all types in <paramref name="moduleTypes"/>.
    /// </summary>
    /// <param name="moduleTypes">The full list of available types.</param>
    /// <param name="prompt">Optional prompt text shown at the top of the dialog.</param>
    /// <param name="initialType">If provided, this type will be pre-selected when the dialog opens.</param>
    public TypePickerDialog(ReadOnlyObservableCollection<Type> moduleTypes, string? prompt = null, Type? initialType = null)
    {
        _allTypes = moduleTypes;
        _initialType = initialType;
        if (prompt is not null) _prompt = prompt;
        InitializeComponent();
        DataContext = this;
        UpdateFilter();
        // Focus the filter box and honour an initial selection once the window is shown.
        Opened += (_, _) =>
        {
            FilterBox.FocusSearchBox();
            if (_initialType is not null)
            {
                var idx = FilteredTypes.IndexOf(_initialType);
                if (idx >= 0)
                {
                    TypeListBox.SelectedIndex = idx;
                    TypeListBox.ScrollIntoView(TypeListBox.SelectedItem!);
                }
            }
        };
    }

    // ── Filtering ─────────────────────────────────────────────────────────

    private void UpdateFilter()
    {
        var filter = _filterText.Trim();
        var source = string.IsNullOrEmpty(filter)
            ? (System.Collections.Generic.IEnumerable<Type>)_allTypes
            : _allTypes.Where(t =>
                (t?.Name     ?? string.Empty).Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                (t?.FullName ?? string.Empty).Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                FriendlyTypeNameConverter.GetFriendlyName(t!).Contains(filter, StringComparison.OrdinalIgnoreCase));

        FilteredTypes = new ObservableCollection<Type>(source);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNoResults)));

        // Keep the list box in sync — auto-select first match.
        if (FilteredTypes.Count > 0)
            TypeListBox.SelectedIndex = 0;
    }

    // ── Button handlers ───────────────────────────────────────────────────

    private void OK_Click(object? sender, RoutedEventArgs e)
    {
        SelectedType = TypeListBox.SelectedItem as Type;
        WasCancelled = SelectedType is null;
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        WasCancelled = true;
        Close();
    }
}
