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
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using XTMF2.GUI.ViewModels;

namespace XTMF2.GUI.Views;

public sealed record EmbeddedFilePathParameterChoice(NodeViewModel Parameter, string DisplayName);

public partial class EmbeddedFilePathParameterPickerDialog : Window
{
    private string _prompt;

    public IReadOnlyList<EmbeddedFilePathParameterChoice> Choices { get; }
    public string Prompt => _prompt;
    public NodeViewModel? SelectedParameter { get; private set; }

    public EmbeddedFilePathParameterPickerDialog()
        : this([], "Select an embedded parameter:")
    {
    }

    public EmbeddedFilePathParameterPickerDialog(
        IReadOnlyList<EmbeddedFilePathParameterChoice> choices,
        string prompt)
    {
        Choices = choices;
        _prompt = prompt;
        InitializeComponent();
        DataContext = this;

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            Cancel_Click(null, new RoutedEventArgs());
            e.Handled = true;
        }, RoutingStrategies.Tunnel);

        ParameterListBox.DoubleTapped += (_, _) =>
        {
            if (ParameterListBox.SelectedItem is EmbeddedFilePathParameterChoice)
                Select_Click(null, new RoutedEventArgs());
        };

        Opened += (_, _) =>
        {
            if (ParameterListBox.ItemCount > 0)
                ParameterListBox.SelectedIndex = 0;
            ParameterListBox.Focus();
        };
    }

    private void Select_Click(object? sender, RoutedEventArgs e)
    {
        SelectedParameter = (ParameterListBox.SelectedItem as EmbeddedFilePathParameterChoice)?.Parameter;
        if (SelectedParameter is not null)
            Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        SelectedParameter = null;
        Close();
    }
}
