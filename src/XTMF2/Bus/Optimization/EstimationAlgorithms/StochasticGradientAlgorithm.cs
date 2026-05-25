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
/// Simultaneous Perturbation Stochastic Approximation (SPSA) — a gradient-based
/// optimiser that estimates the gradient from exactly two function evaluations per
/// iteration regardless of dimensionality.
/// </summary>
/// <remarks>
/// SPSA (Spall 1992) approximates the gradient as:
/// <code>
///   ĝ_k = (f(θ + c_k·Δ) − f(θ − c_k·Δ)) / (2·c_k·Δ)
/// </code>
/// where Δ is a random ±1 Bernoulli vector and c_k = c/(k+1)^γ.
/// The parameter update is θ_{k+1} = θ_k − a_k·ĝ_k with a_k = a/(A+k+1)^α.
/// When <see cref="IsMaximize"/> is <c>true</c> the gradient is added (ascent).
///
/// Recommended starting values follow Spall's guideline:
///   α = 0.602, γ = 0.101, A = 10 % of max iterations.
/// </remarks>
public sealed class StochasticGradientAlgorithm : IEstimationAlgorithm
{
    private readonly double _a;          // gain-sequence numerator for step size
    private readonly double _c;          // gain-sequence numerator for perturbation
    private readonly double _bigA;       // stability constant A
    private readonly double _alpha;      // step-size decay exponent
    private readonly double _gamma;      // perturbation-size decay exponent

    private int      _n;
    private double[] _lower   = [];
    private double[] _upper   = [];
    private int      _maxIter = 200;
    private double   _tol     = 1e-6;
    private bool     _isMaximize;

    /// <inheritdoc/>
    public double[] BestParameters { get; private set; } = [];

    private double _bestInternalFitness = double.MaxValue;

    /// <inheritdoc/>
    /// Returns the actual fitness in the user's original sign (positive for maximise).
    public double BestFitness => _isMaximize ? -_bestInternalFitness : _bestInternalFitness;

    /// <inheritdoc/>
    public string Name => "Stochastic Gradient (SPSA)";

    /// <summary>
    /// Creates a new SPSA instance with the supplied hyperparameters.
    /// </summary>
    /// <param name="a">Step-size gain numerator (default 0.1).</param>
    /// <param name="c">Perturbation gain numerator (default 0.1).</param>
    /// <param name="bigA">Stability constant — typically 10 % of max iterations (default 20).</param>
    /// <param name="alpha">Step-size decay exponent (default 0.602).</param>
    /// <param name="gamma">Perturbation decay exponent (default 0.101).</param>
    public StochasticGradientAlgorithm(
        double a      = 0.1,
        double c      = 0.1,
        double bigA   = 20.0,
        double alpha  = 0.602,
        double gamma  = 0.101)
    {
        _a     = a;
        _c     = c;
        _bigA  = Math.Max(0.0, bigA);
        _alpha = alpha;
        _gamma = gamma;
    }

    /// <inheritdoc/>
    public void Initialize(int dimensions, double[] lowerBounds, double[] upperBounds,
        double[] initialPoint, int maxIterations = 200, double convergenceTolerance = 1e-6,
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

        // Negate fitness internally when maximising so we always minimise.
        Func<double[], double> eval = _isMaximize
            ? v => -fitnessEvaluator(v)
            : fitnessEvaluator;

        var rng   = new Random(42);
        var theta = Clamp((double[])BestParameters.Clone());

        // Evaluate starting point.
        double currentFitness = eval(theta);
        TrackBest(theta, currentFitness);

        double prevBestFitness = _bestInternalFitness;

        for (int k = 0; k < _maxIter; k++)
        {
            if (shouldCancel?.Invoke() == true) break;

            // Compute gain sequences.
            double ak = _a / Math.Pow(_bigA + k + 1.0, _alpha);
            double ck = _c / Math.Pow(k + 1.0, _gamma);

            // Generate simultaneous perturbation vector Δ (Bernoulli ±1).
            var delta = new double[_n];
            for (int d = 0; d < _n; d++)
                delta[d] = rng.NextDouble() < 0.5 ? -1.0 : 1.0;

            // Two-sided gradient approximation.
            var thetaPlus  = Clamp(Perturb(theta,  ck, delta));
            var thetaMinus = Clamp(Perturb(theta, -ck, delta));

            double fPlus  = eval(thetaPlus);
            double fMinus = eval(thetaMinus);

            TrackBest(thetaPlus,  fPlus);
            TrackBest(thetaMinus, fMinus);

            // Gradient estimate: ĝ_k[d] = (f+ − f−) / (2·c_k·Δ[d])
            var next = new double[_n];
            for (int d = 0; d < _n; d++)
            {
                double ghat = (fPlus - fMinus) / (2.0 * ck * delta[d]);
                // Descent step (eval already negated for maximise).
                next[d] = theta[d] - ak * ghat;
            }
            theta = Clamp(next);
            currentFitness = eval(theta);
            TrackBest(theta, currentFitness);

            progressCallback?.Invoke(k + 1, BestFitness);

            // Convergence: no meaningful improvement over last iteration.
            if (Math.Abs(prevBestFitness - _bestInternalFitness) < _tol && k > 0)
                break;
            prevBestFitness = _bestInternalFitness;
        }
    }

    private void TrackBest(double[] point, double f)
    {
        if (f < _bestInternalFitness)
        {
            _bestInternalFitness = f;
            BestParameters       = (double[])point.Clone();
        }
    }

    private double[] Perturb(double[] v, double scale, double[] delta)
    {
        var result = new double[_n];
        for (int d = 0; d < _n; d++)
            result[d] = v[d] + scale * delta[d];
        return result;
    }

    private double[] Clamp(double[] v)
    {
        for (int d = 0; d < _n; d++)
            v[d] = Math.Max(_lower[d], Math.Min(_upper[d], v[d]));
        return v;
    }
}