using System;
using System.Collections.Generic;
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

            Assert.IsTrue(dialog.IsPathOverridesVisible);
            Assert.AreSame(endpoint, dialog.SelectedCoordinatorRunServer);
            StringAssert.Contains(dialog.SelectedRunServerSummary, "Worker 1");
            Assert.AreEqual("/worker/data", dialog.PathOverrides[endpoint.Id][7]);
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    }
}