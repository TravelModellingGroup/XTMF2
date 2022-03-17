/*
    Copyright 2022 University of Toronto

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
using System.Collections.Generic;
using XTMF2.ModelSystemConstruct;
using XTMF2.ModelSystemConstruct.Parameters.Compiler;

namespace XTMF2.UnitTests.ModelSystemConstruct.Parameters.Compiler;

[TestClass]
public class TestLiterals
{
    /// <summary>
    /// Gives an empty set of nodes for use in the compiler.
    /// </summary>
    private static readonly List<Node> EmptyNodeList = new();

    [TestMethod]
    public void TestIntegerLiteral()
    {
        string error = null;
        var text = "12345";
        Assert.IsTrue(ParameterCompiler.CreateExpression(EmptyNodeList, text, out var expression, ref error), $"Failed to compile {text}");
        Assert.IsNotNull(expression);
        Assert.AreEqual(typeof(int), expression.Type);
        Assert.IsTrue(ParameterCompiler.Evaluate(null!, expression, out var result, ref error), error);
        if (result is int intResult)
        {
            Assert.AreEqual(12345, intResult);
        }
        else
        {
            Assert.Fail("The result is not an integer!");
        }
    }

    [TestMethod]
    public void TestBadIntegerLiteral()
    {
        TestInvalidScript("12345abc");
    }

    [TestMethod]
    public void TestFloatLiteral()
    {
        string error = null;
        var text = "12345.6";
        Assert.IsTrue(ParameterCompiler.CreateExpression(EmptyNodeList, text, out var expression, ref error), $"Failed to compile {text}");
        Assert.IsNotNull(expression);
        Assert.AreEqual(typeof(float), expression.Type);
        Assert.IsTrue(ParameterCompiler.Evaluate(null!, expression, out var result, ref error), error);
        if (result is float floatResult)
        {
            Assert.AreEqual(12345.6, floatResult, 0.001f);
        }
        else
        {
            Assert.Fail("The result is not an integer!");
        }
    }

    [TestMethod]
    public void TestBadFloatLiteral()
    {
        TestInvalidScript("1234abc.5");
        TestInvalidScript("1234.5f");
        TestInvalidScript("f1234.5");
        TestInvalidScript("1234.5 f");
    }

    [TestMethod]
    public void TestBooleanLiteralTrue()
    {
        TestBooleanLiteral("True", true);
        TestBooleanLiteral("true", true);
    }

    [TestMethod]
    public void TestBooleanLiteralFalse()
    {
        TestBooleanLiteral("False", false);
        TestBooleanLiteral("false", false);
    }

    /// <summary>
    /// Provides a common call site for testing boolean literals.
    /// </summary>
    /// <param name="text">The text value to test.</param>
    /// <param name="expectedResult">The expected result of the text.</param>
    private static void TestBooleanLiteral(string text, bool expectedResult)
    {
        string error = null;
        Assert.IsTrue(ParameterCompiler.CreateExpression(EmptyNodeList, text, out var expression, ref error), $"Failed to compile {text}");
        Assert.IsNotNull(expression, "The a null expression was returned!");
        Assert.AreEqual(typeof(bool), expression.Type);
        Assert.IsTrue(ParameterCompiler.Evaluate(null!, expression, out var result, ref error), error);
        if (result is bool boolResult)
        {
            Assert.AreEqual(expectedResult, boolResult);
        }
        else
        {
            Assert.Fail("The result is not an bool!");
        }
    }

    /// <summary>
    /// Make sure that the given script text fails to compile.
    /// </summary>
    /// <param name="text"></param>
    private static void TestInvalidScript(string text)
    {
        string error = null;
        Assert.IsFalse(ParameterCompiler.CreateExpression(EmptyNodeList, text, out var expression, ref error), $"Invalid script was compiled: {text}");
        Assert.IsNull(expression);
        Assert.IsNotNull(error);
    }

    [TestMethod]
    public void TestStringLiteral()
    {
        string error = null;
        var text = "\"12345.6\"";
        Assert.IsTrue(ParameterCompiler.CreateExpression(EmptyNodeList, text, out var expression, ref error), $"Failed to compile {text}");
        Assert.IsNotNull(expression);
        Assert.AreEqual(typeof(string), expression.Type);
        Assert.IsTrue(ParameterCompiler.Evaluate(null!, expression, out var result, ref error), error);
        if (result is string strResult)
        {
            Assert.AreEqual("12345.6", strResult);
        }
        else
        {
            Assert.Fail("The result is not an integer!");
        }
    }

    [TestMethod]
    public void TestBadStringLiteral()
    {
        TestInvalidScript("\"No final quote");
        TestInvalidScript("Text before quote \"");
        TestInvalidScript("\"Text\" Text after quote");
        TestInvalidScript("Text after quote \"Text\"");
    }
}
