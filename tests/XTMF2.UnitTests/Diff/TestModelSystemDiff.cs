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
using System;
using System.Linq;
using XTMF2.Diff;
using XTMF2.ModelSystemConstruct;
using XTMF2.UnitTests.Modules;

namespace XTMF2.UnitTests.Diff;

/// <summary>
/// Unit tests for the model-system diff engine:
/// <list type="bullet">
///   <item>Verifies that stable element GUIDs survive a save/load round-trip.</item>
///   <item>Verifies that <see cref="ModelSystemComparer.Compare"/> correctly classifies
///   added, removed, modified, and unchanged elements.</item>
///   <item>Verifies that <see cref="XTMF2.Editing.ProjectSession.LoadModelSystemForDiff"/>
///   returns a snapshot consistent with what was saved.</item>
/// </list>
/// </summary>
[TestClass]
public class TestModelSystemDiff
{
    private static readonly Type s_moduleType = typeof(SimpleTestModule);

    // ── ID round-trip tests ──────────────────────────────────────────────────

    [TestMethod]
    public void TestNodeId_IsNonEmptyAfterCreation()
    {
        TestHelper.RunInModelSystemContext("NodeIdNonEmpty", (user, projectSession, msSession) =>
        {
            var boundary = msSession.ModelSystem.GlobalBoundary;
            Assert.IsTrue(msSession.AddNode(user, boundary, "N", s_moduleType,
                new Rectangle(0, 0, 100, 50), out var node, out var error), error?.Message);
            Assert.IsNotNull(node);
            Assert.AreNotEqual(Guid.Empty, node.Id, "Node.Id must not be Guid.Empty after creation.");
        });
    }

    [TestMethod]
    public void TestNodeId_IsPersistentAcrossSaveLoad()
    {
        // Save the model system, then load it twice as diff snapshots and confirm
        // the node's GUID is the same in both snapshots.
        TestHelper.RunInModelSystemContext("NodeIdPersistent", (user, projectSession, msSession) =>
        {
            var boundary = msSession.ModelSystem.GlobalBoundary;
            var msHeader = msSession.ModelSystemHeader;

            Assert.IsTrue(msSession.AddNode(user, boundary, "Node1", s_moduleType,
                new Rectangle(0, 0, 100, 50), out var node, out var error), error?.Message);
            Assert.IsNotNull(node);
            var originalId = node.Id;

            // Persist to disk.
            Assert.IsTrue(msSession.Save(out error), error?.Message);

            // Load two independent snapshots using the diff API.
            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var snap1, out error),
                error?.Message);
            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var snap2, out error),
                error?.Message);

            var id1 = snap1.GlobalBoundary.Modules.Single().Id;
            var id2 = snap2.GlobalBoundary.Modules.Single().Id;

            Assert.AreEqual(originalId, id1, "First snapshot node Id must match the in-session Id.");
            Assert.AreEqual(originalId, id2, "Second snapshot node Id must match the in-session Id.");
        });
    }

    [TestMethod]
    public void TestBoundaryId_IsPersistentAcrossSaveLoad()
    {
        TestHelper.RunInModelSystemContext("BoundaryIdPersistent", (user, projectSession, msSession) =>
        {
            var global = msSession.ModelSystem.GlobalBoundary;
            var msHeader = msSession.ModelSystemHeader;

            Assert.IsTrue(msSession.AddBoundary(user, global, "SubBound", out var sub, out var error),
                error?.Message);
            Assert.IsNotNull(sub);
            var originalId = sub.Id;

            Assert.IsTrue(msSession.Save(out error), error?.Message);

            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var snap1, out error),
                error?.Message);
            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var snap2, out error),
                error?.Message);

            var sub1 = snap1.GlobalBoundary.Boundaries.Single();
            var sub2 = snap2.GlobalBoundary.Boundaries.Single();

            Assert.AreEqual(originalId, sub1.Id, "First snapshot boundary Id must match.");
            Assert.AreEqual(originalId, sub2.Id, "Second snapshot boundary Id must match.");
        });
    }

    [TestMethod]
    public void TestCommentBlockId_IsPersistentAcrossSaveLoad()
    {
        TestHelper.RunInModelSystemContext("CommentIdPersistent", (user, projectSession, msSession) =>
        {
            var boundary = msSession.ModelSystem.GlobalBoundary;
            var msHeader = msSession.ModelSystemHeader;

            Assert.IsTrue(msSession.AddCommentBlock(user, boundary, "A comment", new Rectangle(0, 0, 100, 30),
                out var comment, out var error), error?.Message);
            Assert.IsNotNull(comment);
            var originalId = comment.Id;

            Assert.IsTrue(msSession.Save(out error), error?.Message);

            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var snap1, out error),
                error?.Message);
            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var snap2, out error),
                error?.Message);

            var c1 = snap1.GlobalBoundary.CommentBlocks.Single();
            var c2 = snap2.GlobalBoundary.CommentBlocks.Single();

            Assert.AreEqual(originalId, c1.Id, "First snapshot CommentBlock Id must match.");
            Assert.AreEqual(originalId, c2.Id, "Second snapshot CommentBlock Id must match.");
        });
    }

    // ── Comparer: identical systems ─────────────────────────────────────────

    [TestMethod]
    public void TestDiff_IdenticalSystems_NoChanges()
    {
        // Load the same persisted snapshot twice; the diff should show no changes.
        TestHelper.RunInModelSystemContext("DiffIdentical", (user, projectSession, msSession) =>
        {
            var boundary = msSession.ModelSystem.GlobalBoundary;
            var msHeader = msSession.ModelSystemHeader;
            Assert.IsTrue(msSession.AddNode(user, boundary, "N1", s_moduleType,
                new Rectangle(0, 0, 100, 50), out _, out var error), error?.Message);
            Assert.IsTrue(msSession.Save(out error), error?.Message);

            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var left, out error),
                error?.Message);
            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var right, out error),
                error?.Message);

            var diff = ModelSystemComparer.Compare(left, right);

            Assert.IsFalse(diff.HasChanges, "Comparing identical snapshots must report no changes.");
            Assert.IsTrue(diff.GlobalBoundary.Nodes.All(n => n.Kind == ElementDiffKind.Unchanged),
                "All nodes must be Unchanged.");
        });
    }

    [TestMethod]
    public void TestDiff_EmptyModelSystem_NoChanges()
    {
        // Two empty snapshots (never saved) should produce an empty diff.
        TestHelper.RunInModelSystemContext("DiffBothEmpty", (user, projectSession, msSession) =>
        {
            var msHeader = msSession.ModelSystemHeader;
            // Do NOT save — LoadModelSystemForDiff falls back to a new empty ModelSystem.
            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var left, out var error),
                error?.Message);
            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var right, out error),
                error?.Message);

            var diff = ModelSystemComparer.Compare(left, right);

            Assert.IsFalse(diff.HasChanges, "Two empty model systems must produce an empty diff.");
        });
    }

    // ── Comparer: added element ─────────────────────────────────────────────

    [TestMethod]
    public void TestDiff_AddedNode_ShowsAdded()
    {
        // left: one node; right: two nodes (the second is new).
        TestHelper.RunInModelSystemContext("DiffAddedNode", (user, projectSession, msSession) =>
        {
            var boundary = msSession.ModelSystem.GlobalBoundary;
            var msHeader = msSession.ModelSystemHeader;

            Assert.IsTrue(msSession.AddNode(user, boundary, "BaseNode", s_moduleType,
                new Rectangle(0, 0, 100, 50), out _, out var error), error?.Message);
            Assert.IsTrue(msSession.Save(out error), error?.Message);

            // Load "left" snapshot (one node).
            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var left, out error),
                error?.Message);

            // Add another node and save again.
            Assert.IsTrue(msSession.AddNode(user, boundary, "NewNode", s_moduleType,
                new Rectangle(0, 60, 100, 50), out _, out error), error?.Message);
            Assert.IsTrue(msSession.Save(out error), error?.Message);

            // Load "right" snapshot (two nodes).
            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var right, out error),
                error?.Message);

            var diff = ModelSystemComparer.Compare(left, right);

            Assert.IsTrue(diff.HasChanges, "Diff must detect the added node.");
            Assert.AreEqual(1, diff.GlobalBoundary.Nodes.Count(n => n.Kind == ElementDiffKind.Added),
                "Exactly one node should be classified as Added.");
        });
    }

    [TestMethod]
    public void TestDiff_AddedComment_ShowsAdded()
    {
        TestHelper.RunInModelSystemContext("DiffAddedComment", (user, projectSession, msSession) =>
        {
            var boundary = msSession.ModelSystem.GlobalBoundary;
            var msHeader = msSession.ModelSystemHeader;

            Assert.IsTrue(msSession.Save(out var error), error?.Message);
            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var left, out error),
                error?.Message);

            Assert.IsTrue(msSession.AddCommentBlock(user, boundary, "New comment",
                new Rectangle(0, 0, 100, 30), out _, out error), error?.Message);
            Assert.IsTrue(msSession.Save(out error), error?.Message);
            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var right, out error),
                error?.Message);

            var diff = ModelSystemComparer.Compare(left, right);

            Assert.IsTrue(diff.HasChanges);
            Assert.AreEqual(1, diff.GlobalBoundary.CommentBlocks.Count(c => c.Kind == ElementDiffKind.Added));
        });
    }

    // ── Comparer: removed element ───────────────────────────────────────────

    [TestMethod]
    public void TestDiff_RemovedNode_ShowsRemoved()
    {
        TestHelper.RunInModelSystemContext("DiffRemovedNode", (user, projectSession, msSession) =>
        {
            var boundary = msSession.ModelSystem.GlobalBoundary;
            var msHeader = msSession.ModelSystemHeader;

            Assert.IsTrue(msSession.AddNode(user, boundary, "N1", s_moduleType,
                new Rectangle(0, 0, 100, 50), out var node, out var error), error?.Message);
            Assert.IsNotNull(node);
            Assert.IsTrue(msSession.Save(out error), error?.Message);

            // Load "left" (has the node).
            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var left, out error),
                error?.Message);

            // Remove the node and save.
            Assert.IsTrue(msSession.RemoveNode(user, node, out error), error?.Message);
            Assert.IsTrue(msSession.Save(out error), error?.Message);

            // Load "right" (no nodes).
            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var right, out error),
                error?.Message);

            var diff = ModelSystemComparer.Compare(left, right);

            Assert.IsTrue(diff.HasChanges, "Diff must detect the removed node.");
            Assert.AreEqual(1, diff.GlobalBoundary.Nodes.Count(n => n.Kind == ElementDiffKind.Removed),
                "Exactly one node should be classified as Removed.");
        });
    }

    [TestMethod]
    public void TestDiff_RemovedComment_ShowsRemoved()
    {
        TestHelper.RunInModelSystemContext("DiffRemovedComment", (user, projectSession, msSession) =>
        {
            var boundary = msSession.ModelSystem.GlobalBoundary;
            var msHeader = msSession.ModelSystemHeader;

            Assert.IsTrue(msSession.AddCommentBlock(user, boundary, "To remove",
                new Rectangle(0, 0, 100, 30), out var comment, out var error), error?.Message);
            Assert.IsNotNull(comment);
            Assert.IsTrue(msSession.Save(out error), error?.Message);
            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var left, out error),
                error?.Message);

            Assert.IsTrue(msSession.RemoveCommentBlock(user, boundary, comment, out error), error?.Message);
            Assert.IsTrue(msSession.Save(out error), error?.Message);
            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var right, out error),
                error?.Message);

            var diff = ModelSystemComparer.Compare(left, right);

            Assert.IsTrue(diff.HasChanges);
            Assert.AreEqual(1, diff.GlobalBoundary.CommentBlocks.Count(c => c.Kind == ElementDiffKind.Removed));
        });
    }

    // ── Comparer: modified element ──────────────────────────────────────────

    [TestMethod]
    public void TestDiff_ModifiedNodeName_ShowsModified()
    {
        TestHelper.RunInModelSystemContext("DiffModifiedNode", (user, projectSession, msSession) =>
        {
            var boundary = msSession.ModelSystem.GlobalBoundary;
            var msHeader = msSession.ModelSystemHeader;

            Assert.IsTrue(msSession.AddNode(user, boundary, "OldName", s_moduleType,
                new Rectangle(0, 0, 100, 50), out var node, out var error), error?.Message);
            Assert.IsNotNull(node);
            Assert.IsTrue(msSession.Save(out error), error?.Message);

            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var left, out error),
                error?.Message);

            Assert.IsTrue(msSession.SetNodeName(user, node, "NewName", out error), error?.Message);
            Assert.IsTrue(msSession.Save(out error), error?.Message);

            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var right, out error),
                error?.Message);

            var diff = ModelSystemComparer.Compare(left, right);

            Assert.IsTrue(diff.HasChanges);
            var nodeDiff = diff.GlobalBoundary.Nodes.Single();
            Assert.AreEqual(ElementDiffKind.Modified, nodeDiff.Kind);
            Assert.AreEqual("OldName", nodeDiff.LeftName);
            Assert.AreEqual("NewName", nodeDiff.RightName);
        });
    }

    [TestMethod]
    public void TestDiff_ModifiedComment_ShowsModified()
    {
        TestHelper.RunInModelSystemContext("DiffModifiedComment", (user, projectSession, msSession) =>
        {
            var boundary = msSession.ModelSystem.GlobalBoundary;
            var msHeader = msSession.ModelSystemHeader;

            Assert.IsTrue(msSession.AddCommentBlock(user, boundary, "Original text",
                new Rectangle(0, 0, 100, 30), out var comment, out var error), error?.Message);
            Assert.IsNotNull(comment);
            Assert.IsTrue(msSession.Save(out error), error?.Message);
            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var left, out error),
                error?.Message);

            Assert.IsTrue(msSession.SetCommentBlockText(user, comment, "Updated text", out error),
                error?.Message);
            Assert.IsTrue(msSession.Save(out error), error?.Message);
            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var right, out error),
                error?.Message);

            var diff = ModelSystemComparer.Compare(left, right);

            Assert.IsTrue(diff.HasChanges);
            var commentDiff = diff.GlobalBoundary.CommentBlocks.Single();
            Assert.AreEqual(ElementDiffKind.Modified, commentDiff.Kind);
            Assert.AreEqual("Original text", commentDiff.LeftComment);
            Assert.AreEqual("Updated text", commentDiff.RightComment);
        });
    }

    // ── Comparer: sub-boundary ──────────────────────────────────────────────

    [TestMethod]
    public void TestDiff_AddedSubBoundary_ShowsAdded()
    {
        TestHelper.RunInModelSystemContext("DiffAddedBoundary", (user, projectSession, msSession) =>
        {
            var global = msSession.ModelSystem.GlobalBoundary;
            var msHeader = msSession.ModelSystemHeader;

            Assert.IsTrue(msSession.Save(out var error), error?.Message);
            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var left, out error),
                error?.Message);

            Assert.IsTrue(msSession.AddBoundary(user, global, "NewSub", out _, out error), error?.Message);
            Assert.IsTrue(msSession.Save(out error), error?.Message);
            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var right, out error),
                error?.Message);

            var diff = ModelSystemComparer.Compare(left, right);

            Assert.IsTrue(diff.HasChanges, "Diff must detect the added sub-boundary.");
            Assert.AreEqual(1,
                diff.GlobalBoundary.SubBoundaries.Count(b => b.Kind == ElementDiffKind.Added));
        });
    }

    // ── Access-control tests ────────────────────────────────────────────────

    [TestMethod]
    public void TestLoadModelSystemForDiff_WrongUser_Fails()
    {
        TestHelper.RunInModelSystemContext("DiffWrongUser",
            (user, hacker, projectSession, msSession) =>
            {
                var msHeader = msSession.ModelSystemHeader;
                Assert.IsFalse(
                    projectSession.LoadModelSystemForDiff(hacker, msHeader, out _, out var error),
                    "An unauthorised user must not be able to load a model system for diff.");
                Assert.IsNotNull(error);
            });
    }

    [TestMethod]
    public void TestLoadModelSystemForDiff_HeaderNotInProject_Fails()
    {
        // Two independent project sessions: verify that a header from one cannot be used
        // in LoadModelSystemForDiff against the other.
        TestHelper.RunInProjectContext("DiffHeaderMismatch1", (user1, projectSession1) =>
        {
            TestHelper.RunInProjectContext("DiffHeaderMismatch2", (user2, projectSession2) =>
            {
                // Create a model system in project 2 so it has a header.
                Assert.IsTrue(projectSession2.CreateNewModelSystem(user2, "MSInProject2",
                    out var headerFromProject2, out var error), error?.Message);
                Assert.IsNotNull(headerFromProject2);

                // Using project 2's header against project 1's session must fail.
                Assert.IsFalse(
                    projectSession1.LoadModelSystemForDiff(user1, headerFromProject2, out _, out error),
                    "A header from a different project must be rejected.");
            });
        });
    }

    // ── Comparer: parameter value changes ───────────────────────────────────

    [TestMethod]
    public void TestDiff_ModifiedBasicParameterValue_ShowsModified()
    {
        TestHelper.RunInModelSystemContext("DiffModifiedParameterValue", (user, projectSession, msSession) =>
        {
            var boundary = msSession.ModelSystem.GlobalBoundary;
            var msHeader = msSession.ModelSystemHeader;

            var paramType = typeof(XTMF2.RuntimeModules.BasicParameter<string>);
            Assert.IsTrue(msSession.AddNode(user, boundary, "Param", paramType,
                new Rectangle(0, 0, 100, 50), out var node, out var error), error?.Message);
            Assert.IsNotNull(node);

            Assert.IsTrue(msSession.SetParameterValue(user, node, "hello", out error), error?.Message);
            Assert.IsTrue(msSession.Save(out error), error?.Message);
            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var left, out error),
                error?.Message);

            Assert.IsTrue(msSession.SetParameterValue(user, node, "world", out error), error?.Message);
            Assert.IsTrue(msSession.Save(out error), error?.Message);
            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var right, out error),
                error?.Message);

            var diff = ModelSystemComparer.Compare(left, right);

            Assert.IsTrue(diff.HasChanges, "Diff must detect the changed parameter value.");
            var nodeDiff = diff.GlobalBoundary.Nodes.Single();
            Assert.AreEqual(ElementDiffKind.Modified, nodeDiff.Kind);
            Assert.AreEqual("hello", nodeDiff.LeftParameter);
            Assert.AreEqual("world", nodeDiff.RightParameter);
        });
    }

    [TestMethod]
    public void TestDiff_IdenticalParameterValues_ShowsUnchanged()
    {
        TestHelper.RunInModelSystemContext("DiffIdenticalParameterValues", (user, projectSession, msSession) =>
        {
            var boundary = msSession.ModelSystem.GlobalBoundary;
            var msHeader = msSession.ModelSystemHeader;

            var paramType = typeof(XTMF2.RuntimeModules.BasicParameter<string>);
            Assert.IsTrue(msSession.AddNode(user, boundary, "Param", paramType,
                new Rectangle(0, 0, 100, 50), out var node, out var error), error?.Message);
            Assert.IsNotNull(node);

            Assert.IsTrue(msSession.SetParameterValue(user, node, "same", out error), error?.Message);
            Assert.IsTrue(msSession.Save(out error), error?.Message);
            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var left, out error),
                error?.Message);

            // Load again without any changes — should be identical
            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var right, out error),
                error?.Message);

            var diff = ModelSystemComparer.Compare(left, right);

            Assert.IsFalse(diff.HasChanges, "Diff must not detect changes when parameter values are identical.");
            var nodeDiff = diff.GlobalBoundary.Nodes.Single();
            Assert.AreEqual(ElementDiffKind.Unchanged, nodeDiff.Kind);
        });
    }

    // ── ID round-trip: Start, FunctionInstance, GhostNode ───────────────────

    [TestMethod]
    public void TestStartId_IsPersistentAcrossSaveLoad()
    {
        TestHelper.RunInModelSystemContext("StartIdPersistent", (user, projectSession, msSession) =>
        {
            var boundary = msSession.ModelSystem.GlobalBoundary;
            var msHeader = msSession.ModelSystemHeader;

            Assert.IsTrue(msSession.AddModelSystemStart(user, boundary, "MyStart",
                new Rectangle(0, 0, 100, 50), out var start, out var error), error?.Message);
            Assert.IsNotNull(start);
            var originalId = start.Id;
            Assert.AreNotEqual(Guid.Empty, originalId, "Start.Id must not be Guid.Empty after creation.");

            Assert.IsTrue(msSession.Save(out error), error?.Message);

            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var snap1, out error),
                error?.Message);
            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var snap2, out error),
                error?.Message);

            var id1 = snap1.GlobalBoundary.Starts.Single().Id;
            var id2 = snap2.GlobalBoundary.Starts.Single().Id;

            Assert.AreEqual(originalId, id1, "First snapshot Start Id must match the in-session Id.");
            Assert.AreEqual(originalId, id2, "Second snapshot Start Id must match the in-session Id.");
        });
    }

    [TestMethod]
    public void TestFunctionInstanceId_IsPersistentAcrossSaveLoad()
    {
        TestHelper.RunInModelSystemContext("FunctionInstanceIdPersistent", (user, projectSession, msSession) =>
        {
            var boundary = msSession.ModelSystem.GlobalBoundary;
            var msHeader = msSession.ModelSystemHeader;

            // Create a FunctionTemplate, then a FunctionInstance of it.
            Assert.IsTrue(msSession.AddFunctionTemplate(user, boundary, "MyTemplate",
                out var ft, out var error), error?.Message);
            Assert.IsNotNull(ft);

            Assert.IsTrue(msSession.AddFunctionInstance(user, boundary, ft, "MyInstance",
                new Rectangle(0, 0, 120, 50), out var fi, out error), error?.Message);
            Assert.IsNotNull(fi);
            var originalId = fi.Id;
            Assert.AreNotEqual(Guid.Empty, originalId, "FunctionInstance.Id must not be Guid.Empty after creation.");

            Assert.IsTrue(msSession.Save(out error), error?.Message);

            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var snap1, out error),
                error?.Message);
            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var snap2, out error),
                error?.Message);

            var id1 = snap1.GlobalBoundary.FunctionInstances.Single().Id;
            var id2 = snap2.GlobalBoundary.FunctionInstances.Single().Id;

            Assert.AreEqual(originalId, id1, "First snapshot FunctionInstance Id must match the in-session Id.");
            Assert.AreEqual(originalId, id2, "Second snapshot FunctionInstance Id must match the in-session Id.");
        });
    }

    [TestMethod]
    public void TestGhostNodeId_IsPersistentAcrossSaveLoad()
    {
        TestHelper.RunInModelSystemContext("GhostNodeIdPersistent", (user, projectSession, msSession) =>
        {
            var boundary = msSession.ModelSystem.GlobalBoundary;
            var msHeader = msSession.ModelSystemHeader;

            // Create a real node, then add a ghost alias to it.
            Assert.IsTrue(msSession.AddNode(user, boundary, "RealNode", s_moduleType,
                new Rectangle(0, 0, 100, 50), out var realNode, out var error), error?.Message);
            Assert.IsNotNull(realNode);

            Assert.IsTrue(msSession.AddGhostNode(user, boundary, realNode,
                new Rectangle(200, 0, 100, 50), out var ghost, out error), error?.Message);
            Assert.IsNotNull(ghost);
            var originalId = ghost.Id;
            Assert.AreNotEqual(Guid.Empty, originalId, "GhostNode.Id must not be Guid.Empty after creation.");

            Assert.IsTrue(msSession.Save(out error), error?.Message);

            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var snap1, out error),
                error?.Message);
            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var snap2, out error),
                error?.Message);

            var id1 = snap1.GlobalBoundary.GhostNodes.Single().Id;
            var id2 = snap2.GlobalBoundary.GhostNodes.Single().Id;

            Assert.AreEqual(originalId, id1, "First snapshot GhostNode Id must match the in-session Id.");
            Assert.AreEqual(originalId, id2, "Second snapshot GhostNode Id must match the in-session Id.");
        });
    }

    [TestMethod]
    public void TestDiff_IdenticalFunctionTemplate_NoChanges()
    {
        // Verify that comparing two identical snapshots with a FunctionTemplate reports no changes
        // (regression test: previously, InternalModules Starts had unstable GUIDs causing false positives).
        TestHelper.RunInModelSystemContext("DiffIdenticalFunctionTemplate", (user, projectSession, msSession) =>
        {
            var boundary = msSession.ModelSystem.GlobalBoundary;
            var msHeader = msSession.ModelSystemHeader;

            Assert.IsTrue(msSession.AddFunctionTemplate(user, boundary, "MyTemplate",
                out _, out var error), error?.Message);

            Assert.IsTrue(msSession.Save(out error), error?.Message);

            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var left, out error),
                error?.Message);
            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var right, out error),
                error?.Message);

            var diff = ModelSystemComparer.Compare(left, right);

            Assert.IsFalse(diff.HasChanges,
                "Comparing two identical snapshots with a FunctionTemplate must report no changes.");
            Assert.IsTrue(diff.GlobalBoundary.FunctionTemplates.All(ft => !ft.HasChanges),
                "All FunctionTemplates must be Unchanged.");
        });
    }

    [TestMethod]
    public void TestDiff_IdenticalFunctionTemplate_WithNodeInInternalModules_NoChanges()
    {
        // Regression: FunctionTemplate with a node inside InternalModules must not show
        // as Modified when comparing two identical snapshots.
        TestHelper.RunInModelSystemContext("DiffIdenticalFTWithNode", (user, projectSession, msSession) =>
        {
            var boundary = msSession.ModelSystem.GlobalBoundary;
            var msHeader = msSession.ModelSystemHeader;

            Assert.IsTrue(msSession.AddFunctionTemplate(user, boundary, "MyTemplate",
                out var ft, out var error), error?.Message);
            Assert.IsNotNull(ft);

            // Add a module node inside InternalModules.
            Assert.IsTrue(msSession.AddNode(user, ft.InternalModules, "InnerNode", s_moduleType,
                new Rectangle(0, 0, 120, 50), out _, out error), error?.Message);

            Assert.IsTrue(msSession.Save(out error), error?.Message);

            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var left, out error),
                error?.Message);
            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var right, out error),
                error?.Message);

            var diff = ModelSystemComparer.Compare(left, right);

            Assert.IsFalse(diff.HasChanges,
                "Comparing identical snapshots must report no changes even with a node in InternalModules.");
            Assert.IsTrue(diff.GlobalBoundary.FunctionTemplates.All(ft2 => !ft2.HasChanges),
                "All FunctionTemplates must be Unchanged.");
        });
    }

    [TestMethod]
    public void TestFunctionTemplateId_IsPersistentAcrossSaveLoad()
    {
        TestHelper.RunInModelSystemContext("FunctionTemplateIdPersistent", (user, projectSession, msSession) =>
        {
            var boundary = msSession.ModelSystem.GlobalBoundary;
            var msHeader = msSession.ModelSystemHeader;

            Assert.IsTrue(msSession.AddFunctionTemplate(user, boundary, "MyTemplate",
                out var ft, out var error), error?.Message);
            Assert.IsNotNull(ft);
            var originalId = ft.Id;
            Assert.AreNotEqual(Guid.Empty, originalId, "FunctionTemplate.Id must not be Guid.Empty.");

            Assert.IsTrue(msSession.Save(out error), error?.Message);

            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var snap1, out error),
                error?.Message);
            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var snap2, out error),
                error?.Message);

            var id1 = snap1.GlobalBoundary.FunctionTemplates.Single().Id;
            var id2 = snap2.GlobalBoundary.FunctionTemplates.Single().Id;

            Assert.AreEqual(originalId, id1, "First snapshot FunctionTemplate Id must match the in-session Id.");
            Assert.AreEqual(originalId, id2, "Second snapshot FunctionTemplate Id must match the in-session Id.");
        });
    }

    [TestMethod]
    public void TestDiff_IdenticalFunctionTemplate_WithStartInInternalModules_NoChanges()
    {
        // Regression: a FunctionTemplate whose InternalModules boundary contains a Start node
        // must not be reported as Modified when comparing two identical snapshots.
        TestHelper.RunInModelSystemContext("DiffIdenticalFTWithStart", (user, projectSession, msSession) =>
        {
            var boundary = msSession.ModelSystem.GlobalBoundary;
            var msHeader = msSession.ModelSystemHeader;

            Assert.IsTrue(msSession.AddFunctionTemplate(user, boundary, "MyTemplate",
                out var ft, out var error), error?.Message);
            Assert.IsNotNull(ft);

            // Add a Start into InternalModules.
            Assert.IsTrue(msSession.AddModelSystemStart(user, ft.InternalModules, "InternalStart",
                new Rectangle(0, 0, 100, 50), out _, out error), error?.Message);

            Assert.IsTrue(msSession.Save(out error), error?.Message);

            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var left, out error),
                error?.Message);
            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var right, out error),
                error?.Message);

            var diff = ModelSystemComparer.Compare(left, right);

            Assert.IsFalse(diff.HasChanges,
                "Comparing identical snapshots must report no changes even with a Start in InternalModules." +
                $"\nActual FT kinds: {string.Join(", ", diff.GlobalBoundary.FunctionTemplates.Select(ft2 => ft2.Kind))}. " +
                $"Start diff kinds: {string.Join(", ", diff.GlobalBoundary.FunctionTemplates.SelectMany(ft2 => ft2.SubBoundary.Starts).Select(s => $"{s.LeftName}/{s.RightName}={s.Kind}"))}");
            Assert.IsTrue(diff.GlobalBoundary.FunctionTemplates.All(ft2 => !ft2.HasChanges),
                "All FunctionTemplates must be Unchanged.");
        });
    }

    [TestMethod]
    public void TestDiff_IdenticalFunctionTemplate_WithFunctionParameterAsLinkDest_NoChanges()
    {
        // Root-cause regression test for the "FunctionTemplate always shows as Modified" bug.
        //
        // Scenario: a node inside InternalModules has a [SubModule] hook (IsParameter=false)
        //   wired to a FunctionParameter.  CompareLink calls GetDestinationIds which returns
        //   FunctionParameter.Id.  Without Id persistence every load generates a fresh GUID
        //   → left-dest ≠ right-dest → link Modified → FT Modified.
        //
        // Fix: FunctionParameter.Save must write IdProperty; FunctionParameter.Load must read it.
        TestHelper.RunInModelSystemContext("DiffFTWithFPLinkDest", (user, projectSession, msSession) =>
        {
            var boundary = msSession.ModelSystem.GlobalBoundary;
            var msHeader = msSession.ModelSystemHeader;

            // Create a FunctionTemplate.
            Assert.IsTrue(msSession.AddFunctionTemplate(user, boundary, "MyTemplate",
                out var ft, out var error), error?.Message);
            Assert.IsNotNull(ft);

            // Add a FunctionParameter of type SimpleTestModule to the template.
            // The FP's Id is the critical piece that must survive save/load.
            Assert.IsTrue(msSession.AddFunctionParameter(user, ft, "MyParam",
                typeof(SimpleTestModule), new Rectangle(200, 0, 120, 50),
                out var fp, out error), error?.Message);
            Assert.IsNotNull(fp);

            // Add a SimpleSubModuleModule node inside InternalModules.
            // It has a [SubModule] hook "Inner Module" (IsParameter=false) of type SimpleTestModule.
            var innerType = typeof(SimpleSubModuleModule);
            Assert.IsTrue(msSession.AddNode(user, ft.InternalModules, "InnerNode", innerType,
                new Rectangle(0, 0, 120, 50), out var innerNode, out error), error?.Message);
            Assert.IsNotNull(innerNode);

            // Find the SubModule hook (IsParameter=false); this link will NOT be filtered
            // by CompareLinkSets, so destination Id matters.
            var hook = innerNode!.Hooks.FirstOrDefault(h => h.Name == "Inner Module");
            Assert.IsNotNull(hook, $"SimpleSubModuleModule must have an 'Inner Module' hook. " +
                $"Available: {string.Join(", ", innerNode.Hooks.Select(h => h.Name))}");

            // Link the InternalModules node's SubModule hook → FunctionParameter.
            Assert.IsTrue(msSession.AddLink(user, innerNode, hook, fp,
                out _, out error), error?.Message);

            Assert.IsTrue(msSession.Save(out error), error?.Message);

            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var left, out error),
                error?.Message);
            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var right, out error),
                error?.Message);

            var diff = ModelSystemComparer.Compare(left, right);

            var ftDiff = diff.GlobalBoundary.FunctionTemplates.Single();
            var changedLinks = ftDiff.SubBoundary.Links.Where(l => l.Kind != ElementDiffKind.Unchanged).ToList();

            Assert.IsFalse(diff.HasChanges,
                $"Comparing identical snapshots must report no changes even with a FP-destination link.\n" +
                $"FT Kind={ftDiff.Kind}. Changed links ({changedLinks.Count}): " +
                string.Join(", ", changedLinks.Select(l => $"{l.OriginNodeName}/{l.HookName}={l.Kind} " +
                    $"leftDests=[{string.Join(",", l.LeftDestinationIds)}] " +
                    $"rightDests=[{string.Join(",", l.RightDestinationIds)}]")));
        });
    }

    // ── Direct comparer unit tests (no I/O) ─────────────────────────────────


}
