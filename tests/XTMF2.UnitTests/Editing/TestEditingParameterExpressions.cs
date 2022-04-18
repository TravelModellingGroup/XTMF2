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
using System;
using System.Linq;
using XTMF2.Editing;
using XTMF2.ModelSystemConstruct;
using XTMF2.RuntimeModules;
using XTMF2.UnitTests.Modules;

namespace XTMF2.UnitTests.Editing;

[TestClass]
public class TestEditingParameterExpressions
{
    [TestMethod]
    public void ParameterExpression()
    {
        TestHelper.RunInModelSystemContext("SetModuleToUseParameterExpression", (user, pSession, mSession) =>
        {
            CommandError error = null;
            string errorStr = null;
            var ms = mSession.ModelSystem;
            var gBound = ms.GlobalBoundary;
            Assert.IsTrue(mSession.AddNodeGenerateParameters(user, ms.GlobalBoundary, "Test",
                typeof(SimpleParameterModule), Rectangle.Hidden, out var node, out var children, out error), error?.Message);
            Assert.IsNotNull(children);
            var childNode = children.FirstOrDefault(n => n.Name == "Real Function");
            Assert.IsNotNull(childNode);
            Assert.IsTrue(mSession.SetParameterExpression(user, childNode, "\"Hello World\" + (1 + 2)", out error));
            Assert.IsNotNull(childNode.ParameterValue);
            Assert.AreEqual(childNode.ParameterValue.Type, typeof(string));
            Assert.IsInstanceOfType(childNode.ParameterValue, typeof(ParameterExpression));
            Assert.AreEqual("Hello World3", childNode.ParameterValue.GetValue(null, typeof(string), ref errorStr));
        });
    }

    [TestMethod]
    public void ParameterExpressionUndo()
    {
        TestHelper.RunInModelSystemContext("SetModuleToUseParameterExpression", (user, pSession, mSession) =>
        {
            CommandError error = null;
            string errorStr = null;
            var ms = mSession.ModelSystem;
            var gBound = ms.GlobalBoundary;
            Assert.IsTrue(mSession.AddNodeGenerateParameters(user, ms.GlobalBoundary, "Test",
                typeof(SimpleParameterModule), Rectangle.Hidden, out var node, out var children, out error), error?.Message);
            Assert.IsNotNull(children);
            var childNode = children.FirstOrDefault(n => n.Name == "Real Function");
            Assert.IsNotNull(childNode);
            Assert.IsTrue(mSession.SetParameterValue(user, childNode, "OriginalValue", out error));
            Assert.IsTrue(mSession.SetParameterExpression(user, childNode, "\"Hello World\" + (1 + 2)", out error));
            Assert.IsNotNull(childNode.ParameterValue);
            Assert.AreEqual(childNode.ParameterValue.Type, typeof(string));
            Assert.IsInstanceOfType(childNode.ParameterValue, typeof(ParameterExpression));
            Assert.AreEqual("Hello World3", childNode.ParameterValue.GetValue(null, typeof(string), ref errorStr));
            Assert.IsTrue(mSession.Undo(user, out error), error?.Message);
            Assert.AreEqual("OriginalValue", childNode.ParameterValue.GetValue(null, typeof(string), ref errorStr));
        });
    }

    [TestMethod]
    public void ParameterExpressionRedo()
    {
        TestHelper.RunInModelSystemContext("SetModuleToUseParameterExpression", (user, pSession, mSession) =>
        {
            CommandError error = null;
            string errorStr = null;
            var ms = mSession.ModelSystem;
            var gBound = ms.GlobalBoundary;
            Assert.IsTrue(mSession.AddNodeGenerateParameters(user, ms.GlobalBoundary, "Test",
                typeof(SimpleParameterModule), Rectangle.Hidden, out var node, out var children, out error), error?.Message);
            Assert.IsNotNull(children);
            var childNode = children.FirstOrDefault(n => n.Name == "Real Function");
            Assert.IsNotNull(childNode);
            Assert.IsTrue(mSession.SetParameterValue(user, childNode, "OriginalValue", out error));
            Assert.IsTrue(mSession.SetParameterExpression(user, childNode, "\"Hello World\" + (1 + 2)", out error));
            Assert.IsNotNull(childNode.ParameterValue);
            Assert.AreEqual(childNode.ParameterValue.Type, typeof(string));
            Assert.IsInstanceOfType(childNode.ParameterValue, typeof(ParameterExpression));
            Assert.AreEqual("Hello World3", childNode.ParameterValue.GetValue(null, typeof(string), ref errorStr));
            Assert.IsTrue(mSession.Undo(user, out error), error?.Message);
            Assert.AreEqual("OriginalValue", childNode.ParameterValue.GetValue(null, typeof(string), ref errorStr));
            Assert.IsTrue(mSession.Redo(user, out error), error?.Message);
            Assert.AreEqual("Hello World3", childNode.ParameterValue.GetValue(null, typeof(string), ref errorStr));
        });
    }

    [TestMethod]
    public void ParameterExpressionSaved()
    {
        TestHelper.RunInModelSystemContext("SetModuleToUseParameterExpression", (user, pSession, mSession) =>
        {
            CommandError error = null;
            string errorStr = null;
            var ms = mSession.ModelSystem;
            var gBound = ms.GlobalBoundary;
            Assert.IsTrue(mSession.AddNodeGenerateParameters(user, ms.GlobalBoundary, "Test",
                typeof(SimpleParameterModule), Rectangle.Hidden, out var node, out var children, out error), error?.Message);
            Assert.IsNotNull(children);
            var childNode = children.FirstOrDefault(n => n.Name == "Real Function");
            Assert.IsNotNull(childNode);
            Assert.IsTrue(mSession.SetParameterExpression(user, childNode, "\"Hello World\" + (1 + 2)", out error));
            Assert.IsNotNull(childNode.ParameterValue);
            Assert.AreEqual(childNode.ParameterValue.Type, typeof(string));
            Assert.IsInstanceOfType(childNode.ParameterValue, typeof(ParameterExpression));
            Assert.AreEqual("Hello World3", childNode.ParameterValue.GetValue(null, typeof(string), ref errorStr));
        },(user, pSession, mSession)=>
        {
            string errorStr = null;
            var ms = mSession.ModelSystem;
            var gBound = ms.GlobalBoundary;
            var childNode = gBound.Modules.FirstOrDefault(n => n.Name == "Real Function");
            Assert.IsNotNull(childNode);
            Assert.IsNotNull(childNode.ParameterValue);
            Assert.AreEqual("\"Hello World\" + (1 + 2)", childNode.ParameterValue.Representation);
            Assert.AreEqual("Hello World3", childNode.ParameterValue.GetValue(null, typeof(string), ref errorStr));
        });
    }
}

