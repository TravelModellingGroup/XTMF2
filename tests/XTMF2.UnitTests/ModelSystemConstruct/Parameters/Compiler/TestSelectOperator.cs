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
public class TestSelectOperator
{
    [TestMethod]
    public void TestSelectInteger()
    {
        TestExpression("true?1:2", 1);
        TestExpression("false?1:2", 2);
    }

    [TestMethod]
    public void TestMultipleSelects()
    {
        TestExpression("true?true?1:2:3", 1);
        TestExpression("true?false?1:2:3", 2);
        TestExpression("false?false?1:2:3", 3);
    }

    [TestMethod]
    public void TestSelectFloat()
    {
        TestExpression("true?1.0:2.0", 1.0f);
        TestExpression("false?1.0:2.0", 2.0f);
    }

    [TestMethod]
    public void TestSelectString()
    {
        TestExpression("true?\"1.0\":\"2.0\"", "1.0");
        TestExpression("false?\"1.0\":\"2.0\"", "2.0");
    }

    [TestMethod]
    public void TestSelectStringFailsTextOnLeft()
    {
        TestFails("asd true?\"1.0\":\"2.0\"");
    }

    [TestMethod]
    public void TestSelectStringFailsTextOnRight()
    {
        TestFails("true?\"1.0\":\"2.0\" asd");
    }

    [TestMethod]
    public void TestSelectStringFailsMissingCondition()
    {
        TestFails("?\"1.0\":\"2.0\"");
    }

    [TestMethod]
    public void TestSelectStringFailsMissingTrue()
    {
        TestFails("true?:\"2.0\"");
    }

    [TestMethod]
    public void TestSelectStringFailsMissingFalse()
    {
        TestFails("true?\"1.0\":");
    }

    [TestMethod]
    public void TestSelectStringFailsMissingCaseSeperator()
    {
        TestFails("true?\"1.0\" \"2.0\"");
    }

    [TestMethod]
    public void TestIntegerConditionFails()
    {
        TestFails("1?\"1.0\" \"2.0\"");
    }

    [TestMethod]
    public void TestFloatConditionFails()
    {
        TestFails("1.0?\"1.0\" \"2.0\"");
    }

    [TestMethod]
    public void TestStringConditionFails()
    {
        TestFails("\"1.0\"?\"1.0\" \"2.0\"");
    }

    [TestMethod]
    public void TestNoSelectOperatorWithBreak()
    {
        TestFails("true 1 : 2");
        TestFails("1:2");
        TestFails(":");
    }

    [TestMethod]
    public void TestConditionOperatorInString()
    {
        TestExpression("\"true?1:0\"", "true?1:0");
    }

    [TestMethod]
    public void TestMixedIntFloatSelect()
    {
        TestFails("true ? 1.0 : 1");
    }

    [TestMethod]
    public void TestMixedIntStrSelect()
    {
        TestFails("true ? 1 : \"Hello\"");
    }

    [TestMethod]
    public void TestMixedIntBooleanSelect()
    {
        TestFails("true ? 1 : true");
    }

    [TestMethod]
    public void TestMixedFloatIntSelect()
    {
        TestFails("true ? 1.0 : 1");
    }

    [TestMethod]
    public void TestMixedFloatStrSelect()
    {
        TestFails("true ? 1.0 : \"Hello\"");
    }

    [TestMethod]
    public void TestMixedFloatBooleanSelect()
    {
        TestFails("true ? 1.0 : true");
    }

    [TestMethod]
    public void TestMixedStrIntSelect()
    {
        TestFails("true ? \"1\" : 1");
    }

    [TestMethod]
    public void TestMixedStrFloatSelect()
    {
        TestFails("true ? \"1\" : 1.0");
    }

    [TestMethod]
    public void TestMixedStrBooleanSelect()
    {
        TestFails("true ? \"1.0\" : true");
    }

    [TestMethod]
    public void TestMixedBooleanIntSelect()
    {
        TestFails("true ? true : 1");
    }

    [TestMethod]
    public void TestMixedBooleanFloatSelect()
    {
        TestFails("true ? true : 1.0");
    }

    [TestMethod]
    public void TestMixedBooleanStrSelect()
    {
        TestFails("true ? true : \"true\"");
    }
}
