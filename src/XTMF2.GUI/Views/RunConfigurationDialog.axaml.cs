using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
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
    public sealed record PathParameter(int NodeIndex, Guid NodeId, string Name, string Value);

    private string? _runName;
    private RunServerEndpoint? _selectedRunServer;
    private RunServerEndpoint? _selectedCoordinatorRunServer;
    private string? _selectedStartName;
    private readonly List<RunServerEndpoint> _selectedRunServers = new();
    private readonly IReadOnlyList<PathParameter> _pathParameters;
    private readonly Dictionary<string, Dictionary<int, string>> _pathOverrides = new(StringComparer.Ordinal);

    public new event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<RunServerEndpoint> RunServers { get; }
    public IReadOnlyList<string> StartNames { get; }
    public bool AllowMultipleRunServers { get; }
    public IReadOnlyList<RunServerEndpoint> SelectedRunServers => _selectedRunServers;
    public RunServerEndpoint? SelectedCoordinatorRunServer
    {
        get => _selectedCoordinatorRunServer;
        set
        {
            if (_selectedCoordinatorRunServer == value) return;
            _selectedCoordinatorRunServer = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedCoordinatorRunServer)));
        }
    }
    public IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>> PathOverrides =>
        _pathOverrides.ToDictionary(pair => pair.Key, pair => (IReadOnlyDictionary<int, string>)pair.Value);

    public bool IsRunServerSelectionVisible => RunServers.Count > 1;
    public bool IsSingleRunServerSelectionVisible => IsRunServerSelectionVisible && !AllowMultipleRunServers;
    public bool IsStartSelectionVisible => StartNames.Count > 1;
    public bool IsPathOverridesVisible => AllowMultipleRunServers && _pathParameters.Count > 0 && _selectedRunServers.Count > 0;
    public string SelectedRunServerSummary => _selectedRunServers.Count == 0
        ? "No RunServers selected"
        : $"Selected ({_selectedRunServers.Count}): {string.Join(", ", _selectedRunServers.Select(server => server.Name))}";

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
        _pathParameters = [];
        AllowMultipleRunServers = false;
        InitializeComponent();
        DataContext = this;
        RebuildPathEditors();
        RegisterEscapeClose();
    }

    public RunConfigurationDialog(string title, string defaultRunName,
                                  IReadOnlyList<RunServerEndpoint> runServers,
                                  IReadOnlyList<string> startNames,
                                  bool allowMultipleRunServers = false,
                                  IReadOnlyList<PathParameter>? pathParameters = null)
    {
        RunServers = runServers;
        StartNames = startNames;
        AllowMultipleRunServers = allowMultipleRunServers;
        _pathParameters = pathParameters ?? [];
        RunName = defaultRunName;
        SelectedRunServer = runServers.Count > 0 ? runServers[0] : null;
        _selectedRunServers.AddRange(runServers.Count > 0 ? [runServers[0]] : []);
        SelectedCoordinatorRunServer = runServers.Count > 0 ? runServers[0] : null;
        InitializePathOverrides();
        SelectedStartName = startNames.Count > 0 ? startNames[0] : null;
        InitializeComponent();
        Title = title;
        DataContext = this;
        RebuildPathEditors();
        RegisterEscapeClose();
        Opened += (_, _) =>
        {
            if (AllowMultipleRunServers && RunServers.Count > 0 &&
                RunServerListBox.SelectedItems is { Count: 0 } selectedItems)
                selectedItems.Add(RunServers[0]);
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
        if (!IsRunNameValid || SelectedRunServer is null || SelectedStartName is null ||
            (AllowMultipleRunServers && _selectedRunServers.Count == 0))
            return;

        WasCancelled = false;
        SavePathOverrides();
        Close();
    }

    private void RunServerSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _selectedRunServers.Clear();
        if (RunServerListBox.SelectedItems is { } selectedItems)
        {
            foreach (var item in selectedItems.OfType<RunServerEndpoint>())
                _selectedRunServers.Add(item);
        }
        SelectedRunServer = _selectedRunServers.FirstOrDefault();
        if (SelectedCoordinatorRunServer is null || !_selectedRunServers.Contains(SelectedCoordinatorRunServer))
            SelectedCoordinatorRunServer = _selectedRunServers.FirstOrDefault();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPathOverridesVisible)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedRunServerSummary)));
        RebuildPathEditors();
    }

    private void InitializePathOverrides()
    {
        foreach (var endpoint in RunServers)
        {
            _pathOverrides[endpoint.Id] = _pathParameters.ToDictionary(parameter => parameter.NodeIndex,
                parameter => endpoint.BasicParameterOverrides.TryGetValue(parameter.NodeId.ToString("D"), out var value)
                    ? value
                    : parameter.Value);
        }
    }

    private void SavePathOverrides()
    {
        if (_pathParameters.Count == 0)
            return;

        foreach (var endpoint in _selectedRunServers)
        {
            endpoint.BasicParameterOverrides.Clear();
            foreach (var parameter in _pathParameters)
                endpoint.BasicParameterOverrides[parameter.NodeId.ToString("D")] =
                    _pathOverrides[endpoint.Id][parameter.NodeIndex];

            var configuredEndpoint = Settings.Default.RunServers
                .FirstOrDefault(candidate => candidate.Id == endpoint.Id);
            if (configuredEndpoint is null)
                continue;
            configuredEndpoint.BasicParameterOverrides =
                new Dictionary<string, string>(endpoint.BasicParameterOverrides, StringComparer.OrdinalIgnoreCase);
        }
        Settings.Default.Save();
    }

    private void RebuildPathEditors()
    {
        if (!AllowMultipleRunServers || _pathParameters.Count == 0)
            return;

        PathOverridesPanel.Children.Clear();
        PathOverridesPanel.Children.Add(new TextBlock
        {
            Text = "Worker path values",
            FontSize = 14,
            Margin = new Avalonia.Thickness(0, 0, 0, 4)
        });
        foreach (var endpoint in _selectedRunServers)
        {
            var values = _pathOverrides[endpoint.Id];
            var editor = new StackPanel { Spacing = 4 };
            editor.Children.Add(new TextBlock { Text = endpoint.Name, FontWeight = FontWeight.Bold });
            foreach (var parameter in _pathParameters)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
                row.Children.Add(new TextBlock { Text = parameter.Name, VerticalAlignment = VerticalAlignment.Center });
                var textBox = new TextBox { Text = values[parameter.NodeIndex], HorizontalAlignment = HorizontalAlignment.Stretch };
                Grid.SetColumn(textBox, 1);
                textBox.TextChanged += (_, _) => values[parameter.NodeIndex] = textBox.Text ?? string.Empty;
                row.Children.Add(textBox);
                editor.Children.Add(row);
            }
            PathOverridesPanel.Children.Add(editor);
        }
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