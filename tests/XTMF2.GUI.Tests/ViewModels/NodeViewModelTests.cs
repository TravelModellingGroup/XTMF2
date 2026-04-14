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
using XTMF2.GUI.Tests.Modules;
using XTMF2.RuntimeModules;

namespace XTMF2.GUI.Tests.ViewModels;

[TestClass]
public class NodeViewModelTests
{
    [TestMethod]
    public void DefaultDimensions_WhenUnderlyingIs0x0_Uses120x50()
    {
        TestGuiHelper.RunInModelSystemContext("Node_DefaultDimensions", (user, _, msSession) =>
        {
            CommandError? error = null;
            Assert.IsTrue(msSession.AddNode(user, msSession.ModelSystem.GlobalBoundary,
                "TestNode", typeof(SimpleGuiTestModule), new Rectangle(0, 0, 0, 0),
                out var node, out error), error?.Message);

            var vm = TestGuiHelper.CreateNodeViewModel(node!, msSession, user);

            Assert.AreEqual(120.0, vm.Width);
            Assert.AreEqual(50.0, vm.Height);
        });
    }

    [TestMethod]
    public void Width_WhenUnderlyingNonZero_UsesUnderlyingValue()
    {
        TestGuiHelper.RunInModelSystemContext("Node_ExplicitWidth", (user, _, msSession) =>
        {
            CommandError? error = null;
            Assert.IsTrue(msSession.AddNode(user, msSession.ModelSystem.GlobalBoundary,
                "TestNode", typeof(SimpleGuiTestModule), new Rectangle(0, 0, 200, 80),
                out var node, out error), error?.Message);

            var vm = TestGuiHelper.CreateNodeViewModel(node!, msSession, user);

            Assert.AreEqual(200.0, vm.Width);
            Assert.AreEqual(80.0, vm.Height);
        });
    }

    [TestMethod]
    public void CenterX_IsXPlusHalfWidth()
    {
        TestGuiHelper.RunInModelSystemContext("Node_CenterX", (user, _, msSession) =>
        {
            CommandError? error = null;
            Assert.IsTrue(msSession.AddNode(user, msSession.ModelSystem.GlobalBoundary,
"TestNode", typeof(SimpleGuiTestModule), new Rectangle(100, 50, 200, 80),
                out var node, out error), error?.Message);

            var vm = TestGuiHelper.CreateNodeViewModel(node!, msSession, user);

            Assert.AreEqual(100.0 + 200.0 / 2.0, vm.CenterX, 1e-9);
        });
    }

    [TestMethod]
    public void CenterY_IsYPlusHalfHeight()
    {
        TestGuiHelper.RunInModelSystemContext("Node_CenterY", (user, _, msSession) =>
        {
            CommandError? error = null;
            Assert.IsTrue(msSession.AddNode(user, msSession.ModelSystem.GlobalBoundary,
"TestNode", typeof(SimpleGuiTestModule), new Rectangle(100, 50, 200, 80),
                out var node, out error), error?.Message);

            var vm = TestGuiHelper.CreateNodeViewModel(node!, msSession, user);

            Assert.AreEqual(50.0 + 80.0 / 2.0, vm.CenterY, 1e-9);
        });
    }

    [TestMethod]
    public void IsParameterNode_ForBasicParameterFloat_ReturnsTrue()
    {
        TestGuiHelper.RunInModelSystemContext("Node_IsParameterNode_True", (user, _, msSession) =>
        {
            CommandError? error = null;
            Assert.IsTrue(msSession.AddNode(user, msSession.ModelSystem.GlobalBoundary,
                "ParamNode", typeof(BasicParameter<float>), new Rectangle(0, 0, 120, 50),
                out var node, out error), error?.Message);

            var vm = TestGuiHelper.CreateNodeViewModel(node!, msSession, user);

            Assert.IsTrue(vm.IsParameterNode);
        });
    }

    [TestMethod]
    public void IsParameterNode_ForNonParameterType_ReturnsFalse()
    {
        TestGuiHelper.RunInModelSystemContext("Node_IsParameterNode_False", (user, _, msSession) =>
        {
            CommandError? error = null;
            Assert.IsTrue(msSession.AddNode(user, msSession.ModelSystem.GlobalBoundary,
                "TestNode", typeof(SimpleGuiTestModule), new Rectangle(0, 0, 120, 50),
                out var node, out error), error?.Message);

            var vm = TestGuiHelper.CreateNodeViewModel(node!, msSession, user);

            Assert.IsFalse(vm.IsParameterNode);
        });
    }

    [TestMethod]
    public void IsInlined_WhenLocationIsRectangleHidden_ReturnsTrue()
    {
        TestGuiHelper.RunInModelSystemContext("Node_IsInlined", (user, _, msSession) =>
        {
            CommandError? error = null;
            Assert.IsTrue(msSession.AddNode(user, msSession.ModelSystem.GlobalBoundary,
                "ParamNode", typeof(BasicParameter<float>), Rectangle.Hidden,
                out var node, out error), error?.Message);

            var vm = TestGuiHelper.CreateNodeViewModel(node!, msSession, user);

            Assert.IsTrue(vm.IsInlined);
        });
    }

    [TestMethod]
    public void IsInlined_WhenLocationIsVisible_ReturnsFalse()
    {
        TestGuiHelper.RunInModelSystemContext("Node_IsNotInlined", (user, _, msSession) =>
        {
            CommandError? error = null;
            Assert.IsTrue(msSession.AddNode(user, msSession.ModelSystem.GlobalBoundary,
                "TestNode", typeof(SimpleGuiTestModule), new Rectangle(10, 10, 120, 50),
                out var node, out error), error?.Message);

            var vm = TestGuiHelper.CreateNodeViewModel(node!, msSession, user);

            Assert.IsFalse(vm.IsInlined);
        });
    }

    [TestMethod]
    public void MoveToPreview_SetsCenterCoordinates_WithoutCommit()
    {
        TestGuiHelper.RunInModelSystemContext("Node_MoveToPreview", (user, _, msSession) =>
        {
            CommandError? error = null;
            Assert.IsTrue(msSession.AddNode(user, msSession.ModelSystem.GlobalBoundary,
                "TestNode", typeof(SimpleGuiTestModule), new Rectangle(0, 0, 120, 50),
                out var node, out error), error?.Message);

            var vm = TestGuiHelper.CreateNodeViewModel(node!, msSession, user);
            vm.MoveToPreview(200, 300);

            Assert.AreEqual(200.0, vm.X, 1e-9);
            Assert.AreEqual(300.0, vm.Y, 1e-9);
            // Model should still be at original location
            Assert.AreEqual(0, node!.Location.X);
            Assert.AreEqual(0, node.Location.Y);
        });
    }

    [TestMethod]
    public void SetName_PropagatesViaSession()
    {
        TestGuiHelper.RunInModelSystemContext("Node_SetName", (user, _, msSession) =>
        {
            CommandError? error = null;
            Assert.IsTrue(msSession.AddNode(user, msSession.ModelSystem.GlobalBoundary,
                "OldName", typeof(SimpleGuiTestModule), new Rectangle(0, 0, 120, 50),
                out var node, out error), error?.Message);

            var vm = TestGuiHelper.CreateNodeViewModel(node!, msSession, user);
            Assert.IsTrue(vm.SetName("NewName", out error), error?.Message);

            Assert.AreEqual("NewName", vm.Name);
            Assert.AreEqual("NewName", node!.Name);
        });
    }

    [TestMethod]
    public void Name_DefaultsToNodeName()
    {
        TestGuiHelper.RunInModelSystemContext("Node_DefaultName", (user, _, msSession) =>
        {
            CommandError? error = null;
            Assert.IsTrue(msSession.AddNode(user, msSession.ModelSystem.GlobalBoundary,
                "MyNode", typeof(SimpleGuiTestModule), new Rectangle(0, 0, 120, 50),
                out var node, out error), error?.Message);

            var vm = TestGuiHelper.CreateNodeViewModel(node!, msSession, user);

            Assert.AreEqual("MyNode", vm.Name);
        });
    }
}
