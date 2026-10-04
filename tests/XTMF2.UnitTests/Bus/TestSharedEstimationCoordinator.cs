using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Bus;
using XTMF2.Bus.Optimization;

namespace XTMF2.UnitTests.Bus;

[TestClass]
public class TestSharedEstimationCoordinator
{
    [TestMethod]
    public async Task EvaluateAsync_DispatchesAcrossWorkersAndPreservesCandidateOrder()
    {
        using var coordinator = new SharedEstimationCoordinator();
        using var first = new FakeWorker("worker-1");
        using var second = new FakeWorker("worker-2");
        Assert.IsTrue(coordinator.AddWorker(first, out var error), error);
        Assert.IsTrue(coordinator.AddWorker(second, out error), error);

        var candidates = new[]
        {
            Candidate("candidate-1"),
            Candidate("candidate-2"),
            Candidate("candidate-3")
        };
        var completed = new ConcurrentQueue<(string WorkerId, SharedEstimationEvaluationResult Result)>();
        var evaluation = coordinator.EvaluateAsync(candidates,
            evaluationCompleted: (workerId, _, result) => completed.Enqueue((workerId, result)));

        await first.WaitForSentCountAsync(1);
        await second.WaitForSentCountAsync(1);
        Assert.HasCount(1, first.Sent);
        Assert.HasCount(1, second.Sent);
        Assert.AreEqual("candidate-1", first.Sent.Single().CandidateId);
        Assert.AreEqual("candidate-2", second.Sent.Single().CandidateId);

        second.Complete("candidate-2", 2.0);
        await second.WaitForSentCountAsync(2);
        Assert.HasCount(1, completed);
        Assert.AreEqual("candidate-3", second.Sent.Last().CandidateId);
        first.Complete("candidate-1", 1.0);
        Assert.HasCount(2, completed);
        second.Complete("candidate-3", 3.0);
        Assert.HasCount(3, completed);

        var results = await evaluation;
        Assert.HasCount(3, results);
        CollectionAssert.AreEquivalent(new[] { "worker-1", "worker-2", "worker-2" },
            completed.Select(item => item.WorkerId).ToArray());
        CollectionAssert.AreEqual(
            new[] { "candidate-1", "candidate-2", "candidate-3" },
            results.Select(result => result.CandidateId).ToArray());
    }

    [TestMethod]
    public async Task EvaluateAsync_AllowsSynchronousWorkersToCompleteDuringAsyncDispatch()
    {
        using var coordinator = new SharedEstimationCoordinator();
        using var local = new FakeWorker("coordinator");
        using var remote = new FakeWorker("remote");
        local.OnSend = candidate => local.Complete(candidate.CandidateId, 1.0);
        remote.OnSend = candidate => remote.Complete(candidate.CandidateId, 2.0);
        Assert.IsTrue(coordinator.AddWorker(local, out var error), error);
        Assert.IsTrue(coordinator.AddWorker(remote, out error), error);

        var evaluation = coordinator.EvaluateAsync([
            Candidate("candidate-1"),
            Candidate("candidate-2"),
            Candidate("candidate-3"),
            Candidate("candidate-4")
        ]);

        var results = await evaluation;
        await local.WaitForSentCountAsync(1);
        await remote.WaitForSentCountAsync(1);

        Assert.HasCount(4, results);
        CollectionAssert.AreEqual(
            new[] { "candidate-1", "candidate-2", "candidate-3", "candidate-4" },
            results.Select(result => result.CandidateId).ToArray());
    }

    [TestMethod]
    public async Task EvaluateAsync_RequeuesAssignedCandidateAfterDisconnect()
    {
        using var coordinator = new SharedEstimationCoordinator();
        using var first = new FakeWorker("worker-1");
        using var replacement = new FakeWorker("worker-2");
        using var finalWorker = new FakeWorker("worker-3");
        Assert.IsTrue(coordinator.AddWorker(first, out var error), error);
        Assert.IsTrue(coordinator.AddWorker(replacement, out error), error);

        var evaluation = coordinator.EvaluateAsync([Candidate("candidate-1")]);
        await first.WaitForSentCountAsync(1);
        first.Disconnect();
        Assert.AreEqual(1, coordinator.ActiveWorkerCount);

        await replacement.WaitForSentCountAsync(1);
        Assert.AreEqual("candidate-1", replacement.Sent.Single().CandidateId);
        first.Complete("candidate-1", 99.0);
        replacement.Disconnect();
        Assert.IsTrue(coordinator.AddWorker(finalWorker, out error), error);
        await finalWorker.WaitForSentCountAsync(1);
        Assert.AreEqual("candidate-1", finalWorker.Sent.Single().CandidateId);
        finalWorker.Complete("candidate-1", 4.0);

        var result = (await evaluation).Single();
        Assert.AreEqual("candidate-1", result.CandidateId);
        Assert.AreEqual(4.0, result.Fitness);
    }

    [TestMethod]
    public async Task EvaluateAsync_RequeuesCandidateWhenWorkerSendThrows()
    {
        using var coordinator = new SharedEstimationCoordinator();
        using var failedWorker = new FakeWorker("worker-failed")
        {
            SendException = new InvalidOperationException("Stream was not writable.")
        };
        using var replacement = new FakeWorker("worker-replacement");
        Assert.IsTrue(coordinator.AddWorker(failedWorker, out var error), error);

        var evaluation = coordinator.EvaluateAsync([Candidate("candidate-send-error")]);

        await failedWorker.WaitForDisposedAsync();
        Assert.IsTrue(failedWorker.IsDisposed);
        Assert.AreEqual(0, coordinator.ActiveWorkerCount);
        Assert.IsFalse(evaluation.IsCompleted);
        Assert.IsTrue(coordinator.AddWorker(replacement, out error), error);
        await replacement.WaitForSentCountAsync(1);
        Assert.AreEqual("candidate-send-error", replacement.Sent.Single().CandidateId);
        replacement.Complete("candidate-send-error", 6.0);

        var result = (await evaluation).Single();
        Assert.AreEqual(6.0, result.Fitness);
    }

    [TestMethod]
    public async Task EvaluateAsync_DistributesSequentialCandidatesRoundRobin()
    {
        using var coordinator = new SharedEstimationCoordinator();
        using var first = new FakeWorker("worker-1");
        using var second = new FakeWorker("worker-2");
        Assert.IsTrue(coordinator.AddWorker(first, out var error), error);
        Assert.IsTrue(coordinator.AddWorker(second, out error), error);

        var firstEvaluation = coordinator.EvaluateAsync([Candidate("candidate-1")]);
        await first.WaitForSentCountAsync(1);
        first.Complete("candidate-1", 1.0);
        await firstEvaluation;

        var secondEvaluation = coordinator.EvaluateAsync([Candidate("candidate-2")]);
        await second.WaitForSentCountAsync(1);
        Assert.AreEqual("candidate-2", second.Sent.Single().CandidateId);
        second.Complete("candidate-2", 2.0);
        await secondEvaluation;

        var nextFirstEvaluation = coordinator.EvaluateAsync([Candidate("candidate-3")]);
        await first.WaitForSentCountAsync(2);
        Assert.AreEqual("candidate-3", first.Sent.Last().CandidateId);
        first.Complete("candidate-3", 3.0);
        await nextFirstEvaluation;
    }

    private static SharedEstimationCandidate Candidate(string id)
        => new("run-1", 1, id, new[] { 1.0 });

    private sealed class FakeWorker(string workerId) : ISharedEstimationWorker
    {
        private readonly object _sync = new();
        private readonly List<SharedEstimationCandidate> _sent = [];
        private TaskCompletionSource _sentChanged = NewSignal();
        private readonly TaskCompletionSource _disposed = NewSignal();

        public string WorkerId { get; } = workerId;
        public IReadOnlyList<SharedEstimationCandidate> Sent
        {
            get
            {
                lock (_sync)
                    return _sent.ToArray();
            }
        }
        public bool IsDisposed => _disposed.Task.IsCompleted;
        public Exception SendException { get; init; } = null!;
        public Action<SharedEstimationCandidate> OnSend { get; set; } = null!;

        public event EventHandler<SharedEstimationEvaluationResult> ResultReceived;
        public event EventHandler Disconnected;

        public bool SendCandidate(SharedEstimationCandidate candidate, out string error)
        {
            if (SendException is not null)
                throw SendException;
            error = null;
            TaskCompletionSource changed;
            lock (_sync)
            {
                _sent.Add(candidate);
                changed = _sentChanged;
                _sentChanged = NewSignal();
            }
            changed.TrySetResult();
            OnSend?.Invoke(candidate);
            return !IsDisposed;
        }

        public async Task WaitForSentCountAsync(int count)
        {
            while (true)
            {
                Task changed;
                lock (_sync)
                {
                    if (_sent.Count >= count)
                        return;
                    changed = _sentChanged.Task;
                }
                await changed.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        public Task WaitForDisposedAsync() => _disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public void Complete(string candidateId, double fitness)
        {
            var candidate = Sent.Single(candidate => candidate.CandidateId == candidateId);
            ResultReceived?.Invoke(this, new SharedEstimationEvaluationResult(
                candidate.RunId, candidate.BatchId, candidate.CandidateId, fitness, null, null, null));
        }

        public void Disconnect() => Disconnected?.Invoke(this, EventArgs.Empty);

        public void Dispose() => _disposed.TrySetResult();

        private static TaskCompletionSource NewSignal()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
