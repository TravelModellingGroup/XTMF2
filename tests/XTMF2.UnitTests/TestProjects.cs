﻿/*
    Copyright 2017-2020 University of Toronto

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
using System.Linq;
using XTMF2.Editing;
using XTMF2.RuntimeModules;

namespace XTMF2.UnitTests
{
    [TestClass]
    public class TestProjects
    {
        [TestMethod]
        public void CreateNewProject()
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var controller = runtime.ProjectController;
            CommandError error = null;
            var localUser = TestHelper.GetTestUser(runtime);
            // delete the project in case it has survived.
            controller.DeleteProject(localUser, "Test", out error);
            Assert.IsTrue(controller.CreateNewProject(localUser, "Test", out ProjectSession session, out error).UsingIf(session, () =>
            {
                var project = session.Project;
                Assert.AreEqual("Test", project.Name);
                Assert.AreEqual(localUser, project.Owner);
            }), "Unable to create project");
            Assert.IsTrue(controller.DeleteProject(localUser, "Test", out error));
            Assert.IsFalse(controller.DeleteProject(localUser, "Test", out error));
        }

        [TestMethod]
        public void CreateNewOrGet()
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var controller = runtime.ProjectController;
            CommandError error = null;
            var localUser = TestHelper.GetTestUser(runtime);
            // delete the project in case it has survived.
            controller.DeleteProject(localUser, "Test", out error);
            Assert.IsTrue(controller.CreateNewOrGet(localUser, "Test", out var session, out error).UsingIf(session, () =>
            {
                var project = session.Project;
                Assert.AreEqual("Test", project.Name);
                Assert.AreEqual(localUser, project.Owner);
            }), "Unable to create project");

            Assert.IsTrue(controller.CreateNewOrGet(localUser, "Test", out session, out error).UsingIf(session, () =>
            {

            }), "Unable to get the project the second time.");
            Assert.IsTrue(controller.DeleteProject(localUser, "Test", out error));
            Assert.IsFalse(controller.DeleteProject(localUser, "Test", out error));
        }

        [TestMethod]
        public void RenameProject()
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var controller = runtime.ProjectController;
            CommandError error = null;
            var localUser = TestHelper.GetTestUser(runtime);
            // delete the project in case it has survived.
            controller.DeleteProject(localUser, "Test", out error);
            Assert.IsTrue(controller.CreateNewProject(localUser, "Test", out ProjectSession session, out error).UsingIf(session, () =>
            {
                var project = session.Project;
                Assert.AreEqual("Test", project.Name);
                Assert.AreEqual(localUser, project.Owner);
                Assert.IsFalse(controller.RenameProject(localUser, project, "RenamedTestProject", out error),
                    "RenameProject succeeded even through it was currently being edited!");
            }), "Unable to create project");
            Assert.IsTrue(controller.GetProject(localUser, "Test", out var project, out error), error?.Message);
            Assert.IsTrue(controller.RenameProject(localUser, project, "RenamedTestProject", out error), error?.Message);
            Assert.IsTrue(controller.DeleteProject(localUser, project, out error), "Failed to cleanup the project.");
        }

        [TestMethod]
        public void ProjectPersistance()
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var controller = runtime.ProjectController;
            const string projectName = "Test";
            CommandError error = null;
            var localUser = TestHelper.GetTestUser(runtime);
            // delete the project just in case it survived
            controller.DeleteProject(localUser, projectName, out error);
            // now create it
            Assert.IsTrue(controller.CreateNewProject(localUser, projectName, out ProjectSession session, out error).UsingIf(session, () =>
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
            CommandError error = null;
            var localUser = TestHelper.GetTestUser(runtime);
            runtime.UserController.Delete("NewUser");
            Assert.IsTrue(runtime.UserController.CreateNew("NewUser", false, out var newUser, out error), error?.Message);
            // delete the project in case it has survived.
            controller.DeleteProject(localUser, "Test", out error);
            Assert.IsTrue(controller.CreateNewProject(localUser, "Test", out ProjectSession session, out error).UsingIf(session, () =>
            {
                var project = session.Project;
                Assert.IsTrue(session.ShareWith(localUser, newUser, out error), error?.Message);
                Assert.IsTrue(controller.GetProjectSession(newUser, project, out var session2, out error).UsingIf(session2, () =>
                {
                    Assert.AreSame(session, session2);
                }), error?.Message);
            }), "Unable to create project");

            // cleanup
            Assert.IsTrue(controller.DeleteProject(localUser, "Test", out error));
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
            CommandError error = null;
            var localUser = TestHelper.GetTestUser(runtime);
            runtime.UserController.Delete("NewUser");
            Assert.IsTrue(runtime.UserController.CreateNew("NewUser", false, out var newUser, out error), error?.Message);
            // delete the project in case it has survived.
            controller.DeleteProject(localUser, "Test", out error);
            Project project = null;
            Assert.IsTrue(controller.CreateNewProject(localUser, "Test", out ProjectSession session, out error).UsingIf(session, () =>
            {
                project = session.Project;
                Assert.IsTrue(session.ShareWith(localUser, newUser, out error), error?.Message);
            }), "Unable to create project");
            Assert.IsTrue(controller.GetProjectSession(newUser, project, out var session2, out error).UsingIf(session2, () =>
            {
                Assert.AreNotSame(session, session2);
            }), error?.Message);

            // cleanup
            Assert.IsTrue(controller.DeleteProject(localUser, "Test", out error));
        }

        private static void CreateModelSystem(User user, ProjectSession project, string msName, string description, Action executeBeforeSessionClosed)
        {
            CommandError error;
            const string startName = "MyStart";
            const string nodeName = "MyNode";
            Assert.IsTrue(project.CreateNewModelSystem(user, msName, out var msHeader, out error), error?.Message);
            Assert.IsTrue(msHeader.SetDescription(project, description, out error), error?.Message);
            Assert.IsTrue(project.EditModelSystem(user, msHeader, out var session, out error), error?.Message);
            using (session)
            {
                var ms = session.ModelSystem;
                Assert.IsTrue(session.AddModelSystemStart(user, ms.GlobalBoundary, startName, Rectangle.Hidden, out var start, out error), error?.Message);
                Assert.IsTrue(session.AddNode(user, ms.GlobalBoundary, nodeName, typeof(IgnoreResult<string>), Rectangle.Hidden, out var node, out error), error?.Message);
                Assert.IsTrue(session.AddLink(user, start, TestHelper.GetHook(start.Hooks, "ToExecute"), node, out var link, out error), error?.Message);
                Assert.IsTrue(session.Save(out error), error?.Message);
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
                CommandError error = null;
                var tempFile = new FileInfo(Path.GetTempFileName());
                try
                {
                    // Make sure the file does not exist before starting.
                    if (tempFile.Exists)
                    {
                        tempFile.Delete();
                    }
                    Assert.IsTrue(project.ExportProject(user, tempFile.FullName, out error), error?.Message);
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
                CommandError error = null;
                const string msName = "MSToExport";
                const string description = "A test model system.";
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
                        Assert.IsFalse(project.ExportProject(user, tempFile.FullName, out error), "We were able to export a project while a model system was being edited!");
                    });
                    Assert.IsTrue(project.ExportProject(user, tempFile.FullName, out error), error?.Message);
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
                CommandError error = null;
                const string msName = "MSToExport";
                const string description = "A test model system.";

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
                    Assert.IsTrue(project.ExportProject(user, tempFile.FullName, out error), error?.Message);
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
        public void ImportProjectFileNoModelSystem()
        {
            TestHelper.RunInProjectContext("ImportProjectFileNoModelSystem", (XTMFRuntime runtime, User user, ProjectSession project) =>
            {
                CommandError error = null;
                var tempFile = new FileInfo(Path.GetTempFileName());
                try
                {
                    // Make sure the file does not exist before starting.
                    if (tempFile.Exists)
                    {
                        tempFile.Delete();
                    }
                    Assert.IsTrue(project.ExportProject(user, tempFile.FullName, out error), error?.Message);
                    Assert.IsTrue(tempFile.Exists, "The exported file does not exist!");
                    var projectController = runtime.ProjectController;
                    Assert.IsTrue(projectController.ImportProjectFile(user, "ImportProjectFileNoModelSystem-Imported", tempFile.FullName,
                        out var importedSession, out error), error?.Message);
                    Assert.IsNotNull(importedSession);
                    using (importedSession)
                    {
                        var modelSystems = importedSession.ModelSystems;
                        Assert.AreEqual(0, modelSystems.Count);
                    }
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
        public void ImportProjectFileSingleModelSystem()
        {
            TestHelper.RunInProjectContext("ImportProjectFileSingleModelSystem", (XTMFRuntime runtime, User user, ProjectSession project) =>
            {
                CommandError error = null;
                var tempFile = new FileInfo(Path.GetTempFileName());
                try
                {
                    // Make sure the file does not exist before starting.
                    if (tempFile.Exists)
                    {
                        tempFile.Delete();
                    }
                    CreateModelSystem(user, project, "ModelSystem1", "A single model system", null);
                    Assert.IsTrue(project.ExportProject(user, tempFile.FullName, out error), error?.Message);
                    Assert.IsTrue(tempFile.Exists, "The exported file does not exist!");
                    var projectController = runtime.ProjectController;
                    Assert.IsTrue(projectController.ImportProjectFile(user, "ImportProjectFileSingleModelSystem-Imported", tempFile.FullName,
                        out var importedSession, out error), error?.Message);
                    Assert.IsNotNull(importedSession);
                    using (importedSession)
                    {
                        var modelSystems = importedSession.ModelSystems;
                        Assert.AreEqual(1, modelSystems.Count);
                    }
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
        public void ImportProjectFileMultipleModelSystem()
        {
            TestHelper.RunInProjectContext("ImportProjectFileSingleModelSystem", (XTMFRuntime runtime, User user, ProjectSession project) =>
            {
                CommandError error = null;
                var tempFile = new FileInfo(Path.GetTempFileName());
                try
                {
                    // Make sure the file does not exist before starting.
                    if (tempFile.Exists)
                    {
                        tempFile.Delete();
                    }
                    const int numberOfModelSystems = 5;
                    for (int i = 0; i < numberOfModelSystems; i++)
                    {
                        CreateModelSystem(user, project, "ModelSystem" + i, "One of many model systems", null);
                    }
                    Assert.IsTrue(project.ExportProject(user, tempFile.FullName, out error), error?.Message);
                    Assert.IsTrue(tempFile.Exists, "The exported file does not exist!");
                    var projectController = runtime.ProjectController;
                    Assert.IsTrue(projectController.ImportProjectFile(user, "ImportProjectFileSingleModelSystem-Imported", tempFile.FullName,
                        out var importedSession, out error), error?.Message);
                    Assert.IsNotNull(importedSession);
                    using (importedSession)
                    {
                        var modelSystems = importedSession.ModelSystems;
                        Assert.AreEqual(numberOfModelSystems, modelSystems.Count);
                    }
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
        public void ImportProjectFileAddedToController()
        {
            const string ImportedModelSystemName = "ImportProjectFileNoModelSystemPersist-Imported";
            TestHelper.RunInProjectContext("ImportProjectFileNoModelSystemPersist", (XTMFRuntime runtime, User user, ProjectSession project) =>
            {
                CommandError error = null;
                var tempFile = new FileInfo(Path.GetTempFileName());
                try
                {
                    // Make sure the file does not exist before starting.
                    if (tempFile.Exists)
                    {
                        tempFile.Delete();
                    }
                    Assert.IsTrue(project.ExportProject(user, tempFile.FullName, out error), error?.Message);
                    Assert.IsTrue(tempFile.Exists, "The exported file does not exist!");
                    var projectController = runtime.ProjectController;

                    Assert.IsTrue(projectController.ImportProjectFile(user, ImportedModelSystemName, tempFile.FullName,
                        out var importedSession, out error), error?.Message);
                    Assert.IsNotNull(importedSession);
                    Assert.IsTrue(user.AvailableProjects.Any(p => p.Name == ImportedModelSystemName), "The imported project was not available to use user.");
                    using (importedSession)
                    {
                        var modelSystems = importedSession.ModelSystems;
                        Assert.AreEqual(0, modelSystems.Count);
                    }
                    Assert.IsTrue(user.AvailableProjects.Any(p => p.Name == ImportedModelSystemName), "The imported project was not available to use user.");
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
        public void SetRunsDirectory()
        {
            TestHelper.RunInProjectContext("SetRunsDirectory", (User user, ProjectSession project) =>
            {
                var dir = new DirectoryInfo(Guid.NewGuid().ToString());
                try
                {
                    CommandError error = null;
                    dir.Create();
                    Assert.IsTrue(project.SetCustomRunDirectory(user, dir.FullName, out error), error?.Message);
                    Assert.AreEqual(dir.FullName, project.RunsDirectory);
                }
                finally
                {
                    if (dir.Exists)
                    {
                        dir.Delete();
                    }
                }
            });
        }

        [TestMethod]
        public void SetRunsDirectoryToInvalidPath()
        {
            TestHelper.RunInProjectContext("SetRunsDirectoryToInvalidPath", (User user, ProjectSession project) =>
            {
                CommandError error = null;
                var initialDirectory = project.RunsDirectory;
                var invalidCharacters = Path.GetInvalidFileNameChars();
                Assert.IsFalse(project.SetCustomRunDirectory(user, new string(invalidCharacters), out error), "An invalid path was able to be set.");
                Assert.AreEqual(initialDirectory, project.RunsDirectory);
            });
        }

        [TestMethod]
        public void ResetRunsDirectory()
        {
            TestHelper.RunInProjectContext("ResetRunsDirectory", (User user, ProjectSession project) =>
            {
                var newRunDir = Guid.NewGuid().ToString();
                DirectoryInfo dir = new DirectoryInfo(newRunDir);
                try
                {
                    CommandError error = null;
                    dir.Create();
                    var initialDirectory = project.RunsDirectory;
                    Assert.IsTrue(project.SetCustomRunDirectory(user, dir.FullName, out error), error?.Message);
                    Assert.AreEqual(dir.FullName, project.RunsDirectory);
                    Assert.IsTrue(project.ResetCustomRunDirectory(user, out error), error?.Message);
                    Assert.AreEqual(initialDirectory, project.RunsDirectory);
                }
                finally
                {
                    if (dir.Exists)
                    {
                        dir.Delete();
                    }
                }
            });
        }

        [TestMethod]
        public void RemoveModelSystem()
        {
            TestHelper.RunInProjectContext("RemoveModelSystem", (User user, ProjectSession project) =>
            {
                const string msName = "RemoveMe";
                CommandError error = null;
                Assert.IsTrue(project.CreateNewModelSystem(user, msName, out var msHeader, out error), error?.Message);
                Assert.IsTrue(project.EditModelSystem(user, msHeader, out var session, out error).UsingIf(session, () =>
                {
                    Assert.IsFalse(project.RemoveModelSystem(user, msHeader, out error), "A model system was able to be removed while it was being edited!");
                }), error?.Message);
                Assert.IsTrue(project.RemoveModelSystem(user, msHeader, out error), error?.Message);
            });
        }

        [TestMethod]
        public void AddAdditionalPastRunDirectory()
        {
            TestHelper.RunInProjectContext("AddAdditionalPastRunDirectory", (User user, ProjectSession project) =>
            {
                string path = Path.GetTempPath();
                CommandError error = null;
                var previousRunDirectories = project.AdditionalPreviousRunDirectories;
                Assert.AreEqual(0, previousRunDirectories.Count, "There were already previous run directories!");
                Assert.IsTrue(project.AddAdditionalPastRunDirectory(user, path, out error), error?.Message ?? "Failed to have an error message!");
                Assert.AreEqual(1, previousRunDirectories.Count, "The previous runs did not include the new path!");
                Assert.AreEqual(path, previousRunDirectories[0], "The path is not the same!");
            });
        }

        [TestMethod]
        public void AddAdditionalPastRunDirectory_Null()
        {
            TestHelper.RunInProjectContext("AddAdditionalPastRunDirectory_Null", (User user, ProjectSession project) =>
            {
                var previousRunDirectories = project.AdditionalPreviousRunDirectories;
                Assert.AreEqual(0, previousRunDirectories.Count, "There were already previous run directories!");
                Assert.IsFalse(project.AddAdditionalPastRunDirectory(user, null, out CommandError error),
                    "The add operation succeeded even though it should have failed!");
                Assert.AreEqual(0, previousRunDirectories.Count, "The invalid previous run directory was added!");
            });
        }

        [TestMethod]
        public void AddAdditionalPastRunDirectory_EmptyString()
        {
            TestHelper.RunInProjectContext("AddAdditionalPastRunDirectory_EmptyString", (User user, ProjectSession project) =>
            {
                var previousRunDirectories = project.AdditionalPreviousRunDirectories;
                Assert.AreEqual(0, previousRunDirectories.Count, "There were already previous run directories!");
                Assert.IsFalse(project.AddAdditionalPastRunDirectory(user, String.Empty, out CommandError error),
                    "The add operation succeeded even though it should have failed!");
                Assert.AreEqual(0, previousRunDirectories.Count, "The invalid previous run directory was added!");
            });
        }

        [TestMethod]
        public void RemoveAdditionalPastRunDirectory()
        {
            TestHelper.RunInProjectContext("AddAdditionalPastRunDirectory", (User user, ProjectSession project) =>
            {
                string path = Path.GetTempPath();
                CommandError error = null;
                var previousRunDirectories = project.AdditionalPreviousRunDirectories;
                Assert.AreEqual(0, previousRunDirectories.Count, "There were already previous run directories!");
                Assert.IsTrue(project.AddAdditionalPastRunDirectory(user, path, out error), error?.Message ?? "Failed to have an error message!");
                Assert.AreEqual(1, previousRunDirectories.Count, "The previous runs did not include the new path!");
                Assert.AreEqual(path, previousRunDirectories[0], "The path is not the same!");
                Assert.IsTrue(project.RemoveAdditionalPastRunDirectory(user, path, out error), error?.Message ?? "Failed to have an error message!");
                Assert.AreEqual(0, previousRunDirectories.Count, "The previous run directory was not removed!");
            });
        }

        [TestMethod]
        public void RemoveAdditionalPastRunDirectory_Null()
        {
            TestHelper.RunInProjectContext("RemoveAdditionalPastRunDirectory_Null", (User user, ProjectSession project) =>
            {
                string path = Path.GetTempPath();
                CommandError error = null;
                var previousRunDirectories = project.AdditionalPreviousRunDirectories;
                Assert.AreEqual(0, previousRunDirectories.Count, "There were already previous run directories!");
                Assert.IsTrue(project.AddAdditionalPastRunDirectory(user, path, out error), error?.Message ?? "Failed to have an error message!");
                Assert.IsFalse(project.RemoveAdditionalPastRunDirectory(user, null, out error),
                    "The remove operation succeeded even though it should have failed!");
                Assert.AreEqual(1, previousRunDirectories.Count, "The previous run directory was removed!");
            });
        }

        [TestMethod]
        public void RemoveAdditionalPastRunDirectory_EmptyString()
        {
            TestHelper.RunInProjectContext("RemoveAdditionalPastRunDirectory_EmptyString", (User user, ProjectSession project) =>
            {
                string path = Path.GetTempPath();
                CommandError error = null;
                var previousRunDirectories = project.AdditionalPreviousRunDirectories;
                Assert.AreEqual(0, previousRunDirectories.Count, "There were already previous run directories!");
                Assert.IsTrue(project.AddAdditionalPastRunDirectory(user, path, out error), error?.Message ?? "Failed to have an error message!");
                Assert.IsFalse(project.RemoveAdditionalPastRunDirectory(user, String.Empty, out error),
                    "The remove operation succeeded even though it should have failed!");
                Assert.AreEqual(1, previousRunDirectories.Count, "The previous run directory was removed!");
            });
        }

        [TestMethod]
        public void RemoveAdditionalPastRunDirectory_NonExistent()
        {
            TestHelper.RunInProjectContext("RemoveAdditionalPastRunDirectory_NonExistent", (User user, ProjectSession project) =>
            {
                string path = Path.GetTempPath();
                CommandError error = null;
                var previousRunDirectories = project.AdditionalPreviousRunDirectories;
                Assert.AreEqual(0, previousRunDirectories.Count, "There were already previous run directories!");
                Assert.IsTrue(project.AddAdditionalPastRunDirectory(user, path, out error), error?.Message ?? "Failed to have an error message!");
                Assert.IsFalse(project.RemoveAdditionalPastRunDirectory(user, path + "a", out error),
                    "The remove operation succeeded even though it should have failed!");
                Assert.AreEqual(1, previousRunDirectories.Count, "The previous run directory was removed!");
            });
        }

        [TestMethod]
        public void AdditionalRunDirectoryPersistance()
        {
            string path = Path.GetTempPath();
            TestHelper.RunInProjectContext("AdditionalRunDirectoryPersistance",
                (user, project) =>
             {
                 CommandError error = null;
                 var previousRunDirectories = project.AdditionalPreviousRunDirectories;
                 Assert.AreEqual(0, previousRunDirectories.Count, "There were already previous run directories!");
                 Assert.IsTrue(project.AddAdditionalPastRunDirectory(user, path, out error), error?.Message ?? "Failed to have an error message!");
                 Assert.AreEqual(1, previousRunDirectories.Count, "The previous runs did not include the new path!");
                 Assert.AreEqual(path, previousRunDirectories[0], "The path is not the same!");
             }, (user, project) =>
             {
                 var previousRunDirectories = project.AdditionalPreviousRunDirectories;
                 Assert.AreEqual(1, previousRunDirectories.Count, "The number of past run directories is wrong after reloading!");
                 Assert.AreEqual(path, previousRunDirectories[0], "The path is not the same after reloading!");
             });
        }
    }
}
