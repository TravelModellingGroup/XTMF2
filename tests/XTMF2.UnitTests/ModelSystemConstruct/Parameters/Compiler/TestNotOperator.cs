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
using XTMF2.Editing;
using XTMF2.ModelSystemConstruct;
using XTMF2.ModelSystemConstruct.Parameters.Compiler;

using static XTMF2.UnitTests.ModelSystemConstruct.Parameters.Compiler.TestCompilerHelper;

namespace XTMF2.UnitTests.ModelSystemConstruct.Parameters.Compiler;

[TestClass]
public class TestNotOperator
{

    [TestMethod]
    public void TestNotTrue()
    {
        TestExpression("!True", false);
    }


    [TestMethod]
    public void TestNotTrueInBrackets()
    {
        TestExpression("!(True)", false);
    }

    [TestMethod]
    public void TestTextAfterFailsInBrackets()
    {
        TestFails("!(True asd)");
    }

    [TestMethod]
    public void TestNotFalse()
    {
        TestExpression("!False", true);
    }

    [TestMethod]
    public void TestNotTrueWithSpace()
    {
        TestExpression("! True", false);
    }

    [TestMethod]
    public void TestNotFalseWithSpace()
    {
        TestExpression("! False", true);
    }

    [TestMethod]
    public void TestNotTrueWithSpaceBefore()
    {
        TestExpression(" !True", false);
    }

    [TestMethod]
    public void TestNotTrueWithSpaceAfter()
    {
        TestExpression("!True ", false);
    }

    [TestMethod]
    public void TestTextBeforeFails()
    {
        TestFails("asd !True");
    }

    [TestMethod]
    public void TestTextAfterFails()
    {
        TestFails("!True asd");
    }

    [TestMethod]
    public void TestNonBoolTextFails()
    {
        TestFails("!asd");
    }

    [TestMethod]
    public void TestNotVariable()
    {
        TestHelper.RunInModelSystemContext("TestBadVariableNames", (User user, ProjectSession project, ModelSystemSession session) =>
        {
            var nodes = new List<Node>()
            {
                CreateNodeForVariable<bool>(session, user, "booleanVar", "true")
            };
            TestExpression("!booleanVar", false, nodes);
        });
    }

    [TestMethod]
    public void TestNotIntegerLiteralFails()
    {
        TestFails("!12345");
    }

    [TestMethod]
    public void TestNotFloatLiteralFails()
    {
        TestFails("!12345.6");
    }

    [TestMethod]
    public void TestNotStringLiteralFails()
    {
        TestFails("!\"true\"");
    }
}
