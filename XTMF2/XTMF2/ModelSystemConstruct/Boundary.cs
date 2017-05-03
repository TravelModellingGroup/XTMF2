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
using System.ComponentModel;
using System.Linq;
using System.Text;
using XTMF2.Editing;

namespace XTMF2
{
    public sealed class Boundary : INotifyPropertyChanged
    {
        public string Name { get; private set; }
        public string Description { get; private set; }
        private object WriteLock = new object();
        private ObservableCollection<ModelSystemStructure> _Modules = new ObservableCollection<ModelSystemStructure>();
        private ObservableCollection<Boundary> _Boundaries = new ObservableCollection<Boundary>();

        public event PropertyChangedEventHandler PropertyChanged;

        public ReadOnlyObservableCollection<ModelSystemStructure> Modules
        {
            get
            {
                lock (WriteLock)
                {
                    return new ReadOnlyObservableCollection<ModelSystemStructure>(_Modules);
                }
            }
        }

        public ReadOnlyObservableCollection<Boundary> Boundaries
        {
            get
            {
                lock (WriteLock)
                {
                    return new ReadOnlyObservableCollection<Boundary>(_Boundaries);
                }
            }
        }

        public bool SetName(ModelSystemSession session, string name, ref string error)
        {
            if(String.IsNullOrWhiteSpace(name))
            {
                error = "A name cannot be whitespace.";
                return false;
            }
            Name = name;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            return true;
        }

        public bool SetDescription(ModelSystemSession session, string description, ref string error)
        {
            Description = description;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description)));
            return true;
        }

        public Boundary Clone()
        {
            lock (WriteLock)
            {
                var ret = new Boundary()
                {
                    _Modules = new ObservableCollection<ModelSystemStructure>(from mod in _Modules
                                                                              select mod.Clone()),
                    _Boundaries = new ObservableCollection<Boundary>(from bound in _Boundaries
                                                                     select bound.Clone())
                };
                return ret;
            }
        }
    }
}
