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
public class TestMultiplyOperator
{
    [TestMethod]
    public void TestMultiply()
    {
        TestExpression("123 * 312", 38376);
    }

    [TestMethod]
    public void TestMultiplyFloat()
    {
        TestExpression("123.0 * 312.0", 38376.0f);
    }

    [TestMethod]
    public void TestMultipleMultiplications()
    {
        TestExpression("1 * 2 * 3", 6);
    }

    [TestMethod]
    public void TestMultipleMultiplicationsSpacesOnSides()
    {
        TestExpression("  1 * 2 * 3  ", 6);
    }

    [TestMethod]
    public void TestMultiplyExtraOnLeftFails()
    {
        TestFails("asd 1 * 2");
    }

    [TestMethod]
    public void TestMultiplyExtraOnRightFails()
    {
        TestFails("1 * 2 asd");
    }

    [TestMethod]
    public void TestMultiplyAtEndFails()
    {
        TestFails("1 *");
    }

    [TestMethod]
    public void TestMultiplyAtStartFails()
    {
        TestFails("* 1");
    }

    [TestMethod]
    public void TestMultiplyInString()
    {
        TestExpression("\"1*2\"", "1*2");
    }

    [TestMethod]
    public void TestMixedIntFloatMultiply()
    {
        TestFails("1.0 * 1");
    }

    [TestMethod]
    public void TestMixedIntStrMultiply()
    {
        TestFails("1 * \"Hello\"");
    }

    [TestMethod]
    public void TestMixedIntBooleanMultiply()
    {
        TestFails("1 * true");
    }

    [TestMethod]
    public void TestMixedFloatIntMultiply()
    {
        TestFails("1.0 * 1");
    }

    [TestMethod]
    public void TestMixedFloatStrMultiply()
    {
        TestFails("1.0 * \"Hello\"");
    }

    [TestMethod]
    public void TestMixedFloatBooleanMultiply()
    {
        TestFails("1.0 * true");
    }

    [TestMethod]
    public void TestMixedStrIntMultiply()
    {
        TestFails("\"1\" * 1");
    }

    [TestMethod]
    public void TestMixedStrFloatMultiply()
    {
        TestFails("\"1\" * 1.0");
    }

    [TestMethod]
    public void TestMixedStrBooleanMultiply()
    {
        TestFails("\"1.0\" * true");
    }

    [TestMethod]
    public void TestMixedBooleanIntMultiply()
    {
        TestFails("true * 1");
    }

    [TestMethod]
    public void TestMixedBooleanFloatMultiply()
    {
        TestFails("true * 1.0");
    }

    [TestMethod]
    public void TestMixedBooleanStrMultiply()
    {
        TestFails("true * \"true\"");
    }
}
