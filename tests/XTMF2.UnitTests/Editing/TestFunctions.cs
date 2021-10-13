/*
    Copyright 2021 University of Toronto

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
using System.Linq;
using XTMF2.ModelSystemConstruct;
using XTMF2.Editing;

namespace XTMF2.UnitTests.Editing
{
    [TestClass]
    public class TestFunctions
    {
        [TestMethod]
        public void TestAddFunctionTemplate()
        {
            TestHelper.RunInModelSystemContext("TestAddFunctionTemplate", (user, pSession, mSession) =>
            {
                CommandError error = null;
                var ms = mSession.ModelSystem;
                var name = "FunctionTemplateName";
                var functionTemplates = ms.GlobalBoundary.FunctionTemplates;
                Assert.AreEqual(0, functionTemplates.Count);
                Assert.IsTrue(mSession.AddFunctionTemplate(user, ms.GlobalBoundary, name, out FunctionTemplate template, out error), error?.Message);
                Assert.AreEqual(1, functionTemplates.Count);
            });
        }

        [TestMethod]
        public void TestAddFunctionTemplateUndo()
        {
            TestHelper.RunInModelSystemContext("TestAddFunctionTemplateUndo", (user, pSession, mSession) =>
            {
                CommandError error = null;
                var ms = mSession.ModelSystem;
                var name = "FunctionTemplateName";
                var functionTemplates = ms.GlobalBoundary.FunctionTemplates;
                Assert.AreEqual(0, functionTemplates.Count);
                Assert.IsTrue(mSession.AddFunctionTemplate(user, ms.GlobalBoundary, name, out FunctionTemplate template, out error), error?.Message);
                Assert.AreEqual(1, functionTemplates.Count);
                Assert.IsTrue(mSession.Undo(user, out error));
                Assert.AreEqual(0, functionTemplates.Count);
                Assert.IsTrue(mSession.Redo(user, out error));
                Assert.AreEqual(1, functionTemplates.Count);
            });
        }

        [TestMethod]
        public void TestRemoveFunctionTemplate()
        {
            TestHelper.RunInModelSystemContext("TestRemoveFunctionTemplate", (user, pSession, mSession) =>
            {
                CommandError error = null;
                var ms = mSession.ModelSystem;
                var name = "FunctionTemplateName";
                var functionTemplates = ms.GlobalBoundary.FunctionTemplates;
                Assert.AreEqual(0, functionTemplates.Count);
                Assert.IsTrue(mSession.AddFunctionTemplate(user, ms.GlobalBoundary, name, out FunctionTemplate template, out error), error?.Message);
                Assert.AreEqual(1, functionTemplates.Count);
                Assert.IsTrue(mSession.RemoveFunctionTemplate(user, ms.GlobalBoundary, template, out error), error?.Message);
                Assert.AreEqual(0, functionTemplates.Count);
            });
        }

        [TestMethod]
        public void TestRemoveFunctionTemplateUndo()
        {
            TestHelper.RunInModelSystemContext("TestRemoveFunctionTemplateUndo", (user, pSession, mSession) =>
            {
                CommandError error = null;
                var ms = mSession.ModelSystem;
                var name = "FunctionTemplateName";
                var functionTemplates = ms.GlobalBoundary.FunctionTemplates;
                Assert.AreEqual(0, functionTemplates.Count);
                Assert.IsTrue(mSession.AddFunctionTemplate(user, ms.GlobalBoundary, name, out FunctionTemplate template, out error), error?.Message);
                Assert.AreEqual(1, functionTemplates.Count);
                Assert.IsTrue(mSession.RemoveFunctionTemplate(user, ms.GlobalBoundary, template, out error), error?.Message);
                Assert.AreEqual(0, functionTemplates.Count);
                Assert.IsTrue(mSession.Undo(user, out error));
                Assert.AreEqual(1, functionTemplates.Count);
                Assert.IsTrue(mSession.Redo(user, out error));
                Assert.AreEqual(0, functionTemplates.Count);
            });
        }

        [TestMethod]
        public void TestFunctionTemplateSave()
        {
            TestHelper.RunInModelSystemContext("TestFunctionTemplateSave", (user, pSession, mSession) =>
             {
                 CommandError error = null;
                 var ms = mSession.ModelSystem;
                 var name = "FunctionTemplateName";
                 var functionTemplates = ms.GlobalBoundary.FunctionTemplates;
                 Assert.AreEqual(0, functionTemplates.Count);
                 Assert.IsTrue(mSession.AddFunctionTemplate(user, ms.GlobalBoundary, name, out FunctionTemplate template, out error), error?.Message);
                 Assert.AreEqual(1, functionTemplates.Count);
                 Assert.IsTrue(mSession.Save(out error), error?.Message);
             }, (user, pSession, mSession)=>
             {
                 var ms = mSession.ModelSystem;
                 var functionTemplates = ms.GlobalBoundary.FunctionTemplates;
                 Assert.AreEqual(1, functionTemplates.Count, "The function template was not saved!");
             });
        }
    }
}
