using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Bus;

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
        var evaluation = coordinator.EvaluateAsync(candidates);

        Assert.AreEqual("candidate-1", first.Sent.Single().CandidateId);
        Assert.AreEqual("candidate-2", second.Sent.Single().CandidateId);

        second.Complete(2.0);
        Assert.AreEqual("candidate-3", second.Sent.Skip(1).Single().CandidateId);
        first.Complete(1.0);
        second.Complete(3.0);

        var results = await evaluation;
        Assert.HasCount(3, results);
        CollectionAssert.AreEqual(
            new[] { "candidate-1", "candidate-2", "candidate-3" },
            results.Select(result => result.CandidateId).ToArray());
    }

    [TestMethod]
    public async Task EvaluateAsync_RequeuesAssignedCandidateAfterDisconnect()
    {
        using var coordinator = new SharedEstimationCoordinator();
        using var first = new FakeWorker("worker-1");
        using var replacement = new FakeWorker("worker-2");
        Assert.IsTrue(coordinator.AddWorker(first, out var error), error);

        var evaluation = coordinator.EvaluateAsync([Candidate("candidate-1")]);
        first.Disconnect();
        Assert.AreEqual(0, coordinator.ActiveWorkerCount);

        Assert.IsTrue(coordinator.AddWorker(replacement, out error), error);
        Assert.AreEqual("candidate-1", replacement.Sent.Single().CandidateId);
        replacement.Complete(4.0);

        var result = (await evaluation).Single();
        Assert.AreEqual("candidate-1", result.CandidateId);
        Assert.AreEqual(4.0, result.Fitness);
    }

    private static SharedEstimationCandidate Candidate(string id)
        => new("run-1", 1, id, new[] { 1.0 });

    private sealed class FakeWorker(string workerId) : ISharedEstimationWorker
    {
        public string WorkerId { get; } = workerId;
        public List<SharedEstimationCandidate> Sent { get; } = [];
        public bool IsDisposed { get; private set; }

        public event EventHandler<IReadOnlyList<SharedEstimationEvaluationResult>> ResultsReceived;
        public event EventHandler Disconnected;

        public bool SendCandidates(IReadOnlyList<SharedEstimationCandidate> candidates, out string error)
        {
            error = null;
            Sent.AddRange(candidates);
            return !IsDisposed;
        }

        public void Complete(double fitness)
        {
            var candidate = Sent.Last();
            ResultsReceived?.Invoke(this, [new SharedEstimationEvaluationResult(
                candidate.RunId, candidate.BatchId, candidate.CandidateId, fitness, null, null, null)]);
        }

        public void Disconnect() => Disconnected?.Invoke(this, EventArgs.Empty);

        public void Dispose() => IsDisposed = true;
    }
}
