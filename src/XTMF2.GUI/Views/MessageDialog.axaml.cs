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
using Avalonia.Interactivity;
using System.ComponentModel;

namespace XTMF2.GUI.Views;

public partial class MessageDialog : Window, INotifyPropertyChanged
{
    public enum MessageType
    {
        Information,
        Warning,
        Error
    }

    private string? _message;
    private MessageType _type;

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

    public MessageType Type
    {
        get => _type;
        set
        {
            _type = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Type)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsInformation)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsWarning)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsError)));
        }
    }

    public bool IsInformation => Type == MessageType.Information;
    public bool IsWarning => Type == MessageType.Warning;
    public bool IsError => Type == MessageType.Error;

    public MessageDialog()
    {
        InitializeComponent();
        DataContext = this;
        Type = MessageType.Error;
    }

    public MessageDialog(string message, string title, MessageType type = MessageType.Information)
    {
        InitializeComponent();
        DataContext = this;
        Title = title;
        Message = message;
        Type = type;
    }

    private void OK_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
