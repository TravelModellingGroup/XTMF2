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
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2;

namespace TestXTMF
{
    [TestClass]
    public class TestXTMFRuntime
    {
        [TestInitialize]
        public void Setup()
        {
            // hide the startup cost of XTMF
            XTMFRuntime runtime = XTMFRuntime.Reference;
        }

        [TestMethod]
        public void CreateRuntime()
        {
            XTMFRuntime runtime = XTMFRuntime.Reference;
        }

        [TestMethod]
        public void GetUserData()
        {
            XTMFRuntime runtime = XTMFRuntime.Reference;
            var users = runtime.SystemConfiguration.Users;
            Assert.IsTrue(users.Count > 0);
        }

        [TestMethod]
        public void GetProjectController()
        {
            XTMFRuntime runtime = XTMFRuntime.Reference;
            var controller = runtime.ProjectController;
            Assert.IsNotNull(controller);
        }
    }
}
