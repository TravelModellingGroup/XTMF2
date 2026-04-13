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
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.VisualTree;
using XTMF2.Editing;
using XTMF2.GUI.ViewModels;
using XTMF2.ModelSystemConstruct;
using System.Collections.ObjectModel;

namespace XTMF2.GUI.Controls;

partial class ModelSystemCanvas
{
    // ── Inline name editor helpers ───────────────────────────────────────────

    /// <summary>Opens the single-line name editor over <paramref name="element"/> (node or start).</summary>
    private void BeginNameEdit(ICanvasElement element)
    {
        CommitParamEdit();
        CommitCommentEdit();

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

    /// <summary>Shows the multi-line comment editor over <paramref name="comment"/>.</summary>
    private void BeginCommentEdit(CommentBlockViewModel comment)
    {
        _editingCommentBlock = comment;
        _editingCommentEditorX = comment.X;
        _editingCommentEditorY = comment.Y;
        _editingCommentEditorW = comment.Width;
        _editingCommentEditorH = comment.Height;
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
        if (_editingCommentBlock is not null)
        {
            CommitCommentEdit();
        }
    }

}
