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
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace XTMF2.Bus.Optimization;

/// <summary>
/// Abstract base class that pairs an <see cref="IEstimationAlgorithm"/> implementation
/// with its configurable hyperparameters.  Each subclass owns one algorithm type
/// and is responsible for constructing and initialising it.
/// </summary>
public abstract class EstimationAlgorithmConfig : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Stable identifier used as the JSON type-discriminator.</summary>
    public abstract string AlgorithmId { get; }

    /// <summary>Human-readable name for GUI display.</summary>
    public abstract string DisplayName { get; }

    /// <summary>Short description of the algorithm shown in the configuration dialog.</summary>
    public abstract string AlgorithmDescription { get; }

    /// <summary>Returns the current hyperparameters as editable descriptors for the configuration dialog.</summary>
    public abstract IReadOnlyList<AlgorithmParameterDescriptor> GetParameters();

    /// <summary>
    /// Validates and applies the values from <paramref name="parameters"/> to this config.
    /// Returns an error message on the first validation failure, or <c>null</c> on success.
    /// </summary>
    public abstract string? ApplyParameters(IReadOnlyList<AlgorithmParameterDescriptor> parameters);

    /// <summary>
    /// Creates and fully initialises an <see cref="IEstimationAlgorithm"/> ready for a Run call.
    /// </summary>
    public abstract IEstimationAlgorithm CreateAlgorithm(
        int dimensions, double[] lower, double[] upper, double[] initial,
        bool isMaximize = false);

    /// <summary>Writes algorithm-specific properties into an already-open JSON object.</summary>
    protected abstract void SaveProperties(Utf8JsonWriter writer);

    /// <summary>Reads and applies one algorithm-specific property.</summary>
    protected abstract void LoadProperty(string propertyName, ref Utf8JsonReader reader);

    private const string TypeProperty = "Type";

    /// <summary>Serialises this config as a self-describing JSON object.</summary>
    internal void Save(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString(TypeProperty, AlgorithmId);
        SaveProperties(writer);
        writer.WriteEndObject();
    }

    /// <summary>
    /// Deserialises an <see cref="EstimationAlgorithmConfig"/> from a JSON object.
    /// Returns <c>null</c> on unknown type; callers fall back to <see cref="Default"/>.
    /// </summary>
    internal static EstimationAlgorithmConfig? Load(ref Utf8JsonReader reader)
    {
        // Peek for type discriminator.
        string? typeId = null;
        var peek = reader;
        while (peek.Read() && peek.TokenType != JsonTokenType.EndObject)
        {
            if (peek.TokenType == JsonTokenType.PropertyName
                && peek.ValueTextEquals(TypeProperty))
            {
                peek.Read();
                typeId = peek.GetString();
                break;
            }
            peek.Read();
            peek.Skip();
        }

        if (typeId is null) { SkipObject(ref reader); return null; }

        EstimationAlgorithmConfig? cfg = typeId switch
        {
            NelderMeadConfig.Id           => new NelderMeadConfig(),
            ParticleSwarmConfig.Id        => new ParticleSwarmConfig(),
            GeneticAlgorithmConfig.Id     => new GeneticAlgorithmConfig(),
            StochasticGradientConfig.Id   => new StochasticGradientConfig(),
            _ => null
        };

        if (cfg is null) { SkipObject(ref reader); return null; }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) continue;
            var prop = reader.GetString() ?? string.Empty;
            reader.Read();
            if (prop == TypeProperty) continue;
            cfg.LoadProperty(prop, ref reader);
        }
        return cfg;
    }

    private static void SkipObject(ref Utf8JsonReader reader)
    {
        int depth = 1;
        while (depth > 0 && reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject) depth++;
            else if (reader.TokenType == JsonTokenType.EndObject) depth--;
        }
    }

    /// <summary>All registered estimation algorithm configs, for GUI pickers.</summary>
    public static IReadOnlyList<EstimationAlgorithmConfig> AvailableAlgorithms { get; } =
        [new NelderMeadConfig(), new ParticleSwarmConfig(), new GeneticAlgorithmConfig(), new StochasticGradientConfig()];

    /// <summary>Default algorithm config used when no config is stored in the file.</summary>
    public static EstimationAlgorithmConfig Default => new NelderMeadConfig();

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is EstimationAlgorithmConfig other && AlgorithmId == other.AlgorithmId;

    /// <inheritdoc/>
    public override int GetHashCode() => AlgorithmId.GetHashCode();

    /// <inheritdoc/>
    public override string ToString() => DisplayName;
}

// ── Nelder-Mead ─────────────────────────────────────────────────────────────

/// <summary>Configuration for the <see cref="NelderMeadAlgorithm"/>.</summary>
public sealed class NelderMeadConfig : EstimationAlgorithmConfig
{
    internal const string Id = "NelderMead";

    public override string AlgorithmId => Id;
    public override string DisplayName  => "Nelder-Mead Simplex";
    public override string AlgorithmDescription =>
        "The Nelder-Mead simplex method is a gradient-free local search. Good for low-dimensional smooth problems.";

    private int    _maxIterations        = 500;
    private double _convergenceTolerance = 1e-6;

    /// <summary>Maximum number of simplex iterations before convergence is declared.</summary>
    public int MaxIterations
    {
        get => _maxIterations;
        set { _maxIterations = value; Raise(nameof(MaxIterations)); }
    }

    /// <summary>Convergence threshold: stop when the fitness range across simplex vertices
    /// is smaller than this value.</summary>
    public double ConvergenceTolerance
    {
        get => _convergenceTolerance;
        set { _convergenceTolerance = value; Raise(nameof(ConvergenceTolerance)); }
    }

    public override IReadOnlyList<AlgorithmParameterDescriptor> GetParameters() =>
    [
        new() { Key = "MaxIterations",        Label = "Max Iterations",
                Hint  = "Stop after this many simplex iterations",
                Value = _maxIterations.ToString(CultureInfo.InvariantCulture) },
        new() { Key = "ConvergenceTolerance", Label = "Convergence Tolerance",
                Hint  = "Stop when fitness range across vertices falls below this",
                Value = _convergenceTolerance.ToString("G", CultureInfo.InvariantCulture) },
    ];

    public override string? ApplyParameters(IReadOnlyList<AlgorithmParameterDescriptor> parameters)
    {
        var inv = CultureInfo.InvariantCulture;
        int mi = _maxIterations; double tol = _convergenceTolerance;
        foreach (var p in parameters)
        {
            switch (p.Key)
            {
                case "MaxIterations":
                    if (!int.TryParse(p.Value, NumberStyles.Integer, inv, out mi) || mi < 1)
                        return "Max Iterations must be a positive integer.";
                    break;
                case "ConvergenceTolerance":
                    if (!double.TryParse(p.Value, NumberStyles.Float, inv, out tol) || tol < 0)
                        return "Convergence Tolerance must be a non-negative number.";
                    break;
            }
        }
        _maxIterations = mi;
        _convergenceTolerance = tol;
        return null;
    }

    public override IEstimationAlgorithm CreateAlgorithm(
        int dimensions, double[] lower, double[] upper, double[] initial,
        bool isMaximize = false)
    {
        var alg = new NelderMeadAlgorithm();
        alg.Initialize(dimensions, lower, upper, initial, _maxIterations, _convergenceTolerance, isMaximize);
        return alg;
    }

    protected override void SaveProperties(Utf8JsonWriter writer)
    {
        writer.WriteNumber("MaxIterations", _maxIterations);
        writer.WriteNumber("ConvergenceTolerance", _convergenceTolerance);
    }

    protected override void LoadProperty(string propertyName, ref Utf8JsonReader reader)
    {
        switch (propertyName)
        {
            case "MaxIterations":        _maxIterations        = reader.GetInt32();  break;
            case "ConvergenceTolerance": _convergenceTolerance = reader.GetDouble(); break;
            default: reader.Skip(); break;
        }
    }
}

// ── Particle Swarm ───────────────────────────────────────────────────────────

/// <summary>Configuration for the <see cref="ParticleSwarmAlgorithm"/>.</summary>
public sealed class ParticleSwarmConfig : EstimationAlgorithmConfig
{
    internal const string Id = "ParticleSwarm";

    public override string AlgorithmId => Id;
    public override string DisplayName  => "Particle Swarm Optimisation";
    public override string AlgorithmDescription =>
        "Particle Swarm Optimisation (PSO) is a population-based global search. The swarm explores the parameter space via velocity updates.";

    private int    _swarmSize            = 30;
    private int    _maxIterations        = 300;
    private double _inertia              = 0.72;
    private double _cognitiveCoeff       = 1.49;
    private double _socialCoeff          = 1.49;
    private double _convergenceTolerance = 1e-6;
    private int    _noImprovementLimit   = 5;

    /// <summary>Number of particles in the swarm.</summary>
    public int SwarmSize
    {
        get => _swarmSize;
        set { _swarmSize = Math.Max(2, value); Raise(nameof(SwarmSize)); }
    }

    /// <summary>Maximum number of iterations before stopping.</summary>
    public int MaxIterations
    {
        get => _maxIterations;
        set { _maxIterations = value; Raise(nameof(MaxIterations)); }
    }

    /// <summary>Inertia weight (ω) — controls momentum of each particle.</summary>
    public double Inertia
    {
        get => _inertia;
        set { _inertia = value; Raise(nameof(Inertia)); }
    }

    /// <summary>Cognitive coefficient (c1) — attraction toward personal best.</summary>
    public double CognitiveCoeff
    {
        get => _cognitiveCoeff;
        set { _cognitiveCoeff = value; Raise(nameof(CognitiveCoeff)); }
    }

    /// <summary>Social coefficient (c2) — attraction toward global best.</summary>
    public double SocialCoeff
    {
        get => _socialCoeff;
        set { _socialCoeff = value; Raise(nameof(SocialCoeff)); }
    }

    /// <summary>Convergence threshold: stop when the global-best improvement per
    /// iteration falls below this value.</summary>
    public double ConvergenceTolerance
    {
        get => _convergenceTolerance;
        set { _convergenceTolerance = value; Raise(nameof(ConvergenceTolerance)); }
    }

    /// <summary>Number of consecutive iterations without improvement before
    /// stopping. Set to 0 to disable stagnation termination (default 5).</summary>
    public int NoImprovementLimit
    {
        get => _noImprovementLimit;
        set { _noImprovementLimit = Math.Max(0, value); Raise(nameof(NoImprovementLimit)); }
    }

    public override IReadOnlyList<AlgorithmParameterDescriptor> GetParameters() =>
    [
        new() { Key = "SwarmSize",            Label = "Swarm Size",
                Hint  = "Number of particles (minimum 2)",
                Value = _swarmSize.ToString(CultureInfo.InvariantCulture) },
        new() { Key = "MaxIterations",        Label = "Max Iterations",
                Hint  = "Stop after this many iterations",
                Value = _maxIterations.ToString(CultureInfo.InvariantCulture) },
        new() { Key = "Inertia",              Label = "Inertia (\u03C9)",
                Hint  = "Momentum factor \u2014 controls how much velocity is retained",
                Value = _inertia.ToString("G", CultureInfo.InvariantCulture) },
        new() { Key = "CognitiveCoeff",       Label = "Cognitive Coefficient (c\u2081)",
                Hint  = "Attraction toward each particle\u2019s personal best",
                Value = _cognitiveCoeff.ToString("G", CultureInfo.InvariantCulture) },
        new() { Key = "SocialCoeff",          Label = "Social Coefficient (c\u2082)",
                Hint  = "Attraction toward the global best position",
                Value = _socialCoeff.ToString("G", CultureInfo.InvariantCulture) },
        new() { Key = "ConvergenceTolerance", Label = "Convergence Tolerance",
                Hint  = "Stop when per-iteration improvement falls below this",
                Value = _convergenceTolerance.ToString("G", CultureInfo.InvariantCulture) },
        new() { Key = "NoImprovementLimit",   Label = "No-Improvement Limit",
                Hint  = "Stop after this many consecutive iterations with no improvement (0 = disabled)",
                Value = _noImprovementLimit.ToString(CultureInfo.InvariantCulture) },
    ];

    public override string? ApplyParameters(IReadOnlyList<AlgorithmParameterDescriptor> parameters)
    {
        var inv = CultureInfo.InvariantCulture;
        int swarm = _swarmSize, maxIter = _maxIterations, noImprove = _noImprovementLimit;
        double inertia = _inertia, cog = _cognitiveCoeff, soc = _socialCoeff, tol = _convergenceTolerance;
        foreach (var p in parameters)
        {
            switch (p.Key)
            {
                case "SwarmSize":
                    if (!int.TryParse(p.Value, NumberStyles.Integer, inv, out swarm) || swarm < 2)
                        return "Swarm Size must be at least 2.";
                    break;
                case "MaxIterations":
                    if (!int.TryParse(p.Value, NumberStyles.Integer, inv, out maxIter) || maxIter < 1)
                        return "Max Iterations must be a positive integer.";
                    break;
                case "Inertia":
                    if (!double.TryParse(p.Value, NumberStyles.Float, inv, out inertia))
                        return "Inertia must be a number.";
                    break;
                case "CognitiveCoeff":
                    if (!double.TryParse(p.Value, NumberStyles.Float, inv, out cog))
                        return "Cognitive Coefficient must be a number.";
                    break;
                case "SocialCoeff":
                    if (!double.TryParse(p.Value, NumberStyles.Float, inv, out soc))
                        return "Social Coefficient must be a number.";
                    break;
                case "ConvergenceTolerance":
                    if (!double.TryParse(p.Value, NumberStyles.Float, inv, out tol) || tol < 0)
                        return "Convergence Tolerance must be a non-negative number.";
                    break;
                case "NoImprovementLimit":
                    if (!int.TryParse(p.Value, NumberStyles.Integer, inv, out noImprove) || noImprove < 0)
                        return "No-Improvement Limit must be a non-negative integer (0 disables).";
                    break;
            }
        }
        _swarmSize = swarm; _maxIterations = maxIter; _inertia = inertia;
        _cognitiveCoeff = cog; _socialCoeff = soc; _convergenceTolerance = tol;
        _noImprovementLimit = noImprove;
        return null;
    }

    public override IEstimationAlgorithm CreateAlgorithm(
        int dimensions, double[] lower, double[] upper, double[] initial,
        bool isMaximize = false)
    {
        var alg = new ParticleSwarmAlgorithm(_swarmSize, _inertia, _cognitiveCoeff, _socialCoeff, _noImprovementLimit);
        alg.Initialize(dimensions, lower, upper, initial, _maxIterations, _convergenceTolerance, isMaximize);
        return alg;
    }

    protected override void SaveProperties(Utf8JsonWriter writer)
    {
        writer.WriteNumber("SwarmSize",            _swarmSize);
        writer.WriteNumber("MaxIterations",        _maxIterations);
        writer.WriteNumber("Inertia",              _inertia);
        writer.WriteNumber("CognitiveCoeff",       _cognitiveCoeff);
        writer.WriteNumber("SocialCoeff",          _socialCoeff);
        writer.WriteNumber("ConvergenceTolerance", _convergenceTolerance);
        writer.WriteNumber("NoImprovementLimit",   _noImprovementLimit);
    }

    protected override void LoadProperty(string propertyName, ref Utf8JsonReader reader)
    {
        switch (propertyName)
        {
            case "SwarmSize":            _swarmSize            = reader.GetInt32();  break;
            case "MaxIterations":        _maxIterations        = reader.GetInt32();  break;
            case "Inertia":              _inertia              = reader.GetDouble(); break;
            case "CognitiveCoeff":       _cognitiveCoeff       = reader.GetDouble(); break;
            case "SocialCoeff":          _socialCoeff          = reader.GetDouble(); break;
            case "ConvergenceTolerance": _convergenceTolerance = reader.GetDouble(); break;
            case "NoImprovementLimit":   _noImprovementLimit   = reader.GetInt32();  break;
            default: reader.Skip(); break;
        }
    }
}

// ── Genetic Algorithm ────────────────────────────────────────────────────────

/// <summary>Configuration for the <see cref="GeneticAlgorithm"/>.</summary>
public sealed class GeneticAlgorithmConfig : EstimationAlgorithmConfig
{
    internal const string Id = "GeneticAlgorithm";

    public override string AlgorithmId => Id;
    public override string DisplayName  => "Genetic Algorithm";
    public override string AlgorithmDescription =>
        "A real-valued genetic algorithm using BLX-\u03B1 crossover, Gaussian mutation, tournament selection, and elitism.";

    private int    _populationSize       = 50;
    private int    _maxGenerations       = 300;
    private double _crossoverRate        = 0.8;
    private double _mutationRate         = 0.05;
    private int    _elitismCount         = 2;
    private int    _tournamentSize       = 3;
    private double _convergenceTolerance = 1e-6;

    /// <summary>Number of individuals in each generation.</summary>
    public int PopulationSize
    {
        get => _populationSize;
        set { _populationSize = Math.Max(4, value); Raise(nameof(PopulationSize)); }
    }

    /// <summary>Maximum number of generations before stopping.</summary>
    public int MaxGenerations
    {
        get => _maxGenerations;
        set { _maxGenerations = value; Raise(nameof(MaxGenerations)); }
    }

    /// <summary>Probability that two parents undergo crossover (0–1).</summary>
    public double CrossoverRate
    {
        get => _crossoverRate;
        set { _crossoverRate = Math.Clamp(value, 0.0, 1.0); Raise(nameof(CrossoverRate)); }
    }

    /// <summary>Per-gene mutation probability (0–1).</summary>
    public double MutationRate
    {
        get => _mutationRate;
        set { _mutationRate = Math.Clamp(value, 0.0, 1.0); Raise(nameof(MutationRate)); }
    }

    /// <summary>Number of best individuals copied unchanged to the next generation.</summary>
    public int ElitismCount
    {
        get => _elitismCount;
        set { _elitismCount = Math.Max(0, value); Raise(nameof(ElitismCount)); }
    }

    /// <summary>Number of candidates compared in each tournament selection.</summary>
    public int TournamentSize
    {
        get => _tournamentSize;
        set { _tournamentSize = Math.Max(2, value); Raise(nameof(TournamentSize)); }
    }

    /// <summary>Convergence threshold: stop when best-fitness improvement per
    /// generation falls below this value.</summary>
    public double ConvergenceTolerance
    {
        get => _convergenceTolerance;
        set { _convergenceTolerance = value; Raise(nameof(ConvergenceTolerance)); }
    }

    public override IReadOnlyList<AlgorithmParameterDescriptor> GetParameters() =>
    [
        new() { Key = "PopulationSize",       Label = "Population Size",
                Hint  = "Individuals per generation (minimum 4)",
                Value = _populationSize.ToString(CultureInfo.InvariantCulture) },
        new() { Key = "MaxGenerations",       Label = "Max Generations",
                Hint  = "Stop after this many generations",
                Value = _maxGenerations.ToString(CultureInfo.InvariantCulture) },
        new() { Key = "CrossoverRate",        Label = "Crossover Rate",
                Hint  = "Probability of crossover between two parents (0\u20131)",
                Value = _crossoverRate.ToString("G", CultureInfo.InvariantCulture) },
        new() { Key = "MutationRate",         Label = "Mutation Rate",
                Hint  = "Per-gene mutation probability (0\u20131)",
                Value = _mutationRate.ToString("G", CultureInfo.InvariantCulture) },
        new() { Key = "ElitismCount",         Label = "Elitism Count",
                Hint  = "Best individuals copied unchanged each generation",
                Value = _elitismCount.ToString(CultureInfo.InvariantCulture) },
        new() { Key = "TournamentSize",       Label = "Tournament Size",
                Hint  = "Candidates compared per tournament selection (minimum 2)",
                Value = _tournamentSize.ToString(CultureInfo.InvariantCulture) },
        new() { Key = "ConvergenceTolerance", Label = "Convergence Tolerance",
                Hint  = "Stop when per-generation improvement falls below this",
                Value = _convergenceTolerance.ToString("G", CultureInfo.InvariantCulture) },
    ];

    public override string? ApplyParameters(IReadOnlyList<AlgorithmParameterDescriptor> parameters)
    {
        var inv = CultureInfo.InvariantCulture;
        int pop = _populationSize, maxGen = _maxGenerations, elite = _elitismCount, tournament = _tournamentSize;
        double cross = _crossoverRate, mut = _mutationRate, tol = _convergenceTolerance;
        foreach (var p in parameters)
        {
            switch (p.Key)
            {
                case "PopulationSize":
                    if (!int.TryParse(p.Value, NumberStyles.Integer, inv, out pop) || pop < 4)
                        return "Population Size must be at least 4.";
                    break;
                case "MaxGenerations":
                    if (!int.TryParse(p.Value, NumberStyles.Integer, inv, out maxGen) || maxGen < 1)
                        return "Max Generations must be a positive integer.";
                    break;
                case "CrossoverRate":
                    if (!double.TryParse(p.Value, NumberStyles.Float, inv, out cross) || cross < 0 || cross > 1)
                        return "Crossover Rate must be between 0 and 1.";
                    break;
                case "MutationRate":
                    if (!double.TryParse(p.Value, NumberStyles.Float, inv, out mut) || mut < 0 || mut > 1)
                        return "Mutation Rate must be between 0 and 1.";
                    break;
                case "ElitismCount":
                    if (!int.TryParse(p.Value, NumberStyles.Integer, inv, out elite) || elite < 0)
                        return "Elitism Count must be a non-negative integer.";
                    break;
                case "TournamentSize":
                    if (!int.TryParse(p.Value, NumberStyles.Integer, inv, out tournament) || tournament < 2)
                        return "Tournament Size must be at least 2.";
                    break;
                case "ConvergenceTolerance":
                    if (!double.TryParse(p.Value, NumberStyles.Float, inv, out tol) || tol < 0)
                        return "Convergence Tolerance must be a non-negative number.";
                    break;
            }
        }
        _populationSize = pop; _maxGenerations = maxGen; _crossoverRate = cross;
        _mutationRate = mut; _elitismCount = elite; _tournamentSize = tournament;
        _convergenceTolerance = tol;
        return null;
    }

    public override IEstimationAlgorithm CreateAlgorithm(
        int dimensions, double[] lower, double[] upper, double[] initial,
        bool isMaximize = false)
    {
        var alg = new GeneticAlgorithm(_populationSize, _crossoverRate, _mutationRate,
                                       _elitismCount, _tournamentSize);
        alg.Initialize(dimensions, lower, upper, initial, _maxGenerations, _convergenceTolerance, isMaximize);
        return alg;
    }

    protected override void SaveProperties(Utf8JsonWriter writer)
    {
        writer.WriteNumber("PopulationSize",       _populationSize);
        writer.WriteNumber("MaxGenerations",       _maxGenerations);
        writer.WriteNumber("CrossoverRate",        _crossoverRate);
        writer.WriteNumber("MutationRate",         _mutationRate);
        writer.WriteNumber("ElitismCount",         _elitismCount);
        writer.WriteNumber("TournamentSize",       _tournamentSize);
        writer.WriteNumber("ConvergenceTolerance", _convergenceTolerance);
    }

    protected override void LoadProperty(string propertyName, ref Utf8JsonReader reader)
    {
        switch (propertyName)
        {
            case "PopulationSize":       _populationSize       = reader.GetInt32();  break;
            case "MaxGenerations":       _maxGenerations       = reader.GetInt32();  break;
            case "CrossoverRate":        _crossoverRate        = reader.GetDouble(); break;
            case "MutationRate":         _mutationRate         = reader.GetDouble(); break;
            case "ElitismCount":         _elitismCount         = reader.GetInt32();  break;
            case "TournamentSize":       _tournamentSize       = reader.GetInt32();  break;
            case "ConvergenceTolerance": _convergenceTolerance = reader.GetDouble(); break;
            default: reader.Skip(); break;
        }
    }
}

// ── Stochastic Gradient (SPSA) ───────────────────────────────────────────────

/// <summary>Configuration for the <see cref="StochasticGradientAlgorithm"/> (SPSA).</summary>
public sealed class StochasticGradientConfig : EstimationAlgorithmConfig
{
    internal const string Id = "StochasticGradient";

    public override string AlgorithmId => Id;
    public override string DisplayName  => "Stochastic Gradient (SPSA)";
    public override string AlgorithmDescription =>
        "SPSA estimates the gradient from two evaluations per iteration using random simultaneous perturbations \u2014 efficient for high-dimensional problems.";

    private int    _maxIterations        = 200;
    private double _convergenceTolerance = 1e-6;
    private double _a                    = 0.1;
    private double _c                    = 0.1;
    private double _bigA                 = 20.0;
    private double _alpha                = 0.602;
    private double _gamma                = 0.101;

    /// <summary>Maximum number of SPSA iterations (each uses 3 evaluations).</summary>
    public int MaxIterations
    {
        get => _maxIterations;
        set { _maxIterations = Math.Max(1, value); Raise(nameof(MaxIterations)); }
    }

    /// <summary>Stop when per-iteration improvement falls below this value.</summary>
    public double ConvergenceTolerance
    {
        get => _convergenceTolerance;
        set { _convergenceTolerance = value; Raise(nameof(ConvergenceTolerance)); }
    }

    /// <summary>Step-size gain numerator <i>a</i>.</summary>
    public double A
    {
        get => _a;
        set { _a = value; Raise(nameof(A)); }
    }

    /// <summary>Perturbation gain numerator <i>c</i>.</summary>
    public double C
    {
        get => _c;
        set { _c = value; Raise(nameof(C)); }
    }

    /// <summary>Stability constant <i>A</i> — typically 10 % of max iterations.</summary>
    public double BigA
    {
        get => _bigA;
        set { _bigA = Math.Max(0.0, value); Raise(nameof(BigA)); }
    }

    /// <summary>Step-size decay exponent α (default 0.602).</summary>
    public double Alpha
    {
        get => _alpha;
        set { _alpha = value; Raise(nameof(Alpha)); }
    }

    /// <summary>Perturbation decay exponent γ (default 0.101).</summary>
    public double Gamma
    {
        get => _gamma;
        set { _gamma = value; Raise(nameof(Gamma)); }
    }

    public override IReadOnlyList<AlgorithmParameterDescriptor> GetParameters() =>
    [
        new() { Key = "MaxIterations",        Label = "Max Iterations",
                Hint  = "Hard cap on SPSA iterations (each uses 3 evaluations)",
                Value = _maxIterations.ToString(CultureInfo.InvariantCulture) },
        new() { Key = "ConvergenceTolerance", Label = "Convergence Tolerance",
                Hint  = "Stop when per-iteration improvement falls below this",
                Value = _convergenceTolerance.ToString("G", CultureInfo.InvariantCulture) },
        new() { Key = "A",                    Label = "Step-size numerator (a)",
                Hint  = "Scales the size of each gradient step",
                Value = _a.ToString("G", CultureInfo.InvariantCulture) },
        new() { Key = "C",                    Label = "Perturbation numerator (c)",
                Hint  = "Scales the \u00B1perturbation used for gradient estimation",
                Value = _c.ToString("G", CultureInfo.InvariantCulture) },
        new() { Key = "BigA",                 Label = "Stability constant (A)",
                Hint  = "Typically 10 % of Max Iterations; prevents large early steps",
                Value = _bigA.ToString("G", CultureInfo.InvariantCulture) },
        new() { Key = "Alpha",                Label = "Step-size decay (\u03B1)",
                Hint  = "Controls how quickly the step size shrinks (recommended 0.602)",
                Value = _alpha.ToString("G", CultureInfo.InvariantCulture) },
        new() { Key = "Gamma",                Label = "Perturbation decay (\u03B3)",
                Hint  = "Controls how quickly the perturbation shrinks (recommended 0.101)",
                Value = _gamma.ToString("G", CultureInfo.InvariantCulture) },
    ];

    public override string? ApplyParameters(IReadOnlyList<AlgorithmParameterDescriptor> parameters)
    {
        var inv = CultureInfo.InvariantCulture;
        int maxIter = _maxIterations;
        double tol = _convergenceTolerance, a = _a, c = _c, bigA = _bigA, alpha = _alpha, gamma = _gamma;
        foreach (var p in parameters)
        {
            switch (p.Key)
            {
                case "MaxIterations":
                    if (!int.TryParse(p.Value, NumberStyles.Integer, inv, out maxIter) || maxIter < 1)
                        return "Max Iterations must be a positive integer.";
                    break;
                case "ConvergenceTolerance":
                    if (!double.TryParse(p.Value, NumberStyles.Float, inv, out tol) || tol < 0)
                        return "Convergence Tolerance must be a non-negative number.";
                    break;
                case "A":
                    if (!double.TryParse(p.Value, NumberStyles.Float, inv, out a) || a <= 0)
                        return "Step-size numerator a must be positive.";
                    break;
                case "C":
                    if (!double.TryParse(p.Value, NumberStyles.Float, inv, out c) || c <= 0)
                        return "Perturbation numerator c must be positive.";
                    break;
                case "BigA":
                    if (!double.TryParse(p.Value, NumberStyles.Float, inv, out bigA) || bigA < 0)
                        return "Stability constant A must be non-negative.";
                    break;
                case "Alpha":
                    if (!double.TryParse(p.Value, NumberStyles.Float, inv, out alpha) || alpha <= 0)
                        return "Decay exponent \u03B1 must be positive.";
                    break;
                case "Gamma":
                    if (!double.TryParse(p.Value, NumberStyles.Float, inv, out gamma) || gamma <= 0)
                        return "Decay exponent \u03B3 must be positive.";
                    break;
            }
        }
        _maxIterations = maxIter; _convergenceTolerance = tol;
        _a = a; _c = c; _bigA = bigA; _alpha = alpha; _gamma = gamma;
        return null;
    }

    public override IEstimationAlgorithm CreateAlgorithm(
        int dimensions, double[] lower, double[] upper, double[] initial,
        bool isMaximize = false)
    {
        var alg = new StochasticGradientAlgorithm(_a, _c, _bigA, _alpha, _gamma);
        alg.Initialize(dimensions, lower, upper, initial, _maxIterations, _convergenceTolerance, isMaximize);
        return alg;
    }

    protected override void SaveProperties(Utf8JsonWriter writer)
    {
        writer.WriteNumber("MaxIterations",        _maxIterations);
        writer.WriteNumber("ConvergenceTolerance", _convergenceTolerance);
        writer.WriteNumber("A",                    _a);
        writer.WriteNumber("C",                    _c);
        writer.WriteNumber("BigA",                 _bigA);
        writer.WriteNumber("Alpha",                _alpha);
        writer.WriteNumber("Gamma",                _gamma);
    }

    protected override void LoadProperty(string propertyName, ref Utf8JsonReader reader)
    {
        switch (propertyName)
        {
            case "MaxIterations":        _maxIterations        = reader.GetInt32();  break;
            case "ConvergenceTolerance": _convergenceTolerance = reader.GetDouble(); break;
            case "A":                    _a                    = reader.GetDouble(); break;
            case "C":                    _c                    = reader.GetDouble(); break;
            case "BigA":                 _bigA                 = reader.GetDouble(); break;
            case "Alpha":                _alpha                = reader.GetDouble(); break;
            case "Gamma":                _gamma                = reader.GetDouble(); break;
            default: reader.Skip(); break;
        }
    }
}
