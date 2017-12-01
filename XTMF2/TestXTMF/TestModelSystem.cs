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
using XTMF2.ModelSystemConstruct;
using static XTMF2.Helper;
using TestXTMF.Modules;
using XTMF2.RuntimeModules;
using static TestXTMF.TestHelper;

namespace TestXTMF
{
    [TestClass]
    public class TestModelSystem
    {
        [TestMethod]
        public void ModelSystemPersistance()
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var userController = runtime.UserController;
            var projectController = runtime.ProjectController;
            string error = null;
            const string userName = "NewUser";
            const string projectName = "TestProject";
            const string modelSystemName = "ModelSystem1";
            // clear out the user if possible
            userController.Delete(userName);
            Assert.IsTrue(userController.CreateNew(userName, false, out var user, ref error), error);
            Assert.IsTrue(projectController.CreateNewProject(user, projectName, out var session, ref error).UsingIf(session, () =>
            {
                Assert.IsTrue(session.CreateNewModelSystem(modelSystemName, out var modelSystemHeader, ref error), error);
                Assert.IsTrue(session.Save(ref error));
            }), error);
            runtime.Shutdown();
            runtime = XTMFRuntime.CreateRuntime();
            userController = runtime.UserController;
            projectController = runtime.ProjectController;
            user = userController.GetUserByName(userName);
            Assert.IsTrue(projectController.GetProjectSession(user, user.AvailableProjects[0], out session, ref error).UsingIf(session, () =>
             {
                 var modelSystems = session.ModelSystems;
                 Assert.AreEqual(1, modelSystems.Count);
                 Assert.AreEqual(modelSystemName, modelSystems[0].Name);
             }), error);
            //cleanup
            userController.Delete(user);
        }

        [TestMethod]
        public void GetModelSystemSession()
        {
            RunInModelSystemContext("GetModelSystemSession", (user, pSession, mSession) =>
            {
                string error = null;
                var globalBoundary = mSession.ModelSystem.GlobalBoundary;
                Assert.IsTrue(mSession.AddModelSystemStart(user, globalBoundary, "Start", out Start start, ref error), error);
                Assert.IsFalse(mSession.AddModelSystemStart(user, globalBoundary, "Start", out Start start_, ref error));
            });
        }

        [TestMethod]
        public void EnsureSameModelSystemSession()
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var userController = runtime.UserController;
            var projectController = runtime.ProjectController;
            string error = null;
            const string userName = "NewUser";
            const string userName2 = "NewUser2";
            const string projectName = "TestProject";
            const string modelSystemName = "ModelSystem1";
            // clear out the user if possible
            userController.Delete(userName);
            userController.Delete(userName2);
            Assert.IsTrue(userController.CreateNew(userName, false, out var user, ref error), error);
            Assert.IsTrue(userController.CreateNew(userName2, false, out var user2, ref error), error);
            Assert.IsTrue(projectController.CreateNewProject(user, projectName, out var session, ref error).UsingIf(session, () =>
            {
                // share the session with the second user
                Assert.IsTrue(session.ShareWith(user, user2, ref error), error);
                // create a new model system for both users to try to edit
                Assert.IsTrue(session.CreateNewModelSystem(modelSystemName, out var modelSystemHeader, ref error), error);
                Assert.IsTrue(session.EditModelSystem(user, modelSystemHeader, out var modelSystemSession, ref error).UsingIf(
                    modelSystemSession, () =>
                    {
                        Assert.IsTrue(session.EditModelSystem(user2, modelSystemHeader, out var modelSystemSession2, ref error).UsingIf(modelSystemSession2, () =>
                        {
                            Assert.AreSame(modelSystemSession, modelSystemSession2);
                        }), error);
                    }), error);
                Assert.IsTrue(session.Save(ref error));
            }), error);

            //cleanup
            userController.Delete(user);
        }

        [TestMethod]
        public void EnsureDifferentModelSystemSession()
        {
            var runtime = XTMFRuntime.CreateRuntime();
            var userController = runtime.UserController;
            var projectController = runtime.ProjectController;
            string error = null;
            const string userName = "NewUser";
            const string userName2 = "NewUser2";
            const string projectName = "TestProject";
            const string modelSystemName = "ModelSystem1";
            // clear out the user if possible
            userController.Delete(userName);
            userController.Delete(userName2);
            Assert.IsTrue(userController.CreateNew(userName, false, out var user, ref error), error);
            Assert.IsTrue(userController.CreateNew(userName2, false, out var user2, ref error), error);
            Assert.IsTrue(projectController.CreateNewProject(user, projectName, out var session, ref error).UsingIf(session, () =>
            {
                // share the session with the second user
                Assert.IsTrue(session.ShareWith(user, user2, ref error), error);
                // create a new model system for both users to try to edit
                Assert.IsTrue(session.CreateNewModelSystem(modelSystemName, out var modelSystemHeader, ref error), error);
                Assert.IsTrue(session.EditModelSystem(user, modelSystemHeader, out var modelSystemSession, ref error).UsingIf(
                    modelSystemSession, () =>
                    {

                    }), error);
                Assert.IsTrue(session.EditModelSystem(user2, modelSystemHeader, out var modelSystemSession2, ref error).UsingIf(modelSystemSession2, () =>
                {
                    Assert.AreNotSame(modelSystemSession, modelSystemSession2);
                }), error);
                Assert.IsTrue(session.Save(ref error));
            }), error);

            //cleanup
            userController.Delete(user);
        }

        [TestMethod]
        public void ModelSystemSavedWithStartOnly()
        {
            RunInModelSystemContext("ModelSystemSavedWithStartOnly", (user, pSession, mSession) =>
            {
                // initialization
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.IsTrue(mSession.AddModelSystemStart(user, ms.GlobalBoundary, "FirstStart", out var start, ref error), error);

            }, (user, pSession, mSession) =>
            {
                // after shutdown
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.AreEqual(1, ms.GlobalBoundary.Starts.Count);
                // we shouldn't be able to add another start with the same name in the same boundary
                Assert.IsFalse(mSession.AddModelSystemStart(user, ms.GlobalBoundary, "FirstStart", out var start, ref error), error);
            });
        }

        [TestMethod]
        public void ModelSystemSavedWithModelSystemStructureOnly()
        {
            RunInModelSystemContext("ModelSystemSavedWithModelSystemStructureOnly", (user, pSession, mSession) =>
            {
                // initialization
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.IsTrue(mSession.AddModelSystemStructure(user, ms.GlobalBoundary, "MyMSS", typeof(SimpleTestModule), out var mss, ref error));
                Assert.AreEqual("MyMSS", mss.Name);
            }, (user, pSession, mSession) =>
            {
                // after shutdown
                var ms = mSession.ModelSystem;
                Assert.AreEqual(1, ms.GlobalBoundary.Modules.Count);
            });
        }

        [TestMethod]
        public void ModelSystemSavedWithStartAndModelSystemStructure()
        {
            RunInModelSystemContext("ModelSystemSavedWithStartAndModelSystemStructure", (user, pSession, mSession) =>
            {
                // initialization
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.IsTrue(mSession.AddModelSystemStructure(user, ms.GlobalBoundary, "MyMSS", typeof(SimpleTestModule), out var mss, ref error));
                Assert.IsTrue(mSession.AddModelSystemStart(user, ms.GlobalBoundary, "FirstStart", out var start, ref error), error);
                Assert.AreEqual("MyMSS", mss.Name);
            }, (user, pSession, mSession) =>
            {
                // after shutdown
                string error = null;
                var ms = mSession.ModelSystem;
                Assert.AreEqual(1, ms.GlobalBoundary.Starts.Count);
                Assert.AreEqual(1, ms.GlobalBoundary.Modules.Count);
                Assert.IsFalse(mSession.AddModelSystemStart(user, ms.GlobalBoundary, "FirstStart", out var start, ref error), error);
            });
        }

        [TestMethod]
        public void ModelSystemWithLink()
        {
            RunInModelSystemContext("ModelSystemWithLink", (user, pSession, mSession) =>
            {
                // initialization
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.IsTrue(mSession.AddModelSystemStructure(user, ms.GlobalBoundary, "MyMSS", typeof(SimpleTestModule), out var mss, ref error));
                Assert.IsTrue(mSession.AddModelSystemStart(user, ms.GlobalBoundary, "FirstStart", out var start, ref error), error);
                Assert.IsTrue(mSession.AddLink(user, start, start.Hooks[0], mss, out var link, ref error), error);
                Assert.AreEqual("MyMSS", mss.Name);
            }, (user, pSession, mSession) =>
            {
                // after shutdown
                string error = null;
                var ms = mSession.ModelSystem;
                Assert.AreEqual(1, ms.GlobalBoundary.Starts.Count);
                Assert.AreEqual(1, ms.GlobalBoundary.Modules.Count);
                Assert.AreEqual(1, ms.GlobalBoundary.Links.Count);
                Assert.IsFalse(mSession.AddModelSystemStart(user, ms.GlobalBoundary, "FirstStart", out var start, ref error), error);
            });
        }

        [TestMethod]
        public void ModelSystemWithMultiLink()
        {
            RunInModelSystemContext("ModelSystemWithLink", (user, pSession, mSession) =>
            {
                // initialization
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.IsTrue(mSession.AddModelSystemStart(user, ms.GlobalBoundary, "FirstStart", out var start, ref error), error);

                Assert.IsTrue(mSession.AddModelSystemStructure(user, ms.GlobalBoundary, "Execute", typeof(Execute), out var mss, ref error));
                Assert.IsTrue(mSession.AddModelSystemStructure(user, ms.GlobalBoundary, "Ignore1", typeof(IgnoreResult<string>), out var ignore1, ref error));
                Assert.IsTrue(mSession.AddModelSystemStructure(user, ms.GlobalBoundary, "Ignore2", typeof(IgnoreResult<string>), out var ignore2, ref error));
                Assert.IsTrue(mSession.AddModelSystemStructure(user, ms.GlobalBoundary, "Ignore3", typeof(IgnoreResult<string>), out var ignore3, ref error));
                Assert.IsTrue(mSession.AddModelSystemStructure(user, ms.GlobalBoundary, "Hello World", typeof(SimpleTestModule), out var hello, ref error));


                Assert.IsTrue(mSession.AddLink(user, start, GetHook(start.Hooks, "ToExecute"), mss, out var link, ref error), error);
                Assert.IsTrue(mSession.AddLink(user, mss, GetHook(mss.Hooks, "To Execute"), ignore1, out var link1, ref error), error);
                Assert.IsTrue(mSession.AddLink(user, mss, GetHook(mss.Hooks, "To Execute"), ignore2, out var link2, ref error), error);
                Assert.IsTrue(mSession.AddLink(user, mss, GetHook(mss.Hooks, "To Execute"), ignore3, out var link3, ref error), error);

                Assert.AreNotSame(link, link1);
                Assert.AreSame(link1, link2);
                Assert.AreSame(link1, link3);

                Assert.IsTrue(mSession.AddLink(user, ignore1, GetHook(ignore1.Hooks, "To Ignore"), hello, out var toSame1, ref error), error);
                Assert.IsTrue(mSession.AddLink(user, ignore2, GetHook(ignore2.Hooks, "To Ignore"), hello, out var toSame2, ref error), error);
                Assert.IsTrue(mSession.AddLink(user, ignore3, GetHook(ignore3.Hooks, "To Ignore"), hello, out var toSame3, ref error), error);

                Assert.AreNotSame(toSame1, toSame2);
                Assert.AreNotSame(toSame1, toSame3);
                Assert.AreNotSame(toSame2, toSame3);

                Assert.AreEqual("Execute", mss.Name);
            }, (user, pSession, mSession) =>
            {
                // after shutdown
                string error = null;
                var ms = mSession.ModelSystem;
                Assert.AreEqual(1, ms.GlobalBoundary.Starts.Count);
                Assert.AreEqual(5, ms.GlobalBoundary.Modules.Count);
                Assert.AreEqual(5, ms.GlobalBoundary.Links.Count);
                Assert.IsFalse(mSession.AddModelSystemStart(user, ms.GlobalBoundary, "FirstStart", out var start, ref error), error);
            });
        }

        [TestMethod]
        public void UndoAddStart()
        {
            RunInModelSystemContext("UndoAddStart", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.AreEqual(0, ms.GlobalBoundary.Starts.Count);
                Assert.IsTrue(mSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", out var Start, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Starts.Count);
                Assert.IsTrue(mSession.Undo(ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Starts.Count);
                Assert.IsTrue(mSession.Redo(ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Starts.Count);
                Assert.AreSame(Start, ms.GlobalBoundary.Starts[0]);
            });
        }

        [TestMethod]
        public void UndoRemoveStart()
        {
            RunInModelSystemContext("UndoAddStart", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.AreEqual(0, ms.GlobalBoundary.Starts.Count);
                Assert.IsTrue(mSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", out var Start, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Starts.Count);
                Assert.IsTrue(mSession.Undo(ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Starts.Count);
                Assert.IsTrue(mSession.Redo(ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Starts.Count);
                Assert.AreSame(Start, ms.GlobalBoundary.Starts[0]);

                //now test explicitly removing the start
                Assert.IsTrue(mSession.RemoveStart(user, Start, ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Starts.Count);
                Assert.IsTrue(mSession.Undo(ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Starts.Count);
                Assert.IsTrue(mSession.Redo(ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Starts.Count);
            });
        }

        [TestMethod]
        public void UndoAddModelSystemStructure()
        {
            RunInModelSystemContext("UndoAddModelSystemStructure", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.AreEqual(0, ms.GlobalBoundary.Modules.Count);
                Assert.IsTrue(mSession.AddModelSystemStructure(user, ms.GlobalBoundary, "Start", typeof(BasicParameter<int>),
                    out var mss, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Modules.Count);
                Assert.IsTrue(mSession.Undo(ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Modules.Count);
                Assert.IsTrue(mSession.Redo(ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Modules.Count);
                Assert.AreSame(mss, ms.GlobalBoundary.Modules[0]);
            });
        }

        [TestMethod]
        public void UndoRemoveModelSystemStructure()
        {
            RunInModelSystemContext("UndoRemoveModelSystemStructure", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.AreEqual(0, ms.GlobalBoundary.Modules.Count);
                Assert.IsTrue(mSession.AddModelSystemStructure(user, ms.GlobalBoundary, "Start", typeof(BasicParameter<int>),
                    out var mss, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Modules.Count);
                Assert.IsTrue(mSession.Undo(ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Modules.Count);
                Assert.IsTrue(mSession.Redo(ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Modules.Count);
                Assert.AreSame(mss, ms.GlobalBoundary.Modules[0]);

                // now remove model system structure explicitly
                Assert.IsTrue(mSession.RemoveModelSystemStructure(user, mss, ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Modules.Count);
                Assert.IsTrue(mSession.Undo(ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Modules.Count);
                Assert.IsTrue(mSession.Undo(ref error), error);
            });
        }

        [TestMethod]
        public void UndoAddLink()
        {
            RunInModelSystemContext("UndoAddLink", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.AreEqual(0, ms.GlobalBoundary.Modules.Count);
                Assert.IsTrue(mSession.AddModelSystemStructure(user, ms.GlobalBoundary, "Start", typeof(BasicParameter<string>),
                    out var parameter, ref error), error);
                Assert.IsTrue(mSession.AddModelSystemStructure(user, ms.GlobalBoundary, "Start", typeof(SimpleParameterModule),
                    out var module, ref error), error);
                Assert.AreEqual(2, ms.GlobalBoundary.Modules.Count);
                Assert.AreEqual(0, ms.GlobalBoundary.Links.Count);
                Assert.IsTrue(mSession.AddLink(user, module, module.Hooks[0], parameter, out var link, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Links.Count);
                Assert.IsTrue(mSession.Undo(ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Links.Count);
                Assert.IsTrue(mSession.Redo(ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Links.Count);
                Assert.AreSame(link, ms.GlobalBoundary.Links[0]);
            });
        }

        [TestMethod]
        public void UndoRemoveLink()
        {
            RunInModelSystemContext("UndoRemoveLink", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.AreEqual(0, ms.GlobalBoundary.Modules.Count);
                Assert.IsTrue(mSession.AddModelSystemStructure(user, ms.GlobalBoundary, "Start", typeof(BasicParameter<string>),
                    out var parameter, ref error), error);
                Assert.IsTrue(mSession.AddModelSystemStructure(user, ms.GlobalBoundary, "Start", typeof(SimpleParameterModule),
                    out var module, ref error), error);
                Assert.AreEqual(2, ms.GlobalBoundary.Modules.Count);
                Assert.AreEqual(0, ms.GlobalBoundary.Links.Count);
                Assert.IsTrue(mSession.AddLink(user, module, module.Hooks[0], parameter, out var link, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Links.Count);
                Assert.IsTrue(mSession.Undo(ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Links.Count);
                Assert.IsTrue(mSession.Redo(ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Links.Count);
                Assert.AreSame(link, ms.GlobalBoundary.Links[0]);

                // now remove the link explicitly
                Assert.IsTrue(mSession.RemoveLink(user, link, ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Links.Count);
                Assert.IsTrue(mSession.Undo(ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Links.Count);
                Assert.AreSame(link, ms.GlobalBoundary.Links[0]);
                Assert.IsTrue(mSession.Redo(ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Links.Count);
            });
        }

        [TestMethod]
        public void AddSingleLinkToDifferentModule()
        {
            /*
             * This test will try to assign a link between from a single hook to different modules.
             * This operation should remove the first link and then add the second
             */
            RunInModelSystemContext("AddSingleLinkToDifferentModule", (user, pSession, mSession) =>
            {
                // initialization
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.IsTrue(mSession.AddModelSystemStart(user, ms.GlobalBoundary, "FirstStart", out var start, ref error), error);
                Assert.IsTrue(mSession.AddModelSystemStructure(user, ms.GlobalBoundary, "MyMSS", typeof(SimpleTestModule), out var mss1, ref error));
                Assert.IsTrue(mSession.AddModelSystemStructure(user, ms.GlobalBoundary, "MyMSS", typeof(SimpleTestModule), out var mss2, ref error));
                Assert.IsTrue(mSession.AddLink(user, start, start.Hooks[0], mss1, out var link1, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Links.Count);
                // This should not create a new link but move the previous one
                Assert.IsTrue(mSession.AddLink(user, start, start.Hooks[0], mss2, out var link2, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Links.Count);
            });
        }

        [TestMethod]
        public void AddBoundary()
        {
            RunInModelSystemContext("AddBoundary", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.AreEqual(0, ms.GlobalBoundary.Boundaries.Count);
                Assert.IsTrue(mSession.AddBoundary(user, ms.GlobalBoundary, "UniqueName", out Boundary subB, ref error), error);
                Assert.IsFalse(mSession.AddBoundary(user, ms.GlobalBoundary, "UniqueName", out Boundary fail1, ref error), "Created a second boundary with the same name!");
                Assert.AreEqual(1, ms.GlobalBoundary.Boundaries.Count);
                Assert.IsTrue(mSession.Undo(ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Boundaries.Count);
                Assert.IsTrue(mSession.Redo(ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Boundaries.Count);
                Assert.AreSame(subB, ms.GlobalBoundary.Boundaries[0]);
                Assert.IsFalse(mSession.AddBoundary(user, ms.GlobalBoundary, "UniqueName", out Boundary fail2, ref error), "Created a second boundary with the same name after redo!");
            });
        }

        [TestMethod]
        public void RemoveBoundary()
        {
            RunInModelSystemContext("RemoveBoundary", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.AreEqual(0, ms.GlobalBoundary.Boundaries.Count);
                Assert.IsTrue(mSession.AddBoundary(user, ms.GlobalBoundary, "UniqueName", out Boundary subB, ref error), error);
                Assert.IsFalse(mSession.AddBoundary(user, ms.GlobalBoundary, "UniqueName", out Boundary fail1, ref error), "Created a second boundary with the same name!");
                Assert.AreEqual(1, ms.GlobalBoundary.Boundaries.Count);
                Assert.IsTrue(mSession.Undo(ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Boundaries.Count);
                Assert.IsTrue(mSession.Redo(ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Boundaries.Count);
                Assert.AreSame(subB, ms.GlobalBoundary.Boundaries[0]);
                Assert.IsFalse(mSession.AddBoundary(user, ms.GlobalBoundary, "UniqueName", out Boundary fail2, ref error), "Created a second boundary with the same name after redo!");

                // Now test removing the boundary explicitly
                Assert.IsTrue(mSession.RemoveBoundary(user, ms.GlobalBoundary, subB, ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Boundaries.Count);
                Assert.IsTrue(mSession.Undo(ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Boundaries.Count);
                Assert.AreSame(subB, ms.GlobalBoundary.Boundaries[0]);
                Assert.IsTrue(mSession.Redo(ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Boundaries.Count);
            });
        }

        [TestMethod]
        public void RemoveLinkToBoundariesThatWereRemoved()
        {
            RunInModelSystemContext("RemoveLinkToBoundariesThatWereRemoved", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                string error = null;
                var global = ms.GlobalBoundary;
                // Setup the delete
                Assert.IsTrue(mSession.AddBoundary(user, global, "ToRemove", out var toRemove, ref error), error);
                Assert.IsTrue(mSession.AddModelSystemStart(user, global, "Start", out var start, ref error), error);
                Assert.IsTrue(mSession.AddModelSystemStructure(user, toRemove, "Tricky", typeof(IgnoreResult<string>),
                    out var tricky, ref error), error);
                Assert.IsTrue(mSession.AddLink(user, start, start.Hooks[0], tricky, out var link, ref error), error);
                Assert.AreEqual(1, global.Starts.Count);
                Assert.AreEqual(1, global.Links.Count);
                Assert.AreEqual(1, toRemove.Modules.Count);

                // Now remove the boundary and check to make sure the number of links is cleaned up
                Assert.IsTrue(mSession.RemoveBoundary(user, global, toRemove, ref error), error);
                Assert.AreEqual(0, global.Links.Count, "We did not remove the link during the remove boundary!");
                Assert.IsTrue(mSession.Undo(ref error), error);
                Assert.AreEqual(1, global.Links.Count, "The link was not restored after the undo on the remove boundary!");
                Assert.IsTrue(mSession.Redo(ref error), error);
                Assert.AreEqual(0, global.Links.Count, "We did not remove the link again doing the redo of the remove boundary!");
            });
        }

        [TestMethod]
        public void RemoveMultiLinkToBoundariesThatWereRemoved()
        {
            RunInModelSystemContext("RemoveMultiLinkToBoundariesThatWereRemoved", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                string error = null;
                var global = ms.GlobalBoundary;
                // Setup the delete
                Assert.IsTrue(mSession.AddBoundary(user, global, "ToRemove", out var toRemove, ref error), error);
                Assert.IsTrue(mSession.AddModelSystemStart(user, global, "Start", out var start, ref error), error);
                Assert.IsTrue(mSession.AddModelSystemStructure(user, global, "Execute", typeof(Execute),
                    out var execute, ref error), error);
                Assert.IsTrue(mSession.AddModelSystemStructure(user, toRemove, "Tricky", typeof(IgnoreResult<string>),
                    out var tricky, ref error), error);
                Assert.IsTrue(mSession.AddLink(user, start, start.Hooks[0], execute, out var link, ref error), error);
                Assert.IsTrue(mSession.AddLink(user, execute, GetHook(execute.Hooks, "To Execute"), tricky, out var link2, ref error), error);
                Assert.AreEqual(1, global.Starts.Count);
                Assert.AreEqual(1, global.Modules.Count);
                Assert.AreEqual(2, global.Links.Count);
                Assert.AreEqual(1, toRemove.Modules.Count);

                // Now remove the boundary and check to make sure the number of links is cleaned up
                Assert.IsTrue(mSession.RemoveBoundary(user, global, toRemove, ref error), error);
                Assert.AreEqual(0, ((MultiLink)global.Links.First(l => l.Origin == execute)).Destinations.Count);
                Assert.IsTrue(mSession.Undo(ref error), error);
                Assert.AreEqual(1, ((MultiLink)global.Links.First(l => l.Origin == execute)).Destinations.Count);
                Assert.IsTrue(mSession.Redo(ref error), error);
                Assert.AreEqual(0, ((MultiLink)global.Links.First(l => l.Origin == execute)).Destinations.Count);
            });
        }

        [TestMethod]
        public void RemoveSingleDestinationInMultiLink()
        {
            RunInModelSystemContext("RemoveMultiLinkToBoundariesThatWereRemoved", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                string error = null;
                var global = ms.GlobalBoundary;
                Assert.IsTrue(mSession.AddModelSystemStart(user, global, "Start", out var start, ref error), error);
                Assert.IsTrue(mSession.AddModelSystemStructure(user, global, "Execute", typeof(Execute),
                    out var execute, ref error), error);
                Assert.IsTrue(mSession.AddModelSystemStructure(user, global, "Tricky", typeof(IgnoreResult<string>),
                    out var ignore, ref error), error);

                Assert.IsTrue(mSession.AddLink(user, start, start.Hooks[0], execute, out var link, ref error), error);
                // Create 2 links from the execute into the ignore
                Assert.IsTrue(mSession.AddLink(user, execute, GetHook(execute.Hooks, "To Execute"),
                    execute, out var linkI1, ref error), error);
                Assert.IsTrue(mSession.AddLink(user, execute, GetHook(execute.Hooks, "To Execute"),
                    execute, out var _, ref error), error);


                Assert.AreEqual(2, ((MultiLink)linkI1).Destinations.Count);
                Assert.IsTrue(mSession.RemoveLinkDestination(user, linkI1, 0, ref error), error);
                Assert.AreEqual(1, ((MultiLink)linkI1).Destinations.Count);
                Assert.IsTrue(mSession.Undo(ref error), error);
                Assert.AreEqual(2, ((MultiLink)linkI1).Destinations.Count);
                Assert.IsTrue(mSession.Redo(ref error), error);
                Assert.AreEqual(1, ((MultiLink)linkI1).Destinations.Count);
            });
        }
    }
}
