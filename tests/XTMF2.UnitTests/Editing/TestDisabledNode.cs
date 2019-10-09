/*
    Copyright 2018 University of Toronto

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
using System.Threading;

namespace TestXTMF.Editing
{
    [TestClass]
    public class TestDisabledNode
    {
        [TestMethod]
        public void TestDisablingNode()
        {
            RunInModelSystemContext("TestDisablingNode", (user, pSession, mSession) =>
            {
                // initialization
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "MyMSS", typeof(SimpleTestModule), out var mss, ref error));
                Assert.AreEqual("MyMSS", mss.Name);
                Assert.IsFalse(mss.IsDisabled, "The node started out as disabled!");
                Assert.IsTrue(mSession.SetNodeDisabled(user, mss, true, ref error), error);
                Assert.IsTrue(mss.IsDisabled, "The node was not disabled!");
                Assert.IsTrue(mSession.Undo(user, ref error), error);
                Assert.IsFalse(mss.IsDisabled, "The node was not re-enabled when undoing the disable instruction!");
                Assert.IsTrue(mSession.Redo(user, ref error), error);
                Assert.IsTrue(mss.IsDisabled, "The node was not disabled during the redo!");
            }, (user, pSession, mSession) =>
            {
                // after shutdown
                var ms = mSession.ModelSystem;
                var modules = ms.GlobalBoundary.Modules;
                Assert.AreEqual(1, modules.Count);
                Assert.IsTrue(modules[0].IsDisabled, "The module was not disabled after reloading the model system!");
            });
        }

        [TestMethod]
        public void TestDisabledNodeRunValidationFailure()
        {
            RunInModelSystemContext("TestDisabledNodeRunValidationFailure", (user, pSession, msSession) =>
            {
                string error2 = null;
                var ms = msSession.ModelSystem;
                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", out Start start, ref error2), error2);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "AnIgnore", typeof(IgnoreResult<string>), out var ignoreMSS, ref error2), error2);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "SPM", typeof(SimpleParameterModule), out var spm, ref error2), error2);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "MyParameter", typeof(BasicParameter<string>), out var basicParameter, ref error2), error2);
                Assert.IsTrue(msSession.SetNodeDisabled(user, basicParameter, true, ref error2), error2);
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
                        Assert.IsFalse(success, "The model system finished running instead of having a validation error!");
                    }
                });
            });
        }

        [TestMethod]
        public void TestDisableLink()
        {
            RunInModelSystemContext("TestDisableLink", (user, pSession, msSession) =>
            {
                // initialization
                var ms = msSession.ModelSystem;
                string error = null;
                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", out Start start, ref error), error);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "AnIgnore", typeof(IgnoreResult<string>), out var ignoreMSS, ref error), error);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "SPM", typeof(SimpleParameterModule), out var spm, ref error), error);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "MyParameter", typeof(BasicParameter<string>), out var basicParameter, ref error), error);
                Assert.IsTrue(msSession.AddLink(user, start, start.Hooks[0], ignoreMSS, out var startLink, ref error), error);

                Assert.IsFalse(startLink.IsDisabled, "The link initialized as disabled!");
                Assert.IsTrue(msSession.SetLinkDisabled(user, startLink, true, ref error), error);
                Assert.IsTrue(startLink.IsDisabled, "The link initialized as disabled!");
                Assert.IsTrue(msSession.Undo(user, ref error), error);
                Assert.IsFalse(startLink.IsDisabled);
                Assert.IsTrue(msSession.Redo(user, ref error), error);
                Assert.IsTrue(startLink.IsDisabled);
            }, (user, pSession, mSession) =>
            {
                // after shutdown
                var ms = mSession.ModelSystem;
                var modules = ms.GlobalBoundary.Modules;
                var links = ms.GlobalBoundary.Links;
                Assert.AreEqual(3, modules.Count);
                Assert.AreEqual(1, links.Count);
                Assert.IsTrue(links[0].IsDisabled, "The link was not disabled on reload.");
            });
        }

        [TestMethod]
        public void TestDisabledLinkRunValidationFailure()
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
                Assert.IsTrue(msSession.AddLink(user, start, start.Hooks[0], ignoreMSS, out var ignoreLink, ref error2), error2);
                Assert.IsTrue(msSession.AddLink(user, ignoreMSS, ignoreMSS.Hooks[0], spm, out var requiredLink, ref error2), error2);
                Assert.IsTrue(msSession.AddLink(user, spm, spm.Hooks[0], basicParameter, out var ignoreLink3, ref error2), error2);
                Assert.IsTrue(msSession.SetLinkDisabled(user, requiredLink, true, ref error2), error2);
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
                        Assert.IsFalse(success, "The model system finished running instead of having a validation error!");
                    }
                });
            });
        }
    }
}
