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
using System.Linq;
using XTMF2.GUI.Resources;

namespace XTMF2.GUI.Views;

public partial class SettingsWindow : Window
{
    private string? _currentTheme;
    private string? _currentLanguage;
    private string _currentAiProvider = "ollama";
    private string _currentAiModel = "llama3.2";
    private string _currentOllamaEndpoint = "http://localhost:11434";
    private string _currentAiAutonomyPolicy = "SuggestOnly";

    public SettingsWindow()
    {
        InitializeComponent();
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
            _currentAiAutonomyPolicy = Properties.Settings.Default.AiAutonomyPolicy;
            AiProviderComboBox.SelectedItem = AiProviderComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag?.ToString() == _currentAiProvider);
            AiModelTextBox.Text = _currentAiModel;
            OllamaEndpointTextBox.Text = _currentOllamaEndpoint;
            AiMaxCompactionCyclesTextBox.Text = Properties.Settings.Default.AiMaxCompactionCycles.ToString();
            AiControlEnabledCheckBox.IsChecked = Properties.Settings.Default.AiControlEnabled;
            AiControlPortTextBox.Text = Properties.Settings.Default.AiControlPort.ToString();
            AiControlCredentialKeyTextBox.Text = Properties.Settings.Default.AiControlCredentialKey;
            AiAutonomyPolicyComboBox.SelectedItem = AiAutonomyPolicyComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag?.ToString() == _currentAiAutonomyPolicy);
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
        SaveSettings();
        Close();
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
        Properties.Settings.Default.AiModel = string.IsNullOrWhiteSpace(AiModelTextBox.Text)
            ? "llama3.2"
            : AiModelTextBox.Text.Trim();
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
        Properties.Settings.Default.AiControlEnabled = AiControlEnabledCheckBox.IsChecked == true;
        if (int.TryParse(AiControlPortTextBox.Text, out var aiControlPort) && aiControlPort is > 0 and <= 65535)
            Properties.Settings.Default.AiControlPort = aiControlPort;
        Properties.Settings.Default.AiControlCredentialKey = AiControlCredentialKeyTextBox.Text?.Trim() ?? string.Empty;
        if (AiAutonomyPolicyComboBox.SelectedItem is ComboBoxItem policyItem && policyItem.Tag is string policy)
            Properties.Settings.Default.AiAutonomyPolicy = policy;

        Properties.Settings.Default.Save();

        // Theme is already saved via ChangeTheme method
        // which calls SaveThemePreference internally
    }

    public void Window_KeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancel_Click(sender, new RoutedEventArgs());
        }
    }
}
