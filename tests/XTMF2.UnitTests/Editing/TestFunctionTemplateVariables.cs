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
using XTMF2.Editing;
using XTMF2.ModelSystemConstruct;
using XTMF2.ModelSystemConstruct.Parameters.Compiler;
using XTMF2.RuntimeModules;

namespace XTMF2.UnitTests.Editing;

[TestClass]
public class TestFunctionTemplateVariables
{
    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="BasicParameter{T}"/> node inside the given boundary,
    /// sets its literal value, and returns the node.
    /// </summary>
    private static Node CreateLocalVar<T>(ModelSystemSession session, User user,
        Boundary boundary, string name, string value)
    {
        CommandError error = null;
        Assert.IsTrue(session.AddNode(user, boundary, name, typeof(BasicParameter<T>),
            Rectangle.Hidden, out var node, out error), error?.Message);
        Assert.IsTrue(session.SetParameterValue(user, node, value, out error), error?.Message);
        return node!;
    }

    // ── Add / Remove ──────────────────────────────────────────────────────

    [TestMethod]
    public void AddFunctionTemplateVariable_Succeeds()
    {
        TestHelper.RunInModelSystemContext(nameof(AddFunctionTemplateVariable_Succeeds),
            (user, _, ms) =>
            {
                CommandError error = null;
                Assert.IsTrue(ms.AddFunctionTemplate(user, ms.ModelSystem.GlobalBoundary, "MyFT",
                    out var ft, out error), error?.Message);

                var node = CreateLocalVar<int>(ms, user, ft.InternalModules, "myVar", "42");

                Assert.IsTrue(ms.AddFunctionTemplateVariable(user, ft, node, out error), error?.Message);
                Assert.HasCount(1, ft.LocalVariables);
                Assert.AreSame(node, ft.LocalVariables[0]);
            });
    }

    [TestMethod]
    public void AddFunctionTemplateVariable_Undo()
    {
        TestHelper.RunInModelSystemContext(nameof(AddFunctionTemplateVariable_Undo),
            (user, _, ms) =>
            {
                CommandError error = null;
                Assert.IsTrue(ms.AddFunctionTemplate(user, ms.ModelSystem.GlobalBoundary, "MyFT",
                    out var ft, out error), error?.Message);

                var node = CreateLocalVar<int>(ms, user, ft.InternalModules, "myVar", "42");

                Assert.IsTrue(ms.AddFunctionTemplateVariable(user, ft, node, out error), error?.Message);
                Assert.HasCount(1, ft.LocalVariables);

                Assert.IsTrue(ms.Undo(user, out error), error?.Message);
                Assert.IsEmpty(ft.LocalVariables, "Undo should remove the local variable.");
            });
    }

    [TestMethod]
    public void AddFunctionTemplateVariable_Redo()
    {
        TestHelper.RunInModelSystemContext(nameof(AddFunctionTemplateVariable_Redo),
            (user, _, ms) =>
            {
                CommandError error = null;
                Assert.IsTrue(ms.AddFunctionTemplate(user, ms.ModelSystem.GlobalBoundary, "MyFT",
                    out var ft, out error), error?.Message);

                var node = CreateLocalVar<int>(ms, user, ft.InternalModules, "myVar", "42");

                Assert.IsTrue(ms.AddFunctionTemplateVariable(user, ft, node, out error), error?.Message);
                Assert.IsTrue(ms.Undo(user, out error), error?.Message);
                Assert.IsEmpty(ft.LocalVariables);

                Assert.IsTrue(ms.Redo(user, out error), error?.Message);
                Assert.HasCount(1, ft.LocalVariables, "Redo should restore the local variable.");
            });
    }

    [TestMethod]
    public void RemoveFunctionTemplateVariable_Succeeds()
    {
        TestHelper.RunInModelSystemContext(nameof(RemoveFunctionTemplateVariable_Succeeds),
            (user, _, ms) =>
            {
                CommandError error = null;
                Assert.IsTrue(ms.AddFunctionTemplate(user, ms.ModelSystem.GlobalBoundary, "MyFT",
                    out var ft, out error), error?.Message);

                var node = CreateLocalVar<int>(ms, user, ft.InternalModules, "myVar", "7");
                Assert.IsTrue(ms.AddFunctionTemplateVariable(user, ft, node, out error), error?.Message);
                Assert.HasCount(1, ft.LocalVariables);

                Assert.IsTrue(ms.RemoveFunctionTemplateVariable(user, ft, node, out error), error?.Message);
                Assert.IsEmpty(ft.LocalVariables, "LocalVariables should be empty after removal.");
            });
    }

    [TestMethod]
    public void RemoveFunctionTemplateVariable_Undo()
    {
        TestHelper.RunInModelSystemContext(nameof(RemoveFunctionTemplateVariable_Undo),
            (user, _, ms) =>
            {
                CommandError error = null;
                Assert.IsTrue(ms.AddFunctionTemplate(user, ms.ModelSystem.GlobalBoundary, "MyFT",
                    out var ft, out error), error?.Message);

                var node = CreateLocalVar<int>(ms, user, ft.InternalModules, "myVar", "7");
                Assert.IsTrue(ms.AddFunctionTemplateVariable(user, ft, node, out error), error?.Message);
                Assert.IsTrue(ms.RemoveFunctionTemplateVariable(user, ft, node, out error), error?.Message);
                Assert.IsEmpty(ft.LocalVariables);

                Assert.IsTrue(ms.Undo(user, out error), error?.Message);
                Assert.HasCount(1, ft.LocalVariables, "Undo should restore the local variable.");
            });
    }

    // ── Validation ────────────────────────────────────────────────────────

    [TestMethod]
    public void AddFunctionTemplateVariable_IncompatibleType_Fails()
    {
        TestHelper.RunInModelSystemContext(nameof(AddFunctionTemplateVariable_IncompatibleType_Fails),
            (user, _, ms) =>
            {
                CommandError error = null;
                Assert.IsTrue(ms.AddFunctionTemplate(user, ms.ModelSystem.GlobalBoundary, "MyFT",
                    out var ft, out error), error?.Message);

                // Node with IModule type has no basic-type parameter value → cannot be a variable.
                Assert.IsTrue(ms.AddNode(user, ft.InternalModules, "nonVar",
                    typeof(IgnoreResult<int>), Rectangle.Hidden, out var node, out error), error?.Message);

                Assert.IsFalse(ms.AddFunctionTemplateVariable(user, ft, node, out error));
                Assert.IsNotNull(error, "Should have an error for incompatible type.");
                Assert.IsEmpty(ft.LocalVariables);
            });
    }

    [TestMethod]
    public void AddFunctionTemplateVariable_AlreadyAdded_Fails()
    {
        TestHelper.RunInModelSystemContext(nameof(AddFunctionTemplateVariable_AlreadyAdded_Fails),
            (user, _, ms) =>
            {
                CommandError error = null;
                Assert.IsTrue(ms.AddFunctionTemplate(user, ms.ModelSystem.GlobalBoundary, "MyFT",
                    out var ft, out error), error?.Message);

                var node = CreateLocalVar<int>(ms, user, ft.InternalModules, "myVar", "1");
                Assert.IsTrue(ms.AddFunctionTemplateVariable(user, ft, node, out error), error?.Message);

                // Adding the same node a second time should fail.
                Assert.IsFalse(ms.AddFunctionTemplateVariable(user, ft, node, out error));
                Assert.IsNotNull(error);
                Assert.HasCount(1, ft.LocalVariables);
            });
    }

    // ── FunctionParameter as local variable ───────────────────────────────

    [TestMethod]
    public void FunctionParameter_IFunctionOfBasicType_CanBeLocalVariable()
    {
        TestHelper.RunInModelSystemContext(nameof(FunctionParameter_IFunctionOfBasicType_CanBeLocalVariable),
            (user, _, ms) =>
            {
                CommandError error = null;
                Assert.IsTrue(ms.AddFunctionTemplate(user, ms.ModelSystem.GlobalBoundary, "MyFT",
                    out var ft, out error), error?.Message);

                // FunctionParameter of type IFunction<int> → eligible as local variable.
                Assert.IsTrue(ms.AddFunctionParameter(user, ft, "myIntParam",
                    typeof(IFunction<int>), Rectangle.Hidden, out var fp, out error), error?.Message);

                Assert.IsTrue(ms.AddFunctionTemplateVariable(user, ft, fp, out error), error?.Message);
                Assert.HasCount(1, ft.LocalVariables);
                Assert.AreSame(fp, ft.LocalVariables[0]);
            });
    }

    [TestMethod]
    public void FunctionParameter_NonBasicType_CannotBeLocalVariable()
    {
        TestHelper.RunInModelSystemContext(nameof(FunctionParameter_NonBasicType_CannotBeLocalVariable),
            (user, _, ms) =>
            {
                CommandError error = null;
                Assert.IsTrue(ms.AddFunctionTemplate(user, ms.ModelSystem.GlobalBoundary, "MyFT",
                    out var ft, out error), error?.Message);

                // FunctionParameter of type IModule → NOT eligible as local variable.
                Assert.IsTrue(ms.AddFunctionParameter(user, ft, "myModParam",
                    typeof(IModule), Rectangle.Hidden, out var fp, out error), error?.Message);

                Assert.IsFalse(ms.AddFunctionTemplateVariable(user, ft, fp, out error));
                Assert.IsNotNull(error);
                Assert.IsEmpty(ft.LocalVariables);
            });
    }

    // ── Scoping: local before global ──────────────────────────────────────

    [TestMethod]
    public void LocalVariable_ResolvedBeforeGlobalVariable()
    {
        TestHelper.RunInModelSystemContext(nameof(LocalVariable_ResolvedBeforeGlobalVariable),
            (user, _, ms) =>
            {
                CommandError error = null;
                Assert.IsTrue(ms.AddFunctionTemplate(user, ms.ModelSystem.GlobalBoundary, "MyFT",
                    out var ft, out error), error?.Message);

                // Template-local variable named "shared" = 42.
                var localVar = CreateLocalVar<int>(ms, user, ft.InternalModules, "shared", "42");
                Assert.IsTrue(ms.AddFunctionTemplateVariable(user, ft, localVar, out error), error?.Message);

                // Global variable named "shared" = 999.
                var globalVar = CreateLocalVar<int>(ms, user, ms.ModelSystem.GlobalBoundary, "shared", "999");
                Assert.IsTrue(ms.AddVariable(user, globalVar, out error), error?.Message);

                // Compile an expression inside InternalModules that references "shared".
                // The local variable (42) should win over the global (999).
                var allVars = new System.Collections.Generic.List<Node>();
                allVars.AddRange(ft.LocalVariables);
                allVars.AddRange(ms.ModelSystem.Variables);

                string compileError = null;
                Assert.IsTrue(ParameterCompiler.CreateExpression(allVars, "shared", out var expr, ref compileError),
                    compileError);
                Assert.IsTrue(ParameterCompiler.Evaluate(null!, expr, out var result, ref compileError),
                    compileError);
                Assert.AreEqual(42, result, "Local variable should shadow the global variable.");
            });
    }

    [TestMethod]
    public void LocalVariable_NotVisibleOutsideTemplate()
    {
        TestHelper.RunInModelSystemContext(nameof(LocalVariable_NotVisibleOutsideTemplate),
            (user, _, ms) =>
            {
                CommandError error = null;
                Assert.IsTrue(ms.AddFunctionTemplate(user, ms.ModelSystem.GlobalBoundary, "MyFT",
                    out var ft, out error), error?.Message);

                // Template-local variable named "secret" inside InternalModules.
                var localVar = CreateLocalVar<int>(ms, user, ft.InternalModules, "secret", "42");
                Assert.IsTrue(ms.AddFunctionTemplateVariable(user, ft, localVar, out error), error?.Message);

                // Compiling "secret" against only global variables (no local vars) should fail.
                string compileError = null;
                Assert.IsFalse(ParameterCompiler.CreateExpression(
                    ms.ModelSystem.Variables, "secret", out var discardExpr, ref compileError),
                    "Local variable should not be visible outside its template.");
            });
    }

    // ── OwningFunctionTemplate back-reference ─────────────────────────────

    [TestMethod]
    public void InternalModules_OwningFunctionTemplate_IsSet()
    {
        TestHelper.RunInModelSystemContext(nameof(InternalModules_OwningFunctionTemplate_IsSet),
            (user, _, ms) =>
            {
                CommandError error = null;
                Assert.IsTrue(ms.AddFunctionTemplate(user, ms.ModelSystem.GlobalBoundary, "MyFT",
                    out var ft, out error), error?.Message);

                Assert.IsNotNull(ft.InternalModules.OwningFunctionTemplate,
                    "InternalModules should have a back-reference to its owning FunctionTemplate.");
                Assert.AreSame(ft, ft.InternalModules.OwningFunctionTemplate);
            });
    }

    [TestMethod]
    public void GlobalBoundary_OwningFunctionTemplate_IsNull()
    {
        TestHelper.RunInModelSystemContext(nameof(GlobalBoundary_OwningFunctionTemplate_IsNull),
            (user, _, ms) =>
            {
                Assert.IsNull(ms.ModelSystem.GlobalBoundary.OwningFunctionTemplate,
                    "Global boundary should have no owning function template.");
            });
    }
}
