using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using XTMF2.Bus;

namespace XTMF2.Bus.Optimization;

/// <summary>
/// Adapts an initialized estimation algorithm to the shared worker coordinator.
/// </summary>
public sealed class SharedEstimationCoordinatorRun
{
    private readonly SharedEstimationCoordinator _coordinator;
    private readonly IEstimationAlgorithm _algorithm;
    private readonly string _runId;
    private readonly bool _isMaximize;
    private readonly string? _reportPath;
    private readonly IReadOnlyList<string> _parameterNames;
    private readonly object _progressSync = new();
    private readonly Dictionary<string, int> _evaluationsByWorker = new(StringComparer.Ordinal);
    private long _nextBatchId;
    private long _nextCandidateId;
    private int _evaluationsCompleted;
    private int _fitnessTestsThisIteration;
    private int _iterations;
    private int _currentIteration;
    private Action<SharedEstimationProgress>? _progressCallback;
    private double _lastReportedFitness;
    private double[] _bestObservedParameters = [];
    private bool _hasObservedFitness;
    private string? _failureReason;
    private CancellationToken _cancellationToken;

    public SharedEstimationCoordinatorRun(
        string runId,
        IEstimationAlgorithm algorithm,
        SharedEstimationCoordinator coordinator,
        bool isMaximize = false,
        string? reportPath = null,
        IReadOnlyList<string>? parameterNames = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(algorithm);
        ArgumentNullException.ThrowIfNull(coordinator);
        _runId = runId;
        _algorithm = algorithm;
        _coordinator = coordinator;
        _isMaximize = isMaximize;
        _reportPath = reportPath;
        _parameterNames = parameterNames ?? Array.Empty<string>();
    }

    public SharedEstimationCompletion Execute(
        Action<SharedEstimationProgress>? progress = null,
        Func<bool>? shouldCancel = null,
        CancellationToken cancellationToken = default)
    {
        _cancellationToken = cancellationToken;
        _fitnessTestsThisIteration = 0;
        _evaluationsByWorker.Clear();
        _bestObservedParameters = [];
        _currentIteration = 1;
        _lastReportedFitness = _algorithm.BestFitness;
        if (!double.IsFinite(_lastReportedFitness))
            _lastReportedFitness = _isMaximize ? double.MinValue : double.MaxValue;
        _hasObservedFitness = false;
        _progressCallback = progress;
        EstimationEvaluationReportWriter? report = null;
        try
        {
            if (_reportPath is not null)
            {
                report = new EstimationEvaluationReportWriter(_reportPath, _algorithm.Name, _parameterNames);
                _reportWriter = report;
            }
            _algorithm.RunBatch(
                fitnessEvaluator: EvaluateSingle,
                batchFitnessEvaluator: EvaluateBatch,
                progressCallback: (iteration, bestFitness) =>
                {
                    lock (_progressSync)
                    {
                        _iterations = Math.Max(_iterations, iteration);
                        if (double.IsFinite(bestFitness))
                        {
                            _lastReportedFitness = bestFitness;
                            _bestObservedParameters = (double[])_algorithm.BestParameters.Clone();
                            _hasObservedFitness = true;
                        }
                        progress?.Invoke(CreateProgress(iteration));
                        _fitnessTestsThisIteration = 0;
                        _currentIteration = iteration + 1;
                    }
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
        finally
        {
            _reportWriter = null;
            report?.Dispose();
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

        var results = _coordinator.EvaluateAsync(candidates, _cancellationToken, OnEvaluationCompleted)
            .GetAwaiter().GetResult();
        var fitnesses = new double[results.Count];
        for (int i = 0; i < results.Count; i++)
        {
            if (results[i].Error is not null || !double.IsFinite(results[i].Fitness))
            {
                _failureReason ??= results[i].Error ?? "A worker returned a non-finite fitness value.";
                fitnesses[i] = _isMaximize ? double.MinValue : double.MaxValue;
            }
            else
            {
                fitnesses[i] = results[i].Fitness;
            }
        }
        return fitnesses;
    }

    private void OnEvaluationCompleted(
        string workerId, SharedEstimationCandidate candidate, SharedEstimationEvaluationResult result)
    {
        lock (_progressSync)
        {
            _evaluationsCompleted++;
            _fitnessTestsThisIteration++;
            _evaluationsByWorker[workerId] = _evaluationsByWorker.GetValueOrDefault(workerId) + 1;
            try
            {
                _reportWriter?.Write(_currentIteration, candidate.Parameters, result.Fitness);
            }
            catch (IOException exception)
            {
                _failureReason ??= $"Unable to write the estimation report: {exception.Message}";
            }
            if (result.Error is not null || !double.IsFinite(result.Fitness))
            {
                _failureReason ??= result.Error ?? "A worker returned a non-finite fitness value.";
            }
            else if (!_hasObservedFitness ||
                     (_isMaximize ? result.Fitness > _lastReportedFitness : result.Fitness < _lastReportedFitness))
            {
                _lastReportedFitness = result.Fitness;
                _bestObservedParameters = candidate.Parameters.ToArray();
                _hasObservedFitness = true;
            }

            _progressCallback?.Invoke(CreateProgress(_currentIteration));
        }
    }

    private SharedEstimationProgress CreateProgress(int iteration)
        => new(_runId, iteration, _hasObservedFitness ? _lastReportedFitness : double.NaN,
            _evaluationsCompleted, 0,
            _coordinator.ActiveWorkerCount, _fitnessTestsThisIteration,
            new Dictionary<string, int>(_evaluationsByWorker),
            (double[])_bestObservedParameters.Clone());

    private EstimationEvaluationReportWriter? _reportWriter;
}