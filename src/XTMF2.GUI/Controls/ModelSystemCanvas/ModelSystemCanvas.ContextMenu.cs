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
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Styling;
using XTMF2.GUI.ViewModels;
using XTMF2.ModelSystemConstruct;

namespace XTMF2.GUI.Controls;

partial class ModelSystemCanvas
{
    private void SetCanvasLinkGroupSelected(Link underlyingLink, bool selected)
    {
        if (_vm is null) return;
        foreach (var lvm in _vm.Links)
        {
            if (lvm.UnderlyingLink == underlyingLink)
            {
                lvm.IsSelected = selected;
            }
        }
    }

    private void RefreshMultiLinkSelectionVisuals()
    {
        if (_vm is null) return;
        foreach (var lvm in _vm.Links)
        {
            lvm.IsSelected = _multiLinkSelection.Contains(lvm.UnderlyingLink);
        }
    }

    private void RefreshMultiElementSelectionVisuals()
    {
        foreach (var el in _multiSelection)
        {
            el.IsSelected = true;
        }
    }

    private void ClearLinkMultiSelectionOnly()
    {
        if (_vm is null)
        {
            _multiLinkSelection.Clear();
            return;
        }

        foreach (var link in _multiLinkSelection)
        {
            SetCanvasLinkGroupSelected(link, false);
        }
        
        _multiLinkSelection.Clear();

        if (_vm.SelectedLink is not null)
        {
            _vm.SelectLinkCommand.Execute(null);
        }
    }

    private void ClearElementMultiSelectionOnly()
    {
        foreach (var el in _multiSelection)
        {
            el.IsSelected = false;
        }
        _multiSelection.Clear();

        if (_vm?.SelectedElement is { } primary)
        {
            primary.IsSelected = false;
            _vm.SelectedElement = null;
        }
    }

    private static Node? GetModuleTargetFromElement(ICanvasElement? element)
        => element switch
        {
            NodeViewModel nvm => nvm.UnderlyingNode,
            FunctionInstanceViewModel fivm => fivm.UnderlyingInstance,
            _ => null,
        };

    private static Grid CreateShortcutMenuHeader(string description, string shortcut)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            MinWidth = 280
        };

        var descriptionText = new TextBlock
        {
            Text = description
        };

        var shortcutText = new TextBlock
        {
            Text = shortcut,
            Margin = new Thickness(24, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Opacity = 0.65
        };

        Grid.SetColumn(descriptionText, 0);
        Grid.SetColumn(shortcutText, 1);
        grid.Children.Add(descriptionText);
        grid.Children.Add(shortcutText);
        return grid;
    }

    private List<Link> GetDisableTargetsForClickedLink(LinkViewModel clickedLink)
    {
        if (_multiLinkSelection.Count <= 1 || !_multiLinkSelection.Contains(clickedLink.UnderlyingLink))
            return new List<Link> { clickedLink.UnderlyingLink };

        var targets = _multiLinkSelection.ToList();
        if (targets.Count == 0)
            targets.Add(clickedLink.UnderlyingLink);
        return targets;
    }

    private List<Node> GetDisableTargetsForClickedElement(ICanvasElement clickedElement, Node clickedNode)
    {
        if (_multiSelection.Count <= 1 || !_multiSelection.Contains(clickedElement))
            return new List<Node> { clickedNode };

        var targets = _multiSelection
            .Select(GetModuleTargetFromElement)
            .Where(n => n is not null)
            .Distinct()
            .Cast<Node>()
            .ToList();

        if (targets.Count == 0)
            targets.Add(clickedNode);

        return targets;
    }

    private bool TrySetDisabledForModules(IReadOnlyList<Node> targets, bool nextDisabled, string defaultError)
    {
        if (_vm?.Session is null || _vm?.User is null || targets.Count == 0)
            return false;

        if (!_vm.Session.SetNodesDisabled(_vm.User, targets, nextDisabled, out var err))
        {
            _vm.ShowToast(err?.Message ?? defaultError, isError: true, durationMs: 6000);
        }

        return true;
    }

    private bool TryToggleSelectedModulesDisabled()
    {
        Node? selectedTarget = GetModuleTargetFromElement(_vm?.SelectedElement);

        List<Node> targets = _multiSelection.Count > 1
            ? _multiSelection.Select(GetModuleTargetFromElement)
                .Where(n => n is not null)
                .Distinct()
                .Cast<Node>()
                .ToList()
            : selectedTarget is not null
                ? new List<Node> { selectedTarget }
                : new List<Node>();

        if (targets.Count == 0)
            return false;

        Node anchor = selectedTarget is not null && targets.Contains(selectedTarget)
            ? selectedTarget
            : targets[0];

        bool nextDisabled = !anchor.IsDisabled;
        return TrySetDisabledForModules(targets, nextDisabled, "Unable to change disabled state.");
    }

    private bool TrySetDisabledForLink(Link target, bool nextDisabled, string defaultError)
        => TrySetDisabledForLinks(new[] { target }, nextDisabled, defaultError);

    private bool TrySetDisabledForLinks(IReadOnlyList<Link> targets, bool nextDisabled, string defaultError)
    {
        if (_vm?.Session is null || _vm?.User is null || targets.Count == 0)
            return false;

        if (!_vm.Session.SetLinksDisabled(_vm.User, targets, nextDisabled, out var err))
        {
            _vm.ShowToast(err?.Message ?? defaultError, isError: true, durationMs: 6000);
        }

        return true;
    }

    private bool TryToggleSelectedLinkDisabled()
    {
        Link? selectedTarget = _vm?.SelectedLink?.UnderlyingLink;
        var targets = _multiLinkSelection.Count > 0
            ? _multiLinkSelection.ToList()
            : selectedTarget is not null
                ? new List<Link> { selectedTarget }
                : new List<Link>();

        if (targets.Count == 0)
            return false;

        Link anchor = selectedTarget is not null && targets.Contains(selectedTarget)
            ? selectedTarget
            : targets[0];

        bool nextDisabled = !anchor.IsDisabled;
        return TrySetDisabledForLinks(targets, nextDisabled,
            "Unable to change link disabled state.");
    }

    private static bool IsDestinationBranchForElement(LinkViewModel link, ICanvasElement destinationElement)
        => (destinationElement, link.Destination) switch
        {
            (NodeViewModel targetNode, NodeViewModel destNode)
                => ReferenceEquals(targetNode.UnderlyingNode, destNode.UnderlyingNode),
            (FunctionInstanceViewModel targetFi, FunctionInstanceViewModel destFi)
                => ReferenceEquals(targetFi.UnderlyingInstance, destFi.UnderlyingInstance),
            _ => false,
        };

    private List<LinkViewModel> GetIncomingDestinationBranchTargetsForClickedElement(ICanvasElement clickedElement)
    {
        if (_vm is null)
            return new List<LinkViewModel>();

        var destinationTargets = _multiSelection.Count > 1 && _multiSelection.Contains(clickedElement)
            ? _multiSelection.Where(el => el is NodeViewModel or FunctionInstanceViewModel).ToList()
            : new List<ICanvasElement> { clickedElement };

        var incoming = _vm.Links
            .Where(lvm => destinationTargets.Any(dest => IsDestinationBranchForElement(lvm, dest)))
            .GroupBy(lvm => (lvm.UnderlyingLink, lvm.DestinationIndex))
            .Select(g => g.First())
            .ToList();

        return incoming;
    }

    private static bool AreAllDestinationBranchesHidden(IReadOnlyList<LinkViewModel> branches)
        => branches.Count > 0 && branches.All(b => b.IsDestinationBranchHidden);

    private bool TrySetDestinationBranchesHidden(IReadOnlyList<LinkViewModel> branches, bool hidden, string defaultError)
    {
        if (_vm?.Session is null || _vm?.User is null || branches.Count == 0)
            return false;

        var targets = branches.Select(b => (b.UnderlyingLink, b.DestinationIndex)).ToList();
        if (!_vm.Session.SetLinkDestinationBranchesHidden(_vm.User, targets, hidden, out var err))
        {
            _vm.ShowToast(err?.Message ?? defaultError, isError: true, durationMs: 6000);
            return false;
        }

        return true;
    }

    private static bool AreAllDestinationsHidden(Link link)
    {
        for (int i = 0; i < link.DestinationCount; i++)
        {
            if (!link.IsDestinationHidden(i))
                return false;
        }
        return true;
    }

    private bool TrySetLinkDestinationHidden(LinkViewModel linkVm, bool hidden, string defaultError)
    {
        if (_vm?.Session is null || _vm?.User is null)
            return false;

        if (!_vm.Session.SetLinkDestinationHidden(_vm.User, linkVm.UnderlyingLink,
                linkVm.DestinationIndex, hidden, out var err))
        {
            _vm.ShowToast(err?.Message ?? defaultError, isError: true, durationMs: 6000);
            return false;
        }

        return true;
    }

    private bool TrySetDestinationsHiddenForLinks(IReadOnlyList<Link> targets, bool hidden, string defaultError)
    {
        if (_vm?.Session is null || _vm?.User is null || targets.Count == 0)
            return false;

        if (!_vm.Session.SetLinksDestinationsHidden(_vm.User, targets, hidden, out var err))
        {
            _vm.ShowToast(err?.Message ?? defaultError, isError: true, durationMs: 6000);
            return false;
        }

        return true;
    }

    private void ShowContextMenu(ICanvasElement? element, LinkViewModel? link)
    {
        if (_vm is null) return;

        // ── Background right-click: offer Add items when nothing was hit ──
        if (element is null && link is null)
        {
            var bgMenu = new ContextMenu();
            var spawnPt = ToCanvasPos(_rightClickPressPos);

            var addStartItem = new MenuItem { Header = "Add Start…" };
            addStartItem.Click += (_, _) => _vm.AddStartAt(spawnPt.X, spawnPt.Y);
            bgMenu.Items.Add(addStartItem);

            var addModuleItem = new MenuItem { Header = CreateShortcutMenuHeader("Add Module…", "Ctrl+M") };
            addModuleItem.Click += (_, _) => _ = _vm.AddModuleAtAsync(spawnPt.X, spawnPt.Y);
            bgMenu.Items.Add(addModuleItem);

            var addCommentItem = new MenuItem { Header = CreateShortcutMenuHeader("Add Comment", "Ctrl+N") };
            addCommentItem.Click += (_, _) => _vm.AddCommentBlockAt(spawnPt.X, spawnPt.Y);
            bgMenu.Items.Add(addCommentItem);

            var addFtItem = new MenuItem { Header = CreateShortcutMenuHeader("Add Function Template…", "Ctrl+T") };
            addFtItem.Click += (_, _) => _ = _vm.AddFunctionTemplateAtAsync(spawnPt.X, spawnPt.Y);
            bgMenu.Items.Add(addFtItem);

            var addFiItem = new MenuItem { Header = CreateShortcutMenuHeader("Add Function Instance…", "Ctrl+I") };
            addFiItem.Click += (_, _) => _ = _vm.AddFunctionInstanceAtAsync(spawnPt.X, spawnPt.Y);
            bgMenu.Items.Add(addFiItem);

            if (_vm.IsInsideFunctionTemplate)
            {
                bgMenu.Items.Add(new Separator());
                var addFpItem = new MenuItem { Header = "Add Function Parameter…" };
                addFpItem.Click += (_, _) => _ = _vm.AddFunctionParameterDirectAsync(spawnPt.X, spawnPt.Y);
                bgMenu.Items.Add(addFpItem);
            }

            // ── Paste (always available; reads system clipboard at click-time) ────
            bgMenu.Items.Add(new Separator());
            var pasteItem = new MenuItem { Header = CreateShortcutMenuHeader("Paste", "Ctrl+V") };
            pasteItem.Click += (_, _) => _ = PasteElementsAsync(spawnPt.X, spawnPt.Y);
            bgMenu.Items.Add(pasteItem);

            bgMenu.Items.Add(new Separator());
            var showHiddenTempItem = new MenuItem
            {
                Header = "Show Hidden Destination Links (Temporary)",
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _vm.RenderAllHiddenDestinationLinks
            };
            showHiddenTempItem.Click += (_, _) =>
            {
                _vm.RenderAllHiddenDestinationLinks = showHiddenTempItem.IsChecked;
                InvalidateVisual();
            };
            bgMenu.Items.Add(showHiddenTempItem);

            var showGhostLinesItem = new MenuItem
            {
                Header = "Show Ghost Correspondence Lines",
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _vm.ShowGhostCorrespondenceLines
            };
            showGhostLinesItem.Click += (_, _) =>
            {
                _vm.ShowGhostCorrespondenceLines = showGhostLinesItem.IsChecked;
                InvalidateVisual();
            };
            bgMenu.Items.Add(showGhostLinesItem);

            ContextMenu = bgMenu;
            ContextMenu.Open(this);
            return;
        }

        var vm = _vm; // capture for closure

        var menu = new ContextMenu();

        // ── Hook-specific items ───────────────────────────────────────────
        if (_rightClickHookHit is { } hookEntry)
        {
            var capturedNode = hookEntry.Node;
            var capturedHook = hookEntry.Hook;

            // "Expand parameter to its own module" when the hook has an inlined BasicParam.
            if (_hookInlinedParam.TryGetValue((capturedNode, capturedHook), out var inlinedParamNode))
            {
                var capturedParam = inlinedParamNode;
                var expandItem = new MenuItem { Header = "Expand parameter to its own module" };
                expandItem.Click += (_, _) =>
                {
                    double rw = NodeRenderWidth(capturedNode);
                    capturedParam.ExpandToCanvas(capturedNode.X + rw + 30.0, capturedNode.Y);
                };
                menu.Items.Add(expandItem);

                // Offer switching between BasicParameter and ScriptedParameter.
                if (capturedParam.IsBasicParameter || capturedParam.IsScriptedParameter)
                {
                    var switchHeader = capturedParam.IsBasicParameter
                        ? "Switch to Scripted Parameter"
                        : "Switch to Basic Parameter";
                    var switchItem = new MenuItem { Header = switchHeader };
                    switchItem.Click += (_, _) =>
                    {
                        if (!capturedParam.SwitchParameterType(out var err))
                            vm.ShowToast(err?.Message ?? "Could not switch parameter type.",
                                         isError: true, durationMs: 6000);
                    };
                    menu.Items.Add(switchItem);
                }

                if (vm.IsInsideFunctionTemplate && capturedParam.IsBasicParameter)
                {
                    var convertItem = new MenuItem { Header = "Create Function Parameter from Basic Parameter" };
                    convertItem.Click += (_, _) => vm.ConvertBasicParameterToFunctionParameter(capturedParam);
                    menu.Items.Add(convertItem);
                }

                menu.Items.Add(new Separator());
            }

            var interBoundaryItem = new MenuItem { Header = "Link to node in another boundary…" };
            interBoundaryItem.Click += (_, _) =>
                _ = vm.CreateInterBoundaryLinkAsync(capturedNode, capturedHook);
            menu.Items.Add(interBoundaryItem);

            // If this hook already has a MultiLink, offer to reorder its destinations.
            var hookMultiLink = _vm.Links
                .Select(lvm => lvm.UnderlyingLink)
                .OfType<MultiLink>()
                .FirstOrDefault(ml => ml.OriginHook == capturedHook
                                   && ml.Origin == capturedNode.UnderlyingNode);
            if (hookMultiLink is not null)
            {
                var capturedHookMl = hookMultiLink;
                var reorderHookItem = new MenuItem { Header = "Reorder Destinations…" };
                reorderHookItem.Click += (_, _) => _ = vm.ReorderLinkDestinationsAsync(capturedHookMl);
                menu.Items.Add(reorderHookItem);
            }

            // Inside a function template, offer creating a FunctionParameter from a regular node hook.
            if (vm.IsInsideFunctionTemplate && capturedHook is not FunctionParameterHook)
            {
                var spawnPt2 = ToCanvasPos(_rightClickPressPos);
                var addFpItem = new MenuItem { Header = "Add Function Parameter" };
                addFpItem.Click += (_, _) =>
                    _ = vm.AddFunctionParameterFromHookAsync(
                        capturedNode, capturedHook, spawnPt2.X, spawnPt2.Y);
                menu.Items.Add(addFpItem);
            }

            // Clear all links from this hook if any exist.
            var hookLinks = _vm.Links
                .Where(lvm => lvm.UnderlyingLink.Origin == capturedNode.UnderlyingNode
                           && lvm.UnderlyingLink.OriginHook == capturedHook)
                .ToList();
            if (hookLinks.Count > 0)
            {
                var clearHookItem = new MenuItem { Header = "Clear Hook" };
                clearHookItem.Click += (_, _) => vm.ClearHookLinks(capturedNode, capturedHook);
                menu.Items.Add(clearHookItem);
            }

            menu.Items.Add(new Separator());
        }

        // ── FunctionInstance hook right-click items ────────────────────────────
        if (_rightClickFiHookHit is { } fiHookEntry)
        {
            var capturedFiOrigin = fiHookEntry.Fi;
            var capturedFpHook = fiHookEntry.Hook;

            // "Expand parameter to its own module" when the FI hook has an inlined param.
            if (_fiHookInlinedParam.TryGetValue((capturedFiOrigin, capturedFpHook), out var inlinedFiParamNode))
            {
                var capturedFiParam = inlinedFiParamNode;
                var expandFiItem = new MenuItem { Header = "Expand parameter to its own module" };
                expandFiItem.Click += (_, _) =>
                {
                    double rw2 = capturedFiOrigin.Width;
                    capturedFiParam.ExpandToCanvas(capturedFiOrigin.X + rw2 + 30.0, capturedFiOrigin.Y);
                };
                menu.Items.Add(expandFiItem);

                // Offer switching between BasicParameter and ScriptedParameter.
                if (capturedFiParam.IsBasicParameter || capturedFiParam.IsScriptedParameter)
                {
                    var switchFiHeader = capturedFiParam.IsBasicParameter
                        ? "Switch to Scripted Parameter"
                        : "Switch to Basic Parameter";
                    var switchFiItem = new MenuItem { Header = switchFiHeader };
                    switchFiItem.Click += (_, _) =>
                    {
                        if (!capturedFiParam.SwitchParameterType(out var err))
                            vm.ShowToast(err?.Message ?? "Could not switch parameter type.",
                                         isError: true, durationMs: 6000);
                    };
                    menu.Items.Add(switchFiItem);
                }
            }

            var allBoundariesItem = new MenuItem { Header = "Link to node in another boundary…" };
            allBoundariesItem.Click += (_, _) =>
                _ = vm.CreateInterBoundaryLinkAsync(capturedFiOrigin, capturedFpHook);
            menu.Items.Add(allBoundariesItem);

            // Clear all links from this FI hook if any exist.
            var fiHookLinks = _vm.Links
                .Where(lvm => lvm.UnderlyingLink.Origin == capturedFiOrigin.UnderlyingInstance
                           && lvm.UnderlyingLink.OriginHook == capturedFpHook)
                .ToList();
            if (fiHookLinks.Count > 0)
            {
                var clearFiHookItem = new MenuItem { Header = "Clear Hook" };
                clearFiHookItem.Click += (_, _) => vm.ClearFiHookLinks(capturedFiOrigin, capturedFpHook);
                menu.Items.Add(clearFiHookItem);
            }

            menu.Items.Add(new Separator());
        }

        // ── Link routing style ────────────────────────────────────────────────
        if (link is not null)
        {
            var capturedRoutingLink = link;
            var routingHeader = link.UnderlyingLink.IsOrthogonal
                ? "Switch to Curved Routing"
                : "Switch to Orthogonal Routing";
            var routingItem = new MenuItem { Header = routingHeader };
            var routingTargets = GetDisableTargetsForClickedLink(capturedRoutingLink);
            routingItem.Click += (_, _) => vm.ToggleLinksOrthogonal(
                routingTargets, !capturedRoutingLink.UnderlyingLink.IsOrthogonal);
            menu.Items.Add(routingItem);

            bool nextDestinationHidden = !link.IsDestinationBranchHidden;
            var toggleDestinationItem = new MenuItem
            {
                Header = nextDestinationHidden ? "Hide This Destination Branch" : "Show This Destination Branch"
            };
            toggleDestinationItem.Click += (_, _) =>
            {
                TrySetLinkDestinationHidden(link, nextDestinationHidden,
                    "Unable to change destination branch visibility.");
                InvalidateVisual();
            };
            menu.Items.Add(toggleDestinationItem);

            var visibilityTargets = GetDisableTargetsForClickedLink(link);
            bool nextHideAllDestinations = !AreAllDestinationsHidden(link.UnderlyingLink);
            var toggleAllDestinationsItem = new MenuItem
            {
                Header = visibilityTargets.Count > 1
                    ? (nextHideAllDestinations
                        ? $"Hide All Destinations For {visibilityTargets.Count} Links"
                        : $"Show All Destinations For {visibilityTargets.Count} Links")
                    : (nextHideAllDestinations
                        ? "Hide All Destinations For This Link"
                        : "Show All Destinations For This Link")
            };
            toggleAllDestinationsItem.Click += (_, _) =>
            {
                TrySetDestinationsHiddenForLinks(visibilityTargets, nextHideAllDestinations,
                    "Unable to change destination visibility.");
                InvalidateVisual();
            };
            menu.Items.Add(toggleAllDestinationsItem);

            var disableTargets = GetDisableTargetsForClickedLink(link);
            bool nextDisabled = !link.UnderlyingLink.IsDisabled;
            var toggleDisableItem = new MenuItem
            {
                Header = disableTargets.Count > 1
                    ? (nextDisabled ? $"Disable {disableTargets.Count} Links" : $"Enable {disableTargets.Count} Links")
                    : (nextDisabled ? "Disable Link" : "Enable Link")
            };
            toggleDisableItem.Click += (_, _) =>
            {
                TrySetDisabledForLinks(disableTargets, nextDisabled,
                    "Unable to change link disabled state.");
                InvalidateVisual();
            };
            menu.Items.Add(toggleDisableItem);
            menu.Items.Add(new Separator());
        }

        // ── MultiLink: reorder destinations ──────────────────────────────────
        if (link?.UnderlyingLink is MultiLink reorderMl)
        {
            var capturedReorderMl = reorderMl;
            var reorderLinkItem = new MenuItem { Header = "Reorder Destinations…" };
            reorderLinkItem.Click += (_, _) => _ = vm.ReorderLinkDestinationsAsync(capturedReorderMl);
            menu.Items.Add(reorderLinkItem);
            menu.Items.Add(new Separator());
        }

        if (element is NodeViewModel fileNodeVm && IsFilePathTargetNode(fileNodeVm))
        {
            var fileMenu = new MenuItem { Header = "File" };

            var openFileItem = new MenuItem { Header = "Open" };
            openFileItem.Click += async (_, _) => await TryOpenOpenReadStreamFromFileParameterAsync(fileNodeVm);
            fileMenu.Items.Add(openFileItem);

            var openParentDirectoryItem = new MenuItem { Header = "Open Parent Directory" };
            openParentDirectoryItem.Click += async (_, _) => await TryOpenOpenReadStreamFromFileParentDirectoryAsync(fileNodeVm);
            fileMenu.Items.Add(openParentDirectoryItem);

            var setFileItem = new MenuItem { Header = "Set File…" };
            setFileItem.Click += async (_, _) => await TryUpdateOpenReadStreamFromFileParameterAsync(false, fileNodeVm);
            fileMenu.Items.Add(setFileItem);

            var setDirectoryItem = new MenuItem { Header = "Set Directory…" };
            setDirectoryItem.Click += async (_, _) => await TryUpdateOpenReadStreamFromFileParameterAsync(true, fileNodeVm);
            fileMenu.Items.Add(setDirectoryItem);

            menu.Items.Add(fileMenu);
            menu.Items.Add(new Separator());
        }

        // ── Standard Delete ───────────────────────────────────────────────
        if (element is CommentBlockViewModel commentVm
            && vm.CurrentFunctionTemplate is { } currentTemplate
            && currentTemplate.UnderlyingTemplate.InternalModules.CommentBlocks.Contains(commentVm.UnderlyingBlock))
        {
            bool isDescription = ReferenceEquals(currentTemplate.UnderlyingTemplate.DescriptionComment,
                                                  commentVm.UnderlyingBlock);
            var descriptionItem = new MenuItem
            {
                Header = isDescription
                    ? "Clear Function Template Description"
                    : "Use as Function Template Description"
            };
            descriptionItem.Click += (_, _) =>
            {
                if (!vm.Session.SetFunctionTemplateDescriptionComment(
                        vm.User,
                        currentTemplate.UnderlyingTemplate,
                        isDescription ? null : commentVm.UnderlyingBlock,
                        out var error))
                {
                    vm.ShowToast(error?.Message ?? "Unable to update the function template description.",
                        isError: true, durationMs: 5000);
                }
                InvalidateAndMeasure();
            };
            menu.Items.Add(descriptionItem);
            menu.Items.Add(new Separator());
        }

        var deleteItem = new MenuItem { Header = "Delete" };
        deleteItem.Click += (_, _) =>
        {
            if (element is not null)
                vm.SelectElementCommand.Execute(element);
            else
                vm.SelectLinkCommand.Execute(link);
            _ = vm.DeleteSelectedCommand.ExecuteAsync(null);
        };

        // ── Enable/Disable regular nodes ─────────────────────────────────
        if (element is NodeViewModel disableNodeVm)
        {
            var incomingDestinationBranches = GetIncomingDestinationBranchTargetsForClickedElement(disableNodeVm);
            if (incomingDestinationBranches.Count > 0)
            {
                bool nextIncomingHidden = !AreAllDestinationBranchesHidden(incomingDestinationBranches);
                var toggleIncomingItem = new MenuItem
                {
                    Header = nextIncomingHidden
                        ? "Hide Incoming Links"
                        : "Show Incoming Links"
                };
                toggleIncomingItem.Click += (_, _) =>
                {
                    TrySetDestinationBranchesHidden(incomingDestinationBranches, nextIncomingHidden,
                        "Unable to change incoming link visibility.");
                    InvalidateVisual();
                };
                menu.Items.Add(toggleIncomingItem);
                menu.Items.Add(new Separator());
            }

            var disableTargets = GetDisableTargetsForClickedElement(disableNodeVm, disableNodeVm.UnderlyingNode);
            bool nextDisabled = !disableNodeVm.UnderlyingNode.IsDisabled;
            var disableNodeItem = new MenuItem
            {
                Header = disableTargets.Count > 1
                    ? (nextDisabled ? $"Disable {disableTargets.Count} Modules" : $"Enable {disableTargets.Count} Modules")
                    : (nextDisabled ? "Disable Node" : "Enable Node")
            };
            disableNodeItem.Click += (_, _) =>
            {
                TrySetDisabledForModules(disableTargets, nextDisabled, "Unable to change node disabled state.");
                InvalidateVisual();
            };
            menu.Items.Add(disableNodeItem);
            menu.Items.Add(new Separator());
        }

        // ── Variable list management + inline option ────────────────────
        if (element is NodeViewModel paramNode && paramNode.IsParameterNode)
        {
            // "Inline parameter" — only when this BasicParam is wired to a Single hook.
            if (_canInlineNodes.Contains(paramNode))
            {
                var inlineItem = new MenuItem { Header = "Inline parameter into parent hook" };
                inlineItem.Click += (_, _) => paramNode.InlineBasicParameter();
                menu.Items.Add(inlineItem);
            }

            // Offer switching between BasicParameter and ScriptedParameter.
            if (paramNode.IsBasicParameter || paramNode.IsScriptedParameter)
            {
                var switchHeader = paramNode.IsBasicParameter
                    ? "Switch to Scripted Parameter"
                    : "Switch to Basic Parameter";
                var capturedParamNode = paramNode;
                var switchItem = new MenuItem { Header = switchHeader };
                switchItem.Click += (_, _) =>
                {
                    if (!capturedParamNode.SwitchParameterType(out var err))
                        vm.ShowToast(err?.Message ?? "Could not switch parameter type.",
                                     isError: true, durationMs: 6000);
                };
                menu.Items.Add(switchItem);
            }

            if (vm.IsInsideFunctionTemplate && paramNode.IsBasicParameter)
            {
                var capturedBasicParameter = paramNode;
                var convertItem = new MenuItem { Header = "Create Function Parameter from Basic Parameter" };
                convertItem.Click += (_, _) => vm.ConvertBasicParameterToFunctionParameter(capturedBasicParameter);
                menu.Items.Add(convertItem);
            }

            menu.Items.Add(new Separator());

            if (vm.IsInsideFunctionTemplate)
            {
                // Inside a FunctionTemplate: only offer local variables for eligible node types.
                if (FunctionTemplate.IsValidLocalVariableNode(paramNode.UnderlyingNode, out _))
                {
                    bool alreadyLocalVar = vm.IsNodeInLocalVariables(paramNode);
                    var localVarHeader = alreadyLocalVar
                        ? "Remove from Local Variables"
                        : "Add as Local Variable";
                    var localVarItem = new MenuItem { Header = localVarHeader };
                    localVarItem.Click += (_, _) =>
                    {
                        if (vm.IsNodeInLocalVariables(paramNode))
                        {
                            _ = vm.RemoveNodeFromLocalVariablesAsync(paramNode);
                        }
                        else
                        {
                            _ = vm.AddNodeToLocalVariablesAsync(paramNode);
                        }
                    };
                    menu.Items.Add(localVarItem);
                    menu.Items.Add(new Separator());
                }
            }
            else
            {
                bool alreadyVar = vm.IsNodeInVariables(paramNode);
                var varHeader = alreadyVar
                    ? "Remove from Model System Variables"
                    : "Add to Model System Variables";
                var varItem = new MenuItem { Header = varHeader };
                varItem.Click += (_, _) =>
                {
                    if (vm.IsNodeInVariables(paramNode))
                    {
                        _ = vm.RemoveNodeFromVariablesAsync(paramNode);
                    }
                    else
                    {
                        _ = vm.AddNodeToVariablesAsync(paramNode);
                    }
                };
                menu.Items.Add(varItem);
                menu.Items.Add(new Separator());

                // ── Estimation / Calibration (float or double BasicParameter only) ──
                if (paramNode.IsNumericBasicParameter)
                {
                    bool alreadyEst = vm.IsNodeInEstimation(paramNode);
                    var estHeader = alreadyEst
                        ? "Remove from Estimation Parameters"
                        : "Add to Estimation Parameters";
                    var capturedEstNode = paramNode;
                    var estItem = new MenuItem { Header = estHeader };
                    estItem.Click += (_, _) =>
                    {
                        if (vm.IsNodeInEstimation(capturedEstNode))
                            _ = vm.RemoveNodeFromEstimationAsync(capturedEstNode);
                        else
                            _ = vm.AddNodeToEstimationAsync(capturedEstNode);
                    };
                    menu.Items.Add(estItem);

                    bool alreadyCal = vm.IsNodeInCalibration(paramNode);
                    var calHeader = alreadyCal
                        ? "Remove from Calibration Parameters"
                        : "Add to Calibration Parameters";
                    var capturedCalNode = paramNode;
                    var calItem = new MenuItem { Header = calHeader };
                    calItem.Click += (_, _) =>
                    {
                        if (vm.IsNodeInCalibration(capturedCalNode))
                            _ = vm.RemoveNodeFromCalibrationAsync(capturedCalNode);
                        else
                            _ = vm.AddNodeToCalibrationAsync(capturedCalNode);
                    };
                    menu.Items.Add(calItem);
                    menu.Items.Add(new Separator());
                }
            }
        }

        // ── Variable management for FunctionInstances ────────────────────
        if (!_vm.IsInsideFunctionTemplate
            && element is FunctionInstanceViewModel fiVarCandidate
            && FunctionTemplate.ExtractFunctionInstanceVariableType(fiVarCandidate.UnderlyingInstance) is not null)
        {
            bool alreadyFiVar = vm.IsFunctionInstanceInVariables(fiVarCandidate);
            var fiVarHeader = alreadyFiVar
                ? "Remove from Model System Variables"
                : "Add to Model System Variables";
            var capturedFiVar = fiVarCandidate;
            var fiVarItem = new MenuItem { Header = fiVarHeader };
            fiVarItem.Click += (_, _) =>
            {
                if (vm.IsFunctionInstanceInVariables(capturedFiVar))
                    _ = vm.RemoveFunctionInstanceFromVariablesAsync(capturedFiVar);
                else
                    _ = vm.AddFunctionInstanceToVariablesAsync(capturedFiVar);
            };
            menu.Items.Add(fiVarItem);
            menu.Items.Add(new Separator());
        }

        // ── IFunction<T> → Create linked ExecuteWithContext ───────────────
        if (element is NodeViewModel funcNode)
        {
            var nodeType = funcNode.UnderlyingNode.Type;
            var iFunctionOpen = typeof(IFunction<>);
            Type? returnType = null;
            if (nodeType is not null)
            {
                foreach (var iface in nodeType.GetInterfaces())
                {
                    if (iface.IsGenericType && iface.GetGenericTypeDefinition() == iFunctionOpen)
                    {
                        returnType = iface.GetGenericArguments()[0];
                        break;
                    }
                }
            }

            if (returnType is not null)
            {
                var capturedFuncNode = funcNode;
                var wrapItem = new MenuItem
                {
                    Header = $"Create ExecuteWithContext<{returnType.Name}> (linked)"
                };
                wrapItem.Click += (_, _) => _ = vm.CreateExecuteWithContextAsync(capturedFuncNode);
                menu.Items.Add(wrapItem);
                menu.Items.Add(new Separator());
            }
        }

        // ── Create Ghost Node + Move to Boundary (regular nodes) ───────────────────
        if (element is NodeViewModel ghostSourceNode)
        {
            var capturedGhostSource = ghostSourceNode;

            var ghostItem = new MenuItem { Header = "Create Ghost Node" };
            ghostItem.Click += (_, _) =>
            {
                double rw = NodeRenderWidth(capturedGhostSource);
                double rh = NodeRenderHeight(capturedGhostSource);
                const double gap = 30.0;
                int gx = (int)(capturedGhostSource.X + rw + gap);
                int gy = (int)capturedGhostSource.Y;
                int gw = (int)rw;
                int gh = (int)rh;
                vm.CreateGhostNode(capturedGhostSource, gx, gy, gw, gh);
            };

            var moveNodeItem = new MenuItem { Header = "Move to Boundary…" };
            moveNodeItem.Click += async (_, _) =>
            {
                await vm.MoveNodeToBoundaryAsync(capturedGhostSource);
                InvalidateAndMeasure();
            };

            menu.Items.Add(new Separator());
            menu.Items.Add(ghostItem);
            menu.Items.Add(moveNodeItem);
        }

        // ── Extract to Function Template ───────────────────────────────────────
        // Available for regular nodes and function instances when NOT already inside
        // a function template's InternalModules view.
        if ((element is NodeViewModel extractSourceNode && !extractSourceNode.IsParameterNode
             || element is FunctionInstanceViewModel)
            && !vm.IsInsideFunctionTemplate)
        {
            // If the right-clicked element is part of a multi-selection, operate on all
            // selected extractable elements; otherwise operate on just this one.
            IReadOnlyList<ICanvasElement> selElements =
                _multiSelection.Count > 1 && _multiSelection.Contains(element)
                ? _multiSelection.ToList()
                : new[] { element! };

            int extractCount = selElements.Count(el =>
                el is NodeViewModel envm && !envm.IsParameterNode
                || el is FunctionInstanceViewModel);

            string extractDescription = extractCount > 1
                ? $"Extract {extractCount} Elements to Function Template…"
                : "Extract to Function Template…";

            var capturedExtractElements = selElements;
            var extractItem = new MenuItem { Header = CreateShortcutMenuHeader(extractDescription, "Ctrl+Shift+M") };
            extractItem.Click += (_, _) => _ = vm.ExtractSelectionToFunctionTemplateAsync(capturedExtractElements);
            menu.Items.Add(new Separator());
            menu.Items.Add(extractItem);
        }

        // ── Move to Boundary (ghost nodes) ────────────────────────────────────
        if (element is GhostNodeViewModel capturedGhost)
        {
            var goToRepresentedItem = new MenuItem
            {
                Header = CreateShortcutMenuHeader("Go to Represented Element", "Ctrl+Enter")
            };
            goToRepresentedItem.Click += (_, _) =>
            {
                vm.NavigateToElementById(capturedGhost.ReferencedNode.Id);
                InvalidateAndMeasure();
            };

            var moveGhostItem = new MenuItem { Header = "Move to Boundary…" };
            moveGhostItem.Click += async (_, _) =>
            {
                await vm.MoveGhostNodeToBoundaryAsync(capturedGhost);
                InvalidateAndMeasure();
            };
            menu.Items.Add(new Separator());
            menu.Items.Add(goToRepresentedItem);
            menu.Items.Add(moveGhostItem);
        }

        // ── Function template – specific items ─────────────────────────────
        if (element is FunctionTemplateViewModel capturedFt)
        {
            // Enter: navigate into the template's InternalModules
            var enterItem = new MenuItem { Header = "Edit Contents (double-click)" };
            enterItem.Click += (_, _) =>
            {
                vm.NavigateIntoFunctionTemplate(capturedFt);
                InvalidateAndMeasure();
            };

            // Rename
            var renameItem = new MenuItem { Header = "Rename…" };
            renameItem.Click += async (_, _) =>
            {
                await vm.RenameFunctionTemplateAsync(capturedFt);
                InvalidateAndMeasure();
            };

            // Move to Boundary
            var moveFtItem = new MenuItem { Header = "Move to Boundary…" };
            moveFtItem.Click += async (_, _) =>
            {
                await vm.MoveFunctionTemplateToBoundaryAsync(capturedFt);
                InvalidateAndMeasure();
            };

            menu.Items.Add(new Separator());
            menu.Items.Add(enterItem);
            menu.Items.Add(renameItem);
            menu.Items.Add(moveFtItem);
        }

        // ── Function instance – specific items ─────────────────────────────
        if (element is FunctionInstanceViewModel capturedFi)
        {
            var incomingDestinationBranches = GetIncomingDestinationBranchTargetsForClickedElement(capturedFi);
            if (incomingDestinationBranches.Count > 0)
            {
                bool nextIncomingHidden = !AreAllDestinationBranchesHidden(incomingDestinationBranches);
                var toggleIncomingItem = new MenuItem
                {
                    Header = nextIncomingHidden
                        ? "Hide Incoming Links"
                        : "Show Incoming Links"
                };
                toggleIncomingItem.Click += (_, _) =>
                {
                    TrySetDestinationBranchesHidden(incomingDestinationBranches, nextIncomingHidden,
                        "Unable to change incoming link visibility.");
                    InvalidateVisual();
                };
                menu.Items.Add(new Separator());
                menu.Items.Add(toggleIncomingItem);
            }

            var openTemplateItem = new MenuItem { Header = "Open Template" };
            openTemplateItem.Click += (_, _) =>
            {
                vm.OpenFunctionTemplateOfInstance(capturedFi);
                InvalidateAndMeasure();
            };

            var expandItem = new MenuItem { Header = "Expand Function Instance" };
            expandItem.Click += async (_, _) =>
            {
                await vm.ExpandFunctionInstanceAsync(capturedFi);
                InvalidateAndMeasure();
            };

            var renameItem = new MenuItem { Header = "Rename…" };
            renameItem.Click += async (_, _) =>
            {
                await vm.RenameFunctionInstanceAsync(capturedFi);
                InvalidateAndMeasure();
            };

            var moveFiItem = new MenuItem { Header = "Move to Boundary…" };
            moveFiItem.Click += async (_, _) =>
            {
                await vm.MoveFunctionInstanceToBoundaryAsync(capturedFi);
                InvalidateAndMeasure();
            };

            var disableTargets = GetDisableTargetsForClickedElement(capturedFi, capturedFi.UnderlyingInstance);
            bool nextDisabled = !capturedFi.UnderlyingInstance.IsDisabled;
            var disableFiItem = new MenuItem
            {
                Header = disableTargets.Count > 1
                    ? (nextDisabled ? $"Disable {disableTargets.Count} Modules" : $"Enable {disableTargets.Count} Modules")
                    : (nextDisabled ? "Disable Function Instance" : "Enable Function Instance")
            };
            disableFiItem.Click += (_, _) =>
            {
                TrySetDisabledForModules(disableTargets, nextDisabled, "Unable to change function instance disabled state.");
                InvalidateVisual();
            };

            menu.Items.Add(new Separator());
            menu.Items.Add(openTemplateItem);
            menu.Items.Add(expandItem);
            menu.Items.Add(renameItem);
            menu.Items.Add(moveFiItem);
            menu.Items.Add(disableFiItem);
        }

        // ── "Add Function Parameter" — when we're inside a function template ─────
        if (_vm.IsInsideFunctionTemplate && element is FunctionParameterViewModel)
        {
            // Right-clicking an existing FunctionParameter: offer rename or remove.
            var fpvm = (FunctionParameterViewModel)element;
            var template = _vm.CurrentFunctionTemplate?.UnderlyingTemplate;

            // ── Local-variable toggle (only for IFunction<basicType> parameters) ──
            if (template is not null
                && FunctionTemplate.IsValidLocalVariableNode(fpvm.UnderlyingParameter, out _))
            {
                bool isAlreadyVar = template.LocalVariables.Contains(fpvm.UnderlyingParameter);
                var localVarHeader = isAlreadyVar
                    ? "Remove from Local Variables"
                    : "Add as Local Variable";
                var localVarItem = new MenuItem { Header = localVarHeader };
                localVarItem.Click += async (_, _) =>
                {
                    await vm.ToggleFunctionTemplateVariableAsync(fpvm.UnderlyingParameter);
                    InvalidateAndMeasure();
                };
                menu.Items.Add(new Separator());
                menu.Items.Add(localVarItem);
            }

            var fpRenameItem = new MenuItem { Header = "Rename…" };
            fpRenameItem.Click += async (_, _) =>
            {
                await vm.RenameFunctionParameterAsync(fpvm);
                InvalidateAndMeasure();
            };

            var removeItem = new MenuItem { Header = "Remove Function Parameter" };
            removeItem.Click += (_, _) =>
            {
                vm.RemoveFunctionParameterAsync(fpvm.UnderlyingParameter);
                InvalidateAndMeasure();
            };
            menu.Items.Add(new Separator());
            menu.Items.Add(fpRenameItem);
            menu.Items.Add(removeItem);
        }
        // ── "Set as Entry Node" — any node while viewing InternalModules ─────────
        if (_vm.IsInsideFunctionTemplate && element is NodeViewModel entryNodeCandidate)
        {
            var currentEntry = _vm.CurrentFunctionTemplate?.UnderlyingTemplate.EntryNode;
            bool alreadyEntry = ReferenceEquals(currentEntry, entryNodeCandidate.UnderlyingNode);
            var entryHeader = alreadyEntry ? "Clear Entry Node" : "Set as Entry Node";
            var capturedEntryCandidate = entryNodeCandidate;
            var entryItem = new MenuItem { Header = entryHeader };
            entryItem.Click += async (_, _) =>
            {
                await vm.SetFunctionTemplateEntryNodeAsync(capturedEntryCandidate);
                InvalidateAndMeasure();
            };
            menu.Items.Add(new Separator());
            menu.Items.Add(entryItem);
        }

        // ── Copy ──────────────────────────────────────────────────────────────
        bool isCopyable = element switch
        {
            NodeViewModel nvm            => !nvm.IsInlined,
            CommentBlockViewModel        => true,
            FunctionTemplateViewModel    => true,
            FunctionInstanceViewModel    => true,
            GhostNodeViewModel           => true,
            _                            => false,
        };
        if (isCopyable)
        {
            var capturedElement = element;
            menu.Items.Add(new Separator());

            // Count copyable elements in the active selection.
            int copyCount = _multiSelection.Count > 0 && _multiSelection.Contains(capturedElement!)
                ? _multiSelection.Count(el => el switch
                  {
                      NodeViewModel nvm2   => !nvm2.IsInlined,
                      CommentBlockViewModel    => true,
                      FunctionTemplateViewModel => true,
                      FunctionInstanceViewModel => true,
                      GhostNodeViewModel       => true,
                      _                        => false,
                  })
                : 1;
            string copyDescription = copyCount > 1 ? $"Copy {copyCount} Elements" : "Copy";

            var copyItem = new MenuItem { Header = CreateShortcutMenuHeader(copyDescription, "Ctrl+C") };
            copyItem.Click += (_, _) =>
            {
                // Narrow selection to just this element when it isn't already multi-selected.
                if (_multiSelection.Count == 0 || !_multiSelection.Contains(capturedElement!))
                {
                    ClearMultiSelection();
                    if (_vm is not null) _vm.SelectElementCommand.Execute(capturedElement);
                }
                _ = CopySelectedElementsAsync();
            };
            menu.Items.Add(copyItem);
        }

        // ── Align (multi-selection only) ──────────────────────────────────────
        if (_multiSelection.Count > 1)
        {
            menu.Items.Add(new Separator());
            var alignMenu = new MenuItem { Header = "Align" };

            void AddAlignItem(string header, AlignMode m)
            {
                var item = new MenuItem { Header = header };
                item.Click += (_, _) => AlignSelectedElements(m);
                alignMenu.Items.Add(item);
            }

            AddAlignItem("Align Left Edges",          AlignMode.Left);
            AddAlignItem("Align Right Edges",         AlignMode.Right);
            alignMenu.Items.Add(new Separator());
            AddAlignItem("Align Top Edges",           AlignMode.Top);
            AddAlignItem("Align Bottom Edges",        AlignMode.Bottom);
            alignMenu.Items.Add(new Separator());
            AddAlignItem("Center on Vertical Axis",   AlignMode.CenterVertical);
            AddAlignItem("Center on Horizontal Axis", AlignMode.CenterHorizontal);
            menu.Items.Add(alignMenu);

            // Distribute requires at least 3 elements to have visible effect.
            if (_multiSelection.Count >= 3)
            {
                var distributeMenu = new MenuItem { Header = "Distribute" };

                var distH = new MenuItem { Header = "Distribute Horizontally" };
                distH.Click += (_, _) => DistributeSelectedElements(horizontal: true);
                distributeMenu.Items.Add(distH);

                var distV = new MenuItem { Header = "Distribute Vertically" };
                distV.Click += (_, _) => DistributeSelectedElements(horizontal: false);
                distributeMenu.Items.Add(distV);

                menu.Items.Add(distributeMenu);
            }
        }

        menu.Items.Add(deleteItem);

        ContextMenu = menu;
        ContextMenu.Open(this);
    }

}
