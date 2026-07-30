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
using XTMF2.GUI.Tests.Modules;
using XTMF2.GUI.ViewModels;

namespace XTMF2.GUI.Tests.ViewModels;

[TestClass]
public class ModelSystemEditorViewModelDeleteTests
{
    [TestMethod]
    public void DeleteMultipleAsync_RemovesFunctionInstancesBeforeTemplates()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(DeleteMultipleAsync_RemovesFunctionInstancesBeforeTemplates),
            (user, projectSession, msSession) =>
            {
                var boundary = msSession.ModelSystem.GlobalBoundary;

                Assert.IsTrue(msSession.AddFunctionTemplate(user, boundary, "TemplateA",
                    out var template, out var error), error?.Message);
                Assert.IsNotNull(template);

                // Give template internals some content so we exercise normal template lifetime.
                Assert.IsTrue(msSession.AddModelSystemStart(user, template!.InternalModules, "Entry",
                    new Rectangle(20, 20, 160, 60), out var entry, out error), error?.Message);
                Assert.IsNotNull(entry);
                Assert.IsTrue(msSession.SetFunctionTemplateEntryNode(user, template, entry, out error),
                    error?.Message);
                Assert.IsNotNull(projectSession);
                Assert.IsTrue(msSession.AddFunctionParameter(user, template, "Input",
                    typeof(SimpleGuiTestModule), new Rectangle(40, 120, 150, 50), out var fp, out error),
                    error?.Message);
                Assert.IsNotNull(fp);

                Assert.IsTrue(msSession.AddFunctionInstance(user, boundary, template,
                    "InstanceA", new Rectangle(300, 220, 160, 70), out var instance, out error), error?.Message);
                Assert.IsNotNull(instance);

                using var vmEditor = new ModelSystemEditorViewModel(msSession, user, runController: null);

                var ftvm = vmEditor.FunctionTemplates.FirstOrDefault(ft => ReferenceEquals(ft.UnderlyingTemplate, template));
                var fivm = vmEditor.FunctionInstances.FirstOrDefault(fi => ReferenceEquals(fi.UnderlyingInstance, instance));
                Assert.IsNotNull(ftvm);
                Assert.IsNotNull(fivm);

                // Deliberately pass template before instance; DeleteMultipleAsync must reorder safely.
                vmEditor.DeleteMultipleAsync(new ICanvasElement[] { ftvm!, fivm! })
                    .GetAwaiter().GetResult();

                Assert.IsEmpty(boundary.FunctionInstances,
                    "FunctionInstances should be removed first, allowing template removal to succeed.");
                Assert.IsEmpty(boundary.FunctionTemplates,
                    "FunctionTemplate should be removed after dependent instances are gone.");
            });
    }
}
