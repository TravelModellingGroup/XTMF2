using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Bus;

namespace XTMF2.UnitTests.Bus;

[TestClass]
public class TestRunServerDeploymentGate
{
    private const int DeploymentMessageCommand = 6;

    [TestMethod]
    public void ProcessRequests_RejectsDeploymentWhenDisabled()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(DeploymentMessageCommand);
            DeploymentProtocol.WriteBeginDrain(writer, "00000000000000000000000000000001");
            writer.Flush();
        }
        stream.Position = 0;

        using var runServer = new RunServerBus(stream, streamOwner: false, runtime: null!, allowDeployment: false);
        var drainRequested = false;
        runServer.SetDeploymentGate(
            () => drainRequested = true,
            () => { },
            () => true);

        runServer.ProcessRequests();

        Assert.IsFalse(drainRequested);
    }
}