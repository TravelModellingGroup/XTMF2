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
}
