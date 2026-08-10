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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.VisualTree;
using XTMF2.GUI.ViewModels;

namespace XTMF2.GUI.Controls;

partial class ModelSystemCanvas
{
    private void UnsubscribeInlineEditorScroll()
    {
        if (_inlineEditorSv is not null && _inlineEditorSvHandler is not null)
            _inlineEditorSv.PropertyChanged -= _inlineEditorSvHandler;
        _inlineEditorSv = null;
        _inlineEditorSvHandler = null;
        _scriptOverlay.HorizontalScrollOffset = 0;
    }

    /// <summary>
    /// Returns the effective enum <see cref="Type"/> for a parameter node, or <c>null</c> if the
    /// node's inner type is not (or does not implement) an enum-returning function.
    /// </summary>
    private static Type? GetEffectiveEnumType(NodeViewModel node)
    {
        var nodeType = node.UnderlyingNode.Type;
        if (nodeType is null || !nodeType.IsGenericType) return null;
        var innerType = nodeType.GetGenericArguments().FirstOrDefault();
        if (innerType is null) return null;
        if (innerType.IsEnum) return innerType;
        // Walk interfaces: look for IFunction<EnumT>.
        var candidates = innerType.IsInterface
            ? new[] { innerType }.Concat(innerType.GetInterfaces())
            : (IEnumerable<Type>)innerType.GetInterfaces();
        foreach (var iface in candidates)
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IFunction<>))
            {
                var arg = iface.GetGenericArguments()[0];
                if (arg.IsEnum) return arg;
            }
        }
        return null;
    }

    private void BeginParamEdit(NodeViewModel node, double rowX = -1, double rowY = -1, double rowW = -1, ICanvasElement? parentElement = null, object? hook = null)
    {
        HideVarDropdown();
        _editingParamNode = node;
        _editingParamEditorX = rowX >= 0 ? rowX : node.X;
        _editingParamEditorY = rowY >= 0 ? rowY : node.Y + NodeHeaderHeight;
        // Width must be positive for ArrangeOverride to position the inline editor.
        _editingParamEditorW = rowW > 0 ? rowW : NodeRenderWidth(node);
        _editingParamParentElement = parentElement;
        _editingParamHook = hook;

        var enumType = GetEffectiveEnumType(node);
        _editingParamIsEnum = enumType is not null && !node.IsScriptedParameter;

        if (_editingParamIsEnum)
        {
            // Populate the ComboBox with enum member names and pre-select the current value.
            // Guard with _enumEditorLoading so SelectionChanged / DropDownClosed don't fire
            // prematurely while we're setting ItemsSource and SelectedItem.
            _enumEditorLoading = true;
            var names = Enum.GetNames(enumType!);
            _inlineEnumEditor.ItemsSource = names;
            var current = node.ParameterValueRepresentation;
            _inlineEnumEditor.SelectedItem = names.Contains(current) ? current
                : (names.Length > 0 ? names[0] : null);
            _enumEditorLoading = false;

            _inlineEditor.IsVisible = false;
            _inlineEnumEditor.IsVisible = true;
            InvalidateMeasure();
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _inlineEnumEditor.Focus();
                _inlineEnumEditor.IsDropDownOpen = true;
            }, Avalonia.Threading.DispatcherPriority.Render);
            return;
        }

        _inlineEnumEditor.IsVisible = false;
        _inlineEditor.Text = node.ParameterValueRepresentation;

            bool isLight = Application.Current?.ActualThemeVariant == ThemeVariant.Light;

        // For scripted parameters the text is rendered by Render() with syntax colours;
        // make the TextBox itself transparent so the coloured tokens show through.
        if (node.IsScriptedParameter)
        {
            _inlineEditor.Foreground = Brushes.Transparent;
                // Set background to light grey for light mode, dark blue for dark mode
                _inlineEditor.Background = isLight
                    ? new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8))
                    : new SolidColorBrush(Color.FromRgb(0x18, 0x28, 0x38));
                    // Use a dark caret in light theme and bright yellow in dark theme.
                    _inlineEditor.CaretBrush = isLight
                        ? new SolidColorBrush(Color.FromRgb(0x1C, 0x24, 0x33))
                        : new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0x00));
            _scriptTokens = TokenizeScript(node.ParameterValueRepresentation);
            _scriptOverlay.Tokens = _scriptTokens;
            _scriptOverlay.IsVisible = true;

            // Subscribe to the TextBox's internal ScrollViewer so the overlay shifts
            // horizontally in lockstep with the TextBox after every caret move or
            // text change (the scroll happens during layout, after TextChanged fires).
            UnsubscribeInlineEditorScroll();
            // The internal SV may not exist until after the first layout pass, so
            // we post the subscription to run once the visual tree is populated.
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var sv = _inlineEditor.GetVisualDescendants()
                                      .OfType<ScrollViewer>()
                                      .FirstOrDefault();
                if (sv is not null)
                {
                    _inlineEditorSvHandler = (_, args) =>
                    {
                        if (args.Property == ScrollViewer.OffsetProperty)
                        {
                            _scriptOverlay.HorizontalScrollOffset = sv.Offset.X;
                            _scriptOverlay.InvalidateVisual();
                        }
                    };
                    _inlineEditorSv = sv;
                    sv.PropertyChanged += _inlineEditorSvHandler;
                }
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
        else
        {
            _inlineEditor.Foreground = isLight ? ParamValueTextBrushL : ParamValueTextBrush;
            // Set background to light grey for light mode, dark blue for dark mode
            _inlineEditor.Background = isLight
                ? new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8))
                : new SolidColorBrush(Color.FromRgb(0x18, 0x28, 0x38));
            // Use bright yellow caret for both themes for maximum visibility
                // Use a dark caret in light theme and bright yellow in dark theme.
                _inlineEditor.CaretBrush = isLight
                    ? new SolidColorBrush(Color.FromRgb(0x1C, 0x24, 0x33))
                    : new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0x00));
            _scriptTokens = Array.Empty<(string, IBrush)>();
            _scriptOverlay.Tokens = _scriptTokens;
            _scriptOverlay.IsVisible = false;
        }

        _inlineEditor.IsVisible = true;
        // Re-layout so ArrangeOverride positions the TextBox at the right row.
        InvalidateMeasure();
        // Focus + select-all after the layout pass completes.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _inlineEditor.Focus();
            _inlineEditor.SelectAll();
        }, Avalonia.Threading.DispatcherPriority.Render);
    }

    /// <summary>Commits the current editor text as the new parameter value.</summary>
    /// <remarks>
    /// For ScriptedParameter nodes the value is validated before the editor is closed.
    /// If the save fails the editor remains open so the user can correct the expression,
    /// and an error toast is shown instead.
    /// </remarks>
    private void CommitParamEdit()
    {
        if (_commitParamEditInProgress) return;
        
        _commitParamEditInProgress = true;
        try
        {
            HideVarDropdown();
            if (_editingParamNode is null) return;
            var node = _editingParamNode;
            // Clear tracking fields after capturing node
            _editingParamParentElement = null;
            _editingParamHook = null;
            var value = _editingParamIsEnum
                ? (_inlineEnumEditor.SelectedItem as string ?? string.Empty)
                : (_inlineEditor.Text ?? string.Empty);

            // Attempt to save. For ScriptedParameter this validates the expression first.
            if (!node.SetParameterValue(value, out var error))
            {
                // Save failed – keep the editor open, restore focus, show the error.
                _vm?.ShowToast(error?.Message ?? "Failed to set parameter value.",
                               isError: true, durationMs: 5000);
                Avalonia.Threading.Dispatcher.UIThread.Post(
                    () => _inlineEditor.Focus(),
                    Avalonia.Threading.DispatcherPriority.Input);
                return;
            }

            // Save succeeded – close the editor.
            UnsubscribeInlineEditorScroll();
            _editingParamNode   = null;
            _editingParamIsEnum = false;
            _enumEditorLoading  = false;
            _inlineEditor.IsVisible     = false;
            _inlineEnumEditor.IsVisible = false;
            _inlineEditor.Foreground = ParamValueTextBrush;
            _inlineEditor.Background = new SolidColorBrush(Color.FromRgb(0x18, 0x28, 0x38));
            _inlineEditor.CaretBrush = null;
            _scriptTokens = Array.Empty<(string, IBrush)>();
            _scriptOverlay.Tokens = _scriptTokens;
            _scriptOverlay.IsVisible = false;
            InvalidateAndMeasure();
        }
        finally
        {
            _commitParamEditInProgress = false;
        }
    }

    /// <summary>Discards the current edit without saving.</summary>
    private void CancelParamEdit()
    {
        HideVarDropdown();
        UnsubscribeInlineEditorScroll();
        _editingParamNode   = null;
        _editingParamParentElement = null;
        _editingParamHook = null;
        _editingParamIsEnum = false;
        _enumEditorLoading  = false;
        _inlineEditor.IsVisible     = false;
        _inlineEnumEditor.IsVisible = false;
        _inlineEditor.Foreground = ParamValueTextBrush;
        _inlineEditor.Background = new SolidColorBrush(Color.FromRgb(0x18, 0x28, 0x38));
        _inlineEditor.CaretBrush = null;
        _scriptTokens = Array.Empty<(string, IBrush)>();
        _scriptOverlay.Tokens = _scriptTokens;
        _scriptOverlay.IsVisible = false;
        InvalidateAndMeasure();
        Focus();
    }

    private XTMF2.ModelSystemConstruct.Node? ResolveFilePathTargetNode(NodeViewModel node)
    {
        var sourceNode = node.UnderlyingNode;
        if (sourceNode.ContainedWithin is null)
            return null;

        foreach (var link in sourceNode.ContainedWithin.Links)
        {
            if (!ReferenceEquals(link.Origin, sourceNode))
                continue;
            if (link.OriginHook?.Name != "File Path")
                continue;

            if (link is XTMF2.ModelSystemConstruct.SingleLink singleLink
                && singleLink.Destination is XTMF2.ModelSystemConstruct.Node destinationNode)
                return destinationNode;

            if (link is XTMF2.ModelSystemConstruct.MultiLink multiLink
                && multiLink.Destinations.FirstOrDefault() is XTMF2.ModelSystemConstruct.Node multiDestination)
                return multiDestination;
        }

        return null;
    }

    private static bool IsFilePathTargetNode(NodeViewModel? node)
    {
        return node is not null && (node.ModuleType switch
        {
            Type t when t == typeof(XTMF2.RuntimeModules.OpenReadStreamFromFile) => true,
            Type t when t == typeof(XTMF2.RuntimeModules.BasicParameter<string>) => true,
            Type t when t == typeof(XTMF2.RuntimeModules.ScriptedParameter<string>) => true,
            _ => false
        });
    }

    private async Task TryOpenOpenReadStreamFromFileParameterAsync(NodeViewModel? targetNodeModel = null)
    {
        var nvm = targetNodeModel ?? _vm?.SelectedElement as NodeViewModel;
        if (_vm is null)
        {
            return;
        }
        if (nvm is null)
        {
            _vm.ShowToast("Select a file-path parameter node to update.", isError: true, durationMs: 3000);
            return;
        }

        if (!IsFilePathTargetNode(nvm))
        {
            _vm.ShowToast("Select a file-path parameter node or an OpenReadStreamFromFile node.", isError: true, durationMs: 3000);
            return;
        }

        string GetExpression(NodeViewModel node)
        {
            if (node.IsScriptedParameter)
            {
                return node.ParameterValueRepresentation ?? string.Empty;
            }
            else if(node.ModuleType == typeof(XTMF2.RuntimeModules.OpenReadStreamFromFile))
            {
                // For OpenReadStreamFromFile nodes, the file path is stored in the "FilePath" parameter.
                var filePathParam = node.GetParameter("File Path");
                if (filePathParam is null)
                {
                    return string.Empty;
                }
                if (filePathParam.ParameterValue?.GetValueAtEditingTime(typeof(string), out var value, out string? conversionError) ?? false)
                {
                    return value as string ?? string.Empty;
                }
                return string.Empty;
            }
            else
            {
                return node.ParameterValueRepresentation ?? string.Empty;
            }
        }

        string GetResolvedValue(NodeViewModel node)
        {
            if (node.IsScriptedParameter)
            {
                return node.EvaluateParameterValue() ?? string.Empty;
            }
            else if(node.ModuleType == typeof(XTMF2.RuntimeModules.OpenReadStreamFromFile))
            {
                var filePathParam = node.GetParameter("File Path");
                if (filePathParam is null)
                {
                    return string.Empty;
                }
                if (filePathParam.ParameterValue?.GetValueAtEditingTime(typeof(string), out var value, out string? conversionError) ?? false)
                {
                    return value as string ?? string.Empty;
                }
                return string.Empty;
            }
            else
            {
                return node.ParameterValueRepresentation ?? string.Empty;
            }
        }

        var targetNode = ResolveFilePathTargetNode(nvm) ?? nvm.UnderlyingNode;
        var targetVm = ReferenceEquals(targetNode, nvm.UnderlyingNode)
            ? nvm
            : new NodeViewModel(targetNode, _vm.Session, _vm.User);
        var currentExpression = GetExpression(targetVm);
        var currentValue = GetResolvedValue(targetVm);

        if (string.IsNullOrWhiteSpace(currentValue))
        {
            _vm?.ShowToast("No file path is currently set.", isError: true, durationMs: 3000);
            return;
        }
        // We need to check to see if it was a directory or if it was a file
        var directoryInfo = new DirectoryInfo(currentValue);
        var fileInfo = new FileInfo(currentValue);
        ;
        if (!directoryInfo.Exists && !fileInfo.Exists)
        {
            _vm.ShowToast($"The file '{currentValue}' does not exist.", isError: true, durationMs: 4000);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = currentValue, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _vm.ShowToast($"Unable to open '{currentValue}': {ex.Message}", isError: true, durationMs: 4000);
        }
    }

    private async Task TryUpdateOpenReadStreamFromFileParameterAsync(bool directory, NodeViewModel? targetNode = null)
    {
        targetNode ??= _vm?.SelectedElement as NodeViewModel;
        if (_vm is null)
        {
            return;
        }
        if (targetNode is not NodeViewModel nvm)
        {
            _vm.ShowToast("Select a file-path parameter node to update.", isError: true, durationMs: 3000);
            return;
        }

        if (!IsFilePathTargetNode(nvm))
        {
            _vm.ShowToast("Select a file-path parameter node or an OpenReadStreamFromFile node.", isError: true, durationMs: 3000);
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
        {
            _vm.ShowToast("Unable to access the file system.", isError: true, durationMs: 3000);
            return;
        }

        try
        {
            if(directory)
            {
                var directories = await topLevel.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
                {
                    Title = "Select Folder",
                    AllowMultiple = false
                });

                if (directories.Count == 0) return;

                var directoryPath = directories[0].TryGetLocalPath();
                if (string.IsNullOrEmpty(directoryPath)) return;

                if(!_vm.UpdateCurrentParameterValueFromFilePath(nvm, directoryPath, true, out var error))
                {
                    _vm.ShowToast(error?.Message ?? "Failed to update the directory path.", isError: true, durationMs: 4000);
                }
            }
            else
            {
                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    Title = "Select File",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new Avalonia.Platform.Storage.FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                    }
                });

                if (files.Count == 0) return;

                var filePath = files[0].TryGetLocalPath();
                if (string.IsNullOrEmpty(filePath)) return;

                if(!_vm.UpdateCurrentParameterValueFromFilePath(nvm, filePath, false, out var error))
                {
                    _vm.ShowToast(error?.Message ?? "Failed to update the file path.", isError: true, durationMs: 4000);
                }
            }
            
            InvalidateVisual();
        }
        catch (Exception ex)
        {
            _vm.ShowToast($"Error opening file picker: {ex.Message}", isError: true, durationMs: 4000);
        }
    }

    // ── Inline enum editor handlers ───────────────────────────────────────

    private void OnInlineEnumEditorDropDownClosed(object? sender, EventArgs e)
    {
        // Fires when the user picks an item or presses Escape to dismiss.
        // Escape is handled by OnInlineEnumEditorKeyDown first (which sets _enumEditorLoading
        // before closing the dropdown), so the guard below prevents a commit on cancel.
        if (_enumEditorLoading || _commitParamEditInProgress) return;
        if (_editingParamNode is not null && _editingParamIsEnum
            && _inlineEnumEditor.SelectedItem is not null)
        {
            CommitParamEdit();
        }
    }

    private void OnInlineEnumEditorLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Don't commit if loading, committing, or if the dropdown popup just took focus.
        if (_enumEditorLoading || _commitParamEditInProgress) return;
        if (_editingParamNode is not null && _editingParamIsEnum && !_inlineEnumEditor.IsDropDownOpen)
            CommitParamEdit();
    }

    private void OnInlineEnumEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            // Set loading flag so the DropDownClosed event that fires when we close
            // the dropdown programmatically does not trigger a commit.
            _enumEditorLoading = true;
            _inlineEnumEditor.IsDropDownOpen = false;
            _enumEditorLoading = false;
            CancelParamEdit();
            e.Handled = true;
        }
    }

    private void OnInlineEditorKeyDown(object? sender, KeyEventArgs e)
    {
        // ── Variable autocomplete dropdown navigation ─────────────────────
        if (_varDropdownVisible)
        {
            int count = _varDropdownStack.Children.Count;
            if (e.Key == Key.Down)
            {
                _varSelectedIndex = Math.Min(_varSelectedIndex + 1, count - 1);
                UpdateDropdownHighlight();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Up)
            {
                _varSelectedIndex = Math.Max(_varSelectedIndex - 1, 0);
                UpdateDropdownHighlight();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Tab)
            {
                SelectCurrentDropdownItem();
                e.Handled = true;
                return;
            }
            if (e.Key is Key.Return or Key.Enter)
            {
                // Complete with the highlighted item; do NOT commit the whole edit.
                SelectCurrentDropdownItem();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape)
            {
                HideVarDropdown();
                e.Handled = true;
                return;
            }
        }

        // ── Standard inline-editor keys ───────────────────────────────────
        if (e.Key is Key.Return or Key.Enter)
        {
            CommitParamEdit();
            Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelParamEdit();
            e.Handled = true;
        }
    }

    // ── Variable autocomplete helpers ─────────────────────────────────────

    // ── Script syntax tokenizer ───────────────────────────────────────────

    /// <summary>
    /// Breaks <paramref name="text"/> into coloured segments for display in the
    /// scripted-parameter inline editor.
    /// <list type="bullet">
    ///   <item>Known model-system variables → <see cref="ScriptVarKnownBrush"/> (green)</item>
    ///   <item>Unrecognised identifiers   → <see cref="ScriptVarUnknownBrush"/> (red)</item>
    ///   <item>Numeric literals            → <see cref="ScriptNumberBrush"/> (light-blue)</item>
    ///   <item>String literals             → <see cref="ScriptStringBrush"/> (orange)</item>
    ///   <item><c>true</c> / <c>false</c>  → <see cref="ScriptKeywordBrush"/> (gold)</item>
    ///   <item>Operators &amp; punctuation → <see cref="ScriptOperatorBrush"/> (steel-blue)</item>
    ///   <item>Whitespace                  → <see cref="ParamValueTextBrush"/> (neutral)</item>
    /// </list>
    /// </summary>
    private (string text, IBrush brush)[] TokenizeScript(string text)
    {
        if (_vm is null || string.IsNullOrEmpty(text))
            return [];

        var knownNames = new HashSet<string>(
            _vm.ModelSystemVariables.Select(v => v.Name)
                .Concat(_vm.LocalVariables.Select(v => v.Name)),
            StringComparer.OrdinalIgnoreCase);

        List<(string, IBrush)> tokens = [];
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];

            // ── String literal ─────────────────────────────────────────────
            if (c == '"')
            {
                int start = i++;
                while (i < text.Length && text[i] != '"') i++;
                if (i < text.Length) i++; // consume closing quote
                tokens.Add((text[start..i], _isLight ? ScriptStringBrushL : ScriptStringBrush));
                continue;
            }

            // ── Whitespace ─────────────────────────────────────────────────
            // Emit whitespace as its own invisible token so it does not bleed
            // into the operator brush and the overlay advances correctly.
            if (c == ' ' || c == '\t')
            {
                int start = i;
                while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
                tokens.Add((text[start..i], Brushes.Transparent));
                continue;
            }

            // ── Identifier / keyword ───────────────────────────────────────
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;

                // Greedily extend the token by consuming "<spaces><word>" segments
                // when the resulting substring matches a known variable name that
                // contains spaces (e.g. "Home Based Work").
                int extended = i;
                while (extended < text.Length && text[extended] == ' ')
                {
                    int spaceEnd = extended;
                    while (spaceEnd < text.Length && text[spaceEnd] == ' ') spaceEnd++;
                    if (spaceEnd >= text.Length ||
                        (!char.IsLetterOrDigit(text[spaceEnd]) && text[spaceEnd] != '_'))
                        break;
                    int wordEnd = spaceEnd;
                    while (wordEnd < text.Length &&
                           (char.IsLetterOrDigit(text[wordEnd]) || text[wordEnd] == '_'))
                        wordEnd++;
                    if (knownNames.Contains(text[start..wordEnd]))
                    {
                        i = wordEnd;
                        extended = wordEnd;
                    }
                    else
                    {
                        break;
                    }
                }

                var word = text[start..i];
                IBrush brush = word switch
                {
                    "true" or "false" => _isLight ? ScriptKeywordBrushL : ScriptKeywordBrush,
                    _ => knownNames.Contains(word)
                                         ? (_isLight ? ScriptVarKnownBrushL : ScriptVarKnownBrush)
                                         : (_isLight ? ScriptVarUnknownBrushL : ScriptVarUnknownBrush),
                };
                tokens.Add((word, brush));
                continue;
            }

            // ── Numeric literal ──────────────────────────────────────────
            if (char.IsDigit(c))
            {
                int start = i;
                while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.')) i++;
                tokens.Add((text[start..i], _isLight ? ScriptNumberBrushL : ScriptNumberBrush));
                continue;
            }

            // ── Operator / punctuation (single or double char) ────────────────
            {
                int start = i++;
                // Absorb two-char operators: &&, ||, ==, !=, >=, <=
                if (i < text.Length && (
                    (c == '&' && text[i] == '&') ||
                    (c == '|' && text[i] == '|') ||
                    (c == '=' && text[i] == '=') ||
                    (c == '!' && text[i] == '=') ||
                    (c == '>' && text[i] == '=') ||
                    (c == '<' && text[i] == '=')))
                {
                    i++;
                }
                tokens.Add((text[start..i], _isLight ? ScriptOperatorBrushL : ScriptOperatorBrush));
            }
        }
        return tokens.ToArray();
    }

    /// <summary>
    /// Returns <c>true</c> for characters that terminate a variable token
    /// in a scripted-parameter expression.
    /// </summary>
    private static bool IsExpressionSpecialChar(char c) =>
        c is '+' or '-' or '*' or '/' or '^' or '?' or ':'
           or '&' or '|' or '<' or '>' or '=' or '!' or '(' or ')' or '"';

    /// <summary>
    /// Called whenever the inline-editor text changes.  When editing a
    /// <see cref="NodeViewModel.IsScriptedParameter"/> node, extracts the
    /// token at the caret and populates (or hides) the autocomplete dropdown.
    /// </summary>
    private void OnInlineEditorTextChanged(object? sender, TextChangedEventArgs e)
    {
        // ── Syntax-highlight tokens for scripted params ─────────────────────────
        if (_editingParamNode is { IsScriptedParameter: true })
        {
            _scriptTokens = TokenizeScript(_inlineEditor.Text ?? string.Empty);
            _scriptOverlay.Tokens = _scriptTokens;
            // Offset is kept current by the _inlineEditorScrollSub observable subscription;
            // just redraw with the already-known offset.
            _scriptOverlay.InvalidateVisual();
        }
        else
        {
            _scriptTokens = [];
            _scriptOverlay.Tokens = _scriptTokens;
        }

        // ── Variable autocomplete dropdown ──────────────────────────────────
        if (_editingParamNode is null || !_editingParamNode.IsScriptedParameter || _vm is null)
        {
            HideVarDropdown();
            return;
        }

        var text = _inlineEditor.Text ?? string.Empty;
        var caret = Math.Clamp(_inlineEditor.CaretIndex, 0, text.Length);

        // Walk backwards from the caret to find the start of the current token.
        int tokenStart = caret;
        while (tokenStart > 0)
        {
            char ch = text[tokenStart - 1];
            if (IsExpressionSpecialChar(ch))
            {
                break;
            }
            tokenStart--;
        }

        var token = text[tokenStart..caret];

        // Strip any leading whitespace that the backward walk included (e.g. a
        // space immediately after an operator).  Adjust _varTokenStart so that
        // CompleteVariable() replaces only the real identifier text.
        int leadingSpaces = token.Length - token.TrimStart().Length;
        tokenStart += leadingSpaces;
        token = token.TrimStart();

        // Also strip trailing whitespace (caret resting just after a space).
        token = token.TrimEnd();

        _varTokenStart = tokenStart;

        if (token.Length == 0)
        {
            HideVarDropdown();
            return;
        }

        bool isLight = Application.Current?.ActualThemeVariant == ThemeVariant.Light;

        var matches = _vm.ModelSystemVariables
            .Concat(_vm.LocalVariables)
            .Where(v => v.Name.Contains(token, StringComparison.OrdinalIgnoreCase))
            .Select(v => v.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Take(MaxVarDropdownItems)
            .ToList();

        if (matches.Count == 0)
        {
            HideVarDropdown();
            return;
        }

        var normalBg = isLight
            ? new SolidColorBrush(Color.FromRgb(0xF8, 0xF9, 0xFF))
            : new SolidColorBrush(Color.FromRgb(0x1E, 0x2E, 0x3E));
        IBrush normalFg = isLight ? Brushes.Black : Brushes.White;

        _varDropdownBorder.Background = normalBg;
        _varDropdownBorder.BorderBrush = isLight
            ? new SolidColorBrush(Color.FromRgb(0x88, 0xAA, 0xCC))
            : new SolidColorBrush(Color.FromRgb(0x44, 0x88, 0xCC));

        _varDropdownStack.Children.Clear();
        foreach (var name in matches)
        {
            var captured = name;
            var tb = new TextBlock
            {
                Text = captured,
                Padding = new Thickness(8, 3, 8, 3),
                Foreground = normalFg,
                Background = normalBg,
                FontSize = HookFontSize,
                FontFamily = new Avalonia.Media.FontFamily("Segoe UI, Arial, sans-serif"),
            };
            tb.PointerEntered += (_, _) =>
            {
                tb.Background = new SolidColorBrush(Color.FromRgb(0x20, 0x60, 0xA0));
                tb.Foreground = Brushes.White;
            };
            tb.PointerExited += (_, _) =>
            {
                // UpdateDropdownHighlight will repaint based on _varSelectedIndex.
                UpdateDropdownHighlight();
            };
            tb.PointerPressed += (_, pe) =>
            {
                CompleteVariable(captured);
                pe.Handled = true;
            };
            _varDropdownStack.Children.Add(tb);
        }

        _varSelectedIndex = 0;
        UpdateDropdownHighlight();
        _varDropdownBorder.IsVisible = true;
        _varDropdownVisible = true;
        InvalidateMeasure();
    }

    /// <summary>Repaints the selection highlight so only the row at <see cref="_varSelectedIndex"/> is highlighted.</summary>
    private void UpdateDropdownHighlight()
    {
        bool isLight = Application.Current?.ActualThemeVariant == ThemeVariant.Light;
        var normalBg = isLight
            ? new SolidColorBrush(Color.FromRgb(0xF8, 0xF9, 0xFF))
            : new SolidColorBrush(Color.FromRgb(0x1E, 0x2E, 0x3E));
        var selBg = new SolidColorBrush(Color.FromRgb(0x20, 0x60, 0xA0));
        IBrush normalFg = isLight ? Brushes.Black : Brushes.White;

        for (int i = 0; i < _varDropdownStack.Children.Count; i++)
        {
            if (_varDropdownStack.Children[i] is not TextBlock tb) continue;
            bool sel = i == _varSelectedIndex;
            tb.Background = sel ? selBg : normalBg;
            tb.Foreground = sel ? Brushes.White : normalFg;
        }
    }

    /// <summary>Completes the current token with the currently highlighted dropdown item.</summary>
    private void SelectCurrentDropdownItem()
    {
        int count = _varDropdownStack.Children.Count;
        if (_varSelectedIndex < 0 || _varSelectedIndex >= count) 
        {
            return;
        }
        if (_varDropdownStack.Children[_varSelectedIndex] is TextBlock tb && tb.Text is { } name)
        {
            CompleteVariable(name);
        }
    }

    /// <summary>
    /// Replaces the token starting at <see cref="_varTokenStart"/> up to the current
    /// caret position with <paramref name="name"/>, then closes the dropdown.
    /// The editor remains open so the user can continue typing.
    /// </summary>
    private void CompleteVariable(string name)
    {
        var text = _inlineEditor.Text ?? string.Empty;
        var caret = Math.Clamp(_inlineEditor.CaretIndex, 0, text.Length);
        _inlineEditor.Text = text[.._varTokenStart] + name + text[caret..];
        _inlineEditor.CaretIndex = _varTokenStart + name.Length;
        HideVarDropdown();
        // Clicking a TextBlock item shifted focus to the canvas; return it to the
        // inline editor so the user can keep typing without clicking again.
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () => _inlineEditor.Focus(),
            Avalonia.Threading.DispatcherPriority.Input);
    }

    /// <summary>Hides and clears the variable autocomplete dropdown.</summary>
    private void HideVarDropdown()
    {
        if (!_varDropdownVisible) return;
        _varDropdownVisible = false;
        _varDropdownBorder.IsVisible = false;
        _varDropdownStack.Children.Clear();
        InvalidateMeasure();
    }

    private void OnInlineEditorLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // When the variable autocomplete dropdown is visible the user may have clicked
        // a suggestion item. TextBlock items are non-focusable, so focus falls to the
        // canvas. We must NOT commit here; CompleteVariable() keeps the session open
        // and will immediately return focus to the inline editor.
        if (_varDropdownVisible) return;

        // Don't commit if we're in the middle of a drag/resize operation (focus naturally shifts to canvas during pointer operations).
        if (_inDragOrResize) return;

        // Commit on focus loss (e.g. user clicks away to another element).
        if (_editingParamNode is not null)
            CommitParamEdit();
    }

    /// <summary>
    /// Updates the stored inline editor position and size fields to match the current
    /// position and size of the element being edited. This is called during element
    /// move/resize operations to keep the inline editor textbox synchronized.
    /// </summary>
    private void SyncEditingElementPositions()
    {
        // Sync parameter editor position/size if editing
        if (_editingParamNode is not null)
        {
            _editingParamEditorX = _editingParamNode.X;
            _editingParamEditorY = _editingParamNode.Y + NodeHeaderHeight;
            _editingParamEditorW = NodeRenderWidth(_editingParamNode);
        }

        // Sync name editor position/size if editing
        if (_editingNameElement is not null)
        {
            if (_editingNameElement is NodeViewModel nvm)
            {
                _nameEditorX = nvm.X;
                _nameEditorY = nvm.Y;
                _nameEditorW = NodeRenderWidth(nvm);
                _nameEditorH = NodeHeaderHeight;
            }
            else if (_editingNameElement is StartViewModel svm)
            {
                _nameEditorX = svm.X - StartViewModel.Radius;
                _nameEditorY = svm.Y + StartViewModel.Radius + 2;
                _nameEditorW = svm.Diameter + 20;
                _nameEditorH = NodeHeaderHeight;
            }
            else if (_editingNameElement is FunctionTemplateViewModel ftvm)
            {
                _nameEditorX = ftvm.X;
                _nameEditorY = ftvm.Y;
                _nameEditorW = ftvm.Width;
                _nameEditorH = FtHeaderHeight;
            }
            else if (_editingNameElement is FunctionInstanceViewModel fivm)
            {
                _nameEditorX = fivm.X;
                _nameEditorY = fivm.Y;
                _nameEditorW = fivm.Width;
                _nameEditorH = FtHeaderHeight;
            }
            else if (_editingNameElement is FunctionParameterViewModel fpvm)
            {
                _nameEditorX = fpvm.X;
                _nameEditorY = fpvm.Y;
                _nameEditorW = fpvm.Width;
                _nameEditorH = FtHeaderHeight;
            }
        }

        // Sync comment editor position/size if editing
        if (_editingCommentBlock is not null)
        {
            _editingCommentEditorX = _editingCommentBlock.X;
            _editingCommentEditorY = _editingCommentBlock.Y + CommentHeaderHeight;
            _editingCommentEditorW = _editingCommentBlock.Width;
            _editingCommentEditorH = _editingCommentBlock.Height - CommentHeaderHeight;
        }

        // Sync comment header editor position/size if editing
        if (_editingCommentHeaderBlock is not null)
        {
            _editingCommentHeaderEditorX = _editingCommentHeaderBlock.X;
            _editingCommentHeaderEditorY = _editingCommentHeaderBlock.Y;
            _editingCommentHeaderEditorW = _editingCommentHeaderBlock.Width - CommentFoldSize;
            _editingCommentHeaderEditorH = CommentHeaderHeight;
        }
    }

}
