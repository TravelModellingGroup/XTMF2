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
public class TestEqualsOperator
{
    [TestMethod]
    public void TestDifferentValue()
    {
        TestExpression("123 == 312", false);
    }

    [TestMethod]
    public void TestSameValue()
    {
        TestExpression("123 == 123", true);
    }

    [TestMethod]
    public void TestFailsTextOnLeft()
    {
        TestFails("asd 123 == 312");
    }

    [TestMethod]
    public void TesFailsTextOnRight()
    {
        TestFails("123 == 312 asd");
    }

    [TestMethod]
    public void TestNothingOnLeftFails()
    {
        TestFails("== 123");
    }

    [TestMethod]
    public void TestNothingOnRightFails()
    {
        TestFails("123 ==");
    }

    [TestMethod]
    public void TestEqualsInString()
    {
        TestExpression("\"true==false\"", "true==false");
    }

    [TestMethod]
    public void TestEqualsAfterLessThan()
    {
        TestExpression("123 < 312 == true", true);
        TestExpression("312 < 123 == true", false);
    }

    [TestMethod]
    public void TestEqualsAfterLessThanOrEqualsThan()
    {
        TestExpression("123 <= 312 == true", true);
        TestExpression("312 <= 123 == true", false);
    }

    [TestMethod]
    public void TestEqualsAfterGreaterThan()
    {
        TestExpression("123 > 312 == true", false);
        TestExpression("312 > 123 == true", true);
    }

    [TestMethod]
    public void TestEqualsAfterGreaterThanOrEqualsThan()
    {
        TestExpression("123 >= 312 == true", false);
        TestExpression("312 >= 123 == true", true);
    }

    [TestMethod]
    public void TestMixedIntFloatEquals()
    {
        TestFails("1.0 == 1");
    }

    [TestMethod]
    public void TestMixedIntStrEquals()
    {
        TestFails("1 == \"Hello\"");
    }

    [TestMethod]
    public void TestMixedIntBooleanEquals()
    {
        TestFails("1 == true");
    }

    [TestMethod]
    public void TestMixedFloatIntEquals()
    {
        TestFails("1.0 == 1");
    }

    [TestMethod]
    public void TestMixedFloatStrEquals()
    {
        TestFails("1.0 == \"Hello\"");
    }

    [TestMethod]
    public void TestMixedFloatBooleanEquals()
    {
        TestFails("1.0 == true");
    }

    [TestMethod]
    public void TestMixedStrIntEquals()
    {
        TestFails("\"1\" == 1");
    }

    [TestMethod]
    public void TestMixedStrFloatEquals()
    {
        TestFails("\"1\" == 1.0");
    }

    [TestMethod]
    public void TestMixedStrBooleanEquals()
    {
        TestFails("\"1.0\" == true");
    }

    [TestMethod]
    public void TestMixedBooleanIntEquals()
    {
        TestFails("true == 1");
    }

    [TestMethod]
    public void TestMixedBooleanFloatEquals()
    {
        TestFails("true == 1.0");
    }

    [TestMethod]
    public void TestMixedBooleanStrEquals()
    {
        TestFails("true == \"true\"");
    }
}
