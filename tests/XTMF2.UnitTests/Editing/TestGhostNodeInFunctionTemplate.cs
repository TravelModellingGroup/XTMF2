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
using XTMF2.ModelSystemConstruct;
using XTMF2.UnitTests.Modules;

namespace XTMF2.UnitTests.Editing
{
    /// <summary>
    /// Regression tests for ghost-node operations scoped to a
    /// <see cref="FunctionTemplate"/>'s InternalModules boundary.
    /// </summary>
    [TestClass]
    public class TestGhostNodeInFunctionTemplate
    {
        // ── Helpers ────────────────────────────────────────────────────────────

        private static FunctionTemplate AddTemplate(User user, ModelSystemSession ms,
            Boundary boundary, string name = "MyTemplate")
        {
            Assert.IsTrue(ms.AddFunctionTemplate(user, boundary, name, out var ft, out var err),
                err?.Message);
            return ft!;
        }

        private static Node AddNode(User user, ModelSystemSession ms, Boundary boundary,
            string name = "TestNode")
        {
            Assert.IsTrue(ms.AddNode(user, boundary, name, typeof(SimpleTestModule),
                new Rectangle(10f, 10f, 120f, 50f), out var node, out var err), err?.Message);
            return node!;
        }

        private static GhostNode AddGhost(User user, ModelSystemSession ms,
            Boundary boundary, Node realNode)
        {
            Assert.IsTrue(ms.AddGhostNode(user, boundary, realNode,
                new Rectangle(200f, 10f, 120f, 50f), out var ghost, out var err), err?.Message);
            return ghost!;
        }

        // ── RemoveGhostNode inside InternalModules ─────────────────────────────

        [TestMethod]
        public void TestRemoveGhostNode_InsideFunctionTemplate_Succeeds()
        {
            TestHelper.RunInModelSystemContext(nameof(TestRemoveGhostNode_InsideFunctionTemplate_Succeeds),
            (user, pSession, ms) =>
            {
                var gb       = ms.ModelSystem.GlobalBoundary;
                var ft       = AddTemplate(user, ms, gb);
                var realNode = AddNode(user, ms, gb, "RealNode");

                // Place a ghost inside the template.
                var ghost = AddGhost(user, ms, ft.InternalModules, realNode);
                Assert.HasCount(1, ft.InternalModules.GhostNodes);

                // The deletion should succeed.
                Assert.IsTrue(ms.RemoveGhostNode(user, ghost, out var err), err?.Message);
                Assert.IsEmpty(ft.InternalModules.GhostNodes,
                    "Ghost node should have been removed from InternalModules.");
            });
        }

        [TestMethod]
        public void TestRemoveGhostNode_InsideFunctionTemplate_UndoRedo()
        {
            TestHelper.RunInModelSystemContext(nameof(TestRemoveGhostNode_InsideFunctionTemplate_UndoRedo),
            (user, pSession, ms) =>
            {
                var gb       = ms.ModelSystem.GlobalBoundary;
                var ft       = AddTemplate(user, ms, gb);
                var realNode = AddNode(user, ms, gb, "RealNode");
                var ghost    = AddGhost(user, ms, ft.InternalModules, realNode);

                Assert.IsTrue(ms.RemoveGhostNode(user, ghost, out var err), err?.Message);
                Assert.IsEmpty(ft.InternalModules.GhostNodes);

                // Undo should restore the ghost.
                Assert.IsTrue(ms.Undo(user, out err), err?.Message);
                Assert.HasCount(1, ft.InternalModules.GhostNodes);

                // Redo should remove it again.
                Assert.IsTrue(ms.Redo(user, out err), err?.Message);
                Assert.IsEmpty(ft.InternalModules.GhostNodes);
            });
        }

        // ── Cascade deletion: removing the real node must also remove ghosts
        //    that live inside a FunctionTemplate's InternalModules. ─────────────

        [TestMethod]
        public void TestRemoveRealNode_CascadesGhostInsideFunctionTemplate()
        {
            TestHelper.RunInModelSystemContext(nameof(TestRemoveRealNode_CascadesGhostInsideFunctionTemplate),
            (user, pSession, ms) =>
            {
                var gb       = ms.ModelSystem.GlobalBoundary;
                var ft       = AddTemplate(user, ms, gb);
                var realNode = AddNode(user, ms, gb, "RealNode");

                // Ghost lives inside the FunctionTemplate.
                _ = AddGhost(user, ms, ft.InternalModules, realNode);
                Assert.HasCount(1, ft.InternalModules.GhostNodes);

                // Removing the real node should cascade-delete the ghost.
                Assert.IsTrue(ms.RemoveNode(user, realNode, out var err), err?.Message);
                Assert.IsEmpty(ft.InternalModules.GhostNodes,
                    "Ghost inside InternalModules should be cascade-deleted when its real node is removed.");
            });
        }

        [TestMethod]
        public void TestRemoveRealNode_CascadesGhostInsideFunctionTemplate_UndoRedo()
        {
            TestHelper.RunInModelSystemContext(nameof(TestRemoveRealNode_CascadesGhostInsideFunctionTemplate_UndoRedo),
            (user, pSession, ms) =>
            {
                var gb       = ms.ModelSystem.GlobalBoundary;
                var ft       = AddTemplate(user, ms, gb);
                var realNode = AddNode(user, ms, gb, "RealNode");
                _ = AddGhost(user, ms, ft.InternalModules, realNode);

                Assert.IsTrue(ms.RemoveNode(user, realNode, out var err), err?.Message);
                Assert.IsEmpty(ft.InternalModules.GhostNodes);

                // Undo: the real node comes back, and so should its ghost.
                Assert.IsTrue(ms.Undo(user, out err), err?.Message);
                Assert.HasCount(1, ft.InternalModules.GhostNodes,
                    "Ghost inside InternalModules should be restored on undo.");
                Assert.IsTrue(gb.Modules.Contains(realNode),
                    "Real node should be restored on undo.");

                // Redo: both are removed again.
                Assert.IsTrue(ms.Redo(user, out err), err?.Message);
                Assert.IsEmpty(ft.InternalModules.GhostNodes);
            });
        }

        // ── Link cleanup: links going to a ghost inside InternalModules ────────

        [TestMethod]
        public void TestRemoveGhostNode_InsideFunctionTemplate_IncomingLinkCleaned()
        {
            TestHelper.RunInModelSystemContext(nameof(TestRemoveGhostNode_InsideFunctionTemplate_IncomingLinkCleaned),
            (user, pSession, ms) =>
            {
                var gb = ms.ModelSystem.GlobalBoundary;
                var ft = AddTemplate(user, ms, gb);

                // Origin: SimpleParameterModule (has a Parameter hook for IFunction<string>).
                Assert.IsTrue(ms.AddNode(user, ft.InternalModules, "Origin",
                    typeof(SimpleParameterModule), new Rectangle(10f, 10f, 120f, 50f),
                    out var origin, out var nodeErr), nodeErr?.Message);

                // Real node the ghost will reference (lives in InternalModules too).
                var realNode = AddNode(user, ms, ft.InternalModules, "RealNode");
                var ghost    = AddGhost(user, ms, ft.InternalModules, realNode);

                // Link origin → ghost using the first available hook.
                var hooks = origin!.Hooks;
                Assert.IsTrue(hooks.Count > 0, "SimpleParameterModule must expose at least one hook.");
                Assert.IsTrue(ms.AddLink(user, origin, hooks[0], ghost, out _, out var linkErr),
                    linkErr?.Message);

                Assert.HasCount(1, ft.InternalModules.Links);

                // Delete the ghost; the link should be cleaned up.
                Assert.IsTrue(ms.RemoveGhostNode(user, ghost, out var err), err?.Message);
                Assert.IsEmpty(ft.InternalModules.GhostNodes);
                Assert.IsEmpty(ft.InternalModules.Links,
                    "Links pointing to a deleted ghost inside InternalModules should be removed.");
            });
        }
    }
}
