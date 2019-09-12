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
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace XTMF2.RuntimeModules
{
    [Module(Name = "Open Read Stream From File", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
Description = "Provides the ability to read a file from the path given to it via the context.")]
    public class OpenReadStreamFromFile : BaseFunction<ReadStream>
    {
        [Parameter(DefaultValue = "true", Description = "True if the file should be checked at runtime to ensure that it exists.", Index=1,
            Name="Check File Exists At Run Start", Required = true)]
        public IFunction<bool> CheckFileExistsAtRunStart;

        [Parameter(DefaultValue = "", Description = "The path to the file to load.", Index = 0,
            Name = "File Path", Required = true)]
        public IFunction<string> FilePath;

        public override ReadStream Invoke()
        {
            try
            {
                return new ReadStream(File.OpenRead(FilePath.Invoke()));
            }
            catch(IOException e)
            {
                throw new XTMFRuntimeException(this, e.Message, e);
            }
        }

        public override bool RuntimeValidation(ref string error)
        {
            if(CheckFileExistsAtRunStart?.Invoke() == true)
            {
                var filePath = FilePath.Invoke();
                if (!File.Exists(filePath))
                {
                    error = $"The file '{filePath}' does not exist!";
                    return false;
                }
            }
            return true;
        }
    }
}
