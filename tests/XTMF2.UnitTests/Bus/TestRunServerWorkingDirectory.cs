using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
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

    [TestMethod]
    public void RunContextClearsExistingFilesOnlyWhenPreparedForExecution()
    {
        var root = Path.Combine(Path.GetTempPath(), "XTMF2-RunClear-" + Guid.NewGuid().ToString("N"));
        var runDirectory = Path.Combine(root, "runs", "sample");
        var existingFile = Path.Combine(runDirectory, "previous-output.txt");
        Directory.CreateDirectory(runDirectory);
        File.WriteAllText(existingFile, "previous output");

        try
        {
            Assert.IsTrue(RunContext.CreateRunContext(CreateRuntime(), "run-clear", [], runDirectory,
                "Start", RunMode.Normal, out var context, clearWorkingDirectoryBeforeStart: true));

            Assert.IsTrue(File.Exists(existingFile), "Creating a run context must not clear existing files.");
            context.PrepareWorkingDirectory();

            Assert.IsTrue(Directory.Exists(runDirectory));
            Assert.IsEmpty(Directory.GetFileSystemEntries(runDirectory));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void RunContextStartsLocalReaderWithAcceptedHostStream()
    {
        var root = Path.Combine(Path.GetTempPath(), "XTMF2-LocalRunBus-" + Guid.NewGuid().ToString("N"));
        var sink = new RecordingRunOutputSink();

        try
        {
            Assert.IsTrue(RunContext.CreateRunContext(CreateRuntime(), "local-run", [], root,
                "Start", RunMode.Normal, out var context));

            context.RunInCurrentProcess(sink);

            Assert.IsNotNull(sink.ReceivedStream);
        }
        finally
        {
            sink.ReceivedStream?.Dispose();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private sealed class RecordingRunOutputSink : IRunOutputSink
    {
        public IReadOnlyList<string> ExtraDlls => Array.Empty<string>();
        public Stream ReceivedStream { get; private set; }

        public Task StartProcessingRequestFromRun(string id, Stream clientToRunStream)
        {
            ReceivedStream = clientToRunStream;
            return Task.CompletedTask;
        }

        public void ModelRunFailedValidation(string runId, string error, string moduleName = null, string elementId = null) { }
        public void ModelRunFailed(string runId, string message, string stackTrace, string moduleName = null, string elementId = null) { }
        public void SendStatusMessage(string runId, string message) { }
        public void SendOptimizationResults(string runId, IReadOnlyList<(int nodeIndex, double value)> results) { }
        public void SendIterationProgress(string runId, int iteration, double fitness,
            int fitnessTestsThisIteration, IReadOnlyList<(int nodeIndex, double value)> values) { }
        public void ModelRunComplete(string runId) { }
        public void ModelRunCancelled(string runId) { }
        public void SendRunArtifacts(string runId, string runDirectory) { }
    }
}
