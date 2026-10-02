using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Bus;

namespace XTMF2.UnitTests.Bus;

[TestClass]
public class TestRemoteRunRegistry
{
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