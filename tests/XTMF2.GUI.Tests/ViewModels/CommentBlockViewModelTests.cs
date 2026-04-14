/*
    Copyright 2026 University of Toronto

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
using XTMF2.GUI.ViewModels;

namespace XTMF2.GUI.Tests.ViewModels;

[TestClass]
public class CommentBlockViewModelTests
{
    [TestMethod]
    public void DefaultDimensions_WhenUnderlyingIs0x0_Uses200x80()
    {
        TestGuiHelper.RunInModelSystemContext("CB_DefaultDimensions", (user, _, msSession) =>
        {
            CommandError? error = null;
            Assert.IsTrue(msSession.AddCommentBlock(user, msSession.ModelSystem.GlobalBoundary,
                "Test comment", new Rectangle(0, 0, 0, 0),
                out var block, out error), error?.Message);

            Assert.IsNotNull(block);

            var vm = TestGuiHelper.CreateCommentBlockViewModel(block, msSession, user);

            Assert.AreEqual(CommentBlockViewModel.DefaultWidth, vm.Width);
            Assert.AreEqual(CommentBlockViewModel.DefaultHeight, vm.Height);
        });
    }

    [TestMethod]
    public void ExplicitDimensions_WhenUnderlyingNonZero_UsesUnderlyingValue()
    {
        TestGuiHelper.RunInModelSystemContext("CB_ExplicitDimensions", (user, _, msSession) =>
        {
            CommandError? error = null;
            Assert.IsTrue(msSession.AddCommentBlock(user, msSession.ModelSystem.GlobalBoundary,
                "Test comment", new Rectangle(10, 20, 150, 60),
                out var block, out error), error?.Message);

            Assert.IsNotNull(block);

            var vm = TestGuiHelper.CreateCommentBlockViewModel(block, msSession, user);

            Assert.AreEqual(150.0, vm.Width);
            Assert.AreEqual(60.0, vm.Height);
        });
    }

    [TestMethod]
    public void CenterX_IsXPlusHalfWidth()
    {
        TestGuiHelper.RunInModelSystemContext("CB_CenterX", (user, _, msSession) =>
        {
            CommandError? error = null;
            Assert.IsTrue(msSession.AddCommentBlock(user, msSession.ModelSystem.GlobalBoundary,
                "Test comment", new Rectangle(100, 200, 200, 80),
                out var block, out error), error?.Message);

            Assert.IsNotNull(block);

            var vm = TestGuiHelper.CreateCommentBlockViewModel(block, msSession, user);

            Assert.AreEqual(100.0 + 200.0 / 2.0, vm.CenterX, 1e-9);
        });
    }

    [TestMethod]
    public void CenterY_IsYPlusHalfHeight()
    {
        TestGuiHelper.RunInModelSystemContext("CB_CenterY", (user, _, msSession) =>
        {
            CommandError? error = null;
            Assert.IsTrue(msSession.AddCommentBlock(user, msSession.ModelSystem.GlobalBoundary,
                "Test comment", new Rectangle(100, 200, 200, 80),
                out var block, out error), error?.Message);

            Assert.IsNotNull(block);

            var vm = TestGuiHelper.CreateCommentBlockViewModel(block, msSession, user);

            Assert.AreEqual(200.0 + 80.0 / 2.0, vm.CenterY, 1e-9);
        });
    }

    [TestMethod]
    public void MoveToPreview_UpdatesXAndY_WithoutCommit()
    {
        TestGuiHelper.RunInModelSystemContext("CB_MoveToPreview", (user, _, msSession) =>
        {
            CommandError? error = null;
            Assert.IsTrue(msSession.AddCommentBlock(user, msSession.ModelSystem.GlobalBoundary,
                "Test comment", new Rectangle(0, 0, 200, 80),
                out var block, out error), error?.Message);

            Assert.IsNotNull(block);

            var vm = TestGuiHelper.CreateCommentBlockViewModel(block, msSession, user);
            vm.MoveToPreview(50, 75);

            Assert.AreEqual(50.0, vm.X, 1e-9);
            Assert.AreEqual(75.0, vm.Y, 1e-9);
            // Underlying model should still be at (0, 0) until CommitMove
            Assert.AreEqual(0, block.Location.X);
            Assert.AreEqual(0, block.Location.Y);
        });
    }

    [TestMethod]
    public void ResizeToPreview_SetsPreviewDimensions()
    {
        TestGuiHelper.RunInModelSystemContext("CB_ResizeToPreview", (user, _, msSession) =>
        {
            CommandError? error = null;
            Assert.IsTrue(msSession.AddCommentBlock(user, msSession.ModelSystem.GlobalBoundary,
                "Test comment", new Rectangle(0, 0, 0, 0),
                out var block, out error), error?.Message);

            Assert.IsNotNull(block);

            var vm = TestGuiHelper.CreateCommentBlockViewModel(block, msSession, user);
            vm.ResizeToPreview(300, 100);

            Assert.AreEqual(300.0, vm.Width, 1e-9);
            Assert.AreEqual(100.0, vm.Height, 1e-9);
        });
    }

    [TestMethod]
    public void IsSelectedDefaultsFalse()
    {
        TestGuiHelper.RunInModelSystemContext("CB_IsSelectedDefault", (user, _, msSession) =>
        {
            CommandError? error = null;
            Assert.IsTrue(msSession.AddCommentBlock(user, msSession.ModelSystem.GlobalBoundary,
                "Test", new Rectangle(0, 0, 0, 0),
                out var block, out error), error?.Message);
            
            Assert.IsNotNull(block);

            var vm = TestGuiHelper.CreateCommentBlockViewModel(block, msSession, user);

            Assert.IsFalse(vm.IsSelected);
        });
    }

    [TestMethod]
    public void Name_ReflectsUnderlyingComment()
    {
        TestGuiHelper.RunInModelSystemContext("CB_Name", (user, _, msSession) =>
        {
            CommandError? error = null;
            Assert.IsTrue(msSession.AddCommentBlock(user, msSession.ModelSystem.GlobalBoundary,
                "MyComment", new Rectangle(0, 0, 0, 0),
                out var block, out error), error?.Message);
            
            Assert.IsNotNull(block);

            var vm = TestGuiHelper.CreateCommentBlockViewModel(block, msSession, user);

            Assert.AreEqual("MyComment", vm.Name);
        });
    }
}
