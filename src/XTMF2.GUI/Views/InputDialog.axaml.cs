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
using System.ComponentModel;

namespace XTMF2.GUI.Views;

public partial class InputDialog : Window, INotifyPropertyChanged
{
    private string? _prompt;
    private string? _inputText;

    public new event PropertyChangedEventHandler? PropertyChanged;

    public string? Prompt
    {
        get => _prompt;
        set
        {
            _prompt = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Prompt)));
        }
    }

    public string? InputText
    {
        get => _inputText;
        set
        {
            _inputText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InputText)));
        }
    }

    public bool WasCancelled { get; private set; } = true;

    public InputDialog()
    {
        InitializeComponent();
        DataContext = this;
        RegisterEscapeClose();
    }

    public InputDialog(string title, string prompt, string defaultText = "")
    {
        InitializeComponent();
        DataContext = this;
        RegisterEscapeClose();
        Title = title;
        Prompt = prompt;
        InputText = defaultText;

        // Focus the input when the window is opened
        Opened += (s, e) => InputTextBox.Focus();
    }

    private void RegisterEscapeClose() =>
        AddHandler(KeyDownEvent, (_, ke) =>
        {
            if (ke.Key != Key.Escape) return;
            Cancel_Click(null, new RoutedEventArgs());
            ke.Handled = true;
        }, RoutingStrategies.Tunnel);

    private void OK_Click(object? sender, RoutedEventArgs e)
    {
        WasCancelled = false;
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        WasCancelled = true;
        Close();
    }
}
