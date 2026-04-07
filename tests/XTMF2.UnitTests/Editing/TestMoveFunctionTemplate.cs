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

namespace XTMF2.UnitTests.Editing
{
    /// <summary>
    /// Unit tests for <see cref="ModelSystemSession.MoveFunctionTemplate"/>.
    /// </summary>
    [TestClass]
    public class TestMoveFunctionTemplate
    {
        // ── Helpers ──────────────────────────────────────────────────────────

        private static FunctionTemplate AddTemplate(User user, ModelSystemSession mSession,
            Boundary boundary, string name = "MyTemplate")
        {
            Assert.IsTrue(mSession.AddFunctionTemplate(user, boundary, name, out var t, out var error),
                error?.Message);
            return t!;
        }

        private static Boundary AddBoundary(User user, ModelSystemSession mSession,
            Boundary parent, string name = "ChildBoundary")
        {
            Assert.IsTrue(mSession.AddBoundary(user, parent, name, out var b, out var error),
                error?.Message);
            return b!;
        }

        private static FunctionInstance AddInstance(User user, ModelSystemSession mSession,
            Boundary boundary, FunctionTemplate template, string name = "MyInstance")
        {
            Assert.IsTrue(mSession.AddFunctionInstance(user, boundary, template, name,
                new Rectangle(10f, 10f, 160f, 70f), out var fi, out var error),
                error?.Message);
            return fi!;
        }

        // ── Basic move ────────────────────────────────────────────────────────

        [TestMethod]
        public void TestMoveFunctionTemplate_GlobalToChild_Succeeds()
        {
            TestHelper.RunInModelSystemContext(nameof(TestMoveFunctionTemplate_GlobalToChild_Succeeds),
            (user, pSession, mSession) =>
            {
                var gb    = mSession.ModelSystem.GlobalBoundary;
                var child = AddBoundary(user, mSession, gb);
                var tmpl  = AddTemplate(user, mSession, gb);

                Assert.Contains(tmpl, gb.FunctionTemplates, "Template should start in global boundary.");

                Assert.IsTrue(mSession.MoveFunctionTemplate(user, tmpl, gb, child, out var error),
                    error?.Message);

                Assert.DoesNotContain(tmpl, gb.FunctionTemplates,
                    "Template must no longer be in the global boundary after move.");
                Assert.Contains(tmpl, child.FunctionTemplates,
                    "Template must appear in the child boundary after move.");
                Assert.AreSame(child, tmpl.Parent,
                    "Template.Parent must be updated to the destination boundary.");
            });
        }

        [TestMethod]
        public void TestMoveFunctionTemplate_ChildToGlobal_Succeeds()
        {
            TestHelper.RunInModelSystemContext(nameof(TestMoveFunctionTemplate_ChildToGlobal_Succeeds),
            (user, pSession, mSession) =>
            {
                var gb    = mSession.ModelSystem.GlobalBoundary;
                var child = AddBoundary(user, mSession, gb);
                var tmpl  = AddTemplate(user, mSession, child);

                Assert.IsTrue(mSession.MoveFunctionTemplate(user, tmpl, child, gb, out var error),
                    error?.Message);

                Assert.DoesNotContain(tmpl, child.FunctionTemplates,
                    "Template must no longer be in the child boundary after move.");
                Assert.Contains(tmpl, gb.FunctionTemplates,
                    "Template must appear in the global boundary after move.");
                Assert.AreSame(gb, tmpl.Parent,
                    "Template.Parent must be updated to the global boundary.");
            });
        }

        [TestMethod]
        public void TestMoveFunctionTemplate_BetweenChildBoundaries_Succeeds()
        {
            TestHelper.RunInModelSystemContext(nameof(TestMoveFunctionTemplate_BetweenChildBoundaries_Succeeds),
            (user, pSession, mSession) =>
            {
                var gb     = mSession.ModelSystem.GlobalBoundary;
                var childA = AddBoundary(user, mSession, gb, "ChildA");
                var childB = AddBoundary(user, mSession, gb, "ChildB");
                var tmpl   = AddTemplate(user, mSession, childA);

                Assert.IsTrue(mSession.MoveFunctionTemplate(user, tmpl, childA, childB, out var error),
                    error?.Message);

                Assert.DoesNotContain(tmpl, childA.FunctionTemplates);
                Assert.Contains(tmpl, childB.FunctionTemplates);
                Assert.AreSame(childB, tmpl.Parent);
            });
        }

        // ── Parent property update ────────────────────────────────────────────

        [TestMethod]
        public void TestMoveFunctionTemplate_ParentPropertyUpdated()
        {
            TestHelper.RunInModelSystemContext(nameof(TestMoveFunctionTemplate_ParentPropertyUpdated),
            (user, pSession, mSession) =>
            {
                var gb    = mSession.ModelSystem.GlobalBoundary;
                var child = AddBoundary(user, mSession, gb);
                var tmpl  = AddTemplate(user, mSession, gb);

                Assert.AreSame(gb, tmpl.Parent, "Parent should initially be global boundary.");

                Assert.IsTrue(mSession.MoveFunctionTemplate(user, tmpl, gb, child, out var error),
                    error?.Message);

                Assert.AreSame(child, tmpl.Parent, "Parent should be updated to child after move.");
            });
        }

        // ── Undo / Redo ───────────────────────────────────────────────────────

        [TestMethod]
        public void TestMoveFunctionTemplate_UndoRedo()
        {
            TestHelper.RunInModelSystemContext(nameof(TestMoveFunctionTemplate_UndoRedo),
            (user, pSession, mSession) =>
            {
                CommandError error = null;
                var gb    = mSession.ModelSystem.GlobalBoundary;
                var child = AddBoundary(user, mSession, gb);
                var tmpl  = AddTemplate(user, mSession, gb);

                // Move global → child.
                Assert.IsTrue(mSession.MoveFunctionTemplate(user, tmpl, gb, child, out error),
                    error?.Message);
                Assert.Contains(tmpl, child.FunctionTemplates);
                Assert.AreSame(child, tmpl.Parent);

                // Undo: template should return to global boundary.
                Assert.IsTrue(mSession.Undo(user, out error), error?.Message);
                Assert.Contains(tmpl, gb.FunctionTemplates, "Undo should put template back in global.");
                Assert.DoesNotContain(tmpl, child.FunctionTemplates, "Undo should remove template from child.");
                Assert.AreSame(gb, tmpl.Parent, "Undo should restore Parent to global boundary.");

                // Redo: template moves to child again.
                Assert.IsTrue(mSession.Redo(user, out error), error?.Message);
                Assert.Contains(tmpl, child.FunctionTemplates, "Redo should move template back to child.");
                Assert.DoesNotContain(tmpl, gb.FunctionTemplates, "Redo should remove template from global.");
                Assert.AreSame(child, tmpl.Parent, "Redo should set Parent back to child boundary.");
            });
        }

        // ── Move with FunctionInstances that remain accessible ────────────────

        [TestMethod]
        public void TestMoveFunctionTemplate_WithFunctionInstance_BothInGlobalScope_Succeeds()
        {
            TestHelper.RunInModelSystemContext(
                nameof(TestMoveFunctionTemplate_WithFunctionInstance_BothInGlobalScope_Succeeds),
            (user, pSession, mSession) =>
            {
                var gb    = mSession.ModelSystem.GlobalBoundary;
                var child = AddBoundary(user, mSession, gb);
                var tmpl  = AddTemplate(user, mSession, gb);

                // FunctionInstance lives in the global boundary (same regular scope as destination).
                var fi = AddInstance(user, mSession, gb, tmpl);

                // Moving template to a child regular boundary: both the FI and the new
                // template location are in the global (regular) scope → should succeed.
                Assert.IsTrue(mSession.MoveFunctionTemplate(user, tmpl, gb, child, out var error),
                    error?.Message);

                Assert.Contains(tmpl, child.FunctionTemplates);
                Assert.AreSame(tmpl, fi.Template, "FunctionInstance must still reference the same template.");
            });
        }

        [TestMethod]
        public void TestMoveFunctionTemplate_WithFunctionInstance_ChildToGlobal_Succeeds()
        {
            TestHelper.RunInModelSystemContext(
                nameof(TestMoveFunctionTemplate_WithFunctionInstance_ChildToGlobal_Succeeds),
            (user, pSession, mSession) =>
            {
                var gb    = mSession.ModelSystem.GlobalBoundary;
                var child = AddBoundary(user, mSession, gb);
                var tmpl  = AddTemplate(user, mSession, child);

                // FunctionInstance in the child boundary (regular scope).
                var fi = AddInstance(user, mSession, child, tmpl);

                // Both child and global are in regular scope → should succeed.
                Assert.IsTrue(mSession.MoveFunctionTemplate(user, tmpl, child, gb, out var error),
                    error?.Message);

                Assert.Contains(tmpl, gb.FunctionTemplates);
                Assert.AreSame(tmpl, fi.Template);
            });
        }

        // ── Move that would break FunctionInstance access ─────────────────────

        [TestMethod]
        public void TestMoveFunctionTemplate_IntoInternalModules_FI_InGlobalScope_Fails()
        {
            TestHelper.RunInModelSystemContext(
                nameof(TestMoveFunctionTemplate_IntoInternalModules_FI_InGlobalScope_Fails),
            (user, pSession, mSession) =>
            {
                var gb = mSession.ModelSystem.GlobalBoundary;

                // Create a "container" FunctionTemplate whose InternalModules scope we will
                // move the other template INTO.
                var containerTemplate = AddTemplate(user, mSession, gb, "ContainerTemplate");

                // Target template that has a FunctionInstance in the global (regular) scope.
                var targetTemplate   = AddTemplate(user, mSession, gb, "TargetTemplate");
                var fi = AddInstance(user, mSession, gb, targetTemplate);

                // Attempting to move targetTemplate into containerTemplate's InternalModules
                // should fail: fi is in global scope (null) but the destination scope is
                // containerTemplate, so they differ.
                Assert.IsFalse(
                    mSession.MoveFunctionTemplate(user, targetTemplate,
                        gb, containerTemplate.InternalModules, out var error),
                    "Move into InternalModules must fail when FI lives in global scope.");
                Assert.IsNotNull(error, "An error message must be provided.");

                // Template and FI should be unchanged.
                Assert.Contains(targetTemplate, gb.FunctionTemplates,
                    "Template must still be in global boundary after failed move.");
                Assert.AreSame(gb, targetTemplate.Parent,
                    "Parent must be unchanged after a failed move.");
            });
        }

        [TestMethod]
        public void TestMoveFunctionTemplate_FI_InInternalModules_MoveToGlobal_Fails()
        {
            TestHelper.RunInModelSystemContext(
                nameof(TestMoveFunctionTemplate_FI_InInternalModules_MoveToGlobal_Fails),
            (user, pSession, mSession) =>
            {
                var gb = mSession.ModelSystem.GlobalBoundary;

                // Container FunctionTemplate — we place both the target template and
                // its FunctionInstance inside this template's InternalModules.
                var containerTemplate = AddTemplate(user, mSession, gb, "ContainerTemplate");
                var internalBoundary  = containerTemplate.InternalModules;

                var targetTemplate = AddTemplate(user, mSession, internalBoundary, "TargetTemplate");
                var fi = AddInstance(user, mSession, internalBoundary, targetTemplate);

                // Both the template and the FI currently live inside containerTemplate's scope.
                // Moving the template to global (a different scope) should fail because
                // the FI (in containerTemplate scope) would lose access.
                Assert.IsFalse(
                    mSession.MoveFunctionTemplate(user, targetTemplate, internalBoundary, gb, out var error),
                    "Moving template out of InternalModules scope must fail when FI remains inside.");
                Assert.IsNotNull(error);

                Assert.Contains(targetTemplate, internalBoundary.FunctionTemplates,
                    "Template must still be in InternalModules after a failed move.");
                Assert.AreSame(internalBoundary, targetTemplate.Parent);
            });
        }

        [TestMethod]
        public void TestMoveFunctionTemplate_FI_InDifferentInternalModules_Fails()
        {
            TestHelper.RunInModelSystemContext(
                nameof(TestMoveFunctionTemplate_FI_InDifferentInternalModules_Fails),
            (user, pSession, mSession) =>
            {
                var gb = mSession.ModelSystem.GlobalBoundary;

                // Two distinct container templates — their InternalModules are different scopes.
                var containerA = AddTemplate(user, mSession, gb, "ContainerA");
                var containerB = AddTemplate(user, mSession, gb, "ContainerB");

                var targetTemplate = AddTemplate(user, mSession, gb, "TargetTemplate");
                var fi = AddInstance(user, mSession, containerA.InternalModules, targetTemplate);

                // FI is in containerA scope; destination (containerB.InternalModules) is
                // containerB scope — different → must fail.
                Assert.IsFalse(
                    mSession.MoveFunctionTemplate(user, targetTemplate,
                        gb, containerB.InternalModules, out var error),
                    "Move to a different InternalModules scope must fail.");
                Assert.IsNotNull(error);

                Assert.Contains(targetTemplate, gb.FunctionTemplates);
                Assert.AreSame(gb, targetTemplate.Parent);
            });
        }

        // ── Rejection cases ───────────────────────────────────────────────────

        [TestMethod]
        public void TestMoveFunctionTemplate_SameBoundary_Fails()
        {
            TestHelper.RunInModelSystemContext(nameof(TestMoveFunctionTemplate_SameBoundary_Fails),
            (user, pSession, mSession) =>
            {
                var gb   = mSession.ModelSystem.GlobalBoundary;
                var tmpl = AddTemplate(user, mSession, gb);

                Assert.IsFalse(mSession.MoveFunctionTemplate(user, tmpl, gb, gb, out var error),
                    "Moving a template to the same boundary must fail.");
                Assert.IsNotNull(error);
            });
        }

        [TestMethod]
        public void TestMoveFunctionTemplate_WrongSourceBoundary_Fails()
        {
            TestHelper.RunInModelSystemContext(nameof(TestMoveFunctionTemplate_WrongSourceBoundary_Fails),
            (user, pSession, mSession) =>
            {
                var gb    = mSession.ModelSystem.GlobalBoundary;
                var child = AddBoundary(user, mSession, gb);
                // Template lives in global, but we claim it is in child.
                var tmpl = AddTemplate(user, mSession, gb);

                Assert.IsFalse(mSession.MoveFunctionTemplate(user, tmpl, child, gb, out var error),
                    "Claiming the wrong source boundary must fail.");
                Assert.IsNotNull(error);

                // Template must be unaffected.
                Assert.Contains(tmpl, gb.FunctionTemplates);
                Assert.AreSame(gb, tmpl.Parent);
            });
        }

        [TestMethod]
        public void TestMoveFunctionTemplate_NullArguments_Throw()
        {
            TestHelper.RunInModelSystemContext(nameof(TestMoveFunctionTemplate_NullArguments_Throw),
            (user, pSession, mSession) =>
            {
                var gb   = mSession.ModelSystem.GlobalBoundary;
                var tmpl = AddTemplate(user, mSession, gb);

                Assert.Throws<System.ArgumentNullException>(
                    () => mSession.MoveFunctionTemplate(null!, tmpl, gb, gb, out _));
                Assert.Throws<System.ArgumentNullException>(
                    () => mSession.MoveFunctionTemplate(user, null!, gb, gb, out _));
                Assert.Throws<System.ArgumentNullException>(
                    () => mSession.MoveFunctionTemplate(user, tmpl, null!, gb, out _));
                Assert.Throws<System.ArgumentNullException>(
                    () => mSession.MoveFunctionTemplate(user, tmpl, gb, null!, out _));
            });
        }
    }
}
