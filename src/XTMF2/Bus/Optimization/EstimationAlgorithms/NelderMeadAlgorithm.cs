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
/// Nelder-Mead downhill simplex algorithm for derivative-free minimisation.
/// </summary>
/// <remarks>
/// Standard NM with α=1 (reflection), γ=2 (expansion), ρ=0.5 (contraction), σ=0.5 (shrink).
/// Proposed points are clamped to the per-dimension bounds supplied during
/// <see cref="Initialize"/>.  An initial simplex is constructed from the supplied starting
/// point by perturbing each dimension by 5 % of its range.
/// </remarks>
public sealed class NelderMeadAlgorithm : IEstimationAlgorithm
{
    // ── NM constants ────────────────────────────────────────────────────────────
    private const double Alpha = 1.0;   // reflection coefficient
    private const double Gamma = 2.0;   // expansion coefficient
    private const double Rho   = 0.5;   // contraction coefficient
    private const double Sigma = 0.5;   // shrink coefficient

    // ── configuration (set by Initialize) ───────────────────────────────────────
    private int      _n;
    private double[] _lower          = [];
    private double[] _upper          = [];
    private double[] _initial        = [];
    private int      _maxIterations  = 500;
    private double   _tolerance      = 1e-6;
    private bool     _isMaximize;

    // ── results ─────────────────────────────────────────────────────────────────
    /// <inheritdoc/>
    public double[] BestParameters { get; private set; } = [];

    private double _bestInternalFitness = double.MaxValue;

    /// <inheritdoc/>
    /// Returns the actual fitness in the user's original sign (positive for maximise).
    public double BestFitness => _isMaximize ? -_bestInternalFitness : _bestInternalFitness;

    /// <inheritdoc/>
    public string Name => "Nelder-Mead Simplex";

    /// <inheritdoc/>
    public void Initialize(int dimensions, double[] lowerBounds, double[] upperBounds,
        double[] initialPoint, int maxIterations = 500, double convergenceTolerance = 1e-6,
        bool isMaximize = false)
    {
        _n                   = dimensions;
        _lower               = lowerBounds;
        _upper               = upperBounds;
        _initial             = initialPoint;
        _maxIterations       = maxIterations;
        _tolerance           = convergenceTolerance;
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

        // Negate fitness internally when maximising so the algorithm always minimises.
        Func<double[], double> eval = _isMaximize
            ? v => -fitnessEvaluator(v)
            : fitnessEvaluator;

        // ── Build initial simplex (n+1 vertices) ────────────────────────────────
        var simplex = new double[_n + 1][];
        var fitness = new double[_n + 1];

        simplex[0] = Clamp(_initial);
        for (int i = 1; i <= _n; i++)
        {
            var v = (double[])_initial.Clone();
            // Perturb dimension (i-1) by 5 % of the feasible range
            double range = _upper[i - 1] - _lower[i - 1];
            double step  = range > 0.0 ? 0.05 * range : 0.05;
            v[i - 1] += step;
            simplex[i] = Clamp(v);
        }

        // Evaluate all initial vertices
        for (int i = 0; i <= _n; i++)
        {
            fitness[i] = eval(simplex[i]);
            TrackBest(simplex[i], fitness[i]);
        }

        // Sorted index array – re-used every iteration
        var idx = new int[_n + 1];
        for (int i = 0; i <= _n; i++) idx[i] = i;

        // ── Main loop ────────────────────────────────────────────────────────────
        for (int iter = 0; iter < _maxIterations; iter++)
        {
            if (shouldCancel?.Invoke() == true)
                break;

            // Sort by ascending fitness
            Array.Sort(idx, (a, b) => fitness[a].CompareTo(fitness[b]));

            double bestF        = fitness[idx[0]];
            double worstF       = fitness[idx[_n]];
            double secondWorstF = fitness[idx[_n - 1]];

            // Convergence: fitness range < tolerance
            if (worstF - bestF < _tolerance)
                break;

            // Centroid of all vertices except the worst
            var centroid = Centroid(simplex, idx, _n);

            // ── Reflection ───────────────────────────────────────────────────────
            var xr = Clamp(AddScaled(centroid, Alpha, Sub(centroid, simplex[idx[_n]])));
            double fr = eval(xr);
            TrackBest(xr, fr);

            if (fr < bestF)
            {
                // ── Expansion ────────────────────────────────────────────────────
                var xe = Clamp(AddScaled(centroid, Gamma, Sub(xr, centroid)));
                double fe = eval(xe);
                TrackBest(xe, fe);

                if (fe < fr)
                {
                    simplex[idx[_n]] = xe;
                    fitness[idx[_n]] = fe;
                }
                else
                {
                    simplex[idx[_n]] = xr;
                    fitness[idx[_n]] = fr;
                }
            }
            else if (fr < secondWorstF)
            {
                simplex[idx[_n]] = xr;
                fitness[idx[_n]] = fr;
            }
            else
            {
                // ── Contraction ───────────────────────────────────────────────────
                bool doShrink;

                if (fr < worstF)
                {
                    // Outside contraction
                    var xc = Clamp(AddScaled(centroid, Rho, Sub(xr, centroid)));
                    double fc = eval(xc);
                    TrackBest(xc, fc);
                    if (fc <= fr)
                    {
                        simplex[idx[_n]] = xc;
                        fitness[idx[_n]] = fc;
                        doShrink = false;
                    }
                    else
                    {
                        doShrink = true;
                    }
                }
                else
                {
                    // Inside contraction
                    var xcc = Clamp(SubScaled(centroid, Rho, Sub(centroid, simplex[idx[_n]])));
                    double fcc = eval(xcc);
                    TrackBest(xcc, fcc);
                    if (fcc < worstF)
                    {
                        simplex[idx[_n]] = xcc;
                        fitness[idx[_n]] = fcc;
                        doShrink = false;
                    }
                    else
                    {
                        doShrink = true;
                    }
                }

                if (doShrink)
                {
                    // Shrink all vertices toward the best
                    var xBest = simplex[idx[0]];
                    for (int s = 1; s <= _n; s++)
                    {
                        simplex[idx[s]] = Clamp(AddScaled(xBest, Sigma, Sub(simplex[idx[s]], xBest)));
                        fitness[idx[s]] = eval(simplex[idx[s]]);
                        TrackBest(simplex[idx[s]], fitness[idx[s]]);
                    }
                }
            }

            progressCallback?.Invoke(iter + 1, BestFitness);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private void TrackBest(double[] point, double f)
    {
        if (f < _bestInternalFitness)
        {
            _bestInternalFitness = f;
            BestParameters       = (double[])point.Clone();
        }
    }

    private double[] Clamp(double[] x)
    {
        var r = new double[_n];
        for (int i = 0; i < _n; i++)
            r[i] = Math.Max(_lower[i], Math.Min(_upper[i], x[i]));
        return r;
    }

    /// <summary>centroid = (1/count) * sum of simplex[idx[0..count-1]]</summary>
    private double[] Centroid(double[][] simplex, int[] idx, int count)
    {
        var c = new double[_n];
        for (int i = 0; i < count; i++)
        {
            var v = simplex[idx[i]];
            for (int j = 0; j < _n; j++)
                c[j] += v[j];
        }
        for (int j = 0; j < _n; j++)
            c[j] /= count;
        return c;
    }

    /// <summary>result = a + scale * b</summary>
    private double[] AddScaled(double[] a, double scale, double[] b)
    {
        var r = new double[_n];
        for (int j = 0; j < _n; j++)
            r[j] = a[j] + scale * b[j];
        return r;
    }

    /// <summary>result = a - scale * b</summary>
    private double[] SubScaled(double[] a, double scale, double[] b)
    {
        var r = new double[_n];
        for (int j = 0; j < _n; j++)
            r[j] = a[j] - scale * b[j];
        return r;
    }

    /// <summary>result = a - b</summary>
    private double[] Sub(double[] a, double[] b)
    {
        var r = new double[_n];
        for (int j = 0; j < _n; j++)
            r[j] = a[j] - b[j];
        return r;
    }
}