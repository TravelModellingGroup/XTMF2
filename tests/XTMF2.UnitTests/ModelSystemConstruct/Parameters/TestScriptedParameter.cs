using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
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
}
