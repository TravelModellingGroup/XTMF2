using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Bus;

namespace XTMF2.UnitTests.Bus;

[TestClass]
[DoNotParallelize]
public class TestSharedEstimationWorkingDirectoryLease
{
    [TestMethod]
    public async Task Acquire_AllowsSameDirectoryAndQueuesDifferentDirectories()
    {
        var originalDirectory = Directory.GetCurrentDirectory();
        var firstDirectory = Directory.CreateTempSubdirectory("xtmf-worker-cwd-a-");
        var secondDirectory = Directory.CreateTempSubdirectory("xtmf-worker-cwd-b-");
        Task<string> differentDirectory = Task.FromResult(String.Empty);
        try
        {
            using (var firstLease = SharedEstimationWorkingDirectoryLease.Acquire(firstDirectory.FullName))
            {
                Assert.AreEqual(firstDirectory.FullName, Directory.GetCurrentDirectory());
                var sameDirectory = Task.Run(() =>
                {
                    using var lease = SharedEstimationWorkingDirectoryLease.Acquire(firstDirectory.FullName);
                    return Directory.GetCurrentDirectory();
                });
                Assert.AreEqual(firstDirectory.FullName, await sameDirectory);

                using var started = new ManualResetEventSlim();
                differentDirectory = Task.Run(() =>
                {
                    started.Set();
                    using var lease = SharedEstimationWorkingDirectoryLease.Acquire(secondDirectory.FullName);
                    return Directory.GetCurrentDirectory();
                });
                Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(3)));
                Assert.IsFalse(differentDirectory.Wait(TimeSpan.FromMilliseconds(50)));
            }

            Assert.AreEqual(secondDirectory.FullName, await differentDirectory);
            Assert.AreEqual(originalDirectory, Directory.GetCurrentDirectory());
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            firstDirectory.Delete(true);
            secondDirectory.Delete(true);
        }
    }
}