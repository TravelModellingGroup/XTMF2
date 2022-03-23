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
public class TestDivideOperator
{
    [TestMethod]
    public void TestDivide()
    {
        TestExpression("123 / 312", 0);
        TestExpression("123 / 122", 1);
    }

    [TestMethod]
    public void TestDivideFloat()
    {
        TestExpression("123.0 / 312.0", 0.3942307f);
        TestExpression("123.0 / 122.0", 1.0081967f);
    }

    [TestMethod]
    public void TestDivideBool()
    {
        TestFails("true / false");
    }

    [TestMethod]
    public void TestMultipleDivisions()
    {
        TestExpression("1 / 2 / 3", 0);
    }

    [TestMethod]
    public void TestMultipleDivisionsSpacesOnSides()
    {
        TestExpression("  1 / 2 / 3  ", 0);
    }

    [TestMethod]
    public void TestDivideExtraOnLeftFails()
    {
        TestFails("asd 1 / 2");
    }

    [TestMethod]
    public void TestDivideExtraOnRightFails()
    {
        TestFails("1 / 2 asd");
    }

    [TestMethod]
    public void TestDivideAtEndFails()
    {
        TestFails("1 /");
    }

    [TestMethod]
    public void TestDivideAtStartFails()
    {
        TestFails("/ 1");
    }

    [TestMethod]
    public void TestMixedIntFloatDivide()
    {
        TestFails("1.0 / 1");
    }

    [TestMethod]
    public void TestMixedIntStrDivide()
    {
        TestFails("1 / \"Hello\"");
    }

    [TestMethod]
    public void TestMixedIntBooleanDivide()
    {
        TestFails("1 / true");
    }

    [TestMethod]
    public void TestMixedFloatIntDivide()
    {
        TestFails("1.0 / 1");
    }

    [TestMethod]
    public void TestMixedFloatStrDivide()
    {
        TestFails("1.0 / \"Hello\"");
    }

    [TestMethod]
    public void TestMixedFloatBooleanDivide()
    {
        TestFails("1.0 / true");
    }

    [TestMethod]
    public void TestMixedStrIntDivide()
    {
        TestFails("\"1\" / 1");
    }

    [TestMethod]
    public void TestMixedStrFloatDivide()
    {
        TestFails("\"1\" / 1.0");
    }

    [TestMethod]
    public void TestMixedStrBooleanDivide()
    {
        TestFails("\"1.0\" / true");
    }

    [TestMethod]
    public void TestMixedBooleanIntDivide()
    {
        TestFails("true / 1");
    }

    [TestMethod]
    public void TestMixedBooleanFloatDivide()
    {
        TestFails("true / 1.0");
    }

    [TestMethod]
    public void TestMixedBooleanStrDivide()
    {
        TestFails("true / \"true\"");
    }
}
