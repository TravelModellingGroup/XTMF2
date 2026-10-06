using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading.Channels;
using XTMF2.Bus;

namespace XTMF2.Bus.Optimization;

/// <summary>
/// Binds shared-estimation protocol events on a RunServerBus to a prepared worker participant.
/// </summary>
public sealed class SharedEstimationWorkerSession : IDisposable
{
    private readonly RunServerBus _bus;
    private readonly object _sync = new();
    private SharedEstimationWorkerProcess? _workerProcess;
    private string? _runId;
    private RunError? _preparationError;
    private bool _disposed;
    private long? _activeBatchId;
    private string? _processingRunId;
    private Scheduler.ReservationLease? _reservation;
    private readonly List<Scheduler.ReservationLease> _reservationsToReleaseWhenIdle = new();
    private TaskCompletionSource<bool>? _assignmentReady;
    private bool _evaluatingCandidate;
    private readonly Channel<SharedEstimationCandidate> _candidateQueue = Channel.CreateUnbounded<SharedEstimationCandidate>();

    internal SharedEstimationWorkerSession(RunServerBus bus)
    {
        _bus = bus;
        _bus.SharedEstimationRunRequested += OnRunRequested;
        _bus.SharedEstimationCandidateReceived += OnCandidateReceived;
        _bus.SharedEstimationCancellationRequested += OnCancellationRequested;
        _ = Task.Run(ProcessCandidateQueueAsync);
    }

    private void OnRunRequested(object sender, SharedEstimationRunRequest request)
    {
        Console.WriteLine($"RunServer estimation run issued: {request.RunId}");
        Console.Out.Flush();

        Scheduler.ReservationLease reservation;
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
        Scheduler.ReservationLease? previousReservation;
        SharedEstimationWorkerProcess? previousWorkerProcess;
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            if (_disposed)
            {
                reservation.Dispose();
                return;
            }
            previousReservation = _reservation;
            previousWorkerProcess = _workerProcess;
            _runId = request.RunId;
            _workerProcess = null;
            _preparationError = null;
            _reservation = reservation;
            _assignmentReady = ready;
            reservation.SetCancellationHandler(() => ClearAssignment(request.RunId, reservation));
        }
        previousWorkerProcess?.Dispose();
        if (previousReservation is not null)
            ReleaseOrDefer(previousReservation);

        _ = PrepareWhenScheduled(request, reservation, ready);

        _bus.SendStatusMessage(request.RunId,
            "[Estimation] Worker queued with the RunServer model-system runs.");
    }

    private async Task PrepareWhenScheduled(SharedEstimationRunRequest request,
        Scheduler.ReservationLease reservation, TaskCompletionSource<bool> ready)
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

        SharedEstimationWorkerProcess? workerProcess = null;
        RunError? error = null;
        try
        {
            if (!SharedEstimationWorkerProcess.TryStart(request, _bus.ExtraDlls,
                    out workerProcess, out var processError))
                error = new RunError(RunErrorType.Validation,
                    processError ?? "Unable to start the estimation worker process.", null, string.Empty);
        }
        catch (Exception exception)
        {
            error = new RunError(RunErrorType.Validation, exception.Message, null, exception.StackTrace);
        }

        lock (_sync)
        {
            if (_disposed || !ReferenceEquals(_reservation, reservation))
            {
                workerProcess?.Dispose();
                reservation.Dispose();
                ready.TrySetResult(false);
                return;
            }
            _workerProcess = workerProcess;
            _preparationError = error;
        }
        ready.TrySetResult(true);
        try
        {
            _bus.SendSharedEstimationWorkerReady(new SharedEstimationWorkerReady(
                request.RunId, workerProcess is not null && error is null, error?.Message));
        }
        catch (Exception exception) when (exception is System.IO.IOException or ObjectDisposedException
            or InvalidOperationException or ArgumentException)
        {
        }
        if (error is not null)
        {
            _bus.SendStatusMessage(request.RunId, $"[Estimation] Worker preparation failed: {error.Message}");
            ClearAssignment(request.RunId, reservation);
        }
        else
            _bus.SendStatusMessage(request.RunId, "[Estimation] Worker has the RunServer execution slot.");
    }

    private void OnCandidateReceived(object sender, SharedEstimationCandidate candidate)
    {
        _candidateQueue.Writer.TryWrite(candidate);
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
                        ? $"Evaluating candidate, batch {_activeBatchId}."
                        : "Worker has the RunServer execution slot.",
                    ActiveWorkers: 1,
                    EvaluationsPending: isEvaluating ? 1 : 0)
            ];
        }
    }

    private async Task ProcessCandidateQueueAsync()
    {
        await foreach (var candidate in _candidateQueue.Reader.ReadAllAsync())
        {
            ProcessCandidate(candidate);
        }
    }

    private void ProcessCandidate(SharedEstimationCandidate candidate)
    {
        Task<bool>? readyTask;
        Scheduler.ReservationLease? assignmentReservation;
        string? assignmentRunId;
        lock (_sync)
        {
            readyTask = _assignmentReady?.Task;
            assignmentReservation = _reservation;
            assignmentRunId = _runId;
            _processingRunId = candidate.RunId;
            _activeBatchId = candidate.BatchId;
        }
        try
        {
            var preparationCompleted = readyTask is not null && readyTask.GetAwaiter().GetResult();
            bool isReady;
            SharedEstimationWorkerProcess? activeWorkerProcess;
            RunError? preparationError;
            string? runId;
            lock (_sync)
            {
                if (_disposed)
                    return;
                isReady = preparationCompleted
                    && assignmentReservation is not null
                    && ReferenceEquals(_reservation, assignmentReservation)
                    && _runId == assignmentRunId;
                activeWorkerProcess = _workerProcess;
                preparationError = _preparationError;
                runId = assignmentRunId;
            }

            SharedEstimationEvaluationResult result;
            if (!isReady || runId is null || candidate.RunId != runId)
            {
                result = CreateError(candidate, "No matching shared-estimation run is active.", null, null);
            }
            else if (preparationError is not null)
            {
                result = CreateError(candidate, preparationError.Message ?? "Worker preparation failed.",
                    preparationError.ModuleName, preparationError.ElementId);
            }
            else if (activeWorkerProcess is null)
            {
                result = CreateError(candidate, "The shared-estimation worker is not prepared.", null, null);
            }
            else
            {
                bool shouldEvaluate;
                lock (_sync)
                {
                    shouldEvaluate = _runId == candidate.RunId;
                    if (shouldEvaluate)
                        _evaluatingCandidate = true;
                }
                if (!shouldEvaluate)
                {
                    result = CreateError(candidate, "The shared-estimation assignment was removed.", null, null);
                }
                else
                {
                    try
                    {
                        result = activeWorkerProcess.Evaluate(candidate);
                    }
                    finally
                    {
                        Scheduler.ReservationLease[] release;
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

            _bus.SendSharedEstimationResult(result);
        }
        finally
        {
            lock (_sync)
            {
                if (_processingRunId == candidate.RunId)
                {
                    _processingRunId = null;
                    _activeBatchId = null;
                }
            }
        }
    }

    private void OnCancellationRequested(object sender, string runId, string? reason)
    {
        Scheduler.ReservationLease? reservation;
        lock (_sync)
            reservation = _runId == runId ? _reservation : null;
        if (reservation is null)
            ClearAssignment(runId);
        else
            reservation.CancelGroup();
    }

    private void ReleaseOrDefer(Scheduler.ReservationLease reservation)
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

    private void ClearAssignment(string runId, Scheduler.ReservationLease? expectedReservation = null)
    {
        Scheduler.ReservationLease? release = null;
        SharedEstimationWorkerProcess? workerProcess = null;
        lock (_sync)
        {
            if (!_disposed && _runId == runId
                && (expectedReservation is null || ReferenceEquals(_reservation, expectedReservation)))
            {
                _runId = null;
                workerProcess = _workerProcess;
                _workerProcess = null;
                _preparationError = null;
                _assignmentReady?.TrySetResult(false);
                _assignmentReady = null;
                release = _reservation;
                _reservation = null;
                _activeBatchId = null;
            }
        }
        workerProcess?.Dispose();
        if (release is not null)
            ReleaseOrDefer(release);
    }

    private static SharedEstimationEvaluationResult CreateError(
        SharedEstimationCandidate candidate, string message, string? moduleName, Guid? elementId)
        => new(candidate.RunId, candidate.BatchId, candidate.CandidateId, double.NaN,
            message, moduleName, elementId);

    public void Dispose()
    {
        Scheduler.ReservationLease? release;
        SharedEstimationWorkerProcess? workerProcess;
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            _runId = null;
            workerProcess = _workerProcess;
            _workerProcess = null;
            _preparationError = null;
            _activeBatchId = null;
            _processingRunId = null;
            _assignmentReady?.TrySetResult(false);
            _assignmentReady = null;
            release = _reservation;
            _reservation = null;
        }

        workerProcess?.Dispose();
        if (release is not null)
            ReleaseOrDefer(release);

        _candidateQueue.Writer.TryComplete();
        _bus.SharedEstimationRunRequested -= OnRunRequested;
        _bus.SharedEstimationCandidateReceived -= OnCandidateReceived;
        _bus.SharedEstimationCancellationRequested -= OnCancellationRequested;
    }
}