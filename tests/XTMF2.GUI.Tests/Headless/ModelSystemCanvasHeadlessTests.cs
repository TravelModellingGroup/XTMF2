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

using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.GUI.Controls;
using XTMF2.GUI.Tests.Modules;
using XTMF2.GUI.ViewModels;
using XTMF2.ModelSystemConstruct;
using System.Linq;
using System.Reflection;
using System;

namespace XTMF2.GUI.Tests.Headless;

/// <summary>
/// Headless smoke tests for <see cref="ModelSystemCanvas"/> — verifies the
/// control can be instantiated and measured without a full runtime context.
/// </summary>
[TestClass]
public class ModelSystemCanvasHeadlessTests
{
    private HeadlessUnitTestSession Session => HeadlessAppLifetime.HeadlessSession!;

    [TestMethod]
    public void ModelSystemCanvas_CanBeInstantiated_WithNoViewModel()
    {
        Session.Dispatch(() =>
        {
            var canvas = new ModelSystemCanvas();
            Assert.IsNotNull(canvas);
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    }

    [TestMethod]
    public void ModelSystemCanvas_Measure_DoesNotThrow()
    {
        Session.Dispatch(() =>
        {
            var canvas = new ModelSystemCanvas();
            canvas.Measure(new Avalonia.Size(800, 600));
            // No exception means success; Bounds may be zero without layout root.
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    }

    [TestMethod]
    public void ModelSystemCanvas_DataContext_DefaultsToNull()
    {
        Session.Dispatch(() =>
        {
            var canvas = new ModelSystemCanvas();
            // Without setting DataContext, the canvas VM is null.
            Assert.IsNull(canvas.DataContext);
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    }

    [TestMethod]
    public void ModelSystemCanvas_AddShortcuts_CreateCommentAndFunctionTemplate()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(ModelSystemCanvas_AddShortcuts_CreateCommentAndFunctionTemplate),
            (user, projectSession, msSession) =>
            {
                using var vm = new ModelSystemEditorViewModel(msSession, user, runController: null);

                Session.Dispatch(() =>
                {
                    var canvas = new ModelSystemCanvas { DataContext = vm };
                    canvas.Measure(new Avalonia.Size(800, 600));
                    canvas.Arrange(new Avalonia.Rect(0, 0, 800, 600));

                    var handleAddShortcut = typeof(ModelSystemCanvas).GetMethod(
                        "TryHandleAddShortcut",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.IsNotNull(handleAddShortcut);

                    var commentShortcut = new KeyEventArgs
                    {
                        Key = Key.N,
                        KeyModifiers = KeyModifiers.Control
                    };
                    var templateShortcut = new KeyEventArgs
                    {
                        Key = Key.T,
                        KeyModifiers = KeyModifiers.Control
                    };

                    Assert.IsTrue((bool)handleAddShortcut!.Invoke(canvas, new object[] { commentShortcut })!);
                    Assert.IsTrue((bool)handleAddShortcut.Invoke(canvas, new object[] { templateShortcut })!);

                    Assert.HasCount(1, vm.CommentBlocks);
                    Assert.HasCount(1, vm.FunctionTemplates);
                }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            });
    }

    [TestMethod]
    public void ModelSystemCanvas_ResolveFilePathTargetNode_TargetsParameterNodeFromHook()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(ModelSystemCanvas_ResolveFilePathTargetNode_TargetsParameterNodeFromHook),
            (user, projectSession, msSession) =>
            {
                var boundary = msSession.ModelSystem.GlobalBoundary;
                Assert.IsTrue(msSession.AddNode(
                    user,
                    boundary,
                    "OpenReadStreamModule",
                    typeof(XTMF2.RuntimeModules.OpenReadStreamFromFile),
                    new Rectangle(20, 20, 160, 60),
                    out var sourceNode,
                    out var error), error?.Message);
                Assert.IsNotNull(sourceNode);

                Assert.IsTrue(msSession.AddNode(
                    user,
                    boundary,
                    "FilePathParameter",
                    typeof(XTMF2.RuntimeModules.BasicParameter<string>),
                    new Rectangle(220, 20, 160, 60),
                    out var parameterNode,
                    out var error2), error2?.Message);
                Assert.IsNotNull(parameterNode);

                var filePathHook = sourceNode!.Hooks.First(h => h.Name == "File Path");
                Assert.IsTrue(msSession.AddLink(user, sourceNode, filePathHook, parameterNode!, out _, out var linkError), linkError?.Message);

                using var vm = new ModelSystemEditorViewModel(msSession, user, runController: null);
                var sourceVm = vm.Nodes.FirstOrDefault(n => ReferenceEquals(n.UnderlyingNode, sourceNode));
                Assert.IsNotNull(sourceVm);

                var canvas = new ModelSystemCanvas();
                var method = typeof(ModelSystemCanvas).GetMethod(
                    "ResolveFilePathTargetNode",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(method);

                var result = (Node?)method!.Invoke(canvas, new object[] { sourceVm! });
                Assert.AreSame(parameterNode, result);
            });
    }

    [TestMethod]
    public void ModelSystemCanvas_ContextMenu_ShowsFileSubmenuForStringFilePathNodes()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(ModelSystemCanvas_ContextMenu_ShowsFileSubmenuForStringFilePathNodes),
            (user, projectSession, msSession) =>
            {
                var boundary = msSession.ModelSystem.GlobalBoundary;
                Assert.IsTrue(msSession.AddNode(
                    user,
                    boundary,
                    "FilePathParameter",
                    typeof(XTMF2.RuntimeModules.BasicParameter<string>),
                    new Rectangle(20, 20, 160, 60),
                    out var parameterNode,
                    out var error), error?.Message);
                Assert.IsNotNull(parameterNode);

                using var vm = new ModelSystemEditorViewModel(msSession, user, runController: null);
                var nodeVm = vm.Nodes.FirstOrDefault(n => ReferenceEquals(n.UnderlyingNode, parameterNode));
                Assert.IsNotNull(nodeVm);
                // Set the UX to invarient culture to ensure the context menu is consistent across locales.
                System.Threading.Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;
                Session.Dispatch(() =>
                {
                    var canvas = new ModelSystemCanvas { DataContext = vm };
                    var showMenu = typeof(ModelSystemCanvas).GetMethod(
                        "ShowContextMenu",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.IsNotNull(showMenu);

                    showMenu!.Invoke(canvas, new object?[] { nodeVm!, null });

                    var menu = canvas.ContextMenu;
                    Assert.IsNotNull(menu);

                    var fileMenu = menu!.Items.OfType<MenuItem>().FirstOrDefault(item =>
                        string.Equals(item.Header?.ToString(), "File", System.StringComparison.Ordinal));
                    Assert.IsNotNull(fileMenu);

                    var fileItemHeaders = fileMenu!.Items.OfType<MenuItem>()
                        .Select(item => item.Header?.ToString())
                        .ToArray();

                    CollectionAssert.Contains(fileItemHeaders, "Open");
                    CollectionAssert.Contains(fileItemHeaders, "Set File…");
                    CollectionAssert.Contains(fileItemHeaders, "Set Directory…");
                }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            });
    }

    [TestMethod]
    public void ModelSystemCanvas_NameEdit_IsCanceledWhenEditedNodeIsDeleted()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(ModelSystemCanvas_NameEdit_IsCanceledWhenEditedNodeIsDeleted),
            (user, projectSession, msSession) =>
            {
                var boundary = msSession.ModelSystem.GlobalBoundary;
                Assert.IsTrue(msSession.AddNode(
                    user,
                    boundary,
                    "EditableNode",
                    typeof(SimpleGuiTestModule),
                    new Rectangle(20, 20, 160, 60),
                    out var node,
                    out var error),
                    error?.Message);
                Assert.IsNotNull(node);

                using var vm = new ModelSystemEditorViewModel(msSession, user, runController: null);
                var nodeVm = vm.Nodes.FirstOrDefault(n => ReferenceEquals(n.UnderlyingNode, node));
                Assert.IsNotNull(nodeVm);

                Session.Dispatch(() =>
                {
                    var canvas = new ModelSystemCanvas
                    {
                        DataContext = vm
                    };

                    var beginNameEdit = typeof(ModelSystemCanvas).GetMethod(
                        "BeginNameEdit",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.IsNotNull(beginNameEdit);
                    beginNameEdit!.Invoke(canvas, new object[] { nodeVm! });

                    var nameEditorField = typeof(ModelSystemCanvas).GetField(
                        "_nameEditor",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.IsNotNull(nameEditorField);
                    var nameEditor = (TextBox?)nameEditorField!.GetValue(canvas);
                    Assert.IsNotNull(nameEditor);
                    Assert.IsTrue(nameEditor!.IsVisible, "Name editor should be visible before deletion.");

                    nameEditor.Text = "ShouldNotCommit";

                    vm.DeleteMultipleAsync(new ICanvasElement[] { nodeVm! })
                        .GetAwaiter().GetResult();

                    var editingNameElementField = typeof(ModelSystemCanvas).GetField(
                        "_editingNameElement",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.IsNotNull(editingNameElementField);
                    Assert.IsNull(editingNameElementField!.GetValue(canvas),
                        "Active name edit should be canceled when the edited element is deleted.");
                    Assert.IsFalse(nameEditor.IsVisible, "Name editor should be hidden after cancellation.");

                    Assert.IsEmpty(boundary.Modules,
                        "The edited node should be deleted from the model.");
                }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            });
    }
}
