using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Bus;
using XTMF2.Bus.Optimization;

namespace XTMF2.UnitTests.Bus.Optimization;

[TestClass]
public class TestSharedEstimationWorkerPool
{
    [TestMethod]
    public void StartRun_DropsWorkerWithNonWritableStreamAndKeepsRunActive()
    {
        using var failedBus = new HostBus(new RecordingDuplexStream(failWrites: true), true);
        var healthyStream = new RecordingDuplexStream();
        using var healthyBus = new HostBus(healthyStream, true);
        using var pool = new SharedEstimationWorkerPool();
        Assert.IsTrue(pool.AddExistingWorker("worker-failed", "endpoint-failed", failedBus, out var error), error);
        Assert.IsTrue(pool.AddExistingWorker("worker-healthy", "endpoint-healthy", healthyBus, out error), error);

        Assert.IsTrue(pool.StartRun(new SharedEstimationRunRequest("run-resilient", "/tmp/run-resilient",
            "Start", [1]), out error), error);

        Assert.AreEqual(0, pool.WorkerCount);
        CollectionAssert.AreEqual(new[] { "worker-healthy" }, pool.WorkerIds.ToArray());
        var evaluation = pool.Coordinator.EvaluateAsync([
            new SharedEstimationCandidate("run-resilient", 1, "candidate-healthy", [0.25])]);
        Assert.IsFalse(evaluation.IsCompleted);
        var startFrameLength = healthyStream.GetWrittenBytes().Length;
        healthyStream.SignalWorkerReady("run-resilient", succeeded: true);
        Assert.IsTrue(healthyStream.WaitForWrittenBytes(startFrameLength + 1, TimeSpan.FromSeconds(3)));
        Assert.AreEqual(1, pool.WorkerCount);
    }

    [TestMethod]
    public void AddExistingWorker_SendsActiveRunBeforePendingCandidates()
    {
        var stream = new RecordingDuplexStream();
        using var bus = new HostBus(stream, true);
        using var pool = new SharedEstimationWorkerPool();
        var request = new SharedEstimationRunRequest("run-reconnected", "/tmp/run-reconnected",
            "Start", [1, 2, 3]);
        Assert.IsTrue(pool.StartRun(request, out var error), error);
        var candidate = new SharedEstimationCandidate("run-reconnected", 1,
            "candidate-reconnected", [0.5]);
        var evaluation = pool.Coordinator.EvaluateAsync([candidate]);

        Assert.IsTrue(pool.AddExistingWorker("worker-1", "endpoint-1", bus, out error), error);

        using var payload = new MemoryStream(stream.GetWrittenBytes());
        using var reader = new BinaryReader(payload);
        Assert.AreEqual(5, reader.ReadInt32());
        var (startVersion, startType) = SharedEstimationProtocol.ReadHeader(reader);
        Assert.AreEqual(SharedEstimationProtocol.Version, startVersion);
        Assert.AreEqual(SharedEstimationMessageType.StartRun, startType);
        Assert.AreEqual("run-reconnected", SharedEstimationProtocol.ReadRunRequestPayload(reader).RunId);
        Assert.AreEqual(payload.Length, payload.Position);

        var startFrameLength = stream.GetWrittenBytes().Length;
        var candidateFrameLength = GetFrameLength(writer =>
        {
            writer.Write(5);
            SharedEstimationProtocol.WriteCandidate(writer, candidate);
        });
        Assert.AreEqual(0, pool.WorkerCount);
        Assert.IsFalse(evaluation.IsCompleted);
        stream.SignalWorkerReady("run-reconnected", succeeded: true);
        Assert.IsTrue(stream.WaitForWrittenBytes(startFrameLength + candidateFrameLength,
            TimeSpan.FromSeconds(3)));

        using var completedPayload = new MemoryStream(stream.GetWrittenBytes());
        using var completedReader = new BinaryReader(completedPayload);
        Assert.AreEqual(5, completedReader.ReadInt32());
        var (startVersionAgain, startTypeAgain) = SharedEstimationProtocol.ReadHeader(completedReader);
        Assert.AreEqual(SharedEstimationProtocol.Version, startVersionAgain);
        Assert.AreEqual(SharedEstimationMessageType.StartRun, startTypeAgain);
        SharedEstimationProtocol.ReadRunRequestPayload(completedReader);
        Assert.AreEqual(5, completedReader.ReadInt32());
        var (_, candidateType) = SharedEstimationProtocol.ReadHeader(completedReader);
        Assert.AreEqual(SharedEstimationMessageType.EvaluateCandidate, candidateType);
        Assert.AreEqual("candidate-reconnected",
            SharedEstimationProtocol.ReadCandidatePayload(completedReader).CandidateId);
        Assert.IsFalse(evaluation.IsCompleted);
    }

    [TestMethod]
    public void StartRun_SendsSharedEndpointSlotCountToEachWorker()
    {
        var firstStream = new RecordingDuplexStream();
        var secondStream = new RecordingDuplexStream();
        using var firstBus = new HostBus(firstStream, true);
        using var secondBus = new HostBus(secondStream, true);
        using var pool = new SharedEstimationWorkerPool();
        Assert.IsTrue(pool.AddExistingWorker("worker-1", "endpoint-1", firstBus, out var error), error);
        Assert.IsTrue(pool.AddExistingWorker("worker-2", "endpoint-1", secondBus, out error), error);

        Assert.IsTrue(pool.StartRun(new SharedEstimationRunRequest("run-slots", "/tmp/run-slots",
            "Start", [1]), out error), error);

        Assert.AreEqual(2, ReadWorkerSlotCount(firstStream.GetWrittenBytes()));
        Assert.AreEqual(2, ReadWorkerSlotCount(secondStream.GetWrittenBytes()));
    }

    [TestMethod]
    public async Task StartRun_ReportsFailureWhenOnlyWorkerCannotPrepare()
    {
        var stream = new RecordingDuplexStream();
        using var bus = new HostBus(stream, true);
        using var pool = new SharedEstimationWorkerPool();
        Assert.IsTrue(pool.AddExistingWorker("worker-1", "endpoint-1", bus, out var error), error);
        var failure = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        pool.WorkerPreparationFailed += message => failure.TrySetResult(message);
        Assert.IsTrue(pool.StartRun(new SharedEstimationRunRequest("run-prepare-failure",
            "/tmp/run-prepare-failure", "Start", [1]), out error), error);

        stream.SignalWorkerReady("run-prepare-failure", succeeded: false, "Model preparation failed.");

        Assert.AreEqual("Model preparation failed.", await failure.Task.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.AreEqual(0, pool.WorkerCount);
    }

    private static int ReadWorkerSlotCount(byte[] frame)
    {
        using var stream = new MemoryStream(frame);
        using var reader = new BinaryReader(stream);
        _ = reader.ReadInt32();
        SharedEstimationProtocol.ReadHeader(reader);
        return SharedEstimationProtocol.ReadRunRequestPayload(reader).WorkerSlotCount;
    }

    private static int GetFrameLength(Action<BinaryWriter> writeFrame)
    {
        using var frame = new MemoryStream();
        using (var writer = new BinaryWriter(frame, System.Text.Encoding.UTF8, true))
            writeFrame(writer);
        return checked((int)frame.Length);
    }

    private sealed class RecordingDuplexStream : Stream
    {
        private readonly object _sync = new();
        private readonly MemoryStream _written = new();
        private readonly List<byte> _incoming = [];
        private int _incomingOffset;
        private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly bool _failWrites;

        public RecordingDuplexStream(bool failWrites = false)
        {
            _failWrites = failWrites;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => !_closed.Task.IsCompleted && !_failWrites;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public byte[] GetWrittenBytes()
        {
            lock (_sync)
                return _written.ToArray();
        }

        public bool WaitForWrittenBytes(int minimumBytes, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            lock (_sync)
            {
                while (_written.Length < minimumBytes && !_closed.Task.IsCompleted)
                {
                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                        return false;
                    Monitor.Wait(_sync, remaining);
                }
                return _written.Length >= minimumBytes;
            }
        }

        public void SignalWorkerReady(string runId, bool succeeded, string error = null)
        {
            using var frame = new MemoryStream();
            using (var writer = new BinaryWriter(frame, System.Text.Encoding.UTF8, true))
            {
                writer.Write(12);
                SharedEstimationProtocol.WriteWorkerReady(writer,
                    new SharedEstimationWorkerReady(runId, succeeded, error));
            }
            lock (_sync)
            {
                _incoming.AddRange(frame.ToArray());
                Monitor.PulseAll(_sync);
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            lock (_sync)
            {
                while (_incomingOffset == _incoming.Count && !_closed.Task.IsCompleted)
                    Monitor.Wait(_sync);
                if (_incomingOffset == _incoming.Count)
                    return 0;
                var readCount = Math.Min(count, _incoming.Count - _incomingOffset);
                _incoming.CopyTo(_incomingOffset, buffer, offset, readCount);
                _incomingOffset += readCount;
                if (_incomingOffset == _incoming.Count)
                {
                    _incoming.Clear();
                    _incomingOffset = 0;
                }
                return readCount;
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_failWrites)
                throw new InvalidOperationException("Stream was not writable.");
            lock (_sync)
            {
                _written.Write(buffer, offset, count);
                Monitor.PulseAll(_sync);
            }
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (_failWrites)
                throw new InvalidOperationException("Stream was not writable.");
            lock (_sync)
            {
                _written.Write(buffer);
                Monitor.PulseAll(_sync);
            }
        }

        protected override void Dispose(bool disposing)
        {
            _closed.TrySetResult();
            lock (_sync)
                Monitor.PulseAll(_sync);
            if (disposing)
                _written.Dispose();
            base.Dispose(disposing);
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}