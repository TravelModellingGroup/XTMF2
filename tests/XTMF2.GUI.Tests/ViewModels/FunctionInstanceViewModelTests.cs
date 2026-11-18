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
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Editing;
using XTMF2.GUI.ViewModels;

namespace XTMF2.GUI.Tests.ViewModels;

[TestClass]
public class FunctionInstanceViewModelTests
{
    [TestMethod]
    public void SearchItems_TracksFunctionInstanceInlining()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(SearchItems_TracksFunctionInstanceInlining),
            (user, projectSession, session) =>
            {
                var boundary = session.ModelSystem.GlobalBoundary;
                Assert.IsTrue(session.AddFunctionTemplate(
                    user, boundary, "Template", out var template, out var error), error?.Message);
                Assert.IsTrue(session.AddFunctionInstance(
                    user, boundary, template!, "Instance", new Rectangle(10, 20, 200, 80),
                    out _, out error), error?.Message);

                using var editor = new ModelSystemEditorViewModel(session, user, runController: null);
                var instance = editor.FunctionInstances.Single();
                Assert.Contains(instance, editor.SearchItems);

                instance.Inline();
                Assert.DoesNotContain(instance, editor.SearchItems);

                instance.ExpandToCanvas(300, 400);
                Assert.Contains(instance, editor.SearchItems);
            });
    }

    [TestMethod]
    public void InlineAndExpand_PreservesUndoRedo()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(InlineAndExpand_PreservesUndoRedo),
            (user, _, session) =>
            {
                var boundary = session.ModelSystem.GlobalBoundary;
                Assert.IsTrue(session.AddFunctionTemplate(
                    user, boundary, "Template", out var template, out var error), error?.Message);
                Assert.IsNotNull(template);

                var originalLocation = new Rectangle(10, 20, 200, 80);
                Assert.IsTrue(session.AddFunctionInstance(
                    user, boundary, template!, "Instance", originalLocation,
                    out var instance, out error), error?.Message);
                Assert.IsNotNull(instance);

                var vm = new FunctionInstanceViewModel(instance!, session, user);
                vm.Inline();

                Assert.IsTrue(vm.IsInlined);
                Assert.IsTrue(session.Undo(user, out error), error?.Message);
                Assert.AreEqual(originalLocation, instance!.Location);
                Assert.IsTrue(session.Redo(user, out error), error?.Message);
                Assert.IsTrue(vm.IsInlined);

                vm.ExpandToCanvas(300, 400);
                Assert.IsFalse(vm.IsInlined);
                Assert.AreEqual(300f, instance!.Location.X);
                Assert.AreEqual(400f, instance.Location.Y);
                Assert.IsTrue(session.Undo(user, out error), error?.Message);
                Assert.IsTrue(vm.IsInlined);
            });
    }
}
