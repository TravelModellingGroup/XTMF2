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
/// Tests for <see cref="ModelSystemSession.RenameFunctionParameter"/>.
/// Verifies that a <see cref="FunctionParameter"/> can be renamed, that the
/// resulting name is reflected on every <see cref="FunctionInstance"/>'s
/// <see cref="FunctionParameterHook"/>, and that undo/redo are supported.
/// </summary>
[TestClass]
public class TestFunctionParameterRename
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

    // ── Basic rename ───────────────────────────────────────────────────────

    /// <summary>Renaming to a valid unique name succeeds.</summary>
    [TestMethod]
    public void RenameFunctionParameter_ValidName_Succeeds()
    {
        TestHelper.RunInModelSystemContext(nameof(RenameFunctionParameter_ValidName_Succeeds),
            (user, pSession, ms) =>
            {
                var ft = AddTemplate(user, ms, ms.ModelSystem.GlobalBoundary);
                var fp = AddFunctionParameter(user, ms, ft, "Original");

                Assert.IsTrue(ms.RenameFunctionParameter(user, ft, fp, "Renamed", out var err),
                    err?.Message);

                Assert.AreEqual("Renamed", fp.Name,
                    "FunctionParameter.Name must reflect the new name.");
            });
    }

    /// <summary>An empty name is rejected.</summary>
    [TestMethod]
    public void RenameFunctionParameter_EmptyName_Fails()
    {
        TestHelper.RunInModelSystemContext(nameof(RenameFunctionParameter_EmptyName_Fails),
            (user, pSession, ms) =>
            {
                var ft = AddTemplate(user, ms, ms.ModelSystem.GlobalBoundary);
                var fp = AddFunctionParameter(user, ms, ft, "P");

                Assert.IsFalse(ms.RenameFunctionParameter(user, ft, fp, "", out var err));
                Assert.IsNotNull(err);
                Assert.AreEqual("P", fp.Name, "Name must not change on failure.");
            });
    }

    /// <summary>A whitespace-only name is rejected.</summary>
    [TestMethod]
    public void RenameFunctionParameter_WhitespaceName_Fails()
    {
        TestHelper.RunInModelSystemContext(nameof(RenameFunctionParameter_WhitespaceName_Fails),
            (user, pSession, ms) =>
            {
                var ft = AddTemplate(user, ms, ms.ModelSystem.GlobalBoundary);
                var fp = AddFunctionParameter(user, ms, ft, "P");

                Assert.IsFalse(ms.RenameFunctionParameter(user, ft, fp, "   ", out var err));
                Assert.IsNotNull(err);
                Assert.AreEqual("P", fp.Name);
            });
    }

    /// <summary>Renaming to the same name as another parameter on the template is rejected.</summary>
    [TestMethod]
    public void RenameFunctionParameter_DuplicateName_Fails()
    {
        TestHelper.RunInModelSystemContext(nameof(RenameFunctionParameter_DuplicateName_Fails),
            (user, pSession, ms) =>
            {
                var ft  = AddTemplate(user, ms, ms.ModelSystem.GlobalBoundary);
                var fp1 = AddFunctionParameter(user, ms, ft, "Alpha");
                var fp2 = AddFunctionParameter(user, ms, ft, "Beta");

                Assert.IsFalse(ms.RenameFunctionParameter(user, ft, fp2, "Alpha", out var err),
                    "Renaming 'Beta' to 'Alpha' should fail – name already exists.");
                Assert.IsNotNull(err);
                Assert.AreEqual("Beta", fp2.Name, "Name must not change on failure.");
            });
    }

    // ── Hook name propagation ──────────────────────────────────────────────

    /// <summary>
    /// After a rename the <see cref="FunctionParameterHook"/> on every
    /// <see cref="FunctionInstance"/> of the template must reflect the new name.
    /// </summary>
    [TestMethod]
    public void RenameFunctionParameter_HookNameUpdated_OnAllInstances()
    {
        TestHelper.RunInModelSystemContext(
            nameof(RenameFunctionParameter_HookNameUpdated_OnAllInstances),
            (user, pSession, ms) =>
            {
                var gb  = ms.ModelSystem.GlobalBoundary;
                var ft  = AddTemplate(user, ms, gb);
                var fp  = AddFunctionParameter(user, ms, ft, "OldName");
                var fi1 = AddFunctionInstance(user, ms, gb, ft, "FI1");
                var fi2 = AddFunctionInstance(user, ms, gb, ft, "FI2");

                Assert.IsTrue(ms.RenameFunctionParameter(user, ft, fp, "NewName", out var err),
                    err?.Message);

                // FunctionParameterHook names are lazily rebuilt, so re-query Hooks each time.
                Assert.AreEqual("NewName", fi1.Hooks[0].Name,
                    "Hook name on FI1 must reflect the rename.");
                Assert.AreEqual("NewName", fi2.Hooks[0].Name,
                    "Hook name on FI2 must reflect the rename.");
            });
    }

    // ── Undo / redo ────────────────────────────────────────────────────────

    /// <summary>Undo restores the original name.</summary>
    [TestMethod]
    public void RenameFunctionParameter_Undo_RestoresOldName()
    {
        TestHelper.RunInModelSystemContext(nameof(RenameFunctionParameter_Undo_RestoresOldName),
            (user, pSession, ms) =>
            {
                var ft = AddTemplate(user, ms, ms.ModelSystem.GlobalBoundary);
                var fp = AddFunctionParameter(user, ms, ft, "Original");

                Assert.IsTrue(ms.RenameFunctionParameter(user, ft, fp, "Renamed", out _));
                Assert.AreEqual("Renamed", fp.Name);

                Assert.IsTrue(ms.Undo(user, out var undoErr), undoErr?.Message);
                Assert.AreEqual("Original", fp.Name, "Undo must restore 'Original'.");
            });
    }

    /// <summary>Redo re-applies the rename after undo.</summary>
    [TestMethod]
    public void RenameFunctionParameter_Redo_ReappliesRename()
    {
        TestHelper.RunInModelSystemContext(nameof(RenameFunctionParameter_Redo_ReappliesRename),
            (user, pSession, ms) =>
            {
                var ft = AddTemplate(user, ms, ms.ModelSystem.GlobalBoundary);
                var fp = AddFunctionParameter(user, ms, ft, "Original");

                Assert.IsTrue(ms.RenameFunctionParameter(user, ft, fp, "Renamed", out _));
                Assert.IsTrue(ms.Undo(user, out _));
                Assert.AreEqual("Original", fp.Name);

                Assert.IsTrue(ms.Redo(user, out var redoErr), redoErr?.Message);
                Assert.AreEqual("Renamed", fp.Name, "Redo must re-apply 'Renamed'.");
            });
    }
}
