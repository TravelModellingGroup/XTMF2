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
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using XTMF2.GUI.Controls;

namespace XTMF2.GUI.Views;

/// <summary>
/// Dialog for adding a <see cref="XTMF2.ModelSystemConstruct.FunctionParameter"/> directly
/// to the current function template.
///
/// The user chooses:
/// <list type="bullet">
///   <item>A parameter <b>name</b>.</item>
///   <item>A <b>type</b> from either the basic presets (String, Int, Float, Bool) or any
///   runtime type via the "Advanced" section.</item>
/// </list>
///
/// In both cases the resulting <see cref="SelectedType"/> is
/// <c>IFunction&lt;X&gt;</c> — nodes inside the template that link to this parameter
/// must satisfy that interface contract.
/// </summary>
public partial class FunctionParameterPickerDialog : Window, INotifyPropertyChanged
{
    // ── Generic IFunction<> definition ────────────────────────────────────
    private static readonly Type s_ifunctionOf1 = typeof(IFunction<>);

    // ── All available runtime types (for the advanced sub-picker) ─────────
    private readonly ReadOnlyObservableCollection<Type>? _allAvailableTypes;

    // ── Selection state ───────────────────────────────────────────────────
    // 0 = none, 1 = string, 2 = int, 3 = float, 4 = bool, 5 = advanced
    private int _selection = 0;
    private Type? _advancedSelectedType;
    private string _parameterName = string.Empty;

    public new event PropertyChangedEventHandler? PropertyChanged;

    // ─────────────────────────────────────────────────────────────────────────────
    //  Public properties
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>The name the user entered for the new function parameter.</summary>
    public string ParameterName
    {
        get => _parameterName;
        set
        {
            _parameterName = value;
            Notify(nameof(ParameterName));
            Notify(nameof(CanOK));
        }
    }

    // ── Radio-button bindings ─────────────────────────────────────────────

    public bool IsBasicString
    {
        get => _selection == 1;
        set { if (value) SetSelection(1); }
    }

    public bool IsBasicInt
    {
        get => _selection == 2;
        set { if (value) SetSelection(2); }
    }

    public bool IsBasicFloat
    {
        get => _selection == 3;
        set { if (value) SetSelection(3); }
    }

    public bool IsBasicBool
    {
        get => _selection == 4;
        set { if (value) SetSelection(4); }
    }

    public bool IsAdvanced
    {
        get => _selection == 5;
        set { if (value) SetSelection(5); }
    }

    private void SetSelection(int id)
    {
        _selection = id;
        Notify(nameof(IsBasicString));
        Notify(nameof(IsBasicInt));
        Notify(nameof(IsBasicFloat));
        Notify(nameof(IsBasicBool));
        Notify(nameof(IsAdvanced));
        Notify(nameof(AdvancedSectionVisible));
        Notify(nameof(CanOK));
        Notify(nameof(ResolvedTypeName));
    }

    /// <summary>Whether the advanced type-picker section should be shown.</summary>
    public bool AdvancedSectionVisible => _selection == 5;

    // ── Advanced type slot ────────────────────────────────────────────────

    /// <summary>The type the user chose in the advanced section, or <c>null</c>.</summary>
    public Type? AdvancedSelectedType
    {
        get => _advancedSelectedType;
        private set
        {
            _advancedSelectedType = value;
            Notify(nameof(AdvancedSelectedType));
            Notify(nameof(AdvancedTypeName));
            Notify(nameof(CanOK));
            Notify(nameof(ResolvedTypeName));
        }
    }

    /// <summary>Friendly name of the advanced type, or "Not chosen" when unset.</summary>
    public string AdvancedTypeName =>
        _advancedSelectedType is null
            ? "Not chosen"
            : FriendlyTypeNameConverter.GetFriendlyName(_advancedSelectedType);

    // ── Resolved type ─────────────────────────────────────────────────────

    /// <summary>
    /// The fully-constructed <c>IFunction&lt;X&gt;</c> type that will become the
    /// <see cref="XTMF2.ModelSystemConstruct.FunctionParameter"/>'s type.
    /// <c>null</c> when no valid selection has been made yet.
    /// </summary>
    public Type? SelectedType
    {
        get
        {
            var inner = InnerType;
            if (inner is null) return null;
            try { return s_ifunctionOf1.MakeGenericType(inner); }
            catch { return null; }
        }
    }

    /// <summary>Human-readable preview, e.g. "IFunction&lt;String&gt;".</summary>
    public string ResolvedTypeName
    {
        get
        {
            var t = SelectedType;
            return t is null ? string.Empty : FriendlyTypeNameConverter.GetFriendlyName(t);
        }
    }

    private Type? InnerType => _selection switch
    {
        1 => typeof(string),
        2 => typeof(int),
        3 => typeof(float),
        4 => typeof(bool),
        5 => _advancedSelectedType,
        _ => null
    };

    // ── Dialog outcome ────────────────────────────────────────────────────

    /// <summary>True when the dialog can be confirmed.</summary>
    public bool CanOK =>
        !string.IsNullOrWhiteSpace(_parameterName) && SelectedType is not null;

    /// <summary>True when the user dismissed the dialog without confirming.</summary>
    public bool WasCancelled { get; private set; } = true;

    // ─────────────────────────────────────────────────────────────────────────────
    //  Constructors
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Design-time constructor required by the Avalonia XAML compiler.</summary>
    public FunctionParameterPickerDialog()
        : this(defaultName: string.Empty, allAvailableTypes: null)
    {
    }

    /// <summary>
    /// Creates the dialog.
    /// </summary>
    /// <param name="defaultName">Pre-filled name for the parameter.</param>
    /// <param name="allAvailableTypes">
    /// Full pool of runtime types (including non-IModule) used by the advanced sub-picker.
    /// When <c>null</c> the advanced section still works but will show an empty list.
    /// </param>
    public FunctionParameterPickerDialog(
        string defaultName,
        ReadOnlyObservableCollection<Type>? allAvailableTypes)
    {
        _parameterName = defaultName;
        _allAvailableTypes = allAvailableTypes;

        InitializeComponent();
        DataContext = this;

        AddHandler(KeyDownEvent, (_, ke) =>
        {
            if (ke.Key != Key.Escape) return;
            Cancel_Click(null, new RoutedEventArgs());
            ke.Handled = true;
        }, RoutingStrategies.Tunnel);

        Opened += (_, _) => NameBox.Focus();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  Advanced type-picker
    // ─────────────────────────────────────────────────────────────────────────────

    private async void PickAdvancedType_Click(object? sender, RoutedEventArgs e)
    {
        var pool = _allAvailableTypes
            ?? new ReadOnlyObservableCollection<Type>(new ObservableCollection<Type>());

        var picker = new TypePickerDialog(
            pool,
            prompt: "Select the context type for IFunction<…>:",
            initialType: _advancedSelectedType);
        await picker.ShowDialog(this);

        if (!picker.WasCancelled && picker.SelectedType is not null)
            AdvancedSelectedType = picker.SelectedType;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  Button handlers
    // ─────────────────────────────────────────────────────────────────────────────

    private void OK_Click(object? sender, RoutedEventArgs e)
    {
        if (!CanOK) return;
        WasCancelled = false;
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        WasCancelled = true;
        Close();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private void Notify(string prop) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}
