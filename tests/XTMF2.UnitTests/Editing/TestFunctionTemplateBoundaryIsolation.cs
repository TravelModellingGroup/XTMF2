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
    along with XTMF2.  If not, see <http://www.gnu.org/licenses/\>.
*/
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Editing;
using XTMF2.ModelSystemConstruct;
using XTMF2.UnitTests.Modules;

namespace XTMF2.UnitTests.Editing
{
    /// <summary>
    /// Verifies that nodes and ghost nodes cannot be moved across the boundary that
    /// separates a FunctionTemplate's InternalModules scope from the global scope.
    /// </summary>
    [TestClass]
    public class TestFunctionTemplateBoundaryIsolation
    {
        // ── Helpers ────────────────────────────────────────────────────────────

        private static FunctionTemplate AddTemplate(User user, ModelSystemSession ms,
            Boundary boundary, string name = "MyTemplate")
        {
            Assert.IsTrue(ms.AddFunctionTemplate(user, boundary, name, out var ft, out var err),
                err?.Message);
            return ft!;
        }

        private static Boundary AddChildBoundary(User user, ModelSystemSession ms,
            Boundary parent, string name)
        {
            Assert.IsTrue(ms.AddBoundary(user, parent, name, out var b, out var err), err?.Message);
            return b!;
        }

        private static Node AddNode(User user, ModelSystemSession ms, Boundary boundary,
            string name = "TestNode")
        {
            Assert.IsTrue(ms.AddNode(user, boundary, name, typeof(SimpleTestModule),
                new Rectangle(10f, 10f, 120f, 50f), out var node, out var err), err?.Message);
            return node!;
        }

        // ── MoveNodeToBoundary ─────────────────────────────────────────────────

        [TestMethod]
        public void TestMoveNode_FunctionTemplateInternal_ToGlobal_Rejected()
        {
            TestHelper.RunInModelSystemContext(nameof(TestMoveNode_FunctionTemplateInternal_ToGlobal_Rejected),
            (user, pSession, ms) =>
            {
                var gb = ms.ModelSystem.GlobalBoundary;
                var ft = AddTemplate(user, ms, gb);
                var node = AddNode(user, ms, ft.InternalModules, "InternalNode");

                var ok = ms.MoveNodeToBoundary(user, node, gb, out var error);
                Assert.IsFalse(ok, "Expected move out of FunctionTemplate to be rejected.");
                Assert.IsNotNull(error);
                // Node must remain inside the FunctionTemplate.
                Assert.AreSame(ft.InternalModules, node.ContainedWithin);
            });
        }

        [TestMethod]
        public void TestMoveNode_Global_ToFunctionTemplateInternal_Rejected()
        {
            TestHelper.RunInModelSystemContext(nameof(TestMoveNode_Global_ToFunctionTemplateInternal_Rejected),
            (user, pSession, ms) =>
            {
                var gb = ms.ModelSystem.GlobalBoundary;
                var ft = AddTemplate(user, ms, gb);
                var node = AddNode(user, ms, gb, "GlobalNode");

                var ok = ms.MoveNodeToBoundary(user, node, ft.InternalModules, out var error);
                Assert.IsFalse(ok, "Expected move into FunctionTemplate from global scope to be rejected.");
                Assert.IsNotNull(error);
                Assert.AreSame(gb, node.ContainedWithin);
            });
        }

        [TestMethod]
        public void TestMoveNode_BetweenTwoFunctionTemplates_Rejected()
        {
            TestHelper.RunInModelSystemContext(nameof(TestMoveNode_BetweenTwoFunctionTemplates_Rejected),
            (user, pSession, ms) =>
            {
                var gb  = ms.ModelSystem.GlobalBoundary;
                var ftA = AddTemplate(user, ms, gb, "FtA");
                var ftB = AddTemplate(user, ms, gb, "FtB");
                var node = AddNode(user, ms, ftA.InternalModules, "InternalNode");

                var ok = ms.MoveNodeToBoundary(user, node, ftB.InternalModules, out var error);
                Assert.IsFalse(ok, "Expected move between two FunctionTemplates to be rejected.");
                Assert.IsNotNull(error);
                Assert.AreSame(ftA.InternalModules, node.ContainedWithin);
            });
        }

        [TestMethod]
        public void TestMoveNode_WithinFunctionTemplateChildBoundary_Accepted()
        {
            TestHelper.RunInModelSystemContext(nameof(TestMoveNode_WithinFunctionTemplateChildBoundary_Accepted),
            (user, pSession, ms) =>
            {
                var gb = ms.ModelSystem.GlobalBoundary;
                var ft = AddTemplate(user, ms, gb);
                // Add a child boundary inside the FunctionTemplate's InternalModules.
                var internalChild = AddChildBoundary(user, ms, ft.InternalModules, "InternalChild");
                var node = AddNode(user, ms, ft.InternalModules, "InternalNode");

                // Moving between InternalModules and its child must succeed (same FT scope).
                var ok = ms.MoveNodeToBoundary(user, node, internalChild, out var error);
                Assert.IsTrue(ok, error?.Message);
                Assert.AreSame(internalChild, node.ContainedWithin);
            });
        }

        [TestMethod]
        public void TestMoveNode_Global_ToGlobalChildBoundary_Accepted()
        {
            TestHelper.RunInModelSystemContext(nameof(TestMoveNode_Global_ToGlobalChildBoundary_Accepted),
            (user, pSession, ms) =>
            {
                var gb    = ms.ModelSystem.GlobalBoundary;
                var child = AddChildBoundary(user, ms, gb, "ChildA");
                var node  = AddNode(user, ms, gb, "GlobalNode");

                // Moving within the global scope (no FunctionTemplate involved) must still work.
                var ok = ms.MoveNodeToBoundary(user, node, child, out var error);
                Assert.IsTrue(ok, error?.Message);
                Assert.AreSame(child, node.ContainedWithin);
            });
        }

        // ── MoveGhostNodeToBoundary ────────────────────────────────────────────

        [TestMethod]
        public void TestMoveGhostNode_FunctionTemplateInternal_ToGlobal_Rejected()
        {
            TestHelper.RunInModelSystemContext(nameof(TestMoveGhostNode_FunctionTemplateInternal_ToGlobal_Rejected),
            (user, pSession, ms) =>
            {
                var gb = ms.ModelSystem.GlobalBoundary;
                // The referenced real node lives on the global boundary.
                var realNode = AddNode(user, ms, gb, "RealNode");
                var ft       = AddTemplate(user, ms, gb);

                // Add a ghost node for realNode inside the FunctionTemplate's InternalModules.
                Assert.IsTrue(ms.AddGhostNode(user, ft.InternalModules, realNode,
                    new Rectangle(10f, 10f, 120f, 50f), out var ghost, out var addErr), addErr?.Message);

                var ok = ms.MoveGhostNodeToBoundary(user, ghost!, gb, out var error);
                Assert.IsFalse(ok, "Expected move of ghost node out of FunctionTemplate to be rejected.");
                Assert.IsNotNull(error);
                Assert.AreSame(ft.InternalModules, ghost!.ContainedWithin);
            });
        }

        [TestMethod]
        public void TestMoveGhostNode_Global_ToFunctionTemplateInternal_Rejected()
        {
            TestHelper.RunInModelSystemContext(nameof(TestMoveGhostNode_Global_ToFunctionTemplateInternal_Rejected),
            (user, pSession, ms) =>
            {
                var gb = ms.ModelSystem.GlobalBoundary;
                var ft = AddTemplate(user, ms, gb);
                var realNode = AddNode(user, ms, gb, "RealNode");

                // Ghost node on the global boundary.
                Assert.IsTrue(ms.AddGhostNode(user, gb, realNode,
                    new Rectangle(10f, 10f, 120f, 50f), out var ghost, out var addErr), addErr?.Message);

                var ok = ms.MoveGhostNodeToBoundary(user, ghost!, ft.InternalModules, out var error);
                Assert.IsFalse(ok, "Expected move of ghost node into FunctionTemplate to be rejected.");
                Assert.IsNotNull(error);
                Assert.AreSame(gb, ghost!.ContainedWithin);
            });
        }

        [TestMethod]
        public void TestMoveGhostNode_WithinFunctionTemplateChildBoundary_Accepted()
        {
            TestHelper.RunInModelSystemContext(nameof(TestMoveGhostNode_WithinFunctionTemplateChildBoundary_Accepted),
            (user, pSession, ms) =>
            {
                var gb           = ms.ModelSystem.GlobalBoundary;
                var ft           = AddTemplate(user, ms, gb);
                var internalChild = AddChildBoundary(user, ms, ft.InternalModules, "InternalChild");
                var realNode     = AddNode(user, ms, ft.InternalModules, "RealNode");

                Assert.IsTrue(ms.AddGhostNode(user, ft.InternalModules, realNode,
                    new Rectangle(10f, 10f, 120f, 50f), out var ghost, out var addErr), addErr?.Message);

                var ok = ms.MoveGhostNodeToBoundary(user, ghost!, internalChild, out var error);
                Assert.IsTrue(ok, error?.Message);
                Assert.AreSame(internalChild, ghost!.ContainedWithin);
            });
        }

        [TestMethod]
        public void TestMoveElementsToBoundary_UndoRedoMovesSelectionAsOneCommand()
        {
            TestHelper.RunInModelSystemContext(nameof(TestMoveElementsToBoundary_UndoRedoMovesSelectionAsOneCommand),
            (user, _, ms) =>
            {
                var root = ms.ModelSystem.GlobalBoundary;
                var destination = AddChildBoundary(user, ms, root, "Destination");
                var first = AddNode(user, ms, root, "First");
                var second = AddNode(user, ms, root, "Second");

                Assert.IsTrue(ms.MoveElementsToBoundary(user, destination,
                    new[] { first, second }, null, null, null, out var error), error?.Message);
                Assert.AreSame(destination, first.ContainedWithin);
                Assert.AreSame(destination, second.ContainedWithin);

                Assert.IsTrue(ms.Undo(user, out error), error?.Message);
                Assert.AreSame(root, first.ContainedWithin);
                Assert.AreSame(root, second.ContainedWithin);

                Assert.IsTrue(ms.Redo(user, out error), error?.Message);
                Assert.AreSame(destination, first.ContainedWithin);
                Assert.AreSame(destination, second.ContainedWithin);
            });
        }
    }
}
