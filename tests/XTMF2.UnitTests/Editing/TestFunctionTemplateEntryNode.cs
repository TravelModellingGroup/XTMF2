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

namespace XTMF2.UnitTests.Editing;

/// <summary>
/// Verifies the <see cref="FunctionTemplate.EntryNode"/> feature:
/// designation of an arbitrary node in <see cref="FunctionTemplate.InternalModules"/>
/// as the entry point, undo/redo support, save/load round-trip, and validation.
/// </summary>
[TestClass]
public class TestFunctionTemplateEntryNode
{
    // ── Helpers ────────────────────────────────────────────────────────────

    private static FunctionTemplate AddTemplate(User user, ModelSystemSession ms,
        Boundary boundary, string name = "MyTemplate")
    {
        Assert.IsTrue(ms.AddFunctionTemplate(user, boundary, name, out var ft, out var err),
            err?.Message);
        return ft!;
    }

    private static Node AddNodeTo(User user, ModelSystemSession ms, Boundary boundary,
        string name = "InternalNode")
    {
        Assert.IsTrue(ms.AddNode(user, boundary, name, typeof(SimpleTestModule),
            new Rectangle(10f, 10f, 120f, 50f), out var node, out var err), err?.Message);
        return node!;
    }

    // ── Basic set / clear ──────────────────────────────────────────────────

    [TestMethod]
    public void TestSetEntryNode_ValidInternalNode_Succeeds()
    {
        TestHelper.RunInModelSystemContext(nameof(TestSetEntryNode_ValidInternalNode_Succeeds),
            (user, pSession, ms) =>
            {
                var ft   = AddTemplate(user, ms, ms.ModelSystem.GlobalBoundary);
                var node = AddNodeTo(user, ms, ft.InternalModules);

                Assert.IsTrue(ms.SetFunctionTemplateEntryNode(user, ft, node, out var err),
                    err?.Message);

                Assert.AreSame(node, ft.EntryNode, "EntryNode should be the assigned node.");
            });
    }

    [TestMethod]
    public void TestSetEntryNode_NullClears_Succeeds()
    {
        TestHelper.RunInModelSystemContext(nameof(TestSetEntryNode_NullClears_Succeeds),
            (user, pSession, ms) =>
            {
                var ft   = AddTemplate(user, ms, ms.ModelSystem.GlobalBoundary);
                var node = AddNodeTo(user, ms, ft.InternalModules);

                Assert.IsTrue(ms.SetFunctionTemplateEntryNode(user, ft, node, out _));
                Assert.IsNotNull(ft.EntryNode, "EntryNode should be set before clearing.");

                Assert.IsTrue(ms.SetFunctionTemplateEntryNode(user, ft, null, out var err),
                    err?.Message);
                Assert.IsNull(ft.EntryNode, "EntryNode should be null after clearing.");
                Assert.IsNull(ft.Type,      "Type should be null when EntryNode is null.");
            });
    }

    [TestMethod]
    public void TestSetEntryNode_TypeMatchesNodeType()
    {
        TestHelper.RunInModelSystemContext(nameof(TestSetEntryNode_TypeMatchesNodeType),
            (user, pSession, ms) =>
            {
                var ft   = AddTemplate(user, ms, ms.ModelSystem.GlobalBoundary);
                var node = AddNodeTo(user, ms, ft.InternalModules);

                Assert.IsTrue(ms.SetFunctionTemplateEntryNode(user, ft, node, out _));

                Assert.AreEqual(node.Type, ft.Type,
                    "FunctionTemplate.Type should mirror the entry node's Type.");
            });
    }

    // ── Validation ─────────────────────────────────────────────────────────

    [TestMethod]
    public void TestSetEntryNode_NodeFromWrongBoundary_Rejected()
    {
        TestHelper.RunInModelSystemContext(nameof(TestSetEntryNode_NodeFromWrongBoundary_Rejected),
            (user, pSession, ms) =>
            {
                var gb    = ms.ModelSystem.GlobalBoundary;
                var ft    = AddTemplate(user, ms, gb);
                // A node in the global boundary — NOT in InternalModules.
                var outer = AddNodeTo(user, ms, gb, "OuterNode");

                var ok = ms.SetFunctionTemplateEntryNode(user, ft, outer, out var err);

                Assert.IsFalse(ok, "Should reject a node that is not in InternalModules.");
                Assert.IsNotNull(err, "An error should be returned.");
                Assert.IsNull(ft.EntryNode, "EntryNode must remain unset after rejection.");
            });
    }

    [TestMethod]
    public void TestSetEntryNode_NodeFromDifferentTemplate_Rejected()
    {
        TestHelper.RunInModelSystemContext(nameof(TestSetEntryNode_NodeFromDifferentTemplate_Rejected),
            (user, pSession, ms) =>
            {
                var gb  = ms.ModelSystem.GlobalBoundary;
                var ft1 = AddTemplate(user, ms, gb, "TemplateA");
                var ft2 = AddTemplate(user, ms, gb, "TemplateB");
                var nodeInFt2 = AddNodeTo(user, ms, ft2.InternalModules, "NodeInB");

                var ok = ms.SetFunctionTemplateEntryNode(user, ft1, nodeInFt2, out var err);

                Assert.IsFalse(ok, "Should reject a node from a different template's InternalModules.");
                Assert.IsNotNull(err);
                Assert.IsNull(ft1.EntryNode);
            });
    }

    // ── Undo / Redo ────────────────────────────────────────────────────────

    [TestMethod]
    public void TestSetEntryNode_UndoRestoresPreviousValue()
    {
        TestHelper.RunInModelSystemContext(nameof(TestSetEntryNode_UndoRestoresPreviousValue),
            (user, pSession, ms) =>
            {
                var ft    = AddTemplate(user, ms, ms.ModelSystem.GlobalBoundary);
                var nodeA = AddNodeTo(user, ms, ft.InternalModules, "NodeA");
                var nodeB = AddNodeTo(user, ms, ft.InternalModules, "NodeB");

                Assert.IsTrue(ms.SetFunctionTemplateEntryNode(user, ft, nodeA, out _));
                Assert.IsTrue(ms.SetFunctionTemplateEntryNode(user, ft, nodeB, out _));
                Assert.AreSame(nodeB, ft.EntryNode, "EntryNode should be NodeB after second set.");

                Assert.IsTrue(ms.Undo(user, out var undoErr), undoErr?.Message);
                Assert.AreSame(nodeA, ft.EntryNode, "Undo should restore NodeA.");
            });
    }

    [TestMethod]
    public void TestSetEntryNode_RedoReappliesChange()
    {
        TestHelper.RunInModelSystemContext(nameof(TestSetEntryNode_RedoReappliesChange),
            (user, pSession, ms) =>
            {
                var ft   = AddTemplate(user, ms, ms.ModelSystem.GlobalBoundary);
                var node = AddNodeTo(user, ms, ft.InternalModules);

                Assert.IsTrue(ms.SetFunctionTemplateEntryNode(user, ft, node, out _));
                Assert.IsTrue(ms.Undo(user, out _));
                Assert.IsNull(ft.EntryNode, "EntryNode should be null after undo.");

                Assert.IsTrue(ms.Redo(user, out var redoErr), redoErr?.Message);
                Assert.AreSame(node, ft.EntryNode, "Redo should reapply the entry node.");
            });
    }

    [TestMethod]
    public void TestSetEntryNode_UndoClear_RestoresNode()
    {
        TestHelper.RunInModelSystemContext(nameof(TestSetEntryNode_UndoClear_RestoresNode),
            (user, pSession, ms) =>
            {
                var ft   = AddTemplate(user, ms, ms.ModelSystem.GlobalBoundary);
                var node = AddNodeTo(user, ms, ft.InternalModules);

                Assert.IsTrue(ms.SetFunctionTemplateEntryNode(user, ft, node, out _));
                Assert.IsTrue(ms.SetFunctionTemplateEntryNode(user, ft, null, out _));
                Assert.IsNull(ft.EntryNode, "EntryNode should be null after clearing.");

                Assert.IsTrue(ms.Undo(user, out var err), err?.Message);
                Assert.AreSame(node, ft.EntryNode, "Undo of clear should restore the node.");
            });
    }

    // ── Save / Load ────────────────────────────────────────────────────────

    [TestMethod]
    public void TestSetEntryNode_SaveLoad_PreservesEntryNode()
    {
        TestHelper.RunInModelSystemContext("TestSetEntryNode_SaveLoad_PreservesEntryNode",
            (user, pSession, ms) =>
            {
                var ft   = AddTemplate(user, ms, ms.ModelSystem.GlobalBoundary);
                var node = AddNodeTo(user, ms, ft.InternalModules);
                Assert.IsTrue(ms.SetFunctionTemplateEntryNode(user, ft, node, out _));
                Assert.IsTrue(ms.Save(out var err), err?.Message);
            },
            (user, pSession, ms) =>
            {
                var boundary = ms.ModelSystem.GlobalBoundary;
                Assert.HasCount(1, boundary.FunctionTemplates,
                    "FunctionTemplate should be reloaded.");
                var ft = boundary.FunctionTemplates[0];
                Assert.IsNotNull(ft.EntryNode,
                    "EntryNode should survive the save/load round-trip.");
                Assert.AreEqual(typeof(SimpleTestModule), ft.Type,
                    "Type should be restored to the entry node's type.");
            });
    }

    [TestMethod]
    public void TestSetEntryNode_SaveLoad_NullEntryNode_RemainsNull()
    {
        TestHelper.RunInModelSystemContext("TestSetEntryNode_SaveLoad_NullEntryNode_RemainsNull",
            (user, pSession, ms) =>
            {
                // A template with no entry node is saved.
                AddTemplate(user, ms, ms.ModelSystem.GlobalBoundary);
                Assert.IsTrue(ms.Save(out var err), err?.Message);
            },
            (user, pSession, ms) =>
            {
                var ft = ms.ModelSystem.GlobalBoundary.FunctionTemplates[0];
                Assert.IsNull(ft.EntryNode,
                    "EntryNode should remain null when none was set before saving.");
                Assert.IsNull(ft.Type,
                    "Type should remain null when EntryNode is null.");
            });
    }

    // ── Initial state ──────────────────────────────────────────────────────

    [TestMethod]
    public void TestEntryNode_InitiallyNull()
    {
        TestHelper.RunInModelSystemContext(nameof(TestEntryNode_InitiallyNull),
            (user, pSession, ms) =>
            {
                var ft = AddTemplate(user, ms, ms.ModelSystem.GlobalBoundary);
                Assert.IsNull(ft.EntryNode, "A freshly created template should have no entry node.");
                Assert.IsNull(ft.Type,      "Type should be null when EntryNode is null.");
            });
    }
}
