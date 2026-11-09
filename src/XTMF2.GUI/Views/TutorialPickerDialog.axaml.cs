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
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using XTMF2;
using XTMF2.GUI.Tutorials;

namespace XTMF2.GUI.Views;

public partial class TutorialPickerDialog : Window
{
    public TutorialDefinition? SelectedTutorial { get; private set; }

    public TutorialPickerDialog()
        : this(Array.Empty<TutorialCatalogEntry>(), new HashSet<string>(StringComparer.OrdinalIgnoreCase))
    {
    }

    public TutorialPickerDialog(
        IReadOnlyList<TutorialCatalogEntry> tutorials,
        IReadOnlySet<string> completedTutorialIds)
    {
        ArgumentNullException.ThrowIfNull(tutorials);
        ArgumentNullException.ThrowIfNull(completedTutorialIds);
        InitializeComponent();
        DataContext = this;
        TutorialList.ItemsSource = tutorials
            .Select(entry => new TutorialPickerItem(
                entry,
                completedTutorialIds.Contains(entry.Tutorial.Id)))
            .ToArray();
    }

    private void OnTutorialSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var item = TutorialList.SelectedItem as TutorialPickerItem;
        StartButton.IsEnabled = item is not null;
        SelectedTutorialTitle.Text = item?.Tutorial.Title ?? "Select a tutorial";
        SelectedTutorialDescription.Text = item?.Tutorial.Description
            ?? "Choose a tutorial to read its description.";
    }

    private void OnStartClick(object? sender, RoutedEventArgs e)
        => StartSelectedTutorial();

    private void OnTutorialDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (StartSelectedTutorial())
            e.Handled = true;
    }

    private bool StartSelectedTutorial()
    {
        if (TutorialList.SelectedItem is not TutorialPickerItem item)
            return false;

        SelectedTutorial = item.Tutorial;
        Close();
        return true;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
        => Close();
}

public sealed class TutorialPickerItem
{
    public TutorialDefinition Tutorial { get; }
    public string LibraryName { get; }
    public int SequenceNumber { get; }
    public bool IsFirstInLibrary { get; }
    public bool IsCompleted { get; }
    public string NumberedTitle => $"{SequenceNumber}. {Tutorial.Title}";

    public TutorialPickerItem(TutorialCatalogEntry entry, bool isCompleted)
    {
        Tutorial = entry.Tutorial;
        LibraryName = entry.LibraryName;
        SequenceNumber = entry.SequenceNumber;
        IsFirstInLibrary = entry.IsFirstInLibrary;
        IsCompleted = isCompleted;
    }
}
