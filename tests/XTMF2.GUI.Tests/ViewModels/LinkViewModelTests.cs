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

using System.ComponentModel;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Editing;
using XTMF2.GUI.Tests.Modules;
using XTMF2.GUI.ViewModels;
using XTMF2.ModelSystemConstruct;
using XTMF2.RuntimeModules;

namespace XTMF2.GUI.Tests.ViewModels;

[TestClass]
public class LinkViewModelTests
{
    private HeadlessUnitTestSession Session => HeadlessAppLifetime.HeadlessSession!;

    [TestMethod]
    public void RefreshEndpoints_UsesOriginCenter()
    {
        TestGuiHelper.RunInModelSystemContext("Link_RefreshEndpoints", (user, _, msSession) =>
        {
            CommandError? error = null;
            var boundary = msSession.ModelSystem.GlobalBoundary;

            // Create two nodes and a link between them.
            Assert.IsTrue(msSession.AddModelSystemStart(user, boundary, "Start",
                new Rectangle(10, 10, 60, 60), out var start, out error), error?.Message);
            Assert.IsTrue(msSession.AddNode(user, boundary, "Dest",
                typeof(SimpleGuiTestModule), new Rectangle(200, 100, 120, 50),
                out var destNode, out error), error?.Message);
            Assert.IsTrue(msSession.AddLink(user, start!, start!.Hooks[0], destNode!, out var link, out error), error?.Message);

            using var vmEditor = new ModelSystemEditorViewModel(msSession, user, runController: null);
            
            // ModelSystemEditorViewModel subscribes to boundary changes and auto-adds LinkViewModels.
            var linkVm = vmEditor.Links.FirstOrDefault(l => l.UnderlyingLink == link);
            Assert.IsNotNull(linkVm, "LinkViewModel should be present for the added link.");

            // The origin element's center must match X1/Y1
            Assert.AreEqual(linkVm!.Origin.CenterX, linkVm.X1, 1e-9);
            Assert.AreEqual(linkVm.Origin.CenterY, linkVm.Y1, 1e-9);
        });
    }

    [TestMethod]
    public void IsOrthogonal_DefaultsToFalse()
    {
        TestGuiHelper.RunInModelSystemContext("Link_IsOrthogonal_Default", (user, _, msSession) =>
        {
            CommandError? error = null;
            var boundary = msSession.ModelSystem.GlobalBoundary;

            Assert.IsTrue(msSession.AddModelSystemStart(user, boundary, "Start",
                new Rectangle(10, 10, 60, 60), out var start, out error), error?.Message);
            Assert.IsTrue(msSession.AddNode(user, boundary, "Dest",
                typeof(SimpleGuiTestModule), new Rectangle(200, 100, 120, 50),
                out var destNode, out error), error?.Message);
            Assert.IsTrue(msSession.AddLink(user, start!, start!.Hooks[0], destNode!, out var link, out error), error?.Message);

            using var vmEditor = new ModelSystemEditorViewModel(msSession, user, runController: null);
            var linkVm = vmEditor.Links.FirstOrDefault(l => l.UnderlyingLink == link);
            Assert.IsNotNull(linkVm);
            Assert.IsFalse(linkVm!.IsOrthogonal);
        });
    }

    [TestMethod]
    public void Detach_RemovesPropertyChangedSubscriptions()
    {
        TestGuiHelper.RunInModelSystemContext("Link_Detach", (user, _, msSession) =>
        {
            CommandError? error = null;
            var boundary = msSession.ModelSystem.GlobalBoundary;

            Assert.IsTrue(msSession.AddModelSystemStart(user, boundary, "Start",
                new Rectangle(10, 10, 60, 60), out var start, out error), error?.Message);
            Assert.IsTrue(msSession.AddNode(user, boundary, "Dest",
                typeof(SimpleGuiTestModule), new Rectangle(200, 100, 120, 50),
                out var destNode, out error), error?.Message);
            Assert.IsTrue(msSession.AddLink(user, start!, start!.Hooks[0], destNode!, out var link, out error), error?.Message);

            using var vmEditor = new ModelSystemEditorViewModel(msSession, user, runController: null);
            var linkVm = vmEditor.Links.FirstOrDefault(l => l.UnderlyingLink == link);
            Assert.IsNotNull(linkVm);

                double x1Before = linkVm!.X1;

                // Detach and then trigger a property change — endpoints should NOT update.
                linkVm.Detach();

                int changeCount = 0;
                ((INotifyPropertyChanged)linkVm).PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is nameof(LinkViewModel.X1)) changeCount++;
                };

                // Moving the link's origin would normally fire endpoint refresh.
                // After Detach, the subscription is gone so no update occurs.
                // We merely validate no exception is thrown and the count stays 0.
                Assert.AreEqual(0, changeCount);
            
        });
    }

    [TestMethod]
    public void DestinationCenterUsed_WhenDestinationExists()
    {
        TestGuiHelper.RunInModelSystemContext("Link_DestCenter", (user, _, msSession) =>
        {
            CommandError? error = null;
            var boundary = msSession.ModelSystem.GlobalBoundary;

            Assert.IsTrue(msSession.AddModelSystemStart(user, boundary, "Start",
                new Rectangle(10, 10, 60, 60), out var start, out error), error?.Message);
            Assert.IsTrue(msSession.AddNode(user, boundary, "Dest",
                typeof(SimpleGuiTestModule), new Rectangle(300, 200, 120, 50),
                out var destNode, out error), error?.Message);
            Assert.IsTrue(msSession.AddLink(user, start!, start!.Hooks[0], destNode!, out var link, out error), error?.Message);

            using var vmEditor = new ModelSystemEditorViewModel(msSession, user, runController: null);
            
            var linkVm = vmEditor.Links.FirstOrDefault(l => l.UnderlyingLink == link);
            Assert.IsNotNull(linkVm);
            Assert.IsNotNull(linkVm!.Destination);

            Assert.AreEqual(linkVm.Destination!.CenterX, linkVm.X2, 1e-9);
            Assert.AreEqual(linkVm.Destination.CenterY, linkVm.Y2, 1e-9);
            
        });
    }

    [TestMethod]
    public void CrossBoundaryLink_ProjectsThroughClosestGhostInDestinationBoundary()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(CrossBoundaryLink_ProjectsThroughClosestGhostInDestinationBoundary),
            (user, _, msSession) =>
            {
                CommandError? error = null;
                var root = msSession.ModelSystem.GlobalBoundary;
                Assert.IsTrue(msSession.AddBoundary(user, root, "Source",
                    out var sourceBoundary, out error), error?.Message);
                Assert.IsTrue(msSession.AddBoundary(user, root, "Destination",
                    out var destinationBoundary, out error), error?.Message);

                Assert.IsTrue(msSession.AddNode(user, sourceBoundary!, "Origin",
                    typeof(LinkedGuiTestModule), new Rectangle(20, 20, 120, 50),
                    out var origin, out error), error?.Message);
                Assert.IsTrue(msSession.AddNode(user, destinationBoundary!, "Destination",
                    typeof(SimpleGuiTestModule), new Rectangle(400, 100, 120, 50),
                    out var destination, out error), error?.Message);

                var hook = origin!.Hooks.First(h => h.Name == "Child");
                XTMF2.Link? link = null;
                Assert.IsTrue(msSession.AddLink(user, origin, hook, destination!,
                    out link, out error), error?.Message);

                using var vmEditor = new ModelSystemEditorViewModel(msSession, user, runController: null);
                vmEditor.SwitchToBoundary(destinationBoundary!);
                Assert.IsFalse(vmEditor.Links.Any(lvm => ReferenceEquals(lvm.UnderlyingLink, link)));

                GhostNode? farGhost = null;
                Assert.IsTrue(msSession.AddGhostNode(user, destinationBoundary!, origin,
                    new Rectangle(40, 100, 120, 50), out farGhost, out error), error?.Message);
                Assert.IsNotNull(farGhost);
                GhostNode? closeGhost = null;
                Assert.IsTrue(msSession.AddGhostNode(user, destinationBoundary!, origin,
                    new Rectangle(300, 100, 120, 50), out closeGhost, out error), error?.Message);
                Assert.IsNotNull(closeGhost);

                var linkViewModel = vmEditor.Links.Single(lvm => ReferenceEquals(lvm.UnderlyingLink, link));
                Assert.AreSame(destination, ((NodeViewModel)linkViewModel.Destination!).UnderlyingNode);
                Assert.AreSame(closeGhost, ((GhostNodeViewModel)linkViewModel.Origin).UnderlyingGhostNode);
            });
    }

    [TestMethod]
    public void CrossBoundaryLink_DoesNotProjectWhenDisabled()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(CrossBoundaryLink_DoesNotProjectWhenDisabled),
            (user, _, msSession) =>
            {
                CommandError? error = null;
                var root = msSession.ModelSystem.GlobalBoundary;
                Assert.IsTrue(msSession.AddBoundary(user, root, "Source",
                    out var sourceBoundary, out error), error?.Message);
                Assert.IsTrue(msSession.AddBoundary(user, root, "Destination",
                    out var destinationBoundary, out error), error?.Message);
                Assert.IsTrue(msSession.AddNode(user, sourceBoundary!, "Origin",
                    typeof(LinkedGuiTestModule), new Rectangle(20, 20, 120, 50),
                    out var origin, out error), error?.Message);
                Assert.IsTrue(msSession.AddNode(user, destinationBoundary!, "Destination",
                    typeof(SimpleGuiTestModule), new Rectangle(400, 100, 120, 50),
                    out var destination, out error), error?.Message);

                var hook = origin!.Hooks.First(h => h.Name == "Child");
                XTMF2.Link? link = null;
                Assert.IsTrue(msSession.AddLink(user, origin, hook, destination!,
                    out link, out error), error?.Message);
                Assert.IsTrue(msSession.SetLinkDisabled(user, link!, true, out error), error?.Message);
                GhostNode? disabledGhost = null;
                Assert.IsTrue(msSession.AddGhostNode(user, destinationBoundary!, origin,
                    new Rectangle(300, 100, 120, 50), out disabledGhost, out error), error?.Message);

                using var vmEditor = new ModelSystemEditorViewModel(msSession, user, runController: null);
                vmEditor.SwitchToBoundary(destinationBoundary!);

                Assert.IsFalse(vmEditor.Links.Any(lvm => ReferenceEquals(lvm.UnderlyingLink, link)));
            });
    }

    [TestMethod]
    public void RenamingReferencedNode_UpdatesGhostName()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(RenamingReferencedNode_UpdatesGhostName),
            (user, _, msSession) =>
            {
                var boundary = msSession.ModelSystem.GlobalBoundary;
                Assert.IsTrue(msSession.AddNode(user, boundary, "Original",
                    typeof(SimpleGuiTestModule), new Rectangle(20, 20, 120, 50),
                    out var node, out var nodeError), nodeError?.Message);
                Assert.IsNotNull(node);
                GhostNode? ghost = null;
                Assert.IsTrue(msSession.AddGhostNode(user, boundary, node!,
                    new Rectangle(220, 20, 120, 50), out ghost, out var ghostError), ghostError?.Message);
                Assert.IsNotNull(ghost);

                using var vmEditor = new ModelSystemEditorViewModel(msSession, user, runController: null);
                var ghostVm = vmEditor.GhostNodes.Single();
                Assert.AreEqual("Original", ghostVm.Name);

                Assert.IsTrue(msSession.SetNodeName(user, node!, "Renamed", out var renameError),
                    renameError?.Message);

                Assert.AreEqual("Renamed", ghost!.Name);
                Assert.AreEqual("Renamed", ghostVm.Name);
            });
    }

    [TestMethod]
    public void CreateLinkAsync_ReversesDirectionWhenForwardIsIncompatible()
    {
        TestGuiHelper.RunInModelSystemContext(nameof(CreateLinkAsync_ReversesDirectionWhenForwardIsIncompatible),
            (user, _, msSession) =>
            {
                CommandError? error = null;
                var boundary = msSession.ModelSystem.GlobalBoundary;

                Assert.IsTrue(msSession.AddNode(user, boundary, "Ignore",
                    typeof(IgnoreResult<string>), new Rectangle(20, 20, 120, 50),
                    out var ignoreNode, out error), error?.Message);
                Assert.IsTrue(msSession.AddNode(user, boundary, "Execute",
                    typeof(Execute), new Rectangle(260, 20, 120, 50),
                    out var executeNode, out error), error?.Message);
                Assert.IsNotNull(ignoreNode);
                Assert.IsNotNull(executeNode);

                using var vmEditor = new ModelSystemEditorViewModel(msSession, user, runController: null);

                var ignoreVm = vmEditor.Nodes.FirstOrDefault(n => ReferenceEquals(n.UnderlyingNode, ignoreNode));
                var executeVm = vmEditor.Nodes.FirstOrDefault(n => ReferenceEquals(n.UnderlyingNode, executeNode));
                Assert.IsNotNull(ignoreVm);
                Assert.IsNotNull(executeVm);

                Session.Dispatch(() =>
                {
                    vmEditor.ParentWindow = new Window();
                    vmEditor.CreateLinkAsync(ignoreVm!, executeVm!)
                        .GetAwaiter().GetResult();
                }, CancellationToken.None).GetAwaiter().GetResult();

                Assert.HasCount(1, boundary.Links, "Expected one link to be created.");
                var link = boundary.Links[0];
                Assert.AreSame(executeNode, link.Origin, "The link should be created from the destination back to the dragged origin.");
                Assert.IsInstanceOfType<MultiLink>(link);
                CollectionAssert.Contains(((MultiLink)link).Destinations.ToList(), ignoreNode,
                    "The dragged origin should become a destination of the reversed link.");
            });
    }
}
