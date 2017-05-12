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
using System.Linq;
using System.ComponentModel;
using XTMF2.Editing;
using System.IO;

namespace XTMF2
{
    public sealed class ModelSystem : INotifyPropertyChanged
    {
        public ModelSystem(string name)
        {
            Name = name;
        }

        private ModelSystem()
        {
            GlobalBoundary = new Boundary("global");
        }

        public string Name { get; private set; }
        public string Description { get; private set; }
        public Boundary GlobalBoundary { get; private set; }
        private object ModelSystemLock = new object();

        public event PropertyChangedEventHandler PropertyChanged;

        public ModelSystem Clone()
        {
            return new ModelSystem(Name)
            {
                Description = Description,
                GlobalBoundary = GlobalBoundary.Clone()
            };
        }

        internal bool SetName(ModelSystemSession session, string name, ref string error)
        {
            if (String.IsNullOrWhiteSpace(name))
            {
                error = "A name cannot be whitespace.";
                return false;
            }
            Name = name;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            return true;
        }

        internal bool SetDescription(ModelSystemSession session, string description, ref string error)
        {
            Description = description;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description)));
            return true;
        }

        internal bool Save(ref string error)
        {
            error = "Not implemented yet!";
            return false;
        }

        /// <summary>
        /// Check to see if the given boundary is within the model system.
        /// </summary>
        /// <param name="boundary"></param>
        /// <returns></returns>
        internal bool Contains(Boundary boundary)
        {
            lock(ModelSystemLock)
            {
                if(GlobalBoundary == boundary)
                {
                    return true;
                }
                return GlobalBoundary.Contains(boundary);
            }
        }

        internal static bool Load(ProjectSession session, ModelSystemHeader modelSystemHeader, out ModelSystemSession msSession, ref string error)
        {
            // the parameters are have already been vetted
            var path = modelSystemHeader.ModelSystemPath;
            var info = new FileInfo(path);
            if(info.Exists)
            {
                // load the existing model system
                throw new NotImplementedException();
            }
            else
            {
                // create a new model system
                msSession = new ModelSystemSession(session, new ModelSystem());
                return true;
            }
        }
    }
}
