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

namespace XTMF2.UnitTests.Editing
{
    [TestClass]
    public class TestCommentBlock
    {
        [TestMethod]
        public void TestCreatingCommentBlock()
        {
            TestHelper.RunInModelSystemContext("TestCreatingCommentBlock", (user, pSession, mSession) =>
            {
                string error = null;
                var ms = mSession.ModelSystem;
                var comment = "My Comment";
                var location = new Point(100, 100);
                var comBlocks = ms.GlobalBoundary.CommentBlocks;
                Assert.AreEqual(0, comBlocks.Count);
                Assert.IsTrue(mSession.AddCommentBlock(user, ms.GlobalBoundary, comment, location, out CommentBlock block, ref error), error);
                Assert.AreEqual(1, comBlocks.Count);
                Assert.AreEqual(comment, comBlocks[0].Comment);
                Assert.AreEqual(location, comBlocks[0].Location);
            });
        }

        [TestMethod]
        public void TestCreatingCommentBlockWithBadUser()
        {
            TestHelper.RunInModelSystemContext("TestCreatingCommentBlockWithBadUser", (user, unauthorizedUser, pSession, mSession) =>
            {
                string error = null;
                var ms = mSession.ModelSystem;
                var comment = "My Comment";
                var location = new Point(100, 100);
                var comBlocks = ms.GlobalBoundary.CommentBlocks;
                Assert.AreEqual(0, comBlocks.Count);
                Assert.IsFalse(mSession.AddCommentBlock(unauthorizedUser, ms.GlobalBoundary, comment, location, out CommentBlock block, ref error), error);
                Assert.AreEqual(0, comBlocks.Count);
            });
        }

        [TestMethod]
        public void TestCommentBlockPersistence()
        {
            var comment = "My Comment";
            var location = new Point(100, 100);
            TestHelper.RunInModelSystemContext("TestCommentBlockPersistence", (user, pSession, msSession) =>
            {
                string error = null;
                var ms = msSession.ModelSystem;
                var comBlocks = ms.GlobalBoundary.CommentBlocks;
                Assert.AreEqual(0, comBlocks.Count);
                Assert.IsTrue(msSession.AddCommentBlock(user, ms.GlobalBoundary, comment, location, out CommentBlock block, ref error), error);
                Assert.AreEqual(1, comBlocks.Count);
                Assert.AreEqual(comment, comBlocks[0].Comment);
                Assert.AreEqual(location, comBlocks[0].Location);
                Assert.IsTrue(msSession.Save(ref error), error);
            }, (user, pSession, msSession)=>
            {
                var ms = msSession.ModelSystem;
                var comBlocks = ms.GlobalBoundary.CommentBlocks;
                Assert.AreEqual(1, comBlocks.Count);
                Assert.AreEqual(comment, comBlocks[0].Comment);
                Assert.AreEqual(location, comBlocks[0].Location);
            });
        }

        [TestMethod]
        public void TestRemovingCommentBlock()
        {
            TestHelper.RunInModelSystemContext("TestRemovingCommentBlock", (user, pSession, msSession) =>
            {
                string error = null;
                var ms = msSession.ModelSystem;
                var comment = "My Comment";
                var location = new Point(100, 100);
                var comBlocks = ms.GlobalBoundary.CommentBlocks;
                Assert.AreEqual(0, comBlocks.Count);
                Assert.IsTrue(msSession.AddCommentBlock(user, ms.GlobalBoundary, comment, location, out CommentBlock block, ref error), error);
                Assert.AreEqual(1, comBlocks.Count);
                Assert.AreEqual(comment, comBlocks[0].Comment);
                Assert.AreEqual(location, comBlocks[0].Location);
                Assert.IsTrue(msSession.RemoveCommentBlock(user, ms.GlobalBoundary, block, ref error), error);
                Assert.AreEqual(0, comBlocks.Count);
            });
        }

        [TestMethod]
        public void TestRemovingCommentBlockWithBadUser()
        {
            TestHelper.RunInModelSystemContext("TestRemovingCommentBlockWithBadUser", (user, unauthorizedUser, pSession, msSession) =>
            {
                string error = null;
                var ms = msSession.ModelSystem;
                var comment = "My Comment";
                var location = new Point(100, 100);
                var comBlocks = ms.GlobalBoundary.CommentBlocks;
                Assert.AreEqual(0, comBlocks.Count);
                Assert.IsTrue(msSession.AddCommentBlock(user, ms.GlobalBoundary, comment, location, out CommentBlock block, ref error), error);
                Assert.AreEqual(1, comBlocks.Count);
                Assert.AreEqual(comment, comBlocks[0].Comment);
                Assert.AreEqual(location, comBlocks[0].Location);
                Assert.IsFalse(msSession.RemoveCommentBlock(unauthorizedUser, ms.GlobalBoundary, block, ref error), error);
                Assert.AreEqual(1, comBlocks.Count);
            });
        }

        [TestMethod]
        public void TestCreatingCommentBlockUndoRedo()
        {
            TestHelper.RunInModelSystemContext("TestCreatingCommentBlockUndoRedo", (user, pSession, msSession) =>
            {
                string error = null;
                var ms = msSession.ModelSystem;
                var comment = "My Comment";
                var location = new Point(100, 100);
                var comBlock = ms.GlobalBoundary.CommentBlocks;
                Assert.AreEqual(0, comBlock.Count);
                Assert.IsTrue(msSession.AddCommentBlock(user, ms.GlobalBoundary, comment, location, out CommentBlock block, ref error), error);
                Assert.AreEqual(1, comBlock.Count);
                Assert.AreEqual(comment, comBlock[0].Comment);
                Assert.AreEqual(location, comBlock[0].Location);
                Assert.IsTrue(msSession.Undo(user, ref error), error);
                Assert.AreEqual(0, comBlock.Count);
                Assert.IsTrue(msSession.Redo(user, ref error), error);
                Assert.AreEqual(1, comBlock.Count);
                Assert.AreEqual(comment, comBlock[0].Comment);
            });
        }

        [TestMethod]
        public void TestRemovingCommentBlockUndoRedo()
        {
            TestHelper.RunInModelSystemContext("TestRemovingCommentBlockUndoRedo", (user, pSession, msSession) =>
            {
                string error = null;
                var ms = msSession.ModelSystem;
                var comment = "My Comment";
                var location = new Point(100, 100);
                var comBlocks = ms.GlobalBoundary.CommentBlocks;
                Assert.AreEqual(0, comBlocks.Count);
                Assert.IsTrue(msSession.AddCommentBlock(user, ms.GlobalBoundary, comment, location, out CommentBlock block, ref error), error);
                Assert.AreEqual(1, comBlocks.Count);
                Assert.AreEqual(comment, comBlocks[0].Comment);
                Assert.AreEqual(location, comBlocks[0].Location);
                Assert.IsTrue(msSession.RemoveCommentBlock(user, ms.GlobalBoundary, block, ref error), error);
                Assert.AreEqual(0, comBlocks.Count);
                Assert.IsTrue(msSession.Undo(user, ref error), error);
                Assert.AreEqual(1, comBlocks.Count);
                Assert.AreEqual(comment, comBlocks[0].Comment);
                Assert.AreEqual(location, comBlocks[0].Location);
                Assert.IsTrue(msSession.Redo(user, ref error), error);
                Assert.AreEqual(0, comBlocks.Count);
            });
        }
    }
}
