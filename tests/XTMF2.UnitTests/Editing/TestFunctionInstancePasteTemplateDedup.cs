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
using XTMF2.ModelSystemConstruct;
using XTMF2.RuntimeModules;

namespace XTMF2.UnitTests.Editing;

/// <summary>
/// Verifies cross-model-system FunctionInstance paste behavior reuses an equivalent
/// FunctionTemplate instead of creating a duplicate.
/// </summary>
[TestClass]
public class TestFunctionInstancePasteTemplateDedup
{
    private static FunctionTemplate BuildTemplate(
        User user,
        ModelSystemSession session,
        string templateName,
        Rectangle templateLocation,
        Rectangle entryLocation,
        Rectangle parameterLocation)
    {
        var boundary = session.ModelSystem.GlobalBoundary;

        Assert.IsTrue(session.AddFunctionTemplate(user, boundary, templateName,
            out var template, out var error), error?.Message);
        Assert.IsNotNull(template);

        Assert.IsTrue(session.SetFunctionTemplateLocation(user, template!, templateLocation, out error),
            error?.Message);

        Assert.IsTrue(session.AddModelSystemStart(user, template!.InternalModules, "Entry",
            entryLocation, out var entry, out error), error?.Message);
        Assert.IsNotNull(entry);

        Assert.IsTrue(session.SetFunctionTemplateEntryNode(user, template, entry, out error),
            error?.Message);

        Assert.IsTrue(session.AddFunctionParameter(user, template, "Input",
            typeof(IFunction<string>), parameterLocation, out _, out error), error?.Message);

        return template;
    }

    [TestMethod]
    public void FunctionInstancePasteAcrossModelSystems_ReusesEquivalentTemplate_IgnoresInternalNodePositions()
    {
        TestHelper.RunInProjectContext(
            nameof(FunctionInstancePasteAcrossModelSystems_ReusesEquivalentTemplate_IgnoresInternalNodePositions),
            (user, projectSession) =>
            {
                CommandError error = null;
                string snapshot = null;

                Assert.IsTrue(projectSession.CreateNewModelSystem(user, "SourceModel", out var sourceHeader, out error),
                    error?.Message);
                Assert.IsTrue(projectSession.CreateNewModelSystem(user, "TargetModel", out var targetHeader, out error),
                    error?.Message);

                Assert.IsTrue(projectSession.EditModelSystem(user, sourceHeader!, out var sourceSession, out error)
                    .UsingIf(sourceSession, () =>
                    {
                        var sourceTemplate = BuildTemplate(
                            user,
                            sourceSession!,
                            "SharedTemplate",
                            new Rectangle(20, 20, 260, 160),
                            new Rectangle(30, 40, 160, 60),
                            new Rectangle(50, 120, 150, 50));

                        Assert.IsTrue(sourceSession.ExportFunctionTemplateSnapshot(
                            sourceTemplate, out snapshot, out error), error?.Message);
                        Assert.IsNotNull(snapshot);
                    }), error?.Message);

                Assert.IsTrue(projectSession.EditModelSystem(user, targetHeader!, out var targetSession, out error)
                    .UsingIf(targetSession, () =>
                    {
                        var targetTemplate = BuildTemplate(
                            user,
                            targetSession!,
                            "SharedTemplate",
                            new Rectangle(440, 220, 280, 180),
                            new Rectangle(510, 310, 160, 60),
                            new Rectangle(560, 380, 150, 50));

                        var targetBoundary = targetSession!.ModelSystem.GlobalBoundary;
                        Assert.HasCount(1, targetBoundary.FunctionTemplates,
                            "Precondition: target model should start with exactly one template.");

                        // Emulate FunctionInstance paste resolution logic:
                        // 1) Search for equivalent template (ignoring positions)
                        // 2) Reuse if found; import only when missing.
                        Assert.IsTrue(targetSession.TryFindEquivalentFunctionTemplateSnapshot(
                            snapshot!, out var existingTemplate, out error), error?.Message);
                        Assert.IsNotNull(existingTemplate,
                            "Equivalent template must be found so paste can avoid duplication.");

                        var selectedTemplate = existingTemplate!;
                        Assert.IsTrue(targetSession.AddFunctionInstance(user, targetBoundary,
                            selectedTemplate, "PastedFunctionInstance",
                            new Rectangle(700, 360, 160, 70), out var fi, out error), error?.Message);
                        Assert.IsNotNull(fi);

                        Assert.HasCount(1, targetBoundary.FunctionTemplates,
                            "Pasting a function instance with an equivalent template must not create a duplicate template.");
                        Assert.AreSame(targetTemplate, selectedTemplate,
                            "Paste should reuse the already-present equivalent template in the target model system.");

                        Assert.HasCount(1, targetBoundary.FunctionInstances);
                        Assert.AreSame(selectedTemplate, targetBoundary.FunctionInstances.Single().Template);
                    }), error?.Message);
            });
    }

    [TestMethod]
    public void FunctionInstancePasteAcrossModelSystems_ImportsEmbeddedTemplateWithInternals()
    {
        TestHelper.RunInProjectContext(
            nameof(FunctionInstancePasteAcrossModelSystems_ImportsEmbeddedTemplateWithInternals),
            (user, projectSession) =>
            {
                CommandError error = null;
                string snapshot = null;

                Assert.IsTrue(projectSession.CreateNewModelSystem(user, "SourceModelEmbedded", out var sourceHeader, out error),
                    error?.Message);
                Assert.IsTrue(projectSession.CreateNewModelSystem(user, "TargetModelEmbedded", out var targetHeader, out error),
                    error?.Message);

                Assert.IsTrue(projectSession.EditModelSystem(user, sourceHeader!, out var sourceSession, out error)
                    .UsingIf(sourceSession, () =>
                    {
                        var template = BuildTemplate(
                            user,
                            sourceSession!,
                            "EmbeddedTemplate",
                            new Rectangle(40, 50, 280, 180),
                            new Rectangle(65, 95, 160, 60),
                            new Rectangle(70, 150, 155, 50));

                        // Add an internal node and a link to ensure contained graph data is carried.
                        Assert.IsTrue(sourceSession!.AddNode(user, template.InternalModules, "InnerNode",
                            typeof(BasicParameter<string>), new Rectangle(180, 120, 130, 50),
                            out var innerNode, out error), error?.Message);
                        Assert.IsNotNull(innerNode);

                        var fp = template.FunctionParameters.Single();
                        var entry = template.EntryNode;
                        Assert.IsNotNull(entry);
                        var hook = entry!.Hooks.FirstOrDefault(h => h.Name == "ToExecute") ?? entry.Hooks.First();

                        Assert.IsTrue(sourceSession.AddLink(user, entry, hook, fp,
                            out _, out error), error?.Message);

                        Assert.IsTrue(sourceSession.ExportFunctionTemplateSnapshot(
                            template, out snapshot, out error), error?.Message);
                        Assert.IsNotNull(snapshot);
                    }), error?.Message);

                Assert.IsTrue(projectSession.EditModelSystem(user, targetHeader!, out var targetSession, out error)
                    .UsingIf(targetSession, () =>
                    {
                        var targetBoundary = targetSession!.ModelSystem.GlobalBoundary;
                        Assert.IsEmpty(targetBoundary.FunctionTemplates,
                            "Precondition: target model should not have templates before import.");

                        Assert.IsTrue(targetSession.ImportFunctionTemplateSnapshot(
                            user,
                            targetBoundary,
                            snapshot,
                            "EmbeddedTemplate",
                            new Rectangle(220, 180, 280, 180),
                            out var imported,
                            out error), error?.Message);
                        Assert.IsNotNull(imported);

                        Assert.HasCount(1, targetBoundary.FunctionTemplates,
                            "Embedded FunctionTemplate should be imported into target model system.");
                        Assert.HasCount(1, imported!.FunctionParameters,
                            "Imported template should preserve FunctionParameters.");
                        Assert.IsNotNull(imported.EntryNode,
                            "Imported template should preserve entry-node designation.");
                        Assert.IsNotEmpty(imported.InternalModules.Modules,
                            "Imported template should preserve internal contained nodes.");

                        Assert.IsTrue(targetSession.AddFunctionInstance(user, targetBoundary,
                            imported, "ImportedTemplateInstance", new Rectangle(500, 260, 160, 70),
                            out var fi, out error), error?.Message);
                        Assert.IsNotNull(fi);
                        Assert.HasCount(1, targetBoundary.FunctionInstances,
                            "Target model should receive a new FunctionInstance using imported template.");
                    }), error?.Message);
            });
    }

    [TestMethod]
    public void FunctionInstancePasteSameModelSystem_ReusesExistingTemplate()
    {
        TestHelper.RunInModelSystemContext(
            nameof(FunctionInstancePasteSameModelSystem_ReusesExistingTemplate),
            (user, _, session) =>
            {
                CommandError error = null;
                var boundary = session.ModelSystem.GlobalBoundary;

                var existingTemplate = BuildTemplate(
                    user,
                    session,
                    "LocalTemplate",
                    new Rectangle(30, 30, 280, 180),
                    new Rectangle(60, 80, 160, 60),
                    new Rectangle(80, 150, 150, 50));

                Assert.IsTrue(session.ExportFunctionTemplateSnapshot(existingTemplate, out var snapshot, out error),
                    error?.Message);
                Assert.IsNotNull(snapshot);

                Assert.IsTrue(session.TryFindEquivalentFunctionTemplateSnapshot(snapshot!, out var resolvedTemplate, out error),
                    error?.Message);
                Assert.IsNotNull(resolvedTemplate);
                Assert.AreSame(existingTemplate, resolvedTemplate,
                    "Same-model paste resolution should reuse the existing equivalent template.");

                Assert.IsTrue(session.AddFunctionInstance(user, boundary, resolvedTemplate!,
                    "DuplicateInstance", new Rectangle(360, 240, 160, 70), out var fi, out error), error?.Message);
                Assert.IsNotNull(fi);

                Assert.HasCount(1, boundary.FunctionTemplates,
                    "Adding a pasted FunctionInstance in the same model system should not create a new FunctionTemplate.");
                Assert.HasCount(1, boundary.FunctionInstances);
                Assert.AreSame(existingTemplate, boundary.FunctionInstances.Single().Template);
            });
    }
}
