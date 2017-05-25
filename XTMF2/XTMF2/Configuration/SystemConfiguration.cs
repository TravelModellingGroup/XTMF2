/*
    Copyright 2017 University of Toronto

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
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using XTMF2.Editing;
using XTMF2.Repository;
using System.Runtime.Loader;

namespace XTMF2.Configuration
{
    public class SystemConfiguration
    {
        public ModuleRepository Modules { get; private set; }
        public TypeRepository Types { get; private set; }
        public string DefaultUserDirectory { get; private set; }

        public SystemConfiguration(XTMFRuntime runtime, string fullPath = null)
        {
            CreateDirectory(DefaultUserDirectory = Path.Combine(Environment.ExpandEnvironmentVariables("%HOMEDRIVE%%HOMEPATH%"), "XTMF", "Users"));
            LoadTypes();
        }

        private void CreateDirectory(string directoryName)
        {
            DirectoryInfo dir = new DirectoryInfo(directoryName);
            if (!dir.Exists)
            {
                dir.Create();
            }
        }

        public void LoadAssembly(string path)
        {
            var fullPath = Path.GetFullPath(path);
            LoadAssembly(AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath));
        }

        private void LoadTypes()
        {
            Modules = new ModuleRepository();
            Types = new TypeRepository();
            // Load the entry assembly for types
            LoadAssembly(Assembly.GetEntryAssembly());
            // Load the baked in XTMF2 modules
            LoadAssembly(typeof(SystemConfiguration).GetTypeInfo().Assembly);
        }

        private void LoadAssembly(Assembly assembly)
        {
            Parallel.ForEach(assembly.ExportedTypes, (Type t) =>
            {
                string error = null;
                Modules.AddIfModuleType(t);
                Types.Add(t, ref error);
            });
        }
    }
}
