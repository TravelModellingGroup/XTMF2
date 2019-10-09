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
using System.IO;
using System.Threading;
using TestXTMF;
using TestXTMF.Modules;
using XTMF2.ModelSystemConstruct;
using XTMF2.RuntimeModules;

namespace XTMF2.Editing
{
    [TestClass]
    public class TestNode
    {
        [TestMethod]
        public void AddStart()
        {
            TestHelper.RunInModelSystemContext("AddStart", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.AreEqual(0, ms.GlobalBoundary.Starts.Count);
                Assert.IsTrue(mSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", out var Start, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Starts.Count);
            });
        }

        [TestMethod]
        public void AddStartWithBadUser()
        {
            TestHelper.RunInModelSystemContext("AddStartWithBadUser", (user, unauthorizedUser, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.AreEqual(0, ms.GlobalBoundary.Starts.Count);
                Assert.IsFalse(mSession.AddModelSystemStart(unauthorizedUser, ms.GlobalBoundary, "Start", out var Start, ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Starts.Count);
            });
        }

        [TestMethod]
        public void UndoAddStart()
        {
            TestHelper.RunInModelSystemContext("UndoAddStart", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.AreEqual(0, ms.GlobalBoundary.Starts.Count);
                Assert.IsTrue(mSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", out var Start, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Starts.Count);
                Assert.IsTrue(mSession.Undo(user, ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Starts.Count);
                Assert.IsTrue(mSession.Redo(user, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Starts.Count);
                Assert.AreSame(Start, ms.GlobalBoundary.Starts[0]);
            });
        }

        [TestMethod]
        public void RemoveStart()
        {
            TestHelper.RunInModelSystemContext("UndoAddStart", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.AreEqual(0, ms.GlobalBoundary.Starts.Count);
                Assert.IsTrue(mSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", out var Start, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Starts.Count);
                Assert.IsTrue(mSession.Undo(user, ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Starts.Count);
                Assert.IsTrue(mSession.Redo(user, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Starts.Count);
                Assert.AreSame(Start, ms.GlobalBoundary.Starts[0]);

                //now test explicitly removing the start
                Assert.IsTrue(mSession.RemoveStart(user, Start, ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Starts.Count);
            });
        }

        [TestMethod]
        public void RemoveStartWithBadUser()
        {
            TestHelper.RunInModelSystemContext("RemoveStartWithBadUser", (user, unauthorizedUser, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.AreEqual(0, ms.GlobalBoundary.Starts.Count);
                Assert.IsTrue(mSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", out var Start, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Starts.Count);
                Assert.IsTrue(mSession.Undo(user, ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Starts.Count);
                Assert.IsTrue(mSession.Redo(user, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Starts.Count);
                Assert.AreSame(Start, ms.GlobalBoundary.Starts[0]);

                //now test explicitly removing the start
                Assert.IsFalse(mSession.RemoveStart(unauthorizedUser, Start, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Starts.Count);
            });
        }

        [TestMethod]
        public void UndoRemoveStart()
        {
            TestHelper.RunInModelSystemContext("UndoAddStart", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.AreEqual(0, ms.GlobalBoundary.Starts.Count);
                Assert.IsTrue(mSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", out var Start, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Starts.Count);
                Assert.IsTrue(mSession.Undo(user, ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Starts.Count);
                Assert.IsTrue(mSession.Redo(user, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Starts.Count);
                Assert.AreSame(Start, ms.GlobalBoundary.Starts[0]);

                //now test explicitly removing the start
                Assert.IsTrue(mSession.RemoveStart(user, Start, ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Starts.Count);
                Assert.IsTrue(mSession.Undo(user, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Starts.Count);
                Assert.IsTrue(mSession.Redo(user, ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Starts.Count);
            });
        }

        [TestMethod]
        public void AddNode()
        {
            TestHelper.RunInModelSystemContext("AddNode", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.AreEqual(0, ms.GlobalBoundary.Modules.Count);
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Start", typeof(BasicParameter<int>),
                    out var mss, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Modules.Count);
            });
        }

        [TestMethod]
        public void AddNodeWithBadUser()
        {
            TestHelper.RunInModelSystemContext("AddNodeWithBadUser", (user, unauthorizedUser, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.AreEqual(0, ms.GlobalBoundary.Modules.Count);
                Assert.IsFalse(mSession.AddNode(unauthorizedUser, ms.GlobalBoundary, "Start", typeof(BasicParameter<int>),
                    out var mss, ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Modules.Count);
            });
        }

        [TestMethod]
        public void UndoAddNode()
        {
            TestHelper.RunInModelSystemContext("UndoAddNode", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.AreEqual(0, ms.GlobalBoundary.Modules.Count);
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Start", typeof(BasicParameter<int>),
                    out var mss, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Modules.Count);
                Assert.IsTrue(mSession.Undo(user, ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Modules.Count);
                Assert.IsTrue(mSession.Redo(user, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Modules.Count);
                Assert.AreSame(mss, ms.GlobalBoundary.Modules[0]);
            });
        }

        [TestMethod]
        public void RemoveNode()
        {
            TestHelper.RunInModelSystemContext("RemoveNode", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.AreEqual(0, ms.GlobalBoundary.Modules.Count);
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Start", typeof(BasicParameter<int>),
                    out var mss, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Modules.Count);
                Assert.IsTrue(mSession.Undo(user, ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Modules.Count);
                Assert.IsTrue(mSession.Redo(user, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Modules.Count);
                Assert.AreSame(mss, ms.GlobalBoundary.Modules[0]);

                // now remove node explicitly
                Assert.IsTrue(mSession.RemoveNode(user, mss, ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Modules.Count);
            });
        }

        [TestMethod]
        public void RemoveNodeWithBadUser()
        {
            TestHelper.RunInModelSystemContext("RemoveNodeWithBadUser", (user, unauthorizedUser, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.AreEqual(0, ms.GlobalBoundary.Modules.Count);
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Start", typeof(BasicParameter<int>),
                    out var mss, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Modules.Count);
                Assert.IsTrue(mSession.Undo(user, ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Modules.Count);
                Assert.IsTrue(mSession.Redo(user, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Modules.Count);
                Assert.AreSame(mss, ms.GlobalBoundary.Modules[0]);

                // now remove node explicitly
                Assert.IsFalse(mSession.RemoveNode(unauthorizedUser, mss, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Modules.Count);
            });
        }

        [TestMethod]
        public void UndoRemoveNode()
        {
            TestHelper.RunInModelSystemContext("UndoRemoveNode", (user, pSession, mSession) =>
            {
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.AreEqual(0, ms.GlobalBoundary.Modules.Count);
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "Start", typeof(BasicParameter<int>),
                    out var mss, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Modules.Count);
                Assert.IsTrue(mSession.Undo(user, ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Modules.Count);
                Assert.IsTrue(mSession.Redo(user, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Modules.Count);
                Assert.AreSame(mss, ms.GlobalBoundary.Modules[0]);

                // now remove node explicitly
                Assert.IsTrue(mSession.RemoveNode(user, mss, ref error), error);
                Assert.AreEqual(0, ms.GlobalBoundary.Modules.Count);
                Assert.IsTrue(mSession.Undo(user, ref error), error);
                Assert.AreEqual(1, ms.GlobalBoundary.Modules.Count);
                Assert.IsTrue(mSession.Undo(user, ref error), error);
            });
        }

        /// <summary>
        /// Test the case where we want to add a node and all parameters filled in automatically.
        /// </summary>
        [TestMethod]
        public void AddNodeWithParameterGeneration()
        {
            TestHelper.RunInModelSystemContext("AddNodeWithParameterGeneration", (user, pSession, msSession) =>
            {
                string error = null;
                var ms = msSession.ModelSystem;
                var gBound = ms.GlobalBoundary;
                Assert.IsTrue(msSession.AddNodeGenerateParameters(user, ms.GlobalBoundary, "Test",
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
            });
        }

        /// <summary>
        /// Test the case where we want to add a node and all parameters filled in automatically.
        /// </summary>
        [TestMethod]
        public void AddNodeWithParameterGenerationWithBadUser()
        {
            TestHelper.RunInModelSystemContext("AddNodeWithParameterGenerationWithBadUser", (user, unauthorizedUser, pSession, msSession) =>
            {
                string error = null;
                var ms = msSession.ModelSystem;
                var gBound = ms.GlobalBoundary;
                Assert.IsFalse(msSession.AddNodeGenerateParameters(unauthorizedUser, ms.GlobalBoundary, "Test",
                    typeof(SimpleParameterModule), out var node, out var children, ref error), error);
                // Test to make sure that there was a second module also added.
                Assert.IsNull(children, "The child parameters of the node were returned as a null!");
                var modules = gBound.Modules;
                var links = gBound.Links;
                Assert.AreEqual(0, modules.Count, "A module was created by an invalid user!");
                Assert.AreEqual(0, links.Count, "A link was created by an invalid user!");
            });
        }

        /// <summary>
        /// Test the case where we want to add a node and all parameters and in addition
        /// test that the undo and redo functionality removed and rebuilds the state properly.
        /// </summary>
        [TestMethod]
        public void AddNodeWithParameterGenerationUndo()
        {
            TestHelper.RunInModelSystemContext("AddNodeWithParameterGenerationUndo", (user, pSession, msSession) =>
            {
                string error = null;
                var ms = msSession.ModelSystem;
                var gBound = ms.GlobalBoundary;
                Assert.IsTrue(msSession.AddNodeGenerateParameters(user, ms.GlobalBoundary, "Test",
                    typeof(SimpleParameterModule), out var node, out var children, ref error), error);
                // Test to make sure that there was a second module also added.
                Assert.IsNotNull(children, "The child parameters of the node were returned as a null!");
                Assert.AreEqual(1, children.Count);
                var modules = gBound.Modules;
                var links = gBound.Links;
                Assert.AreEqual(2, modules.Count, "It seems that the child parameter was not contained in the global boundary.");
                Assert.AreEqual(1, links.Count, "We did not have a link!");
                Assert.IsTrue(msSession.Undo(user, ref error), error);
                Assert.AreEqual(0, modules.Count, "After undoing it seems that a module has survived.");
                Assert.AreEqual(0, links.Count, "The link was not removed on undo.");
                Assert.IsTrue(msSession.Redo(user, ref error), error);
                Assert.AreEqual(2, modules.Count, "After redoing it seems that a module was not restored.");
                Assert.AreEqual(1, links.Count, "The link was not re-added on redo.");
            });
        }

        /// <summary>
        /// Test the case where we want to remove a node and all simple parameters nodes and links connecting them.
        /// </summary>
        [TestMethod]
        public void RemoveNodeWithParameterGeneration()
        {
            TestHelper.RunInModelSystemContext("RemoveNodeWithParameterGeneration", (user, pSession, msSession) =>
            {
                string error = null;
                var ms = msSession.ModelSystem;
                var gBound = ms.GlobalBoundary;
                Assert.IsTrue(msSession.AddNodeGenerateParameters(user, ms.GlobalBoundary, "Test",
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

                Assert.IsTrue(msSession.RemoveNodeGenerateParameters(user, node, ref error), error);

                // Make sure that both modules were deleted
                Assert.AreEqual(0, modules.Count, "Both modules were not removed.");
                Assert.IsTrue(msSession.Undo(user, ref error), error);
                Assert.AreEqual(2, modules.Count, "Both modules were not re-added.");

                Assert.IsTrue(msSession.Redo(user, ref error), error);
                Assert.AreEqual(0, modules.Count, "Both modules were not removed again.");
            });
        }

        /// <summary>
        /// Test the case where we want to remove a node and all simple parameters nodes and links connecting them.
        /// </summary>
        [TestMethod]
        public void RemoveNodeWithParameterGenerationWithBadUser()
        {
            TestHelper.RunInModelSystemContext("RemoveNodeWithParameterGenerationWithBadUser", (user, unauthorizedUser, pSession, msSession) =>
            {
                string error = null;
                var ms = msSession.ModelSystem;
                var gBound = ms.GlobalBoundary;
                Assert.IsTrue(msSession.AddNodeGenerateParameters(user, ms.GlobalBoundary, "Test",
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
                Assert.IsFalse(msSession.RemoveNodeGenerateParameters(unauthorizedUser, node, ref error), error);
                Assert.AreEqual(2, modules.Count, "The number of modules changed after an invalid user invoked RemoveNodeGenerateParameters.");
                Assert.AreEqual(1, links.Count, "The number of links changed after an invalid user invoked RemoveNodeGenerateParameters!");
            });
        }

        /// <summary>
        /// Test the case where we want to remove a node and all simple parameters nodes and links connecting them
        /// where the basic parameter has been made complex by adding another node that references the first node's
        /// parameter.
        /// </summary>
        [TestMethod]
        public void RemoveNodeWithParameterGenerationNotRemovingIfMultiple()
        {
            TestHelper.RunInModelSystemContext("RemoveNodeWithParameterGenerationWithBadUser", (user, unauthorizedUser, pSession, msSession) =>
            {
                string error = null;
                var ms = msSession.ModelSystem;
                var gBound = ms.GlobalBoundary;
                Assert.IsTrue(msSession.AddNodeGenerateParameters(user, ms.GlobalBoundary, "Test",
                    typeof(SimpleParameterModule), out var node, out var children, ref error), error);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Test",
                    typeof(SimpleParameterModule), out var node2, ref error), error);
                // Test to make sure that there was a second module also added.
                Assert.IsNotNull(children, "The child parameters of the node were returned as a null!");
                Assert.AreEqual(1, children.Count);
                var modules = gBound.Modules;
                var links = gBound.Links;
                Assert.AreEqual(1, links.Count);
                Assert.AreEqual(3, modules.Count);
                Assert.IsTrue(msSession.AddLink(user, node2, node2.Hooks[0], children[0], out var node2Link, ref error), error);
                Assert.AreEqual(2, links.Count, "The second link was not added");
                Assert.IsTrue(msSession.RemoveNodeGenerateParameters(user, node, ref error), error);
                Assert.AreEqual(1, links.Count);
                Assert.AreEqual(2, modules.Count);
                Assert.IsTrue(msSession.Undo(user, ref error), error);
                Assert.AreEqual(2, links.Count);
                Assert.AreEqual(3, modules.Count);
                Assert.IsTrue(msSession.Redo(user, ref error), error);
                Assert.AreEqual(1, links.Count);
                Assert.AreEqual(2, modules.Count);
            });
        }

        [TestMethod]
        public void SetParameterValue()
        {
            TestHelper.RunInModelSystemContext("SetParameterValue", (user, pSession, msSession) =>
            {
                string error2 = null;
                var ms = msSession.ModelSystem;
                var parameterValue = "Hello World Parameter";
                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", out Start start, ref error2), error2);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "AnIgnore", typeof(IgnoreResult<string>), out var ignoreMSS, ref error2), error2);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "SPM", typeof(SimpleParameterModule), out var spm, ref error2), error2);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "MyParameter", typeof(BasicParameter<string>), out var basicParameter, ref error2), error2);
                Assert.IsTrue(msSession.SetParameterValue(user, basicParameter, parameterValue, ref error2), error2);
                Assert.AreEqual(parameterValue, basicParameter.ParameterValue, "The value of the parameter was not set correctly."); 
            });
        }

        [TestMethod]
        public void SetParameterValueWithBadUser()
        {
            TestHelper.RunInModelSystemContext("SetParameterValueWithBadUser", (user, unauthorizedUser, pSession, msSession) =>
            {
                string error2 = null;
                var ms = msSession.ModelSystem;
                var parameterValue = "Hello World Parameter";
                var badParameterValue = "HackerValue";
                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", out Start start, ref error2), error2);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "AnIgnore", typeof(IgnoreResult<string>), out var ignoreMSS, ref error2), error2);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "SPM", typeof(SimpleParameterModule), out var spm, ref error2), error2);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "MyParameter", typeof(BasicParameter<string>), out var basicParameter, ref error2), error2);
                Assert.IsTrue(msSession.SetParameterValue(user, basicParameter, parameterValue, ref error2), error2);
                Assert.AreEqual(parameterValue, basicParameter.ParameterValue, "The value of the parameter was not set correctly.");

                Assert.IsFalse(msSession.SetParameterValue(unauthorizedUser, basicParameter, badParameterValue, ref error2), error2);
                Assert.AreEqual(parameterValue, basicParameter.ParameterValue, "The unauthorized user changed the parameter's value!");
            });
        }

        [TestMethod]
        public void DisablingNode()
        {
            TestHelper.RunInModelSystemContext("DisablingNode", (user, pSession, mSession) =>
            {
                // initialization
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "MyMSS", typeof(SimpleTestModule), out var mss, ref error));
                Assert.AreEqual("MyMSS", mss.Name);
                Assert.IsFalse(mss.IsDisabled, "The node started out as disabled!");
                Assert.IsTrue(mSession.SetNodeDisabled(user, mss, true, ref error), error);
                Assert.IsTrue(mss.IsDisabled, "The node was not disabled!");
                Assert.IsTrue(mSession.Undo(user, ref error), error);
                Assert.IsFalse(mss.IsDisabled, "The node was not re-enabled when undoing the disable instruction!");
                Assert.IsTrue(mSession.Redo(user, ref error), error);
                Assert.IsTrue(mss.IsDisabled, "The node was not disabled during the redo!");
            }, (user, pSession, mSession) =>
            {
                // after shutdown
                var ms = mSession.ModelSystem;
                var modules = ms.GlobalBoundary.Modules;
                Assert.AreEqual(1, modules.Count);
                Assert.IsTrue(modules[0].IsDisabled, "The module was not disabled after reloading the model system!");
            });
        }

        [TestMethod]
        public void DisablingNodeWithBadUser()
        {
            TestHelper.RunInModelSystemContext("DisablingNodeWithBadUser", (user, unauthroizedUser, pSession, mSession) =>
            {
                // initialization
                var ms = mSession.ModelSystem;
                string error = null;
                Assert.IsTrue(mSession.AddNode(user, ms.GlobalBoundary, "MyMSS", typeof(SimpleTestModule), out var mss, ref error));
                Assert.AreEqual("MyMSS", mss.Name);
                Assert.IsFalse(mss.IsDisabled, "The node started out as disabled!");
                Assert.IsFalse(mSession.SetNodeDisabled(unauthroizedUser, mss, true, ref error), error);
            });
        }

        [TestMethod]
        public void DisabledNodeRunValidationFailure()
        {
            TestHelper.RunInModelSystemContext("DisabledNodeRunValidationFailure", (user, pSession, msSession) =>
            {
                string error2 = null;
                var ms = msSession.ModelSystem;
                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", out Start start, ref error2), error2);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "AnIgnore", typeof(IgnoreResult<string>), out var ignoreMSS, ref error2), error2);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "SPM", typeof(SimpleParameterModule), out var spm, ref error2), error2);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "MyParameter", typeof(BasicParameter<string>), out var basicParameter, ref error2), error2);
                Assert.IsTrue(msSession.SetNodeDisabled(user, basicParameter, true, ref error2), error2);
                Assert.IsTrue(msSession.SetParameterValue(user, basicParameter, "Hello World Parameter", ref error2), error2);
                Assert.IsTrue(msSession.AddLink(user, start, start.Hooks[0], ignoreMSS, out var ignoreLink1, ref error2), error2);
                Assert.IsTrue(msSession.AddLink(user, ignoreMSS, ignoreMSS.Hooks[0], spm, out var ignoreLink2, ref error2), error2);
                Assert.IsTrue(msSession.AddLink(user, spm, spm.Hooks[0], basicParameter, out var ignoreLink3, ref error2), error2);
                TestHelper.CreateRunClient(true, (runBus) =>
                {
                    string error = null;
                    bool success = false;
                    using (SemaphoreSlim sim = new SemaphoreSlim(0))
                    {
                        runBus.ClientFinishedModelSystem += (sender, e) =>
                        {
                            success = true;
                            sim.Release();
                        };
                        runBus.ClientErrorWhenRunningModelSystem += (sender, runId, e, stack) =>
                        {
                            error = e + "\r\n" + stack;
                            sim.Release();
                        };
                        Assert.IsTrue(runBus.RunModelSystem(msSession, Path.Combine(pSession.RunsDirectory, "CreatingClient"), "Start", out var id, ref error), error);
                        // give the models system some time to complete
                        if (!sim.Wait(2000))
                        {
                            Assert.Fail("The model system failed to execute in time!");
                        }
                        Assert.IsFalse(success, "The model system finished running instead of having a validation error!");
                    }
                });
            });
        }
    }
}
