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

namespace XTMF2.UnitTests.Editing
{
    [TestClass]
    public class TestDocumentationBlock
    {
        [TestMethod]
        public void TestCreatingDocumentationBlock()
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var controller = runtime.ProjectController;
            string error = null;
            var localUser = runtime.UserController.Users[0];
            controller.DeleteProject(localUser, "Test", ref error);
            Assert.IsTrue(controller.CreateNewProject(localUser, "Test", out ProjectSession session, ref error).UsingIf(session, () =>
            {
                var project = session.Project;
                Assert.IsTrue(session.CreateNewModelSystem("TestMS", out var modelSystem, ref error), error);
                Assert.IsTrue(session.EditModelSystem(localUser, modelSystem, out var msSession, ref error).UsingIf(msSession, () =>
                {
                    var ms = msSession.ModelSystem;
                    var documentation = "My Documentation";
                    var location = new Point(100, 100);
                    var docBlocks = ms.GlobalBoundary.DocumentationBlocks;
                    Assert.AreEqual(0, docBlocks.Count);
                    Assert.IsTrue(msSession.AddDocumentationBlock(localUser, ms.GlobalBoundary, documentation, location, out DocumentationBlock block, ref error), error);
                    Assert.AreEqual(1, docBlocks.Count);
                    Assert.AreEqual(documentation, docBlocks[0].Documentation);
                    Assert.AreEqual(location, docBlocks[0].Location);
                }), "Unable to get a model system editing session!");
            }), "Unable to create project");
        }

        [TestMethod]
        public void TestDocumentationBlockPersistence()
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var controller = runtime.ProjectController;
            string error = null;
            var localUser = runtime.UserController.Users[0];
            const string projectName = "Test";
            const string modelSystemName = "TestMS";
            controller.DeleteProject(localUser, projectName, ref error);
            var documentation = "My Documentation";
            var location = new Point(100, 100);
            // first pass
            {
                Assert.IsTrue(controller.CreateNewProject(localUser, projectName, out ProjectSession session, ref error).UsingIf(session, () =>
                {
                    var project = session.Project;
                    Assert.IsTrue(session.CreateNewModelSystem("TestMS", out var modelSystem, ref error), error);
                    Assert.IsTrue(session.EditModelSystem(localUser, modelSystem, out var msSession, ref error).UsingIf(msSession, () =>
                    {
                        var ms = msSession.ModelSystem;
                        var docBlocks = ms.GlobalBoundary.DocumentationBlocks;
                        Assert.AreEqual(0, docBlocks.Count);
                        Assert.IsTrue(msSession.AddDocumentationBlock(localUser, ms.GlobalBoundary, documentation, location, out DocumentationBlock block, ref error), error);
                        Assert.AreEqual(1, docBlocks.Count);
                        Assert.AreEqual(documentation, docBlocks[0].Documentation);
                        Assert.AreEqual(location, docBlocks[0].Location);
                        Assert.IsTrue(msSession.Save(ref error), error);
                    }), "Unable to get a model system editing session!");
                }), "Unable to create project");
            }
            // second pass
            {
                Assert.IsTrue(controller.GetProject(localUser, projectName, out var project, ref error));
                Assert.IsTrue(controller.GetProjectSession(localUser, project, out var session, ref error).UsingIf(session, () =>
                {
                    Assert.IsTrue(session.GetModelSystemHeader(localUser, modelSystemName, out var modelSystem, ref error), error);
                    Assert.IsTrue(session.EditModelSystem(localUser, modelSystem, out var msSession, ref error).UsingIf(msSession, () =>
                    {
                        var ms = msSession.ModelSystem;
                        var docBlocks = ms.GlobalBoundary.DocumentationBlocks;
                        Assert.AreEqual(1, docBlocks.Count);
                        Assert.AreEqual(documentation, docBlocks[0].Documentation);
                        Assert.AreEqual(location, docBlocks[0].Location);
                    }), "Unable to get a model system editing session.");
                }), "Unable to get a project editing session.");
            }
        }

        [TestMethod]
        public void TestRemovingDocumentationBlock()
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var controller = runtime.ProjectController;
            string error = null;
            var localUser = runtime.UserController.Users[0];
            controller.DeleteProject(localUser, "Test", ref error);
            Assert.IsTrue(controller.CreateNewProject(localUser, "Test", out ProjectSession session, ref error).UsingIf(session, () =>
            {
                var project = session.Project;
                Assert.IsTrue(session.CreateNewModelSystem("TestMS", out var modelSystem, ref error), error);
                Assert.IsTrue(session.EditModelSystem(localUser, modelSystem, out var msSession, ref error).UsingIf(msSession, () =>
                {
                    var ms = msSession.ModelSystem;
                    var documentation = "My Documentation";
                    var location = new Point(100, 100);
                    var docBlocks = ms.GlobalBoundary.DocumentationBlocks;
                    Assert.AreEqual(0, docBlocks.Count);
                    Assert.IsTrue(msSession.AddDocumentationBlock(localUser, ms.GlobalBoundary, documentation, location, out DocumentationBlock block, ref error), error);
                    Assert.AreEqual(1, docBlocks.Count);
                    Assert.AreEqual(documentation, docBlocks[0].Documentation);
                    Assert.AreEqual(location, docBlocks[0].Location);
                    Assert.IsTrue(msSession.RemoveDocumentationBlock(localUser, ms.GlobalBoundary, block, ref error), error);
                    Assert.AreEqual(0, docBlocks.Count);
                }), "Unable to get a model system editing session!");
            }), "Unable to create project");
        }
    }
}
