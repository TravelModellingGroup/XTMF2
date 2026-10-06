using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using XTMF2.GUI.Properties;

namespace XTMF2.GUI.Views;

public sealed class RunServerChoice : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isCoordinator;
    private int _concurrentRuns = 1;

    public RunServerChoice(RunServerEndpoint endpoint, bool isSelected)
    {
        Endpoint = endpoint;
        _isSelected = isSelected;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public RunServerEndpoint Endpoint { get; }
    public string Name => Endpoint.Name;
    public bool CanConfigureRuns => IsSelected || _isCoordinator;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanConfigureRuns)));
        }
    }

    public void SetIsCoordinator(bool value)
    {
        if (_isCoordinator == value) return;
        _isCoordinator = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanConfigureRuns)));
    }

    public int ConcurrentRuns
    {
        get => _concurrentRuns;
        set
        {
            var boundedValue = Math.Clamp(value, 1, 32);
            if (_concurrentRuns == boundedValue) return;
            _concurrentRuns = boundedValue;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ConcurrentRuns)));
        }
    }
}

public partial class RunConfigurationDialog : Window, INotifyPropertyChanged
{
    public sealed record PathParameter(int NodeIndex, Guid NodeId, string Name, string Value);

    private string? _runName;
    private RunServerEndpoint? _selectedRunServer;
    private RunServerEndpoint? _selectedCoordinatorRunServer;
    private string? _selectedStartName;
    private bool _useMultipleRunServers;
    private readonly IReadOnlyList<PathParameter> _pathParameters;
    private readonly Dictionary<string, Dictionary<int, string>> _pathOverrides = new(StringComparer.Ordinal);

    public new event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<RunServerEndpoint> RunServers { get; }
    public IReadOnlyList<string> StartNames { get; }
    public ObservableCollection<RunServerChoice> RunServerChoices { get; } = new();
    public bool AllowMultipleRunServers { get; }
    public IReadOnlyList<RunServerEndpoint> SelectedRunServers => RunServerChoices
        .Where(choice => choice.IsSelected)
        .Select(choice => choice.Endpoint)
        .ToArray();
    public IReadOnlyDictionary<string, int> ConcurrentRunsByEndpoint => RunServerChoices
        .Where(choice => choice.IsSelected || choice.Endpoint.Id == SelectedCoordinatorRunServer?.Id)
        .DistinctBy(choice => choice.Endpoint.Id, StringComparer.Ordinal)
        .ToDictionary(choice => choice.Endpoint.Id, choice => choice.ConcurrentRuns, StringComparer.Ordinal);
    public RunServerEndpoint? SelectedCoordinatorRunServer
    {
        get => _selectedCoordinatorRunServer;
        set
        {
            if (_selectedCoordinatorRunServer == value) return;
            _selectedCoordinatorRunServer = value;
            foreach (var choice in RunServerChoices)
                choice.SetIsCoordinator(choice.Endpoint.Id == value?.Id);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedCoordinatorRunServer)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ConcurrentRunsByEndpoint)));
        }
    }
    public IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>> PathOverrides =>
        _pathOverrides.ToDictionary(pair => pair.Key, pair => (IReadOnlyDictionary<int, string>)pair.Value);

    public bool IsRunServerSelectionVisible => RunServers.Count > 1;
    public bool IsSingleRunServerSelectionVisible => IsRunServerSelectionVisible && !UseMultipleRunServers;
    public bool IsMultipleRunServerSelectionVisible => AllowMultipleRunServers && UseMultipleRunServers;
    public bool IsStartSelectionVisible => StartNames.Count > 1;
    public bool IsPathOverridesVisible => IsMultipleRunServerSelectionVisible && _pathParameters.Count > 0 && SelectedRunServers.Count > 0;

    public bool UseMultipleRunServers
    {
        get => _useMultipleRunServers;
        set
        {
            if (_useMultipleRunServers == value) return;
            _useMultipleRunServers = value && AllowMultipleRunServers;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UseMultipleRunServers)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSingleRunServerSelectionVisible)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMultipleRunServerSelectionVisible)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPathOverridesVisible)));
            RebuildPathEditors();
        }
    }
    public string SelectedRunServerSummary => SelectedRunServers.Count == 0
        ? "No RunServers selected"
        : $"Selected ({SelectedRunServers.Count}): {string.Join(", ", SelectedRunServers.Select(server => server.Name))}";

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
        for (var index = 0; index < runServers.Count; index++)
        {
            var choice = new RunServerChoice(runServers[index], index == 0);
            choice.PropertyChanged += OnRunServerChoiceChanged;
            RunServerChoices.Add(choice);
        }
        AllowMultipleRunServers = allowMultipleRunServers;
        _pathParameters = pathParameters ?? [];
        RunName = defaultRunName;
        SelectedRunServer = runServers.Count > 0 ? runServers[0] : null;
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
            (AllowMultipleRunServers && SelectedRunServers.Count == 0))
            return;

        WasCancelled = false;
        SavePathOverrides();
        Close();
    }

    private void OnRunServerChoiceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RunServerChoice.IsSelected))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedRunServers)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ConcurrentRunsByEndpoint)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPathOverridesVisible)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedRunServerSummary)));
            RebuildPathEditors();
        }
        else if (e.PropertyName == nameof(RunServerChoice.ConcurrentRuns))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ConcurrentRunsByEndpoint)));
        }
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

        foreach (var endpoint in SelectedRunServers)
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
        foreach (var endpoint in SelectedRunServers)
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