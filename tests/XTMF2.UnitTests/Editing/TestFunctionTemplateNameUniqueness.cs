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

namespace XTMF2.UnitTests.Editing
{
    /// <summary>
    /// Verifies that FunctionTemplate names are unique across the entire model-system boundary tree
    /// so that FunctionInstances can never reference an ambiguous template.
    /// </summary>
    [TestClass]
    public class TestFunctionTemplateNameUniqueness
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

        // ── Add – same boundary ────────────────────────────────────────────────

        [TestMethod]
        public void TestAddFunctionTemplate_SameBoundary_DuplicateNameRejected()
        {
            TestHelper.RunInModelSystemContext(nameof(TestAddFunctionTemplate_SameBoundary_DuplicateNameRejected),
            (user, pSession, ms) =>
            {
                var gb = ms.ModelSystem.GlobalBoundary;
                AddTemplate(user, ms, gb, "Alpha");

                var ok = ms.AddFunctionTemplate(user, gb, "Alpha", out var ft2, out var error);
                Assert.IsFalse(ok, "Expected duplicate to be rejected in the same boundary.");
                Assert.IsNotNull(error);
            });
        }

        [TestMethod]
        public void TestAddFunctionTemplate_SameBoundary_UniqueNamesAccepted()
        {
            TestHelper.RunInModelSystemContext(nameof(TestAddFunctionTemplate_SameBoundary_UniqueNamesAccepted),
            (user, pSession, ms) =>
            {
                var gb = ms.ModelSystem.GlobalBoundary;
                AddTemplate(user, ms, gb, "Alpha");
                AddTemplate(user, ms, gb, "Beta");
                Assert.HasCount(2, gb.FunctionTemplates);
            });
        }

        // ── Add – parent / child boundary ─────────────────────────────────────

        [TestMethod]
        public void TestAddFunctionTemplate_ChildBoundary_DuplicatesParentNameRejected()
        {
            TestHelper.RunInModelSystemContext(nameof(TestAddFunctionTemplate_ChildBoundary_DuplicatesParentNameRejected),
            (user, pSession, ms) =>
            {
                var gb    = ms.ModelSystem.GlobalBoundary;
                var child = AddChildBoundary(user, ms, gb, "ChildA");
                AddTemplate(user, ms, gb, "Alpha");

                var ok = ms.AddFunctionTemplate(user, child, "Alpha", out var ft2, out var error);
                Assert.IsFalse(ok, "Expected duplicate of parent template to be rejected in child boundary.");
                Assert.IsNotNull(error);
            });
        }

        [TestMethod]
        public void TestAddFunctionTemplate_ParentBoundary_DuplicatesChildNameRejected()
        {
            TestHelper.RunInModelSystemContext(nameof(TestAddFunctionTemplate_ParentBoundary_DuplicatesChildNameRejected),
            (user, pSession, ms) =>
            {
                var gb    = ms.ModelSystem.GlobalBoundary;
                var child = AddChildBoundary(user, ms, gb, "ChildA");
                AddTemplate(user, ms, child, "Alpha");

                var ok = ms.AddFunctionTemplate(user, gb, "Alpha", out var ft2, out var error);
                Assert.IsFalse(ok, "Expected duplicate of child template to be rejected in parent boundary.");
                Assert.IsNotNull(error);
            });
        }

        [TestMethod]
        public void TestAddFunctionTemplate_ChildBoundary_UniqueNameAccepted()
        {
            TestHelper.RunInModelSystemContext(nameof(TestAddFunctionTemplate_ChildBoundary_UniqueNameAccepted),
            (user, pSession, ms) =>
            {
                var gb    = ms.ModelSystem.GlobalBoundary;
                var child = AddChildBoundary(user, ms, gb, "ChildA");
                AddTemplate(user, ms, gb,    "Alpha");
                AddTemplate(user, ms, child, "Beta");
                Assert.HasCount(1, gb.FunctionTemplates);
                Assert.HasCount(1, child.FunctionTemplates);
            });
        }

        // ── Add – sibling boundaries ──────────────────────────────────────────

        [TestMethod]
        public void TestAddFunctionTemplate_SiblingBoundary_DuplicateNameRejected()
        {
            TestHelper.RunInModelSystemContext(nameof(TestAddFunctionTemplate_SiblingBoundary_DuplicateNameRejected),
            (user, pSession, ms) =>
            {
                var gb     = ms.ModelSystem.GlobalBoundary;
                var childA = AddChildBoundary(user, ms, gb, "ChildA");
                var childB = AddChildBoundary(user, ms, gb, "ChildB");
                AddTemplate(user, ms, childA, "Alpha");

                var ok = ms.AddFunctionTemplate(user, childB, "Alpha", out var ft2, out var error);
                Assert.IsFalse(ok, "Expected duplicate of sibling template to be rejected.");
                Assert.IsNotNull(error);
            });
        }

        [TestMethod]
        public void TestAddFunctionTemplate_SiblingBoundary_UniqueNamesAccepted()
        {
            TestHelper.RunInModelSystemContext(nameof(TestAddFunctionTemplate_SiblingBoundary_UniqueNamesAccepted),
            (user, pSession, ms) =>
            {
                var gb     = ms.ModelSystem.GlobalBoundary;
                var childA = AddChildBoundary(user, ms, gb, "ChildA");
                var childB = AddChildBoundary(user, ms, gb, "ChildB");
                AddTemplate(user, ms, childA, "Alpha");
                AddTemplate(user, ms, childB, "Beta");
            });
        }

        // ── Add – deeply nested ───────────────────────────────────────────────

        [TestMethod]
        public void TestAddFunctionTemplate_DeeplyNested_DuplicateRejected()
        {
            TestHelper.RunInModelSystemContext(nameof(TestAddFunctionTemplate_DeeplyNested_DuplicateRejected),
            (user, pSession, ms) =>
            {
                var gb     = ms.ModelSystem.GlobalBoundary;
                var level1 = AddChildBoundary(user, ms, gb,     "L1");
                var level2 = AddChildBoundary(user, ms, level1, "L2");
                AddTemplate(user, ms, level2, "DeepTemplate");

                var ok = ms.AddFunctionTemplate(user, gb, "DeepTemplate", out var ft2, out var error);
                Assert.IsFalse(ok, "Expected deeply nested duplicate to be rejected at root.");
                Assert.IsNotNull(error);

                ok = ms.AddFunctionTemplate(user, level1, "DeepTemplate", out ft2, out error);
                Assert.IsFalse(ok, "Expected deeply nested duplicate to be rejected at level1.");
                Assert.IsNotNull(error);
            });
        }

        // ── Rename ────────────────────────────────────────────────────────────

        [TestMethod]
        public void TestRenameFunctionTemplate_DuplicateNameSameBoundaryRejected()
        {
            TestHelper.RunInModelSystemContext(nameof(TestRenameFunctionTemplate_DuplicateNameSameBoundaryRejected),
            (user, pSession, ms) =>
            {
                var gb    = ms.ModelSystem.GlobalBoundary;
                var alpha = AddTemplate(user, ms, gb, "Alpha");
                AddTemplate(user, ms, gb, "Beta");

                var ok = ms.RenameFunctionTemplate(user, alpha, "Beta", out var error);
                Assert.IsFalse(ok, "Expected rename to existing name to be rejected.");
                Assert.IsNotNull(error);
                Assert.AreEqual("Alpha", alpha.Name);
            });
        }

        [TestMethod]
        public void TestRenameFunctionTemplate_DuplicateNameChildBoundaryRejected()
        {
            TestHelper.RunInModelSystemContext(nameof(TestRenameFunctionTemplate_DuplicateNameChildBoundaryRejected),
            (user, pSession, ms) =>
            {
                var gb    = ms.ModelSystem.GlobalBoundary;
                var child = AddChildBoundary(user, ms, gb, "ChildA");
                var alpha = AddTemplate(user, ms, gb,    "Alpha");
                AddTemplate(user, ms, child, "Beta");

                var ok = ms.RenameFunctionTemplate(user, alpha, "Beta", out var error);
                Assert.IsFalse(ok, "Expected rename to a name in a child boundary to be rejected.");
                Assert.AreEqual("Alpha", alpha.Name);
            });
        }

        [TestMethod]
        public void TestRenameFunctionTemplate_SameNameAccepted()
        {
            TestHelper.RunInModelSystemContext(nameof(TestRenameFunctionTemplate_SameNameAccepted),
            (user, pSession, ms) =>
            {
                var gb    = ms.ModelSystem.GlobalBoundary;
                var alpha = AddTemplate(user, ms, gb, "Alpha");

                var ok = ms.RenameFunctionTemplate(user, alpha, "Alpha", out var error);
                Assert.IsTrue(ok, error?.Message);
                Assert.AreEqual("Alpha", alpha.Name);
            });
        }

        [TestMethod]
        public void TestRenameFunctionTemplate_UniqueNameAccepted()
        {
            TestHelper.RunInModelSystemContext(nameof(TestRenameFunctionTemplate_UniqueNameAccepted),
            (user, pSession, ms) =>
            {
                var gb    = ms.ModelSystem.GlobalBoundary;
                var alpha = AddTemplate(user, ms, gb, "Alpha");

                var ok = ms.RenameFunctionTemplate(user, alpha, "Gamma", out var error);
                Assert.IsTrue(ok, error?.Message);
                Assert.AreEqual("Gamma", alpha.Name);
            });
        }

        // ── Undo restores name availability ───────────────────────────────────

        [TestMethod]
        public void TestAddFunctionTemplate_AfterUndo_NameBecomesAvailableAgain()
        {
            TestHelper.RunInModelSystemContext(nameof(TestAddFunctionTemplate_AfterUndo_NameBecomesAvailableAgain),
            (user, pSession, ms) =>
            {
                var gb = ms.ModelSystem.GlobalBoundary;
                AddTemplate(user, ms, gb, "Alpha");

                Assert.IsTrue(ms.Undo(user, out var undoErr), undoErr?.Message);
                Assert.IsEmpty(gb.FunctionTemplates);

                AddTemplate(user, ms, gb, "Alpha");
                Assert.HasCount(1, gb.FunctionTemplates);
            });
        }

        [TestMethod]
        public void TestRenameFunctionTemplate_AfterUndo_OldNameFreedNewNameBlocked()
        {
            TestHelper.RunInModelSystemContext(nameof(TestRenameFunctionTemplate_AfterUndo_OldNameFreedNewNameBlocked),
            (user, pSession, ms) =>
            {
                var gb    = ms.ModelSystem.GlobalBoundary;
                var alpha = AddTemplate(user, ms, gb, "Alpha");

                Assert.IsTrue(ms.RenameFunctionTemplate(user, alpha, "Gamma", out var err1), err1?.Message);

                // "Gamma" is now taken.
                var ok = ms.AddFunctionTemplate(user, gb, "Gamma", out var ft2, out var error);
                Assert.IsFalse(ok, "Expected 'Gamma' to be blocked after rename.");

                // Undo the rename — name reverts to "Alpha"; "Gamma" is free.
                Assert.IsTrue(ms.Undo(user, out var err2), err2?.Message);
                Assert.AreEqual("Alpha", alpha.Name);

                ok = ms.AddFunctionTemplate(user, gb, "Gamma", out ft2, out error);
                Assert.IsTrue(ok, error?.Message);
            });
        }
    }
}
