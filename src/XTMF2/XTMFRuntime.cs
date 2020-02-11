﻿/*
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
using System.Collections.ObjectModel;
using System.Linq;
using XTMF2.Bus;
using XTMF2.Configuration;
using XTMF2.Controllers;
using XTMF2.Editing;
using XTMF2.Repository;

namespace XTMF2
{
    /// <summary>
    /// Used to control XTMF
    /// </summary>
    public class XTMFRuntime
    {
        /// <summary>
        /// The configuration for the whole XTMF instance
        /// </summary>
        public SystemConfiguration SystemConfiguration { get; private set; }

        /// <summary>
        /// The types that belong to this instance of XTMF
        /// </summary>
        public TypeRepository Types => SystemConfiguration.Types;

        /// <summary>
        /// The modules available for model systems to this instance of XTMF
        /// </summary>
        public ModuleRepository Modules => SystemConfiguration.Modules;

        /// <summary>
        /// The currently executing ClientBus
        /// </summary>
        public ClientBus ClientBus { get; internal set; }

        /// <summary>
        /// The users in the system.  Ensure you dereference the
        /// observable interface if you share this with other objects.
        /// </summary>
        public UserController UserController { get; private set; }

        /// <summary>
        /// The controller for projects
        /// </summary>
        public ProjectController ProjectController { get; private set; }

        /// <summary>
        /// Create a new XTMF Runtime, shutting down any other that
        /// might have already been created.
        /// </summary>
        /// <param name="config">Optionally a custom configuration</param>
        /// <returns>A new XTMF Runtime</returns>
        public static XTMFRuntime CreateRuntime(SystemConfiguration config = null)
        {
            return new XTMFRuntime(config);
        }

        /// <summary>
        /// Create a new instance of XTMF
        /// </summary>
        /// <param name="config">An alternative configuration to load</param>
        private XTMFRuntime(SystemConfiguration config = null)
        {
            // if no configuration is given we need to load the default configuration
            SystemConfiguration = config ?? new SystemConfiguration(this);
            UserController = new UserController(this);
            // Projects need to be loaded after users are available.
            ProjectController = new ProjectController(this);
        }

        /// <summary>
        /// Release all resources consumed by XTMF
        /// </summary>
        public void Shutdown()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }
}
