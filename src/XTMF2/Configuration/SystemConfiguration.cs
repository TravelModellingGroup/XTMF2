/*
    Copyright 2017-2026 University of Toronto

    This file is part of XTMF2.

    XTMF2 is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    XTMF2 is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with XTMF2.  If not, see <http://www.gnu.org/licenses/>.
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using XTMF2.Repository;
using System.Runtime.Loader;
using System.Linq;

namespace XTMF2.Configuration;

/// <summary>
/// This class contains the configuration data
/// for the full XTMF runtime. It does not contain any
/// user level configuration.
/// </summary>
public class SystemConfiguration
{
    /// <summary>
    /// The repository of modules available to this XTMF runtime
    /// </summary>
    public ModuleRepository Modules { get; private set; }

    /// <summary>
    /// The repository of all different available types available to this XTMF runtime
    /// </summary>
    public TypeRepository Types { get; private set; }

    /// <summary>
    /// The path to the default user directory
    /// </summary>
    public string DefaultUserDirectory { get; private set; }

    /// <summary>
    /// Create a new system configuration for the given XTMF Runtime.
    /// </summary>
    /// <param name="runtime">The runtime to bind to.</param>
    /// <param name="fullPath">Optional, the path to the system configuration.</param>
    public SystemConfiguration(XTMFRuntime runtime, string? fullPath = null)
    {
        CreateDirectory(DefaultUserDirectory = fullPath
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "XTMF2", "Users"));
        Modules = new ModuleRepository();
        Types = new TypeRepository();
        // Seed the type repository with common BCL types so that the GUI type-picker
        // can offer them as context-type arguments (e.g. IFunction<string, bool>)
        // even when no XTMF module assembly happens to reference them.
        SeedBuiltInTypes();
        // Load the entry assembly for types
        LoadAssembly(Assembly.GetEntryAssembly()!);
        // Load the baked in XTMF2 modules
        LoadAssembly(typeof(SystemConfiguration).GetTypeInfo().Assembly);
        // Load the assemblies in the assembly's "Modules" directory.
        var libraryLocation = Path.GetDirectoryName(typeof(SystemConfiguration).GetTypeInfo().Assembly.Location);
        if (libraryLocation is not null)
        {
            var modulesDir = Path.Combine(libraryLocation, "Modules");
            if (Directory.Exists(modulesDir))
            {
                LoadAssemblies(modulesDir, Path.Combine(modulesDir, "exclude.txt"));
            }
        }
    }

    /// <summary>
    /// Common BCL/primitive types registered in the <see cref="TypeRepository"/> at
    /// startup so they are available in the GUI type-picker even if no loaded XTMF
    /// assembly happens to reference them directly.
    /// </summary>
    private static readonly Type[] s_builtInTypes =
    [
        // Primitives and their aliases
        typeof(bool),
        typeof(byte),
        typeof(sbyte),
        typeof(char),
        typeof(short),
        typeof(ushort),
        typeof(int),
        typeof(uint),
        typeof(long),
        typeof(ulong),
        typeof(float),
        typeof(double),
        typeof(decimal),
        // Core BCL types
        typeof(string),
        typeof(object),
        typeof(Guid),
        typeof(Uri),
        typeof(DateTime),
        typeof(DateOnly),
        typeof(TimeOnly),
        typeof(TimeSpan),
        typeof(DateTimeOffset),
    ];

    /// <summary>
    /// Adds every entry in <see cref="s_builtInTypes"/> to <see cref="Types"/> so that the
    /// GUI type-picker can offer them as context-type arguments for open-generic hooks.
    /// </summary>
    private void SeedBuiltInTypes()
    {
        foreach (var t in s_builtInTypes)
        {
            if (!Types.Contains(t))
            {
                string? error = null;
                Types.Add(t, ref error);
            }
        }
    }

    private void CreateDirectory(string directoryName)
    {
        DirectoryInfo dir = new DirectoryInfo(directoryName);
        if (!dir.Exists)
        {
            dir.Create();
        }
    }

    /// <summary>
    /// Load an assembly from the given path into the system's configuration.
    /// </summary>
    /// <param name="path">The path to the assembly to load.</param>
    public void LoadAssembly(string path)
    {
        var fullPath = Path.GetFullPath(path);
        LoadAssembly(AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath));
    }

    /// <summary>
    /// Load the assemblies from the given directory into the system's configuration.
    /// </summary>
    /// <param name="path">The directory to load the assemblies from.</param>
    /// <param name="exclusionFile">A text file containing the assemblies to not load.</param>
    public void LoadAssemblies(string path, string? exclusionFile = null)
    {
        var exludedDlls = exclusionFile is null || !File.Exists(exclusionFile) ? [] : File.ReadLines(exclusionFile).ToList();
        LoadAssemblies(path, exludedDlls);
    }

    /// <summary>
    /// Load the assemblies from the given directory into the system's configuration.
    /// </summary>
    /// <param name="path">The directory to load the assemblies from.</param>
    /// <param name="toExclude">A list of DLL files to exclude from loading.</param>
    public void LoadAssemblies(string path, List<string> toExclude)
    {
        var dirInfo = new DirectoryInfo(path);
        var toLoad = dirInfo.EnumerateFiles("*.dll").Where(file => !toExclude.Contains(file.Name)).ToList();
        foreach (var dllFile in toLoad)
        {
            if (!toExclude.Contains(dllFile.Name))
            {
                LoadAssembly(Assembly.LoadFrom(dllFile.FullName));
            }
        }
    }


    /// <summary>
    /// Load the types of the given assembly
    /// </summary>
    /// <param name="assembly">The assembly to load</param>
    public void LoadAssembly(Assembly assembly)
    {
        if (assembly == null)
        {
            throw new ArgumentNullException(nameof(assembly));
        }
        Parallel.ForEach(assembly.ExportedTypes, (Type t) =>
        {
            string? error = null;
            Modules.AddIfModuleType(t);
            Types.Add(t, ref error);
        });
    }
}

