using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Bus;
using XTMF2.GUI;
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
}
