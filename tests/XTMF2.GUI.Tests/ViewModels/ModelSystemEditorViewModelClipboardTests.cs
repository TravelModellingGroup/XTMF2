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
using System.Linq;
using System.Reflection;
using XTMF2.Editing;
using XTMF2.GUI.Controls;
using XTMF2.GUI.Tests.Modules;
using XTMF2.GUI.ViewModels;
using XTMF2.ModelSystemConstruct;
using XTMF2.RuntimeModules;

namespace XTMF2.GUI.Tests.ViewModels;

[TestClass]
public class ModelSystemEditorViewModelClipboardTests
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
    public void ClipboardSerializer_CommentBlock_UsesBodyField()
    {
        var payload = new CanvasClipboardPayload(
            Source: "XTMF2Canvas",
            Version: 1,
            Elements:
            [
                new CanvasElementDto(
                    Kind: CanvasElementKind.CommentBlock,
                    X: 1f,
                    Y: 2f,
                    W: 3f,
                    H: 4f,
                    Name: null,
                    CommentBody: "Body text",
                    CommentHeader: "Header")
            ]);

        var json = CanvasClipboardSerializer.Serialize(payload);
        Assert.IsTrue(json.Contains("\"body\":\"Body text\"", System.StringComparison.Ordinal));
        Assert.DoesNotContain(json, "\"name\":", System.StringComparison.Ordinal);

        var roundTrip = CanvasClipboardSerializer.TryDeserialize(json);
        Assert.IsNotNull(roundTrip);
        Assert.HasCount(1, roundTrip.Elements);
        Assert.AreEqual(CanvasElementKind.CommentBlock, roundTrip.Elements[0].Kind);
        Assert.AreEqual("Body text", roundTrip.Elements[0].CommentBody);
        Assert.AreEqual("Header", roundTrip.Elements[0].CommentHeader);
        Assert.IsNull(roundTrip.Elements[0].Name);
    }

    [TestMethod]
    public void PasteElementsAsync_CommentBlock_RestoresHeader()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(PasteElementsAsync_CommentBlock_RestoresHeader),
            (user, _, msSession) =>
            {
                using var vmEditor = new ModelSystemEditorViewModel(msSession, user, runController: null);

                var payload = new CanvasClipboardPayload(
                    Source: "XTMF2Canvas",
                    Version: 1,
                    Elements:
                    [
                        new CanvasElementDto(
                            Kind: CanvasElementKind.CommentBlock,
                            X: 10f,
                            Y: 15f,
                            W: 200f,
                            H: 80f,
                            Name: null,
                            CommentBody: "Body text",
                            CommentHeader: "Copied Header")
                    ]);

                vmEditor.PasteElementsAsync(payload, anchorX: 0, anchorY: 0)
                    .GetAwaiter().GetResult();

                var comments = msSession.ModelSystem.GlobalBoundary.CommentBlocks;
                Assert.HasCount(1, comments);
                Assert.AreEqual("Body text", comments[0].Comment);
                Assert.AreEqual("Copied Header", comments[0].Header);
            });
    }

    [TestMethod]
    public void PasteElementsAsync_CommentBlock_AllowsEmptyBody()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(PasteElementsAsync_CommentBlock_AllowsEmptyBody),
            (user, _, msSession) =>
            {
                using var vmEditor = new ModelSystemEditorViewModel(msSession, user, runController: null);

                var payload = new CanvasClipboardPayload(
                    Source: "XTMF2Canvas",
                    Version: 1,
                    Elements:
                    [
                        new CanvasElementDto(
                            Kind: CanvasElementKind.CommentBlock,
                            X: 10f,
                            Y: 15f,
                            W: 200f,
                            H: 80f,
                            Name: null,
                            CommentBody: string.Empty,
                            CommentHeader: "Header Only")
                    ]);

                vmEditor.PasteElementsAsync(payload, anchorX: 0, anchorY: 0)
                    .GetAwaiter().GetResult();

                var comments = msSession.ModelSystem.GlobalBoundary.CommentBlocks;
                Assert.HasCount(1, comments);
                Assert.AreEqual(string.Empty, comments[0].Comment);
                Assert.AreEqual("Header Only", comments[0].Header);
            });
    }

    [TestMethod]
    public void PasteElementsAsync_CommentBlock_LegacyNameFallbackStillWorks()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(PasteElementsAsync_CommentBlock_LegacyNameFallbackStillWorks),
            (user, _, msSession) =>
            {
                using var vmEditor = new ModelSystemEditorViewModel(msSession, user, runController: null);

                var payload = new CanvasClipboardPayload(
                    Source: "XTMF2Canvas",
                    Version: 1,
                    Elements:
                    [
                        new CanvasElementDto(
                            Kind: CanvasElementKind.CommentBlock,
                            X: 10f,
                            Y: 15f,
                            W: 200f,
                            H: 80f,
                            Name: "Legacy body text",
                            CommentHeader: "Legacy Header")
                    ]);

                vmEditor.PasteElementsAsync(payload, anchorX: 0, anchorY: 0)
                    .GetAwaiter().GetResult();

                var comments = msSession.ModelSystem.GlobalBoundary.CommentBlocks;
                Assert.HasCount(1, comments);
                Assert.AreEqual("Legacy body text", comments[0].Comment);
                Assert.AreEqual("Legacy Header", comments[0].Header);
            });
    }

    [TestMethod]
    public void PasteElementsAsync_CopiedOriginLinksToExistingDestinationById()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(PasteElementsAsync_CopiedOriginLinksToExistingDestinationById),
            (user, _, msSession) =>
            {
                var boundary = msSession.ModelSystem.GlobalBoundary;
                Assert.IsTrue(msSession.AddNode(
                    user,
                    boundary,
                    "Origin",
                    typeof(LinkedGuiTestModule),
                    new Rectangle(20, 20, 160, 60),
                    out var origin,
                    out var originError),
                    originError?.Message);
                Assert.IsNotNull(origin);

                Assert.IsTrue(msSession.AddNode(
                    user,
                    boundary,
                    "Destination",
                    typeof(SimpleGuiTestModule),
                    new Rectangle(260, 20, 160, 60),
                    out var destination,
                    out var destinationError),
                    destinationError?.Message);
                Assert.IsNotNull(destination);

                var hook = origin!.Hooks.First(h => h.Name == "Child");
                Assert.IsTrue(msSession.AddLink(user, origin, hook, destination!, out var addedLink, out var linkError),
                    linkError?.Message);
                Assert.IsNotNull(addedLink);

                using var vmEditor = new ModelSystemEditorViewModel(msSession, user, runController: null);
                var payload = new CanvasClipboardPayload(
                    Source: "XTMF2Canvas",
                    Version: 1,
                    Elements:
                    [
                        new CanvasElementDto(
                            Kind: CanvasElementKind.Node,
                            X: 20f,
                            Y: 20f,
                            W: 160f,
                            H: 60f,
                            Name: "Origin",
                            TypeName: typeof(LinkedGuiTestModule).AssemblyQualifiedName,
                            OriginalId: origin.Id,
                            CrossLinks:
                            [
                                new CrossNodeLinkDto("Child", "Destination", destination!.Id)
                            ])
                    ]);

                vmEditor.PasteElementsAsync(payload, anchorX: 400, anchorY: 20)
                    .GetAwaiter().GetResult();

                var pastedOrigin = boundary.Modules.Single(n => n.Name == "Origin" && n.Id != origin.Id);
                var pastedLink = boundary.Links.OfType<SingleLink>().SingleOrDefault(l =>
                    ReferenceEquals(l.Origin, pastedOrigin)
                    && ReferenceEquals(l.Destination, destination));

                Assert.IsNotNull(pastedLink);
            });
    }

    [TestMethod]
    public void PasteElementsAsync_CopiedOriginLinksToExistingDestinationByIdInAnotherBoundary()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(PasteElementsAsync_CopiedOriginLinksToExistingDestinationByIdInAnotherBoundary),
            (user, _, msSession) =>
            {
                var boundary = msSession.ModelSystem.GlobalBoundary;
                Assert.IsTrue(msSession.AddBoundary(user, boundary, "Other", out var otherBoundary, out var boundaryError),
                    boundaryError?.Message);
                Assert.IsNotNull(otherBoundary);

                Assert.IsTrue(msSession.AddNode(
                    user,
                    boundary,
                    "Origin",
                    typeof(LinkedGuiTestModule),
                    new Rectangle(20, 20, 160, 60),
                    out var origin,
                    out var originError),
                    originError?.Message);
                Assert.IsNotNull(origin);

                Assert.IsTrue(msSession.AddNode(
                    user,
                    otherBoundary!,
                    "Destination",
                    typeof(SimpleGuiTestModule),
                    new Rectangle(260, 20, 160, 60),
                    out var destination,
                    out var destinationError),
                    destinationError?.Message);
                Assert.IsNotNull(destination);

                var hook = origin!.Hooks.First(h => h.Name == "Child");
                Assert.IsTrue(msSession.AddLink(user, origin, hook, destination!, out var addedLink, out var linkError),
                    linkError?.Message);
                Assert.IsNotNull(addedLink);

                using var vmEditor = new ModelSystemEditorViewModel(msSession, user, runController: null);
                var payload = new CanvasClipboardPayload(
                    Source: "XTMF2Canvas",
                    Version: 1,
                    Elements:
                    [
                        new CanvasElementDto(
                            Kind: CanvasElementKind.Node,
                            X: 20f,
                            Y: 20f,
                            W: 160f,
                            H: 60f,
                            Name: "Origin",
                            TypeName: typeof(LinkedGuiTestModule).AssemblyQualifiedName,
                            OriginalId: origin.Id,
                            CrossLinks:
                            [
                                new CrossNodeLinkDto("Child", "Destination", destination!.Id)
                            ])
                    ]);

                vmEditor.PasteElementsAsync(payload, anchorX: 400, anchorY: 20)
                    .GetAwaiter().GetResult();

                var pastedOrigin = boundary.Modules.Single(n => n.Name == "Origin" && n.Id != origin.Id);
                var pastedLink = boundary.Links.OfType<SingleLink>().SingleOrDefault(l =>
                    ReferenceEquals(l.Origin, pastedOrigin)
                    && ReferenceEquals(l.Destination, destination));

                Assert.IsNotNull(pastedLink);
                Assert.IsTrue(vmEditor.Links.Any(lvm => ReferenceEquals(lvm.UnderlyingLink, pastedLink)),
                    "The pasted cross-boundary link should be represented in the current canvas view.");
            });
    }

    [TestMethod]
    public void CopyMetadata_CrossBoundaryLinkNeedsUnderlyingDestinationWhenCanvasDestinationIsNotVisible()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(CopyMetadata_CrossBoundaryLinkNeedsUnderlyingDestinationWhenCanvasDestinationIsNotVisible),
            (user, _, msSession) =>
            {
                var boundary = msSession.ModelSystem.GlobalBoundary;
                Assert.IsTrue(msSession.AddBoundary(user, boundary, "Other", out var otherBoundary, out var boundaryError),
                    boundaryError?.Message);
                Assert.IsNotNull(otherBoundary);

                Assert.IsTrue(msSession.AddNode(
                    user,
                    boundary,
                    "Origin",
                    typeof(LinkedGuiTestModule),
                    new Rectangle(20, 20, 160, 60),
                    out var origin,
                    out var originError),
                    originError?.Message);
                Assert.IsNotNull(origin);

                Assert.IsTrue(msSession.AddNode(
                    user,
                    otherBoundary!,
                    "Destination",
                    typeof(SimpleGuiTestModule),
                    new Rectangle(260, 20, 160, 60),
                    out var destination,
                    out var destinationError),
                    destinationError?.Message);
                Assert.IsNotNull(destination);

                var hook = origin!.Hooks.First(h => h.Name == "Child");
                Assert.IsTrue(msSession.AddLink(user, origin, hook, destination!, out var addedLink, out var linkError),
                    linkError?.Message);
                Assert.IsNotNull(addedLink);

                using var vmEditor = new ModelSystemEditorViewModel(msSession, user, runController: null);
                var renderedLink = vmEditor.Links.Single(lvm => ReferenceEquals(lvm.UnderlyingLink, addedLink));

                Assert.IsNull(renderedLink.Destination,
                    "The cross-boundary destination is not represented by a visible canvas element in the current boundary.");

                var destinationResolver = typeof(ModelSystemCanvas).GetMethod(
                    "TryGetLinkDestinationForRenderedBranch",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.IsNotNull(destinationResolver);

                object?[] args = [renderedLink, null];
                Assert.IsTrue((bool)destinationResolver!.Invoke(null, args)!);
                Assert.AreSame(destination, args[1]);
            });
    }

    [TestMethod]
    public void PasteElementsAsync_CommentBlock_WorksInInnerBoundary()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(PasteElementsAsync_CommentBlock_WorksInInnerBoundary),
            (user, _, msSession) =>
            {
                Assert.IsTrue(msSession.AddBoundary(user, msSession.ModelSystem.GlobalBoundary, "Inner",
                    out var inner, out var error), error?.Message);
                Assert.IsNotNull(inner);

                using var vmEditor = new ModelSystemEditorViewModel(msSession, user, runController: null);
                vmEditor.SwitchToBoundary(inner!);

                var payload = new CanvasClipboardPayload(
                    Source: "XTMF2Canvas",
                    Version: 1,
                    Elements:
                    [
                        new CanvasElementDto(
                            Kind: CanvasElementKind.CommentBlock,
                            X: 10f,
                            Y: 15f,
                            W: 200f,
                            H: 80f,
                            Name: null,
                            CommentBody: "Inner body",
                            CommentHeader: "Inner header")
                    ]);

                vmEditor.PasteElementsAsync(payload, anchorX: 0, anchorY: 0)
                    .GetAwaiter().GetResult();

                Assert.HasCount(1, inner.CommentBlocks);
                Assert.AreEqual("Inner body", inner.CommentBlocks[0].Comment);
                Assert.AreEqual("Inner header", inner.CommentBlocks[0].Header);
                Assert.HasCount(1, vmEditor.CommentBlocks);
                Assert.AreEqual("Inner body", vmEditor.CommentBlocks[0].Name);
            });
    }

    [TestMethod]
    public void AddCommentBlock_AfterBoundarySwitch_DoesNotDuplicateViewModelEntries()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(AddCommentBlock_AfterBoundarySwitch_DoesNotDuplicateViewModelEntries),
            (user, _, msSession) =>
            {
                Assert.IsTrue(msSession.AddBoundary(user, msSession.ModelSystem.GlobalBoundary, "Inner",
                    out var inner, out var error), error?.Message);
                Assert.IsNotNull(inner);

                using var vmEditor = new ModelSystemEditorViewModel(msSession, user, runController: null);

                vmEditor.SwitchToBoundary(inner!);
                vmEditor.SwitchToBoundary(msSession.ModelSystem.GlobalBoundary);
                vmEditor.SwitchToBoundary(inner!);

                Assert.IsTrue(msSession.AddCommentBlock(user, inner!, "After switch", new Rectangle(20, 20, 200, 80),
                    out var block, out error), error?.Message);
                Assert.IsNotNull(block);

                Assert.HasCount(1, inner!.CommentBlocks);
                Assert.HasCount(1, vmEditor.CommentBlocks);
                Assert.AreEqual("After switch", vmEditor.CommentBlocks[0].Name);
            });
    }

    [TestMethod]
    public void PasteElementsAsync_FunctionInstancePayloadWithDuplicateTemplateSnapshots_ImportsSingleTemplate()
    {
        TestGuiHelper.RunInProjectContext(
            nameof(PasteElementsAsync_FunctionInstancePayloadWithDuplicateTemplateSnapshots_ImportsSingleTemplate),
            (runtime, user, projectSession) =>
            {
                CommandError? error = null;

                Assert.IsTrue(projectSession.CreateNewModelSystem(user, "SourceModel", out var sourceHeader, out error),
                    error?.Message);
                Assert.IsTrue(projectSession.CreateNewModelSystem(user, "TargetModel", out var targetHeader, out error),
                    error?.Message);

                string? snapshot = null;

                Assert.IsTrue(projectSession.EditModelSystem(user, sourceHeader!, out var sourceSession, out error)
                    .UsingIf(sourceSession, () =>
                    {
                        Assert.IsNotNull(sourceSession);
                        var sourceMs = sourceSession!;

                        var sourceTemplate = BuildTemplate(
                            user,
                            sourceMs,
                            "SharedTemplate",
                            new Rectangle(20, 20, 260, 160),
                            new Rectangle(30, 40, 160, 60),
                            new Rectangle(50, 120, 150, 50));

                        Assert.IsTrue(sourceMs.ExportFunctionTemplateSnapshot(sourceTemplate, out snapshot, out error),
                            error?.Message);
                        Assert.IsFalse(string.IsNullOrWhiteSpace(snapshot));
                    }), error?.Message);

                Assert.IsTrue(projectSession.EditModelSystem(user, targetHeader!, out var targetSession, out error)
                    .UsingIf(targetSession, () =>
                    {
                        Assert.IsNotNull(targetSession);
                        var targetMs = targetSession!;

                        using var vmEditor = new ModelSystemEditorViewModel(targetMs, user, runController: null);

                        var payload = new CanvasClipboardPayload(
                            Source: "XTMF2Canvas",
                            Version: 1,
                            Elements:
                            [
                                new CanvasElementDto(
                                    Kind: CanvasElementKind.FunctionTemplate,
                                    X: 100f,
                                    Y: 120f,
                                    W: 240f,
                                    H: 160f,
                                    Name: "SharedTemplate",
                                    EmbeddedTemplateSnapshot: snapshot!,
                                    IsTemplateCompanion: false),
                                new CanvasElementDto(
                                    Kind: CanvasElementKind.FunctionTemplate,
                                    X: 130f,
                                    Y: 150f,
                                    W: 240f,
                                    H: 160f,
                                    Name: "SharedTemplate",
                                    EmbeddedTemplateSnapshot: snapshot!,
                                    IsTemplateCompanion: true),
                                new CanvasElementDto(
                                    Kind: CanvasElementKind.FunctionInstance,
                                    X: 180f,
                                    Y: 220f,
                                    W: 160f,
                                    H: 70f,
                                    Name: "PastedInstance",
                                    TemplateName: "SharedTemplate",
                                        EmbeddedTemplateSnapshot: snapshot!)
                            ]);

                        vmEditor.PasteElementsAsync(payload, anchorX: 0, anchorY: 0)
                            .GetAwaiter().GetResult();

                        var boundary = targetMs.ModelSystem.GlobalBoundary;
                        Assert.HasCount(1, boundary.FunctionTemplates,
                            "Duplicate template snapshot entries should materialize one imported template.");
                        Assert.HasCount(1, boundary.FunctionInstances,
                            "FunctionInstance should paste successfully.");
                        Assert.AreSame(boundary.FunctionTemplates.Single(), boundary.FunctionInstances.Single().Template,
                            "Pasted FunctionInstance should reference the single imported template.");
                    }), error?.Message);
            });
    }

    [TestMethod]
    public void PasteElementsAsync_FunctionInstanceRestoresEmbeddedParameter()
    {
        TestGuiHelper.RunInProjectContext(
            nameof(PasteElementsAsync_FunctionInstanceRestoresEmbeddedParameter),
            (runtime, user, projectSession) =>
            {
                CommandError? error = null;
                Assert.IsTrue(projectSession.CreateNewModelSystem(user, "SourceModel", out var sourceHeader, out error),
                    error?.Message);
                Assert.IsTrue(projectSession.CreateNewModelSystem(user, "TargetModel", out var targetHeader, out error),
                    error?.Message);

                string? snapshot = null;
                Assert.IsTrue(projectSession.EditModelSystem(user, sourceHeader!, out var sourceSession, out error)
                    .UsingIf(sourceSession, () =>
                    {
                        Assert.IsNotNull(sourceSession);
                        var sourceTemplate = BuildTemplate(
                            user,
                            sourceSession!,
                            "EmbeddedTemplate",
                            new Rectangle(20, 20, 260, 160),
                            new Rectangle(30, 40, 160, 60),
                            new Rectangle(50, 120, 150, 50));
                        Assert.IsTrue(sourceSession!.ExportFunctionTemplateSnapshot(sourceTemplate, out snapshot, out error),
                            error?.Message);
                    }), error?.Message);

                Assert.IsTrue(projectSession.EditModelSystem(user, targetHeader!, out var targetSession, out error)
                    .UsingIf(targetSession, () =>
                    {
                        Assert.IsNotNull(targetSession);
                        using var vmEditor = new ModelSystemEditorViewModel(targetSession!, user, runController: null);
                        var payload = new CanvasClipboardPayload(
                            Source: "XTMF2Canvas",
                            Version: 1,
                            Elements:
                            [
                                new CanvasElementDto(
                                    Kind: CanvasElementKind.FunctionInstance,
                                    X: 100f,
                                    Y: 120f,
                                    W: 160f,
                                    H: 70f,
                                    Name: "InstanceWithParameter",
                                    TemplateName: "EmbeddedTemplate",
                                    EmbeddedTemplateSnapshot: snapshot!,
                                    InlinedChildren:
                                    [
                                        new InlinedChildDto(
                                            "Input",
                                            new CanvasElementDto(
                                                Kind: CanvasElementKind.Node,
                                                X: -1f,
                                                Y: -1f,
                                                W: 120f,
                                                H: 50f,
                                                Name: "EmbeddedInput",
                                                TypeName: typeof(BasicParameter<string>).AssemblyQualifiedName,
                                                ParameterValue: "copied-value"))
                                    ])
                            ]);

                        vmEditor.PasteElementsAsync(payload, anchorX: 0, anchorY: 0)
                            .GetAwaiter().GetResult();

                        var instance = targetSession!.ModelSystem.GlobalBoundary.FunctionInstances.Single();
                        var embedded = targetSession.ModelSystem.GlobalBoundary.Modules
                            .Single(node => node.Name == "EmbeddedInput");
                        Assert.AreEqual(Rectangle.Hidden, embedded.Location);
                        Assert.AreEqual("copied-value", embedded.ParameterValue?.Representation);
                        Assert.IsTrue(targetSession.ModelSystem.GlobalBoundary.Links
                            .OfType<SingleLink>()
                            .Any(link => ReferenceEquals(link.Origin, instance)
                                && ReferenceEquals(link.Destination, embedded)));
                    }), error?.Message);
            });
    }

    [TestMethod]
    public void PasteElementsAsync_FunctionInstanceReferencedTemplate_ReusesExistingEquivalentTemplate()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(PasteElementsAsync_FunctionInstanceReferencedTemplate_ReusesExistingEquivalentTemplate),
            (user, _, session) =>
            {
                var existingTemplate = BuildTemplate(
                    user,
                    session,
                    "ExistingTemplate",
                    new Rectangle(30, 30, 260, 160),
                    new Rectangle(40, 60, 160, 60),
                    new Rectangle(70, 130, 150, 50));

                Assert.IsTrue(session.ExportFunctionTemplateSnapshot(existingTemplate, out var snapshot, out var error),
                    error?.Message);
                Assert.IsFalse(string.IsNullOrWhiteSpace(snapshot));

                using var vmEditor = new ModelSystemEditorViewModel(session, user, runController: null);
                var payload = new CanvasClipboardPayload(
                    Source: "XTMF2Canvas",
                    Version: 1,
                    Elements:
                    [
                        new CanvasElementDto(
                            Kind: CanvasElementKind.FunctionTemplate,
                            X: 110f,
                            Y: 130f,
                            W: 240f,
                            H: 160f,
                            Name: "ExistingTemplate",
                            EmbeddedTemplateSnapshot: snapshot,
                            IsTemplateCompanion: false),
                        new CanvasElementDto(
                            Kind: CanvasElementKind.FunctionInstance,
                            X: 160f,
                            Y: 220f,
                            W: 160f,
                            H: 70f,
                            Name: "PastedInstance",
                            TemplateName: "ExistingTemplate",
                            EmbeddedTemplateSnapshot: snapshot)
                    ]);

                vmEditor.PasteElementsAsync(payload, anchorX: 0, anchorY: 0)
                    .GetAwaiter().GetResult();

                var boundary = session.ModelSystem.GlobalBoundary;
                Assert.HasCount(1, boundary.FunctionTemplates,
                    "Equivalent existing template should be reused instead of duplicating.");
                Assert.HasCount(1, boundary.FunctionInstances,
                    "FunctionInstance should be pasted.");
                Assert.AreSame(existingTemplate, boundary.FunctionInstances.Single().Template,
                    "Pasted FunctionInstance should resolve to the existing equivalent template.");
            });
    }

    [TestMethod]
    public void PasteElementsAsync_MixedPayload_DedupesTemplateAndPastesOtherElements()
    {
        TestGuiHelper.RunInProjectContext(
            nameof(PasteElementsAsync_MixedPayload_DedupesTemplateAndPastesOtherElements),
            (_, user, projectSession) =>
            {
                CommandError? error = null;

                Assert.IsTrue(projectSession.CreateNewModelSystem(user, "SourceMixed", out var sourceHeader, out error),
                    error?.Message);
                Assert.IsTrue(projectSession.CreateNewModelSystem(user, "TargetMixed", out var targetHeader, out error),
                    error?.Message);

                string? snapshot = null;
                Assert.IsTrue(projectSession.EditModelSystem(user, sourceHeader!, out var sourceSession, out error)
                    .UsingIf(sourceSession, () =>
                    {
                        Assert.IsNotNull(sourceSession);
                        var sourceMs = sourceSession!;

                        var sourceTemplate = BuildTemplate(
                            user,
                            sourceMs,
                            "MixedTemplate",
                            new Rectangle(40, 40, 260, 160),
                            new Rectangle(60, 90, 160, 60),
                            new Rectangle(85, 150, 150, 50));

                        Assert.IsTrue(sourceMs.ExportFunctionTemplateSnapshot(sourceTemplate, out snapshot, out error),
                            error?.Message);
                        Assert.IsFalse(string.IsNullOrWhiteSpace(snapshot));
                    }), error?.Message);

                Assert.IsTrue(projectSession.EditModelSystem(user, targetHeader!, out var targetSession, out error)
                    .UsingIf(targetSession, () =>
                    {
                        Assert.IsNotNull(targetSession);
                        var targetMs = targetSession!;

                        using var vmEditor = new ModelSystemEditorViewModel(targetMs, user, runController: null);

                        var payload = new CanvasClipboardPayload(
                            Source: "XTMF2Canvas",
                            Version: 1,
                            Elements:
                            [
                                new CanvasElementDto(
                                    Kind: CanvasElementKind.Node,
                                    X: 5f,
                                    Y: 10f,
                                    W: 120f,
                                    H: 50f,
                                    Name: "MixedNode",
                                    TypeName: typeof(BasicParameter<string>).AssemblyQualifiedName,
                                    ParameterValue: "abc",
                                    IsScriptedParam: false),
                                new CanvasElementDto(
                                    Kind: CanvasElementKind.CommentBlock,
                                    X: 12f,
                                    Y: 18f,
                                    W: 220f,
                                    H: 90f,
                                    Name: null,
                                    CommentBody: "Mixed body",
                                    CommentHeader: "Mixed header"),
                                new CanvasElementDto(
                                    Kind: CanvasElementKind.FunctionTemplate,
                                    X: 90f,
                                    Y: 110f,
                                    W: 240f,
                                    H: 160f,
                                    Name: "MixedTemplate",
                                    EmbeddedTemplateSnapshot: snapshot!,
                                    IsTemplateCompanion: false),
                                new CanvasElementDto(
                                    Kind: CanvasElementKind.FunctionTemplate,
                                    X: 120f,
                                    Y: 140f,
                                    W: 240f,
                                    H: 160f,
                                    Name: "MixedTemplate",
                                    EmbeddedTemplateSnapshot: snapshot!,
                                    IsTemplateCompanion: true),
                                new CanvasElementDto(
                                    Kind: CanvasElementKind.FunctionInstance,
                                    X: 180f,
                                    Y: 220f,
                                    W: 160f,
                                    H: 70f,
                                    Name: "MixedInstance",
                                    TemplateName: "MixedTemplate",
                                    EmbeddedTemplateSnapshot: snapshot!)
                            ]);

                        vmEditor.PasteElementsAsync(payload, anchorX: 0, anchorY: 0)
                            .GetAwaiter().GetResult();

                        var boundary = targetMs.ModelSystem.GlobalBoundary;
                        Assert.HasCount(1, boundary.Modules,
                            "Node in mixed payload should be pasted.");
                        Assert.AreEqual("MixedNode", boundary.Modules[0].Name);

                        Assert.HasCount(1, boundary.CommentBlocks,
                            "Comment block in mixed payload should be pasted.");
                        Assert.AreEqual("Mixed body", boundary.CommentBlocks[0].Comment);
                        Assert.AreEqual("Mixed header", boundary.CommentBlocks[0].Header);

                        Assert.HasCount(1, boundary.FunctionTemplates,
                            "Duplicate template snapshot entries should still dedupe in mixed payloads.");
                        Assert.HasCount(1, boundary.FunctionInstances,
                            "Function instance in mixed payload should be pasted.");
                        Assert.AreSame(boundary.FunctionTemplates.Single(), boundary.FunctionInstances.Single().Template,
                            "Function instance should bind to deduped template.");
                    }), error?.Message);
            });
    }
}
