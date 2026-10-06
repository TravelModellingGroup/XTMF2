using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.RunServer.Updater;

namespace XTMF2.UnitTests.Bus;

[TestClass]
public class TestRunServerUpdaterArguments
{
    [TestMethod]
    public void Parse_PreservesPathsAndRepeatedServerArguments()
    {
        var serverArguments = new[] { "-tcp", "0.0.0.0", "1234", "-security", "C:\\Run Server\\security" };
        var args = new[]
        {
            "--old-pid", "123",
            "--install-dir", "C:\\Run Server",
            "--server-path", "C:\\Run Server\\XTMF2.RunServer.exe",
            "--server-assembly", "C:\\Run Server\\XTMF2.RunServer.dll",
            "--deployment-dir", "C:\\Run Server\\.deployments\\request",
            "--staging-root", "C:\\Users\\user\\AppData\\Local\\Temp\\deployment",
            "--server-arg", serverArguments[0],
            "--server-arg", serverArguments[1],
            "--server-arg", serverArguments[2],
            "--server-arg", serverArguments[3],
            "--server-arg", serverArguments[4]
        };

        var options = UpdaterArguments.Parse(args);

        Assert.AreEqual(123, options.PreviousProcessId);
        Assert.AreEqual(Path.GetFullPath("C:\\Run Server"), options.InstallationDirectory);
        CollectionAssert.AreEqual(serverArguments, options.ServerArguments.ToArray());
    }

    [TestMethod]
    public void Parse_RejectsMissingRequiredOptions()
    {
        Assert.Throws<ArgumentException>(() => UpdaterArguments.Parse(Array.Empty<string>()));
    }
}
