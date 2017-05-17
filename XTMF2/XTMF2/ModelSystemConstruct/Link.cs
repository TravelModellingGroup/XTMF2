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
using System.ComponentModel;
using System.Text;
using XTMF2.Editing;

namespace XTMF2
{
    /// <summary>
    /// Defines a directional connection between two model system structures
    /// </summary>
    public sealed class Link : INotifyPropertyChanged
    {
        public ModelSystemStructure Origin { get; private set; }
        public ModelSystemStructure Destination { get; private set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public bool SetOrigin(ModelSystemSession session, ModelSystemStructure origin, ref string error)
        {
            Origin = origin;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Origin)));
            return true;
        }

        public bool SetDestination(ModelSystemSession session, ModelSystemStructure destination, ref string error)
        {
            Destination = destination;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Destination)));
            return true;
        }

        public Link Clone()
        {
            return new Link()
            {
                Origin = Origin,
                Destination = Destination
            };
        }
    }
}
