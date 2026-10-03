using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Bus;
using XTMF2.Bus.Optimization;
using XTMF2.GUI;
using XTMF2.GUI.Properties;
using XTMF2.GUI.ViewModels;
using XTMF2;

namespace XTMF2.GUI.Tests;

[TestClass]
public class RunControllerCompletionTests
{
    [TestMethod]
    public void CompletionReceivedBeforeRunRegistration_IsReleasedAfterRegistration()
    {
        var gate = new SharedEstimationCompletionGate();
        var completion = new SharedEstimationCompletion("run-1", true, 1.25,
            new[] { 0.5, 0.75 }, 4, 2, null);

        Assert.IsTrue(gate.TryReceive(completion, contextReady: false, out var accepted));
        Assert.IsNull(accepted);
        var released = gate.MarkReady("run-1");
        Assert.AreSame(completion, released);
        Assert.IsTrue(gate.TryReceive(released!, contextReady: true, out accepted));
        Assert.AreSame(completion, accepted);
        Assert.IsFalse(gate.TryReceive(completion, contextReady: true, out _));
    }

    [TestMethod]
    public void CompletionReceivedAfterRunRegistration_IsAcceptedOnce()
    {
        var gate = new SharedEstimationCompletionGate();
        var completion = new SharedEstimationCompletion("run-2", true, 2.5,
            new[] { 0.25 }, 3, 1, null);

        Assert.IsTrue(gate.TryReceive(completion, contextReady: true, out var accepted));
        Assert.AreSame(completion, accepted);
        Assert.IsNull(gate.MarkReady("run-2"));
        Assert.IsFalse(gate.TryReceive(completion, contextReady: true, out _));
    }

    [TestMethod]
    public void MarkFinished_PreservesEstimationCompletionSummary()
    {
        TestGuiHelper.RunInModelSystemContext(nameof(MarkFinished_PreservesEstimationCompletionSummary),
            (user, _, session) =>
            {
                var run = new RunViewModel("run-3", "estimation", string.Empty, "remote", session, user);
                run.SetRunMode(RunMode.Estimation,
                    Array.Empty<(int nodeIndex, string name, double min, double max)>(), () => { });
                const string completion = "[Estimation] converged after 8 fitness tests in 3 iterations.";

                run.AppendStatus(completion);
                run.MarkFinished();

                Assert.AreEqual(RunStatus.Finished, run.Status);
                Assert.AreEqual(completion, run.StatusText);
            });
    }

    [TestMethod]
    public void RecoveredOptimizationRun_RemainsUnboundUntilExplicitBinding()
    {
        var snapshot = new RemoteRunSnapshot("run-recovered", "estimation", RunMode.Estimation,
            "/remote/runs/estimation", "Start", "model-hash", RemoteRunState.Completed,
            "Run completed.", 4, 0.125,
            new[] { new RemoteRunParameterValue(12, 1.5) },
            new[] { new RemoteRunParameterValue(12, 1.25) }, null, null, true,
            DateTimeOffset.UtcNow);

        var run = new RunViewModel(snapshot, "orchestrator");

        Assert.IsTrue(run.IsRecoveredRunUnbound);
        Assert.AreEqual(RunStatus.Finished, run.Status);
        Assert.AreEqual(4, run.CurrentIteration);
        Assert.IsTrue(run.HasOptimizationResults);
        Assert.IsFalse(run.ApplyOptimizationResultsCommand.CanExecute(null));
        Assert.AreEqual(1.5, run.OptimizationParameters[0].CurrentValue);
    }

    [TestMethod]
    public void RecoveredRemoteRun_ReportsOutputLocationAndOffersTransfer()
    {
        var snapshot = new RemoteRunSnapshot("run-output", "forecast", RunMode.Normal,
            "/remote/private/run-output", "Start", "model-hash", RemoteRunState.Completed,
            "Run completed.", 0, double.NaN, Array.Empty<RemoteRunParameterValue>(), null,
            null, null, true, DateTimeOffset.UtcNow);

        var run = new RunViewModel(snapshot, "Remote Server");

        Assert.IsTrue(run.IsRemoteRun);
        Assert.IsTrue(run.RemoteOutputAvailable);
        Assert.IsTrue(run.CanTransferRemoteOutput);
        Assert.IsFalse(run.ArtifactsAvailable);
        Assert.AreNotEqual(snapshot.WorkingDirectory, run.RunDirectory);
        Assert.AreEqual("Output is available on the RunServer.", run.OutputTransferStatus);

        run.MarkArtifactsAvailable();

        Assert.IsTrue(run.ArtifactsAvailable);
        Assert.IsFalse(run.RemoteOutputAvailable);
        Assert.IsFalse(run.CanTransferRemoteOutput);
        Assert.AreEqual("Output is on this computer.", run.OutputTransferStatus);
    }

    [TestMethod]
    public void RecoveredRemoteRun_UsesLocalReceiptDirectoryForOpenOutput()
    {
        var snapshot = new RemoteRunSnapshot("run-local-output", "forecast", RunMode.Normal,
            "/remote/server/runs/forecast", "Start", "model-hash", RemoteRunState.Completed,
            "Run completed.", 0, double.NaN, Array.Empty<RemoteRunParameterValue>(), null,
            null, null, true, DateTimeOffset.UtcNow);
        var run = new RunViewModel(snapshot, "Remote Server");
        var localReceiptDirectory = Path.Combine(Path.GetTempPath(), "XTMF2-GUI-RunOutput", snapshot.RunId);

        run.SetLocalOutputDirectory(localReceiptDirectory);

        Assert.AreEqual(localReceiptDirectory, run.RunDirectory);
        Assert.AreNotEqual(snapshot.WorkingDirectory, run.RunDirectory);
    }

    [TestMethod]
    public void RecoveredLocalRun_DoesNotOfferOutputTransfer()
    {
        var snapshot = new RemoteRunSnapshot("run-local-output", "forecast", RunMode.Normal,
            "/local/runs/forecast", "Start", "model-hash", RemoteRunState.Completed,
            "Run completed.", 0, double.NaN, Array.Empty<RemoteRunParameterValue>(), null,
            null, null, true, DateTimeOffset.UtcNow);
        var localDirectory = Directory.CreateTempSubdirectory("xtmf-local-run-output-");
        var run = new RunViewModel(snapshot, "Local RunServer");

        try
        {
            run.SetRemoteRunTracking(false);
            run.SetLocalOutputDirectory(localDirectory.FullName);

            Assert.IsFalse(run.IsRemoteRun);
            Assert.IsFalse(run.CanTransferRemoteOutput);
            Assert.IsTrue(run.ArtifactsAvailable);
            Assert.IsTrue(run.HasRunDirectory);
            Assert.AreEqual(localDirectory.FullName, run.RunDirectory);
            Assert.AreEqual("Output is on this computer.", run.OutputTransferStatus);
        }
        finally
        {
            localDirectory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void RecoveredRemoteRun_OffersTransferBeforeAvailabilitySnapshot()
    {
        var snapshot = new RemoteRunSnapshot("run-output-unknown", "forecast", RunMode.Normal,
            "/remote/private/run-output-unknown", "Start", "model-hash", RemoteRunState.Completed,
            "Run completed.", 0, double.NaN, Array.Empty<RemoteRunParameterValue>(), null,
            null, null, false, DateTimeOffset.UtcNow);

        var run = new RunViewModel(snapshot, "Remote Server");

        Assert.IsFalse(run.RemoteOutputAvailable);
        Assert.IsTrue(run.CanTransferRemoteOutput);
    }

    [TestMethod]
    public void RecoveredInterruptedRun_IsNeutralAndDoesNotLogAnError()
    {
        var snapshot = new RemoteRunSnapshot("run-interrupted", "forecast", RunMode.Normal,
            "/remote/runs/forecast", "Start", "model-hash", RemoteRunState.Interrupted,
            "RunServer restarted; saved run details restored. The run did not resume.",
            0, double.NaN, Array.Empty<RemoteRunParameterValue>(), null, null, null, false,
            DateTimeOffset.UtcNow);

        var run = new RunViewModel(snapshot, "orchestrator");

        Assert.AreEqual(RunStatus.Interrupted, run.Status);
        Assert.IsTrue(run.IsCompleted);
        Assert.AreEqual("↻", run.StatusBadge);
        Assert.AreEqual(snapshot.Status, run.StatusText);
        Assert.Contains(snapshot.Status, run.Messages);
        Assert.IsFalse(run.Messages.Any(message => message.StartsWith("Error:", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void RecoveredSharedEstimation_RestoresProgressAndWorkers()
    {
        var progress = new SharedEstimationProgress("shared-recovered", 5, 0.125,
            18, 2, 3, 4, new Dictionary<string, int> { ["worker-1"] = 6 }, [1.25, 2.5]);
        var snapshot = new SharedEstimationJobSnapshot("shared-recovered", SharedEstimationJobState.Running,
            progress, null, ["worker-1"], "restored-estimation", "/runs/restored-estimation", "model-hash",
            [new SharedEstimationParameterMetadata(14, "Fare", 0, 10),
             new SharedEstimationParameterMetadata(27, "Wait", 0, 20)]);

        var run = new RunViewModel(snapshot, "Remote Orchestrator", () => { });

        Assert.AreEqual("restored-estimation", run.RunName);
        Assert.AreEqual(RunStatus.Running, run.Status);
        Assert.IsTrue(run.IsRecoveredRunUnbound);
        Assert.IsTrue(run.IsRemoteSharedEstimation);
        Assert.AreEqual(5, run.CurrentIteration);
        Assert.AreEqual(0.125, run.CurrentFitness);
        Assert.AreEqual("Fare", run.OptimizationParameters[0].Name);
        Assert.AreEqual(1.25, run.OptimizationParameters[0].CurrentValue);
        Assert.AreEqual("Wait", run.OptimizationParameters[1].Name);
        Assert.AreEqual(2.5, run.OptimizationParameters[1].CurrentValue);
    }

    [TestMethod]
    public void RecoveredCompletedSharedEstimation_KeepsResultsUnappliableUntilBound()
    {
        var completion = new SharedEstimationCompletion("shared-complete", true, 0.25,
            [3.5], 12, 4, null);
        var snapshot = new SharedEstimationJobSnapshot("shared-complete", SharedEstimationJobState.Completed,
            null, completion, [], "completed-estimation", "/runs/completed-estimation", "model-hash",
            [new SharedEstimationParameterMetadata(18, "Mode share", 0, 1)]);

        var run = new RunViewModel(snapshot, "Remote Orchestrator", () => { });

        Assert.AreEqual(RunStatus.Finished, run.Status);
        Assert.IsTrue(run.HasOptimizationResults);
        Assert.IsTrue(run.IsRecoveredRunUnbound);
        Assert.IsFalse(run.ApplyOptimizationResultsCommand.CanExecute(null));
    }

    [TestMethod]
    public void DisconnectedEstimationWorker_IsMarkedInactiveAndReaddedAfterReconnect()
    {
        TestGuiHelper.RunInModelSystemContext(nameof(DisconnectedEstimationWorker_IsMarkedInactiveAndReaddedAfterReconnect),
            (user, _, session) =>
            {
                var run = new RunViewModel("shared-worker-reconnect", "estimation", string.Empty,
                    "coordinator", session, user);
                var endpoint = new RunServerEndpoint { Id = "worker-1", Name = "Worker 1" };
                var addRequests = 0;
                run.SetRunMode(RunMode.Estimation,
                    Array.Empty<(int nodeIndex, string name, double min, double max)>(), () => { });
                run.SetRemoteEstimationWorkers([endpoint], [endpoint.Id], _ =>
                {
                    addRequests++;
                    return null;
                }, _ => null);

                run.MarkRemoteWorkerDisconnected(endpoint.Id);

                Assert.IsFalse(run.RemoteWorkers.Single().IsActive);
                run.ReconnectRemoteWorker(endpoint.Id);

                Assert.AreEqual(1, addRequests);
                Assert.IsTrue(run.RemoteWorkers.Single().IsBusy);
                run.ApplyRemoteWorkerAcknowledgement(new SharedEstimationWorkerControlAcknowledgement(
                    run.RunId, endpoint.Id, Add: true, Succeeded: true, Error: null, ActiveWorkerCount: 1));
                Assert.IsTrue(run.RemoteWorkers.Single().IsActive);
                Assert.IsFalse(run.RemoteWorkers.Single().IsBusy);
            });
    }

    [TestMethod]
    public void ConnectionLostRun_IsInterruptedAndRestoredWhenRecoverySnapshotIsRunning()
    {
        var remoteSnapshot = new RemoteRunSnapshot("run-reconnect-state", "forecast", RunMode.Normal,
            "/remote/runs/forecast", "Start", "model-hash", RemoteRunState.Running,
            "Run is executing.", 0, double.NaN, Array.Empty<RemoteRunParameterValue>(), null,
            null, null, false, DateTimeOffset.UtcNow);
        var remoteRun = new RunViewModel(remoteSnapshot, "Remote Server");

        remoteRun.MarkConnectionLost();

        Assert.AreEqual(RunStatus.Interrupted, remoteRun.Status);
        remoteRun.ApplyRecoveredSnapshot(remoteSnapshot);
        Assert.AreEqual(RunStatus.Running, remoteRun.Status);

        var sharedSnapshot = new SharedEstimationJobSnapshot("shared-reconnect-state",
            SharedEstimationJobState.Running, null, null, ["worker-1"], "estimation",
            "/remote/runs/estimation", "model-hash");
        var sharedRun = new RunViewModel(sharedSnapshot, "Remote Coordinator", () => { });

        sharedRun.MarkConnectionLost();

        Assert.AreEqual(RunStatus.Interrupted, sharedRun.Status);
        sharedRun.ApplyRecoveredSharedEstimationSnapshot(sharedSnapshot);
        Assert.AreEqual(RunStatus.Running, sharedRun.Status);
    }
}
