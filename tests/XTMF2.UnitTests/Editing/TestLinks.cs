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
using XTMF2.UnitTests.Modules;
using XTMF2.ModelSystemConstruct;
using XTMF2.RuntimeModules;
using XTMF2.Editing;

namespace XTMF2.UnitTests.Editing
{
    [TestClass]
    public class TestLinks
    {
        [TestMethod]
        public void SetOrthogonalBreakpointUndoRedo()
        {
            TestHelper.RunInModelSystemContext("SetOrthogonalBreakpointUndoRedo", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                CommandError error = null;
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Parameter", typeof(BasicParameter<string>), Rectangle.Hidden,
                    out var parameter, out error), error?.Message);
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Module", typeof(SimpleParameterModule), Rectangle.Hidden,
                    out var module, out error), error?.Message);
                Assert.IsTrue(mSession.AddLink(user, module, module.Hooks[0], parameter, out var link, out error), error?.Message);

                Assert.IsNull(link.OrthogonalBreakpointX);
                Assert.IsTrue(mSession.SetLinkOrthogonalBreakpointX(user, link, 180.0, out error), error?.Message);
                Assert.AreEqual(180.0, link.OrthogonalBreakpointX);
                Assert.IsTrue(mSession.Undo(user, out error), error?.Message);
                Assert.IsNull(link.OrthogonalBreakpointX);
                Assert.IsTrue(mSession.Redo(user, out error), error?.Message);
                Assert.AreEqual(180.0, link.OrthogonalBreakpointX);
            });
        }

        [TestMethod]
        public void SetMultipleOrthogonalBreakpointsUndoRedoAsOneAction()
        {
            TestHelper.RunInModelSystemContext("SetMultipleOrthogonalBreakpointsUndoRedoAsOneAction", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                CommandError error = null;
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Parameter1", typeof(BasicParameter<string>), Rectangle.Hidden,
                    out var parameter1, out error), error?.Message);
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Module1", typeof(SimpleParameterModule), Rectangle.Hidden,
                    out var module1, out error), error?.Message);
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Parameter2", typeof(BasicParameter<string>), Rectangle.Hidden,
                    out var parameter2, out error), error?.Message);
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Module2", typeof(SimpleParameterModule), Rectangle.Hidden,
                    out var module2, out error), error?.Message);
                Assert.IsTrue(mSession.AddLink(user, module1, module1.Hooks[0], parameter1, out var link1, out error), error?.Message);
                Assert.IsTrue(mSession.AddLink(user, module2, module2.Hooks[0], parameter2, out var link2, out error), error?.Message);

                Assert.IsTrue(mSession.SetLinksOrthogonalBreakpointX(user, new[] { link1, link2 }, 240.0, out error), error?.Message);
                Assert.AreEqual(240.0, link1.OrthogonalBreakpointX);
                Assert.AreEqual(240.0, link2.OrthogonalBreakpointX);
                Assert.IsTrue(mSession.Undo(user, out error), error?.Message);
                Assert.IsNull(link1.OrthogonalBreakpointX);
                Assert.IsNull(link2.OrthogonalBreakpointX);
                Assert.IsTrue(mSession.Redo(user, out error), error?.Message);
                Assert.AreEqual(240.0, link1.OrthogonalBreakpointX);
                Assert.AreEqual(240.0, link2.OrthogonalBreakpointX);
            });
        }

        [TestMethod]
        public void UndoAddLink()
        {
            TestHelper.RunInModelSystemContext("UndoAddLink", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                CommandError error = null;
                Assert.IsEmpty(ms.GlobalBoundary.Modules);
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Start", typeof(BasicParameter<string>), Rectangle.Hidden,
                    out var parameter, out error), error?.Message);
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Start", typeof(SimpleParameterModule), Rectangle.Hidden,
                    out var module, out error), error?.Message);
                Assert.HasCount(2, ms.GlobalBoundary.Modules);
                Assert.IsEmpty(ms.GlobalBoundary.Links);
                Assert.IsTrue(mSession.AddLink(user, module, module.Hooks[0], parameter, out var link, out error), error?.Message);
                Assert.HasCount(1, ms.GlobalBoundary.Links);
                Assert.IsTrue(mSession.Undo(user, out error), error?.Message);
                Assert.IsEmpty(ms.GlobalBoundary.Links);
                Assert.IsTrue(mSession.Redo(user, out error), error?.Message);
                Assert.HasCount(1, ms.GlobalBoundary.Links);
                Assert.AreSame(link, ms.GlobalBoundary.Links[0]);
            });
        }

        [TestMethod]
        public void RemoveLink()
        {
            TestHelper.RunInModelSystemContext("RemoveLink", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                CommandError error = null;
                Assert.IsEmpty(ms.GlobalBoundary.Modules);
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Start", typeof(BasicParameter<string>), Rectangle.Hidden,
                    out var parameter, out error), error?.Message);
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Start", typeof(SimpleParameterModule), Rectangle.Hidden,
                    out var module, out error), error?.Message);
                Assert.HasCount(2, ms.GlobalBoundary.Modules);
                Assert.IsEmpty(ms.GlobalBoundary.Links);
                Assert.IsTrue(mSession.AddLink(user, module, module.Hooks[0], parameter, out var link, out error), error?.Message);
                Assert.HasCount(1, ms.GlobalBoundary.Links);
                Assert.IsTrue(mSession.Undo(user, out error), error?.Message);
                Assert.IsEmpty(ms.GlobalBoundary.Links);
                Assert.IsTrue(mSession.Redo(user, out error), error?.Message);
                Assert.HasCount(1, ms.GlobalBoundary.Links);
                Assert.AreSame(link, ms.GlobalBoundary.Links[0]);

                // now remove the link explicitly
                Assert.IsTrue(mSession.RemoveLink(user, link, out error), error?.Message);
                Assert.IsEmpty(ms.GlobalBoundary.Links);
            });
        }

        [TestMethod]
        public void RemoveLinkWithBadUser()
        {
            TestHelper.RunInModelSystemContext("RemoveLinkWithBadUser", (user, unauthorizedUser, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                CommandError error = null;
                Assert.IsEmpty(ms.GlobalBoundary.Modules);
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Start", typeof(BasicParameter<string>), Rectangle.Hidden,
                    out var parameter, out error), error?.Message);
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Start", typeof(SimpleParameterModule), Rectangle.Hidden,
                    out var module, out error), error?.Message);
                Assert.HasCount(2, ms.GlobalBoundary.Modules);
                Assert.IsEmpty(ms.GlobalBoundary.Links);
                Assert.IsTrue(mSession.AddLink(user, module, module.Hooks[0], parameter, out var link, out error), error?.Message);
                Assert.HasCount(1, ms.GlobalBoundary.Links);
                Assert.IsTrue(mSession.Undo(user, out error), error?.Message);
                Assert.IsEmpty(ms.GlobalBoundary.Links);
                Assert.IsTrue(mSession.Redo(user, out error), error?.Message);
                Assert.HasCount(1, ms.GlobalBoundary.Links);
                Assert.AreSame(link, ms.GlobalBoundary.Links[0]);

                // now remove the link explicitly
                Assert.IsFalse(mSession.RemoveLink(unauthorizedUser, link, out error), error?.Message);
                Assert.HasCount(1, ms.GlobalBoundary.Links);
            });
        }

        [TestMethod]
        public void UndoRemoveLink()
        {
            TestHelper.RunInModelSystemContext("UndoRemoveLink", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                CommandError error = null;
                Assert.IsEmpty(ms.GlobalBoundary.Modules);
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Start", typeof(BasicParameter<string>), Rectangle.Hidden,
                    out var parameter, out error), error?.Message);
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Start", typeof(SimpleParameterModule), Rectangle.Hidden,
                    out var module, out error), error?.Message);
                Assert.HasCount(2, ms.GlobalBoundary.Modules);
                Assert.IsEmpty(ms.GlobalBoundary.Links);
                Assert.IsTrue(mSession.AddLink(user, module, module.Hooks[0], parameter, out var link, out error), error?.Message);
                Assert.HasCount(1, ms.GlobalBoundary.Links);
                Assert.IsTrue(mSession.Undo(user, out error), error?.Message);
                Assert.IsEmpty(ms.GlobalBoundary.Links);
                Assert.IsTrue(mSession.Redo(user, out error), error?.Message);
                Assert.HasCount(1, ms.GlobalBoundary.Links);
                Assert.AreSame(link, ms.GlobalBoundary.Links[0]);

                // now remove the link explicitly
                Assert.IsTrue(mSession.RemoveLink(user, link, out error), error?.Message);
                Assert.IsEmpty(ms.GlobalBoundary.Links);
                Assert.IsTrue(mSession.Undo(user, out error), error?.Message);
                Assert.HasCount(1, ms.GlobalBoundary.Links);
                Assert.AreSame(link, ms.GlobalBoundary.Links[0]);
                Assert.IsTrue(mSession.Redo(user, out error), error?.Message);
                Assert.IsEmpty(ms.GlobalBoundary.Links);
            });
        }

        [TestMethod]
        public void UndoRemoveMultipleLinksAsOneAction()
        {
            TestHelper.RunInModelSystemContext("UndoRemoveMultipleLinksAsOneAction", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                CommandError error = null;
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Parameter1", typeof(BasicParameter<string>), Rectangle.Hidden,
                    out var parameter1, out error), error?.Message);
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Module1", typeof(SimpleParameterModule), Rectangle.Hidden,
                    out var module1, out error), error?.Message);
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Parameter2", typeof(BasicParameter<string>), Rectangle.Hidden,
                    out var parameter2, out error), error?.Message);
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Module2", typeof(SimpleParameterModule), Rectangle.Hidden,
                    out var module2, out error), error?.Message);
                Assert.IsTrue(mSession.AddLink(user, module1, module1.Hooks[0], parameter1, out var link1, out error), error?.Message);
                Assert.IsTrue(mSession.AddLink(user, module2, module2.Hooks[0], parameter2, out var link2, out error), error?.Message);
                Assert.HasCount(2, ms.GlobalBoundary.Links);

                Assert.IsTrue(mSession.RemoveLinks(user, new[] { link1, link2 }, out error), error?.Message);
                Assert.IsEmpty(ms.GlobalBoundary.Links);
                Assert.IsTrue(mSession.Undo(user, out error), error?.Message);
                Assert.HasCount(2, ms.GlobalBoundary.Links);
                Assert.Contains(link1, ms.GlobalBoundary.Links);
                Assert.Contains(link2, ms.GlobalBoundary.Links);
                Assert.IsTrue(mSession.Redo(user, out error), error?.Message);
                Assert.IsEmpty(ms.GlobalBoundary.Links);
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
                CommandError error = null;
                Assert.IsTrue(mSession.AddModelSystemStart(user, ms.GlobalBoundary, "FirstStart", Rectangle.Hidden, out var start, out error), error?.Message);
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "MyMSS", typeof(SimpleTestModule), Rectangle.Hidden, out var mss1, out error));
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "MyMSS", typeof(SimpleTestModule), Rectangle.Hidden, out var mss2, out error));
                Assert.IsTrue(mSession.AddLink(user, start, start.Hooks[0], mss1, out var link1, out error), error?.Message);
                Assert.HasCount(1, ms.GlobalBoundary.Links);
                // This should not create a new link but move the previous one
                Assert.IsTrue(mSession.AddLink(user, start, start.Hooks[0], mss2, out var link2, out error), error?.Message);
                Assert.HasCount(1, ms.GlobalBoundary.Links);
            });
        }

        [TestMethod]
        public void RemoveLinkToBoundariesThatWereRemoved()
        {
            TestHelper.RunInModelSystemContext("RemoveLinkToBoundariesThatWereRemoved", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                CommandError error = null;
                var global = ms.GlobalBoundary;
                // Setup the delete
                Assert.IsTrue(mSession.AddBoundary(user, global, "ToRemove", out var toRemove, out error), error?.Message);
                Assert.IsTrue(mSession.AddModelSystemStart(user, global, "Start", Rectangle.Hidden, out var start, out error), error?.Message);
                Assert.IsTrue(mSession.AddNode(user, toRemove, "Tricky", typeof(IgnoreResult<string>), Rectangle.Hidden,
                    out var tricky, out error), error?.Message);
                Assert.IsTrue(mSession.AddLink(user, start, start.Hooks[0], tricky, out var link, out error), error?.Message);
                Assert.HasCount(1, global.Starts);
                Assert.HasCount(1, global.Links);
                Assert.HasCount(1, toRemove.Modules);

                // Now remove the boundary and check to make sure the number of links is cleaned up
                Assert.IsTrue(mSession.RemoveBoundary(user, global, toRemove, out error), error?.Message);
                Assert.IsEmpty(global.Links, "We did not remove the link during the remove boundary!");
                Assert.IsTrue(mSession.Undo(user, out error), error?.Message);
                Assert.HasCount(1, global.Links, "The link was not restored after the undo on the remove boundary!");
                Assert.IsTrue(mSession.Redo(user, out error), error?.Message);
                Assert.IsEmpty(global.Links, "We did not remove the link again doing the redo of the remove boundary!");
            });
        }

        [TestMethod]
        public void RemoveMultiLinkToBoundariesThatWereRemoved()
        {
            TestHelper.RunInModelSystemContext("RemoveMultiLinkToBoundariesThatWereRemoved", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                CommandError error = null;
                var global = ms.GlobalBoundary;
                // Setup the delete
                Assert.IsTrue(mSession.AddBoundary(user, global, "ToRemove", out var toRemove, out error), error?.Message);
                Assert.IsTrue(mSession.AddModelSystemStart(user, global, "Start", Rectangle.Hidden, out var start, out error), error?.Message);
                Assert.IsTrue(mSession.AddNode(user, global, "Execute", typeof(Execute), Rectangle.Hidden,
                    out var execute, out error), error?.Message);
                Assert.IsTrue(mSession.AddNode(user, toRemove, "Tricky", typeof(IgnoreResult<string>), Rectangle.Hidden,
                    out var tricky, out error), error?.Message);
                Assert.IsTrue(mSession.AddLink(user, start, start.Hooks[0], execute, out var link, out error), error?.Message);
                Assert.IsTrue(mSession.AddLink(user, execute, TestHelper.GetHook(execute.Hooks, "To Execute"), tricky, out var link2, out error), error?.Message);
                Assert.HasCount(1, global.Starts);
                Assert.HasCount(1, global.Modules);
                Assert.HasCount(2, global.Links);
                Assert.HasCount(1, toRemove.Modules);

                // Now remove the boundary and check to make sure the number of links is cleaned up
                Assert.IsTrue(mSession.RemoveBoundary(user, global, toRemove, out error), error?.Message);
                Assert.IsEmpty(((MultiLink)global.Links.First(l => l.Origin == execute)).Destinations);
                Assert.IsTrue(mSession.Undo(user, out error), error?.Message);
                Assert.HasCount(1, ((MultiLink)global.Links.First(l => l.Origin == execute)).Destinations);
                Assert.IsTrue(mSession.Redo(user, out error), error?.Message);
                Assert.IsEmpty(((MultiLink)global.Links.First(l => l.Origin == execute)).Destinations);
            });
        }

        [TestMethod]
        public void RemoveSingleDestinationInMultiLink()
        {
            TestHelper.RunInModelSystemContext("RemoveSingleDestinationInMultiLink", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                CommandError error = null;
                var global = ms.GlobalBoundary;
                Assert.IsTrue(mSession.AddModelSystemStart(user, global, "Start", Rectangle.Hidden, out var start, out error), error?.Message);
                Assert.IsTrue(mSession.AddNode(user, global, "Execute", typeof(Execute), Rectangle.Hidden,
                    out var execute, out error), error?.Message);
                Assert.IsTrue(mSession.AddNode(user, global, "Tricky", typeof(IgnoreResult<string>), Rectangle.Hidden,
                    out var ignore, out error), error?.Message);

                Assert.IsTrue(mSession.AddLink(user, start, start.Hooks[0], execute, out var link, out error), error?.Message);
                // Create 2 links from the execute into the ignore
                Assert.IsTrue(mSession.AddLink(user, execute, TestHelper.GetHook(execute.Hooks, "To Execute"),
                    execute, out var linkI1, out error), error?.Message);
                Assert.IsTrue(mSession.AddLink(user, execute, TestHelper.GetHook(execute.Hooks, "To Execute"),
                    execute, out var _, out error), error?.Message);

                Assert.HasCount(2, ((MultiLink)linkI1).Destinations);
                Assert.IsTrue(mSession.RemoveLinkDestination(user, linkI1, 0, out error), error?.Message);
                Assert.HasCount(1, ((MultiLink)linkI1).Destinations);
                Assert.IsTrue(mSession.Undo(user, out error), error?.Message);
                Assert.HasCount(2, ((MultiLink)linkI1).Destinations);
                Assert.IsTrue(mSession.Redo(user, out error), error?.Message);
                Assert.HasCount(1, ((MultiLink)linkI1).Destinations);
            });
        }

        [TestMethod]
        public void RemoveSingleDestinationInMultiLinkWithBadUser()
        {
            TestHelper.RunInModelSystemContext("RemoveSingleDestinationInMultiLinkWithBadUser", (user, unauthorizedUser, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                CommandError error = null;
                var global = ms.GlobalBoundary;
                Assert.IsTrue(mSession.AddModelSystemStart(user, global, "Start", Rectangle.Hidden, out var start, out error), error?.Message);
                Assert.IsTrue(mSession.AddNode(user, global, "Execute", typeof(Execute), Rectangle.Hidden,
                    out var execute, out error), error?.Message);
                Assert.IsTrue(mSession.AddNode(user, global, "Tricky", typeof(IgnoreResult<string>), Rectangle.Hidden,
                    out var ignore, out error), error?.Message);

                Assert.IsTrue(mSession.AddLink(user, start, start.Hooks[0], execute, out var link, out error), error?.Message);
                // Create 2 links from the execute into the ignore
                Assert.IsTrue(mSession.AddLink(user, execute, TestHelper.GetHook(execute.Hooks, "To Execute"),
                    execute, out var linkI1, out error), error?.Message);
                Assert.IsTrue(mSession.AddLink(user, execute, TestHelper.GetHook(execute.Hooks, "To Execute"),
                    execute, out var _, out error), error?.Message);

                Assert.HasCount(2, ((MultiLink)linkI1).Destinations);
                Assert.IsFalse(mSession.RemoveLinkDestination(unauthorizedUser, linkI1, 0, out error), error?.Message);
                Assert.HasCount(2, ((MultiLink)linkI1).Destinations, "An unauthorized user was able to change the number of destinations.");
            });
        }

        [TestMethod]
        public void DisableLink()
        {
            TestHelper.RunInModelSystemContext("DisableLink", (user, pSession, msSession) =>
            {
                // initialization
                var ms = msSession.ModelSystem;
                CommandError error = null;
                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", Rectangle.Hidden, out Start start, out error), error?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "AnIgnore", typeof(IgnoreResult<string>), Rectangle.Hidden, out var ignoreMSS, out error), error?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "SPM", typeof(SimpleParameterModule), Rectangle.Hidden, out var spm, out error), error?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "MyParameter", typeof(BasicParameter<string>), Rectangle.Hidden, out var basicParameter, out error), error?.Message);
                Assert.IsTrue(msSession.AddLink(user, start, start.Hooks[0], ignoreMSS, out var startLink, out error), error?.Message);

                Assert.IsFalse(startLink.IsDisabled, "The link initialized as disabled!");
                Assert.IsTrue(msSession.SetLinkDisabled(user, startLink, true, out error), error?.Message);
                Assert.IsTrue(startLink.IsDisabled, "The link initialized as disabled!");
                Assert.IsTrue(msSession.Undo(user, out error), error?.Message);
                Assert.IsFalse(startLink.IsDisabled);
                Assert.IsTrue(msSession.Redo(user, out error), error?.Message);
                Assert.IsTrue(startLink.IsDisabled);
            }, (user, pSession, mSession) =>
            {
                // after shutdown
                var ms = mSession.ModelSystem;
                var modules = ms.GlobalBoundary.Modules;
                var links = ms.GlobalBoundary.Links;
                Assert.HasCount(3, modules);
                Assert.HasCount(1, links);
                Assert.IsTrue(links[0].IsDisabled, "The link was not disabled on reload.");
            });
        }

        [TestMethod]
        public void DisableMultipleLinksIsSingleUndoRedo()
        {
            TestHelper.RunInModelSystemContext("DisableMultipleLinksIsSingleUndoRedo", (user, pSession, msSession) =>
            {
                var ms = msSession.ModelSystem;
                CommandError error = null;

                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "StartA", Rectangle.Hidden,
                    out Start startA, out error), error?.Message);
                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "StartB", Rectangle.Hidden,
                    out Start startB, out error), error?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "SinkA", typeof(IgnoreResult<string>), Rectangle.Hidden,
                    out var sinkA, out error), error?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "SinkB", typeof(IgnoreResult<string>), Rectangle.Hidden,
                    out var sinkB, out error), error?.Message);

                Assert.IsTrue(msSession.AddLink(user, startA, startA.Hooks[0], sinkA!, out var linkA, out error), error?.Message);
                Assert.IsTrue(msSession.AddLink(user, startB, startB.Hooks[0], sinkB!, out var linkB, out error), error?.Message);

                Assert.IsFalse(linkA!.IsDisabled);
                Assert.IsFalse(linkB!.IsDisabled);

                Assert.IsTrue(msSession.SetLinksDisabled(user, new[] { linkA, linkB }, true, out error), error?.Message);
                Assert.IsTrue(linkA.IsDisabled, "LinkA was not disabled by the bulk operation.");
                Assert.IsTrue(linkB.IsDisabled, "LinkB was not disabled by the bulk operation.");

                Assert.IsTrue(msSession.Undo(user, out error), error?.Message);
                Assert.IsFalse(linkA.IsDisabled, "LinkA was not restored by a single undo.");
                Assert.IsFalse(linkB.IsDisabled, "LinkB was not restored by a single undo.");

                Assert.IsTrue(msSession.Redo(user, out error), error?.Message);
                Assert.IsTrue(linkA.IsDisabled, "LinkA was not disabled by a single redo.");
                Assert.IsTrue(linkB.IsDisabled, "LinkB was not disabled by a single redo.");
            });
        }

        [TestMethod]
        public void EnableMultipleLinksIsSingleUndoRedo()
        {
            TestHelper.RunInModelSystemContext("EnableMultipleLinksIsSingleUndoRedo", (user, pSession, msSession) =>
            {
                var ms = msSession.ModelSystem;
                CommandError error = null;

                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "StartA", Rectangle.Hidden,
                    out Start startA, out error), error?.Message);
                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "StartB", Rectangle.Hidden,
                    out Start startB, out error), error?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "SinkA", typeof(IgnoreResult<string>), Rectangle.Hidden,
                    out var sinkA, out error), error?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "SinkB", typeof(IgnoreResult<string>), Rectangle.Hidden,
                    out var sinkB, out error), error?.Message);

                Assert.IsTrue(msSession.AddLink(user, startA, startA.Hooks[0], sinkA!, out var linkA, out error), error?.Message);
                Assert.IsTrue(msSession.AddLink(user, startB, startB.Hooks[0], sinkB!, out var linkB, out error), error?.Message);

                Assert.IsTrue(msSession.SetLinkDisabled(user, linkA!, true, out error), error?.Message);
                Assert.IsTrue(msSession.SetLinkDisabled(user, linkB!, true, out error), error?.Message);
                Assert.IsTrue(linkA.IsDisabled);
                Assert.IsTrue(linkB.IsDisabled);

                Assert.IsTrue(msSession.SetLinksDisabled(user, new[] { linkA, linkB }, false, out error), error?.Message);
                Assert.IsFalse(linkA.IsDisabled, "LinkA was not enabled by the bulk operation.");
                Assert.IsFalse(linkB.IsDisabled, "LinkB was not enabled by the bulk operation.");

                Assert.IsTrue(msSession.Undo(user, out error), error?.Message);
                Assert.IsTrue(linkA.IsDisabled, "LinkA disabled state was not restored by a single undo.");
                Assert.IsTrue(linkB.IsDisabled, "LinkB disabled state was not restored by a single undo.");

                Assert.IsTrue(msSession.Redo(user, out error), error?.Message);
                Assert.IsFalse(linkA.IsDisabled, "LinkA was not enabled by a single redo.");
                Assert.IsFalse(linkB.IsDisabled, "LinkB was not enabled by a single redo.");
            });
        }

        [TestMethod]
        public void DisableLinkWithBadUser()
        {
            TestHelper.RunInModelSystemContext("DisableLinkWithBadUser", (user, unauthorizedUser, pSession, msSession) =>
            {
                // initialization
                var ms = msSession.ModelSystem;
                CommandError error = null;
                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", Rectangle.Hidden, out Start start, out error), error?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "AnIgnore", typeof(IgnoreResult<string>), Rectangle.Hidden, out var ignoreMSS, out error), error?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "SPM", typeof(SimpleParameterModule), Rectangle.Hidden, out var spm, out error), error?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "MyParameter", typeof(BasicParameter<string>), Rectangle.Hidden, out var basicParameter, out error), error?.Message);
                Assert.IsTrue(msSession.AddLink(user, start, start.Hooks[0], ignoreMSS, out var startLink, out error), error?.Message);

                Assert.IsFalse(startLink.IsDisabled, "The link initialized as disabled!");
                Assert.IsFalse(msSession.SetLinkDisabled(unauthorizedUser, startLink, true, out error), error?.Message);
            });
        }

        [TestMethod]
        public void HideSingleDestinationBranchPersistsAndUndoRedo()
        {
            TestHelper.RunInModelSystemContext("HideSingleDestinationBranchPersistsAndUndoRedo", (user, pSession, msSession) =>
            {
                var ms = msSession.ModelSystem;
                CommandError error = null;

                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", Rectangle.Hidden,
                    out Start start, out error), error?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Sink", typeof(IgnoreResult<string>), Rectangle.Hidden,
                    out var sink, out error), error?.Message);
                Assert.IsTrue(msSession.AddLink(user, start, start.Hooks[0], sink!, out var link, out error), error?.Message);

                Assert.IsFalse(link!.IsDestinationHidden(0));
                Assert.IsTrue(msSession.SetLinkDestinationHidden(user, link, 0, true, out error), error?.Message);
                Assert.IsTrue(link.IsDestinationHidden(0));

                Assert.IsTrue(msSession.Undo(user, out error), error?.Message);
                Assert.IsFalse(link.IsDestinationHidden(0));

                Assert.IsTrue(msSession.Redo(user, out error), error?.Message);
                Assert.IsTrue(link.IsDestinationHidden(0));
            }, (user, pSession, msSession) =>
            {
                var ms = msSession.ModelSystem;
                var link = ms.GlobalBoundary.Links.Single();
                Assert.IsTrue(link.IsDestinationHidden(0), "Destination hidden state did not persist after reload.");
            });
        }

        [TestMethod]
        public void HideAllDestinationsGlobalUndoRedo()
        {
            TestHelper.RunInModelSystemContext("HideAllDestinationsGlobalUndoRedo", (user, pSession, msSession) =>
            {
                var ms = msSession.ModelSystem;
                CommandError error = null;

                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "StartA", Rectangle.Hidden,
                    out Start startA, out error), error?.Message);
                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "StartB", Rectangle.Hidden,
                    out Start startB, out error), error?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "SinkA", typeof(IgnoreResult<string>), Rectangle.Hidden,
                    out var sinkA, out error), error?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "SinkB", typeof(IgnoreResult<string>), Rectangle.Hidden,
                    out var sinkB, out error), error?.Message);

                Assert.IsTrue(msSession.AddLink(user, startA, startA.Hooks[0], sinkA!, out var linkA, out error), error?.Message);
                Assert.IsTrue(msSession.AddLink(user, startB, startB.Hooks[0], sinkB!, out var linkB, out error), error?.Message);

                Assert.IsTrue(msSession.SetAllLinkDestinationsHidden(user, true, out error), error?.Message);
                Assert.IsTrue(linkA!.IsDestinationHidden(0));
                Assert.IsTrue(linkB!.IsDestinationHidden(0));

                Assert.IsTrue(msSession.Undo(user, out error), error?.Message);
                Assert.IsFalse(linkA.IsDestinationHidden(0));
                Assert.IsFalse(linkB.IsDestinationHidden(0));

                Assert.IsTrue(msSession.Redo(user, out error), error?.Message);
                Assert.IsTrue(linkA.IsDestinationHidden(0));
                Assert.IsTrue(linkB.IsDestinationHidden(0));
            });
        }

        [TestMethod]
        public void HideAllDestinationsGlobalPersistsAfterReload()
        {
            TestHelper.RunInModelSystemContext("HideAllDestinationsGlobalPersistsAfterReload", (user, pSession, msSession) =>
            {
                var ms = msSession.ModelSystem;
                CommandError error = null;

                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "StartA", Rectangle.Hidden,
                    out Start startA, out error), error?.Message);
                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "StartB", Rectangle.Hidden,
                    out Start startB, out error), error?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "SinkA", typeof(IgnoreResult<string>), Rectangle.Hidden,
                    out var sinkA, out error), error?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "SinkB", typeof(IgnoreResult<string>), Rectangle.Hidden,
                    out var sinkB, out error), error?.Message);

                Assert.IsTrue(msSession.AddLink(user, startA, startA.Hooks[0], sinkA!, out _, out error), error?.Message);
                Assert.IsTrue(msSession.AddLink(user, startB, startB.Hooks[0], sinkB!, out _, out error), error?.Message);

                Assert.IsTrue(msSession.SetAllLinkDestinationsHidden(user, true, out error), error?.Message);
            }, (user, pSession, msSession) =>
            {
                var links = msSession.ModelSystem.GlobalBoundary.Links;
                Assert.HasCount(2, links, "Unexpected number of links after reload.");
                foreach (var l in links)
                {
                    Assert.AreEqual(1, l.DestinationCount, "Expected single-destination links in this test setup.");
                    Assert.IsTrue(l.IsDestinationHidden(0), "Global destination-hidden state did not persist after reload.");
                }
            });
        }

        [TestMethod]
        public void HiddenMultiDestinationStateSurvivesRemoveUndo()
        {
            TestHelper.RunInModelSystemContext("HiddenMultiDestinationStateSurvivesRemoveUndo", (user, pSession, msSession) =>
            {
                var ms = msSession.ModelSystem;
                CommandError error = null;

                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", Rectangle.Hidden,
                    out Start start, out error), error?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Execute", typeof(Execute), Rectangle.Hidden,
                    out var execute, out error), error?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "SinkA", typeof(IgnoreResult<string>), Rectangle.Hidden,
                    out var sinkA, out error), error?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "SinkB", typeof(IgnoreResult<string>), Rectangle.Hidden,
                    out var sinkB, out error), error?.Message);

                Assert.IsTrue(msSession.AddLink(user, start, start.Hooks[0], execute!, out _, out error), error?.Message);
                var hook = TestHelper.GetHook(execute!.Hooks, "To Execute");
                Assert.IsTrue(msSession.AddLink(user, execute, hook, sinkA!, out var multiLinkBase, out error), error?.Message);
                Assert.IsTrue(msSession.AddLink(user, execute, hook, sinkB!, out _, out error), error?.Message);

                Assert.IsTrue(multiLinkBase is MultiLink);
                var multiLink = (MultiLink)multiLinkBase!;
                Assert.AreEqual(2, multiLink.DestinationCount);

                Assert.IsTrue(msSession.SetLinkDestinationHidden(user, multiLink, 1, true, out error), error?.Message);
                Assert.IsTrue(multiLink.IsDestinationHidden(1));

                Assert.IsTrue(msSession.RemoveLinkDestination(user, multiLink, 1, out error), error?.Message);
                Assert.AreEqual(1, multiLink.DestinationCount);

                Assert.IsTrue(msSession.Undo(user, out error), error?.Message);
                Assert.AreEqual(2, multiLink.DestinationCount);
                Assert.IsTrue(multiLink.IsDestinationHidden(1), "Removed destination hidden state was not restored on undo.");
            });
        }

        [TestMethod]
        public void HideDestinationBranchWithBadUserFails()
        {
            TestHelper.RunInModelSystemContext("HideDestinationBranchWithBadUserFails", (user, unauthorizedUser, pSession, msSession) =>
            {
                var ms = msSession.ModelSystem;
                CommandError error = null;

                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", Rectangle.Hidden,
                    out Start start, out error), error?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Sink", typeof(IgnoreResult<string>), Rectangle.Hidden,
                    out var sink, out error), error?.Message);
                Assert.IsTrue(msSession.AddLink(user, start, start.Hooks[0], sink!, out var link, out error), error?.Message);

                Assert.IsFalse(msSession.SetLinkDestinationHidden(unauthorizedUser, link!, 0, true, out error), error?.Message);
                Assert.IsFalse(link.IsDestinationHidden(0), "Unauthorized user changed destination-hidden state.");
            });
        }

        [TestMethod]
        public void MultiLinkDestinationHiddenStateStaysConsistentAcrossMoveRemoveAndReload()
        {
            TestHelper.RunInModelSystemContext("MultiLinkDestinationHiddenStateStaysConsistentAcrossMoveRemoveAndReload", (user, pSession, msSession) =>
            {
                var ms = msSession.ModelSystem;
                CommandError error = null;

                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", Rectangle.Hidden,
                    out Start start, out error), error?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Execute", typeof(Execute), Rectangle.Hidden,
                    out var execute, out error), error?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "DestA", typeof(IgnoreResult<string>), Rectangle.Hidden,
                    out var destA, out error), error?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "DestB", typeof(IgnoreResult<string>), Rectangle.Hidden,
                    out var destB, out error), error?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "DestC", typeof(IgnoreResult<string>), Rectangle.Hidden,
                    out var destC, out error), error?.Message);

                Assert.IsTrue(msSession.AddLink(user, start, start.Hooks[0], execute!, out _, out error), error?.Message);
                var hook = TestHelper.GetHook(execute!.Hooks, "To Execute");
                Assert.IsTrue(msSession.AddLink(user, execute, hook, destA!, out var mlBase, out error), error?.Message);
                Assert.IsTrue(msSession.AddLink(user, execute, hook, destB!, out _, out error), error?.Message);
                Assert.IsTrue(msSession.AddLink(user, execute, hook, destC!, out _, out error), error?.Message);

                Assert.IsTrue(mlBase is MultiLink, "Expected a MultiLink from repeated hook connections.");
                var ml = (MultiLink)mlBase!;
                Assert.AreEqual(3, ml.DestinationCount);

                // Initial order: [DestA, DestB, DestC]
                // Mark DestB and DestC as hidden.
                Assert.IsTrue(msSession.SetLinkDestinationHidden(user, ml, 1, true, out error), error?.Message);
                Assert.IsTrue(msSession.SetLinkDestinationHidden(user, ml, 2, true, out error), error?.Message);

                // Move DestC to front -> [DestC, DestA, DestB]
                // Hidden flags should move with destination identity -> [true, false, true]
                Assert.IsTrue(msSession.MoveLinkDestination(user, ml, 2, 0, out error), error?.Message);
                Assert.AreEqual("DestC", ml.Destinations[0].Name);
                Assert.AreEqual("DestA", ml.Destinations[1].Name);
                Assert.AreEqual("DestB", ml.Destinations[2].Name);
                Assert.IsTrue(ml.IsDestinationHidden(0));
                Assert.IsFalse(ml.IsDestinationHidden(1));
                Assert.IsTrue(ml.IsDestinationHidden(2));

                // Remove middle entry (DestA) -> [DestC, DestB]
                // Hidden flags must remain [true, true].
                Assert.IsTrue(msSession.RemoveLinkDestination(user, ml, 1, out error), error?.Message);
                Assert.AreEqual(2, ml.DestinationCount);
                Assert.AreEqual("DestC", ml.Destinations[0].Name);
                Assert.AreEqual("DestB", ml.Destinations[1].Name);
                Assert.IsTrue(ml.IsDestinationHidden(0));
                Assert.IsTrue(ml.IsDestinationHidden(1));
            }, (user, pSession, msSession) =>
            {
                var ms = msSession.ModelSystem;
                var execute = ms.GlobalBoundary.Modules.First(m => m.Name == "Execute");
                var ml = ms.GlobalBoundary.Links
                    .OfType<MultiLink>()
                    .First(l => ReferenceEquals(l.Origin, execute));

                Assert.AreEqual(2, ml.DestinationCount, "Unexpected number of destinations after reload.");
                Assert.AreEqual("DestC", ml.Destinations[0].Name, "Destination order lost across save/load.");
                Assert.AreEqual("DestB", ml.Destinations[1].Name, "Destination order lost across save/load.");
                Assert.IsTrue(ml.IsDestinationHidden(0), "Hidden-state mapping for DestC was not persisted correctly.");
                Assert.IsTrue(ml.IsDestinationHidden(1), "Hidden-state mapping for DestB was not persisted correctly.");
            });
        }

        [TestMethod]
        public void HideIncomingBranchesByDestinationTargetIsSingleUndoRedo()
        {
            TestHelper.RunInModelSystemContext("HideIncomingBranchesByDestinationTargetIsSingleUndoRedo", (user, pSession, msSession) =>
            {
                var ms = msSession.ModelSystem;
                CommandError error = null;

                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "StartA", Rectangle.Hidden,
                    out Start startA, out error), error?.Message);
                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "StartB", Rectangle.Hidden,
                    out Start startB, out error), error?.Message);
                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "StartC", Rectangle.Hidden,
                    out Start startC, out error), error?.Message);

                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "DestA", typeof(IgnoreResult<string>), Rectangle.Hidden,
                    out var destA, out error), error?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "DestB", typeof(IgnoreResult<string>), Rectangle.Hidden,
                    out var destB, out error), error?.Message);

                Assert.IsTrue(msSession.AddLink(user, startA, startA.Hooks[0], destA!, out var linkA, out error), error?.Message);
                Assert.IsTrue(msSession.AddLink(user, startB, startB.Hooks[0], destA!, out var linkB, out error), error?.Message);
                Assert.IsTrue(msSession.AddLink(user, startC, startC.Hooks[0], destB!, out var linkC, out error), error?.Message);

                var targets = new[]
                {
                    (linkA!, 0),
                    (linkB!, 0),
                };

                Assert.IsTrue(msSession.SetLinkDestinationBranchesHidden(user, targets, true, out error), error?.Message);
                Assert.IsTrue(linkA!.IsDestinationHidden(0));
                Assert.IsTrue(linkB!.IsDestinationHidden(0));
                Assert.IsFalse(linkC!.IsDestinationHidden(0));

                Assert.IsTrue(msSession.Undo(user, out error), error?.Message);
                Assert.IsFalse(linkA.IsDestinationHidden(0));
                Assert.IsFalse(linkB.IsDestinationHidden(0));
                Assert.IsFalse(linkC.IsDestinationHidden(0));

                Assert.IsTrue(msSession.Redo(user, out error), error?.Message);
                Assert.IsTrue(linkA.IsDestinationHidden(0));
                Assert.IsTrue(linkB.IsDestinationHidden(0));
                Assert.IsFalse(linkC.IsDestinationHidden(0));
            });
        }

        [TestMethod]
        public void DisabledLinkRunValidationFailure()
        {
            TestHelper.RunInModelSystemContext("DisabledLinkRunValidationFailure", (user, pSession, msSession) =>
            {
                CommandError error2 = null;
                var ms = msSession.ModelSystem;
                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", Rectangle.Hidden, out Start start, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "AnIgnore", typeof(IgnoreResult<string>), Rectangle.Hidden, out var ignoreMSS, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "SPM", typeof(SimpleParameterModule), Rectangle.Hidden, out var spm, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "MyParameter", typeof(BasicParameter<string>), Rectangle.Hidden, out var basicParameter, out error2), error2?.Message);
                Assert.IsTrue(msSession.SetParameterValue(user, basicParameter, "Hello World Parameter", out error2), error2?.Message);
                Assert.IsTrue(msSession.AddLink(user, start, start.Hooks[0], ignoreMSS, out var ignoreLink, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddLink(user, ignoreMSS, ignoreMSS.Hooks[0], spm, out var requiredLink, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddLink(user, spm, spm.Hooks[0], basicParameter, out var ignoreLink3, out error2), error2?.Message);
                Assert.IsTrue(msSession.SetLinkDisabled(user, requiredLink, true, out error2), error2?.Message);
                TestHelper.CreateRunClient(true, (runBus) =>
                {
                    CommandError error = null;
                    bool success = false;
                    using var sim = new SemaphoreSlim(0);
                    runBus.ClientFinishedModelSystem += (sender, e) =>
                    {
                        success = true;
                        sim.Release();
                    };
                    runBus.ClientErrorWhenRunningModelSystem += (sender, runId, e, stack, moduleName, elementId) =>
                    {
                        error = new CommandError(e + "\r\n" + stack);
                        sim.Release();
                    };
                    Assert.IsTrue(runBus.RunModelSystem(msSession, Path.Combine(pSession.RunsDirectory, "CreatingClient"), "Start", out var id, out error), error?.Message);
                    // give the models system some time to complete
                    if (!sim.Wait(2000))
                    {
                        Assert.Fail("The model system failed to execute in time!");
                    }
                    Assert.IsFalse(success, "The model system finished running instead of having a validation error!");
                });
            });
        }
    }
}
