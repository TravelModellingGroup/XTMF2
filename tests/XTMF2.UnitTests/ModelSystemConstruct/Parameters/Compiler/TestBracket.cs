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

using static XTMF2.UnitTests.ModelSystemConstruct.Parameters.Compiler.TestCompilerHelper;

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
        TestExpression("(12345)", 12345);
    }

    [TestMethod]
    public void TestWhitespaceBeforeBracketIntegerLiteral()
    {
        TestExpression(" (12345)", 12345);
    }

    [TestMethod]
    public void TestWhitespaceAfterBracketIntegerLiteral()
    {
        TestExpression("(12345) ", 12345);
    }

    [TestMethod]
    public void TestDoubleBracketIntegerLiteral()
    {
        TestExpression("((12345))", 12345);
    }

    [TestMethod]
    public void TestTooManyOpenBrackets()
    {
        TestFails("((12345)");
    }

    [TestMethod]
    public void TestTooManyCloseBrackets()
    {
        TestFails("(12345))");
    }

    [TestMethod]
    public void TestTextBeforeBracket()
    {
        TestFails("asd (12345)");
    }

    [TestMethod]
    public void TestTextAfterBracket()
    {
        TestFails("(12345) asd");
    }

    [TestMethod]
    public void TestCloseBracketFirst()
    {
        TestFails(")(12345)");
    }
    [TestMethod]
    public void TestCloseBracketBeforeOpen()
    {
        TestFails(")(12345)(");
    }

    [TestMethod]
    public void TestOpenBracketInString()
    {
        TestExpression("\"(\"", "(");
    }

    [TestMethod]
    public void TestCloseBracketInString()
    {
        TestExpression("\")\"", ")");
    }
}
