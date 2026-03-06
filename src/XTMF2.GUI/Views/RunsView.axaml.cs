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
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using XTMF2.GUI.ViewModels;

namespace XTMF2.GUI.Views;

public partial class RunsView : UserControl
{
    private RunsViewModel? _vm;

    public RunsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        _vm?.PropertyChanged -= OnVmPropertyChanged;
        _vm = DataContext as RunsViewModel;
        _vm?.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RunsViewModel.SelectedRun))
            WireSelectedRunMessages();
    }

    private INotifyCollectionChanged? _subscribedMessages;

    private void WireSelectedRunMessages()
    {
        _subscribedMessages?.CollectionChanged -= OnMessagesChanged;
        _subscribedMessages = _vm?.SelectedRun?.Messages;
        _subscribedMessages?.CollectionChanged += OnMessagesChanged;
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Auto-scroll to the bottom when new messages arrive.
        MessageScrollViewer.ScrollToEnd();
    }
}
