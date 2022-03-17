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
public class TestBracket
{
    /// <summary>
    /// Gives an empty set of nodes for use in the compiler.
    /// </summary>
    private static readonly List<Node> EmptyNodeList = new();

    [TestMethod]
    public void TestBracketIntegerLiteral()
    {
        string error = null;
        var text = "(12345)";
        Assert.IsTrue(ParameterCompiler.CreateExpression(EmptyNodeList, text, out var expression, ref error), $"Failed to compile {text}");
        Assert.IsNotNull(expression);
        Assert.AreEqual(typeof(int), expression.Type);
        Assert.IsTrue(ParameterCompiler.Evaluate(null!, expression, out var result, ref error), error);
        if (result is int intResult)
        {
            Assert.AreEqual(12345, intResult);
        }
    }

    [TestMethod]
    public void TestWhitespaceBeforeBracketIntegerLiteral()
    {
        string error = null;
        var text = " (12345)";
        Assert.IsTrue(ParameterCompiler.CreateExpression(EmptyNodeList, text, out var expression, ref error), $"Failed to compile {text}");
        Assert.IsNotNull(expression);
        Assert.AreEqual(typeof(int), expression.Type);
        Assert.IsTrue(ParameterCompiler.Evaluate(null!, expression, out var result, ref error), error);
        if (result is int intResult)
        {
            Assert.AreEqual(12345, intResult);
        }
    }

    [TestMethod]
    public void TestWhitespaceAfterBracketIntegerLiteral()
    {
        string error = null;
        var text = "(12345) ";
        Assert.IsTrue(ParameterCompiler.CreateExpression(EmptyNodeList, text, out var expression, ref error), $"Failed to compile {text}");
        Assert.IsNotNull(expression);
        Assert.AreEqual(typeof(int), expression.Type);
        Assert.IsTrue(ParameterCompiler.Evaluate(null!, expression, out var result, ref error), error);
        if (result is int intResult)
        {
            Assert.AreEqual(12345, intResult);
        }
    }

    [TestMethod]
    public void TestDoubleBracketIntegerLiteral()
    {
        string error = null;
        var text = "((12345))";
        Assert.IsTrue(ParameterCompiler.CreateExpression(EmptyNodeList, text, out var expression, ref error), $"Failed to compile {text}");
        Assert.IsNotNull(expression);
        Assert.AreEqual(typeof(int), expression.Type);
        Assert.IsTrue(ParameterCompiler.Evaluate(null!, expression, out var result, ref error), error);
        if (result is int intResult)
        {
            Assert.AreEqual(12345, intResult);
        }
    }

    [TestMethod]
    public void TestTooManyOpenBrackets()
    {
        string error = null;
        var text = "((12345)";
        Assert.IsFalse(ParameterCompiler.CreateExpression(EmptyNodeList, text, out var expression, ref error), $"Successfully compiled bad code: {text}");
        Assert.IsNull(expression);
        Assert.IsNotNull(error);
    }

    [TestMethod]
    public void TestTooManyCloseBrackets()
    {
        string error = null;
        var text = "(12345))";
        Assert.IsFalse(ParameterCompiler.CreateExpression(EmptyNodeList, text, out var expression, ref error), $"Successfully compiled bad code: {text}");
        Assert.IsNull(expression);
        Assert.IsNotNull(error);
    }

    [TestMethod]
    public void TestTextBeforeBracket()
    {
        string error = null;
        var text = "asd (12345)";
        Assert.IsFalse(ParameterCompiler.CreateExpression(EmptyNodeList, text, out var expression, ref error), $"Successfully compiled bad code: {text}");
        Assert.IsNull(expression);
        Assert.IsNotNull(error);
    }

    [TestMethod]
    public void TestTextAfterBracket()
    {
        string error = null;
        var text = "(12345) asd";
        Assert.IsFalse(ParameterCompiler.CreateExpression(EmptyNodeList, text, out var expression, ref error), $"Successfully compiled bad code: {text}");
        Assert.IsNull(expression);
        Assert.IsNotNull(error);
    }

    [TestMethod]
    public void TestCloseBracketFirst()
    {
        string error = null;
        var text = ")((12345)";
        Assert.IsFalse(ParameterCompiler.CreateExpression(EmptyNodeList, text, out var expression, ref error), $"Successfully compiled bad code: {text}");
        Assert.IsNull(expression);
        Assert.IsNotNull(error);
    }
}