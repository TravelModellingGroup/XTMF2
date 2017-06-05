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
using System.IO;
using System.Text;

namespace XTMF2.RuntimeModules
{
    [Module(Name = "Open Write Stream From File", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
Description = "Provides a WriteStream to the given file name from context.")]
    public class OpenWriteStreamFromFile : BaseFunction<string, WriteStream>
    {
        public override WriteStream Invoke(string context)
        {
            FileInfo f = new FileInfo(context);
            var dir = f.Directory;
            if(!dir.Exists)
            {
                dir.Create();
            }
            return new WriteStream(File.OpenWrite(context));
        }
    }
}
