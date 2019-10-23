/*
    Copyright 2017 University of Toronto

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
using System.IO;
using TestXTMF;
using TestXTMF.Modules;
using XTMF2.Editing;

namespace XTMF2
{
    [TestClass]
    public class TestProjects
    {
        [TestMethod]
        public void CreateNewProject()
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var controller = runtime.ProjectController;
            string error = null;
            var localUser = TestHelper.GetTestUser(runtime);
            // delete the project in case it has survived.
            controller.DeleteProject(localUser, "Test", ref error);
            Assert.IsTrue(controller.CreateNewProject(localUser, "Test", out ProjectSession session, ref error).UsingIf(session, () =>
            {
                var project = session.Project;
                Assert.AreEqual("Test", project.Name);
                Assert.AreEqual(localUser, project.Owner);
            }), "Unable to create project");
            Assert.IsTrue(controller.DeleteProject(localUser, "Test", ref error));
            Assert.IsFalse(controller.DeleteProject(localUser, "Test", ref error));
        }

        [TestMethod]
        public void ProjectPersistance()
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var controller = runtime.ProjectController;
            string projectName = "Test";
            string error = null;
            var localUser = TestHelper.GetTestUser(runtime);
            // delete the project just in case it survived
            controller.DeleteProject(localUser, projectName, ref error);
            // now create it
            Assert.IsTrue(controller.CreateNewProject(localUser, projectName, out ProjectSession session, ref error).UsingIf(session, () =>
            {
                var project = session.Project;
                Assert.AreEqual(projectName, project.Name);
                Assert.AreEqual(localUser, project.Owner);
            }), "Unable to create project");
            var numberOfProjects = localUser.AvailableProjects.Count;
            // Simulate a shutdown of XTMF
            runtime.Shutdown();
            //Startup XTMF again
            runtime = XTMFRuntime.CreateRuntime();
            controller = runtime.ProjectController;
            localUser = TestHelper.GetTestUser(runtime);
            Assert.AreEqual(numberOfProjects, localUser.AvailableProjects.Count);
            var regainedProject = localUser.AvailableProjects[0];
            Assert.AreEqual(projectName, regainedProject.Name);
        }

        [TestMethod]
        public void EnsureSameProjectSession()
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var controller = runtime.ProjectController;
            string error = null;
            var localUser = TestHelper.GetTestUser(runtime);
            runtime.UserController.Delete("NewUser");
            Assert.IsTrue(runtime.UserController.CreateNew("NewUser", false, out var newUser, ref error), error);
            // delete the project in case it has survived.
            controller.DeleteProject(localUser, "Test", ref error);
            Assert.IsTrue(controller.CreateNewProject(localUser, "Test", out ProjectSession session, ref error).UsingIf(session, () =>
            {
                var project = session.Project;
                Assert.IsTrue(session.ShareWith(localUser, newUser, ref error), error);
                Assert.IsTrue(controller.GetProjectSession(newUser, project, out var session2, ref error).UsingIf(session2, () =>
                {
                    Assert.AreSame(session, session2);
                }), error);
            }), "Unable to create project");

            // cleanup
            Assert.IsTrue(controller.DeleteProject(localUser, "Test", ref error));
        }

        [TestMethod]
        public void EnsureDifferentProjectSession()
        {
            /* When a project session is closed it should be disposed of.
             * A subsequent request for a project session to the same project should
             * be a new object.
             */
            var runtime = XTMFRuntime.CreateRuntime();
            var controller = runtime.ProjectController;
            string error = null;
            var localUser = TestHelper.GetTestUser(runtime);
            runtime.UserController.Delete("NewUser");
            Assert.IsTrue(runtime.UserController.CreateNew("NewUser", false, out var newUser, ref error), error);
            // delete the project in case it has survived.
            controller.DeleteProject(localUser, "Test", ref error);
            Project project = null;
            Assert.IsTrue(controller.CreateNewProject(localUser, "Test", out ProjectSession session, ref error).UsingIf(session, () =>
            {
                project = session.Project;
                Assert.IsTrue(session.ShareWith(localUser, newUser, ref error), error);

            }), "Unable to create project");
            Assert.IsTrue(controller.GetProjectSession(newUser, project, out var session2, ref error).UsingIf(session2, () =>
            {
                Assert.AreNotSame(session, session2);
            }), error);

            // cleanup
            Assert.IsTrue(controller.DeleteProject(localUser, "Test", ref error));
        }

        private static void CreateModelSystem(User user, ProjectSession project, string msName, string description, Action executeBeforeSessionClosed)
        {
            string error = null;
            var startName = "MyStart";
            var nodeName = "MyNode";
            Assert.IsTrue(project.CreateNewModelSystem(msName, out var msHeader, ref error), error);
            Assert.IsTrue(msHeader.SetDescription(project, description, ref error), error);
            Assert.IsTrue(project.EditModelSystem(user, msHeader, out var session, ref error), error);
            using (session)
            {
                var ms = session.ModelSystem;
                Assert.IsTrue(session.AddModelSystemStart(user, ms.GlobalBoundary, startName, out var start, ref error), error);
                Assert.IsTrue(session.AddNode(user, ms.GlobalBoundary, nodeName, typeof(IgnoreResult<string>), out var node, ref error), error);
                Assert.IsTrue(session.AddLink(user, start, TestHelper.GetHook(start.Hooks, "ToExecute"), node, out var link, ref error), error);
                Assert.IsTrue(session.Save(ref error), error);
                if (!(executeBeforeSessionClosed is null))
                {
                    executeBeforeSessionClosed();
                }
            }
        }

        [TestMethod]
        public void ExportProjectNoModelSystems()
        {
            TestHelper.RunInProjectContext("ExportProjectNoModelSystems", (user, project) =>
            {
                string error = null;
                var tempFile = new FileInfo(Path.GetTempFileName());
                try
                {
                    // Make sure the file does not exist before starting.
                    if (tempFile.Exists)
                    {
                        tempFile.Delete();
                    }
                    Assert.IsTrue(project.ExportProject(user, tempFile.FullName, ref error), error);
                    Assert.IsTrue(tempFile.Exists, "The exported file does not exist!");
                }
                finally
                {
                    tempFile.Refresh();
                    if (tempFile.Exists)
                    {
                        tempFile.Delete();
                    }
                }
            });
        }

        [TestMethod]
        public void ExportProjectSingleModelSystem()
        {
            TestHelper.RunInProjectContext("ExportProjectSingleModelSystem", (user, project) =>
            {
                string error = null;
                var msName = "MSToExport";
                var description = "A test model system.";
                var tempFile = new FileInfo(Path.GetTempFileName());
                try
                {
                    // Make sure the file does not exist before starting.
                    if (tempFile.Exists)
                    {
                        tempFile.Delete();
                    }
                    CreateModelSystem(user, project, msName, description, () =>
                    {
                        Assert.IsFalse(project.ExportProject(user, tempFile.FullName, ref error), "We were able to export a project while a model system was being edited!");
                    });
                    Assert.IsTrue(project.ExportProject(user, tempFile.FullName, ref error), error);
                    tempFile.Refresh();
                    Assert.IsTrue(tempFile.Exists, "The exported file does not exist!");
                }
                finally
                {
                    tempFile.Refresh();
                    if (tempFile.Exists)
                    {
                        tempFile.Delete();
                    }
                }
            });
        }

        [TestMethod]
        public void ExportProjectMultipleModelSystems()
        {
            TestHelper.RunInProjectContext("ExportProjectMultipleModelSystems", (user, project) =>
            {
                string error = null;
                var msName = "MSToExport";
                var description = "A test model system.";

                var tempFile = new FileInfo(Path.GetTempFileName());
                try
                {
                    // Make sure the file does not exist before starting.
                    if (tempFile.Exists)
                    {
                        tempFile.Delete();
                    }
                    for (int i = 0; i < 5; i++)
                    {
                        CreateModelSystem(user, project, msName + i, description + i, null);
                    }
                    Assert.IsTrue(project.ExportProject(user, tempFile.FullName, ref error), error);
                    Assert.IsTrue(tempFile.Exists, "The exported file does not exist!");
                }
                finally
                {
                    tempFile.Refresh();
                    if (tempFile.Exists)
                    {
                        tempFile.Delete();
                    }
                }
            });
        }
    }
}
