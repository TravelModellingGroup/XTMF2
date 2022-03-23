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
public class TestAddOperator
{
    [TestMethod]
    public void TestAddInteger()
    {
        TestExpression("123 + 312", 435);
    }

    [TestMethod]
    public void TestAddFloat()
    {
        TestExpression("123.0 + 312.0", 435.0f);
    }

    [TestMethod]
    public void TestAddString()
    {
        TestExpression("\"1\" + \"2\"", "12");
    }

    [TestMethod]
    public void TestAddBoolean()
    {
        TestFails("true + false");
    }

    [TestMethod]
    public void TestMultipleAddsInteger()
    {
        TestExpression("1 + 2 + 3", 6);
    }

    [TestMethod]
    public void TestAddMultipleString()
    {
        TestExpression("\"1\" + \"2\" + \"3\"", "123");
    }

    [TestMethod]
    public void TestMultipleAddsFloat()
    {
        TestExpression("1.0 + 2.0 + 3.0", 6.0f);
    }

    [TestMethod]
    public void TestMultipleAddsBoolean()
    {
        TestFails("true + false  true");
    }

    [TestMethod]
    public void TestMultipleAddsSpacesOnSides()
    {
        TestExpression("  1 + 2 + 3  ", 6);
    }

    [TestMethod]
    public void TestAddExtraOnLeftFails()
    {
        TestFails("asd 1 + 2");
    }

    [TestMethod]
    public void TestAddExtraOnRightFails()
    {
        TestFails("1 + 2 asd");
    }

    [TestMethod]
    public void TestAddAtEndFails()
    {
        TestFails("1 +");
    }

    [TestMethod]
    public void TestAddAtStartFails()
    {
        TestFails("+ 1");
    }

    [TestMethod]
    public void TestAddInString()
    {
        TestExpression("\"1+2\"", "1+2");
    }

    [TestMethod]
    public void TestMixedIntFloatAdd()
    {
        TestFails("1.0 + 1");
    }

    [TestMethod]
    public void TestMixedIntStrAdd()
    {
        TestFails("1 + \"Hello\"");
    }

    [TestMethod]
    public void TestMixedIntBooleanAdd()
    {
        TestFails("1 + true");
    }


    [TestMethod]
    public void TestMixedFloatIntAdd()
    {
        TestFails("1.0 + 1");
    }

    [TestMethod]
    public void TestMixedFloatStrAdd()
    {
        TestFails("1.0 + \"Hello\"");
    }

    [TestMethod]
    public void TestMixedFloatBooleanAdd()
    {
        TestFails("1.0 + true");
    }

    [TestMethod]
    public void TestMixedStrIntAdd()
    {
        TestFails("\"1\" + 1");
    }

    [TestMethod]
    public void TestMixedStrFloatAdd()
    {
        TestFails("\"1\" + 1.0");
    }

    [TestMethod]
    public void TestMixedStrBooleanAdd()
    {
        TestFails("\"1.0\" + true");
    }

    [TestMethod]
    public void TestMixedBooleanIntAdd()
    {
        TestFails("true + 1");
    }

    [TestMethod]
    public void TestMixedBooleanFloatAdd()
    {
        TestFails("true + 1.0");
    }

    [TestMethod]
    public void TestMixedBooleanStrAdd()
    {
        TestFails("true + \"true\"");
    }
}
