using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Configuration;

namespace XTMF2.UnitTests.Bus;

[TestClass]
public class TestRunServerConfiguration
{
    [TestMethod]
    public void SaveAndLoad_ResolvesConfiguredPathsRelativeToFile()
    {
        var directory = Directory.CreateTempSubdirectory("xtmf-runserver-config-");
        try
        {
            var configPath = Path.Combine(directory.FullName, "server.json");
            var configuration = new RunServerConfiguration
            {
                ServerStorageDirectory = "data",
                ListenAddress = "127.0.0.1",
                Port = 4321,
                RunWorkspaceBaseDirectory = Path.Combine("scratch", "runs"),
                AdditionalAssemblies = [Path.Combine("Modules", "Custom.dll")],
            };

            configuration.Save(configPath);
            var savedJson = File.ReadAllText(configPath);
            var loaded = RunServerConfiguration.Load(configPath);
            var paths = loaded.CreateStoragePaths();

            StringAssert.Contains(savedJson, "\"serverStorageDirectory\"");
            StringAssert.Contains(savedJson, "\"runWorkspaceBaseDirectory\"");
            Assert.IsFalse(savedJson.Contains("\"dataRoot\"", StringComparison.Ordinal));
            Assert.IsFalse(savedJson.Contains("\"runWorkspaceRoot\"", StringComparison.Ordinal));
            Assert.AreEqual(Path.Combine(directory.FullName, "data"), loaded.ServerStorageDirectory);
            Assert.AreEqual(Path.Combine(directory.FullName, "data", "RemoteRuns"), paths.RemoteRunDirectory);
            Assert.AreEqual(Path.Combine(directory.FullName, "scratch", "runs"), paths.RunWorkspaceBaseDirectory);
            Assert.AreEqual(Path.Combine(directory.FullName, "data", "Deployments"), paths.DeploymentDirectory);
            Assert.AreEqual(Path.Combine(directory.FullName, "Modules", "Custom.dll"), loaded.AdditionalAssemblies.Single());
            Assert.AreEqual(4321, loaded.Port);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [TestMethod]
    public void Load_UsesServerStorageDirectoryForDefaultWorkspaceBase()
    {
        var directory = Directory.CreateTempSubdirectory("xtmf-runserver-config-");
        try
        {
            var configPath = Path.Combine(directory.FullName, "server.json");
            File.WriteAllText(configPath, "{\"version\":1,\"serverStorageDirectory\":\"data\"}");

            var configuration = RunServerConfiguration.Load(configPath);

            Assert.AreEqual(Path.Combine(directory.FullName, "data", "Runs"),
                configuration.CreateStoragePaths().RunWorkspaceBaseDirectory);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [TestMethod]
    public void Validate_RequiresSecurityForNonLoopbackListener()
    {
        var configuration = new RunServerConfiguration
        {
            ListenAddress = "0.0.0.0",
            Port = 5000,
        };

        Assert.ThrowsExactly<InvalidDataException>(() => configuration.Validate());
    }

    [TestMethod]
    public void Validate_RequiresSecurityForDeployment()
    {
        var configuration = new RunServerConfiguration
        {
            AllowDeployment = true,
        };

        Assert.ThrowsExactly<InvalidDataException>(() => configuration.Validate());
    }

    [TestMethod]
    public void Load_RejectsUnsupportedVersion()
    {
        var directory = Directory.CreateTempSubdirectory("xtmf-runserver-config-");
        try
        {
            var configPath = Path.Combine(directory.FullName, "server.json");
            File.WriteAllText(configPath, "{\"version\":2}");

            Assert.ThrowsExactly<InvalidDataException>(() => RunServerConfiguration.Load(configPath));
        }
        finally
        {
            directory.Delete(true);
        }
    }

}