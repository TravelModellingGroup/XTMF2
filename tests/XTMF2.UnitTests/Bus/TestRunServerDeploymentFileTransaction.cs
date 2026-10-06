using System;
using System.IO;
using System.IO.Compression;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.RunServer.Updater;

namespace XTMF2.UnitTests.Bus;

[TestClass]
public class TestRunServerDeploymentFileTransaction
{
    [TestMethod]
    public void ApplyAndRollback_RestoresPreviousRuntimeAndModules()
    {
        var root = Path.Combine(Path.GetTempPath(), "XTMF2UpdaterTests", Guid.NewGuid().ToString("N"));
        var installationDirectory = Path.Combine(root, "install");
        var deploymentDirectory = Path.Combine(root, "deployment");
        Directory.CreateDirectory(Path.Combine(installationDirectory, "Modules"));
        Directory.CreateDirectory(deploymentDirectory);
        File.WriteAllText(Path.Combine(installationDirectory, "XTMF2.dll"), "old runtime");
        File.WriteAllText(Path.Combine(installationDirectory, "Modules", "OldModule.dll"), "old module");
        CreateArchive(Path.Combine(deploymentDirectory, "deployment.zip"),
            ("XTMF2.dll", "new runtime"), ("Modules/NewModule.dll", "new module"));

        try
        {
            var transaction = new DeploymentFileTransaction(installationDirectory, deploymentDirectory);
            transaction.Apply();

            Assert.AreEqual("new runtime", File.ReadAllText(Path.Combine(installationDirectory, "XTMF2.dll")));
            Assert.IsTrue(File.Exists(Path.Combine(installationDirectory, "Modules", "NewModule.dll")));
            Assert.IsFalse(File.Exists(Path.Combine(installationDirectory, "Modules", "OldModule.dll")));

            transaction.Rollback();

            Assert.AreEqual("old runtime", File.ReadAllText(Path.Combine(installationDirectory, "XTMF2.dll")));
            Assert.IsTrue(File.Exists(Path.Combine(installationDirectory, "Modules", "OldModule.dll")));
            Assert.IsFalse(File.Exists(Path.Combine(installationDirectory, "Modules", "NewModule.dll")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Apply_RejectsTraversalBeforeChangingInstalledFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "XTMF2UpdaterTests", Guid.NewGuid().ToString("N"));
        var installationDirectory = Path.Combine(root, "install");
        var deploymentDirectory = Path.Combine(root, "deployment");
        Directory.CreateDirectory(Path.Combine(installationDirectory, "Modules"));
        Directory.CreateDirectory(deploymentDirectory);
        File.WriteAllText(Path.Combine(installationDirectory, "XTMF2.dll"), "old runtime");
        File.WriteAllText(Path.Combine(installationDirectory, "Modules", "OldModule.dll"), "old module");
        CreateArchive(Path.Combine(deploymentDirectory, "deployment.zip"),
            ("XTMF2.dll", "new runtime"), ("Modules/../outside.dll", "unexpected"));

        try
        {
            var transaction = new DeploymentFileTransaction(installationDirectory, deploymentDirectory);

            Assert.Throws<InvalidDataException>(() => transaction.Apply());
            Assert.AreEqual("old runtime", File.ReadAllText(Path.Combine(installationDirectory, "XTMF2.dll")));
            Assert.IsTrue(File.Exists(Path.Combine(installationDirectory, "Modules", "OldModule.dll")));
            Assert.IsFalse(File.Exists(Path.Combine(root, "outside.dll")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void CreateArchive(string archivePath, params (string Name, string Content)[] files)
    {
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        foreach (var (name, content) in files)
        {
            var entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
    }
}
