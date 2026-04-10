/*
    Copyright 2026, Travel Modelling Group, University of Toronto

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
using System.Linq;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Editing;
using XTMF2.ModelSystemConstruct;
using XTMF2.RuntimeModules;
using XTMF2.UnitTests.Modules;

namespace XTMF2.UnitTests.Editing;

/// <summary>
/// Tests for <see cref="ModelSystemSession.ExtractToFunctionTemplate"/>.
/// </summary>
[TestClass]
public class TestExtractToFunctionTemplate
{
    // ── Locations ──────────────────────────────────────────────────────────
    private static readonly Rectangle CallerLoc   = new(0,   0,  160, 60);
    private static readonly Rectangle EntryLoc    = new(200, 0,  160, 60);
    private static readonly Rectangle ExtDestLoc  = new(400, 0,  160, 60);
    private static readonly Rectangle FtLoc       = new(200, 100, 300, 200);
    private static readonly Rectangle FiLoc       = new(200, 0,  160, 60);

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal "caller → entryNode → externalDest" graph and extracts
    /// entryNode into a FunctionTemplate.
    /// </summary>
    private static bool RunExtract(
        User user, ModelSystemSession ms,
        Node caller, NodeHook callerHook,
        Node entryNode,
        Node externalDest, NodeHook entryHook,
        out FunctionTemplate? ft,
        out FunctionInstance? fi,
        out CommandError? error)
    {
        var boundary = ms.ModelSystem.GlobalBoundary;
        // Wire caller → entryNode.
        Assert.IsTrue(ms.AddLink(user, caller, callerHook, entryNode,
            out _, out error), error?.Message);
        // Wire entryNode → externalDest (when hook is provided).
        if (entryHook is not null)
        {
            Assert.IsTrue(ms.AddLink(user, entryNode, entryHook, externalDest,
                out _, out error), error?.Message);
        }
        return ms.ExtractToFunctionTemplate(
            user, boundary,
            new[] { entryNode },
            "MyTemplate", "MyInstance",
            FtLoc, FiLoc,
            out ft, out fi, out error);
    }

    // ── Validation failures ────────────────────────────────────────────────

    [TestMethod]
    public void ExtractToFunctionTemplate_EmptySelection_Fails()
    {
        TestHelper.RunInModelSystemContext(
            nameof(ExtractToFunctionTemplate_EmptySelection_Fails),
            (user, pSess, ms) =>
            {
                var boundary = ms.ModelSystem.GlobalBoundary;
                Assert.IsFalse(ms.ExtractToFunctionTemplate(
                    user, boundary, new Node[0],
                    "T", "FI", FtLoc, FiLoc,
                    out _, out _, out var error));
                Assert.IsNotNull(error);
            });
    }

    [TestMethod]
    public void ExtractToFunctionTemplate_NoExternalIncomingLink_Fails()
    {
        TestHelper.RunInModelSystemContext(
            nameof(ExtractToFunctionTemplate_NoExternalIncomingLink_Fails),
            (user, pSess, ms) =>
            {
                var boundary = ms.ModelSystem.GlobalBoundary;
                Assert.IsTrue(ms.AddNode(user, boundary, "Node", typeof(SimpleTestModule),
                    EntryLoc, out var node, out var error), error?.Message);
                // No link pointing to node from outside.
                Assert.IsFalse(ms.ExtractToFunctionTemplate(
                    user, boundary, new[] { node! },
                    "T", "FI", FtLoc, FiLoc,
                    out _, out _, out error));
                Assert.IsNotNull(error);
            });
    }

    [TestMethod]
    public void ExtractToFunctionTemplate_MultipleExternalIncomingLinks_Fails()
    {
        TestHelper.RunInModelSystemContext(
            nameof(ExtractToFunctionTemplate_MultipleExternalIncomingLinks_Fails),
            (user, pSess, ms) =>
            {
                var boundary = ms.ModelSystem.GlobalBoundary;
                // entryNode: IgnoreResult<string> (IAction)
                Assert.IsTrue(ms.AddNode(user, boundary, "Entry", typeof(IgnoreResult<string>),
                    EntryLoc, out var entryNode, out var error), error?.Message);
                // Two callers pointing to entryNode.
                Assert.IsTrue(ms.AddModelSystemStart(user, boundary, "Start1",
                    CallerLoc, out var start1, out error), error?.Message);
                Assert.IsTrue(ms.AddModelSystemStart(user, boundary, "Start2",
                    new Rectangle(0, 80, 160, 60), out var start2, out error), error?.Message);
                Assert.IsTrue(ms.AddLink(user, start1,
                    TestHelper.GetHook(start1.Hooks, "ToExecute"), entryNode,
                    out _, out error), error?.Message);
                Assert.IsTrue(ms.AddLink(user, start2,
                    TestHelper.GetHook(start2.Hooks, "ToExecute"), entryNode,
                    out _, out error), error?.Message);

                Assert.IsFalse(ms.ExtractToFunctionTemplate(
                    user, boundary, new[] { entryNode! },
                    "T", "FI", FtLoc, FiLoc,
                    out _, out _, out error));
                Assert.IsNotNull(error);
            });
    }

    [TestMethod]
    public void ExtractToFunctionTemplate_DuplicateTemplateName_Fails()
    {
        TestHelper.RunInModelSystemContext(
            nameof(ExtractToFunctionTemplate_DuplicateTemplateName_Fails),
            (user, pSess, ms) =>
            {
                var boundary = ms.ModelSystem.GlobalBoundary;
                // Pre-create a template with the same name.
                Assert.IsTrue(ms.AddFunctionTemplate(user, boundary, "MyTemplate",
                    out _, out var error), error?.Message);

                Assert.IsTrue(ms.AddNode(user, boundary, "Entry", typeof(SimpleTestModule),
                    EntryLoc, out var entry, out error), error?.Message);
                Assert.IsTrue(ms.AddModelSystemStart(user, boundary, "Start",
                    CallerLoc, out var start, out error), error?.Message);
                Assert.IsTrue(ms.AddLink(user, start,
                    TestHelper.GetHook(start.Hooks, "ToExecute"), entry,
                    out _, out error), error?.Message);

                Assert.IsFalse(ms.ExtractToFunctionTemplate(
                    user, boundary, new[] { entry! },
                    "MyTemplate", "FI", FtLoc, FiLoc,
                    out _, out _, out error));
                Assert.IsNotNull(error);
            });
    }

    // ── Basic extraction (no external outgoing links) ─────────────────────

    [TestMethod]
    public void ExtractToFunctionTemplate_Basic_Succeeds()
    {
        TestHelper.RunInModelSystemContext(
            nameof(ExtractToFunctionTemplate_Basic_Succeeds),
            (user, pSess, ms) =>
            {
                var boundary = ms.ModelSystem.GlobalBoundary;
                Assert.IsTrue(ms.AddNode(user, boundary, "Entry", typeof(SimpleTestModule),
                    EntryLoc, out var entryNode, out var error), error?.Message);
                Assert.IsTrue(ms.AddModelSystemStart(user, boundary, "Start",
                    CallerLoc, out var start, out error), error?.Message);
                // Wire start → entryNode.
                Assert.IsTrue(ms.AddLink(user, start,
                    TestHelper.GetHook(start.Hooks, "ToExecute"), entryNode,
                    out _, out error), error?.Message);

                Assert.IsTrue(ms.ExtractToFunctionTemplate(
                    user, boundary, new[] { entryNode! },
                    "MyTemplate", "MyInstance",
                    FtLoc, FiLoc,
                    out var ft, out var fi, out error), error?.Message);

                Assert.IsNotNull(ft);
                Assert.IsNotNull(fi);

                // The template should contain entryNode.
                Assert.HasCount(1, ft!.InternalModules.Modules,
                    "entryNode must be moved into InternalModules.");
                Assert.AreSame(entryNode, ft.InternalModules.Modules[0]);

                // Entry node is designated.
                Assert.AreSame(entryNode, ft.EntryNode);

                // No FunctionParameters (no external outgoing links).
                Assert.IsEmpty(ft.FunctionParameters);

                // FunctionInstance is in the boundary.
                Assert.HasCount(1, boundary.FunctionInstances);
                Assert.AreSame(fi, boundary.FunctionInstances[0]);

                // entryNode must have left the boundary.
                Assert.IsFalse(boundary.Modules.Contains(entryNode),
                    "entryNode must no longer be in the parent boundary.");

                // The incoming link (start → fi) must be in place.
                var incomingLink = boundary.Links
                    .OfType<SingleLink>()
                    .FirstOrDefault(l => l.Origin == start && l.Destination == fi);
                Assert.IsNotNull(incomingLink,
                    "start must now link to the FunctionInstance.");
            });
    }

    // ── Extraction with external outgoing link (creates FunctionParameter) ─

    [TestMethod]
    public void ExtractToFunctionTemplate_WithFunctionParameter_Succeeds()
    {
        TestHelper.RunInModelSystemContext(
            nameof(ExtractToFunctionTemplate_WithFunctionParameter_Succeeds),
            (user, pSess, ms) =>
            {
                var boundary = ms.ModelSystem.GlobalBoundary;
                // Graph: start → entryNode (IgnoreResult<string>) → externalDest (SimpleTestModule)
                Assert.IsTrue(ms.AddNode(user, boundary, "Entry", typeof(IgnoreResult<string>),
                    EntryLoc, out var entryNode, out var error), error?.Message);
                Assert.IsTrue(ms.AddNode(user, boundary, "ExternalDest", typeof(SimpleTestModule),
                    ExtDestLoc, out var externalDest, out var error2), error2?.Message);
                Assert.IsTrue(ms.AddModelSystemStart(user, boundary, "Start",
                    CallerLoc, out var start, out error), error?.Message);

                Assert.IsTrue(ms.AddLink(user, start,
                    TestHelper.GetHook(start.Hooks, "ToExecute"), entryNode,
                    out _, out error), error?.Message);
                Assert.IsTrue(ms.AddLink(user, entryNode,
                    TestHelper.GetHook(entryNode!.Hooks, "To Ignore"), externalDest,
                    out _, out error), error?.Message);

                Assert.IsTrue(ms.ExtractToFunctionTemplate(
                    user, boundary, new[] { entryNode },
                    "MyTemplate", "MyInstance",
                    FtLoc, FiLoc,
                    out var ft, out var fi, out error), error?.Message);

                // ── Verify FunctionTemplate internals ──────────────────────
                Assert.HasCount(1, ft!.InternalModules.Modules,
                    "entryNode should be in InternalModules.");
                Assert.AreSame(entryNode, ft.InternalModules.Modules[0]);
                Assert.AreSame(entryNode, ft.EntryNode);

                // One FunctionParameter for "To Ignore".
                Assert.HasCount(1, ft.FunctionParameters);
                var fp = ft.FunctionParameters[0];
                Assert.AreEqual("To Ignore", fp.Name);

                // Internal link: entryNode → fp (inside InternalModules).
                var internalLink = ft.InternalModules.Links
                    .OfType<SingleLink>()
                    .FirstOrDefault(l => l.Origin == entryNode && l.Destination == fp);
                Assert.IsNotNull(internalLink,
                    "InternalModules must have a link from entryNode to the FunctionParameter.");

                // ── Verify parent boundary ─────────────────────────────────
                // entryNode gone from parent.
                Assert.IsFalse(boundary.Modules.Contains(entryNode),
                    "entryNode must be in InternalModules, not the parent boundary.");

                // externalDest stays in parent.
                Assert.IsTrue(boundary.Modules.Contains(externalDest!),
                    "externalDest must remain in the parent boundary.");

                // start → fi link.
                var toFi = boundary.Links
                    .OfType<SingleLink>()
                    .FirstOrDefault(l => l.Origin == start && l.Destination == fi);
                Assert.IsNotNull(toFi, "start must link to FunctionInstance.");

                // fi → externalDest link via the FunctionParameterHook.
                var fiLink = boundary.Links
                    .OfType<SingleLink>()
                    .FirstOrDefault(l => l.Origin == fi
                        && l.OriginHook is FunctionParameterHook fph
                        && ReferenceEquals(fph.Parameter, fp)
                        && l.Destination == externalDest);
                Assert.IsNotNull(fiLink,
                    "FunctionInstance must link to externalDest via the FunctionParameterHook.");
            });
    }

    // ── Multi-node extraction with internal link ───────────────────────────

    [TestMethod]
    public void ExtractToFunctionTemplate_MultipleNodes_InternalLinkPreserved()
    {
        TestHelper.RunInModelSystemContext(
            nameof(ExtractToFunctionTemplate_MultipleNodes_InternalLinkPreserved),
            (user, pSess, ms) =>
            {
                var boundary = ms.ModelSystem.GlobalBoundary;

                // Graph: start → nodeA (IgnoreResult<string>) → nodeB (IgnoreResult<string>) → externalDest
                Assert.IsTrue(ms.AddNode(user, boundary, "NodeA", typeof(IgnoreResult<string>),
                    EntryLoc, out var nodeA, out var error), error?.Message);
                Assert.IsTrue(ms.AddNode(user, boundary, "NodeB", typeof(IgnoreResult<string>),
                    new Rectangle(300, 0, 160, 60), out var nodeB, out var error2), error2?.Message);
                Assert.IsTrue(ms.AddNode(user, boundary, "ExternalDest", typeof(SimpleTestModule),
                    ExtDestLoc, out var externalDest, out var error3), error3?.Message);
                Assert.IsTrue(ms.AddModelSystemStart(user, boundary, "Start",
                    CallerLoc, out var start, out error), error?.Message);

                // start → nodeA
                Assert.IsTrue(ms.AddLink(user, start,
                    TestHelper.GetHook(start.Hooks, "ToExecute"), nodeA,
                    out _, out error), error?.Message);
                // nodeA → nodeB (internal link to move into InternalModules)
                Assert.IsTrue(ms.AddLink(user, nodeA,
                    TestHelper.GetHook(nodeA!.Hooks, "To Ignore"), nodeB,
                    out _, out error), error?.Message);
                // nodeB → externalDest (external outgoing, becomes FP)
                Assert.IsTrue(ms.AddLink(user, nodeB,
                    TestHelper.GetHook(nodeB!.Hooks, "To Ignore"), externalDest,
                    out _, out error), error?.Message);

                Assert.IsTrue(ms.ExtractToFunctionTemplate(
                    user, boundary, new[] { nodeA, nodeB },
                    "MyTemplate", "MyInstance",
                    FtLoc, FiLoc,
                    out var ft, out var fi, out error), error?.Message);

                // Both nodes in InternalModules.
                Assert.HasCount(2, ft!.InternalModules.Modules,
                    "Both nodeA and nodeB must be in InternalModules.");

                // Internal link nodeA → nodeB is preserved inside InternalModules.
                var internalAB = ft.InternalModules.Links
                    .OfType<SingleLink>()
                    .FirstOrDefault(l => l.Origin == nodeA && l.Destination == nodeB);
                Assert.IsNotNull(internalAB,
                    "The nodeA→nodeB link must have moved into InternalModules.");

                // One FP for nodeB's "To Ignore" hook → externalDest.
                Assert.HasCount(1, ft.FunctionParameters);
                var fp = ft.FunctionParameters[0];

                // Internal link nodeB → fp.
                var internalBFP = ft.InternalModules.Links
                    .OfType<SingleLink>()
                    .FirstOrDefault(l => l.Origin == nodeB && l.Destination == fp);
                Assert.IsNotNull(internalBFP,
                    "nodeB must link to the FunctionParameter inside InternalModules.");

                // Parent boundary has start→fi and fi→externalDest.
                Assert.HasCount(2, boundary.Links,
                    "Boundary must have exactly start→fi and fi→externalDest links.");

                var toExternal = boundary.Links
                    .OfType<SingleLink>()
                    .FirstOrDefault(l => l.Origin == fi && l.Destination == externalDest);
                Assert.IsNotNull(toExternal,
                    "FunctionInstance must link to externalDest.");
            });
    }

    // ── Entry node is set correctly ────────────────────────────────────────

    [TestMethod]
    public void ExtractToFunctionTemplate_EntryNodeSetToIncomingLinkDestination()
    {
        TestHelper.RunInModelSystemContext(
            nameof(ExtractToFunctionTemplate_EntryNodeSetToIncomingLinkDestination),
            (user, pSess, ms) =>
            {
                var boundary = ms.ModelSystem.GlobalBoundary;
                Assert.IsTrue(ms.AddNode(user, boundary, "Entry", typeof(SimpleTestModule),
                    EntryLoc, out var entry, out var error), error?.Message);
                Assert.IsTrue(ms.AddModelSystemStart(user, boundary, "Start",
                    CallerLoc, out var start, out error), error?.Message);
                Assert.IsTrue(ms.AddLink(user, start,
                    TestHelper.GetHook(start.Hooks, "ToExecute"), entry,
                    out _, out error), error?.Message);

                Assert.IsTrue(ms.ExtractToFunctionTemplate(
                    user, boundary, new[] { entry! },
                    "MyTemplate", "MyInstance",
                    FtLoc, FiLoc,
                    out var ft, out _, out error), error?.Message);

                Assert.AreSame(entry, ft!.EntryNode,
                    "The entry node must be the destination of the original external incoming link.");
            });
    }

    // ── Undo/Redo ──────────────────────────────────────────────────────────

    [TestMethod]
    public void ExtractToFunctionTemplate_Undo_RestoresOriginalGraph()
    {
        TestHelper.RunInModelSystemContext(
            nameof(ExtractToFunctionTemplate_Undo_RestoresOriginalGraph),
            (user, pSess, ms) =>
            {
                var boundary = ms.ModelSystem.GlobalBoundary;
                // start → entryNode → externalDest
                Assert.IsTrue(ms.AddNode(user, boundary, "Entry", typeof(IgnoreResult<string>),
                    EntryLoc, out var entryNode, out var error), error?.Message);
                Assert.IsTrue(ms.AddNode(user, boundary, "ExternalDest", typeof(SimpleTestModule),
                    ExtDestLoc, out var externalDest, out var error2), error2?.Message);
                Assert.IsTrue(ms.AddModelSystemStart(user, boundary, "Start",
                    CallerLoc, out var start, out error), error?.Message);
                Assert.IsTrue(ms.AddLink(user, start,
                    TestHelper.GetHook(start.Hooks, "ToExecute"), entryNode,
                    out _, out error), error?.Message);
                Assert.IsTrue(ms.AddLink(user, entryNode,
                    TestHelper.GetHook(entryNode!.Hooks, "To Ignore"), externalDest,
                    out _, out error), error?.Message);

                // Perform extraction.
                Assert.IsTrue(ms.ExtractToFunctionTemplate(
                    user, boundary, new[] { entryNode },
                    "MyTemplate", "MyInstance",
                    FtLoc, FiLoc,
                    out _, out _, out error), error?.Message);

                // State after extraction.
                Assert.HasCount(1, boundary.FunctionTemplates);
                Assert.HasCount(1, boundary.FunctionInstances);
                Assert.IsFalse(boundary.Modules.Contains(entryNode));

                // Undo.
                Assert.IsTrue(ms.Undo(user, out error), error?.Message);

                // State restored: no FT, no FI.
                Assert.IsEmpty(boundary.FunctionTemplates,
                    "Undo must remove the FunctionTemplate.");
                Assert.IsEmpty(boundary.FunctionInstances,
                    "Undo must remove the FunctionInstance.");

                // entryNode is back in the boundary.
                Assert.IsTrue(boundary.Modules.Contains(entryNode),
                    "entryNode must be back in the parent boundary.");
                Assert.AreSame(boundary, entryNode.ContainedWithin,
                    "entryNode.ContainedWithin must point to the parent boundary again.");

                // Original links are restored: start→entryNode and entryNode→externalDest.
                var startLink = boundary.Links
                    .OfType<SingleLink>()
                    .FirstOrDefault(l => l.Origin == start && l.Destination == entryNode);
                Assert.IsNotNull(startLink,
                    "start→entryNode link must be restored after undo.");

                var entryLink = boundary.Links
                    .OfType<SingleLink>()
                    .FirstOrDefault(l => l.Origin == entryNode && l.Destination == externalDest);
                Assert.IsNotNull(entryLink,
                    "entryNode→externalDest link must be restored after undo.");

                // No extra links.
                Assert.HasCount(2, boundary.Links,
                    "Boundary must have exactly the two original links after undo.");
            });
    }

    [TestMethod]
    public void ExtractToFunctionTemplate_UndoRedo_ReappliesExtraction()
    {
        TestHelper.RunInModelSystemContext(
            nameof(ExtractToFunctionTemplate_UndoRedo_ReappliesExtraction),
            (user, pSess, ms) =>
            {
                var boundary = ms.ModelSystem.GlobalBoundary;
                Assert.IsTrue(ms.AddNode(user, boundary, "Entry", typeof(IgnoreResult<string>),
                    EntryLoc, out var entryNode, out var error), error?.Message);
                Assert.IsTrue(ms.AddNode(user, boundary, "ExternalDest", typeof(SimpleTestModule),
                    ExtDestLoc, out var externalDest, out var error2), error2?.Message);
                Assert.IsTrue(ms.AddModelSystemStart(user, boundary, "Start",
                    CallerLoc, out var start, out error), error?.Message);
                Assert.IsTrue(ms.AddLink(user, start,
                    TestHelper.GetHook(start.Hooks, "ToExecute"), entryNode,
                    out _, out error), error?.Message);
                Assert.IsTrue(ms.AddLink(user, entryNode,
                    TestHelper.GetHook(entryNode!.Hooks, "To Ignore"), externalDest,
                    out _, out error), error?.Message);

                Assert.IsTrue(ms.ExtractToFunctionTemplate(
                    user, boundary, new[] { entryNode },
                    "MyTemplate", "MyInstance",
                    FtLoc, FiLoc,
                    out _, out var fi, out error), error?.Message);

                // Undo → Redo.
                Assert.IsTrue(ms.Undo(user, out error), error?.Message);
                Assert.IsTrue(ms.Redo(user, out error), error?.Message);

                // After redo: FT and FI are back.
                Assert.HasCount(1, boundary.FunctionTemplates,
                    "Redo must restore the FunctionTemplate.");
                Assert.HasCount(1, boundary.FunctionInstances,
                    "Redo must restore the FunctionInstance.");

                // entryNode back in InternalModules.
                var ft = boundary.FunctionTemplates[0];
                Assert.IsTrue(ft.InternalModules.Modules.Contains(entryNode),
                    "entryNode must be back in InternalModules after redo.");

                // start → fi link.
                var toFi = boundary.Links
                    .OfType<SingleLink>()
                    .FirstOrDefault(l => l.Origin == start && l.Destination == fi);
                Assert.IsNotNull(toFi, "start→fi link must be present after redo.");

                // fi → externalDest link.
                var toExt = boundary.Links
                    .OfType<SingleLink>()
                    .FirstOrDefault(l => l.Origin == fi && l.Destination == externalDest);
                Assert.IsNotNull(toExt, "fi→externalDest link must be present after redo.");
            });
    }

    // ── FunctionInstance hooks match FunctionParameters ────────────────────

    [TestMethod]
    public void ExtractToFunctionTemplate_FunctionInstanceExposesHookPerParameter()
    {
        TestHelper.RunInModelSystemContext(
            nameof(ExtractToFunctionTemplate_FunctionInstanceExposesHookPerParameter),
            (user, pSess, ms) =>
            {
                var boundary = ms.ModelSystem.GlobalBoundary;
                Assert.IsTrue(ms.AddNode(user, boundary, "Entry", typeof(IgnoreResult<string>),
                    EntryLoc, out var entryNode, out var error), error?.Message);
                Assert.IsTrue(ms.AddNode(user, boundary, "ExternalDest", typeof(SimpleTestModule),
                    ExtDestLoc, out var externalDest, out var error2), error2?.Message);
                Assert.IsTrue(ms.AddModelSystemStart(user, boundary, "Start",
                    CallerLoc, out var start, out error), error?.Message);
                Assert.IsTrue(ms.AddLink(user, start,
                    TestHelper.GetHook(start.Hooks, "ToExecute"), entryNode,
                    out _, out error), error?.Message);
                Assert.IsTrue(ms.AddLink(user, entryNode,
                    TestHelper.GetHook(entryNode!.Hooks, "To Ignore"), externalDest,
                    out _, out error), error?.Message);

                Assert.IsTrue(ms.ExtractToFunctionTemplate(
                    user, boundary, new[] { entryNode },
                    "MyTemplate", "MyInstance",
                    FtLoc, FiLoc,
                    out var ft, out var fi, out error), error?.Message);

                // FI should expose exactly one FunctionParameterHook.
                Assert.HasCount(1, fi!.Hooks,
                    "FunctionInstance must expose one hook per FunctionParameter.");
                Assert.IsInstanceOfType<FunctionParameterHook>(fi.Hooks[0],
                    "The hook must be a FunctionParameterHook.");

                var fph = (FunctionParameterHook)fi.Hooks[0];
                Assert.AreSame(ft!.FunctionParameters[0], fph.Parameter,
                    "The FunctionParameterHook must reference the correct FunctionParameter.");
            });
    }

    // ── FunctionTemplate name uniqueness check ─────────────────────────────

    [TestMethod]
    public void ExtractToFunctionTemplate_BlankTemplateName_Fails()
    {
        TestHelper.RunInModelSystemContext(
            nameof(ExtractToFunctionTemplate_BlankTemplateName_Fails),
            (user, pSess, ms) =>
            {
                var boundary = ms.ModelSystem.GlobalBoundary;
                Assert.IsTrue(ms.AddNode(user, boundary, "Entry", typeof(SimpleTestModule),
                    EntryLoc, out var entry, out var error), error?.Message);
                Assert.IsFalse(ms.ExtractToFunctionTemplate(
                    user, boundary, new[] { entry! },
                    "   ", "FI", FtLoc, FiLoc,
                    out _, out _, out error));
                Assert.IsNotNull(error);
            });
    }

    [TestMethod]
    public void ExtractToFunctionTemplate_BlankInstanceName_Fails()
    {
        TestHelper.RunInModelSystemContext(
            nameof(ExtractToFunctionTemplate_BlankInstanceName_Fails),
            (user, pSess, ms) =>
            {
                var boundary = ms.ModelSystem.GlobalBoundary;
                Assert.IsTrue(ms.AddNode(user, boundary, "Entry", typeof(SimpleTestModule),
                    EntryLoc, out var entry, out var error), error?.Message);
                Assert.IsFalse(ms.ExtractToFunctionTemplate(
                    user, boundary, new[] { entry! },
                    "T", "   ", FtLoc, FiLoc,
                    out _, out _, out error));
                Assert.IsNotNull(error);
            });
    }

    // ── Boundary counts after extraction ──────────────────────────────────

    [TestMethod]
    public void ExtractToFunctionTemplate_BoundaryCountsAreCorrect()
    {
        TestHelper.RunInModelSystemContext(
            nameof(ExtractToFunctionTemplate_BoundaryCountsAreCorrect),
            (user, pSess, ms) =>
            {
                var boundary = ms.ModelSystem.GlobalBoundary;
                Assert.IsTrue(ms.AddNode(user, boundary, "Entry", typeof(IgnoreResult<string>),
                    EntryLoc, out var entryNode, out var error), error?.Message);
                Assert.IsTrue(ms.AddNode(user, boundary, "ExternalDest", typeof(SimpleTestModule),
                    ExtDestLoc, out var externalDest, out var error2), error2?.Message);
                Assert.IsTrue(ms.AddModelSystemStart(user, boundary, "Start",
                    CallerLoc, out var start, out error), error?.Message);
                Assert.IsTrue(ms.AddLink(user, start,
                    TestHelper.GetHook(start.Hooks, "ToExecute"), entryNode,
                    out _, out error), error?.Message);
                Assert.IsTrue(ms.AddLink(user, entryNode,
                    TestHelper.GetHook(entryNode!.Hooks, "To Ignore"), externalDest,
                    out _, out error), error?.Message);

                Assert.IsTrue(ms.ExtractToFunctionTemplate(
                    user, boundary, new[] { entryNode },
                    "MyTemplate", "MyInstance",
                    FtLoc, FiLoc,
                    out var ft, out _, out error), error?.Message);

                // Parent boundary: externalDest node only (entryNode moved to InternalModules).
                Assert.HasCount(1, boundary.Modules,
                    "Only externalDest must remain in the parent boundary.");
                Assert.AreSame(externalDest, boundary.Modules[0]);

                // Two links: start→fi and fi→externalDest.
                Assert.HasCount(2, boundary.Links);

                // One FunctionTemplate, one FunctionInstance.
                Assert.HasCount(1, boundary.FunctionTemplates);
                Assert.HasCount(1, boundary.FunctionInstances);
                Assert.AreSame(ft, boundary.FunctionTemplates[0]);
            });
    }
}
