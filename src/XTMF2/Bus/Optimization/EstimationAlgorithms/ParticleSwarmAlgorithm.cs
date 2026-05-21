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
/// Standard Particle Swarm Optimisation (PSO) for derivative-free minimisation.
/// </summary>
/// <remarks>
/// Uses the inertia-weight formulation (Shi &amp; Eberhart 1998).
/// Proposed positions are clamped to the per-dimension bounds supplied during
/// <see cref="Initialize"/>.  Velocities are clamped to ±(upper−lower)/2 to
/// prevent explosion.  The initial swarm is seeded from a uniform distribution
/// over <c>[lower, upper]</c> with the supplied <paramref name="initialPoint"/>
/// used as one of the particles.
/// </remarks>
public sealed class ParticleSwarmAlgorithm : IEstimationAlgorithm
{
    private readonly int    _swarmSize;
    private readonly double _inertia;
    private readonly double _c1;   // cognitive coefficient
    private readonly double _c2;   // social coefficient
    private readonly int    _noImprovementLimit;

    private int      _n;
    private double[] _lower   = [];
    private double[] _upper   = [];
    private int      _maxIter = 300;
    private double   _tol     = 1e-6;
    private bool     _isMaximize;

    /// <inheritdoc/>
    public double[] BestParameters { get; private set; } = [];

    private double _bestInternalFitness = double.MaxValue;

    /// <inheritdoc/>
    /// Returns the actual fitness in the user's original sign (positive for maximise).
    public double BestFitness => _isMaximize ? -_bestInternalFitness : _bestInternalFitness;

    /// <inheritdoc/>
    public string Name => "Particle Swarm Optimisation";

    /// <summary>
    /// Initialises a new PSO instance with the supplied hyperparameters.
    /// </summary>
    /// <param name="swarmSize">Number of particles (default 30).</param>
    /// <param name="inertia">Inertia weight ω (default 0.72).</param>
    /// <param name="cognitiveCoeff">Personal-best attraction c1 (default 1.49).</param>
    /// <param name="socialCoeff">Global-best attraction c2 (default 1.49).</param>
    /// <param name="noImprovementLimit">Stop after this many consecutive iterations
    /// without improvement in the global best (default 5). Set to 0 to disable.</param>
    public ParticleSwarmAlgorithm(
        int swarmSize = 29, double inertia = -0.4438,
        double cognitiveCoeff = -0.2699, double socialCoeff = 3.3950,
        int noImprovementLimit = 5)
    {
        _swarmSize          = Math.Max(2, swarmSize);
        _inertia            = inertia;
        _c1                 = cognitiveCoeff;
        _c2                 = socialCoeff;
        _noImprovementLimit = Math.Max(0, noImprovementLimit);
    }

    /// <inheritdoc/>
    public void Initialize(int dimensions, double[] lowerBounds, double[] upperBounds,
        double[] initialPoint, int maxIterations = 300, double convergenceTolerance = 1e-6,
        bool isMaximize = false)
    {
        _n                   = dimensions;
        _lower               = lowerBounds;
        _upper               = upperBounds;
        _maxIter             = maxIterations;
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

        // ── Initialise particles ──────────────────────────────────────────────
        var positions  = new double[_swarmSize][];
        var velocities = new double[_swarmSize][];
        var pBest      = new double[_swarmSize][];   // personal best positions
        var pBestFit   = new double[_swarmSize];

        double[] velMax = new double[_n];
        for (int d = 0; d < _n; d++)
            velMax[d] = (_upper[d] - _lower[d]) / 2.0;

        // Seed particle 0 with the supplied initial point.
        positions[0] = Clamp((double[])BestParameters.Clone());
        velocities[0] = new double[_n];  // zero velocity

        for (int i = 1; i < _swarmSize; i++)
        {
            positions[i]  = new double[_n];
            velocities[i] = new double[_n];
            for (int d = 0; d < _n; d++)
            {
                positions[i][d]  = _lower[d] + rng.NextDouble() * (_upper[d] - _lower[d]);
                velocities[i][d] = (rng.NextDouble() * 2.0 - 1.0) * velMax[d];
            }
        }

        // Evaluate initial fitness.
        for (int i = 0; i < _swarmSize; i++)
        {
            double f = eval(positions[i]);
            pBest[i]    = (double[])positions[i].Clone();
            pBestFit[i] = f;
            if (f < _bestInternalFitness)
            {
                _bestInternalFitness = f;
                BestParameters       = (double[])positions[i].Clone();
            }
        }

        // ── Main loop ─────────────────────────────────────────────────────────
        double prevBestFitness = _bestInternalFitness;
        int noImprovementCount = 0;
        for (int iter = 0; iter < _maxIter; iter++)
        {
            if (shouldCancel?.Invoke() == true) break;

            for (int i = 0; i < _swarmSize; i++)
            {
                for (int d = 0; d < _n; d++)
                {
                    double r1 = rng.NextDouble();
                    double r2 = rng.NextDouble();
                    velocities[i][d] =
                        _inertia * velocities[i][d]
                        + _c1 * r1 * (pBest[i][d]      - positions[i][d])
                        + _c2 * r2 * (BestParameters[d] - positions[i][d]);

                    // Clamp velocity.
                    velocities[i][d] = Math.Max(-velMax[d], Math.Min(velMax[d], velocities[i][d]));

                    positions[i][d] = Math.Max(_lower[d], Math.Min(_upper[d],
                        positions[i][d] + velocities[i][d]));
                }

                double fitness = eval(positions[i]);
                if (fitness < pBestFit[i])
                {
                    pBestFit[i] = fitness;
                    pBest[i]    = (double[])positions[i].Clone();

                    if (fitness < _bestInternalFitness)
                    {
                        _bestInternalFitness = fitness;
                        BestParameters       = (double[])positions[i].Clone();
                    }
                }
            }

            progressCallback?.Invoke(iter + 1, BestFitness);

            // Convergence: global-best improvement smaller than tolerance.
            if (Math.Abs(prevBestFitness - _bestInternalFitness) <= _tol)
            {
                noImprovementCount++;
                if (_noImprovementLimit > 0 && noImprovementCount >= _noImprovementLimit)
                    break;
            }
            else
            {
                noImprovementCount = 0;
            }
            prevBestFitness = _bestInternalFitness;
        }
    }

    private double[] Clamp(double[] v)
    {
        for (int d = 0; d < _n; d++)
            v[d] = Math.Max(_lower[d], Math.Min(_upper[d], v[d]));
        return v;
    }
}