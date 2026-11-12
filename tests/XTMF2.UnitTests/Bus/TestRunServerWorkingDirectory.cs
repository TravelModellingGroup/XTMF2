using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Bus;
using XTMF2.Configuration;
using static XTMF2.UnitTests.TestHelper;

namespace XTMF2.UnitTests.Bus;

[TestClass]
public class TestRunServerWorkingDirectory
{
    [TestMethod]
    public void LocalRunUsesRequestedDirectoryWhilePrivateWorkspaceRemainsAvailable()
    {
        var root = Path.Combine(Path.GetTempPath(), "XTMF2-RunWorkspace-" + Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(root, "private-runs");
        var requestedDirectory = Path.Combine(root, "project", "runs", "sample");
        var runId = Guid.NewGuid().ToString("N");
        var storagePaths = new XTMFStoragePaths(root, workspace);

        try
        {
            using var localServer = new RunServerBus(Stream.Null, false, CreateRuntime(),
                runLocal: true, usePrivateWorkspace: false, storagePaths: storagePaths);
            Assert.AreEqual(requestedDirectory,
                localServer.ResolveRunWorkingDirectory(runId, requestedDirectory));
            Assert.IsFalse(Directory.Exists(Path.Combine(workspace, runId)));

            using var remoteServer = new RunServerBus(Stream.Null, false, CreateRuntime(),
                runLocal: true, usePrivateWorkspace: true, storagePaths: storagePaths);
            Assert.AreEqual(Path.Combine(workspace, runId),
                remoteServer.ResolveRunWorkingDirectory(runId, requestedDirectory));
            Assert.IsTrue(Directory.Exists(Path.Combine(workspace, runId)));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void LocalRunDoesNotTransferOrRemoveSharedOutputArtifacts()
    {
        var root = Path.Combine(Path.GetTempPath(), "XTMF2-RunWorkspace-" + Guid.NewGuid().ToString("N"));
        var runDirectory = Path.Combine(root, "project", "runs", "sample");
        var outputPath = Path.Combine(runDirectory, "output.txt");
        Directory.CreateDirectory(runDirectory);
        File.WriteAllText(outputPath, "local output");

        try
        {
            using var stream = new MemoryStream();
            using var localServer = new RunServerBus(stream, false, CreateRuntime(),
                runLocal: true, usePrivateWorkspace: false,
                storagePaths: new XTMFStoragePaths(root));

            localServer.SendRunArtifacts("local-run", runDirectory);

            Assert.AreEqual(0L, stream.Length);
            Assert.AreEqual("local output", File.ReadAllText(outputPath));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
