using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Bus;
using XTMF2.RuntimeModules;
using static XTMF2.UnitTests.TestHelper;

namespace XTMF2.UnitTests.RuntimeModules;

[TestClass]
public class TestLog
{
    [TestMethod]
    public void DisposeWaitsForQueuedStatusMessagesToBeSent()
    {
        var runtime = CreateRuntime();
        using var sendStarted = new ManualResetEventSlim();
        using var allowSendToFinish = new ManualResetEventSlim();
        var sentMessages = new ConcurrentQueue<string>();
        using var runBus = new RunBus("log-disposal-test", message =>
        {
            sendStarted.Set();
            allowSendToFinish.Wait();
            sentMessages.Enqueue(message);
        }, runtime);

        var log = new Log
        {
            LogStream = new OpenWriteStreamToRunStatus(runtime)
        };

        try
        {
            log.Invoke("first");
            Assert.IsTrue(sendStarted.Wait(TimeSpan.FromSeconds(5)), "The asynchronous status send did not start.");
            log.Invoke("second");

            var disposeTask = Task.Run(log.Dispose);
            Assert.IsFalse(disposeTask.Wait(TimeSpan.FromMilliseconds(100)),
                "Dispose returned while a queued status message was still being sent.");

            allowSendToFinish.Set();
            disposeTask.GetAwaiter().GetResult();

            Assert.HasCount(2, sentMessages);
            Assert.IsTrue(sentMessages.TryDequeue(out var firstMessage));
            Assert.IsTrue(firstMessage.EndsWith("first", StringComparison.Ordinal));
            Assert.IsTrue(sentMessages.TryDequeue(out var secondMessage));
            Assert.IsTrue(secondMessage.EndsWith("second", StringComparison.Ordinal));
        }
        finally
        {
            allowSendToFinish.Set();
            log.Dispose();
        }
    }
}
