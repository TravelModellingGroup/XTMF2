using System;
using System.IO;
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
            var snapshot = new RemoteRunSnapshot("run-restart", "forecast", RunMode.Normal,
                "/runs/forecast", "Start", "model-hash", RemoteRunState.Running,
                "Run submitted.", 0, double.NaN, Array.Empty<RemoteRunParameterValue>(), null,
                null, null, false, DateTimeOffset.UtcNow);
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
            Assert.IsTrue(double.IsNaN(recovered[0].Fitness));
            Assert.IsEmpty(registry.GetActiveActivities());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}