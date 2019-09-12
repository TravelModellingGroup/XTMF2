﻿/*
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
using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2;
using XTMF2.Editing;
using XTMF2.Controllers;
using System.Linq;
using XTMF2.ModelSystemConstruct;
using static XTMF2.Helper;
using TestXTMF.Modules;
using XTMF2.RuntimeModules;
using static TestXTMF.TestHelper;
using System.IO;

namespace TestXTMF.RuntimeModules
{
    [TestClass]
    public class TestFileStreams
    {
        [TestMethod]
        public void FileExistsAndTestIfExists()
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
        public void FileExistsAndDontTestIfExists()
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
        public void FileDoesNotExistAndTestIfExists()
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
        public void FileDoesNotExistAndDontTestIfExists()
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
    }
}
