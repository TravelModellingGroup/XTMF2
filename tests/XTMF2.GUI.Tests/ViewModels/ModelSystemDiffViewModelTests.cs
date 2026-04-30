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
using System.Collections.Generic;
using System.Linq;
using XTMF2.Diff;
using XTMF2.GUI.Tests.Modules;
using XTMF2.GUI.ViewModels;
using XTMF2.ModelSystemConstruct;

namespace XTMF2.GUI.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="ModelSystemDiffViewModel"/>, <see cref="DiffBoundaryItem"/>,
/// and <see cref="DiffElementItem"/>.
/// The tests mix two strategies:
/// <list type="bullet">
///   <item>Pure constructor tests that build <see cref="ModelSystemDiff"/> directly.</item>
///   <item>Integration tests that go through the real save/load pipeline via
///   <see cref="TestGuiHelper"/>.</item>
/// </list>
/// </summary>
[TestClass]
public class ModelSystemDiffViewModelTests
{
    private static readonly Type s_moduleType = typeof(SimpleGuiTestModule);

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal <see cref="BoundaryDiff"/> with no elements.
    /// </summary>
    private static BoundaryDiff EmptyBoundaryDiff(string name, ElementDiffKind kind = ElementDiffKind.Unchanged)
        => new BoundaryDiff(
            Guid.NewGuid(), Guid.NewGuid(), kind,
            name, name,
            Array.Empty<NodeDiff>(), Array.Empty<NodeDiff>(),
            Array.Empty<LinkDiff>(), Array.Empty<CommentBlockDiff>(),
            Array.Empty<FunctionTemplateDiff>(),
            Array.Empty<BoundaryDiff>());

    private static ModelSystemDiff EmptyDiff(string leftName = "Left", string rightName = "Right")
        => new ModelSystemDiff(leftName, rightName, EmptyBoundaryDiff("Global"));

    private static NodeDiff MakeNodeDiff(ElementDiffKind kind, string? leftName = null, string? rightName = null)
    {
        var id = Guid.NewGuid();
        var typeName = s_moduleType.AssemblyQualifiedName;
        return new NodeDiff(id, kind, leftName, rightName, typeName, typeName, null, null, false, false);
    }

    // ── Title & name tests ───────────────────────────────────────────────────

    [TestMethod]
    public void Title_ContainsBothNames()
    {
        var diff = EmptyDiff("LeftMS", "RightMS");
        var vm = new ModelSystemDiffViewModel(diff);
        StringAssert.Contains(vm.Title, "LeftMS");
        StringAssert.Contains(vm.Title, "RightMS");
    }

    [TestMethod]
    public void LeftName_MatchesDiff()
    {
        var diff = EmptyDiff("BaseVersion", "NewVersion");
        var vm = new ModelSystemDiffViewModel(diff);
        Assert.AreEqual("BaseVersion", vm.LeftName);
    }

    [TestMethod]
    public void RightName_MatchesDiff()
    {
        var diff = EmptyDiff("BaseVersion", "NewVersion");
        var vm = new ModelSystemDiffViewModel(diff);
        Assert.AreEqual("NewVersion", vm.RightName);
    }

    // ── HasChanges ───────────────────────────────────────────────────────────

    [TestMethod]
    public void HasChanges_FalseForEmptyIdenticalDiff()
    {
        var diff = EmptyDiff();
        var vm = new ModelSystemDiffViewModel(diff);
        Assert.IsFalse(vm.HasChanges, "An empty diff must report no changes.");
    }

    [TestMethod]
    public void HasChanges_TrueWhenNodeAdded()
    {
        var nodeDiff = MakeNodeDiff(ElementDiffKind.Added, rightName: "AddedNode");
        var boundary = new BoundaryDiff(
            Guid.NewGuid(), Guid.NewGuid(), ElementDiffKind.Unchanged,
            "Global", "Global",
            Array.Empty<NodeDiff>(),
            new[] { nodeDiff },
            Array.Empty<LinkDiff>(), Array.Empty<CommentBlockDiff>(),
            Array.Empty<FunctionTemplateDiff>(),
            Array.Empty<BoundaryDiff>());
        var diff = new ModelSystemDiff("Left", "Right", boundary);
        var vm = new ModelSystemDiffViewModel(diff);
        Assert.IsTrue(vm.HasChanges, "HasChanges must be true when a node was added.");
    }

    // ── RootItems ────────────────────────────────────────────────────────────

    [TestMethod]
    public void RootItems_ContainsExactlyOneItem_AfterConstruction()
    {
        var vm = new ModelSystemDiffViewModel(EmptyDiff());
        Assert.HasCount(1, vm.RootItems,
            "RootItems must contain exactly one item (the global boundary).");
    }

    [TestMethod]
    public void RootItems_SingleItemIsGlobalBoundary()
    {
        var vm = new ModelSystemDiffViewModel(EmptyDiff("A", "B"));
        var root = vm.RootItems.Single();
        Assert.IsInstanceOfType(root, typeof(DiffBoundaryItem));
    }

    // ── ShowUnchanged toggle ─────────────────────────────────────────────────

    [TestMethod]
    public void ShowUnchanged_DefaultIsFalse()
    {
        var vm = new ModelSystemDiffViewModel(EmptyDiff());
        Assert.IsFalse(vm.ShowUnchanged, "Default value of ShowUnchanged must be false.");
    }

    [TestMethod]
    public void ShowUnchanged_False_ExcludesUnchangedNodes()
    {
        var unchangedNode = MakeNodeDiff(ElementDiffKind.Unchanged, "SomeName", "SomeName");
        var boundary = new BoundaryDiff(
            Guid.NewGuid(), Guid.NewGuid(), ElementDiffKind.Unchanged,
            "Global", "Global",
            Array.Empty<NodeDiff>(),
            new[] { unchangedNode },
            Array.Empty<LinkDiff>(), Array.Empty<CommentBlockDiff>(),
            Array.Empty<FunctionTemplateDiff>(),
            Array.Empty<BoundaryDiff>());
        var diff = new ModelSystemDiff("L", "R", boundary);
        var vm = new ModelSystemDiffViewModel(diff) { ShowUnchanged = false };

        var root = (DiffBoundaryItem)vm.RootItems.Single();
        Assert.HasCount(0, root.Children,
            "ShowUnchanged=false must exclude unchanged nodes.");
    }

    [TestMethod]
    public void ShowUnchanged_True_IncludesUnchangedNodes()
    {
        var unchangedNode = MakeNodeDiff(ElementDiffKind.Unchanged, "SomeName", "SomeName");
        var boundary = new BoundaryDiff(
            Guid.NewGuid(), Guid.NewGuid(), ElementDiffKind.Unchanged,
            "Global", "Global",
            Array.Empty<NodeDiff>(),
            new[] { unchangedNode },
            Array.Empty<LinkDiff>(), Array.Empty<CommentBlockDiff>(),
            Array.Empty<FunctionTemplateDiff>(),
            Array.Empty<BoundaryDiff>());
        var diff = new ModelSystemDiff("L", "R", boundary);
        var vm = new ModelSystemDiffViewModel(diff) { ShowUnchanged = true };

        var root = (DiffBoundaryItem)vm.RootItems.Single();
        Assert.HasCount(1, root.Children,
            "ShowUnchanged=true must include unchanged nodes.");
    }

    [TestMethod]
    public void ShowUnchanged_Toggle_RebuildsTree()
    {
        var unchangedNode = MakeNodeDiff(ElementDiffKind.Unchanged, "N", "N");
        var boundary = new BoundaryDiff(
            Guid.NewGuid(), Guid.NewGuid(), ElementDiffKind.Unchanged, "G", "G",
            Array.Empty<NodeDiff>(), new[] { unchangedNode },
            Array.Empty<LinkDiff>(), Array.Empty<CommentBlockDiff>(),
            Array.Empty<FunctionTemplateDiff>(),
            Array.Empty<BoundaryDiff>());
        var diff = new ModelSystemDiff("L", "R", boundary);
        var vm = new ModelSystemDiffViewModel(diff);

        // Initially no unchanged items visible.
        var root = (DiffBoundaryItem)vm.RootItems.Single();
        Assert.HasCount(0, root.Children, "Initially ShowUnchanged=false must exclude unchanged nodes.");

        // Toggle on.
        vm.ShowUnchanged = true;
        root = (DiffBoundaryItem)vm.RootItems.Single();
        Assert.HasCount(1, root.Children, "ShowUnchanged=true must include unchanged nodes.");

        // Toggle off again.
        vm.ShowUnchanged = false;
        root = (DiffBoundaryItem)vm.RootItems.Single();
        Assert.HasCount(0, root.Children, "ShowUnchanged=false must exclude unchanged nodes again.");
    }

    // ── DiffBoundaryItem ───────────────────────────────────────────────────

    [TestMethod]
    public void DiffBoundaryItem_DisplayName_MatchesBoundaryName()
    {
        var item = new DiffBoundaryItem(EmptyBoundaryDiff("MyBoundary"), false);
        Assert.AreEqual("MyBoundary", item.DisplayName);
    }

    [TestMethod]
    public void DiffBoundaryItem_HasChanges_FalseForEmptyUnchanged()
    {
        var item = new DiffBoundaryItem(EmptyBoundaryDiff("G", ElementDiffKind.Unchanged), false);
        Assert.IsFalse(item.HasChanges);
    }

    [TestMethod]
    public void DiffBoundaryItem_HasChanges_TrueForAddedBoundary()
    {
        var item = new DiffBoundaryItem(EmptyBoundaryDiff("G", ElementDiffKind.Added), false);
        Assert.IsTrue(item.HasChanges);
    }

    [TestMethod]
    public void DiffBoundaryItem_ChangeCount_ReflectsDirectChanges()
    {
        var added = MakeNodeDiff(ElementDiffKind.Added, rightName: "A");
        var removed = MakeNodeDiff(ElementDiffKind.Removed, leftName: "R");
        var unchanged = MakeNodeDiff(ElementDiffKind.Unchanged, "U", "U");
        var boundary = new BoundaryDiff(
            Guid.NewGuid(), Guid.NewGuid(), ElementDiffKind.Unchanged, "G", "G",
            Array.Empty<NodeDiff>(),
            new[] { added, removed, unchanged },
            Array.Empty<LinkDiff>(), Array.Empty<CommentBlockDiff>(),
            Array.Empty<FunctionTemplateDiff>(),
            Array.Empty<BoundaryDiff>());
        var item = new DiffBoundaryItem(boundary, true);
        Assert.AreEqual(2, item.ChangeCount, "ChangeCount must count only changed elements.");
    }

    [TestMethod]
    public void DiffBoundaryItem_Children_ShowUnchangedFalse_OnlyChangedNodes()
    {
        var added = MakeNodeDiff(ElementDiffKind.Added, rightName: "A");
        var unchanged = MakeNodeDiff(ElementDiffKind.Unchanged, "U", "U");
        var boundary = new BoundaryDiff(
            Guid.NewGuid(), Guid.NewGuid(), ElementDiffKind.Unchanged, "G", "G",
            Array.Empty<NodeDiff>(), new[] { added, unchanged },
            Array.Empty<LinkDiff>(), Array.Empty<CommentBlockDiff>(),
            Array.Empty<FunctionTemplateDiff>(),
            Array.Empty<BoundaryDiff>());
        var item = new DiffBoundaryItem(boundary, showUnchanged: false);
        Assert.HasCount(1, item.Children, "Only the added node should be in Children.");
        Assert.IsInstanceOfType(item.Children[0], typeof(DiffElementItem));
    }

    [TestMethod]
    public void DiffBoundaryItem_NestedSubBoundary_InChildren()
    {
        var sub = EmptyBoundaryDiff("Child", ElementDiffKind.Added);
        var parent = new BoundaryDiff(
            Guid.NewGuid(), Guid.NewGuid(), ElementDiffKind.Unchanged, "Parent", "Parent",
            Array.Empty<NodeDiff>(), Array.Empty<NodeDiff>(), 
            Array.Empty<LinkDiff>(), Array.Empty<CommentBlockDiff>(),
            Array.Empty<FunctionTemplateDiff>(),
            new[] { sub });
        var item = new DiffBoundaryItem(parent, showUnchanged: false);
        Assert.HasCount(1, item.Children, "Added sub-boundary must appear in Children.");
        Assert.IsInstanceOfType(item.Children[0], typeof(DiffBoundaryItem));
    }

    // ── DiffElementItem – type tags ─────────────────────────────────────────

    [TestMethod]
    public void DiffElementItem_NodeTypeTag_IsNode()
    {
        var node = MakeNodeDiff(ElementDiffKind.Added, rightName: "MyNode");
        var item = new DiffElementItem(node, isStart: false);
        Assert.AreEqual("Node", item.TypeTag);
    }

    [TestMethod]
    public void DiffElementItem_StartTypeTag_IsStart()
    {
        var node = MakeNodeDiff(ElementDiffKind.Added, rightName: "Start1");
        var item = new DiffElementItem(node, isStart: true);
        Assert.AreEqual("Start", item.TypeTag);
    }

    [TestMethod]
    public void DiffElementItem_LinkTypeTag_IsLink()
    {
        var originId = Guid.NewGuid();
        var link = new LinkDiff(Guid.NewGuid(), ElementDiffKind.Added,
            originId, "MyHook", "OriginNode", Array.Empty<Guid>(), new[] { Guid.NewGuid() }, false, false);
        var item = new DiffElementItem(link);
        Assert.AreEqual("Link", item.TypeTag);
    }

    [TestMethod]
    public void DiffElementItem_CommentTypeTag_IsComment()
    {
        var comment = new CommentBlockDiff(Guid.NewGuid(), ElementDiffKind.Added, null, "Hello world");
        var item = new DiffElementItem(comment);
        Assert.AreEqual("Comment", item.TypeTag);
    }

    // ── DiffElementItem – labels ─────────────────────────────────────────────

    [TestMethod]
    public void DiffElementItem_AddedNode_LabelIsRightName()
    {
        var node = MakeNodeDiff(ElementDiffKind.Added, rightName: "AddedNode");
        var item = new DiffElementItem(node, isStart: false);
        Assert.AreEqual("AddedNode", item.Label);
    }

    [TestMethod]
    public void DiffElementItem_RemovedNode_LabelIsLeftName()
    {
        var node = MakeNodeDiff(ElementDiffKind.Removed, leftName: "RemovedNode");
        var item = new DiffElementItem(node, isStart: false);
        Assert.AreEqual("RemovedNode", item.Label);
    }

    [TestMethod]
    public void DiffElementItem_Link_LabelIsHookName()
    {
        var link = new LinkDiff(Guid.NewGuid(), ElementDiffKind.Unchanged,
            Guid.NewGuid(), "HookName", "OriginNode", new[] { Guid.NewGuid() }, new[] { Guid.NewGuid() }, false, false);
        var item = new DiffElementItem(link);
        Assert.AreEqual("OriginNode › HookName", item.Label);
    }

    // ── DiffElementItem – ChangeDescription ─────────────────────────────────

    [TestMethod]
    public void DiffElementItem_UnchangedNode_ChangeDescriptionIsNull()
    {
        var node = MakeNodeDiff(ElementDiffKind.Unchanged, "N", "N");
        var item = new DiffElementItem(node, isStart: false);
        Assert.IsNull(item.ChangeDescription,
            "An unchanged node must have a null ChangeDescription.");
    }

    [TestMethod]
    public void DiffElementItem_ModifiedNode_ChangeDescriptionNotNull()
    {
        var id = Guid.NewGuid();
        var typeName = s_moduleType.AssemblyQualifiedName;
        // Same type, different name → Modified.
        var node = new NodeDiff(id, ElementDiffKind.Modified,
            "OldName", "NewName", typeName, typeName, null, null, false, false);
        var item = new DiffElementItem(node, isStart: false);
        Assert.IsNotNull(item.ChangeDescription,
            "A modified node must have a non-null ChangeDescription.");
        StringAssert.Contains(item.ChangeDescription, "OldName");
        StringAssert.Contains(item.ChangeDescription, "NewName");
    }

    [TestMethod]
    public void DiffElementItem_ModifiedComment_ChangeDescriptionNotNull()
    {
        var comment = new CommentBlockDiff(Guid.NewGuid(), ElementDiffKind.Modified,
            "Before", "After");
        var item = new DiffElementItem(comment);
        Assert.IsNotNull(item.ChangeDescription);
    }

    [TestMethod]
    public void DiffElementItem_UnchangedComment_ChangeDescriptionIsNull()
    {
        var comment = new CommentBlockDiff(Guid.NewGuid(), ElementDiffKind.Unchanged,
            "Same text", "Same text");
        var item = new DiffElementItem(comment);
        Assert.IsNull(item.ChangeDescription);
    }

    // ── Children – always empty for leaf items ───────────────────────────────

    [TestMethod]
    public void DiffElementItem_Children_AlwaysEmpty()
    {
        var node = MakeNodeDiff(ElementDiffKind.Added, rightName: "X");
        var item = new DiffElementItem(node, isStart: false);
        Assert.IsEmpty(item.Children, "Leaf items must have empty Children.");
    }

    // ── Integration: ViewModel built from real diff pipeline ─────────────────

    [TestMethod]
    public void ViewModel_IdenticalSnapshots_HasChangesFalse()
    {
        TestGuiHelper.RunInModelSystemContext("VMIdentical", (user, projectSession, msSession) =>
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
            var vm = new ModelSystemDiffViewModel(diff);

            Assert.IsFalse(vm.HasChanges,
                "ViewModel built from identical snapshots must report no changes.");
        });
    }

    [TestMethod]
    public void ViewModel_AddedNode_HasChangesTrue_AndTreeContainsAddedItem()
    {
        TestGuiHelper.RunInModelSystemContext("VMAddedNode", (user, projectSession, msSession) =>
        {
            var boundary = msSession.ModelSystem.GlobalBoundary;
            var msHeader = msSession.ModelSystemHeader;

            Assert.IsTrue(msSession.Save(out var error), error?.Message);
            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var left, out error),
                error?.Message);

            Assert.IsTrue(msSession.AddNode(user, boundary, "NewNode", s_moduleType,
                new Rectangle(0, 0, 100, 50), out _, out error), error?.Message);
            Assert.IsTrue(msSession.Save(out error), error?.Message);
            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var right, out error),
                error?.Message);

            var diff = ModelSystemComparer.Compare(left, right);
            var vm = new ModelSystemDiffViewModel(diff) { ShowUnchanged = true };

            Assert.IsTrue(vm.HasChanges);

            var root = (DiffBoundaryItem)vm.RootItems.Single();
            var addedItems = root.Children
                .OfType<DiffElementItem>()
                .Where(e => e.Kind == ElementDiffKind.Added)
                .ToList();
            Assert.HasCount(1, addedItems,
                "Exactly one Added item must appear in the tree.");
            Assert.AreEqual("Node", addedItems[0].TypeTag);
        });
    }

    [TestMethod]
    public void ViewModel_RemovedNode_TreeContainsRemovedItem()
    {
        TestGuiHelper.RunInModelSystemContext("VMRemovedNode", (user, projectSession, msSession) =>
        {
            var boundary = msSession.ModelSystem.GlobalBoundary;
            var msHeader = msSession.ModelSystemHeader;

            Assert.IsTrue(msSession.AddNode(user, boundary, "ToRemove", s_moduleType,
                new Rectangle(0, 0, 100, 50), out var node, out var error), error?.Message);
            Assert.IsNotNull(node);
            Assert.IsTrue(msSession.Save(out error), error?.Message);
            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var left, out error),
                error?.Message);

            Assert.IsTrue(msSession.RemoveNode(user, node, out error), error?.Message);
            Assert.IsTrue(msSession.Save(out error), error?.Message);
            Assert.IsTrue(projectSession.LoadModelSystemForDiff(user, msHeader, out var right, out error),
                error?.Message);

            var diff = ModelSystemComparer.Compare(left, right);
            var vm = new ModelSystemDiffViewModel(diff) { ShowUnchanged = true };

            var root = (DiffBoundaryItem)vm.RootItems.Single();
            var removedItems = root.Children
                .OfType<DiffElementItem>()
                .Where(e => e.Kind == ElementDiffKind.Removed)
                .ToList();
            Assert.HasCount(1, removedItems,
                "Exactly one Removed item must appear in the tree.");
        });
    }
}
