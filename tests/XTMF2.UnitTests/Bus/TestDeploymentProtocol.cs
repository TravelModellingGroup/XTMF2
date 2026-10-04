using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Bus;

namespace XTMF2.UnitTests.Bus;

[TestClass]
public class TestDeploymentProtocol
{
    [TestMethod]
    public void DrainStatus_RoundTripsWithVersionAndPayload()
    {
        var expected = new RunServerDrainStatus("request-1", RunServerDrainState.Draining, 2, 3,
            "Waiting for existing work.");
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            DeploymentProtocol.WriteDrainStatus(writer, expected);
            writer.Flush();
        }

        stream.Position = 0;
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
        var header = DeploymentProtocol.ReadHeader(reader);
        var actual = DeploymentProtocol.ReadStatusPayload(reader);

        Assert.AreEqual(1, header.Version);
        Assert.AreEqual(DeploymentMessageType.DrainStatus, header.Type);
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void DrainStatus_RejectsNegativeActivityCounts()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            writer.Write(1);
            writer.Write((int)DeploymentMessageType.DrainStatus);
            writer.Write("request-1");
            writer.Write((int)RunServerDrainState.Draining);
            writer.Write(-1);
            writer.Write(0);
            writer.Write(false);
            writer.Flush();
        }

        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        DeploymentProtocol.ReadHeader(reader);
        Assert.Throws<InvalidDataException>(() => DeploymentProtocol.ReadStatusPayload(reader));
    }
}
