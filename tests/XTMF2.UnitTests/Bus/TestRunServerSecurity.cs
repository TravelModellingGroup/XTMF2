using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Bus;

namespace XTMF2.UnitTests.Bus;

[TestClass]
public class TestRunServerSecurity
{
    [TestMethod]
    public void SecureTcpClient_AuthenticatesWithCertificateFingerprintAndToken()
    {
        var securityDirectory = Directory.CreateTempSubdirectory("xtmf-security-test-");
        try
        {
            RunServerSecurity.SaveSetup(securityDirectory.FullName, out var token, out var fingerprint);
            using var certificate = RunServerSecurity.LoadCertificate(securityDirectory.FullName);
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var server = Task.Run(() =>
            {
                using var client = listener.AcceptTcpClient();
                Assert.IsTrue(CreateStreams.AuthenticateSecureTcpClient(
                    client, certificate, token, out var stream, out var error), error);
                stream?.Dispose();
            });

            Assert.IsTrue(CreateStreams.CreateSecureTcpClient(
                "127.0.0.1", port, token, fingerprint,
                out var clientStream, out var clientError), clientError);
            clientStream!.Dispose();
            Assert.IsTrue(server.Wait(TimeSpan.FromSeconds(5)), "The secure server handshake did not complete.");
            server.GetAwaiter().GetResult();
        }
        finally
        {
            securityDirectory.Delete(true);
        }
    }

    [TestMethod]
    public void SecureTcpClient_RejectsInvalidToken()
    {
        var securityDirectory = Directory.CreateTempSubdirectory("xtmf-security-test-");
        try
        {
            RunServerSecurity.SaveSetup(securityDirectory.FullName, out var token, out var fingerprint);
            using var certificate = RunServerSecurity.LoadCertificate(securityDirectory.FullName);
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var server = Task.Run(() =>
            {
                using var client = listener.AcceptTcpClient();
                CreateStreams.AuthenticateSecureTcpClient(
                    client, certificate, token, out var stream, out _);
                stream?.Dispose();
            });

            Assert.IsFalse(CreateStreams.CreateSecureTcpClient(
                "127.0.0.1", port, RunServerSecurity.CreateToken(), fingerprint,
                out var clientStream, out _));
            clientStream?.Dispose();
            Assert.IsTrue(server.Wait(TimeSpan.FromSeconds(5)), "The secure server handshake did not complete.");
            server.GetAwaiter().GetResult();
        }
        finally
        {
            securityDirectory.Delete(true);
        }
    }
}
