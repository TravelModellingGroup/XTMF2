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
using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2;
using XTMF2.Editing;
using XTMF2.Controller;

namespace TestXTMF
{
    [TestClass]
    public class TestProjects
    {
        [TestInitialize]
        public void Setup()
        {
            // hide the startup cost of XTMF
            XTMFRuntime runtime = XTMFRuntime.CreateRuntime();
        }

        [TestMethod]
        public void CreateNewProject()
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var controller = runtime.ProjectController;
            string error = null;
            var localUser = runtime.UserController.Users[0];
            if(controller.CreateNewProject(localUser, "Test", out ProjectSession session, ref error))
            {
                using (session)
                {
                    var project = session.Project;
                    Assert.AreEqual("Test", project.Name);
                    Assert.AreEqual(localUser, project.Owner);
                }
            }
            else
            {
                Assert.Fail("Unable to create project");
            }
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
            var localUser = runtime.UserController.Users[0];
            // delete the project just in case it survived
            controller.DeleteProject(localUser, projectName, ref error);
            // now create it
            if (controller.CreateNewProject(localUser, projectName, out ProjectSession session, ref error))
            {
                using (session)
                {
                    var project = session.Project;
                    Assert.AreEqual(projectName, project.Name);
                    Assert.AreEqual(localUser, project.Owner);
                }
            }
            else
            {
                Assert.Fail("Unable to create project");
            }
            var numberOfProjects = localUser.AvailableProjects.Count;
            // Simulate a shutdown of XTMF
            runtime.Shutdown();
            //Startup XTMF again
            runtime = XTMFRuntime.CreateRuntime();
            controller = runtime.ProjectController;
            localUser = runtime.UserController.Users[0];
            Assert.AreEqual(numberOfProjects, localUser.AvailableProjects.Count);
            var regainedProject = localUser.AvailableProjects[0];
            Assert.AreEqual(projectName, regainedProject.Name);
        }
    }
}
