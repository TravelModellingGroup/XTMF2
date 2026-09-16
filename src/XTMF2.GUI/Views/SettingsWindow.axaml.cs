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
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Layout;
using Avalonia.Threading;
using XTMF2.GUI;
using XTMF2.GUI.Resources;
using XTMF2.AI;
using XTMF2.GUI.Properties;

namespace XTMF2.GUI.Views;

public partial class SettingsWindow : Window
{
    private string? _currentTheme;
    private string? _currentLanguage;
    private string _currentAiProvider = "ollama";
    private string _currentAiModel = "llama3.2";
    private string _currentOllamaEndpoint = "http://localhost:11434";
    private readonly HttpClient _aiHttpClient = new();
    private readonly List<(RunServerEndpoint Endpoint, TextBox Name, TextBox Address, TextBox Port, TextBlock Status, Button Reconnect, Button Remove)> _runServerEditors = new();
    private readonly RunController? _runController;

    public event Action? SettingsSaved;

    public SettingsWindow()
        : this(null)
    {
    }

    public SettingsWindow(RunController? runController)
    {
        _runController = runController;
        InitializeComponent();
        if (_runController is not null)
            _runController.RunServerStateChanged += OnRunServerStateChanged;
        LoadSettings();
    }

    private void LoadSettings()
    {
        // Load theme preference
        _currentTheme = App.NormalizeThemeName(Properties.Settings.Default.Theme);
        
        // Set the selected theme in the combo box
        var themeItem = ThemeComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == _currentTheme);
        
        if (themeItem != null)
        {
            ThemeComboBox.SelectedItem = themeItem;
        }

        // Load language preference
        _currentLanguage = Properties.Settings.Default.Language ?? "en";
        
        // Set the selected language in the combo box
        var languageItem = LanguageComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == _currentLanguage);
        
        if (languageItem != null)
        {
            LanguageComboBox.SelectedItem = languageItem;
        }

        // Load system sounds preference
        PlaySystemSoundsCheckBox.IsChecked = Properties.Settings.Default.PlaySystemSounds;
            _currentAiProvider = Properties.Settings.Default.AiProvider;
            _currentAiModel = Properties.Settings.Default.AiModel;
            _currentOllamaEndpoint = Properties.Settings.Default.OllamaEndpoint;
            AiProviderComboBox.SelectedItem = AiProviderComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag?.ToString() == _currentAiProvider);
            OllamaEndpointTextBox.Text = _currentOllamaEndpoint;
            AiModelComboBox.SelectedItem = _currentAiModel;
            AiMaxCompactionCyclesTextBox.Text = Properties.Settings.Default.AiMaxCompactionCycles.ToString();
            RunServersPanel.Children.Clear();
            _runServerEditors.Clear();
            foreach (var endpoint in Properties.Settings.Default.RunServers)
                AddRunServerEditor(endpoint.Clone());
            foreach (var state in _runController?.GetRunServerStates() ?? Array.Empty<RunServerConnectionInfo>())
                ApplyRunServerState(state);
            _ = RefreshModelsAsync();
    }

    private void AddRunServer_Click(object? sender, RoutedEventArgs e)
        => AddRunServerEditor(new RunServerEndpoint { Name = "RunServer" });

    private void AddRunServerEditor(RunServerEndpoint endpoint)
    {
        var name = new TextBox { Text = endpoint.Name, Watermark = "Name" };
        var address = new TextBox { Text = endpoint.Address, Watermark = "Address" };
        var port = new TextBox { Text = endpoint.Port.ToString(), Watermark = "Port" };
        var status = new TextBlock { Text = endpoint.IsLocal ? "Available" : "Disconnected", Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };
        var reconnect = new Button { Content = "Reconnect", Padding = new Thickness(8, 4), IsEnabled = !endpoint.IsLocal };
        var remove = new Button { Content = "Remove", Padding = new Thickness(8, 4), IsEnabled = !endpoint.IsLocal };
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("1.2*,1.5*,Auto,Auto,Auto,Auto"),
            ColumnSpacing = 8
        };
        Grid.SetColumn(address, 1);
        Grid.SetColumn(port, 2);
        Grid.SetColumn(status, 3);
        Grid.SetColumn(reconnect, 4);
        Grid.SetColumn(remove, 5);
        row.Children.Add(name);
        row.Children.Add(address);
        row.Children.Add(port);
        row.Children.Add(status);
        row.Children.Add(reconnect);
        row.Children.Add(remove);
        reconnect.Click += (_, _) => ReconnectRunServer(endpoint, address, port, reconnect);
        remove.Click += (_, _) =>
        {
            RunServersPanel.Children.Remove(row);
            _runServerEditors.RemoveAll(editor => ReferenceEquals(editor.Remove, remove));
        };
        RunServersPanel.Children.Add(row);
        _runServerEditors.Add((endpoint, name, address, port, status, reconnect, remove));
    }

    private void OnRunServerStateChanged(RunServerConnectionInfo state)
        => Dispatcher.UIThread.Post(() => ApplyRunServerState(state));

    private void ApplyRunServerState(RunServerConnectionInfo state)
    {
        var editor = _runServerEditors.FirstOrDefault(item => item.Endpoint.Id == state.Endpoint.Id);
        if (editor.Status is null)
            return;

        editor.Status.Text = state.State switch
        {
            RunServerConnectionState.Available => "Available",
            RunServerConnectionState.Connecting => "Connecting...",
            _ => string.IsNullOrWhiteSpace(state.Error) ? "Disconnected" : $"Disconnected: {state.Error}"
        };
        editor.Reconnect.IsEnabled = state.State == RunServerConnectionState.Disconnected && !editor.Endpoint.IsLocal;
        ToolTip.SetTip(editor.Status, state.Error);
    }

    private void ReconnectRunServer(RunServerEndpoint endpoint, TextBox address, TextBox port, Button reconnect)
    {
        if (_runController is null || !int.TryParse(port.Text, out var portNumber))
            return;

        var currentEndpoint = endpoint.Clone();
        currentEndpoint.Address = address.Text?.Trim() ?? string.Empty;
        currentEndpoint.Port = portNumber;
        reconnect.IsEnabled = false;
        if (!_runController.ConnectRunServer(currentEndpoint, out var error) && !string.IsNullOrWhiteSpace(error))
            ToolTip.SetTip(reconnect, error);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_runController is not null)
            _runController.RunServerStateChanged -= OnRunServerStateChanged;
        base.OnClosed(e);
    }

    private void ThemeComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ThemeComboBox.SelectedItem is ComboBoxItem selectedItem)
        {
            var themeName = selectedItem.Tag?.ToString();
            if (themeName != null && Application.Current is App app)
            {
                // Apply theme immediately for preview
                app.ChangeTheme(themeName);
                _currentTheme = App.NormalizeThemeName(themeName);
            }
        }
    }

    private void LanguageComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (LanguageComboBox.SelectedItem is ComboBoxItem selectedItem)
        {
            var languageCode = selectedItem.Tag?.ToString();
            if (languageCode != null)
            {
                // Apply language immediately for preview
                LocalizationManager.ChangeLanguage(languageCode);
                _currentLanguage = languageCode;
            }
        }
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            SaveSettings();
            SettingsSaved?.Invoke();
            Close();
        }
        catch (InvalidOperationException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Unable to save settings: {ex.Message}");
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        // Restore the original theme if user cancels
        if (Application.Current is App app)
        {
            var savedTheme = App.NormalizeThemeName(Properties.Settings.Default.Theme);
            
            if (_currentTheme != null && savedTheme != _currentTheme)
            {
                app.ApplyThemePreview(savedTheme);
            }
        }

        // Restore the original language if user cancels
        var savedLanguage = Properties.Settings.Default.Language ?? "en";
        if (_currentLanguage != null && savedLanguage != _currentLanguage)
        {
            LocalizationManager.ChangeLanguage(savedLanguage);
        }

        Close();
    }

    private void SaveSettings()
    {
        var endpoints = new List<RunServerEndpoint>();
        foreach (var editor in _runServerEditors)
        {
            if (string.IsNullOrWhiteSpace(editor.Address.Text) ||
                !int.TryParse(editor.Port.Text, out var port) ||
                port < (editor.Endpoint.IsLocal ? 0 : 1) || port > 65535)
            {
                throw new InvalidOperationException("Each RunServer must have a valid address and port.");
            }

            var endpoint = editor.Endpoint.Clone();
            endpoint.Name = string.IsNullOrWhiteSpace(editor.Name.Text) ? "RunServer" : editor.Name.Text.Trim();
            endpoint.Address = editor.Address.Text.Trim();
            endpoint.Port = port;
            if (endpoints.Any(existing => string.Equals(existing.Address, endpoint.Address, StringComparison.OrdinalIgnoreCase) && existing.Port == endpoint.Port))
                throw new InvalidOperationException("RunServer addresses and ports must be unique.");
            endpoints.Add(endpoint);
        }

        if (!endpoints.Any(endpoint => endpoint.IsLocal))
            throw new InvalidOperationException("The local RunServer cannot be removed.");

        Properties.Settings.Default.RunServers = endpoints;
        // Save language preference
        if (_currentLanguage != null)
        {
            Properties.Settings.Default.Language = _currentLanguage;
        }

        // Save system sounds preference
        Properties.Settings.Default.PlaySystemSounds =
            PlaySystemSoundsCheckBox.IsChecked == true;
        if (AiProviderComboBox.SelectedItem is ComboBoxItem providerItem && providerItem.Tag is string provider)
            Properties.Settings.Default.AiProvider = provider;
        Properties.Settings.Default.AiModel = string.IsNullOrWhiteSpace(AiModelComboBox.Text)
            ? "llama3.2"
            : AiModelComboBox.Text.Trim();
        Properties.Settings.Default.OllamaEndpoint = string.IsNullOrWhiteSpace(OllamaEndpointTextBox.Text)
            ? "http://localhost:11434"
            : OllamaEndpointTextBox.Text.Trim();
        if (int.TryParse(AiMaxCompactionCyclesTextBox.Text, out var maxCompactionCycles))
        {
            Properties.Settings.Default.AiMaxCompactionCycles = Math.Clamp(maxCompactionCycles, 1, 100);
        }
        else
        {
            Properties.Settings.Default.AiMaxCompactionCycles = 100;
        }
        Properties.Settings.Default.Save();

        // Theme is already saved via ChangeTheme method
        // which calls SaveThemePreference internally
    }

    private async void RefreshModels_Click(object? sender, RoutedEventArgs e)
    {
        await RefreshModelsAsync();
    }

    private async Task RefreshModelsAsync()
    {
        if (!Uri.TryCreate(OllamaEndpointTextBox.Text?.Trim(), UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("http" or "https"))
        {
            return;
        }

        try
        {
            var provider = new OllamaProvider(_aiHttpClient, endpoint);
            var models = await provider.GetModelsAsync();
            var selectedModel = AiModelComboBox.Text?.Trim();
            AiModelComboBox.ItemsSource = models.Select(model => model.Id).ToArray();
            if (!string.IsNullOrWhiteSpace(selectedModel) && models.Any(model => model.Id == selectedModel))
            {
                AiModelComboBox.SelectedItem = selectedModel;
            }
            else if (models.Count > 0)
            {
                AiModelComboBox.SelectedItem = models[0].Id;
            }
        }
        catch
        {
            // Keep the configured model when discovery is unavailable.
        }
    }

    public void Window_KeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancel_Click(sender, new RoutedEventArgs());
        }
    }
}
