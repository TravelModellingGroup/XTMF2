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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Layout;
using Avalonia.Threading;
using XTMF2.GUI;
using XTMF2.GUI.Resources;
using XTMF2.GUI.ViewModels;
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
    private readonly RunController? _runController;
    private bool _migrationInProgress;
    private CancellationTokenSource? _migrationCancellation;
    private object? _defaultCancelButtonContent;
    private RunServersWindow? _runServersWindow;
    public event Action? SettingsSaved;

    public SettingsWindow()
        : this(null)
    {
    }

    public SettingsWindow(RunController? runController)
    {
        _runController = runController;
        InitializeComponent();
        _defaultCancelButtonContent = CancelButton.Content;
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
        DataRootTextBox.Text = Properties.Settings.Default.DataRoot;
            _currentAiProvider = Properties.Settings.Default.AiProvider;
            _currentAiModel = Properties.Settings.Default.AiModel;
            _currentOllamaEndpoint = Properties.Settings.Default.OllamaEndpoint;
            AiProviderComboBox.SelectedItem = AiProviderComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag?.ToString() == _currentAiProvider);
            OllamaEndpointTextBox.Text = _currentOllamaEndpoint;
            AiModelComboBox.SelectedItem = _currentAiModel;
            AiMaxCompactionCyclesTextBox.Text = Properties.Settings.Default.AiMaxCompactionCycles.ToString();
            _ = RefreshModelsAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _runServersWindow?.Close();
        base.OnClosed(e);
    }

    private async void ManageRunServers_Click(object? sender, RoutedEventArgs e)
    {
        if (_runServersWindow is not null && _runServersWindow.IsVisible)
        {
            _runServersWindow.Activate();
            return;
        }

        _runServersWindow = new RunServersWindow(_runController);
        _runServersWindow.RunServersSaved += () => _runController?.RefreshConfiguredRunServers();
        try
        {
            await _runServersWindow.ShowDialog(this);
        }
        finally
        {
            _runServersWindow = null;
        }
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

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        DataRootErrorTextBlock.IsVisible = false;
        try
        {
            var newDataRoot = ValidateDataRoot(DataRootTextBox.Text);
            var currentDataRoot = Properties.Settings.Default.DataRoot;
            var migrationMode = GetStorageMigrationMode();
            if (!AreSamePath(currentDataRoot, newDataRoot) && migrationMode is not null)
            {
                if (_runController?.RunsViewModel.Runs.Any(run => run.Status == RunStatus.Running) == true)
                    throw new InvalidOperationException("Finish or stop active runs before migrating the data directory.");

                if (Directory.EnumerateFileSystemEntries(newDataRoot).Any())
                {
                    var confirm = new ConfirmDialog("Destination Not Empty",
                        "The selected directory already contains data. Migration will merge the folder structures and replace files with matching paths; other destination files will be kept. Continue?");
                    await confirm.ShowDialog(this);
                    if (!confirm.Result)
                        return;
                }

                if (!await MigrateStorageRootAsync(currentDataRoot, newDataRoot, migrationMode.Value))
                    return;
            }

            SaveSettings(newDataRoot);
            SettingsSaved?.Invoke();
            Close();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            DataRootErrorTextBlock.Text = ex.Message;
            DataRootErrorTextBlock.IsVisible = true;
        }
    }

    private async Task<bool> MigrateStorageRootAsync(string sourceRoot, string destinationRoot,
        StorageRootMigrationMode mode)
    {
        _migrationInProgress = true;
        _migrationCancellation = new CancellationTokenSource();
        SaveButton.IsEnabled = false;
        CancelButton.Content = "Cancel";
        CancelButton.IsEnabled = mode == StorageRootMigrationMode.Copy;
        DataRootTextBox.IsEnabled = false;
        BrowseDataRootButton.IsEnabled = false;
        StorageMigrationModeComboBox.IsEnabled = false;
        StorageMigrationStatusTextBlock.IsVisible = true;
        StorageMigrationPercentTextBlock.IsVisible = true;
        StorageMigrationProgressBar.IsVisible = true;
        StorageMigrationProgressBar.IsIndeterminate = true;
        StorageMigrationStatusTextBlock.Text = "Starting migration...";

        var cancellationToken = _migrationCancellation.Token;
        var progress = new Progress<StorageRootMigrationProgress>(UpdateStorageMigrationProgress);
        try
        {
            await Task.Run(() => StorageRootMigration.Migrate(sourceRoot, destinationRoot, mode,
                progress, cancellationToken), cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            StorageMigrationStatusTextBlock.Text = "Migration cancelled. Source data was not removed.";
            return false;
        }
        finally
        {
            _migrationCancellation.Dispose();
            _migrationCancellation = null;
            _migrationInProgress = false;
            SaveButton.IsEnabled = true;
            CancelButton.Content = _defaultCancelButtonContent;
            CancelButton.IsEnabled = true;
            DataRootTextBox.IsEnabled = true;
            BrowseDataRootButton.IsEnabled = true;
            StorageMigrationModeComboBox.IsEnabled = true;
        }
    }

    private void UpdateStorageMigrationProgress(StorageRootMigrationProgress progress)
    {
        StorageMigrationStatusTextBlock.Text = progress.Status;
        StorageMigrationProgressBar.IsIndeterminate = progress.Percentage is null;
        if (progress.Percentage is double percentage)
        {
            StorageMigrationProgressBar.Value = percentage;
            StorageMigrationPercentTextBlock.Text = $"{percentage:0}%";
        }
    }

    private StorageRootMigrationMode? GetStorageMigrationMode()
    {
        if (StorageMigrationModeComboBox.SelectedItem is not ComboBoxItem item)
            return null;

        return item.Tag?.ToString() switch
        {
            "Copy" => StorageRootMigrationMode.Copy,
            "Move" => StorageRootMigrationMode.Move,
            _ => null,
        };
    }

    private static bool AreSamePath(string first, string second)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)), comparison);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        if (_migrationInProgress)
        {
            CancelButton.IsEnabled = false;
            StorageMigrationStatusTextBlock.Text = "Cancelling migration...";
            _migrationCancellation?.Cancel();
            return;
        }

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

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_migrationInProgress)
        {
            e.Cancel = true;
            return;
        }
        base.OnClosing(e);
    }

    private void SaveSettings(string dataRoot)
    {
        Properties.Settings.Default.DataRoot = dataRoot;

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

    private static string ValidateDataRoot(string? dataRoot)
    {
        if (string.IsNullOrWhiteSpace(dataRoot))
            throw new InvalidOperationException("Choose a data directory.");

        var fullPath = Path.GetFullPath(dataRoot.Trim());
        var probePath = Path.Combine(fullPath, $".xtmf2-write-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(fullPath);
            File.WriteAllText(probePath, string.Empty);
            File.Delete(probePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            try
            {
                File.Delete(probePath);
            }
            catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException)
            {
            }
            throw new InvalidOperationException($"The data directory cannot be written: {exception.Message}", exception);
        }

        return fullPath;
    }

    private async void BrowseDataRoot_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
            return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = "Choose XTMF data directory",
            AllowMultiple = false,
        });
        if (folders.Count > 0)
            DataRootTextBox.Text = folders[0].Path.LocalPath;
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

    public void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled == false && sender == this && e.Key == Key.Escape)
        {
            e.Handled = true;
            Cancel_Click(sender, new RoutedEventArgs());
        }
    }
}
