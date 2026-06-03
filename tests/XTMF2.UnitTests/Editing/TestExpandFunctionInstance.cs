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
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Editing;
using XTMF2.ModelSystemConstruct;
using XTMF2.RuntimeModules;
using XTMF2.UnitTests.Modules;

namespace XTMF2.UnitTests.Editing;

/// <summary>
/// Tests for <see cref="ModelSystemSession.ExpandFunctionInstance"/>.
/// </summary>
[TestClass]
public class TestExpandFunctionInstance
{
    // ── Shared locations ───────────────────────────────────────────────────
    private static readonly Rectangle CallerLoc  = new(0,   0,  160, 60);
    private static readonly Rectangle EntryLoc   = new(200, 0,  160, 60);
    private static readonly Rectangle ExtDestLoc = new(400, 0,  160, 60);
    private static readonly Rectangle FtLoc      = new(200, 100, 300, 200);
    private static readonly Rectangle FiLoc      = new(200, 0,   160, 60);

    // ── Helper: set up a simple extract + expand scenario ─────────────────

    /// <summary>
    /// Creates a caller → entryNode → externalDest graph, extracts entryNode into
    /// a FunctionTemplate, then expands the resulting FunctionInstance back.
    /// </summary>
    private static void RunExtractThenExpand(
        User user, ModelSystemSession ms,
        out Node caller,
        out Node entryNode,
        out Node externalDest)
    {
        var boundary = ms.ModelSystem.GlobalBoundary;

        Assert.IsTrue(ms.AddNode(user, boundary, "Caller",   typeof(Execute),              CallerLoc,  out caller,       out var err), err?.Message);
        Assert.IsTrue(ms.AddNode(user, boundary, "Entry",    typeof(IgnoreResult<string>), EntryLoc,   out entryNode,    out     err), err?.Message);
        Assert.IsTrue(ms.AddNode(user, boundary, "ExtDest",  typeof(SimpleTestModule), ExtDestLoc, out externalDest, out     err), err?.Message);

        var callerHook = TestHelper.GetHook(caller!.Hooks, "To Execute");
        var entryHook  = TestHelper.GetHook(entryNode!.Hooks, "To Ignore");

        Assert.IsTrue(ms.AddLink(user, caller,     callerHook, entryNode,    out var _lk1, out err), err?.Message);
        Assert.IsTrue(ms.AddLink(user, entryNode,  entryHook,  externalDest, out var _lk2, out err), err?.Message);

        Assert.IsTrue(ms.ExtractToFunctionTemplate(
            user, boundary,
            new[] { entryNode },
            "MyTemplate", "MyInstance",
            FtLoc, FiLoc,
            out var _ft, out var fi, out err), err?.Message);

        Assert.IsTrue(ms.ExpandFunctionInstance(user, fi!, out err), err?.Message);
    }

    // ── Validation failures ────────────────────────────────────────────────

    [TestMethod]
    public void ExpandFunctionInstance_MultipleInstances_Fails()
    {
        TestHelper.RunInModelSystemContext(
            nameof(ExpandFunctionInstance_MultipleInstances_Fails),
            (user, pSess, ms) =>
            {
                var boundary = ms.ModelSystem.GlobalBoundary;

                Assert.IsTrue(ms.AddNode(user, boundary, "Caller",  typeof(Execute),              CallerLoc,  out var caller,  out var err), err?.Message);
                Assert.IsTrue(ms.AddNode(user, boundary, "Entry",   typeof(IgnoreResult<string>), EntryLoc,   out var entry,   out     err), err?.Message);
                Assert.IsTrue(ms.AddNode(user, boundary, "Caller2", typeof(Execute), new Rectangle(0, 200, 160, 60), out var caller2, out err), err?.Message);

                var callerHook  = TestHelper.GetHook(caller!.Hooks, "To Execute");
                var caller2Hook = TestHelper.GetHook(caller2!.Hooks, "To Execute");

                Assert.IsTrue(ms.AddLink(user, caller,  callerHook,  entry, out var _lk1, out err), err?.Message);
                Assert.IsTrue(ms.AddLink(user, caller2, caller2Hook, entry, out var _lk2, out err), err?.Message);

                Assert.IsTrue(ms.ExtractToFunctionTemplate(
                    user, boundary, new[] { entry! },
                    "T", "FI", FtLoc, FiLoc,
                    out var ft, out var fi1, out err), err?.Message);

                // Add a second instance of the same template.
                Assert.IsTrue(ms.AddFunctionInstance(user, boundary, ft!, "FI2", new Rectangle(0, 300, 160, 60), out var _fi2, out err), err?.Message);

                Assert.IsFalse(ms.ExpandFunctionInstance(user, fi1!, out err));
                Assert.IsNotNull(err);
            });
    }

    // ── Success scenarios ──────────────────────────────────────────────────

    [TestMethod]
    public void ExpandFunctionInstance_Basic_Succeeds()
    {
        TestHelper.RunInModelSystemContext(
            nameof(ExpandFunctionInstance_Basic_Succeeds),
            (user, pSess, ms) =>
            {
                RunExtractThenExpand(user, ms, out var _c, out var entryNode, out var _e);

                var boundary = ms.ModelSystem.GlobalBoundary;

                // entryNode should be back in the boundary modules.
                Assert.Contains(entryNode, boundary.Modules,
                    "entryNode must be moved back into the boundary.");
            });
    }

    [TestMethod]
    public void ExpandFunctionInstance_NoFunctionTemplateOrInstanceRemains()
    {
        TestHelper.RunInModelSystemContext(
            nameof(ExpandFunctionInstance_NoFunctionTemplateOrInstanceRemains),
            (user, pSess, ms) =>
            {
                RunExtractThenExpand(user, ms, out var _c, out var _en, out var _e);

                var boundary = ms.ModelSystem.GlobalBoundary;

                Assert.IsEmpty(boundary.FunctionTemplates,
                    "FunctionTemplate must be removed after expansion.");
                Assert.IsEmpty(boundary.FunctionInstances,
                    "FunctionInstance must be removed after expansion.");
            });
    }

    [TestMethod]
    public void ExpandFunctionInstance_CallerLinksToEntryNode()
    {
        TestHelper.RunInModelSystemContext(
            nameof(ExpandFunctionInstance_CallerLinksToEntryNode),
            (user, pSess, ms) =>
            {
                RunExtractThenExpand(user, ms, out var caller, out var entryNode, out var _e);

                var boundary = ms.ModelSystem.GlobalBoundary;

                // The link from caller should now point directly to entryNode.
                var callerLinks = boundary.Links.Where(l => l.Origin == caller).ToList();
                Assert.HasCount(1, callerLinks,
                    "Caller should have exactly one outgoing link after expansion.");
                if (callerLinks[0] is SingleLink sl)
                {
                    Assert.AreEqual(entryNode, sl.Destination,
                        "Caller's link must target entryNode after expansion.");
                }
                else
                {
                    var ml = (MultiLink)callerLinks[0];
                    Assert.Contains(entryNode, ml.Destinations,
                        "Caller's link must contain entryNode after expansion.");
                }
            });
    }

    [TestMethod]
    public void ExpandFunctionInstance_InternalLinkPreserved()
    {
        TestHelper.RunInModelSystemContext(
            nameof(ExpandFunctionInstance_InternalLinkPreserved),
            (user, pSess, ms) =>
            {
                RunExtractThenExpand(user, ms, out var _c, out var entryNode, out var externalDest);

                var boundary = ms.ModelSystem.GlobalBoundary;

                // The link entryNode → externalDest must be re-created as a direct link.
                var entryLinks = boundary.Links.Where(l => l.Origin == entryNode).ToList();
                Assert.HasCount(1, entryLinks,
                    "entryNode should have exactly one outgoing link after expansion.");
                Assert.AreEqual(externalDest, ((SingleLink)entryLinks[0]).Destination,
                    "entryNode's link must target externalDest after expansion.");
            });
    }

    [TestMethod]
    public void ExpandFunctionInstance_UndoRestoresFunctionInstance()
    {
        TestHelper.RunInModelSystemContext(
            nameof(ExpandFunctionInstance_UndoRestoresFunctionInstance),
            (user, pSess, ms) =>
            {
                RunExtractThenExpand(user, ms, out var _c, out var entryNode, out var _e);

                var boundary = ms.ModelSystem.GlobalBoundary;

                // Undo the expansion.
                Assert.IsTrue(ms.Undo(user, out var err), err?.Message);

                Assert.HasCount(1, boundary.FunctionInstances,
                    "Undo must restore the FunctionInstance.");
                Assert.HasCount(1, boundary.FunctionTemplates,
                    "Undo must restore the FunctionTemplate.");
                Assert.DoesNotContain(entryNode, boundary.Modules,
                    "After undo, entryNode should not be in the boundary.");
            });
    }

    [TestMethod]
    public void ExpandFunctionInstance_RedoReappliesExpansion()
    {
        TestHelper.RunInModelSystemContext(
            nameof(ExpandFunctionInstance_RedoReappliesExpansion),
            (user, pSess, ms) =>
            {
                RunExtractThenExpand(user, ms, out var _c, out var entryNode, out var _e);

                var boundary = ms.ModelSystem.GlobalBoundary;

                Assert.IsTrue(ms.Undo(user, out var err), err?.Message);
                Assert.IsTrue(ms.Redo(user, out err), err?.Message);

                Assert.IsEmpty(boundary.FunctionTemplates,
                    "Redo must remove the FunctionTemplate again.");
                Assert.IsEmpty(boundary.FunctionInstances,
                    "Redo must remove the FunctionInstance again.");
                Assert.Contains(entryNode, boundary.Modules,
                    "Redo must move entryNode back to the boundary.");
            });
    }
}
