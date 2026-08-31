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
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using XTMF2.GUI.Controls;

namespace XTMF2.GUI.Views;

/// <summary>
/// A searchable type-picker dialog that lets the user choose a module type
/// from the runtime's <see cref="XTMF2.Repository.ModuleRepository.LoadedModuleTypes"/> list.
/// When the user selects an open-generic type (e.g. one implementing <c>IAction&lt;Context&gt;</c>,
/// <c>IFunction&lt;Result&gt;</c>, <c>IFunction&lt;Context, ReturnType&gt;</c>, or any other
/// open-generic <c>IModule</c>), an additional panel appears so the user can
/// specify each generic type argument.
/// </summary>
public partial class TypePickerDialog : Window, INotifyPropertyChanged
{
    // (No longer needed: we now admit all open-generic IModule types, not just
    //  IAction<> / IFunction<,> implementors.)

    // ── Data sources ──────────────────────────────────────────────────────────────
    private readonly ReadOnlyObservableCollection<Type> _allTypes;
    private readonly ReadOnlyObservableCollection<Type>? _allAvailableTypes; // all runtime types (incl. non-IModule)
    private readonly List<Type> _combinedTypes;   // closed + qualifying open generics
    private Type? _initialType;

    // ── Filter state ─────────────────────────────────────────────────────────────
    private string _filterText = string.Empty;
    private ObservableCollection<Type> _filteredTypes = new();
    private string _prompt = "Select a module type:";

    // ── Type-arg panel state ─────────────────────────────────────────────────────
    private ObservableCollection<TypeArgEntry> _typeArgEntries = new();

    public new event PropertyChangedEventHandler? PropertyChanged;

    // ─────────────────────────────────────────────────────────────────────────────
    //  Nested helper: one row in the type-argument panel
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Represents a single generic type-argument slot that the user must fill in.
    /// </summary>
    public sealed class TypeArgEntry : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Human-readable parameter name shown as the row label (e.g. "Context:").</summary>
        public string Label { get; init; } = string.Empty;

        private Type? _selectedType;

        /// <summary>The type the user has chosen for this slot, or <c>null</c> if not yet chosen.</summary>
        public Type? SelectedType
        {
            get => _selectedType;
            set
            {
                _selectedType = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedType)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
            }
        }

        /// <summary>Friendly name of the chosen type, or "Not chosen" when still unset.</summary>
        public string DisplayName =>
            _selectedType is null
                ? "Not chosen"
                : FriendlyTypeNameConverter.GetFriendlyName(_selectedType);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  Public surface
    // ─────────────────────────────────────────────────────────────────────────────

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

    /// <summary>
    /// The collection of type-argument slots that must be filled when the user selects an
    /// open-generic type.  One slot per generic type parameter.
    /// Empty when a closed type is selected.
    /// </summary>
    public ObservableCollection<TypeArgEntry> TypeArgEntries
    {
        get => _typeArgEntries;
        private set
        {
            _typeArgEntries = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TypeArgEntries)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsShowingTypeArgPanel)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasTypeArg0)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasTypeArg1)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TypeArg0)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TypeArg1)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanOK)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ResolvedTypePreview)));
        }
    }

    /// <summary>True when the type-argument panel should be shown.</summary>
    public bool IsShowingTypeArgPanel => _typeArgEntries.Count > 0;

    // Sentinel entry returned when a slot is unused (avoids nullable binding issues).
    private static readonly TypeArgEntry s_emptyEntry = new() { Label = string.Empty };

    // Convenience access for AXAML binding (avoids complex ItemsControl templates).
    public TypeArgEntry TypeArg0 => _typeArgEntries.Count > 0 ? _typeArgEntries[0] : s_emptyEntry;
    public TypeArgEntry TypeArg1 => _typeArgEntries.Count > 1 ? _typeArgEntries[1] : s_emptyEntry;
    public bool HasTypeArg0 => _typeArgEntries.Count > 0;
    public bool HasTypeArg1 => _typeArgEntries.Count > 1;

    /// <summary>
    /// True when the dialog can be confirmed: there are results, and — if an open-generic type
    /// is selected — all type-argument slots have been filled.
    /// </summary>
    public bool CanOK =>
        !HasNoResults &&
        (!IsShowingTypeArgPanel || _typeArgEntries.All(e => e.SelectedType is not null));

    /// <summary>
    /// A preview of the fully-constructed type name shown below the type-arg panel,
    /// updated as the user fills in arguments.
    /// </summary>
    public string ResolvedTypePreview
    {
        get
        {
            if (!IsShowingTypeArgPanel) return string.Empty;
            var selected = TypeListBox?.SelectedItem as Type;
            if (selected is null || !selected.IsGenericTypeDefinition) return string.Empty;
            if (_typeArgEntries.Any(e => e.SelectedType is null)) return string.Empty;
            try
            {
                var closed = selected.MakeGenericType(_typeArgEntries.Select(e => e.SelectedType!).ToArray());
                return $"Will create: {FriendlyTypeNameConverter.GetFriendlyName(closed)}";
            }
            catch { return string.Empty; }
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

    // ─────────────────────────────────────────────────────────────────────────────
    //  Constructors
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Design-time / default constructor required by the Avalonia XAML compiler.
    /// Use the overload that accepts a module-type collection at runtime.
    /// </summary>
    public TypePickerDialog()
        : this(new ReadOnlyObservableCollection<Type>(new ObservableCollection<Type>()))
    {
    }

    /// <summary>
    /// Creates the dialog pre-populated with all types in <paramref name="moduleTypes"/>
    /// and, optionally, open-generic types that qualify for the context-type UX.
    /// </summary>
    /// <param name="moduleTypes">The full list of closed module types.</param>
    /// <param name="prompt">Optional prompt text shown at the top of the dialog.</param>
    /// <param name="initialType">If provided, this type will be pre-selected when the dialog opens.</param>
    /// <param name="openGenericModuleTypes">
    /// Optional list of open-generic module types (e.g. any open-generic <c>IModule</c>
    /// implementor such as <c>IgnoreResult&lt;&gt;</c> or <c>BasicParameter&lt;&gt;</c>).
    /// All open-generic types in this list are added to the picker; when the user
    /// selects one, a type-argument panel appears for each generic parameter.
    /// </param>
    public TypePickerDialog(
        ReadOnlyObservableCollection<Type> moduleTypes,
        string? prompt = null,
        Type? initialType = null,
        IReadOnlyList<Type>? openGenericModuleTypes = null,
        ReadOnlyObservableCollection<Type>? allAvailableTypes = null)
    {
        _allTypes = moduleTypes;
        _allAvailableTypes = allAvailableTypes;
        _initialType = initialType;
        if (prompt is not null) _prompt = prompt;

        // Build the combined list: closed types first, then all open-generic module types.
        _combinedTypes = new List<Type>(moduleTypes);
        if (openGenericModuleTypes is not null)
        {
            foreach (var og in openGenericModuleTypes)
            {
                if (og.IsGenericTypeDefinition)
                    _combinedTypes.Add(og);
            }
        }

        InitializeComponent();
        DataContext = this;
        AddHandler(KeyDownEvent, (_, ke) =>
        {
            if (ke.Key != Key.Escape) return;
            Cancel_Click(null, new RoutedEventArgs());
            ke.Handled = true;
        }, RoutingStrategies.Tunnel);
        UpdateFilter();

        // Focus the filter box and honour an initial selection once the list is loaded.
        Opened += (_, _) =>
        {
            FilterBox.FocusSearchBox();
        };

        TypeListBox.Loaded += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_initialType is not null)
                {
                    var idx = FilteredTypes.IndexOf(_initialType);
                    if (idx >= 0)
                    {
                        TypeListBox.SelectedIndex = idx;
                        TypeListBox.ScrollIntoView(TypeListBox.SelectedItem!);
                    }
                }

                // Ensure the OK binding reflects the realized ListBox selection.
                OnListSelectionChanged();
            });
        };

        TypeListBox.DoubleTapped += (_, _) =>
        {
            if (TypeListBox.SelectedItem is Type) OK_Click(null, null!);
        };

        TypeListBox.SelectionChanged += (_, _) => OnListSelectionChanged();
    }

    // (IsContextBasedOpenGeneric removed — all open-generic IModule types are now
    //  admitted directly in the constructor via og.IsGenericTypeDefinition.)

    // ─────────────────────────────────────────────────────────────────────────────
    //  Type-argument panel logic
    // ─────────────────────────────────────────────────────────────────────────────

    private void OnListSelectionChanged()
    {
        if (TypeListBox.SelectedItem is not Type selected || !selected.IsGenericTypeDefinition)
        {
            // Closed type selected – clear the type-arg panel.
            TypeArgEntries = new ObservableCollection<TypeArgEntry>();
            return;
        }

        // Build one entry per generic parameter.
        var entries = new ObservableCollection<TypeArgEntry>();
        foreach (var param in selected.GetGenericArguments())
        {
            var entry = new TypeArgEntry { Label = $"{param.Name}:" };
            entry.PropertyChanged += (_, _) =>
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanOK)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ResolvedTypePreview)));
            };
            entries.Add(entry);
        }
        TypeArgEntries = entries;
    }

    /// <summary>
    /// Called by the "Pick…" button for each type-argument slot.
    /// Opens a nested <see cref="TypePickerDialog"/> (using only closed module types)
    /// and stores the result in the appropriate slot.
    /// </summary>
    public async Task PickTypeArgAsync(int index)
    {
        if (index < 0 || index >= _typeArgEntries.Count) return;

        // Use all available runtime types (including non-IModule) so the user
        // can select e.g. a plain data class as the context type.
        var typePool = _allAvailableTypes ?? _allTypes;
        var picker = new TypePickerDialog(
            typePool,
            prompt: $"Select type for '{_typeArgEntries[index].Label.TrimEnd(':')}':",
            initialType: _typeArgEntries[index].SelectedType);
        await picker.ShowDialog(this);

        if (!picker.WasCancelled && picker.SelectedType is not null)
            _typeArgEntries[index].SelectedType = picker.SelectedType;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  Filtering
    // ─────────────────────────────────────────────────────────────────────────────

    private void UpdateFilter()
    {
        var filter = _filterText.Trim();
        var source = string.IsNullOrEmpty(filter)
            ? (IEnumerable<Type>)_combinedTypes
            : _combinedTypes.Where(t =>
                (t?.Name     ?? string.Empty).Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                (t?.FullName ?? string.Empty).Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                FriendlyTypeNameConverter.GetFriendlyName(t!).Contains(filter, StringComparison.OrdinalIgnoreCase));

        FilteredTypes = new ObservableCollection<Type>(source);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNoResults)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanOK)));

        // Keep the list box in sync — auto-select first match.
        if (FilteredTypes.Count > 0)
            TypeListBox.SelectedIndex = 0;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  Button handlers
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Pick button for the first type-argument slot (always the Context type).</summary>
    private async void PickTypeArg0_Click(object? sender, RoutedEventArgs e) => await PickTypeArgAsync(0);

    /// <summary>Pick button for the second type-argument slot (the ReturnType for IFunction).</summary>
    private async void PickTypeArg1_Click(object? sender, RoutedEventArgs e) => await PickTypeArgAsync(1);

    private void FilterBox_EnterPressed(object? sender, RoutedEventArgs e)
    {
        if (FilteredTypes.Count > 0)
        {
            TypeListBox.SelectedIndex = 0;
        }

        OK_Click(null, e);
    }

    private void OK_Click(object? sender, RoutedEventArgs e)
    {
        if (!CanOK) return;

        var rawSelected = TypeListBox.SelectedItem as Type;
        if (rawSelected is null) { WasCancelled = true; Close(); return; }

        if (rawSelected.IsGenericTypeDefinition)
        {
            // Construct the closed generic from the user-supplied type arguments.
            try
            {
                SelectedType = rawSelected.MakeGenericType(
                    _typeArgEntries.Select(arg => arg.SelectedType!).ToArray());
            }
            catch
            {
                // Construction failed (e.g. constraint violation) – treat as cancel.
                WasCancelled = true;
                Close();
                return;
            }
        }
        else
        {
            SelectedType = rawSelected;
        }

        WasCancelled = false;
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        WasCancelled = true;
        Close();
    }
}
