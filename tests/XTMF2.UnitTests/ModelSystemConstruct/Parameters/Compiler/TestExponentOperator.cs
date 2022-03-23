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
public class TestExponentOperator
{
    [TestMethod]
    public void TestExponentInteger()
    {
        TestExpression("3 ^ 4", 81);
        TestExpression("3 ^ 5", 243);
    }

    [TestMethod]
    public void TestExponentFloat()
    {
        TestExpression("3.0 ^ 4.0", 81.0f);
        TestExpression("3.0 ^ 5.0", 243.0f);
    }

    [TestMethod]
    public void TestMultipleExponents()
    {
        TestExpression("1 ^ 2 ^ 3", 1);
    }

    [TestMethod]
    public void TestMultipleExponentsSpacesOnSides()
    {
        TestExpression("  1 ^ 2 ^ 3  ", 1);
    }

    [TestMethod]
    public void TestExponentExtraOnLeftFails()
    {
        TestFails("asd 1 ^ 2");
    }

    [TestMethod]
    public void TestExponentExtraOnRightFails()
    {
        TestFails("1 ^ 2 asd");
    }

    [TestMethod]
    public void TestExponentAtEndFails()
    {
        TestFails("1 ^");
    }

    [TestMethod]
    public void TestExponentAtStartFails()
    {
        TestFails("^ 1");
    }

    [TestMethod]
    public void TestMixedIntFloatExponent()
    {
        TestFails("1.0 ^ 1");
    }

    [TestMethod]
    public void TestMixedIntStrExponent()
    {
        TestFails("1 ^ \"Hello\"");
    }

    [TestMethod]
    public void TestMixedIntBooleanExponent()
    {
        TestFails("1 ^ true");
    }

    [TestMethod]
    public void TestMixedFloatIntExponent()
    {
        TestFails("1.0 ^ 1");
    }

    [TestMethod]
    public void TestMixedFloatStrExponent()
    {
        TestFails("1.0 ^ \"Hello\"");
    }

    [TestMethod]
    public void TestMixedFloatBooleanExponent()
    {
        TestFails("1.0 ^ true");
    }

    [TestMethod]
    public void TestMixedStrIntExponent()
    {
        TestFails("\"1\" ^ 1");
    }

    [TestMethod]
    public void TestMixedStrFloatExponent()
    {
        TestFails("\"1\" ^ 1.0");
    }

    [TestMethod]
    public void TestMixedStrBooleanExponent()
    {
        TestFails("\"1.0\" ^ true");
    }

    [TestMethod]
    public void TestMixedBooleanIntExponent()
    {
        TestFails("true ^ 1");
    }

    [TestMethod]
    public void TestMixedBooleanFloatExponent()
    {
        TestFails("true ^ 1.0");
    }

    [TestMethod]
    public void TestMixedBooleanStrExponent()
    {
        TestFails("true ^ \"true\"");
    }
}
