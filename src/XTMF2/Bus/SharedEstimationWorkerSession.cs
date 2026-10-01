using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading.Channels;

namespace XTMF2.Bus;

/// <summary>
/// Binds shared-estimation protocol events on a RunServerBus to a prepared worker participant.
/// </summary>
public sealed class SharedEstimationWorkerSession : IDisposable
{
    private sealed record WorkerCommand(
        IReadOnlyList<SharedEstimationCandidate>? Candidates = null,
        string? CancelRunId = null);

    private readonly RunServerBus _bus;
    private readonly object _sync = new();
    private SharedEstimationWorkerParticipant? _participant;
    private string? _runId;
    private RunError? _preparationError;
    private bool _disposed;
    private long? _activeBatchId;
    private int _activeCandidateCount;
    private string? _processingRunId;
    private readonly Channel<WorkerCommand> _candidateQueue = Channel.CreateUnbounded<WorkerCommand>();

    internal SharedEstimationWorkerSession(RunServerBus bus)
    {
        _bus = bus;
        _bus.SharedEstimationRunRequested += OnRunRequested;
        _bus.SharedEstimationCandidatesReceived += OnCandidatesReceived;
        _bus.SharedEstimationCancellationRequested += OnCancellationRequested;
        _ = Task.Run(ProcessCandidateQueueAsync);
    }

    private void OnRunRequested(object sender, SharedEstimationRunRequest request)
    {
        Console.WriteLine($"RunServer estimation run issued: {request.RunId}");
        Console.Out.Flush();

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

        _bus.SendStatusMessage(request.RunId,
            "[Estimation] RunServer tasked with a shared estimation run.");
    }

    private void OnCandidatesReceived(object sender, IReadOnlyList<SharedEstimationCandidate> candidates)
    {
        _candidateQueue.Writer.TryWrite(new WorkerCommand(Candidates: candidates));
    }

    public IReadOnlyList<RunServerActivity> GetActiveActivities()
    {
        lock (_sync)
        {
            var activityRunId = _processingRunId ?? _runId;
            if (_disposed || activityRunId is null)
                return Array.Empty<RunServerActivity>();
            var isEvaluating = _activeBatchId.HasValue;
            return
            [
                new RunServerActivity(activityRunId, "Shared estimation worker", "Shared estimation worker",
                    RunServerActivityState.Running,
                    isEvaluating
                        ? $"Evaluating {_activeCandidateCount} candidate(s), batch {_activeBatchId}."
                        : "Assigned and ready for evaluations.",
                    ActiveWorkers: 1,
                    EvaluationsPending: _activeCandidateCount)
            ];
        }
    }

    private async Task ProcessCandidateQueueAsync()
    {
        await foreach (var command in _candidateQueue.Reader.ReadAllAsync())
        {
            if (command.CancelRunId is { } runId)
                ClearAssignment(runId);
            else if (command.Candidates is { } candidates)
                ProcessCandidates(candidates);
        }
    }

    private void ProcessCandidates(IReadOnlyList<SharedEstimationCandidate> candidates)
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
        lock (_sync)
        {
            if (candidates.Count > 0)
            {
                _processingRunId = candidates[0].RunId;
                _activeBatchId = candidates[0].BatchId;
                _activeCandidateCount = candidates.Count;
            }
        }
        try
        {
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

            foreach (var result in results)
                _bus.SendSharedEstimationResults([result]);
        }
        finally
        {
            lock (_sync)
            {
                if (candidates.Count > 0 && _processingRunId == candidates[0].RunId)
                {
                    _processingRunId = null;
                    _activeBatchId = null;
                    _activeCandidateCount = 0;
                }
            }
        }
    }

    private void OnCancellationRequested(object sender, string runId, string? reason)
        => _candidateQueue.Writer.TryWrite(new WorkerCommand(CancelRunId: runId));

    private void ClearAssignment(string runId)
    {
        lock (_sync)
        {
            if (!_disposed && _runId == runId)
            {
                _runId = null;
                _participant = null;
                _preparationError = null;
                if (_processingRunId != runId)
                {
                    _activeBatchId = null;
                    _activeCandidateCount = 0;
                }
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
            _activeBatchId = null;
            _activeCandidateCount = 0;
            _processingRunId = null;
        }

        _candidateQueue.Writer.TryComplete();
        _bus.SharedEstimationRunRequested -= OnRunRequested;
        _bus.SharedEstimationCandidatesReceived -= OnCandidatesReceived;
        _bus.SharedEstimationCancellationRequested -= OnCancellationRequested;
    }
}