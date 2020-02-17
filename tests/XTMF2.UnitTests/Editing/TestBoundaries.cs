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
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using XTMF2.Editing;

namespace XTMF2.UnitTests.Editing
{
    [TestClass]
    public class TestBoundaries
    {
        [TestMethod]
        public void AddBoundary()
        {
            TestHelper.RunInModelSystemContext("AddBoundary", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                CommandError error = null;
                Assert.AreEqual(0, ms.GlobalBoundary.Boundaries.Count);
                Assert.IsTrue(mSession.AddBoundary(user, ms.GlobalBoundary, "UniqueName", out Boundary subB, out error), error?.Message);
                Assert.IsFalse(mSession.AddBoundary(user, ms.GlobalBoundary, "UniqueName", out Boundary fail1, out error), "Created a second boundary with the same name!");
                Assert.AreEqual(1, ms.GlobalBoundary.Boundaries.Count);
                Assert.IsTrue(mSession.Undo(user, out error), error?.Message);
                Assert.AreEqual(0, ms.GlobalBoundary.Boundaries.Count);
                Assert.IsTrue(mSession.Redo(user, out error), error?.Message);
                Assert.AreEqual(1, ms.GlobalBoundary.Boundaries.Count);
                Assert.AreSame(subB, ms.GlobalBoundary.Boundaries[0]);
                Assert.IsFalse(mSession.AddBoundary(user, ms.GlobalBoundary, "UniqueName", out Boundary fail2, out error), "Created a second boundary with the same name after redo!");
            });
        }

        [TestMethod]
        public void AddBoundaryWithBadUser()
        {
            TestHelper.RunInModelSystemContext("AddBoundaryWithBadUser", (user, unauthorizedUser, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                Assert.AreEqual(0, ms.GlobalBoundary.Boundaries.Count);
                Assert.IsFalse(mSession.AddBoundary(unauthorizedUser, ms.GlobalBoundary, "UniqueName", out Boundary subB, out var error), error?.Message);
            });
        }

        [TestMethod]
        public void AddBoundaryNullParent()
        {
            TestHelper.RunInModelSystemContext("AddBoundaryNullParent", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                Assert.AreEqual(0, ms.GlobalBoundary.Boundaries.Count);
                Assert.ThrowsException<ArgumentNullException>(() =>
                {
                    mSession.AddBoundary(user, null, "UniqueName", out Boundary subB, out var error);
                });
            });
        }

        [TestMethod]
        public void AddBoundaryNulUser()
        {
            TestHelper.RunInModelSystemContext("AddBoundaryNullUser", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                Assert.AreEqual(0, ms.GlobalBoundary.Boundaries.Count);
                Assert.ThrowsException<ArgumentNullException>(() =>
                {
                    mSession.AddBoundary(null, ms.GlobalBoundary, "UniqueName", out Boundary subB, out var error);
                });
            });
        }

        [TestMethod]
        public void RemoveBoundary()
        {
            TestHelper.RunInModelSystemContext("RemoveBoundary", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                Assert.AreEqual(0, ms.GlobalBoundary.Boundaries.Count);
                Assert.IsTrue(mSession.AddBoundary(user, ms.GlobalBoundary, "UniqueName", out Boundary subB, out var error), error?.Message);
                Assert.IsFalse(mSession.AddBoundary(user, ms.GlobalBoundary, "UniqueName", out Boundary fail1, out error), "Created a second boundary with the same name!");
                Assert.AreEqual(1, ms.GlobalBoundary.Boundaries.Count);
                Assert.IsTrue(mSession.Undo(user, out error), error?.Message);
                Assert.AreEqual(0, ms.GlobalBoundary.Boundaries.Count);
                Assert.IsTrue(mSession.Redo(user, out error), error?.Message);
                Assert.AreEqual(1, ms.GlobalBoundary.Boundaries.Count);
                Assert.AreSame(subB, ms.GlobalBoundary.Boundaries[0]);
                Assert.IsFalse(mSession.AddBoundary(user, ms.GlobalBoundary, "UniqueName", out Boundary fail2, out error), "Created a second boundary with the same name after redo!");

                // Now test removing the boundary explicitly
                Assert.IsTrue(mSession.RemoveBoundary(user, ms.GlobalBoundary, subB, out error), error?.Message);
                Assert.AreEqual(0, ms.GlobalBoundary.Boundaries.Count);
                Assert.IsTrue(mSession.Undo(user, out error), error?.Message);
                Assert.AreEqual(1, ms.GlobalBoundary.Boundaries.Count);
                Assert.AreSame(subB, ms.GlobalBoundary.Boundaries[0]);
                Assert.IsTrue(mSession.Redo(user, out error), error?.Message);
                Assert.AreEqual(0, ms.GlobalBoundary.Boundaries.Count);
            });
        }

        [TestMethod]
        public void RemoveBoundaryWithBadUser()
        {
            TestHelper.RunInModelSystemContext("RemoveBoundary", (user, unauthorizedUser, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                CommandError error = null;
                Assert.AreEqual(0, ms.GlobalBoundary.Boundaries.Count);
                Assert.IsTrue(mSession.AddBoundary(user, ms.GlobalBoundary, "UniqueName", out Boundary subB, out error), error?.Message);
                Assert.IsFalse(mSession.AddBoundary(user, ms.GlobalBoundary, "UniqueName", out Boundary fail1, out error), "Created a second boundary with the same name!");
                Assert.AreEqual(1, ms.GlobalBoundary.Boundaries.Count);
                Assert.IsTrue(mSession.Undo(user, out error), error?.Message);
                Assert.AreEqual(0, ms.GlobalBoundary.Boundaries.Count);
                Assert.IsTrue(mSession.Redo(user, out error), error?.Message);
                Assert.AreEqual(1, ms.GlobalBoundary.Boundaries.Count);
                Assert.AreSame(subB, ms.GlobalBoundary.Boundaries[0]);
                Assert.IsFalse(mSession.AddBoundary(user, ms.GlobalBoundary, "UniqueName", out Boundary fail2, out error), "Created a second boundary with the same name after redo!");

                // Now test removing the boundary explicitly
                Assert.AreEqual(1, ms.GlobalBoundary.Boundaries.Count);
                Assert.IsFalse(mSession.RemoveBoundary(unauthorizedUser, ms.GlobalBoundary, subB, out error), error?.Message);
                Assert.AreEqual(1, ms.GlobalBoundary.Boundaries.Count);
            });
        }

        [TestMethod]
        public void RemoveBoundaryNullBoundary()
        {
            TestHelper.RunInModelSystemContext("RemoveBoundary", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                Assert.AreEqual(0, ms.GlobalBoundary.Boundaries.Count);
                Assert.IsTrue(mSession.AddBoundary(user, ms.GlobalBoundary, "UniqueName", out Boundary subB, out var error), error?.Message);
                Assert.AreEqual(1, ms.GlobalBoundary.Boundaries.Count);

                // Now test removing the boundary explicitly
                Assert.ThrowsException<ArgumentNullException>(() =>
                {
                    mSession.RemoveBoundary(user, ms.GlobalBoundary, null, out error);
                });
            });
        }

        [TestMethod]
        public void RemoveBoundaryNullParent()
        {
            TestHelper.RunInModelSystemContext("RemoveBoundary", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                Assert.AreEqual(0, ms.GlobalBoundary.Boundaries.Count);
                Assert.IsTrue(mSession.AddBoundary(user, ms.GlobalBoundary, "UniqueName", out Boundary subB, out var error), error?.Message);
                Assert.AreEqual(1, ms.GlobalBoundary.Boundaries.Count);

                // Now test removing the boundary explicitly
                Assert.ThrowsException<ArgumentNullException>(() =>
                {
                    mSession.RemoveBoundary(user, null, subB, out error);
                });
            });
        }

        [TestMethod]
        public void RemoveBoundaryNullUser()
        {
            TestHelper.RunInModelSystemContext("RemoveBoundary", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                CommandError error = null;
                Assert.AreEqual(0, ms.GlobalBoundary.Boundaries.Count);
                Assert.IsTrue(mSession.AddBoundary(user, ms.GlobalBoundary, "UniqueName", out Boundary subB, out error), error?.Message);
                Assert.AreEqual(1, ms.GlobalBoundary.Boundaries.Count);

                // Now test removing the boundary explicitly
                Assert.ThrowsException<ArgumentNullException>(() =>
                {
                    mSession.RemoveBoundary(null, ms.GlobalBoundary, subB, out error);
                });
            });
        }

        [TestMethod]
        public void RemoveBoundaryNotInBoundary()
        {
            TestHelper.RunInModelSystemContext("RemoveBoundary", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                CommandError error = null;
                Assert.AreEqual(0, ms.GlobalBoundary.Boundaries.Count);
                Assert.IsTrue(mSession.AddBoundary(user, ms.GlobalBoundary, "SubB", out Boundary subB, out error), error?.Message);
                Assert.IsTrue(mSession.AddBoundary(user, ms.GlobalBoundary, "SubC", out Boundary subC, out error), error?.Message);
                Assert.IsTrue(mSession.AddBoundary(user, subC, "SubCA", out Boundary subCA, out error), error?.Message);
                Assert.AreEqual(2, ms.GlobalBoundary.Boundaries.Count);
                Assert.AreEqual(1, subC.Boundaries.Count);

                Assert.IsFalse(mSession.RemoveBoundary(user, ms.GlobalBoundary, subCA, out error), "Successfully removed a boundary from a grandparent isntead of failing!");
            });
        }
        [TestMethod]
        public void TestSettingBoundaryName()
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var controller = runtime.ProjectController;
            CommandError error = null;
            var localUser = TestHelper.GetTestUser(runtime);
            controller.DeleteProject(localUser, "Test", out error);
            Assert.IsTrue(controller.CreateNewProject(localUser, "Test", out ProjectSession session, out error).UsingIf(session, () =>
            {
                var project = session.Project;
                Assert.IsTrue(session.CreateNewModelSystem(localUser, "TestMS", out var modelSystem, out error), error?.Message);
                Assert.IsTrue(session.EditModelSystem(localUser, modelSystem, out var msSession, out error).UsingIf(msSession, () =>
                {
                    var ms = msSession.ModelSystem;
                    var newBoundaryName = "NewBoundaryName";
                    var oldName = ms.GlobalBoundary.Name;
                    Assert.IsTrue(msSession.SetBoundaryName(localUser, ms.GlobalBoundary, newBoundaryName, out error), error?.Message);
                    Assert.AreEqual(newBoundaryName, ms.GlobalBoundary.Name);
                    Assert.IsTrue(msSession.Undo(localUser, out error), error?.Message);
                    Assert.AreEqual(oldName, ms.GlobalBoundary.Name);
                    Assert.IsTrue(msSession.Redo(localUser, out error), error?.Message);
                    Assert.AreEqual(newBoundaryName, ms.GlobalBoundary.Name);
                }), "Unable to get a model system editing session!");
            }), "Unable to create project");
        }

        [TestMethod]
        public void TestSettingBoundaryNameWithBadUser()
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var controller = runtime.ProjectController;
            CommandError error = null;
            (var localUser, var hacker) = TestHelper.GetTestUsers(runtime);
            controller.DeleteProject(localUser, "Test", out error);
            Assert.IsTrue(controller.CreateNewProject(localUser, "Test", out ProjectSession session, out error).UsingIf(session, () =>
            {
                var project = session.Project;
                Assert.IsTrue(session.CreateNewModelSystem(localUser, "TestMS", out var modelSystem, out error), error?.Message);
                Assert.IsTrue(session.EditModelSystem(localUser, modelSystem, out var msSession, out error).UsingIf(msSession, () =>
                {
                    var ms = msSession.ModelSystem;
                    var newBoundaryName = "NewBoundaryName";
                    Assert.IsFalse(msSession.SetBoundaryName(hacker, ms.GlobalBoundary, newBoundaryName, out error), error?.Message);
                }), "Unable to get a model system editing session!");
            }), "Unable to create project");
        }

        [TestMethod]
        public void TestSettingBoundaryDescription()
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var controller = runtime.ProjectController;
            CommandError error = null;
            var localUser = TestHelper.GetTestUser(runtime);
            controller.DeleteProject(localUser, "Test", out error);
            Assert.IsTrue(controller.CreateNewProject(localUser, "Test", out ProjectSession session, out error).UsingIf(session, () =>
            {
                var project = session.Project;
                Assert.IsTrue(session.CreateNewModelSystem(localUser, "TestMS", out var modelSystem, out error), error?.Message);
                Assert.IsTrue(session.EditModelSystem(localUser, modelSystem, out var msSession, out error).UsingIf(msSession, () =>
                {
                    var ms = msSession.ModelSystem;
                    var newBoundaryDescription = "NewBoundaryDescription";
                    var oldName = ms.GlobalBoundary.Description;
                    Assert.IsTrue(msSession.SetBoundaryDescription(localUser, ms.GlobalBoundary, newBoundaryDescription, out error), error?.Message);
                    Assert.AreEqual(newBoundaryDescription, ms.GlobalBoundary.Description);
                    Assert.IsTrue(msSession.Undo(localUser, out error), error?.Message);
                    Assert.AreEqual(oldName, ms.GlobalBoundary.Description);
                    Assert.IsTrue(msSession.Redo(localUser, out error), error?.Message);
                    Assert.AreEqual(newBoundaryDescription, ms.GlobalBoundary.Description);
                }), "Unable to get a model system editing session");
            }), "Unable to create project");
        }

        [TestMethod]
        public void TestSettingBoundaryDescriptionWithBadUser()
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var controller = runtime.ProjectController;
            CommandError error = null;
            (var localUser, var unauthorizedUser) = TestHelper.GetTestUsers(runtime);
            controller.DeleteProject(localUser, "Test", out error);
            Assert.IsTrue(controller.CreateNewProject(localUser, "Test", out ProjectSession session, out error).UsingIf(session, () =>
            {
                var project = session.Project;
                Assert.IsTrue(session.CreateNewModelSystem(localUser, "TestMS", out var modelSystem, out error), error?.Message);
                Assert.IsTrue(session.EditModelSystem(localUser, modelSystem, out var msSession, out error).UsingIf(msSession, () =>
                {
                    var ms = msSession.ModelSystem;
                    var newBoundaryDescription = "NewBoundaryDescription";
                    var oldName = ms.GlobalBoundary.Description;
                    Assert.IsFalse(msSession.SetBoundaryDescription(unauthorizedUser, ms.GlobalBoundary, newBoundaryDescription, out error), error?.Message);
                }), "Unable to get a model system editing session");
            }), "Unable to create project");
        }

        [TestMethod]
        public void TestSettingBoundaryPersistence()
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var controller = runtime.ProjectController;
            CommandError error = null;
            var localUser = TestHelper.GetTestUser(runtime);
            const string projectName = "Test";
            const string modelSystemName = "TestMS";
            controller.DeleteProject(localUser, projectName, out error);
            var newBoundaryDescription = "NewBoundaryDescription";
            var newBoundaryName = "NewBoundaryName";
            // first pass
            {
                Assert.IsTrue(controller.CreateNewProject(localUser, projectName, out ProjectSession session, out error).UsingIf(session, () =>
                {
                    var project = session.Project;
                    Assert.IsTrue(session.CreateNewModelSystem(localUser, modelSystemName, out var modelSystem, out error), error?.Message);
                    Assert.IsTrue(session.EditModelSystem(localUser, modelSystem, out var msSession, out error).UsingIf(msSession, () =>
                        {
                            var ms = msSession.ModelSystem;
                            Assert.IsTrue(msSession.SetBoundaryName(localUser, ms.GlobalBoundary, newBoundaryName, out error), error?.Message);
                            Assert.IsTrue(msSession.SetBoundaryDescription(localUser, ms.GlobalBoundary, newBoundaryDescription, out error), error?.Message);
                            Assert.IsTrue(msSession.Save(out error), error?.Message);
                            Assert.IsTrue(session.Save(out error), error?.Message);
                        }), error?.Message);

                }), "Unable to create project");
            }
            // second pass
            {
                Assert.IsTrue(controller.GetProject(localUser, projectName, out var project, out error));
                Assert.IsTrue(controller.GetProjectSession(localUser, project, out var session, out error).UsingIf(session, () =>
                {
                    Assert.IsTrue(session.GetModelSystemHeader(localUser, modelSystemName, out var modelSystem, out error), error?.Message);
                    Assert.IsTrue(session.EditModelSystem(localUser, modelSystem, out var msSession, out error).UsingIf(msSession, () =>
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
