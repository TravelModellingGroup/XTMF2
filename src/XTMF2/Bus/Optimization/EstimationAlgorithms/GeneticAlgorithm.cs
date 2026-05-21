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

namespace XTMF2.Bus.Optimization;

/// <summary>
/// Real-valued Genetic Algorithm (GA) for derivative-free minimisation.
/// </summary>
/// <remarks>
/// Uses <b>arithmetic (BLX-α) crossover</b>, <b>Gaussian mutation</b>,
/// <b>tournament selection</b>, and optional elitism.
/// Each gene is clamped to its per-dimension bounds after mutation.
/// The algorithm is deterministic given a fixed random seed (42).
/// </remarks>
public sealed class GeneticAlgorithm : IEstimationAlgorithm
{
    private readonly int    _populationSize;
    private readonly double _crossoverRate;
    private readonly double _mutationRate;
    private readonly int    _elitismCount;
    private readonly int    _tournamentSize;

    private int      _n;
    private double[] _lower   = [];
    private double[] _upper   = [];
    private int      _maxGen  = 300;
    private double   _tol     = 1e-6;
    private bool     _isMaximize;

    /// <inheritdoc/>
    public double[] BestParameters { get; private set; } = [];

    private double _bestInternalFitness = double.MaxValue;

    /// <inheritdoc/>
    /// Returns the actual fitness in the user's original sign (positive for maximise).
    public double BestFitness => _isMaximize ? -_bestInternalFitness : _bestInternalFitness;

    /// <inheritdoc/>
    public string Name => "Genetic Algorithm";

    /// <summary>
    /// Creates a new <see cref="GeneticAlgorithm"/> with the specified hyperparameters.
    /// </summary>
    public GeneticAlgorithm(
        int    populationSize = 50,
        double crossoverRate  = 0.8,
        double mutationRate   = 0.05,
        int    elitismCount   = 2,
        int    tournamentSize = 3)
    {
        _populationSize = Math.Max(4, populationSize);
        _crossoverRate  = Math.Clamp(crossoverRate, 0.0, 1.0);
        _mutationRate   = Math.Clamp(mutationRate,  0.0, 1.0);
        _elitismCount   = Math.Max(0, elitismCount);
        _tournamentSize = Math.Max(2, tournamentSize);
    }

    /// <inheritdoc/>
    public void Initialize(int dimensions, double[] lowerBounds, double[] upperBounds,
        double[] initialPoint, int maxIterations = 300, double convergenceTolerance = 1e-6,
        bool isMaximize = false)
    {
        _n                   = dimensions;
        _lower               = lowerBounds;
        _upper               = upperBounds;
        _maxGen              = maxIterations;
        _tol                 = convergenceTolerance;
        _isMaximize          = isMaximize;
        BestParameters       = (double[])initialPoint.Clone();
        _bestInternalFitness = double.MaxValue;
    }

    /// <inheritdoc/>
    public void Run(Func<double[], double> fitnessEvaluator,
                    Action<int, double>? progressCallback = null,
                    Func<bool>? shouldCancel = null)
    {
        if (_n == 0) return;

        var rng = new Random(42);

        // Negate fitness internally when maximising so the algorithm always minimises.
        Func<double[], double> eval = _isMaximize
            ? v => -fitnessEvaluator(v)
            : fitnessEvaluator;

        // ── Seed population ───────────────────────────────────────────────────
        var population  = new double[_populationSize][];
        var fitnesses   = new double[_populationSize];

        // Individual 0 is the initial (null-hypothesis) point.
        population[0] = Clamp((double[])BestParameters.Clone());

        for (int i = 1; i < _populationSize; i++)
        {
            population[i] = new double[_n];
            for (int d = 0; d < _n; d++)
                population[i][d] = _lower[d] + rng.NextDouble() * (_upper[d] - _lower[d]);
        }

        for (int i = 0; i < _populationSize; i++)
        {
            fitnesses[i] = eval(population[i]);
            if (fitnesses[i] < _bestInternalFitness)
            {
                _bestInternalFitness = fitnesses[i];
                BestParameters       = (double[])population[i].Clone();
            }
        }

        // ── Evolution loop
        var sortedIdx = new int[_populationSize];
        double prevBestFitness = _bestInternalFitness;

        for (int gen = 0; gen < _maxGen; gen++)
        {
            if (shouldCancel?.Invoke() == true) break;

            // Sort indices by ascending fitness.
            for (int i = 0; i < _populationSize; i++) sortedIdx[i] = i;
            Array.Sort(sortedIdx, (a, b) => fitnesses[a].CompareTo(fitnesses[b]));

            var next     = new double[_populationSize][];
            var nextFit  = new double[_populationSize];

            // ── Elitism ───────────────────────────────────────────────────────
            int eliteCount = Math.Min(_elitismCount, _populationSize);
            for (int e = 0; e < eliteCount; e++)
            {
                int src = sortedIdx[e];
                next[e]    = (double[])population[src].Clone();
                nextFit[e] = fitnesses[src];
            }

            // ── Fill rest through selection + crossover + mutation ─────────────
            for (int i = eliteCount; i < _populationSize; i++)
            {
                int parentA = TournamentSelect(rng, fitnesses);
                int parentB = TournamentSelect(rng, fitnesses);

                double[] child;
                if (rng.NextDouble() < _crossoverRate)
                    child = BLXAlphaCross(rng, population[parentA], population[parentB]);
                else
                    child = (double[])population[parentA].Clone();

                Mutate(rng, child);
                next[i]    = child;
                nextFit[i] = eval(child);

                if (nextFit[i] < _bestInternalFitness)
                {
                    _bestInternalFitness = nextFit[i];
                    BestParameters       = (double[])child.Clone();
                }
            }

            population = next;
            fitnesses  = nextFit;

            progressCallback?.Invoke(gen + 1, BestFitness);

            // Convergence: improvement smaller than tolerance.
            if (Math.Abs(prevBestFitness - _bestInternalFitness) < _tol)
                break;
            prevBestFitness = _bestInternalFitness;
        }
    }

    /// <summary>Tournament selection — returns index of the winner (lowest fitness).</summary>
    private int TournamentSelect(Random rng, double[] fitnesses)
    {
        int best = rng.Next(_populationSize);
        for (int t = 1; t < _tournamentSize; t++)
        {
            int candidate = rng.Next(_populationSize);
            if (fitnesses[candidate] < fitnesses[best])
                best = candidate;
        }
        return best;
    }

    /// <summary>
    /// BLX-α crossover (α = 0.5) — extends the search range slightly beyond the
    /// parents' interval, encouraging exploration.
    /// </summary>
    private double[] BLXAlphaCross(Random rng, double[] a, double[] b)
    {
        const double alpha = 0.5;
        var child = new double[_n];
        for (int d = 0; d < _n; d++)
        {
            double lo  = Math.Min(a[d], b[d]);
            double hi  = Math.Max(a[d], b[d]);
            double ext = alpha * (hi - lo);
            child[d] = lo - ext + rng.NextDouble() * (hi - lo + 2 * ext);
        }
        return Clamp(child);
    }

    /// <summary>
    /// Gaussian mutation: each gene is perturbed with probability
    /// <c>_mutationRate</c>.  The standard deviation is 5 % of the feasible range.
    /// </summary>
    private void Mutate(Random rng, double[] individual)
    {
        for (int d = 0; d < _n; d++)
        {
            if (rng.NextDouble() < _mutationRate)
            {
                double sigma = 0.05 * (_upper[d] - _lower[d]);
                individual[d] += SampleGaussian(rng) * sigma;
            }
        }
        Clamp(individual);
    }

    /// <summary>Box-Muller normal sample.</summary>
    private static double SampleGaussian(Random rng)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    private double[] Clamp(double[] v)
    {
        for (int d = 0; d < _n; d++)
            v[d] = Math.Max(_lower[d], Math.Min(_upper[d], v[d]));
        return v;
    }
}