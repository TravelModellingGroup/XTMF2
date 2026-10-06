using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Bus;

namespace XTMF2.UnitTests.Bus;

[TestClass]
public class TestSchedulerDrain
{
    [TestMethod]
    public void BeginDrain_RejectsNewRunsAndReservations()
    {
        using var scheduler = new Scheduler(runLocal: true);
        Assert.IsTrue(RunContext.CreateRunContext(null!, "run-1", [], Path.GetTempPath(),
            "Start", RunMode.Normal, out var context));

        scheduler.BeginDrain();

        Assert.IsFalse(scheduler.IsAcceptingWork);
        Assert.Throws<InvalidOperationException>(() => scheduler.Run(context, new Sink()));
        Assert.Throws<InvalidOperationException>(() => scheduler.Reserve(context));
        Assert.IsTrue(scheduler.IsIdle);
    }

    private sealed class Sink : IRunOutputSink
    {
        public System.Collections.Generic.IReadOnlyList<string> ExtraDlls => Array.Empty<string>();
        public System.Threading.Tasks.Task StartProcessingRequestFromRun(string id, Stream clientToRunStream)
            => System.Threading.Tasks.Task.CompletedTask;
        public void ModelRunFailedValidation(string runId, string error, string moduleName = null, string elementId = null) { }
        public void ModelRunFailed(string runId, string message, string stackTrace, string moduleName = null, string elementId = null) { }
        public void SendStatusMessage(string runId, string message) { }
        public void SendOptimizationResults(string runId, System.Collections.Generic.IReadOnlyList<(int nodeIndex, double value)> results) { }
        public void SendIterationProgress(string runId, int iteration, double fitness, int fitnessTestsThisIteration,
            System.Collections.Generic.IReadOnlyList<(int nodeIndex, double value)> values) { }
        public void ModelRunComplete(string runId) { }
        public void SendRunArtifacts(string runId, string runDirectory) { }
    }
}