using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using XTMF2.GUI.Properties;

namespace XTMF2.GUI.Views;

public partial class RunConfigurationDialog : Window, INotifyPropertyChanged
{
    private string? _runName;
    private RunServerEndpoint? _selectedRunServer;
    private string? _selectedStartName;

    public new event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<RunServerEndpoint> RunServers { get; }
    public IReadOnlyList<string> StartNames { get; }

    public bool IsRunServerSelectionVisible => RunServers.Count > 1;
    public bool IsStartSelectionVisible => StartNames.Count > 1;

    public string? RunName
    {
        get => _runName;
        set
        {
            _runName = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RunName)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRunNameValid)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRunNameInvalid)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RunNameValidationMessage)));
        }
    }

    public string? RunNameValidationMessage => GetRunNameValidationMessage(RunName);

    public bool IsRunNameValid => RunNameValidationMessage is null;

    public bool IsRunNameInvalid => !IsRunNameValid;

    public RunServerEndpoint? SelectedRunServer
    {
        get => _selectedRunServer;
        set
        {
            _selectedRunServer = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedRunServer)));
        }
    }

    public string? SelectedStartName
    {
        get => _selectedStartName;
        set
        {
            _selectedStartName = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedStartName)));
        }
    }

    public bool WasCancelled { get; private set; } = true;

    public RunConfigurationDialog()
    {
        RunServers = [];
        StartNames = [];
        InitializeComponent();
        DataContext = this;
        RegisterEscapeClose();
    }

    public RunConfigurationDialog(string title, string defaultRunName,
                                  IReadOnlyList<RunServerEndpoint> runServers,
                                  IReadOnlyList<string> startNames)
    {
        RunServers = runServers;
        StartNames = startNames;
        RunName = defaultRunName;
        SelectedRunServer = runServers.Count > 0 ? runServers[0] : null;
        SelectedStartName = startNames.Count > 0 ? startNames[0] : null;
        InitializeComponent();
        Title = title;
        DataContext = this;
        RegisterEscapeClose();
        Opened += (_, _) =>
        {
            RunNameTextBox.Focus();
            RunNameTextBox.SelectAll();
        };
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
        if (!IsRunNameValid || SelectedRunServer is null || SelectedStartName is null)
            return;

        WasCancelled = false;
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        WasCancelled = true;
        Close();
    }

    private static string? GetRunNameValidationMessage(string? runName)
    {
        if (string.IsNullOrWhiteSpace(runName))
            return "A run name is required.";

        if (runName is "." or "..")
            return "A run name cannot be '.' or '..'.";

        if (runName.EndsWith('.') || runName != runName.TrimEnd())
            return "A run name cannot end with a space or period.";

        if (runName.Any(char.IsControl))
            return "A run name cannot contain control characters.";

        var invalidCharacters = Path.GetInvalidFileNameChars();
        foreach (var character in runName)
        {
            if (character is '/' or '\\')
                return "A run name cannot contain path separators.";

            if (Array.IndexOf(invalidCharacters, character) >= 0 ||
                character is ':' or '<' or '>' or '"' or '|' or '?' or '*')
                return "A run name contains a character that is invalid in a file name.";
        }

        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(runName);
        return IsWindowsDeviceName(fileNameWithoutExtension)
            ? "That run name is reserved by Windows and cannot be used."
            : null;
    }

    private static bool IsWindowsDeviceName(string name)
    {
        var normalizedName = name.TrimEnd('.', ' ').ToUpperInvariant();
        return normalizedName is "CON" or "PRN" or "AUX" or "NUL" ||
               (normalizedName.Length == 4 && normalizedName.StartsWith("COM", StringComparison.Ordinal) && normalizedName[3] is >= '1' and <= '9') ||
               (normalizedName.Length == 4 && normalizedName.StartsWith("LPT", StringComparison.Ordinal) && normalizedName[3] is >= '1' and <= '9');
    }
}