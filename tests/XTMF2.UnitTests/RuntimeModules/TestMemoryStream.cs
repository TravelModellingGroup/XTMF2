/*
    Copyright 2019 University of Toronto

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
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using XTMF2.RuntimeModules;
using static XTMF2.UnitTests.TestHelper;

namespace XTMF2.UnitTests.RuntimeModules
{
    [TestClass]
    public class TestMemoryStream
    {
        [TestMethod]
        public void CreatingMemoryPipe()
        {
            using(var memory = new MemoryPipe())
            {
                for (int i = 0; i < 2; i++)
                {
                    using (var writeStream = memory.GetWriteStream(null))
                    using (var writer = new StreamWriter(writeStream))
                    {
                        writer.Write("Hello World");
                    }
                    // Getting another write stream should wipe the buffer
                    using (var writeStream = memory.GetWriteStream(null))
                    using (var writer = new StreamWriter(writeStream))
                    {
                        writer.Write("Hello2 World2");
                    }
                    using (var readStream = memory.GetReadStream(null))
                    using (var reader = new StreamReader(readStream))
                    {
                        Assert.AreEqual("Hello2 World2", reader.ReadLine());
                    }
                }
            }
        }

        [TestMethod]
        public void OpeningMemoryPipe()
        {
            using (MemoryPipe pipe = new MemoryPipe())
            {
                var dataOut = new OpenWriteStreamFromMemoryPipe()
                {
                    Name = "OpenWritePipe",
                    Pipe = CreateParameter(pipe, "Pipe")
                };
                var dataIn = new OpenReadStreamFromMemoryPipe()
                {
                    Name = "OpenWritePipe",
                    Pipe = CreateParameter(pipe, "Pipe")
                };
                using (var writer = new StreamWriter(dataOut.Invoke()))
                {
                    writer.Write("Hello World");
                }
                using (var reader = new StreamReader(dataIn.Invoke()))
                {
                    Assert.AreEqual("Hello World", reader.ReadLine());
                }
            }
        }
    }
}
