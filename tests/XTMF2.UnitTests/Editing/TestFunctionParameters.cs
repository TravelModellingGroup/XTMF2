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
using XTMF2.ModelSystemConstruct;
using XTMF2.Editing;

namespace XTMF2.UnitTests.Editing
{
    [TestClass]
    public class TestFunctionParameters
    {
        // ── Add / Remove ──────────────────────────────────────────────────

        [TestMethod]
        public void TestAddFunctionParameter()
        {
            TestHelper.RunInModelSystemContext(nameof(TestAddFunctionParameter), (user, pSession, mSession) =>
            {
                CommandError error = null;
                var ms = mSession.ModelSystem;
                Assert.IsTrue(mSession.AddFunctionTemplate(user, ms.GlobalBoundary, "MyFT",
                    out FunctionTemplate template, out error), error?.Message);

                Assert.IsTrue(mSession.AddFunctionParameter(user, template, "MyParam",
                    typeof(XTMF2.IModule), Rectangle.Hidden, out var fp, out error), error?.Message);

                Assert.IsNotNull(fp, "AddFunctionParameter should return a non-null parameter.");
                Assert.AreEqual("MyParam", fp.Name);
                Assert.AreEqual(typeof(XTMF2.IModule), fp.Type);
                Assert.HasCount(1, template.FunctionParameters);
                Assert.AreSame(template, fp.Template);
            });
        }

        [TestMethod]
        public void TestAddFunctionParameterUndo()
        {
            TestHelper.RunInModelSystemContext(nameof(TestAddFunctionParameterUndo), (user, pSession, mSession) =>
            {
                CommandError error = null;
                var ms = mSession.ModelSystem;
                Assert.IsTrue(mSession.AddFunctionTemplate(user, ms.GlobalBoundary, "MyFT",
                    out FunctionTemplate template, out error), error?.Message);

                Assert.IsTrue(mSession.AddFunctionParameter(user, template, "P1",
                    typeof(XTMF2.IModule), Rectangle.Hidden, out _, out error), error?.Message);
                Assert.HasCount(1, template.FunctionParameters);

                Assert.IsTrue(mSession.Undo(user, out error), error?.Message);
                Assert.IsEmpty(template.FunctionParameters, "Undo should remove the FunctionParameter.");

                Assert.IsTrue(mSession.Redo(user, out error), error?.Message);
                Assert.HasCount(1, template.FunctionParameters, "Redo should restore the FunctionParameter.");
            });
        }

        [TestMethod]
        public void TestRemoveFunctionParameter()
        {
            TestHelper.RunInModelSystemContext(nameof(TestRemoveFunctionParameter), (user, pSession, mSession) =>
            {
                CommandError error = null;
                var ms = mSession.ModelSystem;
                Assert.IsTrue(mSession.AddFunctionTemplate(user, ms.GlobalBoundary, "MyFT",
                    out FunctionTemplate template, out error), error?.Message);
                Assert.IsTrue(mSession.AddFunctionParameter(user, template, "P1",
                    typeof(XTMF2.IModule), Rectangle.Hidden, out var fp, out error), error?.Message);

                Assert.IsTrue(mSession.RemoveFunctionParameter(user, template, fp, out error), error?.Message);
                Assert.IsEmpty(template.FunctionParameters, "FunctionParameters should be empty after removal.");
            });
        }

        [TestMethod]
        public void TestRemoveFunctionParameterUndoRedo()
        {
            TestHelper.RunInModelSystemContext(nameof(TestRemoveFunctionParameterUndoRedo), (user, pSession, mSession) =>
            {
                CommandError error = null;
                var ms = mSession.ModelSystem;
                Assert.IsTrue(mSession.AddFunctionTemplate(user, ms.GlobalBoundary, "MyFT",
                    out FunctionTemplate template, out error), error?.Message);
                Assert.IsTrue(mSession.AddFunctionParameter(user, template, "P1",
                    typeof(XTMF2.IModule), Rectangle.Hidden, out var fp, out error), error?.Message);

                Assert.IsTrue(mSession.RemoveFunctionParameter(user, template, fp, out error), error?.Message);
                Assert.IsEmpty(template.FunctionParameters);

                // Undo: parameter comes back.
                Assert.IsTrue(mSession.Undo(user, out error), error?.Message);
                Assert.HasCount(1, template.FunctionParameters, "Undo should restore the FunctionParameter.");

                // Redo: parameter is removed again.
                Assert.IsTrue(mSession.Redo(user, out error), error?.Message);
                Assert.IsEmpty(template.FunctionParameters, "Redo should re-remove the FunctionParameter.");
            });
        }

        // ── Unique-name enforcement ───────────────────────────────────────

        [TestMethod]
        public void TestFunctionParameterNamesAreUnique()
        {
            TestHelper.RunInModelSystemContext(nameof(TestFunctionParameterNamesAreUnique), (user, pSession, mSession) =>
            {
                CommandError error = null;
                var ms = mSession.ModelSystem;
                Assert.IsTrue(mSession.AddFunctionTemplate(user, ms.GlobalBoundary, "MyFT",
                    out FunctionTemplate template, out error), error?.Message);

                Assert.IsTrue(mSession.AddFunctionParameter(user, template, "P1",
                    typeof(XTMF2.IModule), Rectangle.Hidden, out _, out error), error?.Message);

                // Adding a second parameter with the same name must fail.
                Assert.IsFalse(mSession.AddFunctionParameter(user, template, "P1",
                    typeof(XTMF2.IModule), Rectangle.Hidden, out _, out error),
                    "Adding a duplicate FunctionParameter name should fail.");
                Assert.IsNotNull(error, "An error should be set when the name is duplicate.");
            });
        }

        [TestMethod]
        public void TestFunctionParameterMultipleWithDifferentNames()
        {
            TestHelper.RunInModelSystemContext(nameof(TestFunctionParameterMultipleWithDifferentNames), (user, pSession, mSession) =>
            {
                CommandError error = null;
                var ms = mSession.ModelSystem;
                Assert.IsTrue(mSession.AddFunctionTemplate(user, ms.GlobalBoundary, "MyFT",
                    out FunctionTemplate template, out error), error?.Message);

                Assert.IsTrue(mSession.AddFunctionParameter(user, template, "A",
                    typeof(XTMF2.IModule), Rectangle.Hidden, out _, out error), error?.Message);
                Assert.IsTrue(mSession.AddFunctionParameter(user, template, "B",
                    typeof(XTMF2.IModule), Rectangle.Hidden, out _, out error), error?.Message);
                Assert.IsTrue(mSession.AddFunctionParameter(user, template, "C",
                    typeof(XTMF2.IModule), Rectangle.Hidden, out _, out error), error?.Message);

                Assert.HasCount(3, template.FunctionParameters);
            });
        }

        // ── FunctionInstance hooks reflect FunctionParameters ─────────────

        [TestMethod]
        public void TestFunctionInstanceHooksReflectFunctionParameters()
        {
            TestHelper.RunInModelSystemContext(nameof(TestFunctionInstanceHooksReflectFunctionParameters), (user, pSession, mSession) =>
            {
                CommandError error = null;
                var ms = mSession.ModelSystem;
                Assert.IsTrue(mSession.AddFunctionTemplate(user, ms.GlobalBoundary, "MyFT",
                    out FunctionTemplate template, out error), error?.Message);
                Assert.IsTrue(mSession.AddFunctionInstance(user, ms.GlobalBoundary, template, "MyFI",
                    Rectangle.Hidden, out var fi, out error), error?.Message);

                // No parameters yet — FI should have no hooks.
                Assert.IsEmpty(fi.Hooks, "FunctionInstance should start with no hooks when template has no parameters.");

                // Add two function parameters to the template.
                Assert.IsTrue(mSession.AddFunctionParameter(user, template, "Alpha",
                    typeof(XTMF2.IModule), Rectangle.Hidden, out _, out error), error?.Message);
                Assert.IsTrue(mSession.AddFunctionParameter(user, template, "Beta",
                    typeof(XTMF2.IModule), Rectangle.Hidden, out _, out error), error?.Message);

                Assert.HasCount(2, fi.Hooks, "FunctionInstance.Hooks should mirror the template's FunctionParameters.");
                Assert.AreEqual("Alpha", fi.Hooks[0].Name);
                Assert.AreEqual("Beta",  fi.Hooks[1].Name);
            });
        }

        [TestMethod]
        public void TestFunctionInstanceHooksUpdateOnParameterRemoval()
        {
            TestHelper.RunInModelSystemContext(nameof(TestFunctionInstanceHooksUpdateOnParameterRemoval), (user, pSession, mSession) =>
            {
                CommandError error = null;
                var ms = mSession.ModelSystem;
                Assert.IsTrue(mSession.AddFunctionTemplate(user, ms.GlobalBoundary, "FT",
                    out FunctionTemplate template, out error), error?.Message);
                Assert.IsTrue(mSession.AddFunctionParameter(user, template, "X",
                    typeof(XTMF2.IModule), Rectangle.Hidden, out var fp, out error), error?.Message);
                Assert.IsTrue(mSession.AddFunctionInstance(user, ms.GlobalBoundary, template, "FI",
                    Rectangle.Hidden, out var fi, out error), error?.Message);

                Assert.HasCount(1, fi.Hooks);

                Assert.IsTrue(mSession.RemoveFunctionParameter(user, template, fp, out error), error?.Message);
                Assert.IsEmpty(fi.Hooks, "Removing FunctionParameter should also remove the corresponding FI hook.");
            });
        }

        // ── FunctionParameter is a valid link destination inside template ─

        [TestMethod]
        public void TestLinkToFunctionParameterInsideTemplate()
        {
            TestHelper.RunInModelSystemContext(nameof(TestLinkToFunctionParameterInsideTemplate), (user, pSession, mSession) =>
            {
                CommandError error = null;
                var ms = mSession.ModelSystem;
                Assert.IsTrue(mSession.AddFunctionTemplate(user, ms.GlobalBoundary, "FT",
                    out FunctionTemplate template, out error), error?.Message);

                // Add a FunctionParameter (valid link destination).
                Assert.IsTrue(mSession.AddFunctionParameter(user, template, "Input",
                    typeof(XTMF2.IModule), Rectangle.Hidden, out var fp, out error), error?.Message);

                // Add a node inside the template whose first hook accepts IModule.
                Assert.IsTrue(mSession.AddNode(user, template.InternalModules, "Inner",
                    typeof(XTMF2.UnitTests.Modules.SimpleParameterModule),
                    Rectangle.Hidden, out var innerNode, out error), error?.Message);

                // Get the hook that accepts IFunction<string>.
                var hook = innerNode.Hooks?.FirstOrDefault(h => h.Name == "Real Function");
                Assert.IsNotNull(hook, "SimpleParameterModule should have a 'Real Function' hook.");

                // Link inner node's hook -> FunctionParameter (should succeed).
                Assert.IsTrue(mSession.AddLink(user, innerNode, hook, fp,
                    out _, out error), error?.Message);

                // The link should now exist in the template's InternalModules.
                Assert.IsTrue(template.InternalModules.Links.Any(l => l is SingleLink sl && sl.Destination == fp),
                    "InternalModules should contain a link whose destination is the FunctionParameter.");
            });
        }

        // ── Save / Load roundtrip ─────────────────────────────────────────

        [TestMethod]
        public void TestFunctionParameterSaveLoad()
        {
            TestHelper.RunInModelSystemContext(nameof(TestFunctionParameterSaveLoad), (user, pSession, mSession) =>
            {
                CommandError error = null;
                var ms = mSession.ModelSystem;
                Assert.IsTrue(mSession.AddFunctionTemplate(user, ms.GlobalBoundary, "FT",
                    out FunctionTemplate template, out error), error?.Message);
                Assert.IsTrue(mSession.AddFunctionParameter(user, template, "Alpha",
                    typeof(XTMF2.IModule), Rectangle.Hidden, out _, out error), error?.Message);
                Assert.IsTrue(mSession.AddFunctionParameter(user, template, "Beta",
                    typeof(XTMF2.IModule), Rectangle.Hidden, out _, out error), error?.Message);

                // Save and reload the model system.
                Assert.IsTrue(pSession.Save(out error), error?.Message);
            },
            (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                var template = ms.GlobalBoundary.FunctionTemplates.FirstOrDefault(ft => ft.Name == "FT");
                Assert.IsNotNull(template, "FunctionTemplate 'FT' should survive save/load.");
                Assert.HasCount(2, template.FunctionParameters,
                    "Both FunctionParameters should survive save/load.");
                Assert.AreEqual("Alpha", template.FunctionParameters[0].Name);
                Assert.AreEqual("Beta",  template.FunctionParameters[1].Name);
                Assert.AreEqual(typeof(XTMF2.IModule), template.FunctionParameters[0].Type);
            });
        }
    }
}
