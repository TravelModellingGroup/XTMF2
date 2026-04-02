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
                Assert.IsEmpty(functionTemplates);
                Assert.IsTrue(mSession.AddFunctionTemplate(user, ms.GlobalBoundary, name, out FunctionTemplate template, out error), error?.Message);
                Assert.HasCount(1, functionTemplates);
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
                Assert.IsEmpty(functionTemplates);
                Assert.IsTrue(mSession.AddFunctionTemplate(user, ms.GlobalBoundary, name, out FunctionTemplate template, out error), error?.Message);
                Assert.HasCount(1, functionTemplates);
                Assert.IsTrue(mSession.Undo(user, out error));
                Assert.IsEmpty(functionTemplates);
                Assert.IsTrue(mSession.Redo(user, out error));
                Assert.HasCount(1, functionTemplates);
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
                Assert.IsEmpty(functionTemplates);
                Assert.IsTrue(mSession.AddFunctionTemplate(user, ms.GlobalBoundary, name, out FunctionTemplate template, out error), error?.Message);
                Assert.HasCount(1, functionTemplates);
                Assert.IsTrue(mSession.RemoveFunctionTemplate(user, ms.GlobalBoundary, template, out error), error?.Message);
                Assert.IsEmpty(functionTemplates);
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
                Assert.IsEmpty(functionTemplates);
                Assert.IsTrue(mSession.AddFunctionTemplate(user, ms.GlobalBoundary, name, out FunctionTemplate template, out error), error?.Message);
                Assert.HasCount(1, functionTemplates);
                Assert.IsTrue(mSession.RemoveFunctionTemplate(user, ms.GlobalBoundary, template, out error), error?.Message);
                Assert.IsEmpty(functionTemplates);
                Assert.IsTrue(mSession.Undo(user, out error));
                Assert.HasCount(1, functionTemplates);
                Assert.IsTrue(mSession.Redo(user, out error));
                Assert.IsEmpty(functionTemplates);
            });
        }

        [TestMethod]
        public void TestRemoveExposedNodeFromFunctionTemplate()
        {
            TestHelper.RunInModelSystemContext("TestRemoveExposedNodeFromFunctionTemplate", (user, pSession, mSession) =>
            {
                CommandError error = null;
                var ms = mSession.ModelSystem;
                Assert.IsTrue(mSession.AddFunctionTemplate(user, ms.GlobalBoundary, "MyFT", out FunctionTemplate template, out error), error?.Message);
                Assert.IsTrue(mSession.AddNode(user, template.InternalModules, "MyParam",
                    typeof(XTMF2.RuntimeModules.BasicParameter<string>), Rectangle.Hidden,
                    out var node, out error), error?.Message);
                Assert.IsTrue(mSession.ToggleFunctionTemplateExposedNode(user, template, node, out error), error?.Message);
                Assert.HasCount(1, template.ExposedNodes, "Node should be exposed before deletion.");

                Assert.IsTrue(mSession.RemoveNode(user, node, out error), error?.Message);
                Assert.IsEmpty(template.ExposedNodes, "Deleting an exposed node must also remove it from ExposedNodes.");
            });
        }

        [TestMethod]
        public void TestRemoveExposedNodeFromFunctionTemplateUndoRedo()
        {
            TestHelper.RunInModelSystemContext("TestRemoveExposedNodeFromFunctionTemplateUndoRedo", (user, pSession, mSession) =>
            {
                CommandError error = null;
                var ms = mSession.ModelSystem;
                Assert.IsTrue(mSession.AddFunctionTemplate(user, ms.GlobalBoundary, "MyFT", out FunctionTemplate template, out error), error?.Message);
                Assert.IsTrue(mSession.AddNode(user, template.InternalModules, "MyParam",
                    typeof(XTMF2.RuntimeModules.BasicParameter<string>), Rectangle.Hidden,
                    out var node, out error), error?.Message);
                Assert.IsTrue(mSession.ToggleFunctionTemplateExposedNode(user, template, node, out error), error?.Message);
                Assert.HasCount(1, template.ExposedNodes, "Node should be exposed before deletion.");

                Assert.IsTrue(mSession.RemoveNode(user, node, out error), error?.Message);
                Assert.IsEmpty(template.ExposedNodes, "Deleting an exposed node must also remove it from ExposedNodes.");

                // Undo: node comes back and should be re-exposed.
                Assert.IsTrue(mSession.Undo(user, out error), error?.Message);
                Assert.HasCount(1, template.ExposedNodes, "Undo should restore the node to ExposedNodes.");

                // Redo: node is deleted again, exposed list should be empty again.
                Assert.IsTrue(mSession.Redo(user, out error), error?.Message);
                Assert.IsEmpty(template.ExposedNodes, "Redo should re-remove the node from ExposedNodes.");
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
                 Assert.IsEmpty(functionTemplates);
                 Assert.IsTrue(mSession.AddFunctionTemplate(user, ms.GlobalBoundary, name, out FunctionTemplate template, out error), error?.Message);
                 Assert.HasCount(1, functionTemplates);
                 Assert.IsTrue(mSession.Save(out error), error?.Message);
             }, (user, pSession, mSession)=>
             {
                 var ms = mSession.ModelSystem;
                 var functionTemplates = ms.GlobalBoundary.FunctionTemplates;
                 Assert.HasCount(1, functionTemplates, "The function template was not saved!");
             });
        }
    }
}
