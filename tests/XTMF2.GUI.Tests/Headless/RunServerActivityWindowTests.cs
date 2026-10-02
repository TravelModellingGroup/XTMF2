using Avalonia.Controls;
using Avalonia.Headless;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using XTMF2.Bus;
using XTMF2.GUI;
using XTMF2.GUI.Properties;
using XTMF2.GUI.ViewModels;
using XTMF2.GUI.Views;

namespace XTMF2.GUI.Tests.Headless;

[TestClass]
public class RunServerActivityWindowTests
{
    private HeadlessUnitTestSession Session => HeadlessAppLifetime.HeadlessSession!;

    [TestMethod]
    public void ActivityWindow_LoadsFromRuntimeXaml()
    {
        Session.Dispatch(() =>
        {
            var window = new RunServerActivityWindow();
            Assert.IsNotNull(window.Content);
            Assert.IsNotNull(window.FindControl<StackPanel>("ServerList"));
            Assert.IsNotNull(window.FindControl<Button>("RefreshButton"));
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    }

    [TestMethod]
    public void ActiveActivityRow_ExposesKillButton()
    {
        Session.Dispatch(() =>
        {
            var window = new RunServerActivityWindow();
            var endpoint = new RunServerEndpoint
            {
                Id = "server-1",
                Name = "Server 1",
                Address = "127.0.0.1",
                Port = 5000
            };
            window.RenderServers(new[]
            {
                new RunServerActivityServerSnapshot(endpoint, RunServerConnectionState.Available, null,
                    new[]
                    {
                        new RunServerActivity("run-1", "Forecast", "Normal",
                            RunServerActivityState.Running, "Running")
                    })
            });

            var serverList = window.FindControl<StackPanel>("ServerList")!;
            var serverContent = (StackPanel)((Border)serverList.Children[0]).Child!;
            var activityRow = (Grid)serverContent.Children[2];
            Assert.IsTrue(activityRow.Classes.Contains("activity-row"));
            Assert.IsNotNull(activityRow.Children.OfType<Button>().SingleOrDefault(button =>
                Equals(button.Content, "✕")));
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    }

    [TestMethod]
    public void RecoveredSnapshot_UpdatesExistingRunServerLabel()
    {
        Session.Dispatch(() =>
        {
            var runs = new RunsViewModel();
            var snapshot = new RemoteRunSnapshot("run-recovered-label", "Forecast", RunMode.Normal,
                "/remote/runs/forecast", "Start", "model-hash", RemoteRunState.Completed,
                "Run completed.", 0, double.NaN, Array.Empty<RemoteRunParameterValue>(), null,
                null, null, false, DateTimeOffset.UtcNow);

            var run = runs.RestoreRemoteRun(snapshot, "Local RunServer", () => { });
            runs.RestoreRemoteRun(snapshot, "Remote Production", () => { });

            Assert.AreEqual("Remote Production", run.RunServer);
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    }
}