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

namespace TestXTMF.Editing
{
    [TestClass]
    public class TestBoundaries
    {
        [TestMethod]
        public void TestSettingBoundaryName()
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
                    var newBoundaryName = "NewBoundaryName";
                    var oldName = ms.GlobalBoundary.Name;
                    Assert.IsTrue(msSession.SetBoundaryName(localUser, ms.GlobalBoundary, newBoundaryName, ref error), error);
                    Assert.AreEqual(newBoundaryName, ms.GlobalBoundary.Name);
                    Assert.IsTrue(msSession.Undo(ref error), error);
                    Assert.AreEqual(oldName, ms.GlobalBoundary.Name);
                    Assert.IsTrue(msSession.Redo(ref error), error);
                    Assert.AreEqual(newBoundaryName, ms.GlobalBoundary.Name);
                }), "Unable to get a model system editing session!");
            }), "Unable to create project");
        }

        [TestMethod]
        public void TestSettingBoundaryDescription()
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
                    var newBoundaryDescription = "NewBoundaryDescription";
                    var oldName = ms.GlobalBoundary.Description;
                    Assert.IsTrue(msSession.SetBoundaryDescription(localUser, ms.GlobalBoundary, newBoundaryDescription, ref error), error);
                    Assert.AreEqual(newBoundaryDescription, ms.GlobalBoundary.Description);
                    Assert.IsTrue(msSession.Undo(ref error), error);
                    Assert.AreEqual(oldName, ms.GlobalBoundary.Description);
                    Assert.IsTrue(msSession.Redo(ref error), error);
                    Assert.AreEqual(newBoundaryDescription, ms.GlobalBoundary.Description);
                }), "Unable to get a model system editing session");
            }), "Unable to create project");
        }

        [TestMethod]
        public void TestSettingBoundaryPersistence()
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var controller = runtime.ProjectController;
            string error = null;
            var localUser = runtime.UserController.Users[0];
            const string projectName = "Test";
            const string modelSystemName = "TestMS";
            controller.DeleteProject(localUser, projectName, ref error);
            var newBoundaryDescription = "NewBoundaryDescription";
            var newBoundaryName = "NewBoundaryName";
            // first pass
            {
                Assert.IsTrue(controller.CreateNewProject(localUser, projectName, out ProjectSession session, ref error).UsingIf(session, () =>
                {
                    var project = session.Project;
                    Assert.IsTrue(session.CreateNewModelSystem(modelSystemName, out var modelSystem, ref error), error);
                    Assert.IsTrue(session.EditModelSystem(localUser, modelSystem, out var msSession, ref error).UsingIf(msSession, () =>
                        {
                            var ms = msSession.ModelSystem;
                            Assert.IsTrue(msSession.SetBoundaryName(localUser, ms.GlobalBoundary, newBoundaryName, ref error), error);
                            Assert.IsTrue(msSession.SetBoundaryDescription(localUser, ms.GlobalBoundary, newBoundaryDescription, ref error), error);
                            Assert.IsTrue(msSession.Save(ref error), error);
                            Assert.IsTrue(session.Save(ref error), error);
                        }), error);

                }), "Unable to create project");
            }
            // second pass
            {
                Assert.IsTrue(controller.GetProject(localUser, projectName, out var project, ref error));
                Assert.IsTrue(controller.GetProjectSession(localUser, project, out var session, ref error).UsingIf(session, () =>
                {
                    Assert.IsTrue(session.GetModelSystemHeader(localUser, modelSystemName, out var modelSystem, ref error), error);
                    Assert.IsTrue(session.EditModelSystem(localUser, modelSystem, out var msSession, ref error).UsingIf(msSession, () =>
                    {
                        var ms = msSession.ModelSystem;
                        Assert.AreEqual(newBoundaryName, ms.GlobalBoundary.Name);
                        Assert.AreEqual(newBoundaryDescription, ms.GlobalBoundary.Description);
                    }), "Unable to get a model system editing session.");
                }), "Unable to get a project editing session.");
            }
        }
    }
}
