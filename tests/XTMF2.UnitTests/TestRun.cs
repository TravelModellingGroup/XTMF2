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
using XTMF2.Controllers;
using System.Linq;
using XTMF2.ModelSystemConstruct;
using static XTMF2.Helper;
using TestXTMF.Modules;
using System.IO;
using System.Threading;
using XTMF2.RuntimeModules;
using static TestXTMF.TestHelper;

namespace TestXTMF
{
    [TestClass]
    public class TestRun
    {
        [TestMethod]
        public void CreatingClient()
        {
            RunInModelSystemContext("CreatingClient", (user, pSession, msSession) =>
            {
                TestHelper.CreateRunClient(true, (runBus) =>
                {
                    
                });
            });
        }

        [TestMethod]
        public void SendModelSystem()
        {
            RunInModelSystemContext("CreatingClient", (user, pSession, msSession) =>
            {
                CreateRunClient(true, (runBus) =>
                {
                    string error = null;
                    Assert.IsTrue(runBus.RunModelSystem(msSession, Path.Combine(Directory.GetCurrentDirectory(), "CreatingClient"), "Start", out var id, ref error), error);
                });
            });
        }

        [TestMethod]
        public void RunModelSystemToComplete()
        {
            RunInModelSystemContext("RunModelSystemToComplete", (user, pSession, msSession) =>
            {
                string error2 = null;
                var ms = msSession.ModelSystem;
                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", out Start start, ref error2), error2);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "AnIgnore", typeof(IgnoreResult<string>), out var ignoreMSS, ref error2), error2);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "MyFunction", typeof(SimpleTestModule), out var stm, ref error2), error2);
                Assert.IsTrue(msSession.AddLink(user, start, start.Hooks[0], ignoreMSS, out var ignoreLink1, ref error2), error2);
                Assert.IsTrue(msSession.AddLink(user, ignoreMSS, ignoreMSS.Hooks[0], stm, out var ignoreLink2, ref error2), error2);
                CreateRunClient(true, (runBus) =>
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
                            error = e + "\r\n" + stack;
                            sim.Release();
                        };
                        Assert.IsTrue(runBus.RunModelSystem(msSession, Path.Combine(pSession.RunsDirectory, "CreatingClient"), "Start", out var id, ref error), error);
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

        [TestMethod]
        public void ParameterModules()
        {
            RunInModelSystemContext("ParameterModules", (user, pSession, msSession) =>
            {
                string error2 = null;
                var ms = msSession.ModelSystem;
                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", out Start start, ref error2), error2);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "AnIgnore", typeof(IgnoreResult<string>), out var ignoreMSS, ref error2), error2);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "SPM", typeof(SimpleParameterModule), out var spm, ref error2), error2);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "MyParameter", typeof(BasicParameter<string>), out var basicParameter, ref error2), error2);
                Assert.IsTrue(msSession.SetParameterValue(user, basicParameter, "Hello World Parameter", ref error2), error2);
                Assert.IsTrue(msSession.AddLink(user, start, start.Hooks[0], ignoreMSS, out var ignoreLink1, ref error2), error2);
                Assert.IsTrue(msSession.AddLink(user, ignoreMSS, ignoreMSS.Hooks[0], spm, out var ignoreLink2, ref error2), error2);
                Assert.IsTrue(msSession.AddLink(user, spm, spm.Hooks[0], basicParameter, out var ignoreLink3, ref error2), error2);
                CreateRunClient(true, (runBus) =>
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
                            error = e + "\r\n" + stack;
                            sim.Release();
                        };
                        Assert.IsTrue(runBus.RunModelSystem(msSession, Path.Combine(pSession.RunsDirectory, "CreatingClient"), "Start", out var id, ref error), error);
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

        [TestMethod]
        public void RunWithMultiLink()
        {
            RunInModelSystemContext("ParameterModules", (user, pSession, msSession) =>
            {
                string error = null;
                var ms = msSession.ModelSystem;
                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "FirstStart", out var start, ref error), error);

                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Execute", typeof(Execute), out var mss, ref error));
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Ignore1", typeof(IgnoreResult<string>), out var ignore1, ref error));
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Ignore2", typeof(IgnoreResult<string>), out var ignore2, ref error));
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Ignore3", typeof(IgnoreResult<string>), out var ignore3, ref error));
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Hello World", typeof(SimpleTestModule), out var hello, ref error));


                Assert.IsTrue(msSession.AddLink(user, start, GetHook(start.Hooks, "ToExecute"), mss, out var link, ref error), error);
                Assert.IsTrue(msSession.AddLink(user, mss, GetHook(mss.Hooks, "To Execute"), ignore1, out var link1, ref error), error);
                Assert.IsTrue(msSession.AddLink(user, mss, GetHook(mss.Hooks, "To Execute"), ignore2, out var link2, ref error), error);
                Assert.IsTrue(msSession.AddLink(user, mss, GetHook(mss.Hooks, "To Execute"), ignore3, out var link3, ref error), error);

                Assert.AreNotSame(link, link1);
                Assert.AreSame(link1, link2);
                Assert.AreSame(link1, link3);

                Assert.IsTrue(msSession.AddLink(user, ignore1, GetHook(ignore1.Hooks, "To Ignore"), hello, out var toSame1, ref error), error);
                Assert.IsTrue(msSession.AddLink(user, ignore2, GetHook(ignore2.Hooks, "To Ignore"), hello, out var toSame2, ref error), error);
                Assert.IsTrue(msSession.AddLink(user, ignore3, GetHook(ignore3.Hooks, "To Ignore"), hello, out var toSame3, ref error), error);
                CreateRunClient(true, (runBus) =>
                {
                    string error2 = null;
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
                            error2 = e + "\r\n" + stack;
                            sim.Release();
                        };
                        Assert.IsTrue(runBus.RunModelSystem(msSession, Path.Combine(pSession.RunsDirectory, "CreatingClient"), "FirstStart", out var id, ref error2), error2);
                        // give the models system some time to complete
                        if (!sim.Wait(2000))
                        {
                            Assert.Fail("The model system failed to execute in time!");
                        }
                        Assert.IsTrue(success, "The model system failed to execute to success! " + error2);
                    }
                });
            });
        }

        [TestMethod]
        public void ReportRunProgress()
        {
            RunInModelSystemContext("RunModelSystemToComplete", (user, pSession, msSession) =>
            {
                string error2 = null;
                var ms = msSession.ModelSystem;
                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", out Start start, ref error2), error2);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "AnIgnore", typeof(IgnoreResult<string>), out var ignoreMSS, ref error2), error2);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "AnIgnore", typeof(ReportFunctionInvocation<string>), out var report, ref error2), error2);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Message", typeof(BasicParameter<string>), out var message, ref error2), error2);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "MyFunction", typeof(SimpleTestModule), out var stm, ref error2), error2);
                Assert.IsTrue(msSession.AddLink(user, start, start.Hooks[0], ignoreMSS, out var ignoreLink1, ref error2), error2);
                Assert.IsTrue(msSession.AddLink(user, ignoreMSS, ignoreMSS.Hooks[0], report, out var ignoreLink2, ref error2), error2);
                Assert.IsTrue(msSession.AddLink(user, report, GetHook(report.Hooks, "To Invoke"), stm, out var ignoreLink3, ref error2), error2);
                Assert.IsTrue(msSession.AddLink(user, report, GetHook(report.Hooks, "Message"), message, out var ignoreLink4, ref error2), error2);
                Assert.IsTrue(msSession.SetParameterValue(user, message, "Reporting through XTMF", ref error2), error2);
                CreateRunClient(true, (runBus) =>
                {
                    string error = null;
                    bool success = false;
                    string reportedStatus = null;
                    using (SemaphoreSlim sim = new SemaphoreSlim(0))
                    {
                        runBus.ClientFinishedModelSystem += (sender, e) =>
                        {
                            success = true;
                            sim.Release();
                        };
                        runBus.ClientErrorWhenRunningModelSystem += (sender, runId, e, stack) =>
                        {
                            error = e + "\r\n" + stack;
                            sim.Release();
                        };
                        runBus.ClientReportedStatus += (sender, runId, status) =>
                        {
                            reportedStatus = status;
                        };
                        Assert.IsTrue(runBus.RunModelSystem(msSession, Path.Combine(pSession.RunsDirectory, "CreatingClient"), "Start", out var id, ref error), error);
                        // give the models system some time to complete
                        if (!sim.Wait(2000))
                        {
                            Assert.Fail("The model system failed to execute in time!");
                        }
                        Assert.IsNotNull(reportedStatus);
                        Assert.AreEqual("Reporting through XTMF", reportedStatus);
                        Assert.IsTrue(success, "The model system failed to execute to success! " + error);
                    }
                });
            });
        }
    }
}
