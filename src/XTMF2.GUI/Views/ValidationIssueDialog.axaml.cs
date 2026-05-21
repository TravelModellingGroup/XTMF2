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
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace XTMF2.GUI.Views;

public partial class ValidationIssueDialog : Window, INotifyPropertyChanged
{
    private string? _message;

    public new event PropertyChangedEventHandler? PropertyChanged;

    public string? Message
    {
        get => _message;
        set
        {
            _message = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Message)));
        }
    }

    public bool Result { get; private set; }

    public ValidationIssueDialog()
    {
        InitializeComponent();
        DataContext = this;
        RegisterEscapeClose();
    }

    public ValidationIssueDialog(string title, string message)
        : this()
    {
        Title = title;
        Message = message;
    }

    private void RegisterEscapeClose() =>
        AddHandler(KeyDownEvent, (_, ke) =>
        {
            if (ke.Key != Key.Escape) return;
            Cancel_Click(null, new RoutedEventArgs());
            ke.Handled = true;
        }, RoutingStrategies.Tunnel);

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        Result = true;
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Result = false;
        Close();
    }
}
