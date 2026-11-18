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
using System.IO;
using System.Linq;
using System.Threading;
using XTMF2.Editing;
using XTMF2.ModelSystemConstruct;
using XTMF2.ModelSystemConstruct.Parameters.Compiler;
using XTMF2.RuntimeModules;
using XTMF2.UnitTests.Modules;

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

    [TestMethod]
    public void ConvertBasicParameterToFunctionParameter_PreservesLocalVariableAndUndoRedo()
    {
        TestHelper.RunInModelSystemContext(
            nameof(ConvertBasicParameterToFunctionParameter_PreservesLocalVariableAndUndoRedo),
            (user, _, ms) =>
            {
                CommandError error = null;
                var modelSystem = ms.ModelSystem;
                Assert.IsTrue(ms.AddFunctionTemplate(user, modelSystem.GlobalBoundary, "LocalParameterFT",
                    out var template, out error), error?.Message);
                Assert.IsTrue(ms.AddNode(user, template.InternalModules, "Value",
                    typeof(BasicParameter<string>), Rectangle.Hidden, out var source, out error), error?.Message);
                Assert.IsTrue(ms.SetParameterValue(user, source, "original", out error), error?.Message);
                Assert.IsTrue(ms.AddFunctionTemplateVariable(user, template, source!, out error), error?.Message);

                var instances = new List<FunctionInstance>();
                foreach (var name in new[] { "FI1", "FI2" })
                {
                    Assert.IsTrue(ms.AddFunctionInstance(user, modelSystem.GlobalBoundary, template, name,
                        Rectangle.Hidden, out var instance, out error), error?.Message);
                    instances.Add(instance!);
                }

                Assert.IsTrue(ms.ConvertParameterToFunctionParameter(user, source!,
                    out var parameter, out error), error?.Message);
                Assert.IsNotNull(parameter);
                Assert.AreEqual(typeof(IFunction<string>), parameter.Type);
                Assert.Contains(parameter, template.LocalVariables);
                Assert.DoesNotContain(source!, template.InternalModules.Modules);

                var providers = instances.Select(instance =>
                {
                    var link = modelSystem.GlobalBoundary.Links.Single(candidate =>
                        candidate.Origin == instance
                        && candidate.OriginHook is FunctionParameterHook hook
                        && ReferenceEquals(hook.Parameter, parameter));
                    Assert.IsInstanceOfType<SingleLink>(link);
                    return ((SingleLink)link).Destination;
                }).ToList();
                Assert.HasCount(2, providers);
                Assert.IsTrue(providers.All(provider =>
                    provider.ParameterValue?.Representation == "original"));

                Assert.IsTrue(ms.Undo(user, out error), error?.Message);
                Assert.Contains(source!, template.InternalModules.Modules);
                Assert.DoesNotContain(parameter, template.FunctionParameters);
                Assert.Contains(source!, template.LocalVariables);
                Assert.IsEmpty(modelSystem.GlobalBoundary.Modules);

                Assert.IsTrue(ms.Redo(user, out error), error?.Message);
                Assert.Contains(parameter, template.LocalVariables);
                Assert.HasCount(2, modelSystem.GlobalBoundary.Modules);
                Assert.IsTrue(modelSystem.GlobalBoundary.Modules.All(provider =>
                    provider.ParameterValue?.Representation == "original"));
            });
    }

    [TestMethod]
    public void ConvertMultipleParametersToFunctionParameters_IsOneUndoRedoOperation()
    {
        TestHelper.RunInModelSystemContext(
            nameof(ConvertMultipleParametersToFunctionParameters_IsOneUndoRedoOperation),
            (user, _, ms) =>
            {
                CommandError error = null;
                var modelSystem = ms.ModelSystem;
                var global = modelSystem.GlobalBoundary;
                Assert.IsTrue(ms.AddFunctionTemplate(user, global, "BulkParameterFT",
                    out var template, out error), error?.Message);
                Assert.IsTrue(ms.AddNode(user, template.InternalModules, "BasicValue",
                    typeof(BasicParameter<string>), Rectangle.Hidden, out var basicSource, out error),
                    error?.Message);
                Assert.IsTrue(ms.SetParameterValue(user, basicSource, "original", out error), error?.Message);
                Assert.IsTrue(ms.AddFunctionTemplateVariable(user, template, basicSource!, out error),
                    error?.Message);
                Assert.IsTrue(ms.AddNode(user, template.InternalModules, "ScriptedValue",
                    typeof(ScriptedParameter<int>), Rectangle.Hidden, out var scriptedSource, out error),
                    error?.Message);
                Assert.IsTrue(ms.SetParameterExpression(user, scriptedSource, "1", out error), error?.Message);

                var instances = new List<FunctionInstance>();
                foreach (var name in new[] { "FI1", "FI2" })
                {
                    Assert.IsTrue(ms.AddFunctionInstance(user, global, template, name,
                        Rectangle.Hidden, out var instance, out error), error?.Message);
                    instances.Add(instance!);
                }

                Assert.IsTrue(ms.ConvertParametersToFunctionParameters(
                    user, new[] { basicSource!, scriptedSource! }, out error), error?.Message);

                var basicParameter = template.FunctionParameters.Single(parameter => parameter.Name == "BasicValue");
                var scriptedParameter = template.FunctionParameters.Single(parameter => parameter.Name == "ScriptedValue");
                Assert.AreEqual(typeof(IFunction<string>), basicParameter.Type);
                Assert.AreEqual(typeof(IFunction<int>), scriptedParameter.Type);
                Assert.Contains(basicParameter, template.LocalVariables);
                Assert.DoesNotContain(scriptedParameter, template.LocalVariables);
                Assert.HasCount(4, global.Modules);

                foreach (var instance in instances)
                {
                    Node GetProvider(FunctionParameter parameter)
                    {
                        var link = global.Links.Single(candidate =>
                            ReferenceEquals(candidate.Origin, instance)
                            && candidate.OriginHook is FunctionParameterHook hook
                            && ReferenceEquals(hook.Parameter, parameter));
                        Assert.IsInstanceOfType<SingleLink>(link);
                        return ((SingleLink)link).Destination;
                    }

                    var basicProvider = GetProvider(basicParameter);
                    var scriptedProvider = GetProvider(scriptedParameter);
                    Assert.AreEqual(typeof(BasicParameter<string>), basicProvider.Type);
                    Assert.AreEqual("original", basicProvider.ParameterValue?.Representation);
                    Assert.AreEqual(typeof(ScriptedParameter<int>), scriptedProvider.Type);
                    Assert.AreEqual("1", scriptedProvider.ParameterValue?.Representation);
                }

                Assert.IsTrue(ms.Undo(user, out error), error?.Message);
                Assert.IsEmpty(template.FunctionParameters);
                Assert.Contains(basicSource!, template.InternalModules.Modules);
                Assert.Contains(scriptedSource!, template.InternalModules.Modules);
                Assert.Contains(basicSource!, template.LocalVariables);
                Assert.IsEmpty(global.Modules);

                Assert.IsTrue(ms.Redo(user, out error), error?.Message);
                Assert.HasCount(2, template.FunctionParameters);
                Assert.Contains(basicParameter, template.LocalVariables);
                Assert.HasCount(4, global.Modules);
            });
    }

    [TestMethod]
    public void ConvertMultipleParametersToFunctionParameters_RollsBackIfAnyNodeIsInvalid()
    {
        TestHelper.RunInModelSystemContext(
            nameof(ConvertMultipleParametersToFunctionParameters_RollsBackIfAnyNodeIsInvalid),
            (user, _, ms) =>
            {
                CommandError error = null;
                Assert.IsTrue(ms.AddFunctionTemplate(user, ms.ModelSystem.GlobalBoundary, "BulkFailureFT",
                    out var template, out error), error?.Message);
                Assert.IsTrue(ms.AddNode(user, template.InternalModules, "ValidValue",
                    typeof(BasicParameter<string>), Rectangle.Hidden, out var validSource, out error),
                    error?.Message);
                Assert.IsTrue(ms.SetParameterValue(user, validSource, "original", out error), error?.Message);
                Assert.IsTrue(ms.AddNode(user, template.InternalModules, "NotAParameter",
                    typeof(SimpleParameterModule), Rectangle.Hidden, out var invalidSource, out error),
                    error?.Message);

                Assert.IsFalse(ms.ConvertParametersToFunctionParameters(
                    user, new[] { validSource!, invalidSource! }, out error));
                Assert.IsNotNull(error);
                Assert.IsEmpty(template.FunctionParameters);
                Assert.Contains(validSource!, template.InternalModules.Modules);
                Assert.Contains(invalidSource!, template.InternalModules.Modules);
                Assert.IsEmpty(template.LocalVariables);
            });
    }

    [TestMethod]
    public void ConvertScriptedParameterToFunctionParameter_PreservesExpressionAtRuntime()
    {
        TestHelper.RunInModelSystemContext(
            nameof(ConvertScriptedParameterToFunctionParameter_PreservesExpressionAtRuntime),
            (user, projectSession, ms) =>
            {
                CommandError error = null;
                var modelSystem = ms.ModelSystem;
                var global = modelSystem.GlobalBoundary;
                Assert.IsTrue(ms.AddFunctionTemplate(user, global, "ScriptedLocalParameterFT",
                    out var template, out error), error?.Message);
                Assert.IsTrue(ms.AddFunctionParameter(user, template, "Input", typeof(IFunction<string>),
                    Rectangle.Hidden, out var input, out error), error?.Message);
                Assert.IsTrue(ms.AddFunctionTemplateVariable(user, template, input!, out error), error?.Message);
                Assert.IsTrue(ms.AddNode(user, template.InternalModules, "Converted",
                    typeof(ScriptedParameter<string>), Rectangle.Hidden, out var source, out error),
                    error?.Message);
                Assert.IsTrue(ms.SetParameterExpression(user, source, "Input", out error), error?.Message);
                Assert.IsTrue(ms.SetFunctionTemplateEntryNode(user, template, source!, out error), error?.Message);

                var instances = new List<FunctionInstance>();
                foreach (var (name, value) in new[] { ("FI1", "first"), ("FI2", "second") })
                {
                    Assert.IsTrue(ms.AddFunctionInstance(user, global, template, name,
                        Rectangle.Hidden, out var instance, out error), error?.Message);
                    Assert.IsTrue(ms.AddNode(user, global, $"{name}Input",
                        typeof(BasicParameter<string>), Rectangle.Hidden, out var provider, out error),
                        error?.Message);
                    Assert.IsTrue(ms.SetParameterValue(user, provider, value, out error), error?.Message);
                    var inputHook = instance!.Hooks.OfType<FunctionParameterHook>()
                        .Single(hook => ReferenceEquals(hook.Parameter, input));
                    Assert.IsTrue(ms.AddLink(user, instance, inputHook, provider!,
                        out _, out error), error?.Message);
                    instances.Add(instance);
                }

                Assert.IsTrue(ms.ConvertParameterToFunctionParameter(user, source!,
                    out var convertedParameter, out error), error?.Message);
                Assert.IsNotNull(convertedParameter);
                Assert.AreEqual(typeof(IFunction<string>), convertedParameter.Type);
                Assert.DoesNotContain(convertedParameter, template.LocalVariables);
                var convertedProviders = instances.Select(instance =>
                {
                    var link = global.Links.Single(candidate =>
                        candidate.Origin == instance
                        && candidate.OriginHook is FunctionParameterHook hook
                        && ReferenceEquals(hook.Parameter, convertedParameter));
                    Assert.IsInstanceOfType<SingleLink>(link);
                    return ((SingleLink)link).Destination;
                }).ToList();
                Assert.HasCount(2, convertedProviders);
                Assert.IsTrue(convertedProviders.All(provider =>
                    provider.ParameterValue?.Representation == "Input"));

                Assert.AreSame(convertedParameter, template.EntryNode,
                    "Converting the entry parameter should preserve the template's output designation.");
                Assert.IsTrue(ms.AddModelSystemStart(user, global, "Start",
                    Rectangle.Hidden, out var start, out error), error?.Message);
                Assert.IsTrue(ms.AddNode(user, global, "Ignore",
                    typeof(IgnoreResult<string>), Rectangle.Hidden, out var ignore, out error), error?.Message);
                Assert.IsTrue(ms.AddNode(user, global, "SimpleParameter",
                    typeof(SimpleParameterModule), Rectangle.Hidden, out var simpleParameter, out error),
                    error?.Message);
                Assert.IsTrue(ms.AddLink(user, start, start!.Hooks[0], ignore!,
                    out _, out error), error?.Message);
                Assert.IsTrue(ms.AddLink(user, ignore!, ignore!.Hooks[0], simpleParameter!,
                    out _, out error), error?.Message);
                Assert.IsTrue(ms.AddLink(user, simpleParameter!, simpleParameter!.Hooks[0], instances[0],
                    out _, out error), error?.Message);

                TestHelper.CreateRunClient(true, runBus =>
                {
                    CommandError runError = null;
                    bool success = false;
                    using var semaphore = new SemaphoreSlim(0);
                    runBus.ClientFinishedModelSystem += (_, _) =>
                    {
                        success = true;
                        semaphore.Release();
                    };
                    runBus.ClientErrorWhenRunningModelSystem += (_, _, message, stack, _, _) =>
                    {
                        runError = new CommandError(message + "\r\n" + stack);
                        semaphore.Release();
                    };
                    Assert.IsTrue(ms.Save(out error), error?.Message);
                    Assert.IsTrue(runBus.RunModelSystem(ms,
                        Path.Combine(projectSession.RunsDirectory, "ScriptedLocalParameterRuntime"),
                        "Start", out _, out runError), runError?.Message);
                    Assert.IsTrue(semaphore.Wait(TestHelper.TestTimeoutMilliseconds),
                        "Model system did not complete in time.");
                    Assert.IsTrue(success, "Model system failed: " + runError?.Message);
                });
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

    // ── ScriptedParameter references LocalVariable ────────────────────────

    [TestMethod]
    public void ScriptedParameter_CanReference_LocalVariable()
    {
        TestHelper.RunInModelSystemContext(nameof(ScriptedParameter_CanReference_LocalVariable),
            (user, _, ms) =>
            {
                CommandError error = null;
                Assert.IsTrue(ms.AddFunctionTemplate(user, ms.ModelSystem.GlobalBoundary, "MyFT",
                    out var ft, out error), error?.Message);

                // Create BasicParameter<int> "myVar" = 42 inside InternalModules, add as local var.
                var localVar = CreateLocalVar<int>(ms, user, ft.InternalModules, "myVar", "42");
                Assert.IsTrue(ms.AddFunctionTemplateVariable(user, ft, localVar, out error), error?.Message);

                // Create a ScriptedParameter<int> inside InternalModules.
                Assert.IsTrue(ms.AddNode(user, ft.InternalModules, "result",
                    typeof(ScriptedParameter<int>), Rectangle.Hidden, out var scriptedNode, out error),
                    error?.Message);

                // The scripted parameter should be able to reference the local variable by name.
                Assert.IsTrue(ms.SetParameterExpression(user, scriptedNode, "myVar", out error),
                    error?.Message);
                Assert.IsNotNull(scriptedNode.ParameterValue,
                    "ScriptedParameter should have a ParameterValue after SetParameterExpression.");
            });
    }

    [TestMethod]
    public void ScriptedParameter_CanReference_FunctionParameterLocalVariable()
    {
        TestHelper.RunInModelSystemContext(nameof(ScriptedParameter_CanReference_FunctionParameterLocalVariable),
            (user, _, ms) =>
            {
                CommandError error = null;
                Assert.IsTrue(ms.AddFunctionTemplate(user, ms.ModelSystem.GlobalBoundary, "MyFT",
                    out var ft, out error), error?.Message);

                // Add a FunctionParameter of type IFunction<int> and mark it as a local variable.
                Assert.IsTrue(ms.AddFunctionParameter(user, ft, "myFP", typeof(IFunction<int>),
                    Rectangle.Hidden, out var fp, out error), error?.Message);
                Assert.IsTrue(ms.AddFunctionTemplateVariable(user, ft, fp, out error), error?.Message);

                // Create a ScriptedParameter<int> inside InternalModules.
                Assert.IsTrue(ms.AddNode(user, ft.InternalModules, "result",
                    typeof(ScriptedParameter<int>), Rectangle.Hidden, out var scriptedNode, out error),
                    error?.Message);

                // The ScriptedParameter should be able to reference the FunctionParameter local var.
                Assert.IsTrue(ms.SetParameterExpression(user, scriptedNode, "myFP", out error),
                    error?.Message);
                Assert.IsNotNull(scriptedNode.ParameterValue);
            });
    }

    [TestMethod]
    public void ScriptedParameter_CanReference_LocalVariable_WithoutPriorValue()
    {
        // Regression test: before the fix, IsValidLocalVariableNode returned false and
        // Variable.CreateVariableForNode threw CompilerException when the backing node had
        // ParameterValue == null (a freshly-created BasicParameter<int> with no value ever set).
        TestHelper.RunInModelSystemContext(nameof(ScriptedParameter_CanReference_LocalVariable_WithoutPriorValue),
            (user, _, ms) =>
            {
                CommandError error = null;
                Assert.IsTrue(ms.AddFunctionTemplate(user, ms.ModelSystem.GlobalBoundary, "MyFT",
                    out var ft, out error), error?.Message);

                // Create a BasicParameter<int> but deliberately do NOT set a value → ParameterValue is null.
                Assert.IsTrue(ms.AddNode(user, ft.InternalModules, "unsetVar", typeof(BasicParameter<int>),
                    Rectangle.Hidden, out var unsetNode, out error), error?.Message);
                Assert.IsNull(unsetNode!.ParameterValue, "Pre-condition: no value set yet.");

                // Should still be eligible as a local variable (type is determinable from node.Type).
                Assert.IsTrue(ms.AddFunctionTemplateVariable(user, ft, unsetNode, out error),
                    error?.Message);

                // A ScriptedParameter inside InternalModules should be able to reference it by name.
                Assert.IsTrue(ms.AddNode(user, ft.InternalModules, "result", typeof(ScriptedParameter<int>),
                    Rectangle.Hidden, out var scriptedNode, out error), error?.Message);
                Assert.IsTrue(ms.SetParameterExpression(user, scriptedNode, "unsetVar", out error),
                    error?.Message);
                Assert.IsNotNull(scriptedNode.ParameterValue);
            });
    }

    [TestMethod]
    public void LocalVariable_FunctionParameter_CanBeReferenced_AtRuntime()
    {
        // Regression / feature test: a FunctionParameter that is also a local variable must be
        // correctly resolved at runtime through FunctionInstance.Current.GetBoundModule().
        TestHelper.RunInModelSystemContext(nameof(LocalVariable_FunctionParameter_CanBeReferenced_AtRuntime),
            (user, pSession, ms) =>
            {
                CommandError error = null;
                var msys = ms.ModelSystem;

                // ── Build the FunctionTemplate ─────────────────────────────────────
                Assert.IsTrue(ms.AddFunctionTemplate(user, msys.GlobalBoundary, "FPLocalVarFT",
                    out var ft, out error), error?.Message);

                // FunctionParameter "myFP" of type IFunction<string> → local variable
                Assert.IsTrue(ms.AddFunctionParameter(user, ft, "myFP", typeof(IFunction<string>),
                    Rectangle.Hidden, out var fp, out error), error?.Message);
                Assert.IsTrue(ms.AddFunctionTemplateVariable(user, ft, fp, out error), error?.Message);

                // ScriptedParameter<string> "result" referencing "myFP"
                Assert.IsTrue(ms.AddNode(user, ft.InternalModules, "result",
                    typeof(ScriptedParameter<string>), Rectangle.Hidden, out var resultNode, out error),
                    error?.Message);
                Assert.IsTrue(ms.SetParameterExpression(user, resultNode, "myFP", out error),
                    error?.Message);
                Assert.IsTrue(ms.SetFunctionTemplateEntryNode(user, ft, resultNode, out error), error?.Message);

                // ── GlobalBoundary: Start → ignore → SPM → FI ─────────────────────
                Assert.IsTrue(ms.AddFunctionInstance(user, msys.GlobalBoundary, ft, "fi",
                    Rectangle.Hidden, out var fi, out error), error?.Message);

                // Wire FI's "myFP" hook to a BasicParameter<string> externally
                Assert.IsTrue(ms.AddNode(user, msys.GlobalBoundary, "fpSource",
                    typeof(BasicParameter<string>), Rectangle.Hidden, out var fpSource, out error), error?.Message);
                Assert.IsTrue(ms.SetParameterValue(user, fpSource, "FP bound value", out error), error?.Message);

                Assert.IsTrue(ms.AddModelSystemStart(user, msys.GlobalBoundary, "Start",
                    Rectangle.Hidden, out var start, out error), error?.Message);
                Assert.IsTrue(ms.AddNode(user, msys.GlobalBoundary, "AnIgnore",
                    typeof(IgnoreResult<string>), Rectangle.Hidden, out var ignore, out error), error?.Message);
                Assert.IsTrue(ms.AddNode(user, msys.GlobalBoundary, "SPM",
                    typeof(SimpleParameterModule), Rectangle.Hidden, out var spm, out error), error?.Message);

                Assert.IsTrue(ms.AddLink(user, start, start.Hooks[0], ignore, out _, out error), error?.Message);
                Assert.IsTrue(ms.AddLink(user, ignore, ignore.Hooks[0], spm, out _, out error), error?.Message);
                Assert.IsTrue(ms.AddLink(user, spm, spm.Hooks[0], fi, out _, out error), error?.Message);
                // Bind FI's myFP hook → fpSource
                var fpHook = fi.Hooks.First(h => h.Name == "myFP");
                Assert.IsTrue(ms.AddLink(user, fi, fpHook, fpSource, out _, out error), error?.Message);

                // ── Run ───────────────────────────────────────────────────────────
                TestHelper.CreateRunClient(true, (runBus) =>
                {
                    CommandError runError = null;
                    bool success = false;
                    using var sem = new SemaphoreSlim(0);
                    runBus.ClientFinishedModelSystem += (_, _) => { success = true; sem.Release(); };
                    runBus.ClientErrorWhenRunningModelSystem += (_, _, e, stack, moduleName, elementId) =>
                    {
                        runError = new CommandError(e + "\r\n" + stack);
                        sem.Release();
                    };
                    Assert.IsTrue(runBus.RunModelSystem(ms,
                        Path.Combine(pSession.RunsDirectory, "FPLocalVarRuntime"),
                        "Start", out _, out runError), runError?.Message);
                    Assert.IsTrue(sem.Wait(TestHelper.TestTimeoutMilliseconds), "Model system did not complete in time!");
                    Assert.IsTrue(success, "Model system failed: " + runError?.Message);
                });
            });
    }

    [TestMethod]
    public void LocalVariable_SetableParameter_CanBeReferenced_AtRuntime()
    {
        // Feature test: a SetableParameter<T> local variable must be resolved via the
        // per-instance cloned module (FunctionInstance.Current.GetRuntimeModule), not
        // the shared template-node Module (which is null inside InternalModules).
        TestHelper.RunInModelSystemContext(nameof(LocalVariable_SetableParameter_CanBeReferenced_AtRuntime),
            (user, pSession, ms) =>
            {
                CommandError error = null;
                var msys = ms.ModelSystem;

                // ── FunctionTemplate ───────────────────────────────────────────────
                Assert.IsTrue(ms.AddFunctionTemplate(user, msys.GlobalBoundary, "SetableFT",
                    out var ft, out error), error?.Message);

                // SetableParameter<string> "liveVar" = "Setable" → local variable
                Assert.IsTrue(ms.AddNode(user, ft.InternalModules, "liveVar",
                    typeof(SetableParameter<string>), Rectangle.Hidden, out var liveVarNode, out error),
                    error?.Message);
                Assert.IsTrue(ms.SetParameterValue(user, liveVarNode, "Setable value", out error), error?.Message);
                Assert.IsTrue(ms.AddFunctionTemplateVariable(user, ft, liveVarNode, out error), error?.Message);

                // ScriptedParameter<string> "result" → expression = "liveVar"
                Assert.IsTrue(ms.AddNode(user, ft.InternalModules, "result",
                    typeof(ScriptedParameter<string>), Rectangle.Hidden, out var resultNode, out error),
                    error?.Message);
                Assert.IsTrue(ms.SetParameterExpression(user, resultNode, "liveVar", out error), error?.Message);
                Assert.IsTrue(ms.SetFunctionTemplateEntryNode(user, ft, resultNode, out error), error?.Message);

                // ── GlobalBoundary: Start → ignore → SPM → FI ─────────────────────
                Assert.IsTrue(ms.AddFunctionInstance(user, msys.GlobalBoundary, ft, "fi",
                    Rectangle.Hidden, out var fi, out error), error?.Message);
                Assert.IsTrue(ms.AddModelSystemStart(user, msys.GlobalBoundary, "Start",
                    Rectangle.Hidden, out var start, out error), error?.Message);
                Assert.IsTrue(ms.AddNode(user, msys.GlobalBoundary, "AnIgnore",
                    typeof(IgnoreResult<string>), Rectangle.Hidden, out var ignore, out error), error?.Message);
                Assert.IsTrue(ms.AddNode(user, msys.GlobalBoundary, "SPM",
                    typeof(SimpleParameterModule), Rectangle.Hidden, out var spm, out error), error?.Message);

                Assert.IsTrue(ms.AddLink(user, start, start.Hooks[0], ignore, out _, out error), error?.Message);
                Assert.IsTrue(ms.AddLink(user, ignore, ignore.Hooks[0], spm, out _, out error), error?.Message);
                Assert.IsTrue(ms.AddLink(user, spm, spm.Hooks[0], fi, out _, out error), error?.Message);

                TestHelper.CreateRunClient(true, (runBus) =>
                {
                    CommandError runError = null;
                    bool success = false;
                    using var sem = new SemaphoreSlim(0);
                    runBus.ClientFinishedModelSystem += (_, _) => { success = true; sem.Release(); };
                    runBus.ClientErrorWhenRunningModelSystem += (_, _, e, stack, moduleName, elementId) =>
                    {
                        runError = new CommandError(e + "\r\n" + stack);
                        sem.Release();
                    };
                    Assert.IsTrue(runBus.RunModelSystem(ms,
                        Path.Combine(pSession.RunsDirectory, "SetableLocalVarRuntime"),
                        "Start", out _, out runError), runError?.Message);
                    Assert.IsTrue(sem.Wait(TestHelper.TestTimeoutMilliseconds), "Model system did not complete in time!");
                    Assert.IsTrue(success, "Model system failed: " + runError?.Message);
                });
            });
    }

    [TestMethod]
    public void LocalVariable_CanBeUsed_AtRuntime()
    {
        // Regression test: ScriptedParameter inside a FunctionTemplate should be able
        // to reference a local variable at runtime (not just at compile time).
        TestHelper.RunInModelSystemContext(nameof(LocalVariable_CanBeUsed_AtRuntime),
            (user, pSession, ms) =>
            {
                CommandError error = null;
                var msys = ms.ModelSystem;

                // ── Build the FunctionTemplate ─────────────────────────────────────
                Assert.IsTrue(ms.AddFunctionTemplate(user, msys.GlobalBoundary, "MyFT",
                    out var ft, out error), error?.Message);

                // BasicParameter<string> "helloParam" = "Hello" → register as local var
                Assert.IsTrue(ms.AddNode(user, ft.InternalModules, "helloParam",
                    typeof(BasicParameter<string>), Rectangle.Hidden, out var localVarNode, out error),
                    error?.Message);
                Assert.IsTrue(ms.SetParameterValue(user, localVarNode, "Hello", out error), error?.Message);
                Assert.IsTrue(ms.AddFunctionTemplateVariable(user, ft, localVarNode, out error), error?.Message);

                // ScriptedParameter<string> "result" → expression = "helloParam" → set as EntryNode
                Assert.IsTrue(ms.AddNode(user, ft.InternalModules, "result",
                    typeof(ScriptedParameter<string>), Rectangle.Hidden, out var resultNode, out error),
                    error?.Message);
                Assert.IsTrue(ms.SetParameterExpression(user, resultNode, "helloParam", out error),
                    error?.Message);
                Assert.IsTrue(ms.SetFunctionTemplateEntryNode(user, ft, resultNode, out error), error?.Message);

                // ── Build the execution chain in GlobalBoundary ────────────────────
                // FunctionInstance "fi" exposes ScriptedParameter<string> as IFunction<string>.
                Assert.IsTrue(ms.AddFunctionInstance(user, msys.GlobalBoundary, ft, "fi",
                    Rectangle.Hidden, out var fi, out error), error?.Message);

                Assert.IsTrue(ms.AddModelSystemStart(user, msys.GlobalBoundary, "Start",
                    Rectangle.Hidden, out var start, out error), error?.Message);
                Assert.IsTrue(ms.AddNode(user, msys.GlobalBoundary, "AnIgnore",
                    typeof(IgnoreResult<string>), Rectangle.Hidden, out var ignore, out error),
                    error?.Message);
                Assert.IsTrue(ms.AddNode(user, msys.GlobalBoundary, "SPM",
                    typeof(SimpleParameterModule), Rectangle.Hidden, out var spm, out error),
                    error?.Message);

                // Start → ignore → spm → fi
                Assert.IsTrue(ms.AddLink(user, start, start.Hooks[0], ignore, out _, out error), error?.Message);
                Assert.IsTrue(ms.AddLink(user, ignore, ignore.Hooks[0], spm, out _, out error), error?.Message);
                Assert.IsTrue(ms.AddLink(user, spm, spm.Hooks[0], fi, out _, out error), error?.Message);

                // ── Run through RunBus (exercises the full serialise/deserialise path) ─
                TestHelper.CreateRunClient(true, (runBus) =>
                {
                    CommandError runError = null;
                    bool success = false;
                    using var sem = new SemaphoreSlim(0);
                    runBus.ClientFinishedModelSystem += (_, _) => 
                    {
                        success = true; 
                        sem.Release(); 
                    };
                    runBus.ClientErrorWhenRunningModelSystem += (_, _, e, stack, moduleName, elementId) =>
                    {
                        runError = new CommandError(e + "\r\n" + stack);
                        sem.Release();
                    };
                    Assert.IsTrue(runBus.RunModelSystem(ms,
                        Path.Combine(pSession.RunsDirectory, "LocalVarRuntime"),
                        "Start", out _, out runError), runError?.Message);
                    Assert.IsTrue(sem.Wait(TestHelper.TestTimeoutMilliseconds), "Model system did not complete in time!");
                    Assert.IsTrue(success, "Model system failed: " + runError?.Message);
                });
            });
    }
}
