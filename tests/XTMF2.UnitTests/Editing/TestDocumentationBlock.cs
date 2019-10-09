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
using XTMF2.ModelSystemConstruct;

namespace XTMF2.Editing
{
    [TestClass]
    public class TestCommentBlock
    {
        [TestMethod]
        public void TestCreatingCommentBlock()
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
                    var comment = "My Comment";
                    var location = new Point(100, 100);
                    var comBlocks = ms.GlobalBoundary.CommentBlocks;
                    Assert.AreEqual(0, comBlocks.Count);
                    Assert.IsTrue(msSession.AddCommentBlock(localUser, ms.GlobalBoundary, comment, location, out CommentBlock block, ref error), error);
                    Assert.AreEqual(1, comBlocks.Count);
                    Assert.AreEqual(comment, comBlocks[0].Comment);
                    Assert.AreEqual(location, comBlocks[0].Location);
                }), "Unable to get a model system editing session!");
            }), "Unable to create project");
        }

        [TestMethod]
        public void TestCommentBlockPersistence()
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var controller = runtime.ProjectController;
            string error = null;
            var localUser = runtime.UserController.Users[0];
            const string projectName = "Test";
            const string modelSystemName = "TestMS";
            controller.DeleteProject(localUser, projectName, ref error);
            var comment = "My Comment";
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
                        var comBlocks = ms.GlobalBoundary.CommentBlocks;
                        Assert.AreEqual(0, comBlocks.Count);
                        Assert.IsTrue(msSession.AddCommentBlock(localUser, ms.GlobalBoundary, comment, location, out CommentBlock block, ref error), error);
                        Assert.AreEqual(1, comBlocks.Count);
                        Assert.AreEqual(comment, comBlocks[0].Comment);
                        Assert.AreEqual(location, comBlocks[0].Location);
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
                        var comBlocks = ms.GlobalBoundary.CommentBlocks;
                        Assert.AreEqual(1, comBlocks.Count);
                        Assert.AreEqual(comment, comBlocks[0].Comment);
                        Assert.AreEqual(location, comBlocks[0].Location);
                    }), "Unable to get a model system editing session.");
                }), "Unable to get a project editing session.");
            }
        }

        [TestMethod]
        public void TestRemovingCommentBlock()
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
                    var comment = "My Comment";
                    var location = new Point(100, 100);
                    var comBlocks = ms.GlobalBoundary.CommentBlocks;
                    Assert.AreEqual(0, comBlocks.Count);
                    Assert.IsTrue(msSession.AddCommentBlock(localUser, ms.GlobalBoundary, comment, location, out CommentBlock block, ref error), error);
                    Assert.AreEqual(1, comBlocks.Count);
                    Assert.AreEqual(comment, comBlocks[0].Comment);
                    Assert.AreEqual(location, comBlocks[0].Location);
                    Assert.IsTrue(msSession.RemoveCommentBlock(localUser, ms.GlobalBoundary, block, ref error), error);
                    Assert.AreEqual(0, comBlocks.Count);
                }), "Unable to get a model system editing session!");
            }), "Unable to create project");
        }

        [TestMethod]
        public void TestCreatingCommentBlockUndoRedo()
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
                    var comment = "My Comment";
                    var location = new Point(100, 100);
                    var comBlock = ms.GlobalBoundary.CommentBlocks;
                    Assert.AreEqual(0, comBlock.Count);
                    Assert.IsTrue(msSession.AddCommentBlock(localUser, ms.GlobalBoundary, comment, location, out CommentBlock block, ref error), error);
                    Assert.AreEqual(1, comBlock.Count);
                    Assert.AreEqual(comment, comBlock[0].Comment);
                    Assert.AreEqual(location, comBlock[0].Location);
                    Assert.IsTrue(msSession.Undo(localUser, ref error), error);
                    Assert.AreEqual(0, comBlock.Count);
                    Assert.IsTrue(msSession.Redo(localUser, ref error), error);
                    Assert.AreEqual(1, comBlock.Count);
                    Assert.AreEqual(comment, comBlock[0].Comment);
                    Assert.AreEqual(location, comBlock[0].Location);
                }), "Unable to get a model system editing session!");
            }), "Unable to create project");
        }

        [TestMethod]
        public void TestRemovingCommentBlockUndoRedo()
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
                    var comment = "My Comment";
                    var location = new Point(100, 100);
                    var comBlocks = ms.GlobalBoundary.CommentBlocks;
                    Assert.AreEqual(0, comBlocks.Count);
                    Assert.IsTrue(msSession.AddCommentBlock(localUser, ms.GlobalBoundary, comment, location, out CommentBlock block, ref error), error);
                    Assert.AreEqual(1, comBlocks.Count);
                    Assert.AreEqual(comment, comBlocks[0].Comment);
                    Assert.AreEqual(location, comBlocks[0].Location);
                    Assert.IsTrue(msSession.RemoveCommentBlock(localUser, ms.GlobalBoundary, block, ref error), error);
                    Assert.AreEqual(0, comBlocks.Count);
                    Assert.IsTrue(msSession.Undo(localUser, ref error), error);
                    Assert.AreEqual(1, comBlocks.Count);
                    Assert.AreEqual(comment, comBlocks[0].Comment);
                    Assert.AreEqual(location, comBlocks[0].Location);
                    Assert.IsTrue(msSession.Redo(localUser, ref error), error);
                    Assert.AreEqual(0, comBlocks.Count);
                }), "Unable to get a model system editing session!");
            }), "Unable to create project");
        }
    }
}
