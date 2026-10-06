using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Bus;
using XTMF2.Bus.Optimization;

namespace XTMF2.UnitTests.Bus;

[TestClass]
public class TestRemoteRunRegistry
{
    [TestMethod]
    public async Task SharedWorkerReservation_BlocksOrdinaryRunsFromIndependentObservers()
    {
        var directory = Path.Combine(Path.GetTempPath(), "XTMF2-RemoteRunRegistry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var registry = new RemoteRunRegistry(directory);
            using var firstObserver = CreateObserver();
            using var secondObserver = CreateObserver();
            Assert.IsTrue(RunContext.CreateRunContext(null!, "worker-run", [], Path.GetTempPath(),
                "Start", RunMode.Normal, out var workerContext));
            Assert.IsTrue(RunContext.CreateRunContext(null!, "first-ordinary-run", [], Path.GetTempPath(),
                "Start", RunMode.Normal, out var firstRun));
            Assert.IsTrue(RunContext.CreateRunContext(null!, "second-ordinary-run", [], Path.GetTempPath(),
                "Start", RunMode.Normal, out var secondRun));

            using var workerLease = registry.ReserveWorkerSlot(workerContext, 1);
            await workerLease.Started.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.IsTrue(registry.Submit(firstObserver, firstRun, "first", RunMode.Normal,
                firstRun.WorkingDirectory, firstRun.StartToExecute, []));
            Assert.IsTrue(registry.Submit(secondObserver, secondRun, "second", RunMode.Normal,
                secondRun.WorkingDirectory, secondRun.StartToExecute, []));

            var activities = registry.GetActiveActivities();
            Assert.HasCount(3, activities);
            Assert.AreEqual("worker-run", activities[0].RunId);
            Assert.AreEqual(RunServerActivityState.Running, activities[0].State);
            CollectionAssert.AreEqual(new[] { "first-ordinary-run", "second-ordinary-run" },
                activities.Skip(1).Select(activity => activity.RunId).ToArray());
            Assert.IsTrue(activities.Skip(1).All(activity => activity.State == RunServerActivityState.Queued));

            registry.Dispose();
            workerLease.Dispose();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task SeparateRegistries_HaveIndependentExecutionSlots()
    {
        var firstDirectory = Path.Combine(Path.GetTempPath(), "XTMF2-RemoteRunRegistry-" + Guid.NewGuid().ToString("N"));
        var secondDirectory = Path.Combine(Path.GetTempPath(), "XTMF2-RemoteRunRegistry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(firstDirectory);
        Directory.CreateDirectory(secondDirectory);
        try
        {
            using var firstRegistry = new RemoteRunRegistry(firstDirectory);
            using var secondRegistry = new RemoteRunRegistry(secondDirectory);
            Assert.IsTrue(RunContext.CreateRunContext(null!, "first-process-run", [], Path.GetTempPath(),
                "Start", RunMode.Normal, out var firstContext));
            Assert.IsTrue(RunContext.CreateRunContext(null!, "second-process-run", [], Path.GetTempPath(),
                "Start", RunMode.Normal, out var secondContext));

            using var firstLease = firstRegistry.ReserveWorkerSlot(firstContext, 1);
            using var secondLease = secondRegistry.ReserveWorkerSlot(secondContext, 1);
            await Task.WhenAll(firstLease.Started, secondLease.Started).WaitAsync(TimeSpan.FromSeconds(3));

            Assert.AreEqual(RunServerActivityState.Running, firstRegistry.GetActiveActivities().Single().State);
            Assert.AreEqual(RunServerActivityState.Running, secondRegistry.GetActiveActivities().Single().State);
        }
        finally
        {
            Directory.Delete(firstDirectory, recursive: true);
            Directory.Delete(secondDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void RegistryRestart_MarksPersistedActiveRunInterrupted()
    {
        var directory = Path.Combine(Path.GetTempPath(), "XTMF2-RemoteRunRegistry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var ownerUserId = Guid.NewGuid();
            var snapshot = new RemoteRunSnapshot("run-restart", "forecast", RunMode.Normal,
                "/runs/forecast", "Start", "model-hash", RemoteRunState.Running,
                "Run submitted.", 0, double.NaN, Array.Empty<RemoteRunParameterValue>(), null,
                null, null, false, DateTimeOffset.UtcNow, OwnerUserId: ownerUserId);
            var options = new JsonSerializerOptions
            {
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };
            File.WriteAllBytes(Path.Combine(directory, "active.json"),
                JsonSerializer.SerializeToUtf8Bytes(snapshot, options));

            using var registry = new RemoteRunRegistry(directory);

            var recovered = registry.GetSnapshots();
            Assert.HasCount(1, recovered);
            Assert.AreEqual(RemoteRunState.Interrupted, recovered[0].State);
            Assert.AreEqual("run-restart", recovered[0].RunId);
            Assert.AreEqual(ownerUserId, recovered[0].OwnerUserId);
            Assert.IsTrue(double.IsNaN(recovered[0].Fitness));
            Assert.IsEmpty(registry.GetActiveActivities());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static RunServerBus CreateObserver()
        => new(new MemoryStream(), true, runtime: null!, remoteRunRegistry: null,
            allowDeployment: false);

    [TestMethod]
    public void DeleteRun_RemovesWorkspaceArchiveAndManifest()
    {
        var storageDirectory = Path.Combine(Path.GetTempPath(), "XTMF2-RemoteRunRegistry-" + Guid.NewGuid().ToString("N"));
        var runId = Guid.NewGuid().ToString("N");
        var workingDirectory = Path.Combine(Path.GetTempPath(), "XTMF2", "Runs", runId);
        Directory.CreateDirectory(workingDirectory);
        File.WriteAllText(Path.Combine(workingDirectory, "output.txt"), "run output");
        Directory.CreateDirectory(storageDirectory);
        try
        {
            var manifestPath = GetManifestPath(storageDirectory, runId);
            var archivePath = GetArtifactPath(storageDirectory, runId);
            File.WriteAllBytes(manifestPath, CreateSnapshot(runId, workingDirectory));
            File.WriteAllBytes(archivePath, [1, 2, 3]);

            using var registry = new RemoteRunRegistry(storageDirectory);

            Assert.IsTrue(registry.DeleteRun(runId, out var error), error);
            Assert.IsFalse(Directory.Exists(workingDirectory));
            Assert.IsFalse(File.Exists(manifestPath));
            Assert.IsFalse(File.Exists(archivePath));
            Assert.IsEmpty(registry.GetSnapshots());
        }
        finally
        {
            if (Directory.Exists(workingDirectory))
                Directory.Delete(workingDirectory, recursive: true);
            if (Directory.Exists(storageDirectory))
                Directory.Delete(storageDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void DeleteRun_RefusesDirectoryOutsidePrivateWorkspace()
    {
        var storageDirectory = Path.Combine(Path.GetTempPath(), "XTMF2-RemoteRunRegistry-" + Guid.NewGuid().ToString("N"));
        var runId = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(storageDirectory);
        try
        {
            var manifestPath = GetManifestPath(storageDirectory, runId);
            File.WriteAllBytes(manifestPath, CreateSnapshot(runId, Path.GetTempPath()));
            using var registry = new RemoteRunRegistry(storageDirectory);

            Assert.IsFalse(registry.DeleteRun(runId, out var error));
            Assert.Contains("outside the RunServer's private run workspace", error);
            Assert.IsTrue(File.Exists(manifestPath));
            Assert.HasCount(1, registry.GetSnapshots());
        }
        finally
        {
            if (Directory.Exists(storageDirectory))
                Directory.Delete(storageDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void TryReadArtifacts_RebuildsArchiveAfterReceiptAcknowledgement()
    {
        var storageDirectory = Path.Combine(Path.GetTempPath(), "XTMF2-RemoteRunRegistry-" + Guid.NewGuid().ToString("N"));
        var runId = Guid.NewGuid().ToString("N");
        var workingDirectory = Path.Combine(Path.GetTempPath(), "XTMF2", "Runs", runId);
        Directory.CreateDirectory(workingDirectory);
        Directory.CreateDirectory(storageDirectory);
        File.WriteAllText(Path.Combine(workingDirectory, "output.txt"), "retained run output");
        try
        {
            var manifestPath = GetManifestPath(storageDirectory, runId);
            var archivePath = GetArtifactPath(storageDirectory, runId);
            File.WriteAllBytes(manifestPath, CreateSnapshot(runId, workingDirectory));
            File.WriteAllBytes(archivePath, [1, 2, 3]);
            using var registry = new RemoteRunRegistry(storageDirectory);

            Assert.IsTrue(registry.AcknowledgeReceived(runId));
            Assert.IsFalse(File.Exists(archivePath));
            Assert.IsTrue(registry.GetSnapshots()[0].ArtifactsAvailable);
            Assert.IsTrue(registry.TryReadArtifacts(runId, out var archive));
            Assert.IsNotNull(archive);
            Assert.IsTrue(File.Exists(archivePath));
        }
        finally
        {
            if (Directory.Exists(workingDirectory))
                Directory.Delete(workingDirectory, recursive: true);
            if (Directory.Exists(storageDirectory))
                Directory.Delete(storageDirectory, recursive: true);
        }
    }

    private static byte[] CreateSnapshot(string runId, string workingDirectory)
    {
        var snapshot = new RemoteRunSnapshot(runId, "forecast", RunMode.Normal,
            workingDirectory, "Start", "model-hash", RemoteRunState.Completed,
            "Run completed.", 0, double.NaN, Array.Empty<RemoteRunParameterValue>(), null,
            null, null, true, DateTimeOffset.UtcNow);
        var options = new JsonSerializerOptions
        {
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };
        return JsonSerializer.SerializeToUtf8Bytes(snapshot, options);
    }

    private static string GetManifestPath(string storageDirectory, string runId)
        => Path.Combine(storageDirectory,
            $"{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(runId)))}.json");

    private static string GetArtifactPath(string storageDirectory, string runId)
        => Path.Combine(storageDirectory,
            $"{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(runId)))}.zip");
}