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
using XTMF2.GUI.ViewModels;
using XTMF2.ModelSystemConstruct;

namespace XTMF2.GUI.Tests.ViewModels;

[TestClass]
public class ModelSystemEditorViewModelClipboardTests
{
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
}
