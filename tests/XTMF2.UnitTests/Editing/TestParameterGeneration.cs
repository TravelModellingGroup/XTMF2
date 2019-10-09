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
    /// <summary>
    /// The test class is designed to test the automatic generation and removal of parameters
    /// when editing a model system.
    /// </summary>
    [TestClass]
    public class TestParameterGeneration
    {
        /// <summary>
        /// Test the case where we want to add a node and all parameters filled in automatically.
        /// </summary>
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
                    var links = gBound.Links;
                    Assert.AreEqual(2, modules.Count, "It seems that the child parameter was not contained in the global boundary.");
                    Assert.AreEqual(1, links.Count, "We did not have a link!");
                    // Find the automatically added basic parameter and make sure that it has the correct default value
                    bool found = false;
                    for (int i = 0; i < modules.Count; i++)
                    {
                        if (modules[i].Name == "Real Function")
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

        /// <summary>
        /// Test the case where we want to add a node and all parameters and in addition
        /// test that the undo and redo functionality removed and rebuilds the state properly.
        /// </summary>
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
                    var links = gBound.Links;
                    Assert.AreEqual(2, modules.Count, "It seems that the child parameter was not contained in the global boundary.");
                    Assert.AreEqual(1, links.Count, "We did not have a link!");
                    Assert.IsTrue(msSession.Undo(localUser, ref error), error);
                    Assert.AreEqual(0, modules.Count, "After undoing it seems that a module has survived.");
                    Assert.AreEqual(0, links.Count, "The link was not removed on undo.");
                    Assert.IsTrue(msSession.Redo(localUser, ref error), error);
                    Assert.AreEqual(2, modules.Count, "After redoing it seems that a module was not restored.");
                    Assert.AreEqual(1, links.Count, "The link was not re-added on redo.");
                }), "Unable to get a model system editing session!");
            }), "Unable to create project");
        }

        /// <summary>
        /// Test the case where we want to remove a node and all simple parameters nodes and links connecting them.
        /// </summary>
        [TestMethod]
        public void TestRemoveNodeWithParameterGeneration()
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
                    var links = gBound.Links;
                    Assert.AreEqual(2, modules.Count, "It seems that the child parameter was not contained in the global boundary.");
                    Assert.AreEqual(1, links.Count, "We did not have a link!");
                    // Find the automatically added basic parameter and make sure that it has the correct default value
                    bool found = false;
                    for (int i = 0; i < modules.Count; i++)
                    {
                        if (modules[i].Name == "Real Function")
                        {
                            found = true;
                            Assert.AreEqual(typeof(BasicParameter<string>), modules[i].Type, "The automatically generated parameter was not of type BasicParameter<string>!");
                            Assert.AreEqual("Hello World", modules[i].ParameterValue, "The default value of the parameter was not 'Hello World'!");
                            break;
                        }
                    }
                    Assert.IsTrue(found, "We did not find the automatically created parameter module!");

                    Assert.IsTrue(msSession.RemoveNodeGenerateParameters(localUser, node, ref error), error);

                    // Make sure that both modules were deleted
                    Assert.AreEqual(0, modules.Count, "Both modules were not removed.");
                    Assert.IsTrue(msSession.Undo(localUser, ref error), error);
                    Assert.AreEqual(2, modules.Count, "Both modules were not re-added.");

                    Assert.IsTrue(msSession.Redo(localUser, ref error), error);
                    Assert.AreEqual(0, modules.Count, "Both modules were not removed again.");

                }), "Unable to get a model system editing session!");
            }), "Unable to create project");
        }

        /// <summary>
        /// Test the case where we want to remove a node and all simple parameters nodes and links connecting them
        /// where the basic parameter has been made complex by adding another node that references the first node's
        /// parameter.
        /// </summary>
        [TestMethod]
        public void TestRemoveNodeWithParameterGenerationNotRemovingIfMultiple()
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
                    Assert.IsTrue(msSession.AddNode(localUser, ms.GlobalBoundary, "Test",
                        typeof(SimpleParameterModule), out var node2, ref error), error);
                    // Test to make sure that there was a second module also added.
                    Assert.IsNotNull(children, "The child parameters of the node were returned as a null!");
                    Assert.AreEqual(1, children.Count);
                    var modules = gBound.Modules;
                    var links = gBound.Links;
                    Assert.AreEqual(1, links.Count);
                    Assert.AreEqual(3, modules.Count);
                    Assert.IsTrue(msSession.AddLink(localUser, node2, node2.Hooks[0], children[0], out var node2Link, ref error), error);
                    Assert.AreEqual(2, links.Count, "The second link was not added");
                    Assert.IsTrue(msSession.RemoveNodeGenerateParameters(localUser, node, ref error), error);
                    Assert.AreEqual(1, links.Count);
                    Assert.AreEqual(2, modules.Count);
                    Assert.IsTrue(msSession.Undo(localUser, ref error), error);
                    Assert.AreEqual(2, links.Count);
                    Assert.AreEqual(3, modules.Count);
                    Assert.IsTrue(msSession.Redo(localUser, ref error), error);
                    Assert.AreEqual(1, links.Count);
                    Assert.AreEqual(2, modules.Count);


                }), "Unable to get a model system editing session!");
            }), "Unable to create project");
        }
    }
}
