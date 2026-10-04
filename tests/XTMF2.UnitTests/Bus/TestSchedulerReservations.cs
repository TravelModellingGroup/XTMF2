using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Bus;

namespace XTMF2.UnitTests.Bus;

[TestClass]
public class TestSchedulerReservations
{
    [TestMethod]
    public async Task Reservation_HoldsOrdinaryRunsUntilReleased()
    {
        using var scheduler = new Scheduler(runLocal: true);
        Assert.IsTrue(RunContext.CreateRunContext(null!, "worker-run", [], Path.GetTempPath(),
            "Start", RunMode.Normal, out var workerContext));
        Assert.IsTrue(RunContext.CreateRunContext(null!, "ordinary-run", [], Path.GetTempPath(),
            "Start", RunMode.Normal, out var ordinaryContext));
        using var reservation = scheduler.Reserve(workerContext);
        var sink = new RecordingRunOutputSink();

        await reservation.Started.WaitAsync(TimeSpan.FromSeconds(3));
        scheduler.Run(ordinaryContext, sink);

        var inventory = scheduler.GetInventory();
        Assert.HasCount(2, inventory);
        Assert.IsTrue(inventory[0].IsRunning);
        Assert.IsTrue(inventory[0].IsReservation);
        Assert.AreEqual("ordinary-run", inventory[1].Context.ID);
        Assert.IsFalse(inventory[1].IsRunning);
        Assert.IsFalse(sink.RunFailed.Task.IsCompleted);

        reservation.Dispose();
        Assert.AreEqual("ordinary-run",
            await sink.RunFailed.Task.WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [TestMethod]
    public async Task WorkerLeases_ShareOneScheduledTaskAndHoldOrdinaryRuns()
    {
        using var scheduler = new Scheduler(runLocal: true);
        Assert.IsTrue(RunContext.CreateRunContext(null!, "worker-run-1", [], Path.GetTempPath(),
            "Start", RunMode.Normal, out var firstWorkerContext));
        Assert.IsTrue(RunContext.CreateRunContext(null!, "worker-run-2", [], Path.GetTempPath(),
            "Start", RunMode.Normal, out var secondWorkerContext));
        Assert.IsTrue(RunContext.CreateRunContext(null!, "ordinary-run-1", [], Path.GetTempPath(),
            "Start", RunMode.Normal, out var firstOrdinaryContext));
        Assert.IsTrue(RunContext.CreateRunContext(null!, "ordinary-run-2", [], Path.GetTempPath(),
            "Start", RunMode.Normal, out var ordinaryContext));
        var firstSink = new RecordingRunOutputSink();
        var secondSink = new RecordingRunOutputSink();

        using var firstLease = scheduler.ReserveGroup("estimation-run", firstWorkerContext, 2);
        await firstLease.Started.WaitAsync(TimeSpan.FromSeconds(3));
        scheduler.Run(firstOrdinaryContext, firstSink);

        using var secondLease = scheduler.ReserveGroup("estimation-run", secondWorkerContext, 2);
        await secondLease.Started.WaitAsync(TimeSpan.FromSeconds(3));
        scheduler.Run(ordinaryContext, secondSink);

        var inventory = scheduler.GetInventory();
        Assert.AreEqual(1, inventory.Count(item => item.IsRunning && item.IsReservation));
        Assert.IsFalse(firstSink.RunFailed.Task.IsCompleted);
        Assert.IsFalse(secondSink.RunFailed.Task.IsCompleted);

        firstLease.Dispose();
        Assert.IsFalse(firstSink.RunFailed.Task.IsCompleted);
        Assert.IsFalse(secondSink.RunFailed.Task.IsCompleted);
        secondLease.Dispose();
        var failedRuns = await Task.WhenAll(firstSink.RunFailed.Task, secondSink.RunFailed.Task)
            .WaitAsync(TimeSpan.FromSeconds(3));
        CollectionAssert.AreEquivalent(new[] { "ordinary-run-1", "ordinary-run-2" }, failedRuns);
    }

    private sealed class RecordingRunOutputSink : IRunOutputSink
    {
        public IReadOnlyList<string> ExtraDlls => Array.Empty<string>();
        public TaskCompletionSource<string> RunFailed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task StartProcessingRequestFromRun(string id, Stream clientToRunStream) => Task.CompletedTask;
        public void ModelRunFailedValidation(string runId, string error, string moduleName = null, string elementId = null) { }
        public void ModelRunFailed(string runId, string message, string stackTrace, string moduleName = null, string elementId = null)
            => RunFailed.TrySetResult(runId);
        public void SendStatusMessage(string runId, string message) { }
        public void SendOptimizationResults(string runId, IReadOnlyList<(int nodeIndex, double value)> results) { }
        public void SendIterationProgress(string runId, int iteration, double fitness,
            int fitnessTestsThisIteration, IReadOnlyList<(int nodeIndex, double value)> values) { }
        public void ModelRunComplete(string runId) { }
        public void SendRunArtifacts(string runId, string runDirectory) { }
    }
}