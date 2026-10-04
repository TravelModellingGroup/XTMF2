using System;
using Avalonia.Controls;
using Avalonia.Headless;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Bus;
using XTMF2.Bus.Optimization;
using XTMF2.GUI.Properties;
using XTMF2.GUI.ViewModels;
using XTMF2.GUI.Views;

namespace XTMF2.GUI.Tests.Headless;

[TestClass]
public class RemoteEstimationWorkersWindowTests
{
    private HeadlessUnitTestSession Session => HeadlessAppLifetime.HeadlessSession!;

    [TestMethod]
    public void WorkerWindow_BindsLiveRunWorkerCollection()
    {
        Session.Dispatch(() =>
        {
            var run = CreateRun();
            var window = new RemoteEstimationWorkersWindow(run);

            Assert.AreSame(run, window.DataContext);
            Assert.AreSame(run.RemoteWorkers, window.FindControl<ItemsControl>("RemoteWorkersList")!.ItemsSource);
            Assert.AreEqual("Estimation Workers - Shared forecast", window.Title);
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    }

    [TestMethod]
    public void WorkerWindowButton_IsBesideParameterProgressButton()
    {
        Session.Dispatch(() =>
        {
            var view = new RunsView();
            var progressButton = view.FindControl<Button>("ViewProgressButton");
            var workersButton = view.FindControl<Button>("EstimationWorkersButton");

            Assert.IsNotNull(progressButton);
            Assert.IsNotNull(workersButton);
            Assert.AreSame(progressButton.Parent, workersButton.Parent);
            Assert.AreEqual("Estimation Workers", workersButton.Content);
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    }

    [TestMethod]
    public void WorkerWindow_ShowsReadyCountAgainstConfiguredCapacity()
    {
        Session.Dispatch(() =>
        {
            var run = CreateRun();
            var worker = run.RemoteWorkers[0];

            Assert.AreEqual("0 / 3 workers", worker.ActiveWorkerCountDisplay);

            run.UpdateSharedEstimationProgress(new SharedEstimationProgress(
                "shared-run", 1, 0.5, 2, 0, 2, 2,
                new System.Collections.Generic.Dictionary<string, int>
                {
                    ["server-2"] = 2,
                    ["server-2#run-2"] = 3,
                    ["server-3"] = 7,
                    ["coordinator"] = 11
                },
                Array.Empty<double>(), ["server-2", "server-2#run-2"]));

            Assert.AreEqual(2, worker.ActiveWorkerCount);
            Assert.AreEqual("2 / 3 workers", worker.ActiveWorkerCountDisplay);
            Assert.AreEqual(5, worker.CompletedEvaluations);
            Assert.AreEqual("Tests: 5", worker.EvaluationCountDisplay);
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    }

    [TestMethod]
    public void CoordinatorWorker_IsShownButCannotBeRemoved()
    {
        Session.Dispatch(() =>
        {
            var removalRequested = false;
            var run = CreateRun();
            run.SetRemoteEstimationWorkers(
                [new RunServerEndpoint
                {
                    Id = RemoteEstimationWorkerViewModel.CoordinatorWorkerId,
                    Name = "Coordinator",
                    Address = "127.0.0.1"
                }],
                [RemoteEstimationWorkerViewModel.CoordinatorWorkerId],
                _ => null,
                _ =>
                {
                    removalRequested = true;
                    return null;
                },
                new System.Collections.Generic.Dictionary<string, int>
                {
                    [RemoteEstimationWorkerViewModel.CoordinatorWorkerId] = 2
                });
            var coordinator = run.RemoteWorkers[0];

            run.UpdateSharedEstimationProgress(new SharedEstimationProgress(
                "shared-run", 1, 0.5, 2, 0, 2, 2,
                new System.Collections.Generic.Dictionary<string, int>
                {
                    ["coordinator"] = 4,
                    ["coordinator#run-2"] = 6
                },
                Array.Empty<double>(), ["coordinator", "coordinator#run-2"]));
            run.RemoveRemoteWorkerCommand.Execute(coordinator);

            Assert.IsTrue(coordinator.IsCoordinator);
            Assert.IsTrue(coordinator.IsActive);
            Assert.IsFalse(coordinator.CanRemove);
            Assert.AreEqual(2, coordinator.ActiveWorkerCount);
            Assert.AreEqual("Tests: 10", coordinator.EvaluationCountDisplay);
            Assert.IsFalse(removalRequested);
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    }

    [TestMethod]
    public void LocalCoordinatorRun_DoesNotOfferRemoteOutputTransfer()
    {
        var run = CreateRun();
        run.SetRemoteRunTracking(false);
        run.MarkFinished("Cancelled");

        Assert.IsFalse(run.CanTransferRemoteOutput);
    }

    private static RunViewModel CreateRun()
    {
        var run = new RunViewModel("shared-run", "Shared forecast", "/runs/shared", "server-1",
            null!, null!);
        run.SetRunMode(RunMode.Estimation, Array.Empty<(int nodeIndex, string name, double min, double max)>(), () => { });
        run.SetRemoteEstimationWorkers(
            [new RunServerEndpoint { Id = "server-2", Name = "Remote Two", Address = "10.0.0.2", Port = 5000 }],
            ["server-2"],
            _ => null,
            _ => null,
            new System.Collections.Generic.Dictionary<string, int> { ["server-2"] = 3 });
        return run;
    }
}