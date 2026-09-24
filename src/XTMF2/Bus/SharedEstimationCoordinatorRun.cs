using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using XTMF2.Bus.Optimization;

namespace XTMF2.Bus;

/// <summary>
/// Adapts an initialized estimation algorithm to the shared worker coordinator.
/// </summary>
public sealed class SharedEstimationCoordinatorRun
{
    private readonly SharedEstimationCoordinator _coordinator;
    private readonly IEstimationAlgorithm _algorithm;
    private readonly string _runId;
    private long _nextBatchId;
    private long _nextCandidateId;
    private int _evaluationsCompleted;
    private int _iterations;
    private string? _failureReason;
    private CancellationToken _cancellationToken;

    public SharedEstimationCoordinatorRun(
        string runId,
        IEstimationAlgorithm algorithm,
        SharedEstimationCoordinator coordinator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(algorithm);
        ArgumentNullException.ThrowIfNull(coordinator);
        _runId = runId;
        _algorithm = algorithm;
        _coordinator = coordinator;
    }

    public SharedEstimationCompletion Execute(
        Action<SharedEstimationProgress>? progress = null,
        Func<bool>? shouldCancel = null,
        CancellationToken cancellationToken = default)
    {
        _cancellationToken = cancellationToken;
        try
        {
            _algorithm.RunBatch(
                fitnessEvaluator: EvaluateSingle,
                batchFitnessEvaluator: EvaluateBatch,
                progressCallback: (iteration, bestFitness) =>
                {
                    _iterations = Math.Max(_iterations, iteration);
                    progress?.Invoke(new SharedEstimationProgress(
                        _runId,
                        iteration,
                        bestFitness,
                        _evaluationsCompleted,
                        0,
                        _coordinator.ActiveWorkerCount));
                },
                shouldCancel: shouldCancel);

            return new SharedEstimationCompletion(
                _runId,
                _failureReason is null,
                _algorithm.BestFitness,
                _algorithm.BestParameters,
                _evaluationsCompleted,
                _iterations,
                _failureReason);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new SharedEstimationCompletion(
                _runId,
                false,
                _algorithm.BestFitness,
                _algorithm.BestParameters,
                _evaluationsCompleted,
                _iterations,
                "Shared estimation was cancelled.");
        }
        catch (Exception exception)
        {
            _failureReason ??= exception.Message;
            return new SharedEstimationCompletion(
                _runId,
                false,
                _algorithm.BestFitness,
                _algorithm.BestParameters,
                _evaluationsCompleted,
                _iterations,
                _failureReason);
        }
    }

    private double EvaluateSingle(double[] parameters)
        => EvaluateBatch([parameters])[0];

    private IReadOnlyList<double> EvaluateBatch(IReadOnlyList<double[]> parameters)
    {
        if (parameters.Count == 0)
            return Array.Empty<double>();

        var batchId = Interlocked.Increment(ref _nextBatchId);
        var candidates = parameters.Select(values => new SharedEstimationCandidate(
            _runId,
            batchId,
            $"{_runId}:{Interlocked.Increment(ref _nextCandidateId)}",
            values)).ToArray();

        var results = _coordinator.EvaluateAsync(candidates, _cancellationToken).GetAwaiter().GetResult();
        var fitnesses = new double[results.Count];
        for (int i = 0; i < results.Count; i++)
        {
            _evaluationsCompleted++;
            if (results[i].Error is not null)
            {
                _failureReason ??= results[i].Error;
                fitnesses[i] = double.MaxValue;
            }
            else
            {
                fitnesses[i] = results[i].Fitness;
            }
        }
        return fitnesses;
    }
}