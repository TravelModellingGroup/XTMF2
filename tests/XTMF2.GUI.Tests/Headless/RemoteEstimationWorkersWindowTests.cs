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
                new System.Collections.Generic.Dictionary<string, int>(),
                Array.Empty<double>(), ["server-2", "server-2#run-2"]));

            Assert.AreEqual(2, worker.ActiveWorkerCount);
            Assert.AreEqual("2 / 3 workers", worker.ActiveWorkerCountDisplay);
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
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