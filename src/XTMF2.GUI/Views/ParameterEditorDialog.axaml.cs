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
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using System.ComponentModel;
using System.Linq;

namespace XTMF2.GUI.Views;

/// <summary>
/// Dialog for editing a BasicParameter or ScriptedParameter node.
/// Allows setting the value string and switching between Basic and Scripted modes.
/// </summary>
public partial class ParameterEditorDialog : Window, INotifyPropertyChanged
{
    // ── Validators passed in by the caller ───────────────────────────────────
    // Return null for valid, error message string for invalid.
    private readonly Func<string, string?> _basicValidator;
    private readonly Func<string, string?> _scriptedValidator;

    // ── Backing fields ────────────────────────────────────────────────────────
    private string   _typeLabel        = string.Empty;
    private bool     _isBasicMode      = true;
    private string   _valueText        = string.Empty;
    private string   _errorMessage     = string.Empty;

    // ── Enum support ──────────────────────────────────────────────────────────
    private readonly bool     _isEnumType;
    private readonly string[] _enumValues = Array.Empty<string>();
    private string?  _selectedEnumValue;

    public new event PropertyChangedEventHandler? PropertyChanged;

    // ── Bindable properties ───────────────────────────────────────────────────

    /// <summary>Friendly display of the inner parameter type, e.g. "Parameter type: Float".</summary>
    public string TypeLabel
    {
        get => _typeLabel;
        set { _typeLabel = value; RaisePropertyChanged(nameof(TypeLabel)); }
    }

    /// <summary>True when "Value (Basic Parameter)" radio is selected.</summary>
    public bool IsBasicMode
    {
        get => _isBasicMode;
        set
        {
            if (_isBasicMode == value) return;
            _isBasicMode = value;
            RaisePropertyChanged(nameof(IsBasicMode));
            RaisePropertyChanged(nameof(IsScriptedMode));
            RaisePropertyChanged(nameof(ShowTextBox));
            RaisePropertyChanged(nameof(ShowEnumPicker));
            ErrorMessage = string.Empty;
        }
    }

    /// <summary>True when "Expression (Scripted Parameter)" radio is selected.</summary>
    public bool IsScriptedMode
    {
        get => !_isBasicMode;
        set
        {
            // value==true means "switch to scripted" → _isBasicMode should become false;
            // guard: skip if already in the requested state.
            if (_isBasicMode == !value) return;
            _isBasicMode = !value;
            RaisePropertyChanged(nameof(IsBasicMode));
            RaisePropertyChanged(nameof(IsScriptedMode));
            RaisePropertyChanged(nameof(ShowTextBox));
            RaisePropertyChanged(nameof(ShowEnumPicker));
            ErrorMessage = string.Empty;
        }
    }

    // ── Enum properties ───────────────────────────────────────────────────────

    /// <summary>True when the parameter's inner type is an enumeration.</summary>
    public bool IsEnumType => _isEnumType;

    /// <summary>The names of all enum members, used to populate the ComboBox.</summary>
    public string[] EnumValues => _enumValues;

    /// <summary>The currently selected enum member name.</summary>
    public string? SelectedEnumValue
    {
        get => _selectedEnumValue;
        set
        {
            _selectedEnumValue = value;
            RaisePropertyChanged(nameof(SelectedEnumValue));
        }
    }

    /// <summary>True when the free-text box should be visible.</summary>
    public bool ShowTextBox   => !_isEnumType || !_isBasicMode;

    /// <summary>True when the enum ComboBox should be visible.</summary>
    public bool ShowEnumPicker => _isEnumType && _isBasicMode;

    /// <summary>The value / expression text currently in the text box.</summary>
    public string ValueText
    {
        get => _valueText;
        set { _valueText = value; RaisePropertyChanged(nameof(ValueText)); }
    }

    /// <summary>Error message shown below the text box. Empty string = no error.</summary>
    public string ErrorMessage
    {
        get => _errorMessage;
        set
        {
            _errorMessage = value;
            RaisePropertyChanged(nameof(ErrorMessage));
            RaisePropertyChanged(nameof(HasError));
        }
    }

    /// <summary>True when there is an active inline error to display.</summary>
    public bool HasError => !string.IsNullOrEmpty(_errorMessage);

    // ── Output ────────────────────────────────────────────────────────────────

    /// <summary>True if the user cancelled or closed the dialog without clicking OK.</summary>
    public bool WasCancelled { get; private set; } = true;

    /// <summary>The validated value / expression text committed by the user.</summary>
    public string ResultValue { get; private set; } = string.Empty;

    /// <summary>True if the user chose "Expression (Scripted Parameter)" mode.</summary>
    public bool ResultIsScripted { get; private set; }

    // ── Construction ──────────────────────────────────────────────────────────

    /// <summary>Parameterless constructor required by the Avalonia XAML compiler.</summary>
    public ParameterEditorDialog()
    {
        _basicValidator    = _ => null;
        _scriptedValidator = _ => null;
        InitializeComponent();
        DataContext = this;
        RegisterEscapeClose();
    }

    /// <summary>
    /// Opens a parameter editor for the given node.
    /// </summary>
    /// <param name="innerTypeName">Friendly name of the inner type (e.g. "Float").</param>
    /// <param name="currentValue">Current parameter value string.</param>
    /// <param name="isCurrentlyScripted">Whether the node is currently a ScriptedParameter.</param>
    /// <param name="basicValidator">Delegate that validates a basic value; returns null on success or an error message.</param>
    /// <param name="scriptedValidator">Delegate that validates a scripted expression; returns null on success or an error message.</param>
    public ParameterEditorDialog(
        string           innerTypeName,
        string           currentValue,
        bool             isCurrentlyScripted,
        Func<string, string?> basicValidator,
        Func<string, string?> scriptedValidator,
        Type?            innerType = null)
    {
        _basicValidator    = basicValidator;
        _scriptedValidator = scriptedValidator;

        // Set the mode BEFORE InitializeComponent so bindings see the final state.
        _isBasicMode = !isCurrentlyScripted;

        // Detect enumeration types.
        if (innerType is not null && innerType.IsEnum)
        {
            _isEnumType  = true;
            _enumValues  = Enum.GetNames(innerType);
            // Pre-select the current value if it is a valid enum name.
            _selectedEnumValue = _enumValues.Contains(currentValue, StringComparer.OrdinalIgnoreCase)
                ? currentValue
                : (_enumValues.Length > 0 ? _enumValues[0] : null);
        }

        // Initialise caption / text box text before DataContext so the
        // one-time reads in InitializeComponent see correct values.
        _typeLabel  = $"Parameter type: {innerTypeName}";
        _valueText  = currentValue;

        InitializeComponent();
        DataContext = this;
        RegisterEscapeClose();

        Opened += (_, _) =>
        {
            if (_isEnumType && _isBasicMode)
                EnumComboBox.Focus();
            else
                ValueTextBox.Focus();
        };
    }

    private void RegisterEscapeClose() =>
        AddHandler(KeyDownEvent, (_, ke) =>
        {
            if (ke.Key != Key.Escape) return;
            Cancel_Click(null, new RoutedEventArgs());
            ke.Handled = true;
        }, RoutingStrategies.Tunnel);

    // ── Button handlers ───────────────────────────────────────────────────────

    private void OK_Click(object? sender, RoutedEventArgs e)
    {
        string text;
        if (_isEnumType && _isBasicMode)
        {
            // Use the selected enum value; it is always valid by construction.
            text = _selectedEnumValue ?? string.Empty;
            if (string.IsNullOrEmpty(text))
            {
                ErrorMessage = "Please select a value.";
                return;
            }
        }
        else
        {
            text = ValueText ?? string.Empty;
        }

        var validator = _isBasicMode ? _basicValidator : _scriptedValidator;
        var errorMsg  = validator(text);

        if (errorMsg is not null)
        {
            ErrorMessage = errorMsg;
            return;
        }

        WasCancelled    = false;
        ResultValue     = text;
        ResultIsScripted = !_isBasicMode;
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RaisePropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
