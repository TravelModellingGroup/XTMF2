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
using static XTMF2.TestHelper;

namespace XTMF2.RuntimeModules
{
    [TestClass]
    public class TestFileStreams
    {
        [TestMethod]
        public void ReadFileExistsAndTestIfExists()
        {
            CreateTemporaryFile((fileName) =>
            {
                OpenReadStreamFromFile streamFromFile = new OpenReadStreamFromFile()
                {
                    Name = "Reader",
                    FilePath = CreateParameter(fileName, "FileName"),
                    CheckFileExistsAtRunStart = CreateParameter(true, "Check")
                };
                string error = null;
                Assert.IsTrue(streamFromFile.RuntimeValidation(ref error));
                Assert.IsNull(error);
                // make sure that we can read the file
                Assert.IsTrue(NoExecutionErrors(() =>
                {
                    using (var reader = streamFromFile.Invoke())
                    {

                    }
                }, out var e), e?.Message);
            });
        }

        [TestMethod]
        public void ReadFileExistsAndDontTestIfExists()
        {
            CreateTemporaryFile((fileName) =>
            {
                OpenReadStreamFromFile streamFromFile = new OpenReadStreamFromFile()
                {
                    Name = "Reader",
                    FilePath = CreateParameter(fileName, "FileName"),
                    CheckFileExistsAtRunStart = CreateParameter(false, "Check")
                };
                string error = null;
                Assert.IsTrue(streamFromFile.RuntimeValidation(ref error));
                Assert.IsNull(error);
                // make sure that we can read the file
                Assert.IsTrue(NoExecutionErrors(() =>
                {
                    using (var reader = streamFromFile.Invoke())
                    {

                    }
                }, out var e), e?.Message);
            });
        }

        [TestMethod]
        public void ReadFileDoesNotExistAndTestIfExists()
        {
            CreateTemporaryFile((fileName) =>
            {
                // Make sure the file does not exist.
                File.Delete(fileName);

                OpenReadStreamFromFile streamFromFile = new OpenReadStreamFromFile()
                {
                    Name = "Reader",
                    FilePath = CreateParameter(fileName, "FileName"),
                    CheckFileExistsAtRunStart = CreateParameter(true, "Check")
                };
                string error = null;
                Assert.IsFalse(streamFromFile.RuntimeValidation(ref error));
                Assert.IsNotNull(error, "There was no error message!");
                // make sure that we can read the file
                Assert.IsFalse(NoExecutionErrors(() =>
                {
                    using (var reader = streamFromFile.Invoke())
                    {

                    }
                }, out var _), "There was no error when reading a file that does not exist!");
            });
        }

        [TestMethod]
        public void ReadFileDoesNotExistAndDontTestIfExists()
        {
            CreateTemporaryFile((fileName) =>
            {
                // Make sure the file does not exist.
                File.Delete(fileName);

                OpenReadStreamFromFile streamFromFile = new OpenReadStreamFromFile()
                {
                    Name = "Reader",
                    FilePath = CreateParameter(fileName, "FileName"),
                    CheckFileExistsAtRunStart = CreateParameter(false, "Check")
                };
                string error = null;
                Assert.IsTrue(streamFromFile.RuntimeValidation(ref error));
                Assert.IsNull(error);
                // make sure that we can read the file
                Assert.IsFalse(NoExecutionErrors(() =>
                {
                    using (var reader = streamFromFile.Invoke())
                    {

                    }
                }, out var _), "There was no error when reading a file that does not exist!");
            });
        }

        [TestMethod]
        public void WriteFileWithValidPath()
        {
            CreateTemporaryFile((fileName) =>
            {
                OpenWriteStreamFromFile streamFromFile = new OpenWriteStreamFromFile()
                {
                    Name = "Writer",
                    FilePath = CreateParameter(fileName, "FileName")
                };
                string error = null;
                Assert.IsTrue(streamFromFile.RuntimeValidation(ref error));
                Assert.IsNull(error);
                Assert.IsTrue(NoExecutionErrors(() =>
                {
                    using (var writer = streamFromFile.Invoke())
                    {

                    }
                }, out var e), e?.Message);
            });
        }

        [TestMethod]
        public void WriteFileWithInvalidPath()
        {
            var fileName = "ASDF??://";
            OpenWriteStreamFromFile streamFromFile = new OpenWriteStreamFromFile()
            {
                Name = "Writer",
                FilePath = CreateParameter(fileName, "FileName")
            };
            string error = null;
            Assert.IsTrue(streamFromFile.RuntimeValidation(ref error));
            Assert.IsNull(error);
            Assert.IsFalse(NoExecutionErrors(() =>
            {
                using (var writer = streamFromFile.Invoke())
                {

                }
            }, out var e), "There was no error when trying to write to an invalid path!");
        }

        [TestMethod]
        public void WriteFileWithEmptyPath()
        {
            var fileName = " ";
            OpenWriteStreamFromFile streamFromFile = new OpenWriteStreamFromFile()
            {
                Name = "Writer",
                FilePath = CreateParameter(fileName, "FileName")
            };
            string error = null;
            Assert.IsTrue(streamFromFile.RuntimeValidation(ref error));
            Assert.IsNull(error);
            Assert.IsFalse(NoExecutionErrors(() =>
            {
                using (var writer = streamFromFile.Invoke())
                {

                }
            }, out var e), "There was no error when trying to write to an invalid path!");
            Assert.IsInstanceOfType(e, typeof(XTMFRuntimeException));
        }
    }
}
