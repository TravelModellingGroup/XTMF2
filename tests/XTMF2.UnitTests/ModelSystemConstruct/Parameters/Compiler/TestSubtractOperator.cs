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
public class TestSubtractOperator
{
    [TestMethod]
    public void TestSubtract()
    {
        TestExpression("123 - 312", -189);
    }

    [TestMethod]
    public void TestSubtractFloat()
    {
        TestExpression("123.0 - 312.0", -189.0f);
    }

    [TestMethod]
    public void TestMultipleSubtracts()
    {
        TestExpression("1 - 2 - 3", -4);
    }

    [TestMethod]
    public void TestMultipleSubtractsSpacesOnSides()
    {
        TestExpression("  1 - 2 - 3  ", -4);
    }

    [TestMethod]
    public void TestSubtractExtraOnLeftFails()
    {
        TestFails("asd 1 - 2");
    }

    [TestMethod]
    public void TestSubtractExtraOnRightFails()
    {
        TestFails("1 - 2 asd");
    }

    [TestMethod]
    public void TestSubtractAtEndFails()
    {
        TestFails("1 -");
    }

    [TestMethod]
    public void TestMixedIntFloatSubtract()
    {
        TestFails("1.0 - 1");
    }

    [TestMethod]
    public void TestMixedIntStrSubtract()
    {
        TestFails("1 - \"Hello\"");
    }

    [TestMethod]
    public void TestMixedIntBooleanSubtract()
    {
        TestFails("1 - true");
    }

    [TestMethod]
    public void TestMixedFloatIntSubtract()
    {
        TestFails("1.0 - 1");
    }

    [TestMethod]
    public void TestMixedFloatStrSubtract()
    {
        TestFails("1.0 - \"Hello\"");
    }

    [TestMethod]
    public void TestMixedFloatBooleanSubtract()
    {
        TestFails("1.0 - true");
    }

    [TestMethod]
    public void TestMixedStrIntSubtract()
    {
        TestFails("\"1\" - 1");
    }

    [TestMethod]
    public void TestMixedStrFloatSubtract()
    {
        TestFails("\"1\" - 1.0");
    }

    [TestMethod]
    public void TestMixedStrBooleanSubtract()
    {
        TestFails("\"1.0\" - true");
    }

    [TestMethod]
    public void TestMixedBooleanIntSubtract()
    {
        TestFails("true - 1");
    }

    [TestMethod]
    public void TestMixedBooleanFloatSubtract()
    {
        TestFails("true - 1.0");
    }

    [TestMethod]
    public void TestMixedBooleanStrSubtract()
    {
        TestFails("true - \"true\"");
    }
}
