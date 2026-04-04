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
/// Verifies that a <see cref="FunctionParameter"/> cannot be removed from a
/// <see cref="FunctionTemplate"/> while any <see cref="FunctionInstance"/> of that
/// template has an active link wired through the corresponding
/// <see cref="FunctionParameterHook"/>.
/// </summary>
[TestClass]
public class TestFunctionParameterRemovalGuard
{
    // ── Scaffold ───────────────────────────────────────────────────────────

    private static FunctionTemplate AddTemplate(User user, ModelSystemSession ms,
        Boundary boundary, string name = "T")
    {
        Assert.IsTrue(ms.AddFunctionTemplate(user, boundary, name, out var ft, out var err),
            err?.Message);
        return ft!;
    }

    private static FunctionParameter AddFunctionParameter(User user, ModelSystemSession ms,
        FunctionTemplate ft, string name = "Param")
    {
        Assert.IsTrue(ms.AddFunctionParameter(user, ft, name, typeof(SimpleTestModule),
            new Rectangle(0, 0, 120, 50), out var fp, out var err), err?.Message);
        return fp!;
    }

    private static FunctionInstance AddFunctionInstance(User user, ModelSystemSession ms,
        Boundary boundary, FunctionTemplate ft, string name = "FI")
    {
        Assert.IsTrue(ms.AddFunctionInstance(user, boundary, ft, name,
            new Rectangle(200, 0, 180, 60), out var fi, out var err), err?.Message);
        return fi!;
    }

    private static Node AddNode(User user, ModelSystemSession ms, Boundary boundary,
        string name = "Dest")
    {
        Assert.IsTrue(ms.AddNode(user, boundary, name, typeof(SimpleTestModule),
            new Rectangle(400, 0, 120, 50), out var node, out var err), err?.Message);
        return node!;
    }

    // ── Tests ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Removing a <see cref="FunctionParameter"/> that has no wired links succeeds.
    /// </summary>
    [TestMethod]
    public void RemoveFunctionParameter_NoActiveLink_Succeeds()
    {
        TestHelper.RunInModelSystemContext(nameof(RemoveFunctionParameter_NoActiveLink_Succeeds),
            (user, pSession, ms) =>
            {
                var gb = ms.ModelSystem.GlobalBoundary;
                var ft = AddTemplate(user, ms, gb);
                var fp = AddFunctionParameter(user, ms, ft);

                Assert.IsTrue(ms.RemoveFunctionParameter(user, ft, fp, out var err),
                    err?.Message);

                Assert.IsEmpty(ft.FunctionParameters,
                    "Parameter should be removed when no link is wired.");
            });
    }

    /// <summary>
    /// Attempting to remove a <see cref="FunctionParameter"/> while a
    /// <see cref="FunctionInstance"/> has an active link through its hook is rejected.
    /// </summary>
    [TestMethod]
    public void RemoveFunctionParameter_WithActiveLink_Fails()
    {
        TestHelper.RunInModelSystemContext(nameof(RemoveFunctionParameter_WithActiveLink_Fails),
            (user, pSession, ms) =>
            {
                var gb   = ms.ModelSystem.GlobalBoundary;
                var ft   = AddTemplate(user, ms, gb);
                var fp   = AddFunctionParameter(user, ms, ft);
                var fi   = AddFunctionInstance(user, ms, gb, ft);
                var dest = AddNode(user, ms, gb);

                // Wire the FunctionParameterHook of fi to 'dest'.
                var fpHook = fi.Hooks[0]; // FunctionParameterHook for fp
                Assert.IsTrue(ms.AddLink(user, fi, fpHook, dest, out _, out var linkErr),
                    linkErr?.Message);

                // RemoveFunctionParameter must be blocked.
                Assert.IsFalse(ms.RemoveFunctionParameter(user, ft, fp, out var err),
                    "Should be blocked while a FunctionInstance has an active link on this hook.");
                Assert.IsNotNull(err, "An error description must be provided.");
                Assert.HasCount(1, ft.FunctionParameters,
                    "Parameter must not be removed.");
            });
    }

    /// <summary>
    /// After the wired link is removed, the <see cref="FunctionParameter"/> can be deleted.
    /// </summary>
    [TestMethod]
    public void RemoveFunctionParameter_AfterLinkRemoval_Succeeds()
    {
        TestHelper.RunInModelSystemContext(nameof(RemoveFunctionParameter_AfterLinkRemoval_Succeeds),
            (user, pSession, ms) =>
            {
                var gb   = ms.ModelSystem.GlobalBoundary;
                var ft   = AddTemplate(user, ms, gb);
                var fp   = AddFunctionParameter(user, ms, ft);
                var fi   = AddFunctionInstance(user, ms, gb, ft);
                var dest = AddNode(user, ms, gb);

                var fpHook = fi.Hooks[0];
                Assert.IsTrue(ms.AddLink(user, fi, fpHook, dest, out var link, out var linkErr),
                    linkErr?.Message);

                // Remove the wiring, then the parameter should be removable.
                Assert.IsTrue(ms.RemoveLink(user, link!, out var removeErr), removeErr?.Message);
                Assert.IsTrue(ms.RemoveFunctionParameter(user, ft, fp, out var fpErr),
                    fpErr?.Message);

                Assert.IsEmpty(ft.FunctionParameters,
                    "Parameter should be removed after the link is deleted.");
            });
    }

    /// <summary>
    /// The guard works even when the <see cref="FunctionInstance"/> lives in a
    /// nested <see cref="Boundary"/> (the traversal must recurse into sub-boundaries).
    /// </summary>
    [TestMethod]
    public void RemoveFunctionParameter_ActiveLinkInNestedBoundary_Fails()
    {
        TestHelper.RunInModelSystemContext(nameof(RemoveFunctionParameter_ActiveLinkInNestedBoundary_Fails),
            (user, pSession, ms) =>
            {
                var gb       = ms.ModelSystem.GlobalBoundary;
                var ft       = AddTemplate(user, ms, gb);
                var fp       = AddFunctionParameter(user, ms, ft);

                // Place the FunctionInstance inside a nested boundary.
                Assert.IsTrue(ms.AddBoundary(user, gb, "Inner", out var inner, out var bErr),
                    bErr?.Message);
                var fi   = AddFunctionInstance(user, ms, inner!, ft, "FI_Inner");
                var dest = AddNode(user, ms, inner!, "DestInner");

                var fpHook = fi.Hooks[0];
                Assert.IsTrue(ms.AddLink(user, fi, fpHook, dest, out _, out var linkErr),
                    linkErr?.Message);

                // The guard must detect the link even though it is in a nested boundary.
                Assert.IsFalse(ms.RemoveFunctionParameter(user, ft, fp, out var err),
                    "Should be blocked even for FunctionInstances in nested boundaries.");
                Assert.IsNotNull(err);
            });
    }
}
