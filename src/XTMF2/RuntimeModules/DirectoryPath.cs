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
using System.Collections.Generic;
using System.Text;

namespace XTMF2.RuntimeModules
{
    [Module(Name = "Directory Path", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
Description = "Provides the ability to specify a directory path recursively.")]
    public sealed class DirectoryPath : BaseFunction<string>
    {
        [SubModule(Required = false, Name = "Parent", Description = "Optional parent directory", Index = 0)]
        public DirectoryPath? Parent;

        [Parameter(Name = "Name", DefaultValue = "directoryName", Description = "The path to add to the Parent path", Index = 1)]
        public IFunction<string>? Path;

        public override string Invoke()
        {
            if(Parent != null)
            {
                return System.IO.Path.Combine(Parent.Invoke(), Path!.Invoke());
            }
            return Path!.Invoke();
        }
    }
}
