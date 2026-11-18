using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using XTMF2.Editing;
using XTMF2.ModelSystemConstruct;
using XTMF2.ModelSystemConstruct.Parameters.Compiler;
using XTMF2.RuntimeModules;

namespace XTMF2.UnitTests.ModelSystemConstruct.Parameters;

[TestClass]
public class TestScriptedParameter
{
    [TestMethod]
    public void ScriptedParameter_StringLiteral_RoundTripsRepresentation()
    {
        TestHelper.RunInModelSystemContext(nameof(ScriptedParameter_StringLiteral_RoundTripsRepresentation),
            (user, _, ms) =>
            {
                CommandError error = null;
                Assert.IsTrue(ms.AddNode(user, ms.ModelSystem.GlobalBoundary, "sp",
                    typeof(ScriptedParameter<string>), Rectangle.Hidden, out var scriptedNode, out error), error?.Message);

                // Set the scripted parameter to a regular quoted string literal
                Assert.IsTrue(ms.SetParameterExpression(user, scriptedNode, "\"Hello World\"", out error), error?.Message);
                Assert.IsNotNull(scriptedNode.ParameterValue, "ScriptedParameter should have a ParameterValue after SetParameterExpression.");

                // The representation should be parseable by CreateExpression (round-trip)
                var allVars = ms.ModelSystem.Variables as IList<Node>;
                string compileError = null;
                Assert.IsTrue(ParameterCompiler.CreateExpression(allVars, scriptedNode.ParameterValue.Representation, out var expr, ref compileError),
                    $"Representation was not parseable: {compileError}");
            });
    }

    [TestMethod]
    public void ModelSystemLoad_InvalidScriptedParameterExpression_ReturnsError()
    {
        TestHelper.RunInModelSystemContext(nameof(ModelSystemLoad_InvalidScriptedParameterExpression_ReturnsError),
            (user, _, ms) =>
            {
                Assert.IsTrue(ms.AddNode(user, ms.ModelSystem.GlobalBoundary, "scripted",
                    typeof(ScriptedParameter<int>), new Rectangle(10, 10), out var scriptedNode, out var commandError),
                    commandError?.Message);
                Assert.IsTrue(ms.SetParameterExpression(user, scriptedNode, "1", out commandError),
                    commandError?.Message);

                string saveError = null;
                using var savedModel = new MemoryStream();
                Assert.IsTrue(ms.ModelSystem.Save(ref saveError, savedModel), saveError);
                var invalidModel = Encoding.UTF8.GetString(savedModel.ToArray()).Replace(
                    "\"ParameterExpression\":\"1\"",
                    "\"ParameterExpression\":\"missingVariable\"",
                    StringComparison.Ordinal);
                Assert.AreNotEqual(Encoding.UTF8.GetString(savedModel.ToArray()), invalidModel);

                string loadError = null;
                var loadedSuccessfully = ModelSystem.Load(invalidModel, TestHelper.CreateRuntime(),
                    out var loadedModel, ref loadError);

                Assert.IsFalse(loadedSuccessfully);
                Assert.IsNull(loadedModel);
                StringAssert.Contains(loadError, "scripted");
                StringAssert.Contains(loadError, "missingVariable");
            });
    }

    [TestMethod]
    public void ModelSystemLoad_RemovesUnconnectedHiddenNodesAndFunctionInstances()
    {
        TestHelper.RunInModelSystemContext(nameof(ModelSystemLoad_RemovesUnconnectedHiddenNodesAndFunctionInstances),
            (user, _, ms) =>
            {
                Assert.IsTrue(ms.AddFunctionTemplate(user, ms.ModelSystem.GlobalBoundary, "Template",
                    out var template, out var commandError), commandError?.Message);
                Assert.IsTrue(ms.AddNode(user, template.InternalModules, "entry", typeof(BasicParameter<int>),
                    Rectangle.Hidden, out var entryNode, out commandError), commandError?.Message);
                Assert.IsTrue(ms.SetFunctionTemplateEntryNode(user, template, entryNode, out commandError),
                    commandError?.Message);
                Assert.IsTrue(ms.AddNode(user, template.InternalModules, "local", typeof(BasicParameter<int>),
                    Rectangle.Hidden, out var localNode, out commandError), commandError?.Message);
                Assert.IsTrue(ms.AddFunctionTemplateVariable(user, template, localNode, out commandError),
                    commandError?.Message);
                Assert.IsTrue(ms.AddNode(user, template.InternalModules, "orphan",
                    typeof(ScriptedParameter<int>), Rectangle.Hidden, out var orphanNode, out commandError),
                    commandError?.Message);
                Assert.IsTrue(ms.AddFunctionInstance(user, ms.ModelSystem.GlobalBoundary, template,
                    "orphanInstance", Rectangle.Hidden, out var orphanInstance, out commandError), commandError?.Message);

                string saveError = null;
                using var savedModel = new MemoryStream();
                Assert.IsTrue(ms.ModelSystem.Save(ref saveError, savedModel), saveError);
                string loadError = null;
                var loadedSuccessfully = ModelSystem.Load(Encoding.UTF8.GetString(savedModel.ToArray()),
                    TestHelper.CreateRuntime(), out var loadedModel, ref loadError);

                Assert.IsTrue(loadedSuccessfully, loadError);
                Assert.IsNotNull(loadedModel);
                Assert.IsEmpty(loadedModel.GlobalBoundary.FunctionInstances);
                var loadedTemplate = loadedModel.GlobalBoundary.FunctionTemplates.Single();
                Assert.AreEqual("entry", loadedTemplate.EntryNode?.Name);
                Assert.AreEqual("local", loadedTemplate.LocalVariables.Single().Name);
                Assert.IsFalse(loadedTemplate.InternalModules.Modules.Any(node => node.Name == "orphan"));
            });
    }
}
