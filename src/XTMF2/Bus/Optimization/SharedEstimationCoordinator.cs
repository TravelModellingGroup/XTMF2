using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTMF2.Bus;

namespace XTMF2.Bus.Optimization;

public sealed class SharedEstimationCoordinator : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, WorkerSlot> _workers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingEvaluation> _pending = new(StringComparer.Ordinal);
    private readonly List<EvaluationBatch> _batches = [];
    private string? _lastAssignedWorkerId;
    private bool _disposed;

    public event Action<string>? WorkerRemoved;

    public int ActiveWorkerCount
    {
        get
        {
            lock (_gate)
                return _workers.Values.Count(slot => slot.IsReady);
        }
    }

    public bool AddWorker(ISharedEstimationWorker worker, out string? error, bool isReady = true)
    {
        ArgumentNullException.ThrowIfNull(worker);
        error = null;
        List<Dispatch> dispatches;
        List<(EvaluationBatch Batch, SharedEstimationEvaluationResult Result)> completed = [];
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_workers.ContainsKey(worker.WorkerId))
            {
                error = $"A worker with ID '{worker.WorkerId}' is already registered.";
                return false;
            }

            var slot = new WorkerSlot(worker, isReady);
            _workers.Add(worker.WorkerId, slot);
            worker.ResultReceived += OnWorkerResultReceived;
            worker.Disconnected += OnWorkerDisconnected;
            dispatches = BuildDispatchesLocked();
        }

        SendDispatches(dispatches);
        return true;
    }

    public bool SetWorkerReady(string workerId, bool isReady, out string? error)
    {
        error = null;
        List<Dispatch> dispatches;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_workers.TryGetValue(workerId, out var slot))
            {
                error = $"Worker '{workerId}' is not registered.";
                return false;
            }

            slot.IsReady = isReady;
            if (!isReady && slot.AssignedCandidateId is { } candidateId)
            {
                if (_pending.TryGetValue(candidateId, out var pending))
                    pending.AssignedWorkerId = null;
                slot.AssignedCandidateId = null;
            }
            dispatches = isReady ? BuildDispatchesLocked() : [];
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
        CancellationToken cancellationToken = default,
        Action<string, SharedEstimationCandidate, SharedEstimationEvaluationResult>? evaluationCompleted = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
            return Task.FromResult<IReadOnlyList<SharedEstimationEvaluationResult>>([]);
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<IReadOnlyList<SharedEstimationEvaluationResult>>(cancellationToken);

        var batch = new EvaluationBatch(candidates, evaluationCompleted);
        List<Dispatch> dispatches;
        List<(Action<SharedEstimationEvaluationResult>? Callback, SharedEstimationEvaluationResult Result)> completed = [];
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

    private void OnWorkerResultReceived(object? sender, SharedEstimationEvaluationResult result)
    {
        if (sender is not ISharedEstimationWorker worker)
            return;

        List<Dispatch> dispatches;
        List<(EvaluationBatch Batch, string WorkerId, SharedEstimationCandidate Candidate,
            SharedEstimationEvaluationResult Result)> completed = [];
        lock (_gate)
        {
            if (_disposed || !_workers.ContainsKey(worker.WorkerId))
                return;

            if (_workers.TryGetValue(worker.WorkerId, out var assignedSlot)
                && assignedSlot.AssignedCandidateId == result.CandidateId)
                assignedSlot.AssignedCandidateId = null;

            if (_pending.TryGetValue(result.CandidateId, out var pending)
                && pending.AssignedWorkerId == worker.WorkerId)
            {
                _pending.Remove(result.CandidateId);
                pending.Batch.Results[result.CandidateId] = result;
                pending.Batch.PendingProgressCallbacks++;
                completed.Add((pending.Batch, worker.WorkerId, pending.Candidate, result));
            }
            dispatches = BuildDispatchesLocked();
        }

        foreach (var item in completed)
        {
            try
            {
                item.Batch.EvaluationCompleted?.Invoke(item.WorkerId, item.Candidate, item.Result);
            }
            catch (Exception)
            {
            }
            finally
            {
                lock (_gate)
                {
                    item.Batch.PendingProgressCallbacks--;
                    TryCompleteBatchLocked(item.Batch);
                }
            }
        }

        SendDispatches(dispatches);
    }

    private void OnWorkerDisconnected(object? sender, EventArgs e)
    {
        if (sender is ISharedEstimationWorker worker)
            RemoveWorkerAfterDisconnect(worker);
    }

    private void RemoveWorkerAfterDisconnect(ISharedEstimationWorker worker)
    {
        WorkerSlot? slot;
        List<Dispatch> dispatches;
        lock (_gate)
        {
            if (_disposed || !_workers.TryGetValue(worker.WorkerId, out slot)
                || !ReferenceEquals(slot.Worker, worker))
            {
                slot = null;
                dispatches = [];
            }
            else
            {
                _workers.Remove(worker.WorkerId);
                Unsubscribe(slot);
                RequeueWorkerJobsLocked(worker.WorkerId);
                dispatches = BuildDispatchesLocked();
            }
        }

        if (slot is null)
        {
            worker.Dispose();
            return;
        }

        slot.Worker.Dispose();
        WorkerRemoved?.Invoke(worker.WorkerId);
        SendDispatches(dispatches);
    }

    private List<Dispatch> BuildDispatchesLocked()
    {
        var dispatches = new List<Dispatch>();
        var workers = _workers.Values.ToArray();
        if (workers.Length == 0)
            return dispatches;

        int startIndex = Array.FindIndex(workers,
            slot => slot.Worker.WorkerId == _lastAssignedWorkerId);
        startIndex = (startIndex + 1) % workers.Length;
        var unassigned = _pending.Values.Where(pending => pending.AssignedWorkerId is null).ToArray();
        int candidateIndex = 0;
        for (int offset = 0; offset < workers.Length && candidateIndex < unassigned.Length; offset++)
        {
            var slot = workers[(startIndex + offset) % workers.Length];
            if (!slot.IsReady || slot.AssignedCandidateId is not null)
                continue;

            var pending = unassigned[candidateIndex++];
            pending.AssignedWorkerId = slot.Worker.WorkerId;
            slot.AssignedCandidateId = pending.Candidate.CandidateId;
            _lastAssignedWorkerId = slot.Worker.WorkerId;
            dispatches.Add(new Dispatch(slot.Worker, pending.Candidate));
        }
        return dispatches;
    }

    private void SendDispatches(IReadOnlyList<Dispatch> dispatches)
    {
        foreach (var dispatch in dispatches)
            _ = Task.Run(() => SendDispatch(dispatch));
    }

    private void SendDispatch(Dispatch dispatch)
    {
        lock (_gate)
        {
            if (_disposed
                || !_workers.TryGetValue(dispatch.Worker.WorkerId, out var slot)
                || !ReferenceEquals(slot.Worker, dispatch.Worker)
                || slot.AssignedCandidateId != dispatch.Candidate.CandidateId)
                return;
        }

        bool sent;
        try
        {
            sent = dispatch.Worker.SendCandidate(dispatch.Candidate, out _);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            sent = false;
        }
        if (!sent)
            RemoveWorkerAfterDisconnect(dispatch.Worker);
    }

    private void RequeueWorkerJobsLocked(string workerId)
    {
        foreach (var pending in _pending.Values.ToArray())
        {
            if (pending.AssignedWorkerId != workerId)
                continue;

            pending.AssignedWorkerId = null;
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
        }
    }

    private void TryCompleteBatchLocked(EvaluationBatch batch)
    {
        if (batch.Results.Count != batch.Candidates.Count || batch.PendingProgressCallbacks != 0)
            return;
        _batches.Remove(batch);
        batch.Cancellation.Dispose();
        batch.Completion.TrySetResult(batch.Candidates
            .Select(candidate => batch.Results[candidate.CandidateId])
            .ToArray());
    }

    private void Unsubscribe(WorkerSlot slot)
    {
        slot.Worker.ResultReceived -= OnWorkerResultReceived;
        slot.Worker.Disconnected -= OnWorkerDisconnected;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class WorkerSlot(ISharedEstimationWorker worker, bool isReady)
    {
        public ISharedEstimationWorker Worker { get; } = worker;
        public bool IsReady { get; set; } = isReady;
        public string? AssignedCandidateId { get; set; }
    }

    private sealed class PendingEvaluation(EvaluationBatch batch, SharedEstimationCandidate candidate)
    {
        public EvaluationBatch Batch { get; } = batch;
        public SharedEstimationCandidate Candidate { get; } = candidate;
        public string? AssignedWorkerId { get; set; }
    }

    private sealed class EvaluationBatch(
        IReadOnlyList<SharedEstimationCandidate> candidates,
        Action<string, SharedEstimationCandidate, SharedEstimationEvaluationResult>? evaluationCompleted)
    {
        public IReadOnlyList<SharedEstimationCandidate> Candidates { get; } = candidates;
        public Action<string, SharedEstimationCandidate, SharedEstimationEvaluationResult>? EvaluationCompleted { get; } = evaluationCompleted;
        public int PendingProgressCallbacks { get; set; }
        public Dictionary<string, SharedEstimationEvaluationResult> Results { get; } = new(StringComparer.Ordinal);
        public TaskCompletionSource<IReadOnlyList<SharedEstimationEvaluationResult>> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenRegistration Cancellation { get; set; }
    }

    private sealed record Dispatch(ISharedEstimationWorker Worker, SharedEstimationCandidate Candidate);
}
