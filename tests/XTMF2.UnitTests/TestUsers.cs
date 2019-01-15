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
using XTMF2.Controllers;
using System.Linq;

namespace TestXTMF
{
    [TestClass]
    public class TestUsers
    {
        [TestMethod]
        public void CreateUser()
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var userController = runtime.UserController;
            string error = null;
            const string userName = "NewUser";
            // ensure the user doesn't exist before we start
            userController.Delete(userName);
            Assert.IsTrue(userController.CreateNew(userName, false, out var user, ref error));
            Assert.IsNull(error);
            Assert.IsFalse(userController.CreateNew(userName, false, out var secondUser, ref error));
            Assert.IsNotNull(error);
            error = null;
            Assert.IsTrue(userController.Delete(user));
        }

        [TestMethod]
        public void UserPersistance()
        {
            //ensure that a user can survive between different XTMF sessions
            var runtime = XTMFRuntime.CreateRuntime();
            var userController = runtime.UserController;
            string error = null;
            const string userName = "NewUser";
            // ensure the user doesn't exist before we start
            userController.Delete(userName);
            Assert.IsTrue(userController.CreateNew(userName, false, out var user, ref error));
            // unload XTMF to simulate it shutting down
            runtime.Shutdown();
            // rebuild XTMF
            runtime = XTMFRuntime.CreateRuntime();
            userController = runtime.UserController;
            Assert.IsNotNull(userController.Users.FirstOrDefault(u => u.UserName == user.UserName));
            // cleanup
            Assert.IsTrue(userController.Delete(userName));
        }

        [TestMethod]
        public void AddUserToProject()
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var userController = runtime.UserController;
            var projectController = runtime.ProjectController;
            string error = null;
            const string userName1 = "FirstUser";
            const string userName2 = "SecondtUser";
            const string projectName1 = "TestShareBetweenUsers1";
            const string projectName2 = "TestShareBetweenUsers2";
            // ensure the user doesn't exist before we start and then create our users
            userController.Delete(userName1);
            userController.Delete(userName2);
            Assert.IsTrue(userController.CreateNew(userName1, false, out var user1, ref error), error);
            Assert.IsTrue(userController.CreateNew(userName2, false, out var user2, ref error), error);
            // now we need to create a project for both users

            Assert.IsTrue(projectController.CreateNewProject(user1, projectName1, out var session1, ref error), error);
            Assert.IsTrue(projectController.CreateNewProject(user2, projectName2, out var session2, ref error), error);

            // make sure we only have 1 project
            Assert.AreEqual(1, user1.AvailableProjects.Count);
            Assert.AreEqual(1, user2.AvailableProjects.Count);

            // Share project1 with user1
            Assert.IsTrue(session1.ShareWith(user1, user2, ref error), error);
            Assert.AreEqual(1, user1.AvailableProjects.Count);
            Assert.AreEqual(2, user2.AvailableProjects.Count);

            Assert.IsFalse(session1.ShareWith(user1, user2, ref error), error);
            Assert.IsFalse(session1.ShareWith(user2, user2, ref error), error);

            // Delete user1 and make sure that user2 loses reference to project1
            Assert.IsTrue(userController.Delete(user1));
            Assert.AreEqual(1, user2.AvailableProjects.Count);

            // finish cleaning up
            Assert.IsTrue(userController.Delete(user2));
        }

        [TestMethod]
        public void AddUserToProjectTwice()
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var userController = runtime.UserController;
            var projectController = runtime.ProjectController;
            string error = null;
            const string userName1 = "FirstUser";
            const string userName2 = "SecondtUser";
            const string projectName1 = "TestShareBetweenUsers1";
            // ensure the user doesn't exist before we start and then create our users
            userController.Delete(userName1);
            userController.Delete(userName2);
            Assert.IsTrue(userController.CreateNew(userName1, false, out var user1, ref error), error);
            Assert.IsTrue(userController.CreateNew(userName2, false, out var user2, ref error), error);
            // now we need to create a project for both users

            Assert.IsTrue(projectController.CreateNewProject(user1, projectName1, out var session1, ref error), error);
            
            // make sure we only have 1 project
            Assert.AreEqual(1, user1.AvailableProjects.Count);
            Assert.AreEqual(0, user2.AvailableProjects.Count);

            Assert.IsTrue(session1.ShareWith(user1, user2, ref error));
            Assert.AreEqual(1, user2.AvailableProjects.Count);

            // Ensure that the command fails to be added the second time
            Assert.IsFalse(session1.ShareWith(user1, user2, ref error));
            Assert.AreEqual(1, user2.AvailableProjects.Count);

            // Delete user1 and make sure that user2 loses reference to project1
            Assert.IsTrue(userController.Delete(user1));
            Assert.AreEqual(0, user2.AvailableProjects.Count);

            // finish cleaning up
            Assert.IsTrue(userController.Delete(user2));
        }

        [TestMethod]
        public void RemoveUserFromProject()
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var userController = runtime.UserController;
            var projectController = runtime.ProjectController;
            string error = null;
            const string userName1 = "FirstUser";
            const string userName2 = "SecondtUser";
            const string projectName1 = "TestShareBetweenUsers1";
            const string projectName2 = "TestShareBetweenUsers2";
            // ensure the user doesn't exist before we start and then create our users
            userController.Delete(userName1);
            userController.Delete(userName2);
            Assert.IsTrue(userController.CreateNew(userName1, false, out var user1, ref error), error);
            Assert.IsTrue(userController.CreateNew(userName2, false, out var user2, ref error), error);
            // now we need to create a project for both users

            Assert.IsTrue(projectController.CreateNewProject(user1, projectName1, out var session1, ref error), error);
            Assert.IsTrue(projectController.CreateNewProject(user2, projectName2, out var session2, ref error), error);

            // make sure we only have 1 project
            Assert.AreEqual(1, user1.AvailableProjects.Count);
            Assert.AreEqual(1, user2.AvailableProjects.Count);

            // Share project1 with user1
            Assert.IsTrue(session1.ShareWith(user1, user2, ref error), error);
            Assert.AreEqual(1, user1.AvailableProjects.Count);
            Assert.AreEqual(2, user2.AvailableProjects.Count);

            Assert.IsFalse(session1.ShareWith(user1, user2, ref error), error);
            Assert.IsFalse(session1.ShareWith(user2, user2, ref error), error);

            Assert.IsFalse(session1.RestrictAccess(user1, user1, ref error));
            Assert.IsFalse(session1.RestrictAccess(user2, user1, ref error));
            Assert.IsTrue(session1.RestrictAccess(user1, user2, ref error), error);

            Assert.AreEqual(1, user1.AvailableProjects.Count);
            Assert.AreEqual(1, user2.AvailableProjects.Count);

            // Delete user1 and make sure that user2 loses reference to project1
            Assert.IsTrue(userController.Delete(user1));
            Assert.AreEqual(1, user2.AvailableProjects.Count);

            // finish cleaning up
            Assert.IsTrue(userController.Delete(user2));
        }

        [TestMethod]
        public void RemoveUserFromProjectTwice()
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var userController = runtime.UserController;
            var projectController = runtime.ProjectController;
            string error = null;
            const string userName1 = "FirstUser";
            const string userName2 = "SecondtUser";
            const string projectName1 = "TestShareBetweenUsers1";
            const string projectName2 = "TestShareBetweenUsers2";
            // ensure the user doesn't exist before we start and then create our users
            userController.Delete(userName1);
            userController.Delete(userName2);
            Assert.IsTrue(userController.CreateNew(userName1, false, out var user1, ref error), error);
            Assert.IsTrue(userController.CreateNew(userName2, false, out var user2, ref error), error);
            // now we need to create a project for both users

            Assert.IsTrue(projectController.CreateNewProject(user1, projectName1, out var session1, ref error), error);
            Assert.IsTrue(projectController.CreateNewProject(user2, projectName2, out var session2, ref error), error);

            // make sure we only have 1 project
            Assert.AreEqual(1, user1.AvailableProjects.Count);
            Assert.AreEqual(1, user2.AvailableProjects.Count);

            // Share project1 with user1
            Assert.IsTrue(session1.ShareWith(user1, user2, ref error), error);
            Assert.AreEqual(1, user1.AvailableProjects.Count);
            Assert.AreEqual(2, user2.AvailableProjects.Count);

            Assert.IsFalse(session1.ShareWith(user1, user2, ref error), error);
            Assert.IsFalse(session1.ShareWith(user2, user2, ref error), error);

            Assert.IsFalse(session1.RestrictAccess(user1, user1, ref error));
            Assert.IsFalse(session1.RestrictAccess(user2, user1, ref error));
            Assert.IsTrue(session1.RestrictAccess(user1, user2, ref error), error);
            // Ensure that we can't do it again
            Assert.IsFalse(session1.RestrictAccess(user1, user2, ref error), error);

            Assert.AreEqual(1, user1.AvailableProjects.Count);
            Assert.AreEqual(1, user2.AvailableProjects.Count);

            // Delete user1 and make sure that user2 loses reference to project1
            Assert.IsTrue(userController.Delete(user1));
            Assert.AreEqual(1, user2.AvailableProjects.Count);

            // finish cleaning up
            Assert.IsTrue(userController.Delete(user2));
        }

        [TestMethod]
        public void SwitchOwner()
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var userController = runtime.UserController;
            var projectController = runtime.ProjectController;
            string error = null;
            const string userName1 = "FirstUser";
            const string userName2 = "SecondtUser";
            const string projectName1 = "TestShareBetweenUsers1";
            // ensure the user doesn't exist before we start and then create our users
            userController.Delete(userName1);
            userController.Delete(userName2);
            Assert.IsTrue(userController.CreateNew(userName1, false, out var user1, ref error), error);
            Assert.IsTrue(userController.CreateNew(userName2, false, out var user2, ref error), error);
            // now we need to create a project for both users

            Assert.IsTrue(projectController.CreateNewProject(user1, projectName1, out var session1, ref error), error);

            Assert.AreEqual(1, user1.AvailableProjects.Count);
            Assert.AreEqual(0, user2.AvailableProjects.Count);

            Assert.IsTrue(session1.SwitchOwner(user1, user2, ref error), error);
            Assert.IsFalse(session1.SwitchOwner(user1, user2, ref error));

            Assert.AreEqual(0, user1.AvailableProjects.Count);
            Assert.AreEqual(1, user2.AvailableProjects.Count);

            Assert.IsTrue(userController.Delete(user1));

            Assert.AreEqual(1, user2.AvailableProjects.Count);

            Assert.IsTrue(userController.Delete(user2));
        }
    }
}
