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
/// Contract for an estimation search algorithm.  Implementations propose parameter vectors,
/// receive scalar fitness values (lower = better), and ultimately report the best parameters
/// found during the search.
/// </summary>
/// <remarks>
/// Usage pattern:
/// <code>
/// algorithm.Initialize(n, lower, upper, initial);
/// algorithm.Run(params => EvaluateModelSystem(params), (iter, fitness) => ReportProgress(iter, fitness));
/// var best = algorithm.BestParameters;
/// </code>
/// This interface is designed to support a future plug-in model where different algorithms
/// (genetic, PSO, Bayesian, etc.) can be selected per model system.
/// </remarks>
public interface IEstimationAlgorithm
{
    /// <summary>Human-readable name for this algorithm (shown in status messages).</summary>
    string Name { get; }

    /// <summary>
    /// Configure the algorithm before calling <see cref="Run"/>.
    /// </summary>
    /// <param name="dimensions">Number of parameters to optimise.</param>
    /// <param name="lowerBounds">Minimum allowed value per parameter.</param>
    /// <param name="upperBounds">Maximum allowed value per parameter.</param>
    /// <param name="initialPoint">Starting parameter values (null-hypothesis).</param>
    /// <param name="maxIterations">Hard cap on the number of model-system evaluations.</param>
    /// <param name="convergenceTolerance">
    /// Stop early when the range of fitness values across the active search set falls below
    /// this threshold.
    /// </param>
    void Initialize(int dimensions, double[] lowerBounds, double[] upperBounds,
        double[] initialPoint, int maxIterations = 500, double convergenceTolerance = 1e-6,
        bool isMaximize = false);

    /// <summary>
    /// Execute the search.  <paramref name="fitnessEvaluator"/> is called once per model-system
    /// execution with the proposed parameter vector; it must return the scalar fitness value
    /// (lower = better).  The algorithm controls the evaluation schedule.
    /// </summary>
    /// <param name="fitnessEvaluator">
    /// Callback that runs the model system and returns its fitness value.
    /// </param>
    /// <param name="progressCallback">
    /// Optional callback invoked after each outer iteration with the current iteration index
    /// and the best fitness seen so far.  Used for status reporting.
    /// </param>
    /// <param name="shouldCancel">
    /// Optional predicate polled at the start of each outer iteration.  When it returns
    /// <c>true</c> the algorithm exits immediately, preserving the best solution found so far.
    /// </param>
    void Run(Func<double[], double> fitnessEvaluator,
             Action<int, double>? progressCallback = null,
             Func<bool>? shouldCancel = null);

    /// <summary>The parameter vector that produced the lowest fitness seen so far.</summary>
    double[] BestParameters { get; }

    /// <summary>The lowest fitness value observed during the search.</summary>
    double BestFitness { get; }
}
