using System;
using System.Collections.Generic;
using System.Linq;
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
            dialog.RunServerChoices.Single(choice => choice.Endpoint.Id == local.Id).IsSelected = true;

            Assert.AreSame(remote, dialog.SelectedCoordinatorRunServer);
            Assert.Contains(remote, dialog.RunServers);
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    }

    [TestMethod]
    public void ServerRows_UseCheckboxSelectionAndIndependentRunCounts()
    {
        Session.Dispatch(() =>
        {
            var coordinator = new RunServerEndpoint { Id = "coordinator", Name = "Coordinator" };
            var local = new RunServerEndpoint { Id = "local", Name = "Local" };
            var remote = new RunServerEndpoint { Id = "remote-1", Name = "Remote 1" };
            var dialog = new RunConfigurationDialog("Run Estimation", "estimation",
                [coordinator, local, remote], ["Start"], allowMultipleRunServers: true);
            dialog.UseMultipleRunServers = true;
            var remoteRow = dialog.RunServerChoices.Single(choice => choice.Endpoint.Id == remote.Id);
            var localRow = dialog.RunServerChoices.Single(choice => choice.Endpoint.Id == local.Id);
            var coordinatorRow = dialog.RunServerChoices.Single(choice => choice.Endpoint.Id == coordinator.Id);
            remoteRow.IsSelected = true;
            remoteRow.ConcurrentRuns = 3;
            localRow.IsSelected = true;
            localRow.ConcurrentRuns = 2;
            coordinatorRow.IsSelected = false;

            Assert.IsTrue(remoteRow.IsSelected);
            Assert.IsTrue(localRow.IsSelected);
            Assert.AreEqual(3, remoteRow.ConcurrentRuns);
            Assert.AreEqual(2, localRow.ConcurrentRuns);
            Assert.AreEqual(3, dialog.ConcurrentRunsByEndpoint[remote.Id]);
            Assert.AreEqual(2, dialog.ConcurrentRunsByEndpoint[local.Id]);
            Assert.AreEqual(1, dialog.ConcurrentRunsByEndpoint[coordinator.Id]);
            Assert.Contains(coordinator, dialog.RunServers);
            Assert.IsFalse(dialog.SelectedRunServers.Any(endpoint => endpoint.Id == coordinator.Id));
            Assert.IsTrue(coordinatorRow.CanConfigureRuns);
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    }
}