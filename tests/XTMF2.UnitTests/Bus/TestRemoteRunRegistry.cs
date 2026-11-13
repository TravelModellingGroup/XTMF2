using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Bus;
using XTMF2.Bus.Optimization;

namespace XTMF2.UnitTests.Bus;

[TestClass]
public class TestRemoteRunRegistry
{
    [TestMethod]
    public void ArtifactMirror_RoundTripsFilesAndEmptyDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "XTMF2-ArtifactMirror-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        var mirror = Path.Combine(root, "mirror");
        var receivedMirror = Path.Combine(root, "received-mirror");
        var destination = Path.Combine(root, "destination");
        Directory.CreateDirectory(Path.Combine(source, "nested", "empty"));
        var expectedBytes = Enumerable.Range(0, 100_000).Select(value => (byte)(value % 251)).ToArray();
        File.WriteAllBytes(Path.Combine(source, "nested", "payload.bin"), expectedBytes);
        File.WriteAllText(Path.Combine(source, "empty.txt"), string.Empty);
        try
        {
            var progress = new List<(long processed, long total)>();
            RunArtifactMirror.CreateFromDirectory(source, mirror,
                (processed, total) => progress.Add((processed, total)), maxDegreeOfParallelism: 2);
            using var transfer = new MemoryStream();
            using (var writer = new BinaryWriter(transfer, Encoding.UTF8, leaveOpen: true))
                RunArtifactMirror.WriteTransfer(writer, mirror);
            transfer.Position = 0;
            using (var reader = new BinaryReader(transfer, Encoding.UTF8, leaveOpen: true))
                RunArtifactMirror.ReadTransfer(reader, receivedMirror);
            RunArtifactMirror.ExtractToDirectory(receivedMirror, destination);

            CollectionAssert.AreEqual(expectedBytes,
                File.ReadAllBytes(Path.Combine(destination, "nested", "payload.bin")));
            Assert.AreEqual(0L, new FileInfo(Path.Combine(destination, "empty.txt")).Length);
            Assert.IsTrue(Directory.Exists(Path.Combine(destination, "nested", "empty")));
            Assert.IsGreaterThanOrEqualTo(2, progress.Count);
            Assert.AreEqual(progress[^1].total, progress[^1].processed);
            Assert.IsTrue(Directory.Exists(Path.Combine(mirror, "files")));
            Assert.IsTrue(File.Exists(Path.Combine(mirror, "manifest.json")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void ArtifactMirror_ReadTransferExpandsEachPayloadBeforeTheNextOneArrives()
    {
        var root = Path.Combine(Path.GetTempPath(), "XTMF2-ArtifactMirror-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        var mirror = Path.Combine(root, "mirror");
        var receivedMirror = Path.Combine(root, "received");
        Directory.CreateDirectory(source);
        var firstBytes = new byte[1024 * 1024];
        new Random(17).NextBytes(firstBytes);
        File.WriteAllBytes(Path.Combine(source, "first.bin"), firstBytes);
        File.WriteAllBytes(Path.Combine(source, "second.bin"), [1, 2, 3]);
        try
        {
            RunArtifactMirror.CreateFromDirectory(source, mirror);
            byte[] transferBytes;
            using (var transfer = new MemoryStream())
            {
                using (var writer = new BinaryWriter(transfer, Encoding.UTF8, leaveOpen: true))
                    RunArtifactMirror.WriteTransfer(writer, mirror);
                transferBytes = transfer.ToArray();
            }

            var firstPayloadEnd = GetFirstPayloadEnd(transferBytes);
            using var blockingStream = new BlockingReadStream(transferBytes, firstPayloadEnd);
            using var reader = new BinaryReader(blockingStream, Encoding.UTF8, leaveOpen: true);
            var readTask = Task.Run(() => RunArtifactMirror.ReadTransfer(reader, receivedMirror));
            try
            {
                Assert.IsTrue(blockingStream.WaitForBlock(TimeSpan.FromSeconds(5)),
                    "The transfer did not reach the second payload.");
                var stagingDirectory = Directory.EnumerateDirectories(root, "received.tmp-*").Single();
                var expandedFirstPayload = Path.Combine(stagingDirectory, "expanded", "00000000.gz.expanded");
                Assert.IsTrue(SpinWait.SpinUntil(() => File.Exists(expandedFirstPayload),
                    TimeSpan.FromSeconds(5)),
                    "The first payload was not decompressed while the second payload was still pending.");
            }
            finally
            {
                blockingStream.Release();
                readTask.GetAwaiter().GetResult();
            }

            var destination = Path.Combine(root, "destination");
            RunArtifactMirror.ExtractToDirectory(receivedMirror, destination);
            CollectionAssert.AreEqual(firstBytes, File.ReadAllBytes(Path.Combine(destination, "first.bin")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void ArtifactMirror_ReadTransferRejectsPathTraversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "XTMF2-ArtifactMirror-" + Guid.NewGuid().ToString("N"));
        var destination = Path.Combine(root, "received");
        Directory.CreateDirectory(root);
        try
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(1);
                writer.Write(1);
                writer.Write(false);
                writer.Write("../outside.txt");
                writer.Write(0L);
                writer.Write(0L);
                writer.Write(new byte[32]);
            }
            stream.Position = 0;
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            Assert.ThrowsExactly<InvalidDataException>(() => RunArtifactMirror.ReadTransfer(reader, destination));
            Assert.IsFalse(Directory.Exists(destination));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void ArtifactMirror_ReadTransferRemovesStagingAfterTruncatedPayload()
    {
        var root = Path.Combine(Path.GetTempPath(), "XTMF2-ArtifactMirror-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        var mirror = Path.Combine(root, "mirror");
        var destination = Path.Combine(root, "received");
        Directory.CreateDirectory(source);
        File.WriteAllBytes(Path.Combine(source, "payload.bin"), Enumerable.Range(0, 1024)
            .Select(value => (byte)value).ToArray());
        try
        {
            RunArtifactMirror.CreateFromDirectory(source, mirror);
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
                RunArtifactMirror.WriteTransfer(writer, mirror);
            stream.SetLength(stream.Length - 1);
            stream.Position = 0;
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

            Assert.ThrowsExactly<EndOfStreamException>(() => RunArtifactMirror.ReadTransfer(reader, destination));
            Assert.IsFalse(Directory.Exists(destination));
            Assert.IsEmpty(Directory.EnumerateDirectories(root, "received.tmp-*"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

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
            await workerLease.Started.WaitAsync(TimeSpan.FromMilliseconds(TestHelper.TestTimeoutMilliseconds));
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
            await Task.WhenAll(firstLease.Started, secondLease.Started).WaitAsync(TimeSpan.FromMilliseconds(TestHelper.TestTimeoutMilliseconds));

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
    public void DeleteRun_RemovesWorkspaceMirrorAndManifest()
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
            var mirrorPath = GetArtifactMirrorPath(storageDirectory, runId);
            File.WriteAllBytes(manifestPath, CreateSnapshot(runId, workingDirectory));
            Directory.CreateDirectory(mirrorPath);
            File.WriteAllText(Path.Combine(mirrorPath, "manifest.json"), "{}");

            using var registry = new RemoteRunRegistry(storageDirectory);

            Assert.IsTrue(registry.DeleteRun(runId, out var error), error);
            Assert.IsFalse(Directory.Exists(workingDirectory));
            Assert.IsFalse(File.Exists(manifestPath));
            Assert.IsFalse(Directory.Exists(mirrorPath));
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
    public void DeleteRun_RemovesManifestButPreservesDirectoryOutsidePrivateWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "XTMF2-RemoteRunRegistry-" + Guid.NewGuid().ToString("N"));
        var storageDirectory = Path.Combine(root, "state");
        var workspaceRoot = Path.Combine(root, "workspaces");
        var outsideDirectory = Path.Combine(root, "outside");
        var runId = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(storageDirectory);
        Directory.CreateDirectory(outsideDirectory);
        var outsideFile = Path.Combine(outsideDirectory, "keep.txt");
        File.WriteAllText(outsideFile, "keep external data");
        try
        {
            var manifestPath = GetManifestPath(storageDirectory, runId);
            var mirrorPath = GetArtifactMirrorPath(storageDirectory, runId);
            File.WriteAllBytes(manifestPath, CreateSnapshot(runId, outsideDirectory));
            Directory.CreateDirectory(mirrorPath);
            using var registry = new RemoteRunRegistry(storageDirectory, workspaceRoot);

            Assert.IsTrue(registry.DeleteRun(runId, out var error), error);
            Assert.IsNull(error);
            Assert.IsFalse(File.Exists(manifestPath));
            Assert.IsFalse(Directory.Exists(mirrorPath));
            Assert.IsEmpty(registry.GetSnapshots());
            Assert.AreEqual("keep external data", File.ReadAllText(outsideFile));
            Assert.IsTrue(new RemoteRunDeletionResponse("request", runId, false,
                $"The remote run directory '{outsideDirectory}' is outside the RunServer's private run workspace. Cleanup failed.")
                .IsDeletedOrNotFound);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void TryGetArtifactMirror_RemovesManifestWhenWorkingDirectoryIsOutsidePrivateWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "XTMF2-RemoteRunRegistry-" + Guid.NewGuid().ToString("N"));
        var storageDirectory = Path.Combine(root, "state");
        var workspaceRoot = Path.Combine(root, "workspaces");
        var outsideDirectory = Path.Combine(root, "outside");
        var runId = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(storageDirectory);
        Directory.CreateDirectory(outsideDirectory);
        var externalFile = Path.Combine(outsideDirectory, "keep.txt");
        File.WriteAllText(externalFile, "do not delete");
        var manifestPath = GetManifestPath(storageDirectory, runId);
        File.WriteAllBytes(manifestPath, CreateSnapshot(runId, outsideDirectory));
        try
        {
            using var registry = new RemoteRunRegistry(storageDirectory, workspaceRoot);

            Assert.IsFalse(registry.TryGetArtifactMirror(runId, out _, out var error));
            Assert.Contains(outsideDirectory, error);
            Assert.IsFalse(File.Exists(manifestPath));
            Assert.IsEmpty(registry.GetSnapshots());
            Assert.AreEqual("do not delete", File.ReadAllText(externalFile));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void DeleteRun_UsesConfiguredPrivateWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "XTMF2-RemoteRunRegistry-" + Guid.NewGuid().ToString("N"));
        var storageDirectory = Path.Combine(root, "state");
        var workspaceRoot = Path.Combine(root, "workspaces");
        var runId = Guid.NewGuid().ToString("N");
        var workingDirectory = Path.Combine(workspaceRoot, runId);
        Directory.CreateDirectory(storageDirectory);
        Directory.CreateDirectory(workingDirectory);
        File.WriteAllText(Path.Combine(workingDirectory, "output.txt"), "run output");
        try
        {
            var manifestPath = GetManifestPath(storageDirectory, runId);
            File.WriteAllBytes(manifestPath, CreateSnapshot(runId, workingDirectory));
            using var registry = new RemoteRunRegistry(storageDirectory, workspaceRoot);

            Assert.IsTrue(registry.DeleteRun(runId, out var error), error);
            Assert.IsFalse(Directory.Exists(workingDirectory));
            Assert.IsFalse(File.Exists(manifestPath));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void TryGetArtifactMirror_PersistsAfterAcknowledgementAndRemovesOriginals()
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
            var mirrorPath = GetArtifactMirrorPath(storageDirectory, runId);
            File.WriteAllBytes(manifestPath, CreateSnapshot(runId, workingDirectory));
            using var registry = new RemoteRunRegistry(storageDirectory);

            Assert.IsTrue(registry.AcknowledgeReceived(runId));
            Assert.IsTrue(registry.GetSnapshots()[0].ArtifactsAvailable);
            var progress = new List<(long processed, long total)>();
            Assert.IsTrue(registry.TryGetArtifactMirror(runId, out var retainedMirror,
                (processed, total) => progress.Add((processed, total))));
            Assert.AreEqual(mirrorPath, retainedMirror);
            Assert.IsTrue(Directory.Exists(mirrorPath));
            Assert.IsFalse(Directory.Exists(workingDirectory));
            Assert.IsGreaterThanOrEqualTo(2, progress.Count);
            Assert.AreEqual(0L, progress[0].processed);
            Assert.AreEqual(Encoding.UTF8.GetByteCount("retained run output"), progress[^1].processed);
            Assert.AreEqual(progress[^1].processed, progress[^1].total);
            Assert.IsTrue(registry.TryGetArtifactMirror(runId, out var retrievedMirror));
            Assert.AreEqual(mirrorPath, retrievedMirror);
            var restoredDirectory = Path.Combine(storageDirectory, "restored");
            RunArtifactMirror.ExtractToDirectory(retainedMirror!, restoredDirectory);
            Assert.AreEqual("retained run output", File.ReadAllText(Path.Combine(restoredDirectory, "output.txt")));
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
    public void TryGetArtifactMirror_PreservesOriginalsWhenPublishedMirrorIsCorrupt()
    {
        var storageDirectory = Path.Combine(Path.GetTempPath(), "XTMF2-RemoteRunRegistry-" + Guid.NewGuid().ToString("N"));
        var runId = Guid.NewGuid().ToString("N");
        var workingDirectory = Path.Combine(Path.GetTempPath(), "XTMF2", "Runs", runId);
        Directory.CreateDirectory(workingDirectory);
        Directory.CreateDirectory(storageDirectory);
        var sourcePath = Path.Combine(workingDirectory, "output.txt");
        File.WriteAllText(sourcePath, "original output");
        var mirrorPath = GetArtifactMirrorPath(storageDirectory, runId);
        try
        {
            RunArtifactMirror.CreateFromDirectory(workingDirectory, mirrorPath);
            var payloadPath = RunArtifactMirror.GetPayloadPath(mirrorPath,
                RunArtifactMirror.ReadManifest(mirrorPath).Single(entry => !entry.IsDirectory));
            using (var payload = new FileStream(payloadPath, FileMode.Open, FileAccess.Write, FileShare.None))
                payload.WriteByte(0);
            File.WriteAllBytes(GetManifestPath(storageDirectory, runId),
                CreateSnapshot(runId, workingDirectory));
            using var registry = new RemoteRunRegistry(storageDirectory);

            Assert.IsFalse(registry.TryGetArtifactMirror(runId, out _));
            Assert.IsTrue(Directory.Exists(workingDirectory));
            Assert.AreEqual("original output", File.ReadAllText(sourcePath));
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

    private static string GetArtifactMirrorPath(string storageDirectory, string runId)
        => Path.Combine(storageDirectory,
            $"{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(runId)))}.mirror");

    private static long GetFirstPayloadEnd(byte[] transferBytes)
    {
        using var stream = new MemoryStream(transferBytes);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        reader.ReadInt32();
        var count = reader.ReadInt32();
        long firstPayloadLength = 0;
        for (var index = 0; index < count; index++)
        {
            var isDirectory = reader.ReadBoolean();
            reader.ReadString();
            reader.ReadInt64();
            var compressedLength = reader.ReadInt64();
            if (!isDirectory)
            {
                reader.ReadBytes(32);
                if (firstPayloadLength == 0)
                    firstPayloadLength = compressedLength;
            }
        }

        return stream.Position + firstPayloadLength;
    }

    private sealed class BlockingReadStream(byte[] content, long blockPosition) : Stream
    {
        private readonly MemoryStream _stream = new(content, writable: false);
        private readonly ManualResetEventSlim _blocked = new();
        private readonly ManualResetEventSlim _release = new();
        private int _hasBlocked;

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _stream.Length;
        public override long Position
        {
            get => _stream.Position;
            set => _stream.Position = value;
        }

        public bool WaitForBlock(TimeSpan timeout) => _blocked.Wait(timeout);
        public void Release() => _release.Set();

        public override int Read(byte[] buffer, int offset, int count)
        {
            BlockAtPosition();
            return _stream.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            BlockAtPosition();
            return _stream.Read(buffer);
        }

        private void BlockAtPosition()
        {
            if (_stream.Position >= blockPosition && Interlocked.Exchange(ref _hasBlocked, 1) == 0)
            {
                _blocked.Set();
                _release.Wait();
            }
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => _stream.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _stream.Dispose();
                _blocked.Dispose();
                _release.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}