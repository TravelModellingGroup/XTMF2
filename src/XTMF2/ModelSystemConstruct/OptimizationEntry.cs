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
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using XTMF2.Bus.Optimization;

namespace XTMF2.ModelSystemConstruct;

/// <summary>
/// Controls whether the estimation fitness function should be minimised or maximised.
/// </summary>
public enum EstimationObjective
{
    /// <summary>Search for the parameter vector that produces the <em>smallest</em> fitness value.</summary>
    Minimize = 0,
    /// <summary>Search for the parameter vector that produces the <em>largest</em> fitness value.</summary>
    Maximize = 1,
}

/// <summary>
/// Represents a <see cref="Node"/> that has been nominated for <b>estimation</b>.
/// Estimation runs the model system with a search algorithm and computes a fitness value,
/// then updates the parameter to the best result at the end.
/// </summary>
public sealed class EstimationEntry : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private const string NodeIndexProperty = "NodeIndex";
    private const string MinProperty = "Min";
    private const string MaxProperty = "Max";
    private const string NullHypothesisProperty = "NullHypothesis";
    private const string IsEnabledProperty = "IsEnabled";

    /// <summary>The parameter node nominated for estimation.</summary>
    public Node Node { get; }

    private double _min;
    private double _max;
    private double _nullHypothesis;
    private bool _isEnabled = true;

    /// <summary>Minimum value the search algorithm may assign to this parameter.</summary>
    public double Min
    {
        get => _min;
        set { _min = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Min))); }
    }

    /// <summary>Maximum value the search algorithm may assign to this parameter.</summary>
    public double Max
    {
        get => _max;
        set { _max = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Max))); }
    }

    /// <summary>Starting (null-hypothesis) value for this parameter during estimation.</summary>
    public double NullHypothesis
    {
        get => _nullHypothesis;
        set { _nullHypothesis = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NullHypothesis))); }
    }

    /// <summary>When <c>false</c> this parameter is excluded from estimation runs.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled))); }
    }

    public EstimationEntry(Node node, double min, double max, double nullHypothesis, bool isEnabled = true)
    {
        Node = node;
        _min = min;
        _max = max;
        _nullHypothesis = nullHypothesis;
        _isEnabled = isEnabled;
    }

    internal void Save(Utf8JsonWriter writer, System.Collections.Generic.Dictionary<Node, int> nodeDictionary)
    {
        if (!nodeDictionary.TryGetValue(Node, out var idx)) return;
        writer.WriteStartObject();
        writer.WriteNumber(NodeIndexProperty, idx);
        writer.WriteNumber(MinProperty, _min);
        writer.WriteNumber(MaxProperty, _max);
        writer.WriteNumber(NullHypothesisProperty, _nullHypothesis);
        writer.WriteBoolean(IsEnabledProperty, _isEnabled);
        writer.WriteEndObject();
    }

    internal static EstimationEntry? Load(System.Collections.Generic.Dictionary<int, Node> nodes,
        ref Utf8JsonReader reader, ref string? error)
    {
        int nodeIndex = -1;
        double min = 0, max = 1, nullHypothesis = 0;
        bool isEnabled = true;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) continue;
            if (reader.ValueTextEquals(NodeIndexProperty))
            { reader.Read(); nodeIndex = reader.GetInt32(); }
            else if (reader.ValueTextEquals(MinProperty))
            { reader.Read(); min = reader.GetDouble(); }
            else if (reader.ValueTextEquals(MaxProperty))
            { reader.Read(); max = reader.GetDouble(); }
            else if (reader.ValueTextEquals(NullHypothesisProperty))
            { reader.Read(); nullHypothesis = reader.GetDouble(); }
            else if (reader.ValueTextEquals(IsEnabledProperty))
            { reader.Read(); isEnabled = reader.GetBoolean(); }
            else
            { reader.Read(); reader.Skip(); }
        }
        if (nodeIndex < 0 || !nodes.TryGetValue(nodeIndex, out var node))
            return null; // node was removed; silently skip
        return new EstimationEntry(node, min, max, nullHypothesis, isEnabled);
    }
}

/// <summary>
/// Represents a <see cref="Node"/> that has been nominated for <b>calibration</b>.
/// Calibration runs the model system, computes a ratio of
/// <c>TargetOutputNode / ModelOutputNode</c> each iteration, and scales
/// the parameter proportionally until convergence.
/// </summary>
public sealed class CalibrationEntry : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private const string NodeIndexProperty            = "NodeIndex";
    private const string ModelOutputNodeProperty      = "ModelOutputNode";
    private const string TargetOutputNodeProperty     = "TargetOutputNode";
    private const string MinProperty                  = "Min";
    private const string MaxProperty                  = "Max";
    private const string IsEnabledProperty            = "IsEnabled";
    private const string ErrorToleranceProperty       = "ErrorTolerance";
    private const string StepSizeProperty             = "StepSize";
    private const string AlgorithmProperty            = "Algorithm";

    /// <summary>The parameter node nominated for calibration.</summary>
    public Node Node { get; }

    private Node?  _modelOutputNode;
    private Node?  _targetOutputNode;
    private double _min;
    private double _max;
    private bool   _isEnabled = true;
    private double _errorTolerance = 1e-4;
    private double _stepSize = 1.0;
    private CalibrationAlgorithmBase _algorithm = CalibrationAlgorithmBase.Default;

    /// <summary>
    /// The node (implementing <c>IFunction&lt;float&gt;</c> or
    /// <c>IFunction&lt;double&gt;</c>) that provides the model's computed
    /// output for this calibration parameter.  May be <c>null</c> until
    /// the user assigns one.
    /// </summary>
    public Node? ModelOutputNode
    {
        get => _modelOutputNode;
        set { _modelOutputNode = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ModelOutputNode))); }
    }

    /// <summary>
    /// The node (implementing <c>IFunction&lt;float&gt;</c> or
    /// <c>IFunction&lt;double&gt;</c>) that provides the observed/expected
    /// target value for this calibration parameter.  May be <c>null</c> until
    /// the user assigns one.
    /// </summary>
    public Node? TargetOutputNode
    {
        get => _targetOutputNode;
        set { _targetOutputNode = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TargetOutputNode))); }
    }

    /// <summary>Minimum allowed value for this parameter during calibration.</summary>
    public double Min
    {
        get => _min;
        set { _min = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Min))); }
    }

    /// <summary>Maximum allowed value for this parameter during calibration.</summary>
    public double Max
    {
        get => _max;
        set { _max = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Max))); }
    }

    /// <summary>When <c>false</c> this parameter is excluded from calibration runs.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled))); }
    }

    /// <summary>
    /// Convergence tolerance for this parameter.  If <c>|ratio - 1| &lt;= ErrorTolerance</c>
    /// the parameter is considered converged for that iteration and will not be updated.
    /// When all enabled parameters are within their individual tolerance the calibration
    /// loop terminates early.  Default is <c>1e-4</c>.
    /// </summary>
    public double ErrorTolerance
    {
        get => _errorTolerance;
        set { _errorTolerance = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ErrorTolerance))); }
    }

    /// <summary>
    /// Fraction of the calculated calibration correction applied each iteration.
    /// Default is <c>1.0</c>.
    /// </summary>
    public double StepSize
    {
        get => _stepSize;
        set { _stepSize = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StepSize))); }
    }

    /// <summary>
    /// The update rule applied to this parameter each calibration iteration.
    /// Default is <see cref="LogOddsAlgorithm"/>.
    /// </summary>
    public CalibrationAlgorithmBase Algorithm
    {
        get => _algorithm;
        set { _algorithm = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Algorithm))); }
    }

    public CalibrationEntry(Node node, double min, double max,
        Node? modelOutputNode = null, Node? targetOutputNode = null, bool isEnabled = true,
        double errorTolerance = 1e-4, CalibrationAlgorithmBase? algorithm = null,
        double stepSize = 1.0)
    {
        Node               = node;
        _modelOutputNode   = modelOutputNode;
        _targetOutputNode  = targetOutputNode;
        _min               = min;
        _max               = max;
        _isEnabled         = isEnabled;
        _errorTolerance    = errorTolerance;
        _algorithm         = algorithm ?? CalibrationAlgorithmBase.Default;
        _stepSize          = stepSize;
    }

    internal void Save(Utf8JsonWriter writer, Dictionary<Node, int> nodeDictionary)
    {
        if (!nodeDictionary.TryGetValue(Node, out var idx)) return;
        writer.WriteStartObject();
        writer.WriteNumber(NodeIndexProperty, idx);
        if (_modelOutputNode is not null && nodeDictionary.TryGetValue(_modelOutputNode, out var moIdx))
            writer.WriteNumber(ModelOutputNodeProperty, moIdx);
        if (_targetOutputNode is not null && nodeDictionary.TryGetValue(_targetOutputNode, out var toIdx))
            writer.WriteNumber(TargetOutputNodeProperty, toIdx);
        writer.WriteNumber(MinProperty,        _min);
        writer.WriteNumber(MaxProperty,        _max);
        writer.WriteBoolean(IsEnabledProperty, _isEnabled);
        writer.WriteNumber(ErrorToleranceProperty, _errorTolerance);
        writer.WriteNumber(StepSizeProperty, _stepSize);
        writer.WritePropertyName(AlgorithmProperty);
        _algorithm.Save(writer);
        writer.WriteEndObject();
    }

    internal static CalibrationEntry? Load(Dictionary<int, Node> nodes,
        ref Utf8JsonReader reader, ref string? error)
    {
        int    nodeIndex            = -1;
        int    modelOutputIndex     = -1;
        int    targetOutputIndex    = -1;
        double min = 0, max = 1;
        bool   isEnabled            = true;
        double errorTolerance       = 1e-4;
        double stepSize             = 1.0;
        CalibrationAlgorithmBase? algorithm = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) continue;
            if      (reader.ValueTextEquals(NodeIndexProperty))         { reader.Read(); nodeIndex         = reader.GetInt32();   }
            else if (reader.ValueTextEquals(ModelOutputNodeProperty))   { reader.Read(); modelOutputIndex  = reader.GetInt32();   }
            else if (reader.ValueTextEquals(TargetOutputNodeProperty))  { reader.Read(); targetOutputIndex = reader.GetInt32();   }
            else if (reader.ValueTextEquals(MinProperty))               { reader.Read(); min               = reader.GetDouble(); }
            else if (reader.ValueTextEquals(MaxProperty))               { reader.Read(); max               = reader.GetDouble(); }
            else if (reader.ValueTextEquals(IsEnabledProperty))         { reader.Read(); isEnabled         = reader.GetBoolean();}
            else if (reader.ValueTextEquals(ErrorToleranceProperty))    { reader.Read(); errorTolerance    = reader.GetDouble(); }
            else if (reader.ValueTextEquals(StepSizeProperty))          { reader.Read(); stepSize          = reader.GetDouble(); }
            else if (reader.ValueTextEquals(AlgorithmProperty))
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.String)
                {
                    // Legacy format: enum name as string
                    var legacyId = reader.GetString();
                    algorithm = legacyId switch
                    {
                        "ProportionalScaling" => new ProportionalScalingAlgorithm(),
                        "LogOdds" => new LogOddsAlgorithm(),
                        _ => CalibrationAlgorithmBase.Default
                    };
                }
                else if (reader.TokenType == JsonTokenType.StartObject)
                {
                    algorithm = CalibrationAlgorithmBase.Load(ref reader);
                }
                else { reader.Skip(); }
            }
            else                                                         { reader.Read(); reader.Skip(); }
        }
        if (nodeIndex < 0 || !nodes.TryGetValue(nodeIndex, out var node)) return null;
        nodes.TryGetValue(modelOutputIndex,  out var modelOutputNode);
        nodes.TryGetValue(targetOutputIndex, out var targetOutputNode);
        return new CalibrationEntry(node, min, max, modelOutputNode, targetOutputNode, isEnabled, errorTolerance, algorithm, stepSize);
    }
}

// ── Groups ───────────────────────────────────────────────────────────────────

/// <summary>
/// A named group of <see cref="EstimationEntry"/> items.  The fitness
/// function for an estimation run is a single model-system-level node
/// (stored on <see cref="ModelSystem.EstimationFitnessNode"/>), shared
/// across all groups.
/// </summary>
public sealed class EstimationGroup : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private const string NameProperty       = "Name";
    private const string IsEnabledProperty  = "IsEnabled";
    private const string ParametersProperty = "Parameters";

    private string _name;
    private bool   _isEnabled = true;

    /// <summary>Display name of this estimation group.</summary>
    public string Name
    {
        get => _name;
        set { _name = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name))); }
    }

    /// <summary>When <c>false</c> this group is excluded from estimation runs.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled))); }
    }

    /// <summary>Parameters belonging to this estimation group.</summary>
    public ObservableCollection<EstimationEntry> Parameters { get; } = new();

    public EstimationGroup(string name) { _name = name; }

    internal void Save(Utf8JsonWriter writer, Dictionary<Node, int> nodeDictionary)
    {
        writer.WriteStartObject();
        writer.WriteString(NameProperty, _name);
        writer.WriteBoolean(IsEnabledProperty, _isEnabled);
        writer.WritePropertyName(ParametersProperty);
        writer.WriteStartArray();
        foreach (var entry in Parameters)
            entry.Save(writer, nodeDictionary);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    internal static EstimationGroup? Load(Dictionary<int, Node> nodes,
        ref Utf8JsonReader reader, ref string? error)
    {
        string name      = "Group";
        bool   isEnabled = true;
        var    parameters = new List<EstimationEntry>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) continue;
            if (reader.ValueTextEquals(NameProperty))
            { reader.Read(); name = reader.GetString() ?? "Group"; }
            else if (reader.ValueTextEquals(IsEnabledProperty))
            { reader.Read(); isEnabled = reader.GetBoolean(); }
            else if (reader.ValueTextEquals(ParametersProperty))
            {
                if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray) break;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    if (reader.TokenType == JsonTokenType.StartObject)
                    {
                        var e = EstimationEntry.Load(nodes, ref reader, ref error);
                        if (e is not null) parameters.Add(e);
                    }
            }
            else { reader.Read(); reader.Skip(); }
        }
        var group = new EstimationGroup(name) { IsEnabled = isEnabled };
        foreach (var p in parameters) group.Parameters.Add(p);
        return group;
    }
}

/// <summary>
/// A named group of <see cref="CalibrationEntry"/> items.  Each entry
/// carries its own <see cref="CalibrationEntry.TargetNode"/>.
/// </summary>
public sealed class CalibrationGroup : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private const string NameProperty            = "Name";
    private const string IsEnabledProperty       = "IsEnabled";
    private const string ParametersProperty      = "Parameters";
    private const string DefaultAlgorithmProperty = "DefaultAlgorithm";

    private string _name;
    private bool   _isEnabled = true;
    private CalibrationAlgorithmBase _defaultAlgorithm = CalibrationAlgorithmBase.Default;

    /// <summary>Display name of this calibration group.</summary>
    public string Name
    {
        get => _name;
        set { _name = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name))); }
    }

    /// <summary>When <c>false</c> this group is excluded from calibration runs.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled))); }
    }

    /// <summary>
    /// The algorithm that will be assigned to new <see cref="CalibrationEntry"/> items
    /// added to this group.  Changing this does not retroactively update existing entries.
    /// </summary>
    public CalibrationAlgorithmBase DefaultAlgorithm
    {
        get => _defaultAlgorithm;
        set { _defaultAlgorithm = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DefaultAlgorithm))); }
    }

    /// <summary>Parameters belonging to this calibration group.</summary>
    public ObservableCollection<CalibrationEntry> Parameters { get; } = new();

    public CalibrationGroup(string name) { _name = name; }

    internal void Save(Utf8JsonWriter writer, Dictionary<Node, int> nodeDictionary)
    {
        writer.WriteStartObject();
        writer.WriteString(NameProperty, _name);
        writer.WriteBoolean(IsEnabledProperty, _isEnabled);
        writer.WritePropertyName(DefaultAlgorithmProperty);
        _defaultAlgorithm.Save(writer);
        writer.WritePropertyName(ParametersProperty);
        writer.WriteStartArray();
        foreach (var entry in Parameters)
            entry.Save(writer, nodeDictionary);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    internal static CalibrationGroup? Load(Dictionary<int, Node> nodes,
        ref Utf8JsonReader reader, ref string? error)
    {
        string name      = "Group";
        bool   isEnabled = true;
        CalibrationAlgorithmBase? defaultAlgorithm = null;
        var    parameters = new List<CalibrationEntry>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) continue;
            if (reader.ValueTextEquals(NameProperty))
            { reader.Read(); name = reader.GetString() ?? "Group"; }
            else if (reader.ValueTextEquals(IsEnabledProperty))
            { reader.Read(); isEnabled = reader.GetBoolean(); }
            else if (reader.ValueTextEquals(DefaultAlgorithmProperty))
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.String)
                {
                    var legacyId = reader.GetString();
                    defaultAlgorithm = legacyId switch
                    {
                        "ProportionalScaling" => new ProportionalScalingAlgorithm(),
                        "LogOdds" => new LogOddsAlgorithm(),
                        _ => CalibrationAlgorithmBase.Default
                    };
                }
                else if (reader.TokenType == JsonTokenType.StartObject)
                    defaultAlgorithm = CalibrationAlgorithmBase.Load(ref reader);
                else
                    reader.Skip();
            }
            else if (reader.ValueTextEquals(ParametersProperty))
            {
                if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray) break;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    if (reader.TokenType == JsonTokenType.StartObject)
                    {
                        var e = CalibrationEntry.Load(nodes, ref reader, ref error);
                        if (e is not null) parameters.Add(e);
                    }
            }
            else { reader.Read(); reader.Skip(); }
        }
        var group = new CalibrationGroup(name) { IsEnabled = isEnabled, DefaultAlgorithm = defaultAlgorithm ?? CalibrationAlgorithmBase.Default };
        foreach (var p in parameters) group.Parameters.Add(p);
        return group;
    }
}
