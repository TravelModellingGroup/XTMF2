using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace XTMF2.Bus;

public sealed class SharedEstimationCoordinator : IDisposable
{
    private const int MaximumAttempts = 3;
    private readonly object _gate = new();
    private readonly Dictionary<string, WorkerSlot> _workers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingEvaluation> _pending = new(StringComparer.Ordinal);
    private readonly List<EvaluationBatch> _batches = [];
    private bool _disposed;

    public event Action<string>? WorkerRemoved;

    public int ActiveWorkerCount
    {
        get
        {
            lock (_gate)
                return _workers.Count;
        }
    }

    public bool AddWorker(ISharedEstimationWorker worker, out string? error)
    {
        ArgumentNullException.ThrowIfNull(worker);
        error = null;
        List<Dispatch> dispatches;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_workers.ContainsKey(worker.WorkerId))
            {
                error = $"A worker with ID '{worker.WorkerId}' is already registered.";
                return false;
            }

            var slot = new WorkerSlot(worker);
            _workers.Add(worker.WorkerId, slot);
            worker.ResultsReceived += OnWorkerResultsReceived;
            worker.Disconnected += OnWorkerDisconnected;
            dispatches = BuildDispatchesLocked();
        }

        SendDispatches(dispatches);
        return true;
    }

    public bool RemoveWorker(string workerId, out string? error)
    {
        error = null;
        WorkerSlot? slot;
        List<Dispatch> dispatches;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_workers.Remove(workerId, out slot))
            {
                error = $"Worker '{workerId}' is not registered.";
                return false;
            }

            Unsubscribe(slot);
            RequeueWorkerJobsLocked(workerId);
            dispatches = BuildDispatchesLocked();
        }

        slot.Worker.Dispose();
        WorkerRemoved?.Invoke(workerId);
        SendDispatches(dispatches);
        return true;
    }

    public Task<IReadOnlyList<SharedEstimationEvaluationResult>> EvaluateAsync(
        IReadOnlyList<SharedEstimationCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
            return Task.FromResult<IReadOnlyList<SharedEstimationEvaluationResult>>([]);
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<IReadOnlyList<SharedEstimationEvaluationResult>>(cancellationToken);

        var batch = new EvaluationBatch(candidates);
        List<Dispatch> dispatches;
        lock (_gate)
        {
            ThrowIfDisposed();
            foreach (var candidate in candidates)
            {
                if (_pending.ContainsKey(candidate.CandidateId))
                    throw new ArgumentException($"Candidate ID '{candidate.CandidateId}' is already active.", nameof(candidates));
            }

            _batches.Add(batch);
            foreach (var candidate in candidates)
                _pending.Add(candidate.CandidateId, new PendingEvaluation(batch, candidate));
            dispatches = BuildDispatchesLocked();
        }

        batch.Cancellation = cancellationToken.Register(() => CancelBatch(batch));
        SendDispatches(dispatches);
        return batch.Completion.Task;
    }

    public void Dispose()
    {
        WorkerSlot[] workers;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            foreach (var batch in _batches)
            {
                batch.Cancellation.Dispose();
                batch.Completion.TrySetCanceled();
            }
            _pending.Clear();
            _batches.Clear();
            workers = _workers.Values.ToArray();
            _workers.Clear();
            foreach (var slot in workers)
                Unsubscribe(slot);
        }

        foreach (var slot in workers)
            slot.Worker.Dispose();
    }

    private void OnWorkerResultsReceived(object? sender, IReadOnlyList<SharedEstimationEvaluationResult> results)
    {
        if (sender is not ISharedEstimationWorker worker)
            return;

        List<Dispatch> dispatches;
        lock (_gate)
        {
            if (_disposed || !_workers.ContainsKey(worker.WorkerId))
                return;

            foreach (var result in results)
            {
                if (!_pending.TryGetValue(result.CandidateId, out var pending)
                    || pending.AssignedWorkerId != worker.WorkerId)
                    continue;

                _pending.Remove(result.CandidateId);
                if (_workers.TryGetValue(worker.WorkerId, out var slot)
                    && slot.AssignedCandidateId == result.CandidateId)
                    slot.AssignedCandidateId = null;
                pending.Batch.Results[result.CandidateId] = result;
                TryCompleteBatchLocked(pending.Batch);
            }
            dispatches = BuildDispatchesLocked();
        }

        SendDispatches(dispatches);
    }

    private void OnWorkerDisconnected(object? sender, EventArgs e)
    {
        if (sender is ISharedEstimationWorker worker)
            RemoveWorkerAfterDisconnect(worker.WorkerId);
    }

    private void RemoveWorkerAfterDisconnect(string workerId)
    {
        WorkerSlot? slot;
        List<Dispatch> dispatches;
        lock (_gate)
        {
            if (_disposed || !_workers.Remove(workerId, out slot))
                return;
            Unsubscribe(slot);
            RequeueWorkerJobsLocked(workerId);
            dispatches = BuildDispatchesLocked();
        }

        WorkerRemoved?.Invoke(workerId);
        SendDispatches(dispatches);
    }

    private List<Dispatch> BuildDispatchesLocked()
    {
        var dispatches = new List<Dispatch>();
        foreach (var slot in _workers.Values)
        {
            if (slot.AssignedCandidateId is not null)
                continue;

            var pending = _pending.Values.FirstOrDefault(p => p.AssignedWorkerId is null);
            if (pending is null)
                break;

            pending.AssignedWorkerId = slot.Worker.WorkerId;
            pending.Attempts++;
            slot.AssignedCandidateId = pending.Candidate.CandidateId;
            dispatches.Add(new Dispatch(slot.Worker, pending.Candidate));
        }
        return dispatches;
    }

    private void SendDispatches(IReadOnlyList<Dispatch> dispatches)
    {
        foreach (var dispatch in dispatches)
        {
            if (dispatch.Worker.SendCandidates([dispatch.Candidate], out _))
                continue;
            RemoveWorkerAfterDisconnect(dispatch.Worker.WorkerId);
        }
    }

    private void RequeueWorkerJobsLocked(string workerId)
    {
        var failed = new List<PendingEvaluation>();
        foreach (var pending in _pending.Values.ToArray())
        {
            if (pending.AssignedWorkerId != workerId)
                continue;

            pending.AssignedWorkerId = null;
            if (pending.Attempts >= MaximumAttempts)
                failed.Add(pending);
        }

        foreach (var pending in failed)
        {
            _pending.Remove(pending.Candidate.CandidateId);
            pending.Batch.Results[pending.Candidate.CandidateId] =
                new SharedEstimationEvaluationResult(
                    pending.Candidate.RunId,
                    pending.Candidate.BatchId,
                    pending.Candidate.CandidateId,
                    double.MaxValue,
                    $"Candidate evaluation failed after {MaximumAttempts} worker attempts.",
                    null,
                    null);
            TryCompleteBatchLocked(pending.Batch);
        }
    }

    private void CancelBatch(EvaluationBatch batch)
    {
        lock (_gate)
        {
            if (batch.Completion.Task.IsCompleted)
                return;
            foreach (var candidate in batch.Candidates)
                _pending.Remove(candidate.CandidateId);
            _batches.Remove(batch);
            batch.Completion.TrySetCanceled();
            foreach (var slot in _workers.Values)
            {
                if (slot.AssignedCandidateId is not null
                    && batch.Candidates.Any(c => c.CandidateId == slot.AssignedCandidateId))
                    slot.AssignedCandidateId = null;
            }
        }
    }

    private void TryCompleteBatchLocked(EvaluationBatch batch)
    {
        if (batch.Results.Count != batch.Candidates.Count)
            return;
        _batches.Remove(batch);
        batch.Cancellation.Dispose();
        batch.Completion.TrySetResult(batch.Candidates
            .Select(candidate => batch.Results[candidate.CandidateId])
            .ToArray());
    }

    private void Unsubscribe(WorkerSlot slot)
    {
        slot.Worker.ResultsReceived -= OnWorkerResultsReceived;
        slot.Worker.Disconnected -= OnWorkerDisconnected;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class WorkerSlot(ISharedEstimationWorker worker)
    {
        public ISharedEstimationWorker Worker { get; } = worker;
        public string? AssignedCandidateId { get; set; }
    }

    private sealed class PendingEvaluation(EvaluationBatch batch, SharedEstimationCandidate candidate)
    {
        public EvaluationBatch Batch { get; } = batch;
        public SharedEstimationCandidate Candidate { get; } = candidate;
        public string? AssignedWorkerId { get; set; }
        public int Attempts { get; set; }
    }

    private sealed class EvaluationBatch(IReadOnlyList<SharedEstimationCandidate> candidates)
    {
        public IReadOnlyList<SharedEstimationCandidate> Candidates { get; } = candidates;
        public Dictionary<string, SharedEstimationEvaluationResult> Results { get; } = new(StringComparer.Ordinal);
        public TaskCompletionSource<IReadOnlyList<SharedEstimationEvaluationResult>> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenRegistration Cancellation { get; set; }
    }

    private sealed record Dispatch(ISharedEstimationWorker Worker, SharedEstimationCandidate Candidate);
}
