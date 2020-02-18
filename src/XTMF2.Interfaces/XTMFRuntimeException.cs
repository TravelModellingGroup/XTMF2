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

namespace XTMF2
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1032:Implement standard exception constructors",
        Justification = "This point of this error is to include the offending module, supplying alternatives that ignore " +
        "the module parameter could lead to bad client code.")]
    public sealed class XTMFRuntimeException : Exception
    {

        public IModule? FailingModule { get; private set; }

        public XTMFRuntimeException(IModule? module, string message)
            :base(message)
        {
            FailingModule = module;
        }

        public XTMFRuntimeException(IModule? module, string message, Exception innerException) : base(message, innerException)
        {
            FailingModule = module;
        }
    }
}