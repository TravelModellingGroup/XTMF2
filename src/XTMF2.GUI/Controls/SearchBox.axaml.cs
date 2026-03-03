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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;

namespace XTMF2.GUI.Controls;

public partial class SearchBox : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<SearchBox, string>(nameof(Text), defaultValue: "");

    public static readonly StyledProperty<string> WatermarkProperty =
        AvaloniaProperty.Register<SearchBox, string>(nameof(Watermark), defaultValue: "");

    /// <summary>Raised when the user presses Enter inside the search box.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> EnterPressedEvent =
        RoutedEvent.Register<SearchBox, RoutedEventArgs>(nameof(EnterPressed), RoutingStrategies.Bubble);

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    public event EventHandler<RoutedEventArgs> EnterPressed
    {
        add => AddHandler(EnterPressedEvent, value);
        remove => RemoveHandler(EnterPressedEvent, value);
    }

    public SearchBox()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Moves keyboard focus into the inner text box of this search box.
    /// </summary>
    public void FocusSearchBox()
    {
        InnerTextBox.Focus(NavigationMethod.Tab);
    }

    private void InnerTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (!string.IsNullOrEmpty(Text))
            {
                // Text present: clear the filter and consume the event so the
                // host window (e.g. TypePickerDialog) does not close.
                Text = "";
                e.Handled = true;
            }
            // Empty box: don't consume — let the event bubble so the host
            // window can act on it (e.g. close via IsCancel routing).
        }
        else if (e.Key == Key.Enter)
        {
            RaiseEvent(new RoutedEventArgs(EnterPressedEvent));
            e.Handled = true;
        }
    }
}
