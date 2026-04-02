/*
    Copyright 2026 University of Toronto

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
using XTMF2.Editing;
using XTMF2.ModelSystemConstruct;

namespace XTMF2.UnitTests.Editing
{
    [TestClass]
    public class TestFunctionInstances
    {
        // ── Helpers ────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a FunctionTemplate on the global boundary and returns it.
        /// </summary>
        private static FunctionTemplate AddTemplate(User user, ModelSystemSession mSession,
            string templateName = "MyTemplate")
        {
            Assert.IsTrue(mSession.AddFunctionTemplate(user, mSession.ModelSystem.GlobalBoundary,
                templateName, out var template, out var error), error?.Message);
            return template!;
        }

        /// <summary>
        /// Creates a FunctionInstance on the global boundary of the given template.
        /// </summary>
        private static FunctionInstance AddInstance(User user, ModelSystemSession mSession,
            FunctionTemplate template, string instanceName = "MyInstance",
            Rectangle location = default)
        {
            if (location == default)
                location = new Rectangle(10f, 20f, 160f, 70f);

            Assert.IsTrue(mSession.AddFunctionInstance(user, mSession.ModelSystem.GlobalBoundary,
                template, instanceName, location, out var instance, out var error), error?.Message);
            return instance!;
        }

        // ── Add ────────────────────────────────────────────────────────────────

        [TestMethod]
        public void TestAddFunctionInstance()
        {
            TestHelper.RunInModelSystemContext("TestAddFunctionInstance", (user, pSession, mSession) =>
            {
                var template  = AddTemplate(user, mSession);
                var instances = mSession.ModelSystem.GlobalBoundary.FunctionInstances;

                Assert.IsEmpty(instances);
                var instance = AddInstance(user, mSession, template);

                Assert.HasCount(1, instances);
                Assert.AreEqual("MyInstance", instance.Name);
                Assert.AreSame(template, instance.Template);
            });
        }

        [TestMethod]
        public void TestAddFunctionInstanceUndoRedo()
        {
            TestHelper.RunInModelSystemContext("TestAddFunctionInstanceUndoRedo", (user, pSession, mSession) =>
            {
                CommandError error = null;
                var template  = AddTemplate(user, mSession);
                var instances = mSession.ModelSystem.GlobalBoundary.FunctionInstances;

                Assert.IsEmpty(instances);
                AddInstance(user, mSession, template);
                Assert.HasCount(1, instances);

                // Undo: instance should disappear
                Assert.IsTrue(mSession.Undo(user, out error), error?.Message);
                Assert.IsEmpty(instances);

                // Redo: instance returns
                Assert.IsTrue(mSession.Redo(user, out error), error?.Message);
                Assert.HasCount(1, instances);
            });
        }

        [TestMethod]
        public void TestAddMultipleFunctionInstances()
        {
            TestHelper.RunInModelSystemContext("TestAddMultipleFunctionInstances", (user, pSession, mSession) =>
            {
                var template  = AddTemplate(user, mSession);
                var instances = mSession.ModelSystem.GlobalBoundary.FunctionInstances;

                AddInstance(user, mSession, template, "Instance1");
                AddInstance(user, mSession, template, "Instance2");
                AddInstance(user, mSession, template, "Instance3");

                Assert.HasCount(3, instances);
                Assert.AreEqual("Instance1", instances[0].Name);
                Assert.AreEqual("Instance2", instances[1].Name);
                Assert.AreEqual("Instance3", instances[2].Name);
            });
        }

        [TestMethod]
        public void TestAddFunctionInstancePreservesLocation()
        {
            TestHelper.RunInModelSystemContext("TestAddFunctionInstancePreservesLocation", (user, pSession, mSession) =>
            {
                var template = AddTemplate(user, mSession);
                var location = new Rectangle(50f, 75f, 200f, 100f);
                var instance = AddInstance(user, mSession, template, location: location);

                Assert.AreEqual(50f,  instance.Location.X);
                Assert.AreEqual(75f,  instance.Location.Y);
                Assert.AreEqual(200f, instance.Location.Width);
                Assert.AreEqual(100f, instance.Location.Height);
            });
        }

        // ── Remove ─────────────────────────────────────────────────────────────

        [TestMethod]
        public void TestRemoveFunctionInstance()
        {
            TestHelper.RunInModelSystemContext("TestRemoveFunctionInstance", (user, pSession, mSession) =>
            {
                CommandError error = null;
                var template  = AddTemplate(user, mSession);
                var instances = mSession.ModelSystem.GlobalBoundary.FunctionInstances;

                var instance = AddInstance(user, mSession, template);
                Assert.HasCount(1, instances);

                Assert.IsTrue(mSession.RemoveFunctionInstance(user, instance, out error), error?.Message);
                Assert.IsEmpty(instances);
            });
        }

        [TestMethod]
        public void TestRemoveFunctionInstanceUndoRedo()
        {
            TestHelper.RunInModelSystemContext("TestRemoveFunctionInstanceUndoRedo", (user, pSession, mSession) =>
            {
                CommandError error = null;
                var template  = AddTemplate(user, mSession);
                var instances = mSession.ModelSystem.GlobalBoundary.FunctionInstances;

                var instance = AddInstance(user, mSession, template);
                Assert.HasCount(1, instances);

                Assert.IsTrue(mSession.RemoveFunctionInstance(user, instance, out error), error?.Message);
                Assert.IsEmpty(instances);

                // Undo: instance returns
                Assert.IsTrue(mSession.Undo(user, out error), error?.Message);
                Assert.HasCount(1, instances);

                // Redo: instance removed again
                Assert.IsTrue(mSession.Redo(user, out error), error?.Message);
                Assert.IsEmpty(instances);
            });
        }

        // ── Rename ─────────────────────────────────────────────────────────────

        [TestMethod]
        public void TestRenameFunctionInstance()
        {
            TestHelper.RunInModelSystemContext("TestRenameFunctionInstance", (user, pSession, mSession) =>
            {
                CommandError error = null;
                var template = AddTemplate(user, mSession);
                var instance = AddInstance(user, mSession, template, "OriginalName");

                Assert.AreEqual("OriginalName", instance.Name);
                Assert.IsTrue(mSession.RenameFunctionInstance(user, instance, "RenamedInstance", out error), error?.Message);
                Assert.AreEqual("RenamedInstance", instance.Name);
            });
        }

        [TestMethod]
        public void TestRenameFunctionInstanceUndoRedo()
        {
            TestHelper.RunInModelSystemContext("TestRenameFunctionInstanceUndoRedo", (user, pSession, mSession) =>
            {
                CommandError error = null;
                var template = AddTemplate(user, mSession);
                var instance = AddInstance(user, mSession, template, "OriginalName");

                Assert.IsTrue(mSession.RenameFunctionInstance(user, instance, "NewName", out error), error?.Message);
                Assert.AreEqual("NewName", instance.Name);

                // Undo: name reverts
                Assert.IsTrue(mSession.Undo(user, out error), error?.Message);
                Assert.AreEqual("OriginalName", instance.Name);

                // Redo: name returns to new value
                Assert.IsTrue(mSession.Redo(user, out error), error?.Message);
                Assert.AreEqual("NewName", instance.Name);
            });
        }

        [TestMethod]
        public void TestRenameFunctionInstanceRejectsEmptyName()
        {
            TestHelper.RunInModelSystemContext("TestRenameFunctionInstanceRejectsEmptyName", (user, pSession, mSession) =>
            {
                var template = AddTemplate(user, mSession);
                var instance = AddInstance(user, mSession, template, "ValidName");

                Assert.IsFalse(mSession.RenameFunctionInstance(user, instance, "", out _),
                    "Renaming with an empty string should fail.");
                Assert.AreEqual("ValidName", instance.Name, "Name should be unchanged after a failed rename.");
            });
        }

        [TestMethod]
        public void TestRenameFunctionInstanceRejectsWhitespaceName()
        {
            TestHelper.RunInModelSystemContext("TestRenameFunctionInstanceRejectsWhitespaceName", (user, pSession, mSession) =>
            {
                var template = AddTemplate(user, mSession);
                var instance = AddInstance(user, mSession, template, "ValidName");

                Assert.IsFalse(mSession.RenameFunctionInstance(user, instance, "   ", out _),
                    "Renaming with whitespace-only string should fail.");
                Assert.AreEqual("ValidName", instance.Name, "Name should be unchanged after a failed rename.");
            });
        }

        // ── SetLocation ────────────────────────────────────────────────────────

        [TestMethod]
        public void TestSetFunctionInstanceLocation()
        {
            TestHelper.RunInModelSystemContext("TestSetFunctionInstanceLocation", (user, pSession, mSession) =>
            {
                CommandError error = null;
                var template  = AddTemplate(user, mSession);
                var instance  = AddInstance(user, mSession, template, location: new Rectangle(0f, 0f, 120f, 50f));

                var newLocation = new Rectangle(300f, 400f, 220f, 110f);
                Assert.IsTrue(mSession.SetFunctionInstanceLocation(user, instance, newLocation, out error), error?.Message);

                Assert.AreEqual(300f, instance.Location.X);
                Assert.AreEqual(400f, instance.Location.Y);
                Assert.AreEqual(220f, instance.Location.Width);
                Assert.AreEqual(110f, instance.Location.Height);
            });
        }

        [TestMethod]
        public void TestSetFunctionInstanceLocationUndoRedo()
        {
            TestHelper.RunInModelSystemContext("TestSetFunctionInstanceLocationUndoRedo", (user, pSession, mSession) =>
            {
                CommandError error = null;
                var originalLocation = new Rectangle(10f, 20f, 160f, 70f);
                var template  = AddTemplate(user, mSession);
                var instance  = AddInstance(user, mSession, template, location: originalLocation);

                var newLocation = new Rectangle(500f, 600f, 240f, 120f);
                Assert.IsTrue(mSession.SetFunctionInstanceLocation(user, instance, newLocation, out error), error?.Message);
                Assert.AreEqual(500f, instance.Location.X);

                // Undo: location reverts
                Assert.IsTrue(mSession.Undo(user, out error), error?.Message);
                Assert.AreEqual(originalLocation.X, instance.Location.X);
                Assert.AreEqual(originalLocation.Y, instance.Location.Y);
                Assert.AreEqual(originalLocation.Width,  instance.Location.Width);
                Assert.AreEqual(originalLocation.Height, instance.Location.Height);

                // Redo: new location returns
                Assert.IsTrue(mSession.Redo(user, out error), error?.Message);
                Assert.AreEqual(newLocation.X, instance.Location.X);
                Assert.AreEqual(newLocation.Y, instance.Location.Y);
                Assert.AreEqual(newLocation.Width,  instance.Location.Width);
                Assert.AreEqual(newLocation.Height, instance.Location.Height);
            });
        }

        // ── Save / Load ────────────────────────────────────────────────────────

        [TestMethod]
        public void TestFunctionInstanceSave()
        {
            TestHelper.RunInModelSystemContext("TestFunctionInstanceSave",
                (user, pSession, mSession) =>
                {
                    CommandError error = null;
                    var template = AddTemplate(user, mSession, "SavedTemplate");
                    AddInstance(user, mSession, template, "SavedInstance",
                        new Rectangle(11f, 22f, 160f, 70f));
                    Assert.IsTrue(mSession.Save(out error), error?.Message);
                },
                (user, pSession, mSession) =>
                {
                    var boundary  = mSession.ModelSystem.GlobalBoundary;
                    Assert.HasCount(1, boundary.FunctionTemplates,
                        "FunctionTemplate was not reloaded.");
                    Assert.HasCount(1, boundary.FunctionInstances,
                        "FunctionInstance was not reloaded.");

                    var fi = boundary.FunctionInstances[0];
                    Assert.AreEqual("SavedInstance", fi.Name);
                    Assert.AreEqual("SavedTemplate",  fi.Template.Name);
                    Assert.AreEqual(11f, fi.Location.X);
                    Assert.AreEqual(22f, fi.Location.Y);
                });
        }

        [TestMethod]
        public void TestFunctionInstanceSaveMultiple()
        {
            TestHelper.RunInModelSystemContext("TestFunctionInstanceSaveMultiple",
                (user, pSession, mSession) =>
                {
                    CommandError error = null;
                    var template = AddTemplate(user, mSession, "MyTemplate");
                    AddInstance(user, mSession, template, "Alpha", new Rectangle(10f, 10f, 120f, 50f));
                    AddInstance(user, mSession, template, "Beta",  new Rectangle(20f, 20f, 140f, 60f));
                    Assert.IsTrue(mSession.Save(out error), error?.Message);
                },
                (user, pSession, mSession) =>
                {
                    var boundary  = mSession.ModelSystem.GlobalBoundary;
                    Assert.HasCount(2, boundary.FunctionInstances,
                        "Both FunctionInstances should be reloaded.");
                    Assert.AreEqual("Alpha", boundary.FunctionInstances[0].Name);
                    Assert.AreEqual("Beta",  boundary.FunctionInstances[1].Name);
                });
        }

        // ── Template reference integrity ──────────────────────────────────────

        [TestMethod]
        public void TestFunctionInstanceReferencesCorrectTemplate()
        {
            TestHelper.RunInModelSystemContext("TestFunctionInstanceReferencesCorrectTemplate", (user, pSession, mSession) =>
            {
                var templateA = AddTemplate(user, mSession, "TemplateA");
                var templateB = AddTemplate(user, mSession, "TemplateB");

                var instanceA = AddInstance(user, mSession, templateA, "InstanceA");
                var instanceB = AddInstance(user, mSession, templateB, "InstanceB");

                Assert.AreSame(templateA, instanceA.Template);
                Assert.AreSame(templateB, instanceB.Template);
            });
        }

        [TestMethod]
        public void TestFunctionInstanceContainedWithin()
        {
            TestHelper.RunInModelSystemContext("TestFunctionInstanceContainedWithin", (user, pSession, mSession) =>
            {
                var boundary = mSession.ModelSystem.GlobalBoundary;
                var template = AddTemplate(user, mSession);
                var instance = AddInstance(user, mSession, template);

                Assert.AreSame(boundary, instance.ContainedWithin);
            });
        }
    }
}
