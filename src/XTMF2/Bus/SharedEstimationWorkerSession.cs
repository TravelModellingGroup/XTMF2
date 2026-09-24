using System;
using System.Collections.Generic;

namespace XTMF2.Bus;

/// <summary>
/// Binds shared-estimation protocol events on a RunServerBus to a prepared worker participant.
/// </summary>
public sealed class SharedEstimationWorkerSession : IDisposable
{
    private readonly RunServerBus _bus;
    private readonly object _sync = new();
    private SharedEstimationWorkerParticipant? _participant;
    private string? _runId;
    private RunError? _preparationError;
    private bool _disposed;

    internal SharedEstimationWorkerSession(RunServerBus bus)
    {
        _bus = bus;
        _bus.SharedEstimationRunRequested += OnRunRequested;
        _bus.SharedEstimationCandidatesReceived += OnCandidatesReceived;
        _bus.SharedEstimationCancellationRequested += OnCancellationRequested;
    }

    private void OnRunRequested(object sender, SharedEstimationRunRequest request)
    {
        SharedEstimationWorkerParticipant? participant = null;
        RunError? error = null;
        try
        {
            SharedEstimationWorkerParticipant.TryCreate(_bus.Runtime, request, out participant, out error);
        }
        catch (Exception exception)
        {
            error = new RunError(RunErrorType.Validation, exception.Message, null, exception.StackTrace);
        }

        lock (_sync)
        {
            if (_disposed)
                return;
            _runId = request.RunId;
            _participant = participant;
            _preparationError = error;
        }
    }

    private void OnCandidatesReceived(object sender, IReadOnlyList<SharedEstimationCandidate> candidates)
    {
        SharedEstimationWorkerParticipant? participant;
        RunError? preparationError;
        string? runId;
        lock (_sync)
        {
            if (_disposed)
                return;
            participant = _participant;
            preparationError = _preparationError;
            runId = _runId;
        }

        var results = new SharedEstimationEvaluationResult[candidates.Count];
        for (int i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (runId is null || candidate.RunId != runId)
            {
                results[i] = CreateError(candidate, "No matching shared-estimation run is active.", null, null);
            }
            else if (preparationError is not null)
            {
                results[i] = CreateError(candidate, preparationError.Message ?? "Worker preparation failed.",
                    preparationError.ModuleName, preparationError.ElementId);
            }
            else if (participant is null)
            {
                results[i] = CreateError(candidate, "The shared-estimation worker is not prepared.", null, null);
            }
            else
            {
                results[i] = participant.Evaluate(candidate);
            }
        }

        _bus.SendSharedEstimationResults(results);
    }

    private void OnCancellationRequested(object sender, string runId, string? reason)
    {
        lock (_sync)
        {
            if (!_disposed && _runId == runId)
            {
                _runId = null;
                _participant = null;
                _preparationError = null;
            }
        }
    }

    private static SharedEstimationEvaluationResult CreateError(
        SharedEstimationCandidate candidate, string message, string? moduleName, Guid? elementId)
        => new(candidate.RunId, candidate.BatchId, candidate.CandidateId, double.NaN,
            message, moduleName, elementId);

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            _runId = null;
            _participant = null;
            _preparationError = null;
        }

        _bus.SharedEstimationRunRequested -= OnRunRequested;
        _bus.SharedEstimationCandidatesReceived -= OnCandidatesReceived;
        _bus.SharedEstimationCancellationRequested -= OnCancellationRequested;
    }
}