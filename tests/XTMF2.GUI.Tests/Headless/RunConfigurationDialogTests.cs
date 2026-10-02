using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Headless;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.GUI.Properties;
using XTMF2.GUI.Views;

namespace XTMF2.GUI.Tests.Headless;

[TestClass]
public class RunConfigurationDialogTests
{
    private HeadlessUnitTestSession Session => HeadlessAppLifetime.HeadlessSession!;

    [TestMethod]
    public void SavedEndpointOverride_IsLoadedByStableNodeId()
    {
        Session.Dispatch(() =>
        {
            var nodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var endpoint = new RunServerEndpoint
            {
                Id = "worker-1",
                Name = "Worker 1",
                BasicParameterOverrides = new Dictionary<string, string>
                {
                    [nodeId.ToString("D")] = "/worker/data"
                }
            };
            var dialog = new RunConfigurationDialog(
                "Run Estimation",
                "estimation",
                [endpoint],
                ["Start"],
                allowMultipleRunServers: true,
                [new RunConfigurationDialog.PathParameter(7, nodeId, "Input path", "/shared/data")]);
            dialog.UseMultipleRunServers = true;

            Assert.IsTrue(dialog.IsPathOverridesVisible);
            Assert.AreSame(endpoint, dialog.SelectedCoordinatorRunServer);
            StringAssert.Contains(dialog.SelectedRunServerSummary, "Worker 1");
            Assert.AreEqual("/worker/data", dialog.PathOverrides[endpoint.Id][7]);
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    }

    [TestMethod]
    public void EstimationCanSelectSingleRemoteRunServerByDefault()
    {
        Session.Dispatch(() =>
        {
            var local = new RunServerEndpoint { Id = "local", Name = "Local" };
            var remote = new RunServerEndpoint { Id = "remote-1", Name = "Remote 1" };
            var dialog = new RunConfigurationDialog("Run Estimation", "estimation",
                [local, remote], ["Start"], allowMultipleRunServers: true);

            Assert.IsFalse(dialog.UseMultipleRunServers);
            Assert.IsTrue(dialog.IsSingleRunServerSelectionVisible);
            dialog.SelectedRunServer = remote;
            Assert.AreSame(remote, dialog.SelectedRunServer);

            dialog.UseMultipleRunServers = true;
            Assert.IsTrue(dialog.IsMultipleRunServerSelectionVisible);
            Assert.IsFalse(dialog.IsSingleRunServerSelectionVisible);
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    }

    [TestMethod]
    public void DistributedEstimationCanChooseRemoteCoordinatorIndependentlyOfWorkers()
    {
        Session.Dispatch(() =>
        {
            var local = new RunServerEndpoint { Id = "local", Name = "Local" };
            var remote = new RunServerEndpoint { Id = "remote-1", Name = "Remote 1" };
            var dialog = new RunConfigurationDialog("Run Estimation", "estimation",
                [local, remote], ["Start"], allowMultipleRunServers: true);
            dialog.UseMultipleRunServers = true;
            dialog.SelectedCoordinatorRunServer = remote;
            var workerList = dialog.FindControl<ListBox>("RunServerListBox");
            Assert.IsNotNull(workerList);
            workerList.SelectedItems!.Add(local);

            Assert.AreSame(remote, dialog.SelectedCoordinatorRunServer);
            Assert.Contains(remote, dialog.RunServers);
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    }
}