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
using Avalonia.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.GUI.Controls;
using XTMF2.GUI.Tests.Modules;
using XTMF2.GUI.Tutorials;
using XTMF2.GUI.ViewModels;
using XTMF2.Editing;
using XTMF2.ModelSystemConstruct;
using XTMF2.RuntimeModules;
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
    public void CommandPaletteBackgroundActionExecutesContextMenuHandler()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(CommandPaletteBackgroundActionExecutesContextMenuHandler),
            (user, projectSession, msSession) =>
            {
                using var vm = new ModelSystemEditorViewModel(msSession, user, runController: null);

                Session.Dispatch(() =>
                {
                    var canvas = new ModelSystemCanvas { DataContext = vm };
                    canvas.Measure(new Avalonia.Size(800, 600));
                    canvas.Arrange(new Avalonia.Rect(0, 0, 800, 600));

                    var actions = canvas.GetCommandPaletteActions();
                    var showGhostLines = actions.Single(action =>
                        action.DisplayName == "Show Ghost Correspondence Lines");
                    var initialGhostLineVisibility = vm.ShowGhostCorrespondenceLines;
                    showGhostLines.Execute();
                    Assert.AreNotEqual(initialGhostLineVisibility, vm.ShowGhostCorrespondenceLines);

                    var addStart = actions.Single(action => action.DisplayName == "Add Start…");
                    addStart.Execute();

                    Assert.HasCount(1, vm.Starts);
                }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            });
    }

    [TestMethod]
    public void CommandPaletteIncludesSelectedNodeAndHookContextActions()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(CommandPaletteIncludesSelectedNodeAndHookContextActions),
            (user, projectSession, msSession) =>
            {
                Assert.IsTrue(msSession.AddNode(
                    user,
                    msSession.ModelSystem.GlobalBoundary,
                    "LinkedNode",
                    typeof(LinkedGuiTestModule),
                    new Rectangle(0, 0, 120, 50),
                    out var node,
                    out var error), error?.Message);

                using var vm = new ModelSystemEditorViewModel(msSession, user, runController: null);
                var nodeViewModel = vm.Nodes.Single(candidate => candidate.UnderlyingNode == node);
                nodeViewModel.ShowHooks = true;
                vm.SelectedElement = nodeViewModel;

                Session.Dispatch(() =>
                {
                    var canvas = new ModelSystemCanvas { DataContext = vm };
                    canvas.Measure(new Avalonia.Size(800, 600));
                    canvas.Arrange(new Avalonia.Rect(0, 0, 800, 600));

                    var actions = canvas.GetCommandPaletteActions();
                    Assert.IsTrue(actions.Any(action => action.DisplayName == "Delete"));
                    Assert.IsTrue(actions.Any(action => action.DisplayName == "Add Start…"));
                    Assert.IsTrue(actions.Any(action => action.DisplayName == "Add Comment"));
                    Assert.IsTrue(actions.Any(action => action.DisplayName == "Add Module…"));
                    Assert.IsTrue(actions.Any(action => action.DisplayName.StartsWith(
                        "Hook LinkedNode.Child /", StringComparison.Ordinal)),
                        string.Join(" | ", actions.Select(action => action.DisplayName)));
                }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            });
    }

    [TestMethod]
    public void TutorialGuideAdvancesAfterRealEditingActions()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(TutorialGuideAdvancesAfterRealEditingActions),
            (user, projectSession, msSession) =>
            {
                using var vm = new ModelSystemEditorViewModel(msSession, user, runController: null);

                Session.Dispatch(() =>
                {
                    Assert.IsFalse(vm.IsTutorialGuideVisible);
                    var completionCount = 0;
                    vm.StartTutorial(
                        new GettingStartedTutorialProvider().GetTutorials().Single(),
                        _ => completionCount++);
                    var tutorial = vm.TutorialGuide!;
                    Assert.IsTrue(vm.IsTutorialGuideVisible);
                    Assert.AreEqual("Welcome to XTMF2", tutorial.Title);
                    Assert.IsTrue(tutorial.CanShowWhyThisMatters);
                    Assert.IsTrue(tutorial.IsWhyThisMattersVisible);
                    Assert.IsFalse(tutorial.CanRevealWhyThisMatters);
                    tutorial.NextCommand.Execute(null);
                    Assert.AreEqual("Add a Start", tutorial.Title);
                    Assert.IsTrue(tutorial.IsWhyThisMattersVisible);

                    vm.AddStartAt(20, 20);
                    Assert.IsTrue(tutorial.CanAdvance);

                    tutorial.NextCommand.Execute(null);
                    Assert.AreEqual("Add a module node", tutorial.Title);
                    Assert.IsFalse(tutorial.CanAdvance);
                    Assert.IsTrue(msSession.AddNodeGenerateParameters(
                        user,
                        msSession.ModelSystem.GlobalBoundary,
                        "Sleep",
                        typeof(Sleep),
                        new Rectangle(20, 20, 120, 50),
                        out _,
                        out _,
                        out var error), error?.Message);
                    Assert.IsTrue(tutorial.CanAdvance);

                    tutorial.NextCommand.Execute(null);
                    Assert.AreEqual("Connect Start to the node", tutorial.Title);
                    Assert.IsFalse(tutorial.CanAdvance);
                    var start = msSession.ModelSystem.GlobalBoundary.Starts.Single();
                    var toExecute = start.Hooks!.Single(hook => hook.Name == "ToExecute");
                    Assert.IsTrue(msSession.AddLink(
                        user,
                        start,
                        toExecute,
                        msSession.ModelSystem.GlobalBoundary.Modules.Single(node => node.Type == typeof(Sleep)),
                        out _,
                        out error), error?.Message);
                    Assert.IsTrue(tutorial.CanAdvance);

                    tutorial.NextCommand.Execute(null);
                    Assert.AreEqual("Add a comment", tutorial.Title);
                    Assert.IsTrue(msSession.AddCommentBlock(
                        user,
                        msSession.ModelSystem.GlobalBoundary,
                        "Tutorial comment",
                        new Rectangle(20, 100, 160, 80),
                        out _,
                        out error), error?.Message);
                    Assert.IsTrue(tutorial.CanAdvance);

                    tutorial.NextCommand.Execute(null);
                    Assert.AreEqual("Save your model system", tutorial.Title);
                    Assert.AreEqual(TutorialTargetIds.SaveButton, tutorial.TargetId);
                    Assert.IsTrue(msSession.Save(out error), error?.Message);
                    Assert.IsTrue(tutorial.CanAdvance);
                    Assert.AreEqual(0, completionCount);

                    tutorial.NextCommand.Execute(null);
                    Assert.AreEqual("Run your model system", tutorial.Title);
                    Assert.AreEqual(TutorialTargetIds.RunButton, tutorial.TargetId);
                    Assert.IsFalse(tutorial.CanAdvance);
                    vm.NotifyModelSystemRunCompleted(msSession, succeeded: false);
                    Assert.IsFalse(tutorial.CanAdvance);
                    vm.NotifyModelSystemRunCompleted(msSession, succeeded: true);
                    Assert.IsTrue(tutorial.CanAdvance);
                    Assert.AreEqual(0, completionCount);

                    tutorial.NextCommand.Execute(null);
                    Assert.AreEqual("Getting Started complete", tutorial.Title);
                    Assert.IsNull(tutorial.TargetId);
                    Assert.AreEqual(1, completionCount);
                    tutorial.NextCommand.Execute(null);
                    Assert.AreEqual(1, completionCount);
                    Assert.IsFalse(vm.IsTutorialGuideVisible);
                }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            });
    }

    [TestMethod]
    public void TutorialGuideDoesNotOfferSkipWithoutStateAction()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(TutorialGuideDoesNotOfferSkipWithoutStateAction),
            (user, projectSession, msSession) =>
            {
                using var vm = new ModelSystemEditorViewModel(msSession, user, runController: null);
                var completionCount = 0;
                var tutorialDefinition = new TutorialDefinition(
                    "tests.skipped-tutorial",
                    "Skipped tutorial",
                    "Test that skipping does not count as completion.",
                    [
                        new TutorialStepDefinition(
                            "intro",
                            "Intro",
                            "Begin the lesson.",
                            new TutorialCompletionCondition(TutorialCompletionKinds.Manual)),
                        new TutorialStepDefinition(
                            "lesson",
                            "Lesson",
                            "Complete the lesson.",
                            new TutorialCompletionCondition(TutorialCompletionKinds.Manual))
                    ]);

                Session.Dispatch(() =>
                {
                    vm.StartTutorial(tutorialDefinition, _ => completionCount++);
                    var tutorial = vm.TutorialGuide!;
                    Assert.IsFalse(tutorial.CanSkip);
                    tutorial.SkipCommand.Execute(null);

                    Assert.AreEqual(0, completionCount);
                    Assert.AreEqual("Intro", tutorial.Title);
                }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            });
    }

    [TestMethod]
    public void TutorialGuideSkipAppliesStateActionAndAdvancesOneStep()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(TutorialGuideSkipAppliesStateActionAndAdvancesOneStep),
            (user, projectSession, msSession) =>
            {
                using var vm = new ModelSystemEditorViewModel(msSession, user, runController: null);
                var tutorialDefinition = new GettingStartedTutorialProvider().GetTutorials().Single();

                Session.Dispatch(() =>
                {
                    vm.StartTutorial(tutorialDefinition);
                    var tutorial = vm.TutorialGuide!;
                    tutorial.NextCommand.Execute(null);
                    Assert.AreEqual("Add a Start", tutorial.Title);
                    Assert.IsTrue(tutorial.CanSkip);
                    tutorial.SkipCommand.Execute(null);

                    Assert.AreEqual("Add a module node", tutorial.Title);
                    Assert.IsTrue(tutorial.CanSkip);
                    tutorial.SkipCommand.Execute(null);

                    Assert.AreEqual("Connect Start to the node", tutorial.Title);
                    Assert.IsTrue(tutorial.CanSkip);
                    tutorial.SkipCommand.Execute(null);

                    Assert.AreEqual("Add a comment", tutorial.Title);
                    Assert.IsTrue(tutorial.CanSkip);
                    tutorial.SkipCommand.Execute(null);

                    Assert.AreEqual("Save your model system", tutorial.Title);
                    Assert.IsTrue(tutorial.CanSkip);
                    tutorial.SkipCommand.Execute(null);

                    Assert.AreEqual("Run your model system", tutorial.Title);
                    Assert.IsFalse(tutorial.CanSkip);
                    Assert.AreEqual(
                        "Start 1",
                        msSession.ModelSystem.GlobalBoundary.Starts.Single().Name);
                    Assert.IsGreaterThanOrEqualTo(
                        420f,
                        msSession.ModelSystem.GlobalBoundary.Starts.Single().Location.X);
                    Assert.AreEqual(
                        typeof(Sleep),
                        msSession.ModelSystem.GlobalBoundary.Modules.Single(node => node.Type == typeof(Sleep)).Type);
                    Assert.IsGreaterThanOrEqualTo(
                        420f,
                        msSession.ModelSystem.GlobalBoundary.Modules.Single(node => node.Type == typeof(Sleep)).Location.X);
                    var executionLink = (SingleLink)msSession.ModelSystem.GlobalBoundary.Links
                        .Single(link => link.OriginHook.Name == "ToExecute");
                    Assert.AreEqual(typeof(Sleep), executionLink.Destination.Type);
                    Assert.AreEqual(
                        "Model behavior",
                        msSession.ModelSystem.GlobalBoundary.CommentBlocks.Single().Header);
                    Assert.HasCount(1, vm.Starts);
                    Assert.IsTrue(vm.Nodes.Any(node => node.UnderlyingNode.Type == typeof(Sleep)));
                    Assert.HasCount(1, vm.Links.Where(link => link.UnderlyingLink.OriginHook.Name == "ToExecute"));
                    Assert.HasCount(1, vm.CommentBlocks);
                    Assert.IsFalse(msSession.IsDirty);
                }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            });
    }

    [TestMethod]
    public void LogTutorialSkipPreparesThePipelineBeforeMessageConfiguration()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(LogTutorialSkipPreparesThePipelineBeforeMessageConfiguration),
            (user, projectSession, msSession) =>
            {
                using var vm = new ModelSystemEditorViewModel(msSession, user, runController: null);
                var tutorialDefinition = new BuiltInRuntimeModuleTutorialProvider()
                    .GetTutorials()
                    .Single(tutorial => tutorial.Order == 2);

                Session.Dispatch(() =>
                {
                    vm.StartTutorial(tutorialDefinition);
                    var tutorial = vm.TutorialGuide!;
                    tutorial.NextCommand.Execute(null);

                    string[] skippableStepTitles =
                    [
                        "Add a Start",
                        "Add a run-status stream",
                        "Add a Log",
                        "Choose where Log writes",
                        "Add the first message action",
                        "Connect the first action to Log",
                        "Start the message sequence"
                    ];
                    foreach (var title in skippableStepTitles)
                    {
                        Assert.AreEqual(title, tutorial.Title);
                        Assert.IsTrue(tutorial.CanSkip);
                        tutorial.SkipCommand.Execute(null);
                    }

                    Assert.AreEqual("Set the first message", tutorial.Title);
                    Assert.IsFalse(tutorial.CanSkip);

                    var boundary = msSession.ModelSystem.GlobalBoundary;
                    var start = boundary.Starts.Single();
                    var statusStream = boundary.Modules.Single(node => node.Name == "Run Status Stream");
                    var log = boundary.Modules.Single(node => node.Name == "Log");
                    var firstAction = boundary.Modules.Single(node => node.Name == "Write Message 1");
                    bool HasLink(Node origin, string hookName, Node destination)
                        => boundary.Links.Any(link =>
                            ReferenceEquals(link.Origin, origin)
                            && link.OriginHook.Name == hookName
                            && (link is SingleLink single && ReferenceEquals(single.Destination, destination)
                                || link is MultiLink multi && multi.Destinations.Contains(destination)));

                    Assert.AreEqual(typeof(OpenWriteStreamToRunStatus), statusStream.Type);
                    Assert.AreEqual(typeof(Log), log.Type);
                    Assert.AreEqual(typeof(WriteToLogA), firstAction.Type);
                    Assert.IsTrue(HasLink(log, "LogStream", statusStream));
                    Assert.IsTrue(HasLink(firstAction, "Log", log));
                    Assert.IsTrue(HasLink(start, "ToExecute", firstAction));

                    tutorial.NextCommand.Execute(null);
                    string[] secondActionStepTitles =
                    [
                        "Add a second message action",
                        "Connect the second action to Log",
                        "Run the second action next"
                    ];
                    foreach (var title in secondActionStepTitles)
                    {
                        Assert.AreEqual(title, tutorial.Title);
                        Assert.IsTrue(tutorial.CanSkip);
                        tutorial.SkipCommand.Execute(null);
                    }

                    Assert.AreEqual("Set the second message", tutorial.Title);
                    Assert.IsFalse(tutorial.CanSkip);
                    var secondAction = boundary.Modules.Single(node => node.Name == "Write Message 2");
                    Assert.AreEqual(typeof(WriteToLogA), secondAction.Type);
                    Assert.IsTrue(HasLink(secondAction, "Log", log));
                    Assert.IsTrue(HasLink(firstAction, "To Invoke", secondAction));

                    tutorial.NextCommand.Execute(null);
                    Assert.AreEqual("Save the model system", tutorial.Title);
                    Assert.IsTrue(tutorial.CanSkip);
                    tutorial.SkipCommand.Execute(null);
                    Assert.AreEqual("Run and inspect the messages", tutorial.Title);
                    Assert.IsFalse(tutorial.CanSkip);
                    Assert.IsFalse(msSession.IsDirty);
                }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            });
    }

    [TestMethod]
    public void ExecuteIterationTutorialSkipPreparesTheExecutionAndLoggingGraph()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(ExecuteIterationTutorialSkipPreparesTheExecutionAndLoggingGraph),
            (user, projectSession, msSession) =>
            {
                using var vm = new ModelSystemEditorViewModel(msSession, user, runController: null);
                var tutorialDefinition = new BuiltInRuntimeModuleTutorialProvider()
                    .GetTutorials()
                    .Single(tutorial => tutorial.Order == 3);

                Session.Dispatch(() =>
                {
                    vm.StartTutorial(tutorialDefinition);
                    var tutorial = vm.TutorialGuide!;
                    tutorial.NextCommand.Execute(null);

                    string[] setupStepTitles =
                    [
                        "Add a Start",
                        "Add Execute",
                        "Connect Start to Execute",
                        "Add a Log",
                        "Add a run-status stream",
                        "Connect Log to the run output",
                        "Add an iteration value",
                        "Connect Execute to the iteration value"
                    ];
                    foreach (var title in setupStepTitles)
                    {
                        Assert.AreEqual(title, tutorial.Title);
                        Assert.IsTrue(tutorial.CanSkip);
                        tutorial.SkipCommand.Execute(null);
                    }

                    Assert.AreEqual("Make the iteration value available", tutorial.Title);
                    Assert.IsFalse(tutorial.CanSkip);
                    var boundary = msSession.ModelSystem.GlobalBoundary;
                    var start = boundary.Starts.Single();
                    var execute = boundary.Modules.Single(node => node.Name == "Execute");
                    var log = boundary.Modules.Single(node => node.Name == "Log");
                    var statusStream = boundary.Modules.Single(node => node.Name == "Run Status Stream");
                    var currentIteration = boundary.Modules.Single(node => node.Name == "CurrentIteration");
                    bool HasLink(Node origin, string hookName, Node destination)
                        => boundary.Links.Any(link =>
                            ReferenceEquals(link.Origin, origin)
                            && link.OriginHook.Name == hookName
                            && (link is SingleLink single && ReferenceEquals(single.Destination, destination)
                                || link is MultiLink multi && multi.Destinations.Contains(destination)));

                    Assert.AreEqual(typeof(Execute), execute.Type);
                    Assert.AreEqual(typeof(Log), log.Type);
                    Assert.AreEqual(typeof(OpenWriteStreamToRunStatus), statusStream.Type);
                    Assert.AreEqual(typeof(SetableParameter<int>), currentIteration.Type);
                    Assert.IsTrue(HasLink(start, "ToExecute", execute));
                    Assert.IsTrue(HasLink(execute, "Current Iteration", currentIteration));
                    Assert.IsTrue(HasLink(log, "LogStream", statusStream));

                    Assert.IsNull(currentIteration.ParameterValue);
                    Assert.IsTrue(msSession.SetParameterValue(user, currentIteration, "0", out var error), error?.Message);
                    Assert.IsTrue(msSession.AddVariable(user, currentIteration, out error), error?.Message);
                    Assert.Contains(currentIteration, msSession.ModelSystem.Variables);
                    tutorial.NextCommand.Execute(null);
                    foreach (var title in new[]
                    {
                        "Add a logging action",
                        "Add the action to Execute",
                        "Connect the action to Log"
                    })
                    {
                        Assert.AreEqual(title, tutorial.Title);
                        Assert.IsTrue(tutorial.CanSkip);
                        tutorial.SkipCommand.Execute(null);
                    }

                    Assert.AreEqual("Script the iteration message", tutorial.Title);
                    Assert.IsFalse(tutorial.CanSkip);
                    var logAction = boundary.Modules.Single(node => node.Name == "Log Iteration");
                    Assert.AreEqual(typeof(WriteToLogA), logAction.Type);
                    Assert.IsTrue(HasLink(execute, "To Execute", logAction));
                    Assert.IsTrue(HasLink(logAction, "Log", log));

                    tutorial.NextCommand.Execute(null);
                    Assert.AreEqual("Choose the number of iterations", tutorial.Title);
                    Assert.IsFalse(tutorial.CanSkip);
                    tutorial.NextCommand.Execute(null);
                    Assert.AreEqual("Save the model system", tutorial.Title);
                    Assert.IsTrue(tutorial.CanSkip);
                }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            });
    }

    [TestMethod]
    public void FunctionTemplateTutorialTracksExtractionNavigationAndReuse()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(FunctionTemplateTutorialTracksExtractionNavigationAndReuse),
            (user, projectSession, msSession) =>
            {
                using var vm = new ModelSystemEditorViewModel(msSession, user, runController: null);
                var tutorialDefinition = new BuiltInRuntimeModuleTutorialProvider()
                    .GetTutorials()
                    .Single(tutorial => tutorial.Order == 4);

                Session.Dispatch(() =>
                {
                    Assert.IsTrue(vm.StartTutorial(tutorialDefinition));
                    var tutorial = vm.TutorialGuide!;
                    Assert.AreEqual("Reuse an Iteration Logger", tutorial.Title);
                    Assert.AreEqual("welcome", tutorialDefinition.Steps[0].Id);
                    var boundary = msSession.ModelSystem.GlobalBoundary;
                    var start = boundary.Starts.Single();
                    var execute = boundary.Modules.Single(node => node.Name == "Execute");
                    var logAction = boundary.Modules.Single(node => node.Name == "Log Iteration");
                    var log = boundary.Modules.Single(node => node.Name == "Log");
                    var statusStream = boundary.Modules.Single(node => node.Name == "Run Status Stream");
                    var currentIteration = boundary.Modules.Single(node => node.Name == "CurrentIteration");
                    var scriptedMessage = boundary.Modules.Single(node => node.Name == "Message");
                    var iterations = boundary.Modules.Single(node => node.Name == "Iterations");

                    Assert.AreEqual(typeof(Execute), execute.Type);
                    Assert.AreEqual(typeof(WriteToLogA), logAction.Type);
                    Assert.AreEqual(typeof(SetableParameter<int>), currentIteration.Type);
                    Assert.AreEqual(typeof(ScriptedParameter<string>), scriptedMessage.Type);
                    Assert.AreEqual(typeof(OpenWriteStreamToRunStatus), statusStream.Type);
                    Assert.IsNotNull(currentIteration.ParameterValue);
                    Assert.IsNotNull(iterations.ParameterValue);
                    Assert.Contains(currentIteration, msSession.ModelSystem.Variables);
                    Assert.IsEmpty(boundary.FunctionTemplates);
                    Assert.IsEmpty(boundary.FunctionInstances);
                    Assert.IsTrue(boundary.Links.Any(link =>
                        ReferenceEquals(link.Origin, start)
                        && link.OriginHook.Name == "ToExecute"
                        && link is SingleLink startLink
                        && ReferenceEquals(startLink.Destination, execute)));
                    Assert.IsTrue(boundary.Links.Any(link =>
                        ReferenceEquals(link.Origin, execute)
                        && link.OriginHook.Name == "To Execute"
                        && (link is SingleLink executeLink
                                && ReferenceEquals(executeLink.Destination, logAction)
                            || link is MultiLink executeMultiLink
                                && executeMultiLink.Destinations.Contains(logAction))));
                    Assert.IsTrue(boundary.Links.Any(link =>
                        ReferenceEquals(link.Origin, execute)
                        && link.OriginHook.Name == "Current Iteration"
                        && link is SingleLink iterationLink
                        && ReferenceEquals(iterationLink.Destination, currentIteration)));
                    Assert.IsTrue(boundary.Links.Any(link =>
                        ReferenceEquals(link.Origin, logAction)
                        && link.OriginHook.Name == "Log"
                        && link is SingleLink logLink
                        && ReferenceEquals(logLink.Destination, log)));
                    Assert.IsTrue(boundary.Links.Any(link =>
                        ReferenceEquals(link.Origin, log)
                        && link.OriginHook.Name == "LogStream"
                        && link is SingleLink streamLink
                        && ReferenceEquals(streamLink.Destination, statusStream)));

                    tutorial.NextCommand.Execute(null);
                    Assert.AreEqual("Extract the logger into a template", tutorial.Title);
                    Assert.IsFalse(tutorial.CanAdvance);

                    Assert.IsTrue(msSession.ExtractToFunctionTemplate(
                        user,
                        boundary,
                        [logAction],
                        "Iteration Logger",
                        "Iteration Logger",
                        Rectangle.Hidden,
                        Rectangle.Hidden,
                        out var template,
                        out var firstInstance,
                        out var error), error?.Message);
                    Assert.IsNotNull(template);
                    Assert.IsNotNull(firstInstance);
                    Assert.AreEqual("Log", template!.FunctionParameters.Single().Name);
                    Assert.IsTrue(tutorial.CanAdvance);
                    tutorial.NextCommand.Execute(null);

                    Assert.AreEqual("Open the instance's template", tutorial.Title);
                    Assert.IsFalse(tutorial.CanAdvance);
                    var templateViewModel = vm.FunctionTemplates.Single(candidate =>
                        candidate.UnderlyingTemplate == template);
                    vm.NavigateIntoFunctionTemplate(templateViewModel);
                    Assert.IsTrue(tutorial.CanAdvance);
                    tutorial.NextCommand.Execute(null);

                    Assert.AreEqual("Inspect the template contents", tutorial.Title);
                    Assert.IsTrue(vm.IsInsideFunctionTemplate);
                    Assert.IsTrue(vm.Nodes.Any(node => node.UnderlyingNode.Type == typeof(WriteToLogA)));
                    tutorial.NextCommand.Execute(null);

                    Assert.AreEqual("Return to the parent model", tutorial.Title);
                    vm.NavigateUpCommand.Execute(null);
                    Assert.IsFalse(vm.IsInsideFunctionTemplate);
                    Assert.IsTrue(tutorial.CanAdvance);
                    tutorial.NextCommand.Execute(null);

                    Assert.AreEqual("Add another instance", tutorial.Title);
                    Assert.IsFalse(tutorial.CanAdvance);
                    Assert.IsTrue(msSession.AddFunctionInstanceGenerateParameters(
                        user,
                        boundary,
                        template,
                        "Iteration Logger 2",
                        Rectangle.Hidden,
                        out var secondInstance,
                        out _,
                        out error), error?.Message);
                    Assert.IsNotNull(secondInstance);
                    Assert.IsTrue(tutorial.CanAdvance);
                    tutorial.NextCommand.Execute(null);

                    Assert.AreEqual("Supply the second instance's Log", tutorial.Title);
                    Assert.IsFalse(tutorial.CanAdvance);
                    Assert.IsTrue(msSession.AddLink(
                        user,
                        secondInstance!,
                        secondInstance!.Hooks.Single(hook => hook.Name == "Log"),
                        log,
                        out _,
                        out error), error?.Message);
                    Assert.IsTrue(tutorial.CanAdvance);
                    tutorial.NextCommand.Execute(null);

                    Assert.AreEqual("Run both instances in each iteration", tutorial.Title);
                    Assert.IsFalse(tutorial.CanAdvance);
                    Assert.IsTrue(msSession.AddLink(
                        user,
                        execute,
                        execute.Hooks.Single(hook => hook.Name == "To Execute"),
                        secondInstance!,
                        out _,
                        out error), error?.Message);
                    Assert.IsTrue(tutorial.CanAdvance);
                    tutorial.NextCommand.Execute(null);

                    Assert.AreEqual("Save the model system", tutorial.Title);
                    Assert.IsTrue(tutorial.CanSkip);
                    tutorial.SkipCommand.Execute(null);
                    Assert.AreEqual("Run and confirm reuse", tutorial.Title);
                    Assert.IsFalse(tutorial.CanSkip);
                }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            });
    }

    [TestMethod]
    public void TutorialGuideTracksStartAndTargetedLinkAddedForCurrentStep()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(TutorialGuideTracksStartAndTargetedLinkAddedForCurrentStep),
            (user, projectSession, msSession) =>
            {
                using var vm = new ModelSystemEditorViewModel(msSession, user, runController: null);

                Session.Dispatch(() =>
                {
                    var tutorialDefinition = new TutorialDefinition(
                        "tests.start-and-link",
                        "Start and link",
                        "Test start and link completion conditions.",
                        [
                            new TutorialStepDefinition(
                                "intro",
                                "Intro",
                                "Begin the lesson.",
                                new TutorialCompletionCondition(TutorialCompletionKinds.Manual),
                                "This explanation is optional for provider tutorials."),
                            new TutorialStepDefinition(
                                "add-start",
                                "Add Start",
                                "Add a Start node.",
                                new TutorialCompletionCondition(TutorialCompletionKinds.StartAdded)),
                            new TutorialStepDefinition(
                                "add-execute",
                                "Add Execute",
                                "Add an Execute module.",
                                new TutorialCompletionCondition(
                                    TutorialCompletionKinds.ModuleAdded,
                                    typeof(Execute).AssemblyQualifiedName)),
                            new TutorialStepDefinition(
                                "connect",
                                "Connect Start",
                                "Connect Start to Execute.",
                                new TutorialCompletionCondition(TutorialCompletionKinds.LinkAdded, "ToExecute"))
                        ]);
                    vm.StartTutorial(tutorialDefinition);
                    var guide = vm.TutorialGuide!;
                    Assert.IsTrue(guide.CanShowWhyThisMatters);
                    Assert.IsFalse(guide.IsWhyThisMattersVisible);
                    Assert.IsTrue(guide.CanRevealWhyThisMatters);
                    guide.ShowWhyThisMattersCommand.Execute(null);
                    Assert.IsTrue(guide.IsWhyThisMattersVisible);
                    Assert.IsFalse(guide.CanRevealWhyThisMatters);
                    guide.NextCommand.Execute(null);
                    Assert.IsFalse(guide.IsWhyThisMattersVisible);

                    Assert.IsFalse(guide.CanAdvance);
                    vm.AddStartAt(20, 20);
                    Assert.IsTrue(guide.CanAdvance);
                    guide.NextCommand.Execute(null);

                    Assert.IsFalse(guide.CanAdvance);
                    Assert.IsTrue(msSession.AddNode(
                        user,
                        msSession.ModelSystem.GlobalBoundary,
                        "Execute",
                        typeof(Execute),
                        new Rectangle(180, 20, 120, 50),
                        out var execute,
                        out var error), error?.Message);
                    Assert.IsTrue(guide.CanAdvance);
                    guide.NextCommand.Execute(null);

                    Assert.IsFalse(guide.CanAdvance);
                    var start = vm.Starts.Single().UnderlyingStart;
                    var toExecute = start.Hooks!.Single(hook => hook.Name == "ToExecute");
                    Assert.IsTrue(msSession.AddLink(user, start, toExecute, execute!, out _, out error), error?.Message);
                    Assert.IsTrue(guide.CanAdvance);
                }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            });
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
    public void ModelSystemCanvas_ContextMenu_ShortcutHeadersUseTwoColumnGrid()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(ModelSystemCanvas_ContextMenu_ShortcutHeadersUseTwoColumnGrid),
            (user, projectSession, msSession) =>
            {
                using var vm = new ModelSystemEditorViewModel(msSession, user, runController: null);

                Session.Dispatch(() =>
                {
                    var canvas = new ModelSystemCanvas { DataContext = vm };
                    var showMenu = typeof(ModelSystemCanvas).GetMethod(
                        "ShowContextMenu",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.IsNotNull(showMenu);

                    showMenu!.Invoke(canvas, new object?[] { null, null });

                    var menu = canvas.ContextMenu;
                    Assert.IsNotNull(menu);

                    var addModuleItem = menu!.Items.OfType<MenuItem>().FirstOrDefault(item =>
                        item.Header is Grid grid
                        && grid.Children.OfType<TextBlock>().Any(text => text.Text == "Add Module…")
                        && grid.Children.OfType<TextBlock>().Any(text => text.Text == "Ctrl+M"));

                    Assert.IsNotNull(addModuleItem);
                }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            });
    }

    [TestMethod]
    public void ModelSystemCanvas_ContextMenu_ShowsBulkLinkForOrderedMultiSelection()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(ModelSystemCanvas_ContextMenu_ShowsBulkLinkForOrderedMultiSelection),
            (user, _, session) =>
            {
                var boundary = session.ModelSystem.GlobalBoundary;
                Assert.IsTrue(session.AddNode(user, boundary, "Origin",
                    typeof(MultiLinkedGuiTestModule), new Rectangle(20, 20, 160, 60),
                    out var origin, out var originError), originError?.Message);
                Assert.IsTrue(session.AddNode(user, boundary, "Destination",
                    typeof(SimpleGuiTestModule), new Rectangle(240, 20, 160, 60),
                    out var destination, out var destinationError), destinationError?.Message);

                using var vm = new ModelSystemEditorViewModel(session, user, runController: null);
                Session.Dispatch(() =>
                {
                    var canvas = new ModelSystemCanvas { DataContext = vm };
                    var originVm = vm.Nodes.Single(node => node.UnderlyingNode == origin);
                    var destinationVm = vm.Nodes.Single(node => node.UnderlyingNode == destination);
                    var addToSelection = typeof(ModelSystemCanvas).GetMethod(
                        "AddToMultiSelection", BindingFlags.Instance | BindingFlags.NonPublic);
                    var showMenu = typeof(ModelSystemCanvas).GetMethod(
                        "ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.IsNotNull(addToSelection);
                    Assert.IsNotNull(showMenu);

                    addToSelection!.Invoke(canvas, new object[] { originVm });
                    addToSelection.Invoke(canvas, new object[] { destinationVm });
                    showMenu!.Invoke(canvas, new object?[] { originVm, null });

                    var bulkLinkItem = canvas.ContextMenu!.Items.OfType<MenuItem>().FirstOrDefault(item =>
                        item.Header is Grid grid
                        && grid.Children.OfType<TextBlock>().Any(text => text.Text == "Link to All Selected…")
                        && grid.Children.OfType<TextBlock>().Any(text => text.Text == "Ctrl+L"));
                    Assert.IsNotNull(bulkLinkItem);
                }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            });
    }

    [TestMethod]
    public void ModelSystemCanvas_ArrowKeys_DoNotNavigateWhileEditingParameterOrComment()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(ModelSystemCanvas_ArrowKeys_DoNotNavigateWhileEditingParameterOrComment),
            (user, projectSession, msSession) =>
            {
                var boundary = msSession.ModelSystem.GlobalBoundary;
                Assert.IsTrue(msSession.AddNode(
                    user,
                    boundary,
                    "ParameterNode",
                    typeof(XTMF2.RuntimeModules.BasicParameter<string>),
                    new Rectangle(20, 20, 160, 60),
                    out var parameterNode,
                    out var parameterError),
                    parameterError?.Message);
                Assert.IsNotNull(parameterNode);
                Assert.IsTrue(msSession.AddNode(
                    user,
                    boundary,
                    "RightNode",
                    typeof(SimpleGuiTestModule),
                    new Rectangle(260, 20, 160, 60),
                    out _,
                    out var rightNodeError),
                    rightNodeError?.Message);

                using var vm = new ModelSystemEditorViewModel(msSession, user, runController: null);
                vm.AddCommentBlockAt(20, 160);

                Session.Dispatch(() =>
                {
                    var canvas = new ModelSystemCanvas { DataContext = vm };
                    var beginParamEdit = typeof(ModelSystemCanvas).GetMethod(
                        "BeginParamEdit",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    var beginCommentEdit = typeof(ModelSystemCanvas).GetMethod(
                        "BeginCommentEdit",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    var onKeyDown = typeof(ModelSystemCanvas).GetMethod(
                        "OnKeyDown",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.IsNotNull(beginParamEdit);
                    Assert.IsNotNull(beginCommentEdit);
                    Assert.IsNotNull(onKeyDown);

                    var parameterVm = vm.Nodes.FirstOrDefault(n => ReferenceEquals(n.UnderlyingNode, parameterNode));
                    var commentVm = vm.CommentBlocks.FirstOrDefault();
                    Assert.IsNotNull(parameterVm);
                    Assert.IsNotNull(commentVm);

                    vm.SelectElementCommand.Execute(commentVm);
                    beginCommentEdit!.Invoke(canvas, new object[] { commentVm! });
                    onKeyDown!.Invoke(canvas, new object[] { new KeyEventArgs { Key = Key.Right } });
                    Assert.AreSame(commentVm, vm.SelectedElement);

                    vm.SelectElementCommand.Execute(parameterVm);
                    beginParamEdit!.Invoke(canvas, new object?[] { parameterVm!, -1.0, -1.0, -1.0, null, null });
                    onKeyDown.Invoke(canvas, new object[] { new KeyEventArgs { Key = Key.Right } });
                    Assert.AreSame(parameterVm, vm.SelectedElement);
                }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            });
    }

    [TestMethod]
    public void ModelSystemCanvas_ParameterTabPosition_IgnoresHiddenOptionalHooks()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(ModelSystemCanvas_ParameterTabPosition_IgnoresHiddenOptionalHooks),
            (user, projectSession, msSession) =>
            {
                var boundary = msSession.ModelSystem.GlobalBoundary;
                Assert.IsTrue(msSession.AddNode(
                    user,
                    boundary,
                    "Origin",
                    typeof(OptionalThenParameterGuiTestModule),
                    new Rectangle(20, 40, 160, 60),
                    out var origin,
                    out var originError),
                    originError?.Message);
                Assert.IsNotNull(origin);

                Assert.IsTrue(msSession.AddNode(
                    user,
                    boundary,
                    "Editable Value",
                    typeof(XTMF2.RuntimeModules.BasicParameter<string>),
                    Rectangle.Hidden,
                    out var parameter,
                    out var parameterError),
                    parameterError?.Message);
                Assert.IsNotNull(parameter);

                var editableHook = origin!.Hooks.First(h => h.Name == "Editable Value");
                Assert.IsTrue(msSession.AddLink(user, origin, editableHook, parameter!, out var link, out var linkError),
                    linkError?.Message);
                Assert.IsNotNull(link);

                using var vm = new ModelSystemEditorViewModel(msSession, user, runController: null);

                Session.Dispatch(() =>
                {
                    var canvas = new ModelSystemCanvas { DataContext = vm };
                    var buildCache = typeof(ModelSystemCanvas).GetMethod(
                        "BuildHookAnchorCache",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    var calculateRow = typeof(ModelSystemCanvas).GetMethod(
                        "CalculateParameterRowPosition",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.IsNotNull(buildCache);
                    Assert.IsNotNull(calculateRow);

                    buildCache!.Invoke(canvas, []);

                    var originVm = vm.Nodes.FirstOrDefault(n => ReferenceEquals(n.UnderlyingNode, origin));
                    Assert.IsNotNull(originVm);

                    var result = calculateRow!.Invoke(canvas, [originVm!, editableHook]);
                    Assert.IsNotNull(result);

                    var row = ((double X, double Y, double W))result!;
                    Assert.AreEqual(originVm!.Y + 28.0, row.Y, 0.0001,
                        "The editor should align to the first visible hook row, not the raw hook index after a hidden optional hook.");
                }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            });
    }

    [TestMethod]
    public void ModelSystemCanvas_AutoScrollTimer_ContinuesForPendingLinkDrag()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(ModelSystemCanvas_AutoScrollTimer_ContinuesForPendingLinkDrag),
            (user, projectSession, msSession) =>
            {
                using var vm = new ModelSystemEditorViewModel(msSession, user, runController: null);
                vm.AddStartAt(20, 20);

                Session.Dispatch(() =>
                {
                    var canvas = new ModelSystemCanvas { DataContext = vm };
                    var linkOriginField = typeof(ModelSystemCanvas).GetField(
                        "_linkOrigin",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    var timerField = typeof(ModelSystemCanvas).GetField(
                        "_autoScrollTimer",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    var autoScrollTick = typeof(ModelSystemCanvas).GetMethod(
                        "OnAutoScrollTick",
                        BindingFlags.Instance | BindingFlags.NonPublic);

                    Assert.IsNotNull(linkOriginField);
                    Assert.IsNotNull(timerField);
                    Assert.IsNotNull(autoScrollTick);

                    linkOriginField!.SetValue(canvas, vm.Starts[0]);
                    var timer = (DispatcherTimer)timerField!.GetValue(canvas)!;
                    timer.Start();

                    autoScrollTick!.Invoke(canvas, new object?[] { null, EventArgs.Empty });

                    Assert.IsTrue(timer.IsEnabled);
                    timer.Stop();
                }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            });
    }

    [TestMethod]
    public void ModelSystemCanvas_HitTestsVisibleHookRowsBeyondStoredNodeHeight()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(ModelSystemCanvas_HitTestsVisibleHookRowsBeyondStoredNodeHeight),
            (user, _, msSession) =>
            {
                Assert.IsTrue(msSession.AddNode(
                    user,
                    msSession.ModelSystem.GlobalBoundary,
                    "NodeWithHooks",
                    typeof(OptionalThenParameterGuiTestModule),
                    new Rectangle(100, 100, 120, 50),
                    out var node,
                    out var error), error?.Message);
                Assert.IsNotNull(node);

                using var vm = new ModelSystemEditorViewModel(msSession, user, runController: null);
                var nodeVm = vm.Nodes.Single(candidate => ReferenceEquals(candidate.UnderlyingNode, node));
                nodeVm.ShowHooks = true;

                Session.Dispatch(() =>
                {
                    var canvas = new ModelSystemCanvas { DataContext = vm };
                    var buildCache = typeof(ModelSystemCanvas).GetMethod(
                        "BuildHookAnchorCache",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    var hitTest = typeof(ModelSystemCanvas).GetMethod(
                        "HitTest",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.IsNotNull(buildCache);
                    Assert.IsNotNull(hitTest);
                    buildCache!.Invoke(canvas, null);

                    Assert.IsGreaterThanOrEqualTo(node!.Hooks.Count, 2,
                        "The test node must have multiple visible hook rows.");
                    var hit = hitTest!.Invoke(canvas, [new Avalonia.Point(120, 154), false]);
                    Assert.AreSame(nodeVm, hit,
                        "A visible hook row below the stored node height must still hit its node.");
                }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            });
    }

    [TestMethod]
    public void ModelSystemCanvas_AutoScrollTimer_ContinuesForRubberBandSelection()
    {
        Session.Dispatch(() =>
        {
            var canvas = new ModelSystemCanvas();
            var selectionStartField = typeof(ModelSystemCanvas).GetField(
                "_selRectStart",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var timerField = typeof(ModelSystemCanvas).GetField(
                "_autoScrollTimer",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var autoScrollTick = typeof(ModelSystemCanvas).GetMethod(
                "OnAutoScrollTick",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(selectionStartField);
            Assert.IsNotNull(timerField);
            Assert.IsNotNull(autoScrollTick);

            selectionStartField!.SetValue(canvas, new Avalonia.Point(20, 20));
            var timer = (DispatcherTimer)timerField!.GetValue(canvas)!;
            timer.Start();

            autoScrollTick!.Invoke(canvas, new object?[] { null, EventArgs.Empty });

            Assert.IsTrue(timer.IsEnabled);
            timer.Stop();
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
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

    [TestMethod]
    public void ModelSystemCanvas_GhostLinkUsesReplicatedHookAnchor()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(ModelSystemCanvas_GhostLinkUsesReplicatedHookAnchor),
            (user, _, msSession) =>
            {
                var boundary = msSession.ModelSystem.GlobalBoundary;
                Assert.IsTrue(msSession.AddNode(user, boundary, "Origin",
                    typeof(LinkedGuiTestModule), new Rectangle(20, 20, 160, 60),
                    out var origin, out var nodeError), nodeError?.Message);
                Assert.IsNotNull(origin);
                GhostNode? ghost = null;
                Assert.IsTrue(msSession.AddGhostNode(user, boundary, origin!,
                    new Rectangle(300, 100, 160, 60), out ghost, out var ghostError), ghostError?.Message);
                Assert.IsNotNull(ghost);

                using var vm = new ModelSystemEditorViewModel(msSession, user, runController: null);
                var ghostVm = vm.GhostNodes.Single();
                var hook = ghostVm.Hooks.First(h => h.Name == "Child");

                Session.Dispatch(() =>
                {
                    var canvas = new ModelSystemCanvas { DataContext = vm };
                    var ghostHookAnchor = typeof(ModelSystemCanvas).GetMethod(
                        "GhostHookAnchor", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.IsNotNull(ghostHookAnchor);

                    var anchor = (Avalonia.Point)ghostHookAnchor!.Invoke(canvas,
                        new object[] { ghostVm, hook, new Avalonia.Point(500, 130) })!;
                    Assert.AreEqual(ghostVm.X + ghostVm.Width, anchor.X, 1e-9);
                    Assert.AreEqual(ghostVm.Y + 28.0 + 8.0, anchor.Y, 1e-9);

                    var leftAnchor = (Avalonia.Point)ghostHookAnchor.Invoke(canvas,
                        new object[] { ghostVm, hook, new Avalonia.Point(100, 130) })!;
                    Assert.AreEqual(ghostVm.X, leftAnchor.X, 1e-9);
                    Assert.AreEqual(anchor.Y, leftAnchor.Y, 1e-9);
                }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            });
    }

    [TestMethod]
    public void ModelSystemCanvas_OrthogonalBreakpointSelectsOriginHookSide()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(ModelSystemCanvas_OrthogonalBreakpointSelectsOriginHookSide),
            (user, _, msSession) =>
            {
                var boundary = msSession.ModelSystem.GlobalBoundary;
                Assert.IsTrue(msSession.AddNode(user, boundary, "Origin",
                    typeof(LinkedGuiTestModule), new Rectangle(200, 20, 160, 60),
                    out var origin, out var nodeError), nodeError?.Message);
                Assert.IsNotNull(origin);
                Assert.IsTrue(msSession.AddNode(user, boundary, "Destination",
                    typeof(SimpleGuiTestModule), new Rectangle(500, 100, 160, 60),
                    out var destination, out var destinationError), destinationError?.Message);
                Assert.IsNotNull(destination);

                var hook = origin!.Hooks.First(h => h.Name == "Child");
                Assert.IsTrue(msSession.AddLink(user, origin, hook, destination!,
                    out var link, out var linkError), linkError?.Message);
                Assert.IsNotNull(link);
                Assert.IsTrue(msSession.SetLinkOrthogonal(user, link!, true, out var orthogonalError),
                    orthogonalError?.Message);

                using var vm = new ModelSystemEditorViewModel(msSession, user, runController: null);
                var originVm = vm.Nodes.Single(node => ReferenceEquals(node.UnderlyingNode, origin));
                var linkVm = vm.Links.Single(lvm => ReferenceEquals(lvm.UnderlyingLink, link));

                Session.Dispatch(() =>
                {
                    var canvas = new ModelSystemCanvas { DataContext = vm };
                    var buildCache = typeof(ModelSystemCanvas).GetMethod(
                        "BuildHookAnchorCache", BindingFlags.Instance | BindingFlags.NonPublic);
                    var computeOrigin = typeof(ModelSystemCanvas).GetMethod(
                        "ComputeOrthogonalOriginPoint", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.IsNotNull(buildCache);
                    Assert.IsNotNull(computeOrigin);
                    buildCache!.Invoke(canvas, null);

                    Assert.IsTrue(msSession.SetLinkOrthogonalBreakpointX(user, link!, 100,
                        out var leftError), leftError?.Message);
                    var leftPoint = (Avalonia.Point)computeOrigin!.Invoke(canvas, new object[] { linkVm })!;
                    Assert.AreEqual(originVm.X, leftPoint.X, 1e-9);

                    Assert.IsTrue(msSession.SetLinkOrthogonalBreakpointX(user, link!, 500,
                        out var rightError), rightError?.Message);
                    var rightPoint = (Avalonia.Point)computeOrigin.Invoke(canvas, new object[] { linkVm })!;
                    Assert.AreEqual(originVm.X + originVm.Width, rightPoint.X, 1e-9);
                }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            });
    }
}
