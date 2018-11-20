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
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace XTMF2.RuntimeModules
{
    [Module(Name = "Open Read Stream From File", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
Description = "Provides the ability to read a file from the path given to it via the context.")]
    public class OpenReadStreamFromFile : BaseFunction<string, ReadStream>
    {
        public override ReadStream Invoke(string context)
        {
            return new ReadStream(File.OpenRead(context));
        }
    }
}
