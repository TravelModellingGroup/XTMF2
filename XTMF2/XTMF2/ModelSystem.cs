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

namespace XTMF2
{
    public sealed class ModelSystem
    {
        public string Name { get; private set; }
        public string Description { get; private set; }
        private ObservableCollection<ModelSystemStructure> _Modules = new ObservableCollection<ModelSystemStructure>();

        public ReadOnlyObservableCollection<ModelSystemStructure> Modules => new ReadOnlyObservableCollection<ModelSystemStructure>(_Modules);

        public bool Save(ref string error)
        {
            error = "Not implemented yet!";
            return false;
        }
    }
}
