using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Bus;

namespace XTMF2.UnitTests.Bus;

[TestClass]
public class TestHostBusDisposal
{
    [TestMethod]
    public void Dispose_ClosesOwnedStreamBeforeWaitingForListener()
    {
        var stream = new BlockingReadStream();
        var bus = new HostBus(stream, true);
        Assert.IsTrue(stream.ReadStarted.Wait(TimeSpan.FromSeconds(2)));

        var disposeTask = Task.Run(bus.Dispose);

        Assert.IsTrue(disposeTask.Wait(TimeSpan.FromSeconds(2)),
            "HostBus.Dispose should close the stream to release its blocked listener.");
        Assert.IsTrue(stream.IsDisposed);
    }

    private sealed class BlockingReadStream : Stream
    {
        private readonly ManualResetEventSlim _readReleased = new();
        private volatile bool _disposed;

        public ManualResetEventSlim ReadStarted { get; } = new();
        public bool IsDisposed => _disposed;
        public override bool CanRead => !_disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadStarted.Set();
            _readReleased.Wait();
            return 0;
        }

        protected override void Dispose(bool disposing)
        {
            _disposed = true;
            _readReleased.Set();
            base.Dispose(disposing);
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}