/*
    Copyright 2019 University of Toronto

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
using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2;
using XTMF2.Editing;
using XTMF2.Controllers;
using System.Linq;
using XTMF2.ModelSystemConstruct;
using static XTMF2.Helper;
using TestXTMF.Modules;
using XTMF2.RuntimeModules;
using static TestXTMF.TestHelper;
using System.IO;
using System.Threading;

namespace TestXTMF2.Editing
{
    [TestClass]
    public class TestParameterGeneration
    {
        [TestMethod]
        public void TestAddNodeWithParameterGeneration()
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var controller = runtime.ProjectController;
            string error = null;
            var localUser = runtime.UserController.Users[0];
            controller.DeleteProject(localUser, "Test", ref error);
            Assert.IsTrue(controller.CreateNewProject(localUser, "Test", out ProjectSession session, ref error).UsingIf(session, () =>
            {
                var project = session.Project;
                Assert.IsTrue(session.CreateNewModelSystem("TestMS", out var modelSystem, ref error), error);
                Assert.IsTrue(session.EditModelSystem(localUser, modelSystem, out var msSession, ref error).UsingIf(msSession, () =>
                {
                    var ms = msSession.ModelSystem;
                    var gBound = ms.GlobalBoundary;
                    Assert.IsTrue(msSession.AddNodeGenerateParameters(localUser, ms.GlobalBoundary, "Test", 
                        typeof(SimpleParameterModule), out var node, out var children, ref error), error);
                    // Test to make sure that there was a second module also added.
                    Assert.IsNotNull(children, "The child parameters of the node were returned as a null!");
                    Assert.AreEqual(1, children.Count);
                    var modules = gBound.Modules;
                    Assert.AreEqual(2, modules.Count, "It seems that the child parameter was not contained in the global boundary.");
                    // Find the automatically added basic parameter and make sure that it has the correct default value
                    bool found = false;
                    for (int i = 0; i < modules.Count; i++)
                    {
                        if(modules[i].Name == "Real Function")
                        {
                            found = true;
                            Assert.AreEqual(typeof(BasicParameter<string>), modules[i].Type, "The automatically generated parameter was not of type BasicParameter<string>!");
                            Assert.AreEqual("Hello World", modules[i].ParameterValue, "The default value of the parameter was not 'Hello World'!");
                            break;
                        }
                    }
                    Assert.IsTrue(found, "We did not find the automatically created parameter module!");
                }), "Unable to get a model system editing session!");
            }), "Unable to create project");
        }

        [TestMethod]
        public void TestAddNodeWithParameterGenerationUndo()
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var controller = runtime.ProjectController;
            string error = null;
            var localUser = runtime.UserController.Users[0];
            controller.DeleteProject(localUser, "Test", ref error);
            Assert.IsTrue(controller.CreateNewProject(localUser, "Test", out ProjectSession session, ref error).UsingIf(session, () =>
            {
                var project = session.Project;
                Assert.IsTrue(session.CreateNewModelSystem("TestMS", out var modelSystem, ref error), error);
                Assert.IsTrue(session.EditModelSystem(localUser, modelSystem, out var msSession, ref error).UsingIf(msSession, () =>
                {
                    var ms = msSession.ModelSystem;
                    var gBound = ms.GlobalBoundary;
                    Assert.IsTrue(msSession.AddNodeGenerateParameters(localUser, ms.GlobalBoundary, "Test",
                        typeof(SimpleParameterModule), out var node, out var children, ref error), error);
                    // Test to make sure that there was a second module also added.
                    Assert.IsNotNull(children, "The child parameters of the node were returned as a null!");
                    Assert.AreEqual(1, children.Count);
                    var modules = gBound.Modules;
                    Assert.AreEqual(2, modules.Count, "It seems that the child parameter was not contained in the global boundary.");
                    Assert.IsTrue(msSession.Undo(ref error), error);
                    Assert.AreEqual(0, modules.Count, "After undoing it seems that a module has survived.");
                    Assert.IsTrue(msSession.Redo(ref error), error);
                    Assert.AreEqual(2, modules.Count, "After redoing it seems that a module was not restored.");
                }), "Unable to get a model system editing session!");
            }), "Unable to create project");
        }
    }
}
