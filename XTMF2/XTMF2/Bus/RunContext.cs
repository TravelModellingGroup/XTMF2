/*
    Copyright 2017 Travel Modelling Group, Department of Civil Engineering, University of Toronto

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
    along with XTMF.  If not, see <http://www.gnu.org/licenses/>.
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using XTMF2.ModelSystemConstruct;

namespace XTMF2.Bus
{
    public sealed class RunContext
    {

        private string ModelSystemAsString;
        private readonly string CurrentWorkingDirectory;
        public bool HasExecuted { get; private set; }
        public string ID { get; private set; }
        public string StartToExecute { get; private set; }

        private ModelSystem ModelSystem;
        private XTMFRuntime Runtime;

        public RunContext(XTMFRuntime runtime, string id, string modelSystem, string cwd, string start)
        {
            Runtime = runtime;
            ID = id;
            ModelSystemAsString = modelSystem;
            CurrentWorkingDirectory = cwd;
            HasExecuted = false;
            StartToExecute = start;
        }

        public static bool CreateRunContext(XTMFRuntime runtime, string id, byte[] modelSystem, string cwd, 
            string start, out RunContext context)
        {
            if(!Convert(modelSystem, out string modelSystemAsString))
            {
                context = null;
                return false;
            }
            context = new RunContext(runtime, id, modelSystemAsString, cwd, start);
            return true;
        }

        private static bool Convert(byte[] rawData, out string modelSystemAsString)
        {
            modelSystemAsString = Encoding.Unicode.GetString(rawData);
            return !String.IsNullOrWhiteSpace(modelSystemAsString);
        }

        internal bool ValidateModelSystem(ref string error)
        {
            // Construct the model system
            if (!XTMF2.ModelSystem.Load(ModelSystemAsString, Runtime, out ModelSystem ms, ref error)
                || !ms.Construct(ref error))
            {
                return false;
            }
            // Ensure that the starting point exists
            ModelSystem = ms;
           
            if(!GetStart(Start.ParseStartString(StartToExecute),
                out var s, ref error))
            {
                ModelSystem = null;
                return false;
            }
            // if everything is fine store the constructed model system
            
            return true;
        }

        private bool GetStart(List<string> startPath, out Start start, ref string error)
        {
            start = null;
            if (startPath.Count == 0)
            {
                error = "No start path was defined!";
                return false;
            }
            // get the boundary the start should be contained within.
            Boundary current = ModelSystem.GlobalBoundary;
            for (int i = 0; i < startPath.Count - 1; i++)
            {
                bool found = false;
                foreach(var child in current.Boundaries)
                {
                    if(child.Name.Equals(startPath[i], StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        current = child;
                    }
                }
                if(!found)
                {
                    error = $"Unable to find a child boundary named {startPath[i]} in parent boundary {current.Name}!";
                    return false;
                }
            }
            var startName = startPath[startPath.Count - 1];
            foreach(var s in current.Starts)
            {
                if(startName.Equals(s.Name, StringComparison.OrdinalIgnoreCase))
                {
                    start = s;
                    return true;
                }
            }
            error = $"Unable to find {startName} within boundary = {current.FullPath}.";
            return false;
        }

        internal bool Run(ref string error, ref string stackTrace)
        {
            if(!GetStart(Start.ParseStartString(StartToExecute), out var startingMss, ref error))
            {
                return false;
            }
            var originalDir = Directory.GetCurrentDirectory();
            try
            {
                Directory.CreateDirectory(CurrentWorkingDirectory);
                Directory.SetCurrentDirectory(CurrentWorkingDirectory);
                ((IAction)startingMss.Module).Invoke();
            }
            catch(Exception e)
            {
                error = e.Message;
                stackTrace = e.StackTrace;
                return false;
            }
            finally
            {
                Directory.SetCurrentDirectory(originalDir);
            }
            // success for now
            return true;
        }
    }
}
