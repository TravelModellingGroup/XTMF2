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
using System.Text;
using System.Threading;
using XTMF2.Editing;

namespace XTMF2.Editing
{
    public sealed class ProjectSession : IDisposable
    {
        public Project Project { get; private set; }

        private XTMFRuntime Runtime;

        private CommandBuffer Commands = new CommandBuffer();

        private int _References = 1;

        public int References => _References;

        public ProjectSession(XTMFRuntime runtime, Project project)
        {
            Project = project;
            Runtime = runtime;
        }

        public void Dispose()
        {
            var left = Interlocked.Decrement(ref _References);
            if(left <= 0)
            {
                Runtime.ProjectController.UnloadSession(this);
            }
        }

        internal void IncrementCounter()
        {
            Interlocked.Increment(ref _References);
        }
    }
}
