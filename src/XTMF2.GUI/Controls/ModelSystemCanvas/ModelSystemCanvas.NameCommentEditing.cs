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
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using XTMF2.Editing;
using XTMF2.GUI.ViewModels;

namespace XTMF2.GUI.Controls;

partial class ModelSystemCanvas
{
    private void BeginDescriptionEdit(FunctionParameterViewModel parameter)
    {
        CommitParamEdit();
        CommitCommentEdit();
        CommitCommentHeaderEdit();
        CommitNameEdit();

        _editingDescriptionParameter = parameter;
        _descriptionEditorX = parameter.X;
        _descriptionEditorY = parameter.Y + FtHeaderHeight + FpTypeRowHeight;
        _descriptionEditorW = parameter.Width;
        _descriptionEditorH = FpDescriptionRowHeight;
        _descriptionEditor.Text = parameter.Description;
        _descriptionEditor.IsVisible = true;
        InvalidateMeasure();
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _descriptionEditor.Focus();
            _descriptionEditor.SelectAll();
        }, Avalonia.Threading.DispatcherPriority.Render);
    }

    private void CommitDescriptionEdit()
    {
        if (_editingDescriptionParameter is null) return;
        var parameter = _editingDescriptionParameter;
        _editingDescriptionParameter = null;
        _descriptionEditor.IsVisible = false;
        var description = (_descriptionEditor.Text ?? string.Empty).Trim();
        if (!parameter.SetDescription(description, out var error))
        {
            _vm?.ShowToast(error?.Message ?? "Failed to set description.", isError: true, durationMs: 4000);
        }
        InvalidateAndMeasure();
    }

    private void CancelDescriptionEdit()
    {
        _editingDescriptionParameter = null;
        _descriptionEditor.IsVisible = false;
        InvalidateAndMeasure();
        Focus();
    }

    private void OnDescriptionEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return)
        {
            CommitDescriptionEdit();
            Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelDescriptionEdit();
            e.Handled = true;
        }
    }

    private void OnDescriptionEditorLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_inDragOrResize && _editingDescriptionParameter is not null)
            CommitDescriptionEdit();
    }

    // ── Inline name editor helpers ───────────────────────────────────────────

    /// <summary>Opens the single-line name editor over <paramref name="element"/> (node or start).</summary>
    private void BeginNameEdit(ICanvasElement element)
    {
        CommitParamEdit();
        CommitCommentEdit();
        CommitCommentHeaderEdit();

        bool isLight = Application.Current?.ActualThemeVariant == ThemeVariant.Light;
        _nameEditor.Foreground = isLight ? Brushes.Black : Brushes.White;
        _nameEditor.Background = isLight
            ? new SolidColorBrush(Color.FromRgb(0xE8, 0xF0, 0xFE))
            : new SolidColorBrush(Color.FromRgb(0x1A, 0x2C, 0x40));

        if (element is NodeViewModel nvm)
        {
            _nameEditorX = nvm.X;
            _nameEditorY = nvm.Y;
            _nameEditorW = NodeRenderWidth(nvm);
            _nameEditorH = NodeHeaderHeight;
        }
        else if (element is StartViewModel svm)
        {
            _nameEditorX = svm.X - StartViewModel.Radius;
            _nameEditorY = svm.Y + StartViewModel.Radius + 2;
            _nameEditorW = svm.Diameter + 20;
            _nameEditorH = NodeHeaderHeight;
        }
        else if (element is FunctionTemplateViewModel ftvm)
        {
            _nameEditorX = ftvm.X;
            _nameEditorY = ftvm.Y;
            _nameEditorW = ftvm.Width;
            _nameEditorH = FtHeaderHeight;
        }
        else if (element is FunctionInstanceViewModel fivm)
        {
            _nameEditorX = fivm.X;
            _nameEditorY = fivm.Y;
            _nameEditorW = fivm.Width;
            _nameEditorH = FtHeaderHeight;
        }
        else if (element is FunctionParameterViewModel fpvmEdit)
        {
            _nameEditorX = fpvmEdit.X;
            _nameEditorY = fpvmEdit.Y;
            _nameEditorW = fpvmEdit.Width;
            _nameEditorH = FtHeaderHeight;
        }
        else
        {
            return;
        }

        _editingNameElement = element;
        _nameEditor.Text = element.Name;
        _nameEditor.IsVisible = true;
        InvalidateMeasure();
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _nameEditor.Focus();
            _nameEditor.SelectAll();
        }, Avalonia.Threading.DispatcherPriority.Render);
    }

    /// <summary>Saves the name editor text and closes the editor.</summary>
    private void CommitNameEdit()
    {
        if (_editingNameElement is null) return;
        var element = _editingNameElement;
        var name = (_nameEditor.Text ?? string.Empty).Trim();
        _editingNameElement = null;
        _nameEditor.IsVisible = false;
        if (!string.IsNullOrWhiteSpace(name))
        {
            CommandError? renameError = null;
            bool ok = element switch
            {
                NodeViewModel nvm => nvm.SetName(name, out _),
                StartViewModel svm => svm.SetName(name, out _),
                FunctionTemplateViewModel ftvm => ftvm.SetName(name, out renameError),
                FunctionInstanceViewModel fivm => fivm.SetName(name, out renameError),
                FunctionParameterViewModel fpvmC => fpvmC.SetName(name, out renameError),
                _ => true,
            };
            if (!ok)
            {
                _vm?.ShowToast(renameError?.Message ?? "Failed to rename.", isError: true, durationMs: 4000);
            }
        }
        InvalidateAndMeasure();
    }

    /// <summary>Discards the name edit without saving.</summary>
    private void CancelNameEdit()
    {
        _editingNameElement = null;
        _nameEditor.IsVisible = false;
        InvalidateAndMeasure();
        Focus();
    }

    private void OnNameEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Return or Key.Enter)
        {
            CommitNameEdit();
            Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelNameEdit();
            e.Handled = true;
        }
    }

    private void OnNameEditorLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Don't commit if we're in the middle of a drag/resize operation.
        if (_inDragOrResize) return;

        if (_editingNameElement is not null)
        {
            CommitNameEdit();
        }
    }

    /// <summary>
    /// Opens the inline name editor for the currently selected node or start.
    /// Called from the editor view when F2 is pressed and a node/start is selected.
    /// </summary>
    public void BeginNameEditForSelected()
    {
        if (_vm?.SelectedElement is NodeViewModel or StartViewModel
                                 or FunctionTemplateViewModel or FunctionInstanceViewModel
                                 or FunctionParameterViewModel)
        {
            BeginNameEdit(_vm.SelectedElement);
        }
    }

    // ── Inline comment editor helpers ────────────────────────────────────────

    public void BeginCommentEditForSelected()
    {
        if (_vm?.SelectedElement is CommentBlockViewModel comment)
        {
            BeginCommentEdit(comment);
        }
    }

    /// <summary>
    /// Handles Tab/Shift+Tab behavior for comment blocks.
    /// If not currently editing, starts with header edit.
    /// If already editing header/body, toggles to the other field.
    /// </summary>
    private bool TryCycleCommentEditOnTab()
    {
        // Never hijack Tab while other inline editors are active.
        if (_editingParamNode is not null || _editingNameElement is not null)
            return false;

        var selectedComment = _vm?.SelectedElement as CommentBlockViewModel;

        // If not currently editing a comment, only start the cycle when a comment is selected.
        if (_editingCommentHeaderBlock is null && _editingCommentBlock is null && selectedComment is null)
            return false;

        var activeComment = _editingCommentHeaderBlock
            ?? _editingCommentBlock
            ?? selectedComment;

        if (activeComment is null)
            return false;

        if (_editingCommentHeaderBlock is not null)
        {
            BeginCommentEdit(activeComment);
            return true;
        }

        if (_editingCommentBlock is not null)
        {
            BeginCommentHeaderEdit(activeComment);
            return true;
        }

        // Selected but not currently editing: start with header first.
        BeginCommentHeaderEdit(activeComment);
        return true;
    }

    /// <summary>Shows the multi-line comment editor over <paramref name="comment"/>, positioned below the header band.</summary>
    private void BeginCommentEdit(CommentBlockViewModel comment)
    {
        CommitCommentHeaderEdit();
        _editingCommentBlock = comment;
        _editingCommentEditorX = comment.X;
        _editingCommentEditorY = comment.Y + CommentHeaderHeight;
        _editingCommentEditorW = comment.Width;
        _editingCommentEditorH = comment.Height - CommentHeaderHeight;
        _commentEditor.Text = comment.Name;   // Name returns the underlying Comment text.
        // Pick colours based on the active theme.
        bool isLight = Application.Current?.ActualThemeVariant == ThemeVariant.Light;
        _commentEditor.Foreground = isLight
            ? CommentTextBrush   // dark text on sticky-note yellow
            : Brushes.White;     // white text on dark background
        _commentEditor.Background = isLight
            ? new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xF0, 0x96))   // sticky-note yellow
            : new SolidColorBrush(Color.FromRgb(0x18, 0x28, 0x18));          // dark green-tinted panel
        _commentEditor.IsVisible = true;
        InvalidateMeasure();
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _commentEditor.Focus();
        }, Avalonia.Threading.DispatcherPriority.Render);
    }

    /// <summary>Saves the comment editor text and closes the editor.</summary>
    private void CommitCommentEdit()
    {
        if (_editingCommentBlock is null) return;
        var comment = _editingCommentBlock;
        var text = _commentEditor.Text ?? string.Empty;
        _editingCommentBlock = null;
        _commentEditor.IsVisible = false;
        comment.SetText(text);
        InvalidateAndMeasure();
    }

    /// <summary>Discards the comment edit without saving.</summary>
    private void CancelCommentEdit()
    {
        _editingCommentBlock = null;
        _commentEditor.IsVisible = false;
        InvalidateAndMeasure();
        Focus();
    }

    private void OnCommentEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt)) == 0)
        {
            if (TryCycleCommentEditOnTab())
            {
                e.Handled = true;
                return;
            }
        }
        if ((e.Key is Key.Return or Key.Enter) && (e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            // Ctrl+Enter commits; plain Enter inserts a newline (default).
            CommitCommentEdit();
            Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelCommentEdit();
            e.Handled = true;
        }
    }

    private void OnCommentEditorLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Don't commit if we're in the middle of a drag/resize operation.
        if (_inDragOrResize) return;

        if (_editingCommentBlock is not null)
        {
            CommitCommentEdit();
        }
    }

    // ── Inline comment header editor helpers ────────────────────────────

    /// <summary>Shows the single-line header editor over the header band of <paramref name="comment"/>.</summary>
    internal void BeginCommentHeaderEdit(CommentBlockViewModel comment)
    {
        CommitCommentEdit();
        _editingCommentHeaderBlock = comment;
        _editingCommentHeaderEditorX = comment.X;
        _editingCommentHeaderEditorY = comment.Y;
        // Leave room for the fold corner at the top-right.
        _editingCommentHeaderEditorW = comment.Width - CommentFoldSize;
        _editingCommentHeaderEditorH = CommentHeaderHeight;
        _commentHeaderEditor.Text = comment.Header;
        bool isLight = Application.Current?.ActualThemeVariant == ThemeVariant.Light;
        _commentHeaderEditor.Foreground = isLight ? CommentTextBrush : Brushes.White;
        _commentHeaderEditor.Background = isLight
            ? new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xD5, 0x1A))
            : new SolidColorBrush(Color.FromRgb(0x2A, 0x20, 0x00));
        _commentHeaderEditor.IsVisible = true;
        InvalidateMeasure();
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _commentHeaderEditor.Focus();
            _commentHeaderEditor.SelectAll();
        }, Avalonia.Threading.DispatcherPriority.Render);
    }

    /// <summary>Saves the header editor text and closes the editor.</summary>
    internal void CommitCommentHeaderEdit()
    {
        if (_editingCommentHeaderBlock is null) return;
        var comment = _editingCommentHeaderBlock;
        var text = (_commentHeaderEditor.Text ?? string.Empty).Trim();
        _editingCommentHeaderBlock = null;
        _commentHeaderEditor.IsVisible = false;
        comment.SetHeader(text);
        InvalidateAndMeasure();
    }

    /// <summary>Discards the header edit without saving.</summary>
    private void CancelCommentHeaderEdit()
    {
        _editingCommentHeaderBlock = null;
        _commentHeaderEditor.IsVisible = false;
        InvalidateAndMeasure();
        Focus();
    }

    private void OnCommentHeaderEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt)) == 0)
        {
            if (TryCycleCommentEditOnTab())
            {
                e.Handled = true;
                return;
            }
        }
        if (e.Key is Key.Return or Key.Enter)
        {
            CommitCommentHeaderEdit();
            Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelCommentHeaderEdit();
            e.Handled = true;
        }
    }

    private void OnCommentHeaderEditorLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_inDragOrResize) return;
        if (_editingCommentHeaderBlock is not null)
        {
            CommitCommentHeaderEdit();
        }
    }
}
