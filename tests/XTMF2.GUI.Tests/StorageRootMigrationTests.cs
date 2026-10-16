using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.GUI;

namespace XTMF2.GUI.Tests;

[TestClass]
public class StorageRootMigrationTests
{
    [TestMethod]
    public void Copy_PreservesFolderStructureAndReportsOrderedStages()
    {
        var root = Directory.CreateTempSubdirectory("xtmf-storage-migration-");
        var source = Path.Combine(root.FullName, "source");
        var destination = Path.Combine(root.FullName, "destination");
        try
        {
            WriteFile(source, Path.Combine("Users", "local", "Project", "model.xmsf"), "model");
            WriteFile(source, Path.Combine("Deployments", "module.dll"), "module");
            WriteFile(source, Path.Combine("Runs", "run-1", "output.bin"), "run");
            WriteRunManifest(source, Path.Combine("RemoteRuns", "run-1.json"),
                Path.Combine(source, "Runs", "run-1"));
            Directory.CreateDirectory(Path.Combine(source, "Users", "local", "EmptyProject"));
            var progress = new RecordingProgress();

            StorageRootMigration.Migrate(source, destination, StorageRootMigrationMode.Copy, progress);

            Assert.IsTrue(File.Exists(Path.Combine(destination, "Users", "local", "Project", "model.xmsf")));
            Assert.IsTrue(Directory.Exists(Path.Combine(destination, "Users", "local", "EmptyProject")));
            Assert.IsTrue(File.Exists(Path.Combine(destination, "Deployments", "module.dll")));
            Assert.IsTrue(File.Exists(Path.Combine(destination, "Runs", "run-1", "output.bin")));
            Assert.IsTrue(File.Exists(Path.Combine(destination, "RemoteRuns", "run-1.json")));
            using (var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(destination, "RemoteRuns", "run-1.json"))))
            {
                Assert.AreEqual(Path.Combine(destination, "Runs", "run-1"),
                    manifest.RootElement.GetProperty("WorkingDirectory").GetString());
            }
            Assert.IsTrue(File.Exists(Path.Combine(source, "Runs", "run-1", "output.bin")));

            CollectionAssert.AreEqual(new[]
            {
                "Scanning current data...",
                "Preparing destination folder structure...",
                "Copying projects and model systems...",
                "Copying other XTMF data...",
                "Copying run data...",
                "Migration complete.",
            }, progress.GetDistinctStatuses());
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public void Move_DoesNotDeleteAnySourceDataWhenRunCopyFails()
    {
        var root = Directory.CreateTempSubdirectory("xtmf-storage-migration-");
        var source = Path.Combine(root.FullName, "source");
        var destination = Path.Combine(root.FullName, "destination");
        try
        {
            WriteFile(source, Path.Combine("Users", "local", "Project", "model.xmsf"), "model");
            WriteFile(source, Path.Combine("Runs", "run-1", "output.bin"), "run");
            Directory.CreateDirectory(Path.Combine(destination, "Runs", "run-1", "output.bin"));

            Assert.ThrowsExactly<IOException>(() => StorageRootMigration.Migrate(source, destination,
                StorageRootMigrationMode.Move));

            Assert.IsTrue(File.Exists(Path.Combine(source, "Users", "local", "Project", "model.xmsf")));
            Assert.IsTrue(File.Exists(Path.Combine(source, "Runs", "run-1", "output.bin")));
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public void Copy_MergesIntoExistingDestinationAndKeepsDestinationOnlyFiles()
    {
        var root = Directory.CreateTempSubdirectory("xtmf-storage-migration-");
        var source = Path.Combine(root.FullName, "source");
        var destination = Path.Combine(root.FullName, "destination");
        try
        {
            WriteFile(source, Path.Combine("Users", "local", "Project", "model.xmsf"), "source model");
            WriteFile(destination, Path.Combine("Users", "local", "Project", "model.xmsf"), "old model");
            WriteFile(destination, Path.Combine("Users", "other", "Project", "notes.txt"), "keep me");

            StorageRootMigration.Migrate(source, destination, StorageRootMigrationMode.Copy);

            Assert.AreEqual("source model", File.ReadAllText(Path.Combine(destination,
                "Users", "local", "Project", "model.xmsf")));
            Assert.AreEqual("keep me", File.ReadAllText(Path.Combine(destination,
                "Users", "other", "Project", "notes.txt")));
            Assert.IsTrue(File.Exists(Path.Combine(source, "Users", "local", "Project", "model.xmsf")));
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public void Move_RemovesSourceOnlyAfterAllDataHasBeenCopied()
    {
        var root = Directory.CreateTempSubdirectory("xtmf-storage-migration-");
        var source = Path.Combine(root.FullName, "source");
        var destination = Path.Combine(root.FullName, "destination");
        try
        {
            WriteFile(source, Path.Combine("Users", "local", "Project", "model.xmsf"), "model");
            WriteFile(source, Path.Combine("Runs", "run-1", "output.bin"), "run");
            WriteRunManifest(source, Path.Combine("RemoteRuns", "run-1.json"),
                Path.Combine(source, "Runs", "run-1"));

            StorageRootMigration.Migrate(source, destination, StorageRootMigrationMode.Move);

            Assert.IsFalse(Directory.Exists(source));
            Assert.AreEqual("model", File.ReadAllText(Path.Combine(destination, "Users", "local", "Project", "model.xmsf")));
            Assert.AreEqual("run", File.ReadAllText(Path.Combine(destination, "Runs", "run-1", "output.bin")));
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(destination, "RemoteRuns", "run-1.json")));
            Assert.AreEqual(Path.Combine(destination, "Runs", "run-1"),
                manifest.RootElement.GetProperty("WorkingDirectory").GetString());
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public void Move_CancellationAtSourceRemovalCheckpointKeepsSourceData()
    {
        var root = Directory.CreateTempSubdirectory("xtmf-storage-migration-");
        var source = Path.Combine(root.FullName, "source");
        var destination = Path.Combine(root.FullName, "destination");
        using var cancellation = new System.Threading.CancellationTokenSource();
        try
        {
            WriteFile(source, Path.Combine("Users", "local", "Project", "model.xmsf"), "model");
            WriteFile(source, Path.Combine("Runs", "run-1", "output.bin"), "run");

            Assert.ThrowsExactly<OperationCanceledException>(() => StorageRootMigration.Migrate(
                source, destination, StorageRootMigrationMode.Move,
                new CancellingProgress(value =>
                {
                    if (value.Status == "Removing copied source data...")
                        cancellation.Cancel();
                }),
                cancellationToken: cancellation.Token));

            Assert.IsTrue(File.Exists(Path.Combine(source, "Users", "local", "Project", "model.xmsf")));
            Assert.IsTrue(File.Exists(Path.Combine(source, "Runs", "run-1", "output.bin")));
            Assert.IsTrue(File.Exists(Path.Combine(destination, "Users", "local", "Project", "model.xmsf")));
        }
        finally
        {
            root.Delete(true);
        }
    }

    private static void WriteFile(string root, string relativePath, string contents)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    private static void WriteRunManifest(string root, string relativePath, string workingDirectory)
    {
        WriteFile(root, relativePath, JsonSerializer.Serialize(new { WorkingDirectory = workingDirectory }));
    }

    private sealed class RecordingProgress : IProgress<StorageRootMigrationProgress>
    {
        private readonly List<string> _statuses = [];

        public void Report(StorageRootMigrationProgress value)
        {
            if (_statuses.Count == 0 || _statuses[^1] != value.Status)
                _statuses.Add(value.Status);
        }

        public string[] GetDistinctStatuses() => _statuses.ToArray();
    }

    private sealed class CancellingProgress(Action<StorageRootMigrationProgress> onReport)
        : IProgress<StorageRootMigrationProgress>
    {
        public void Report(StorageRootMigrationProgress value) => onReport(value);
    }
}