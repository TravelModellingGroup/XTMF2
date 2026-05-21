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
using XTMF2.RuntimeModules;

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
        public void TestRemoveFunctionParameterFromFunctionTemplate()
        {
            TestHelper.RunInModelSystemContext("TestRemoveFunctionParameterFromFunctionTemplate", (user, pSession, mSession) =>
            {
                CommandError error = null;
                var ms = mSession.ModelSystem;
                Assert.IsTrue(mSession.AddFunctionTemplate(user, ms.GlobalBoundary, "MyFT", out FunctionTemplate template, out error), error?.Message);
                Assert.IsTrue(mSession.AddFunctionParameter(user, template, "MyParam",
                    typeof(XTMF2.IModule), Rectangle.Hidden,
                    out var fp, out error), error?.Message);
                Assert.HasCount(1, template.FunctionParameters, "Parameter should exist before removal.");

                Assert.IsTrue(mSession.RemoveFunctionParameter(user, template, fp, out error), error?.Message);
                Assert.IsEmpty(template.FunctionParameters, "FunctionParameters should be empty after removal.");
            });
        }

        [TestMethod]
        public void TestRemoveFunctionParameterFromFunctionTemplateUndoRedo()
        {
            TestHelper.RunInModelSystemContext("TestRemoveFunctionParameterFromFunctionTemplateUndoRedo", (user, pSession, mSession) =>
            {
                CommandError error = null;
                var ms = mSession.ModelSystem;
                Assert.IsTrue(mSession.AddFunctionTemplate(user, ms.GlobalBoundary, "MyFT", out FunctionTemplate template, out error), error?.Message);
                Assert.IsTrue(mSession.AddFunctionParameter(user, template, "MyParam",
                    typeof(XTMF2.IModule), Rectangle.Hidden,
                    out var fp, out error), error?.Message);
                Assert.HasCount(1, template.FunctionParameters, "Parameter should exist before removal.");

                Assert.IsTrue(mSession.RemoveFunctionParameter(user, template, fp, out error), error?.Message);
                Assert.IsEmpty(template.FunctionParameters, "FunctionParameters should be empty after removal.");

                // Undo: parameter comes back.
                Assert.IsTrue(mSession.Undo(user, out error), error?.Message);
                Assert.HasCount(1, template.FunctionParameters, "Undo should restore the FunctionParameter.");

                // Redo: parameter is removed again.
                Assert.IsTrue(mSession.Redo(user, out error), error?.Message);
                Assert.IsEmpty(template.FunctionParameters, "Redo should re-remove the FunctionParameter.");
            });
        }

        [TestMethod]
        public void TestRemoveFunctionTemplate_WithReferencingInstance_Fails()
        {
            TestHelper.RunInModelSystemContext(nameof(TestRemoveFunctionTemplate_WithReferencingInstance_Fails),
            (user, pSession, mSession) =>
            {
                CommandError error = null;
                var ms     = mSession.ModelSystem;
                var gb     = ms.GlobalBoundary;

                Assert.IsTrue(mSession.AddFunctionTemplate(user, gb, "MyTemplate",
                    out var template, out error), error?.Message);

                // Place an instance that references the template.
                Assert.IsTrue(mSession.AddFunctionInstance(user, gb, template, "MyInstance",
                    new Rectangle(10f, 10f, 160f, 70f), out _, out error), error?.Message);

                // Removing the template while the instance still exists must be rejected.
                var ok = mSession.RemoveFunctionTemplate(user, gb, template, out error);
                Assert.IsFalse(ok,
                    "RemoveFunctionTemplate should fail when a FunctionInstance still references it.");
                Assert.IsNotNull(error);
                Assert.HasCount(1, gb.FunctionTemplates,
                    "The template must still be present after the failed removal.");
            });
        }

        [TestMethod]
        public void TestRemoveFunctionTemplate_AfterInstanceRemoved_Succeeds()
        {
            TestHelper.RunInModelSystemContext(nameof(TestRemoveFunctionTemplate_AfterInstanceRemoved_Succeeds),
            (user, pSession, mSession) =>
            {
                CommandError error = null;
                var ms     = mSession.ModelSystem;
                var gb     = ms.GlobalBoundary;

                Assert.IsTrue(mSession.AddFunctionTemplate(user, gb, "MyTemplate",
                    out var template, out error), error?.Message);
                Assert.IsTrue(mSession.AddFunctionInstance(user, gb, template, "MyInstance",
                    new Rectangle(10f, 10f, 160f, 70f), out var instance, out error), error?.Message);

                // Remove the instance first…
                Assert.IsTrue(mSession.RemoveFunctionInstance(user, instance, out error), error?.Message);

                // …now the template removal should succeed.
                Assert.IsTrue(mSession.RemoveFunctionTemplate(user, gb, template, out error), error?.Message);
                Assert.IsEmpty(gb.FunctionTemplates);
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

        [TestMethod]
        public void DisablingLocalVariableIsRejected()
        {
            TestHelper.RunInModelSystemContext(nameof(DisablingLocalVariableIsRejected), (user, pSession, mSession) =>
            {
                CommandError error = null;
                var ms = mSession.ModelSystem;
                var gb = ms.GlobalBoundary;

                Assert.IsTrue(mSession.AddFunctionTemplate(user, gb, "MyTemplate", out var template, out error), error?.Message);
                Assert.IsTrue(mSession.AddNode(user, template.InternalModules, "LocalVar", typeof(BasicParameter<int>), Rectangle.Hidden,
                    out var localVarNode, out error), error?.Message);
                Assert.IsTrue(mSession.AddFunctionTemplateVariable(user, template, localVarNode, out error), error?.Message);

                Assert.IsFalse(mSession.SetNodeDisabled(user, localVarNode, true, out error),
                    "Disabling a local variable should fail.");
                Assert.IsNotNull(error);
                Assert.IsFalse(localVarNode.IsDisabled);
            });
        }

        [TestMethod]
        public void FunctionInstanceDisabled_PersistsAcrossSaveLoad()
        {
            TestHelper.RunInModelSystemContext(nameof(FunctionInstanceDisabled_PersistsAcrossSaveLoad), (user, pSession, mSession) =>
            {
                CommandError error = null;
                var ms = mSession.ModelSystem;
                var gb = ms.GlobalBoundary;

                Assert.IsTrue(mSession.AddFunctionTemplate(user, gb, "MyTemplate", out var template, out error), error?.Message);
                Assert.IsTrue(mSession.AddFunctionInstance(user, gb, template, "MyInstance",
                    new Rectangle(10f, 10f, 160f, 70f), out var instance, out error), error?.Message);

                Assert.IsTrue(mSession.SetNodeDisabled(user, instance, true, out error), error?.Message);
                Assert.IsTrue(instance.IsDisabled, "FunctionInstance should be disabled before save.");
            }, (user, pSession, mSession) =>
            {
                var fi = mSession.ModelSystem.GlobalBoundary.FunctionInstances.Single();
                Assert.IsTrue(fi.IsDisabled, "FunctionInstance disabled state should persist after reload.");
            });
        }
    }
}
