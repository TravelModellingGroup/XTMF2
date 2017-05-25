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
using XTMF2.Editing;
using XTMF2.Controller;
using System.Linq;
using XTMF2.ModelSystemConstruct;
using static XTMF2.Helper;
using TestXTMF.Modules;
using System.IO;
using System.Threading;

namespace TestXTMF
{
    [TestClass]
    public class TestRun
    {
        [TestMethod]
        public void CreatingClient()
        {
            TestHelper.RunInModelSystemContext("CreatingClient", (user, pSession, msSession) =>
            {
                TestHelper.CreateRunClient(true, (runBus) =>
                {
                    
                });
            });
        }

        [TestMethod]
        public void SendModelSystem()
        {
            TestHelper.RunInModelSystemContext("CreatingClient", (user, pSession, msSession) =>
            {
                TestHelper.CreateRunClient(true, (runBus) =>
                {
                    string error = null;
                    Assert.IsTrue(runBus.RunModelSystem(msSession, Path.Combine(Directory.GetCurrentDirectory(), "CreatingClient"), "Start", out var id, ref error), error);
                });
            });
        }

        [TestMethod]
        public void RunModelSystemToComplete()
        {
            TestHelper.RunInModelSystemContext("RunModelSystemToComplete", (user, pSession, msSession) =>
            {
                TestHelper.CreateRunClient(true, (runBus) =>
                {
                    string error = null;
                    bool success = false;
                    using (SemaphoreSlim sim = new SemaphoreSlim(0))
                    {
                        runBus.ClientFinishedModelSystem += (sender, e) =>
                        {
                            success = true;
                            sim.Release();
                        };
                        runBus.ClientErrorWhenRunningModelSystem += (sender, runId, e, stack) =>
                        {
                            error = e;
                            sim.Release();
                        };
                        Assert.IsTrue(runBus.RunModelSystem(msSession, Path.Combine(Directory.GetCurrentDirectory(), "CreatingClient"), "Start", out var id, ref error), error);
                        // give the models system some time to complete
                        if (!sim.Wait(2000))
                        {
                            Assert.Fail("The model system failed to execute in time!");
                        }
                        Assert.IsTrue(success, "The model system failed to execute to success! " + error);
                    }
                });
            });
        }
    }
}
