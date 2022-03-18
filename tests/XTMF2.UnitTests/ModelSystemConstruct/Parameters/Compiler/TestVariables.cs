/*
    Copyright 2017-2021 University of Toronto
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

using static XTMF2.UnitTests.ModelSystemConstruct.Parameters.Compiler.TestCompilerHelper;

namespace XTMF2.UnitTests.ModelSystemConstruct.Parameters.Compiler;

[TestClass]
public class TestVariables
{
    [TestMethod]
    public void TestBooleanVariable()
    {
        TestHelper.RunInModelSystemContext("TestBooleanVariable", (User user, ProjectSession project, ModelSystemSession session) =>
        {
            string error = null;
            var nodes = new List<Node>()
            {
                CreateNodeForVariable<bool>(session, user, "myBoolVariable", "true")
            };
            var text = "myBoolVariable";
            Assert.IsTrue(ParameterCompiler.CreateExpression(nodes, text, out var expression, ref error), $"Failed to compile {text}");
            Assert.IsNotNull(expression);
            Assert.AreEqual(typeof(bool), expression.Type);
            Assert.IsTrue(ParameterCompiler.Evaluate(null, expression, out var result, ref error), error);
            if (result is bool boolResult)
            {
                Assert.AreEqual(true, boolResult);
            }
            else
            {
                Assert.Fail("The result is not a boolean!");
            }
        });
    }

    [TestMethod]
    public void TestIntegerVariable()
    {
        TestHelper.RunInModelSystemContext("TestIntegerVariable", (User user, ProjectSession project, ModelSystemSession session) =>
        {
            string error = null;
            var nodes = new List<Node>()
            {
                CreateNodeForVariable<int>(session, user, "myIntVariable", "12345")
            };
            var text = "myIntVariable";
            Assert.IsTrue(ParameterCompiler.CreateExpression(nodes, text, out var expression, ref error), $"Failed to compile {text}");
            Assert.IsNotNull(expression);
            Assert.AreEqual(typeof(int), expression.Type);
            Assert.IsTrue(ParameterCompiler.Evaluate(null, expression, out var result, ref error), error);
            if (result is int intResult)
            {
                Assert.AreEqual(12345, intResult);
            }
            else
            {
                Assert.Fail("The result is not an integer!");
            }
        });
    }

    [TestMethod]
    public void TestFloatVariable()
    {
        TestHelper.RunInModelSystemContext("TestFloatVariable", (User user, ProjectSession project, ModelSystemSession session) =>
        {
            string error = null;
            var nodes = new List<Node>()
            {
                CreateNodeForVariable<float>(session, user, "myFloatVariable", "12345.6")
            };
            var text = "myFloatVariable";
            Assert.IsTrue(ParameterCompiler.CreateExpression(nodes, text, out var expression, ref error), $"Failed to compile {text}");
            Assert.IsNotNull(expression);
            Assert.AreEqual(typeof(float), expression.Type);
            Assert.IsTrue(ParameterCompiler.Evaluate(null, expression, out var result, ref error), error);
            if (result is float floatResult)
            {
                Assert.AreEqual(12345.6, floatResult, 0.001f);
            }
            else
            {
                Assert.Fail("The result is not an float!");
            }
        });
    }

    [TestMethod]
    public void TestStringVariable()
    {
        TestHelper.RunInModelSystemContext("TestStringVariable", (User user, ProjectSession project, ModelSystemSession session) =>
        {
            string error = null;
            var nodes = new List<Node>()
            {
                CreateNodeForVariable<string>(session, user, "myStringVariable", "12345.6")
            };
            var text = "myStringVariable";
            Assert.IsTrue(ParameterCompiler.CreateExpression(nodes, text, out var expression, ref error), $"Failed to compile {text}");
            Assert.IsNotNull(expression);
            Assert.AreEqual(typeof(string), expression.Type);
            Assert.IsTrue(ParameterCompiler.Evaluate(null, expression, out var result, ref error), error);
            if (result is string strResult)
            {
                Assert.AreEqual("12345.6", strResult);
            }
            else
            {
                Assert.Fail("The result is not a string!");
            }
        });
    }

    [TestMethod]
    public void TestWhitespaceAroundVariable()
    {
        TestHelper.RunInModelSystemContext("TestWhitespaceAroundVariable", (User user, ProjectSession project, ModelSystemSession session) =>
        {
            string error = null;
            var nodes = new List<Node>()
            {
                CreateNodeForVariable<string>(session, user, "myStringVariable", "12345.6")
            };
            var text = "  myStringVariable  ";
            Assert.IsTrue(ParameterCompiler.CreateExpression(nodes, text, out var expression, ref error), $"Failed to compile {text}");
            Assert.IsNotNull(expression);
            Assert.AreEqual(typeof(string), expression.Type);
            Assert.IsTrue(ParameterCompiler.Evaluate(null, expression, out var result, ref error), error);
            if (result is string strResult)
            {
                Assert.AreEqual("12345.6", strResult);
            }
            else
            {
                Assert.Fail("The result is not a string!");
            }
        });
    }

    [TestMethod]
    public void TestBadVariableNames()
    {
        TestHelper.RunInModelSystemContext("TestBadVariableNames", (User user, ProjectSession project, ModelSystemSession session) =>
        {
            var nodes = new List<Node>()
            {
                CreateNodeForVariable<string>(session, user, "myStringVariable", "12345.6")
            };
            TestFails("myStringVariable asd", nodes);
            TestFails("asd myStringVariable", nodes);
            TestFails("wrongVariableName", nodes);
        });
    }
}
