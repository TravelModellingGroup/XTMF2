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

using static XTMF2.UnitTests.ModelSystemConstruct.Parameters.Compiler.TestCompilerHelper;

namespace XTMF2.UnitTests.ModelSystemConstruct.Parameters.Compiler;

[TestClass]
public class TestLiterals
{
    [TestMethod]
    public void TestIntegerLiteral()
    {
        TestExpression("12345", 12345);
    }

    [TestMethod]
    public void TestBadIntegerLiteral()
    {
        TestFails("12345abc");
    }

    [TestMethod]
    public void TestFloatLiteral()
    {
        TestExpression("12345.6", 12345.6f);
    }

    [TestMethod]
    public void TestBadFloatLiteral()
    {
        TestFails("1234abc.5");
        TestFails("1234.5f");
        TestFails("f1234.5");
        TestFails("1234.5 f");
    }

    [TestMethod]
    public void TestBooleanLiteralTrue()
    {
        TestExpression("True", true);
        TestExpression("true", true);
    }

    [TestMethod]
    public void TestBooleanLiteralFalse()
    {
        TestExpression("False", false);
        TestExpression("false", false);
    }

    [TestMethod]
    public void TestStringLiteral()
    {
        TestExpression("\"12345.6\"", "12345.6");
    }

    [TestMethod]
    public void TestWhitespaceBeforeStringLiteral()
    {
        TestExpression(" \"12345.6\"", "12345.6");
    }

    [TestMethod]
    public void TestWhitespaceAfterStringLiteral()
    {
        TestExpression("\"12345.6\" ", "12345.6");
    }

    [TestMethod]
    public void TestBadStringLiteral()
    {
        TestFails("\"No final quote");
        TestFails("Text before quote \"");
        TestFails("\"Text\" Text after quote");
        TestFails("Text before quote \"Text\"");
    }
}
