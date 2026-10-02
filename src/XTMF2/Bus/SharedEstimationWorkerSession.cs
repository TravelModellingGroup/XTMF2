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
    private sealed record WorkerCommand(IReadOnlyList<SharedEstimationCandidate> Candidates);

    private readonly RunServerBus _bus;
    private readonly object _sync = new();
    private SharedEstimationWorkerParticipant? _participant;
    private string? _runId;
    private RunError? _preparationError;
    private bool _disposed;
    private long? _activeBatchId;
    private int _activeCandidateCount;
    private string? _processingRunId;
    private Scheduler.Reservation? _reservation;
    private readonly List<Scheduler.Reservation> _reservationsToReleaseWhenIdle = new();
    private TaskCompletionSource<bool>? _assignmentReady;
    private bool _evaluatingCandidate;
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

        Scheduler.Reservation reservation;
        try
        {
            reservation = _bus.ReserveSharedEstimationWorker(request);
        }
        catch (Exception exception)
        {
            _bus.SendStatusMessage(request.RunId,
                $"[Estimation] Unable to queue worker: {exception.Message}");
            return;
        }
        Scheduler.Reservation? previousReservation;
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            if (_disposed)
            {
                reservation.Dispose();
                return;
            }
            previousReservation = _reservation;
            _runId = request.RunId;
            _participant = null;
            _preparationError = null;
            _reservation = reservation;
            _assignmentReady = ready;
            reservation.SetCancellationHandler(() => ClearAssignment(request.RunId, reservation));
        }
        if (previousReservation is not null)
            ReleaseOrDefer(previousReservation);

        _ = PrepareWhenScheduled(request, reservation, ready);

        _bus.SendStatusMessage(request.RunId,
            "[Estimation] Worker queued with the RunServer model-system runs.");
    }

    private async Task PrepareWhenScheduled(SharedEstimationRunRequest request,
        Scheduler.Reservation reservation, TaskCompletionSource<bool> ready)
    {
        try
        {
            await reservation.Started.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            ready.TrySetResult(false);
            return;
        }

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
            if (_disposed || !ReferenceEquals(_reservation, reservation))
            {
                reservation.Dispose();
                ready.TrySetResult(false);
                return;
            }
            _participant = participant;
            _preparationError = error;
        }
        ready.TrySetResult(true);
        if (error is not null)
            _bus.SendStatusMessage(request.RunId, $"[Estimation] Worker preparation failed: {error.Message}");
        else
            _bus.SendStatusMessage(request.RunId, "[Estimation] Worker has the RunServer execution slot.");
    }

    private void OnCandidatesReceived(object sender, IReadOnlyList<SharedEstimationCandidate> candidates)
    {
        _candidateQueue.Writer.TryWrite(new WorkerCommand(candidates));
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
                        : "Worker has the RunServer execution slot.",
                    ActiveWorkers: 1,
                    EvaluationsPending: _activeCandidateCount)
            ];
        }
    }

    private async Task ProcessCandidateQueueAsync()
    {
        await foreach (var command in _candidateQueue.Reader.ReadAllAsync())
        {
            ProcessCandidates(command.Candidates);
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
        Task<bool>? readyTask;
        lock (_sync)
        {
            readyTask = _assignmentReady?.Task;
            if (candidates.Count > 0)
            {
                _processingRunId = candidates[0].RunId;
                _activeBatchId = candidates[0].BatchId;
                _activeCandidateCount = candidates.Count;
            }
        }
        try
        {
            var isReady = readyTask is not null && readyTask.GetAwaiter().GetResult();
            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (!isReady || runId is null || candidate.RunId != runId)
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
                    lock (_sync)
                    {
                        if (_runId != candidate.RunId)
                        {
                            results[i] = CreateError(candidate, "The shared-estimation assignment was removed.", null, null);
                            continue;
                        }
                        _evaluatingCandidate = true;
                    }
                    try
                    {
                        results[i] = participant.Evaluate(candidate);
                    }
                    finally
                    {
                        Scheduler.Reservation[] release;
                        lock (_sync)
                        {
                            _evaluatingCandidate = false;
                            release = _reservationsToReleaseWhenIdle.ToArray();
                            _reservationsToReleaseWhenIdle.Clear();
                        }
                        foreach (var reservation in release)
                            reservation.Dispose();
                    }
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
        => ClearAssignment(runId);

    private void ReleaseOrDefer(Scheduler.Reservation reservation)
    {
        lock (_sync)
        {
            if (_evaluatingCandidate)
            {
                _reservationsToReleaseWhenIdle.Add(reservation);
                return;
            }
        }
        reservation.Dispose();
    }

    private void ClearAssignment(string runId, Scheduler.Reservation? expectedReservation = null)
    {
        Scheduler.Reservation? release = null;
        lock (_sync)
        {
            if (!_disposed && _runId == runId
                && (expectedReservation is null || ReferenceEquals(_reservation, expectedReservation)))
            {
                _runId = null;
                _participant = null;
                _preparationError = null;
                _assignmentReady?.TrySetResult(false);
                _assignmentReady = null;
                release = _reservation;
                _reservation = null;
                _activeBatchId = null;
                _activeCandidateCount = 0;
            }
        }
        if (release is not null)
            ReleaseOrDefer(release);
    }

    private static SharedEstimationEvaluationResult CreateError(
        SharedEstimationCandidate candidate, string message, string? moduleName, Guid? elementId)
        => new(candidate.RunId, candidate.BatchId, candidate.CandidateId, double.NaN,
            message, moduleName, elementId);

    public void Dispose()
    {
        Scheduler.Reservation? release;
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
            _assignmentReady?.TrySetResult(false);
            _assignmentReady = null;
            release = _reservation;
            _reservation = null;
        }

        if (release is not null)
            ReleaseOrDefer(release);

        _candidateQueue.Writer.TryComplete();
        _bus.SharedEstimationRunRequested -= OnRunRequested;
        _bus.SharedEstimationCandidatesReceived -= OnCandidatesReceived;
        _bus.SharedEstimationCancellationRequested -= OnCancellationRequested;
    }
}