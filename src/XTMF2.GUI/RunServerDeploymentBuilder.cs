using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using XTMF2;

namespace XTMF2.GUI;

internal static class RunServerDeploymentBuilder
{
    public sealed record DeploymentArchive(byte[] Content, IReadOnlyList<string> ModuleNames);

    public static DeploymentArchive CreateLocalArchive(IReadOnlyList<string> selectedModules)
    {
        if (selectedModules.Count == 0)
            throw new InvalidOperationException("Select at least one module DLL to upload.");

        var runtimeAssembly = typeof(XTMFRuntime).Assembly.Location;
        var files = selectedModules
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length != selectedModules.Count)
            throw new FileNotFoundException("One or more selected module DLLs no longer exists.");

        var moduleNames = files.Select(path => Path.GetFileName(path)!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var runtimeEntry = archive.CreateEntry("XTMF2.dll", CompressionLevel.Fastest);
            using (var source = File.OpenRead(runtimeAssembly))
            using (var destination = runtimeEntry.Open())
                source.CopyTo(destination);

            foreach (var file in files)
            {
                var entry = archive.CreateEntry("Modules/" + Path.GetFileName(file), CompressionLevel.Fastest);
                using var source = File.OpenRead(file);
                using var destination = entry.Open();
                source.CopyTo(destination);
            }

            var manifest = archive.CreateEntry("deployment-manifest.txt", CompressionLevel.Fastest);
            using var manifestWriter = new StreamWriter(manifest.Open());
            manifestWriter.WriteLine("XTMF2.dll");
            foreach (var moduleName in moduleNames)
                manifestWriter.WriteLine("Modules/" + moduleName);
        }
        return new DeploymentArchive(output.ToArray(), moduleNames);
    }
}
