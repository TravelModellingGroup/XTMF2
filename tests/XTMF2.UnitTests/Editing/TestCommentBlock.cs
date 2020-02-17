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
using XTMF2.Editing;
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
                CommandError error = null;
                var ms = mSession.ModelSystem;
                var comment = "My Comment";
                var location = new Rectangle(100, 100);
                var comBlocks = ms.GlobalBoundary.CommentBlocks;
                Assert.AreEqual(0, comBlocks.Count);
                Assert.IsTrue(mSession.AddCommentBlock(user, ms.GlobalBoundary, comment, location, out CommentBlock block, out error), error?.Message);
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
                CommandError error = null;
                var ms = mSession.ModelSystem;
                var comment = "My Comment";
                var location = new Rectangle(100, 100);
                var comBlocks = ms.GlobalBoundary.CommentBlocks;
                Assert.AreEqual(0, comBlocks.Count);
                Assert.IsFalse(mSession.AddCommentBlock(unauthorizedUser, ms.GlobalBoundary, comment, location, out CommentBlock block, out error), error?.Message);
                Assert.AreEqual(0, comBlocks.Count);
            });
        }

        [TestMethod]
        public void TestCommentBlockPersistence()
        {
            var comment = "My Comment";
            var location = new Rectangle(100, 100);
            TestHelper.RunInModelSystemContext("TestCommentBlockPersistence", (user, pSession, msSession) =>
            {
                CommandError error = null;
                var ms = msSession.ModelSystem;
                var comBlocks = ms.GlobalBoundary.CommentBlocks;
                Assert.AreEqual(0, comBlocks.Count);
                Assert.IsTrue(msSession.AddCommentBlock(user, ms.GlobalBoundary, comment, location, out CommentBlock block, out error), error?.Message);
                Assert.AreEqual(1, comBlocks.Count);
                Assert.AreEqual(comment, comBlocks[0].Comment);
                Assert.AreEqual(location, comBlocks[0].Location);
                Assert.IsTrue(msSession.Save(out error), error?.Message);
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
                CommandError error = null;
                var ms = msSession.ModelSystem;
                var comment = "My Comment";
                var location = new Rectangle(100, 100);
                var comBlocks = ms.GlobalBoundary.CommentBlocks;
                Assert.AreEqual(0, comBlocks.Count);
                Assert.IsTrue(msSession.AddCommentBlock(user, ms.GlobalBoundary, comment, location, out CommentBlock block, out error), error?.Message);
                Assert.AreEqual(1, comBlocks.Count);
                Assert.AreEqual(comment, comBlocks[0].Comment);
                Assert.AreEqual(location, comBlocks[0].Location);
                Assert.IsTrue(msSession.RemoveCommentBlock(user, ms.GlobalBoundary, block, out error), error?.Message);
                Assert.AreEqual(0, comBlocks.Count);
            });
        }

        [TestMethod]
        public void TestRemovingCommentBlockWithBadUser()
        {
            TestHelper.RunInModelSystemContext("TestRemovingCommentBlockWithBadUser", (user, unauthorizedUser, pSession, msSession) =>
            {
                CommandError error = null;
                var ms = msSession.ModelSystem;
                var comment = "My Comment";
                var location = new Rectangle(100, 100);
                var comBlocks = ms.GlobalBoundary.CommentBlocks;
                Assert.AreEqual(0, comBlocks.Count);
                Assert.IsTrue(msSession.AddCommentBlock(user, ms.GlobalBoundary, comment, location, out CommentBlock block, out error), error?.Message);
                Assert.AreEqual(1, comBlocks.Count);
                Assert.AreEqual(comment, comBlocks[0].Comment);
                Assert.AreEqual(location, comBlocks[0].Location);
                Assert.IsFalse(msSession.RemoveCommentBlock(unauthorizedUser, ms.GlobalBoundary, block, out error), error?.Message);
                Assert.AreEqual(1, comBlocks.Count);
            });
        }

        [TestMethod]
        public void TestCreatingCommentBlockUndoRedo()
        {
            TestHelper.RunInModelSystemContext("TestCreatingCommentBlockUndoRedo", (user, pSession, msSession) =>
            {
                CommandError error = null;
                var ms = msSession.ModelSystem;
                var comment = "My Comment";
                var location = new Rectangle(100, 100);
                var comBlock = ms.GlobalBoundary.CommentBlocks;
                Assert.AreEqual(0, comBlock.Count);
                Assert.IsTrue(msSession.AddCommentBlock(user, ms.GlobalBoundary, comment, location, out CommentBlock block, out error), error?.Message);
                Assert.AreEqual(1, comBlock.Count);
                Assert.AreEqual(comment, comBlock[0].Comment);
                Assert.AreEqual(location, comBlock[0].Location);
                Assert.IsTrue(msSession.Undo(user, out error), error?.Message);
                Assert.AreEqual(0, comBlock.Count);
                Assert.IsTrue(msSession.Redo(user, out error), error?.Message);
                Assert.AreEqual(1, comBlock.Count);
                Assert.AreEqual(comment, comBlock[0].Comment);
            });
        }

        [TestMethod]
        public void TestRemovingCommentBlockUndoRedo()
        {
            TestHelper.RunInModelSystemContext("TestRemovingCommentBlockUndoRedo", (user, pSession, msSession) =>
            {
                CommandError error = null;
                var ms = msSession.ModelSystem;
                var comment = "My Comment";
                var location = new Rectangle(100, 100);
                var comBlocks = ms.GlobalBoundary.CommentBlocks;
                Assert.AreEqual(0, comBlocks.Count);
                Assert.IsTrue(msSession.AddCommentBlock(user, ms.GlobalBoundary, comment, location, out CommentBlock block, out error), error?.Message);
                Assert.AreEqual(1, comBlocks.Count);
                Assert.AreEqual(comment, comBlocks[0].Comment);
                Assert.AreEqual(location, comBlocks[0].Location);
                Assert.IsTrue(msSession.RemoveCommentBlock(user, ms.GlobalBoundary, block, out error), error?.Message);
                Assert.AreEqual(0, comBlocks.Count);
                Assert.IsTrue(msSession.Undo(user, out error), error?.Message);
                Assert.AreEqual(1, comBlocks.Count);
                Assert.AreEqual(comment, comBlocks[0].Comment);
                Assert.AreEqual(location, comBlocks[0].Location);
                Assert.IsTrue(msSession.Redo(user, out error), error?.Message);
                Assert.AreEqual(0, comBlocks.Count);
            });
        }

        [TestMethod]
        public void TestChangingCommentBlockText()
        {
            TestHelper.RunInModelSystemContext("TestChangingCommentBlockText", (user, pSession, msSession) =>
            {
                CommandError error = null;
                var ms = msSession.ModelSystem;
                var comment = "My Comment";
                var newComment = "New comment";
                var location = new Rectangle(100, 100);
                var comBlocks = ms.GlobalBoundary.CommentBlocks;
                Assert.AreEqual(0, comBlocks.Count);
                Assert.IsTrue(msSession.AddCommentBlock(user, ms.GlobalBoundary, comment, location, out CommentBlock block, out error), error?.Message);
                Assert.AreEqual(1, comBlocks.Count);
                Assert.AreEqual(comment, comBlocks[0].Comment);
                Assert.IsTrue(msSession.SetCommentBlockText(user, block, newComment, out error), error?.Message);
                Assert.AreEqual(newComment, block.Comment, "The comment block's text was not set!");
                Assert.IsTrue(msSession.Undo(user, out error), error?.Message);
                Assert.AreEqual(comment, block.Comment, "The comment block's text was not undone!");
                Assert.IsTrue(msSession.Redo(user, out error), error?.Message);
                Assert.AreEqual(newComment, block.Comment);
            });
        }

        [TestMethod]
        public void TestChangingCommentBlockPosition()
        {
            TestHelper.RunInModelSystemContext("TestChangingCommentBlockPosition", (user, pSession, msSession) =>
            {
                CommandError error = null;
                var ms = msSession.ModelSystem;
                var comment = "My Comment";
                var location = new Rectangle(100, 100);
                var newLocation = new Rectangle(100, 200);
                var comBlocks = ms.GlobalBoundary.CommentBlocks;
                Assert.AreEqual(0, comBlocks.Count);
                Assert.IsTrue(msSession.AddCommentBlock(user, ms.GlobalBoundary, comment, location, out CommentBlock block, out error), error?.Message);
                Assert.AreEqual(1, comBlocks.Count);
                Assert.AreEqual(comment, comBlocks[0].Comment);
                Assert.IsTrue(msSession.SetCommentBlockLocation(user, block, newLocation, out error), error?.Message);
                Assert.AreEqual(newLocation, block.Location, "The comment block's location was not set!");
                Assert.IsTrue(msSession.Undo(user, out error), error?.Message);
                Assert.AreEqual(location, block.Location, "The comment block's location was not undone!");
                Assert.IsTrue(msSession.Redo(user, out error), error?.Message);
                Assert.AreEqual(newLocation, block.Location);
            });
        }
    }
}
