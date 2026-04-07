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
using System.Linq;
using XTMF2.Editing;
using XTMF2.ModelSystemConstruct;
using XTMF2.RuntimeModules;
using XTMF2.UnitTests.Modules;

namespace XTMF2.UnitTests.Editing;

/// <summary>
/// Tests that ScriptedParameter expression text is kept in sync when a
/// model-system variable or local variable node is renamed via
/// <see cref="ModelSystemSession.SetNodeName"/>.
/// </summary>
[TestClass]
public class TestRenameVariableUpdatesScriptedParameters
{
    // ── Helpers ───────────────────────────────────────────────────────────

    private static Node CreateBasicParamNode<T>(ModelSystemSession ms, User user,
        Boundary boundary, string name, string value)
    {
        CommandError error = null;
        Assert.IsTrue(ms.AddNode(user, boundary, name, typeof(BasicParameter<T>),
            Rectangle.Hidden, out var node, out error), error?.Message);
        Assert.IsTrue(ms.SetParameterValue(user, node, value, out error), error?.Message);
        return node!;
    }

    private static Node CreateScriptedParamNode<T>(ModelSystemSession ms, User user,
        Boundary boundary, string name)
    {
        CommandError error = null;
        Assert.IsTrue(ms.AddNode(user, boundary, name, typeof(ScriptedParameter<T>),
            Rectangle.Hidden, out var node, out error), error?.Message);
        return node!;
    }

    // ── Model-system variable rename - scripted parameter update ─────────

    [TestMethod]
    public void RenameModelSystemVariable_UpdatesScriptedParameterRepresentation()
    {
        TestHelper.RunInModelSystemContext(
            nameof(RenameModelSystemVariable_UpdatesScriptedParameterRepresentation),
            (user, _, ms) =>
            {
                CommandError error = null;
                var gb = ms.ModelSystem.GlobalBoundary;

                // Create a global variable node and register it.
                var varNode = CreateBasicParamNode<int>(ms, user, gb, "myVar", "10");
                Assert.IsTrue(ms.AddVariable(user, varNode, out error), error?.Message);

                // Create a ScriptedParameter that references the variable.
                var scriptedNode = CreateScriptedParamNode<int>(ms, user, gb, "scriptedResult");
                Assert.IsTrue(ms.SetParameterExpression(user, scriptedNode, "myVar", out error),
                    error?.Message);
                Assert.AreEqual("myVar", scriptedNode.ParameterValue!.Representation.Trim());

                // Rename the variable node.
                Assert.IsTrue(ms.SetNodeName(user, varNode, "renamedVar", out error), error?.Message);
                Assert.AreEqual("renamedVar", varNode.Name);

                // The scripted parameter's representation must reflect the new name.
                Assert.AreEqual("renamedVar", scriptedNode.ParameterValue!.Representation.Trim(),
                    "ScriptedParameter expression should have been updated after variable rename.");
            });
    }

    [TestMethod]
    public void RenameModelSystemVariable_ScriptedParameterStillCompiles()
    {
        // Verify that after the rename the expression compiles correctly
        // (i.e. the node reference is updated, not just the text).
        TestHelper.RunInModelSystemContext(
            nameof(RenameModelSystemVariable_ScriptedParameterStillCompiles),
            (user, _, ms) =>
            {
                CommandError error = null;
                var gb = ms.ModelSystem.GlobalBoundary;

                var varNode = CreateBasicParamNode<int>(ms, user, gb, "x", "7");
                Assert.IsTrue(ms.AddVariable(user, varNode, out error), error?.Message);

                var scriptedNode = CreateScriptedParamNode<int>(ms, user, gb, "s");
                Assert.IsTrue(ms.SetParameterExpression(user, scriptedNode, "x", out error),
                    error?.Message);

                Assert.IsTrue(ms.SetNodeName(user, varNode, "y", out error), error?.Message);

                // The Representation should now be "y".
                Assert.AreEqual("y", scriptedNode.ParameterValue!.Representation.Trim());
            });
    }

    [TestMethod]
    public void RenameModelSystemVariable_UndoRestoresRepresentation()
    {
        TestHelper.RunInModelSystemContext(
            nameof(RenameModelSystemVariable_UndoRestoresRepresentation),
            (user, _, ms) =>
            {
                CommandError error = null;
                var gb = ms.ModelSystem.GlobalBoundary;

                var varNode = CreateBasicParamNode<int>(ms, user, gb, "oldName", "5");
                Assert.IsTrue(ms.AddVariable(user, varNode, out error), error?.Message);

                var scriptedNode = CreateScriptedParamNode<int>(ms, user, gb, "s");
                Assert.IsTrue(ms.SetParameterExpression(user, scriptedNode, "oldName", out error),
                    error?.Message);

                Assert.IsTrue(ms.SetNodeName(user, varNode, "newName", out error), error?.Message);
                Assert.AreEqual("newName", scriptedNode.ParameterValue!.Representation.Trim());

                // Undo should restore both the node name and the scripted parameter text.
                Assert.IsTrue(ms.Undo(user, out error), error?.Message);
                Assert.AreEqual("oldName", varNode.Name);
                Assert.AreEqual("oldName", scriptedNode.ParameterValue!.Representation.Trim(),
                    "Undo should restore the scripted parameter representation to the old name.");
            });
    }

    [TestMethod]
    public void RenameModelSystemVariable_RedoReappliesRepresentation()
    {
        TestHelper.RunInModelSystemContext(
            nameof(RenameModelSystemVariable_RedoReappliesRepresentation),
            (user, _, ms) =>
            {
                CommandError error = null;
                var gb = ms.ModelSystem.GlobalBoundary;

                var varNode = CreateBasicParamNode<int>(ms, user, gb, "before", "1");
                Assert.IsTrue(ms.AddVariable(user, varNode, out error), error?.Message);

                var scriptedNode = CreateScriptedParamNode<int>(ms, user, gb, "s");
                Assert.IsTrue(ms.SetParameterExpression(user, scriptedNode, "before", out error),
                    error?.Message);

                Assert.IsTrue(ms.SetNodeName(user, varNode, "after", out error), error?.Message);
                Assert.IsTrue(ms.Undo(user, out error), error?.Message);
                Assert.IsTrue(ms.Redo(user, out error), error?.Message);

                Assert.AreEqual("after", varNode.Name);
                Assert.AreEqual("after", scriptedNode.ParameterValue!.Representation.Trim(),
                    "Redo should re-apply the renamed variable in the scripted parameter representation.");
            });
    }

    [TestMethod]
    public void RenameNonVariable_DoesNotModifyScriptedParameters()
    {
        // Renaming an ordinary node (not a variable) should leave scripted parameters untouched.
        TestHelper.RunInModelSystemContext(
            nameof(RenameNonVariable_DoesNotModifyScriptedParameters),
            (user, _, ms) =>
            {
                CommandError error = null;
                var gb = ms.ModelSystem.GlobalBoundary;

                // "plain" is a regular node, NOT added to ModelSystem.Variables.
                var plainNode = CreateBasicParamNode<int>(ms, user, gb, "plain", "3");

                var varNode = CreateBasicParamNode<int>(ms, user, gb, "myVar", "99");
                Assert.IsTrue(ms.AddVariable(user, varNode, out error), error?.Message);

                var scriptedNode = CreateScriptedParamNode<int>(ms, user, gb, "s");
                Assert.IsTrue(ms.SetParameterExpression(user, scriptedNode, "myVar", out error),
                    error?.Message);

                // Rename the non-variable node.
                Assert.IsTrue(ms.SetNodeName(user, plainNode, "renamedPlain", out error), error?.Message);

                // Scripted parameter should be unchanged.
                Assert.AreEqual("myVar", scriptedNode.ParameterValue!.Representation.Trim(),
                    "Renaming a non-variable node must not touch scripted parameters.");
            });
    }

    [TestMethod]
    public void RenameModelSystemVariable_MultipleScriptedParametersUpdated()
    {
        TestHelper.RunInModelSystemContext(
            nameof(RenameModelSystemVariable_MultipleScriptedParametersUpdated),
            (user, _, ms) =>
            {
                CommandError error = null;
                var gb = ms.ModelSystem.GlobalBoundary;

                var varNode = CreateBasicParamNode<int>(ms, user, gb, "count", "3");
                Assert.IsTrue(ms.AddVariable(user, varNode, out error), error?.Message);

                // Two scripted parameter nodes referencing the same variable.
                var s1 = CreateScriptedParamNode<int>(ms, user, gb, "s1");
                var s2 = CreateScriptedParamNode<int>(ms, user, gb, "s2");
                Assert.IsTrue(ms.SetParameterExpression(user, s1, "count", out error), error?.Message);
                Assert.IsTrue(ms.SetParameterExpression(user, s2, "count + 1", out error), error?.Message);

                Assert.IsTrue(ms.SetNodeName(user, varNode, "total", out error), error?.Message);

                Assert.AreEqual("total", s1.ParameterValue!.Representation.Trim());
                Assert.AreEqual("total + 1", s2.ParameterValue!.Representation.Trim());
            });
    }

    // ── Local variable rename - scripted parameter update ─────────────────

    [TestMethod]
    public void RenameLocalVariable_UpdatesScriptedParameterInsideTemplate()
    {
        TestHelper.RunInModelSystemContext(
            nameof(RenameLocalVariable_UpdatesScriptedParameterInsideTemplate),
            (user, _, ms) =>
            {
                CommandError error = null;
                var gb = ms.ModelSystem.GlobalBoundary;

                Assert.IsTrue(ms.AddFunctionTemplate(user, gb, "MyFT",
                    out var ft, out error), error?.Message);

                // Create a local variable inside the template.
                var localVar = CreateBasicParamNode<int>(ms, user, ft.InternalModules, "lv", "5");
                Assert.IsTrue(ms.AddFunctionTemplateVariable(user, ft, localVar, out error), error?.Message);

                // A ScriptedParameter inside the template references the local variable.
                var scriptedNode = CreateScriptedParamNode<int>(ms, user, ft.InternalModules, "s");
                Assert.IsTrue(ms.SetParameterExpression(user, scriptedNode, "lv", out error),
                    error?.Message);
                Assert.AreEqual("lv", scriptedNode.ParameterValue!.Representation.Trim());

                // Rename the local variable.
                Assert.IsTrue(ms.SetNodeName(user, localVar, "lvRenamed", out error), error?.Message);

                Assert.AreEqual("lvRenamed", scriptedNode.ParameterValue!.Representation.Trim(),
                    "ScriptedParameter inside the FunctionTemplate should reflect the renamed local var.");
            });
    }

    [TestMethod]
    public void RenameLocalVariable_DoesNotAffectScriptedParametersOutsideTemplate()
    {
        TestHelper.RunInModelSystemContext(
            nameof(RenameLocalVariable_DoesNotAffectScriptedParametersOutsideTemplate),
            (user, _, ms) =>
            {
                CommandError error = null;
                var gb = ms.ModelSystem.GlobalBoundary;

                // Global variable with same name as the local variable.
                var globalVar = CreateBasicParamNode<int>(ms, user, gb, "lv", "100");
                Assert.IsTrue(ms.AddVariable(user, globalVar, out error), error?.Message);

                Assert.IsTrue(ms.AddFunctionTemplate(user, gb, "MyFT",
                    out var ft, out error), error?.Message);

                var localVar = CreateBasicParamNode<int>(ms, user, ft.InternalModules, "lv", "5");
                Assert.IsTrue(ms.AddFunctionTemplateVariable(user, ft, localVar, out error), error?.Message);

                // Scripted parameter OUTSIDE the template references the GLOBAL "lv".
                var outsideScripted = CreateScriptedParamNode<int>(ms, user, gb, "outside");
                Assert.IsTrue(ms.SetParameterExpression(user, outsideScripted, "lv", out error),
                    error?.Message);

                // Rename the LOCAL variable.
                Assert.IsTrue(ms.SetNodeName(user, localVar, "lvLocal", out error), error?.Message);

                // The outside scripted parameter should still reference the global "lv" by name.
                Assert.AreEqual("lv", outsideScripted.ParameterValue!.Representation.Trim(),
                    "Renaming a local variable must not touch scripted parameters outside the template.");
            });
    }

    [TestMethod]
    public void RenameGlobalVariable_SkipsTemplatesWithShadowingLocalVariable()
    {
        // When a FunctionTemplate has a local variable with the same old name as the
        // global variable being renamed, ScriptedParameters inside that template
        // reference the LOCAL variable (which shadows the global). They must NOT be updated.
        TestHelper.RunInModelSystemContext(
            nameof(RenameGlobalVariable_SkipsTemplatesWithShadowingLocalVariable),
            (user, _, ms) =>
            {
                CommandError error = null;
                var gb = ms.ModelSystem.GlobalBoundary;

                // Global variable named "x".
                var globalVar = CreateBasicParamNode<int>(ms, user, gb, "x", "1");
                Assert.IsTrue(ms.AddVariable(user, globalVar, out error), error?.Message);

                // FunctionTemplate with a local variable ALSO named "x" (shadows global).
                Assert.IsTrue(ms.AddFunctionTemplate(user, gb, "FT",
                    out var ft, out error), error?.Message);
                var localX = CreateBasicParamNode<int>(ms, user, ft.InternalModules, "x", "99");
                Assert.IsTrue(ms.AddFunctionTemplateVariable(user, ft, localX, out error), error?.Message);

                // Scripted parameter inside the template — references the LOCAL "x".
                var innerScripted = CreateScriptedParamNode<int>(ms, user, ft.InternalModules, "s");
                Assert.IsTrue(ms.SetParameterExpression(user, innerScripted, "x", out error),
                    error?.Message);

                // Scripted parameter OUTSIDE the template — references the GLOBAL "x".
                var outerScripted = CreateScriptedParamNode<int>(ms, user, gb, "sOut");
                Assert.IsTrue(ms.SetParameterExpression(user, outerScripted, "x", out error),
                    error?.Message);

                // Rename the GLOBAL variable from "x" to "xGlobal".
                Assert.IsTrue(ms.SetNodeName(user, globalVar, "xGlobal", out error), error?.Message);

                // The outer scripted parameter should be updated.
                Assert.AreEqual("xGlobal", outerScripted.ParameterValue!.Representation.Trim(),
                    "Scripted parameter referencing the global variable should be updated.");

                // The inner scripted parameter (which references the LOCAL x) must NOT be updated.
                Assert.AreEqual("x", innerScripted.ParameterValue!.Representation.Trim(),
                    "Scripted parameter inside a template with a shadowing local variable must not be changed.");
            });
    }
}
