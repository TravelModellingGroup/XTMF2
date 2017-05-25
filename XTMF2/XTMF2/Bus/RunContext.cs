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

namespace XTMF2.Bus
{
    public sealed class RunContext
    {

        private string ModelSystemAsString;
        private readonly object CurrentWorkingDirectory;
        public bool HasExecuted { get; private set; }
        public string ID { get; private set; }
        public string StartToExecute { get; private set; }

        private ModelSystem ModelSystem;

        public RunContext(string id, string modelSystem, string cwd, string start)
        {
            ID = id;
            ModelSystemAsString = modelSystem;
            CurrentWorkingDirectory = cwd;
            HasExecuted = false;
            StartToExecute = start;
        }

        public static bool CreateRunContext(string id, byte[] modelSystem, string cwd, string start, out RunContext context)
        {
            if(!Convert(modelSystem, out string modelSystemAsString))
            {
                context = null;
                return false;
            }
            context = new RunContext(id, modelSystemAsString, cwd, start);
            return true;
        }

        private static bool Convert(byte[] rawData, out string modelSystemAsString)
        {
            modelSystemAsString = Encoding.Unicode.GetString(rawData);
            return !String.IsNullOrWhiteSpace(modelSystemAsString);
        }

        internal bool ValidateModelSystem(ref string error)
        {
            if(!XTMF2.ModelSystem.Load(ModelSystemAsString, out ModelSystem, ref error)
                || !ModelSystem.Construct(ref error))
            {
                return false;
            }
            return true;
        }

        internal bool Run(ref string error, ref string stackTrace)
        {
            // success for now
            return true;
        }
    }
}
