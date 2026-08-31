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
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using XTMF2.GUI.Resources;

namespace XTMF2.GUI.Views;

/// <summary>
/// A single entry shown in the tab list of <see cref="CodeStyleErrorDialog"/>.
/// </summary>
public sealed class CodeStyleErrorEntry(int number, string message)
{
    public string Label { get; } = Strings.Format(Strings.RuntimeInitialization_ErrorTabLabel, number);
    public string Message { get; } = message;
}

/// <summary>
/// Dialog that lets the user select, from a tab list, which of several code
/// style errors reported during runtime initialization to view in full.
/// </summary>
public partial class CodeStyleErrorDialog : Window, INotifyPropertyChanged
{
    private int _selectedIndex;

    public new event PropertyChangedEventHandler? PropertyChanged;

    public string? IntroText { get; private set; }

    public IReadOnlyList<CodeStyleErrorEntry> Errors { get; private set; } = [];

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_selectedIndex == value || value < 0 || value >= Errors.Count)
            {
                return;
            }
            _selectedIndex = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedIndex)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentMessage)));
        }
    }

    public string CurrentMessage => Errors.Count == 0 ? string.Empty : Errors[_selectedIndex].Message;

    public CodeStyleErrorDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    public CodeStyleErrorDialog(string title, string introText, IReadOnlyList<string> errors)
        : this()
    {
        Title = title;
        IntroText = introText;
        Errors = errors.Select((message, i) => new CodeStyleErrorEntry(i + 1, message)).ToList();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IntroText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Errors)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentMessage)));
    }

    private void ErrorListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedIndex: >= 0 } listBox)
        {
            SelectedIndex = listBox.SelectedIndex;
        }
    }

    private void OK_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
