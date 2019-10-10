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
using System.Linq;
using System.Threading;
using TestXTMF;
using TestXTMF.Modules;
using XTMF2.ModelSystemConstruct;
using XTMF2.RuntimeModules;

namespace XTMF2.Editing
{
    [TestClass]
    public class TestLinks
    {
        [TestMethod]
        public void UndoAddLink()
        {
            TestHelper.RunInModelSystemContext("UndoAddLink", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.AreEqual(0, ms.GlobalBoundary.Modules.Count);
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Start", typeof(BasicParameter<string>),
                    out var parameter, ref error), error);
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Start", typeof(SimpleParameterModule),
                    out var module, ref error), error);
                Assert.AreEqual(2, ms.GlobalBoundary.Modules.Count);
                Assert.AreEqual(0, ms.GlobalBoundary.Links.Count);
                Assert.IsTrue(mSession.AddLink(user, module, module.Hooks[0], parameter, out var link, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Links.Count);
                Assert.IsTrue(mSession.Undo(user, ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Links.Count);
                Assert.IsTrue(mSession.Redo(user, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Links.Count);
                Assert.AreSame(link, ms.GlobalBoundary.Links[0]);
            });
        }

        [TestMethod]
        public void RemoveLink()
        {
            TestHelper.RunInModelSystemContext("RemoveLink", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.AreEqual(0, ms.GlobalBoundary.Modules.Count);
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Start", typeof(BasicParameter<string>),
                    out var parameter, ref error), error);
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Start", typeof(SimpleParameterModule),
                    out var module, ref error), error);
                Assert.AreEqual(2, ms.GlobalBoundary.Modules.Count);
                Assert.AreEqual(0, ms.GlobalBoundary.Links.Count);
                Assert.IsTrue(mSession.AddLink(user, module, module.Hooks[0], parameter, out var link, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Links.Count);
                Assert.IsTrue(mSession.Undo(user, ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Links.Count);
                Assert.IsTrue(mSession.Redo(user, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Links.Count);
                Assert.AreSame(link, ms.GlobalBoundary.Links[0]);

                // now remove the link explicitly
                Assert.IsTrue(mSession.RemoveLink(user, link, ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Links.Count);
            });
        }

        [TestMethod]
        public void RemoveLinkWithBadUser()
        {
            TestHelper.RunInModelSystemContext("RemoveLinkWithBadUser", (user, unauthorizedUser, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.AreEqual(0, ms.GlobalBoundary.Modules.Count);
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Start", typeof(BasicParameter<string>),
                    out var parameter, ref error), error);
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Start", typeof(SimpleParameterModule),
                    out var module, ref error), error);
                Assert.AreEqual(2, ms.GlobalBoundary.Modules.Count);
                Assert.AreEqual(0, ms.GlobalBoundary.Links.Count);
                Assert.IsTrue(mSession.AddLink(user, module, module.Hooks[0], parameter, out var link, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Links.Count);
                Assert.IsTrue(mSession.Undo(user, ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Links.Count);
                Assert.IsTrue(mSession.Redo(user, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Links.Count);
                Assert.AreSame(link, ms.GlobalBoundary.Links[0]);

                // now remove the link explicitly
                Assert.IsFalse(mSession.RemoveLink(unauthorizedUser, link, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Links.Count);
            });
        }

        [TestMethod]
        public void UndoRemoveLink()
        {
            TestHelper.RunInModelSystemContext("UndoRemoveLink", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.AreEqual(0, ms.GlobalBoundary.Modules.Count);
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Start", typeof(BasicParameter<string>),
                    out var parameter, ref error), error);
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Start", typeof(SimpleParameterModule),
                    out var module, ref error), error);
                Assert.AreEqual(2, ms.GlobalBoundary.Modules.Count);
                Assert.AreEqual(0, ms.GlobalBoundary.Links.Count);
                Assert.IsTrue(mSession.AddLink(user, module, module.Hooks[0], parameter, out var link, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Links.Count);
                Assert.IsTrue(mSession.Undo(user, ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Links.Count);
                Assert.IsTrue(mSession.Redo(user, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Links.Count);
                Assert.AreSame(link, ms.GlobalBoundary.Links[0]);

                // now remove the link explicitly
                Assert.IsTrue(mSession.RemoveLink(user, link, ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Links.Count);
                Assert.IsTrue(mSession.Undo(user, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Links.Count);
                Assert.AreSame(link, ms.GlobalBoundary.Links[0]);
                Assert.IsTrue(mSession.Redo(user, ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Links.Count);
            });
        }

        [TestMethod]
        public void AddSingleLinkToDifferentModule()
        {
            /*
             * This test will try to assign a link between from a single hook to different modules.
             * This operation should remove the first link and then add the second
             */
            TestHelper.RunInModelSystemContext("AddSingleLinkToDifferentModule", (user, pSession, mSession) =>
            {
                // initialization
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.IsTrue(mSession.AddModelSystemStart(user, ms.GlobalBoundary, "FirstStart", out var start, ref error), error);
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "MyMSS", typeof(SimpleTestModule), out var mss1, ref error));
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "MyMSS", typeof(SimpleTestModule), out var mss2, ref error));
                Assert.IsTrue(mSession.AddLink(user, start, start.Hooks[0], mss1, out var link1, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Links.Count);
                // This should not create a new link but move the previous one
                Assert.IsTrue(mSession.AddLink(user, start, start.Hooks[0], mss2, out var link2, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Links.Count);
            });
        }

        [TestMethod]
        public void RemoveLinkToBoundariesThatWereRemoved()
        {
            TestHelper.RunInModelSystemContext("RemoveLinkToBoundariesThatWereRemoved", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                string error = null;
                var global = ms.GlobalBoundary;
                // Setup the delete
                Assert.IsTrue(mSession.AddBoundary(user, global, "ToRemove", out var toRemove, ref error), error);
                Assert.IsTrue(mSession.AddModelSystemStart(user, global, "Start", out var start, ref error), error);
                Assert.IsTrue(mSession.AddNode(user, toRemove, "Tricky", typeof(IgnoreResult<string>),
                    out var tricky, ref error), error);
                Assert.IsTrue(mSession.AddLink(user, start, start.Hooks[0], tricky, out var link, ref error), error);
                Assert.AreEqual(1, global.Starts.Count);
                Assert.AreEqual(1, global.Links.Count);
                Assert.AreEqual(1, toRemove.Modules.Count);

                // Now remove the boundary and check to make sure the number of links is cleaned up
                Assert.IsTrue(mSession.RemoveBoundary(user, global, toRemove, ref error), error);
                Assert.AreEqual(0, global.Links.Count, "We did not remove the link during the remove boundary!");
                Assert.IsTrue(mSession.Undo(user, ref error), error);
                Assert.AreEqual(1, global.Links.Count, "The link was not restored after the undo on the remove boundary!");
                Assert.IsTrue(mSession.Redo(user, ref error), error);
                Assert.AreEqual(0, global.Links.Count, "We did not remove the link again doing the redo of the remove boundary!");
            });
        }

        [TestMethod]
        public void RemoveMultiLinkToBoundariesThatWereRemoved()
        {
            TestHelper.RunInModelSystemContext("RemoveMultiLinkToBoundariesThatWereRemoved", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                string error = null;
                var global = ms.GlobalBoundary;
                // Setup the delete
                Assert.IsTrue(mSession.AddBoundary(user, global, "ToRemove", out var toRemove, ref error), error);
                Assert.IsTrue(mSession.AddModelSystemStart(user, global, "Start", out var start, ref error), error);
                Assert.IsTrue(mSession.AddNode(user, global, "Execute", typeof(Execute),
                    out var execute, ref error), error);
                Assert.IsTrue(mSession.AddNode(user, toRemove, "Tricky", typeof(IgnoreResult<string>),
                    out var tricky, ref error), error);
                Assert.IsTrue(mSession.AddLink(user, start, start.Hooks[0], execute, out var link, ref error), error);
                Assert.IsTrue(mSession.AddLink(user, execute, TestHelper.GetHook(execute.Hooks, "To Execute"), tricky, out var link2, ref error), error);
                Assert.AreEqual(1, global.Starts.Count);
                Assert.AreEqual(1, global.Modules.Count);
                Assert.AreEqual(2, global.Links.Count);
                Assert.AreEqual(1, toRemove.Modules.Count);

                // Now remove the boundary and check to make sure the number of links is cleaned up
                Assert.IsTrue(mSession.RemoveBoundary(user, global, toRemove, ref error), error);
                Assert.AreEqual(0, ((MultiLink)global.Links.First(l => l.Origin == execute)).Destinations.Count);
                Assert.IsTrue(mSession.Undo(user, ref error), error);
                Assert.AreEqual(1, ((MultiLink)global.Links.First(l => l.Origin == execute)).Destinations.Count);
                Assert.IsTrue(mSession.Redo(user, ref error), error);
                Assert.AreEqual(0, ((MultiLink)global.Links.First(l => l.Origin == execute)).Destinations.Count);
            });
        }

        [TestMethod]
        public void RemoveSingleDestinationInMultiLink()
        {
            TestHelper.RunInModelSystemContext("RemoveSingleDestinationInMultiLink", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                string error = null;
                var global = ms.GlobalBoundary;
                Assert.IsTrue(mSession.AddModelSystemStart(user, global, "Start", out var start, ref error), error);
                Assert.IsTrue(mSession.AddNode(user, global, "Execute", typeof(Execute),
                    out var execute, ref error), error);
                Assert.IsTrue(mSession.AddNode(user, global, "Tricky", typeof(IgnoreResult<string>),
                    out var ignore, ref error), error);

                Assert.IsTrue(mSession.AddLink(user, start, start.Hooks[0], execute, out var link, ref error), error);
                // Create 2 links from the execute into the ignore
                Assert.IsTrue(mSession.AddLink(user, execute, TestHelper.GetHook(execute.Hooks, "To Execute"),
                    execute, out var linkI1, ref error), error);
                Assert.IsTrue(mSession.AddLink(user, execute, TestHelper.GetHook(execute.Hooks, "To Execute"),
                    execute, out var _, ref error), error);

                Assert.AreEqual(2, ((MultiLink)linkI1).Destinations.Count);
                Assert.IsTrue(mSession.RemoveLinkDestination(user, linkI1, 0, ref error), error);
                Assert.AreEqual(1, ((MultiLink)linkI1).Destinations.Count);
                Assert.IsTrue(mSession.Undo(user, ref error), error);
                Assert.AreEqual(2, ((MultiLink)linkI1).Destinations.Count);
                Assert.IsTrue(mSession.Redo(user, ref error), error);
                Assert.AreEqual(1, ((MultiLink)linkI1).Destinations.Count);
            });
        }

        [TestMethod]
        public void RemoveSingleDestinationInMultiLinkWithBadUser()
        {
            TestHelper.RunInModelSystemContext("RemoveSingleDestinationInMultiLinkWithBadUser", (user, unauthorizedUser, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                string error = null;
                var global = ms.GlobalBoundary;
                Assert.IsTrue(mSession.AddModelSystemStart(user, global, "Start", out var start, ref error), error);
                Assert.IsTrue(mSession.AddNode(user, global, "Execute", typeof(Execute),
                    out var execute, ref error), error);
                Assert.IsTrue(mSession.AddNode(user, global, "Tricky", typeof(IgnoreResult<string>),
                    out var ignore, ref error), error);

                Assert.IsTrue(mSession.AddLink(user, start, start.Hooks[0], execute, out var link, ref error), error);
                // Create 2 links from the execute into the ignore
                Assert.IsTrue(mSession.AddLink(user, execute, TestHelper.GetHook(execute.Hooks, "To Execute"),
                    execute, out var linkI1, ref error), error);
                Assert.IsTrue(mSession.AddLink(user, execute, TestHelper.GetHook(execute.Hooks, "To Execute"),
                    execute, out var _, ref error), error);

                Assert.AreEqual(2, ((MultiLink)linkI1).Destinations.Count);
                Assert.IsFalse(mSession.RemoveLinkDestination(unauthorizedUser, linkI1, 0, ref error), error);
                Assert.AreEqual(2, ((MultiLink)linkI1).Destinations.Count, "An unauthorized user was able to change the number of destinations.");
            });
        }

        [TestMethod]
        public void DisableLink()
        {
            TestHelper.RunInModelSystemContext("DisableLink", (user, pSession, msSession) =>
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
        public void DisableLinkWithBadUser()
        {
            TestHelper.RunInModelSystemContext("DisableLinkWithBadUser", (user, unauthorizedUser, pSession, msSession) =>
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
                Assert.IsFalse(msSession.SetLinkDisabled(unauthorizedUser, startLink, true, ref error), error);
            });
        }

        [TestMethod]
        public void DisabledLinkRunValidationFailure()
        {
            TestHelper.RunInModelSystemContext("DisabledLinkRunValidationFailure", (user, pSession, msSession) =>
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
