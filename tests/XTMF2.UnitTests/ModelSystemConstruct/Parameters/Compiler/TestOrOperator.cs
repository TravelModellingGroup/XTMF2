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
public class TestOrOperator
{
    [TestMethod]
    public void TestOr()
    {
        TestExpression("true || true", true);
        TestExpression("true || false", true);
        TestExpression("false || true", true);
        TestExpression("false || false", false);
    }

    [TestMethod]
    public void TestOrNoSpaces()
    {
        TestExpression("true||true", true);
        TestExpression("true||false", true);
        TestExpression("false||true", true);
        TestExpression("false||false", false);
    }

    [TestMethod]
    public void TestOrOutsideOfAnd()
    {
        TestExpression("true || true && false", true);
    }

    [TestMethod]
    public void TestOrInString()
    {
        TestExpression("\"true || true\"", "true || true");
    }

    [TestMethod]
    public void TestOrWithIntegers()
    {
        TestFails("1||2");
    }

    [TestMethod]
    public void TestOrWithFloats()
    {
        TestFails("1.0||2.0");
    }

    [TestMethod]
    public void TestOrWithStrings()
    {
        TestFails("\"1.0\"||\"2.0\"");
    }
}
