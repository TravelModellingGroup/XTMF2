﻿/*
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
public class TestGreaterThanOrEqualsOperator
{
    [TestMethod]
    public void TestGreaterThanOrEquals()
    {
        TestExpression("123 >= 312", false);
        TestExpression("312 >= 123", true);
    }

    [TestMethod]
    public void TestGreaterThanOrEqualsSameValue()
    {
        TestExpression("123 >= 123", true);
    }

    [TestMethod]
    public void TestGreaterThanOrEqualsFailsTextOnLeft()
    {
        TestFails("asd 123 >= 312");
    }

    [TestMethod]
    public void TestGreaterThanOrEqualsFailsTextOnRight()
    {
        TestFails("123 >= 312 asd");
    }

    [TestMethod]
    public void TestGreaterThanEqualsNothingOnLeftFails()
    {
        TestFails(">= 123");
    }

    [TestMethod]
    public void TestGreaterThanEqualsNothingOnRightFails()
    {
        TestFails("123 >=");
    }

    [TestMethod]
    public void TestGreaterThanOrEqualsInString()
    {
        TestExpression("\"1>=2\"", "1>=2");
    }

    [TestMethod]
    public void TestMixedIntFloatGreatherThanOrEqual()
    {
        TestFails("1.0 >= 1");
    }

    [TestMethod]
    public void TestMixedIntStrGreatherThanOrEqual()
    {
        TestFails("1 >= \"Hello\"");
    }

    [TestMethod]
    public void TestMixedIntBooleanGreatherThanOrEqual()
    {
        TestFails("1 >= true");
    }

    [TestMethod]
    public void TestMixedFloatIntGreatherThanOrEqual()
    {
        TestFails("1.0 >= 1");
    }

    [TestMethod]
    public void TestMixedFloatStrGreatherThanOrEqual()
    {
        TestFails("1.0 >= \"Hello\"");
    }

    [TestMethod]
    public void TestMixedFloatBooleanGreatherThanOrEqual()
    {
        TestFails("1.0 >= true");
    }

    [TestMethod]
    public void TestMixedStrIntGreatherThanOrEqual()
    {
        TestFails("\"1\" >= 1");
    }

    [TestMethod]
    public void TestMixedStrFloatGreatherThanOrEqual()
    {
        TestFails("\"1\" >= 1.0");
    }

    [TestMethod]
    public void TestMixedStrBooleanGreatherThanOrEqual()
    {
        TestFails("\"1.0\" >= true");
    }

    [TestMethod]
    public void TestMixedBooleanIntGreatherThanOrEqual()
    {
        TestFails("true >= 1");
    }

    [TestMethod]
    public void TestMixedBooleanFloatGreatherThanOrEqual()
    {
        TestFails("true >= 1.0");
    }

    [TestMethod]
    public void TestMixedBooleanStrGreatherThanOrEqual()
    {
        TestFails("true >= \"true\"");
    }
}
