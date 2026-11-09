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
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2;
using XTMF2.GUI.Tutorials;
using XTMF2.RuntimeModules;

namespace XTMF2.GUI.Tests.Tutorials;

[TestClass]
public class TutorialCatalogTests
{
    [TestMethod]
    public void FromModuleAssembliesLoadsTutorialProviderDefinitions()
    {
        var catalog = TutorialCatalog.FromModuleAssemblies(
            [typeof(TestLibraryTutorialProvider).Assembly]);

        Assert.AreEqual(6, catalog.Tutorials.Count(tutorial =>
            !tutorial.Id.StartsWith("tests.", StringComparison.Ordinal)));
        Assert.IsTrue(catalog.Tutorials.Take(6).All(tutorial =>
            !tutorial.Id.StartsWith("tests.", StringComparison.Ordinal)));
        Assert.IsTrue(catalog.Tutorials.Skip(6).All(tutorial =>
            tutorial.Id.StartsWith("tests.", StringComparison.Ordinal)));
        CollectionAssert.AreEqual(
            new[]
            {
                "xtmf2.getting-started",
                "xtmf2.runtime.log-messages",
                "xtmf2.runtime.execute-actions",
                "xtmf2.runtime.function-templates",
                "xtmf2.runtime.parameter-estimation",
                "xtmf2.runtime.calibration"
            },
            catalog.Entries.Take(6).Select(entry => entry.Tutorial.Id).ToArray());
        CollectionAssert.AreEqual(
            new[] { 1, 2, 3, 4, 5, 6 },
            catalog.Entries.Take(6).Select(entry => entry.SequenceNumber).ToArray());
        CollectionAssert.AreEqual(
            new[] { "tests.library-tutorial", "tests.another-library-tutorial", "tests.unordered-library-tutorial" },
            catalog.Entries.Skip(6).Select(entry => entry.Tutorial.Id).ToArray());
        CollectionAssert.AreEqual(
            new[] { 1, 2, 3 },
            catalog.Entries.Skip(6).Select(entry => entry.SequenceNumber).ToArray());
        Assert.AreEqual(
            catalog.Entries[6].LibraryName,
            catalog.Entries[8].LibraryName);
        Assert.IsTrue(catalog.Tutorials.Any(tutorial => tutorial.Id == "xtmf2.getting-started"));
        Assert.IsTrue(catalog.Tutorials.Any(tutorial => tutorial.Id == "xtmf2.runtime.log-messages"));
        Assert.IsTrue(catalog.Tutorials.Any(tutorial => tutorial.Id == "xtmf2.runtime.execute-actions"));
        Assert.IsTrue(catalog.Tutorials.Any(tutorial => tutorial.Id == "xtmf2.runtime.function-templates"));
        Assert.IsTrue(catalog.Tutorials.Any(tutorial => tutorial.Id == "xtmf2.runtime.parameter-estimation"));
        Assert.IsTrue(catalog.Tutorials.Any(tutorial => tutorial.Id == "xtmf2.runtime.calibration"));
        Assert.IsTrue(catalog.Tutorials.Any(tutorial => tutorial.Id == "tests.library-tutorial"));
        Assert.IsTrue(catalog.Tutorials.Any(tutorial => tutorial.Id == "tests.another-library-tutorial"));
        Assert.IsTrue(catalog.Tutorials.Any(tutorial => tutorial.Id == "tests.unordered-library-tutorial"));
    }

    [TestMethod]
    public void CalibrationTutorialCoversSetupRunAndApplyingResults()
    {
        var tutorial = new BuiltInRuntimeModuleTutorialProvider()
            .GetTutorials()
            .Single(candidate => candidate.Order == 6);

        Assert.AreEqual("xtmf2.runtime.calibration", tutorial.Id);
        Assert.AreEqual("Calibrate a Model Parameter", tutorial.Title);
        Assert.AreEqual(TutorialCompletionKinds.CalibrationParameterAdded,
            tutorial.Steps.Single(step => step.Id == "add-calibration-parameter").Completion.Kind);
        Assert.AreEqual(TutorialTargetIds.CalibrationSettingsButton,
            tutorial.Steps.Single(step => step.Id == "configure-calibration").TargetId);
        Assert.AreEqual(TutorialCompletionKinds.CalibrationConfigured,
            tutorial.Steps.Single(step => step.Id == "configure-calibration").Completion.Kind);
        Assert.AreEqual(TutorialTargetIds.CalibrationRunButton,
            tutorial.Steps.Single(step => step.Id == "run-calibration").TargetId);
        Assert.AreEqual(TutorialCompletionKinds.CalibrationRunCompleted,
            tutorial.Steps.Single(step => step.Id == "run-calibration").Completion.Kind);
        Assert.AreEqual(TutorialCompletionKinds.CalibrationParameterApplied,
            tutorial.Steps.Single(step => step.Id == "apply-calibration-result").Completion.Kind);
    }

    [TestMethod]
    public void RuntimeLogTutorialBuildsTwoMessagesAndRequiresSuccessfulRun()
    {
        var tutorial = new BuiltInRuntimeModuleTutorialProvider()
            .GetTutorials()
            .Single(candidate => candidate.Order == 2);

        Assert.AreEqual("xtmf2.runtime.log-messages", tutorial.Id);
        Assert.AreEqual("Write Messages to the Run Log", tutorial.Title);
        Assert.IsTrue(tutorial.Steps.Any(step =>
            step.Completion.Kind == TutorialCompletionKinds.ModuleAdded
            && step.Completion.Target == typeof(OpenWriteStreamToRunStatus).AssemblyQualifiedName));
        Assert.IsTrue(tutorial.Steps.Any(step =>
            step.Completion.Kind == TutorialCompletionKinds.ModuleAdded
            && step.Completion.Target == typeof(Log).AssemblyQualifiedName));
        Assert.AreEqual(
            2,
            tutorial.Steps.Count(step =>
                step.Completion.Kind == TutorialCompletionKinds.ModuleAdded
                && step.Completion.Target == typeof(WriteToLogA).AssemblyQualifiedName));
        Assert.IsTrue(tutorial.Steps
            .Where(step => step.Id.StartsWith("configure-", StringComparison.Ordinal))
            .All(step => step.Completion.Kind == TutorialCompletionKinds.Manual && step.SkipAction is null));
        Assert.AreEqual(TutorialTargetIds.SaveButton, tutorial.Steps.Single(step => step.Id == "save").TargetId);
        Assert.AreEqual(TutorialTargetIds.RunButton, tutorial.Steps[^1].TargetId);
        Assert.AreEqual(
            TutorialCompletionKinds.ModelSystemRunCompleted,
            tutorial.Steps[^1].Completion.Kind);
        Assert.IsNull(tutorial.Steps[^1].SkipAction);
    }

    [TestMethod]
    public void ExecuteIterationTutorialScriptsAndLogsTheCurrentIteration()
    {
        var tutorial = new BuiltInRuntimeModuleTutorialProvider()
            .GetTutorials()
            .Single(candidate => candidate.Order == 3);

        Assert.AreEqual("xtmf2.runtime.execute-actions", tutorial.Id);
        Assert.AreEqual("Log Each Execute Iteration", tutorial.Title);
        Assert.IsTrue(tutorial.Steps.Any(step =>
            step.Completion.Kind == TutorialCompletionKinds.ModuleAdded
            && step.Completion.Target == typeof(Execute).AssemblyQualifiedName));
        Assert.IsTrue(tutorial.Steps.Any(step =>
            step.Completion.Kind == TutorialCompletionKinds.ModuleAdded
            && step.Completion.Target == typeof(WriteToLogA).AssemblyQualifiedName));

        var iterationValueStep = tutorial.Steps.Single(step => step.Id == "add-current-iteration");
        Assert.AreEqual(TutorialCompletionKinds.ModuleAdded, iterationValueStep.Completion.Kind);
        Assert.AreEqual(typeof(SetableParameter<int>).AssemblyQualifiedName, iterationValueStep.Completion.Target);
        Assert.IsNotNull(iterationValueStep.SkipAction);

        var iterationConnectionStep = tutorial.Steps.Single(step => step.Id == "connect-current-iteration");
        Assert.AreEqual(TutorialCompletionKinds.LinkAdded, iterationConnectionStep.Completion.Kind);
        Assert.AreEqual("Current Iteration", iterationConnectionStep.Completion.Target);
        Assert.IsNotNull(iterationConnectionStep.SkipAction);

        var variableStep = tutorial.Steps.Single(step => step.Id == "make-iteration-variable");
        Assert.AreEqual(TutorialCompletionKinds.Manual, variableStep.Completion.Kind);
        Assert.IsNull(variableStep.SkipAction);

        var scriptStep = tutorial.Steps.Single(step => step.Id == "script-message");
        Assert.AreEqual(TutorialCompletionKinds.Manual, scriptStep.Completion.Kind);
        Assert.IsNull(scriptStep.SkipAction);
        StringAssert.Contains(scriptStep.Instructions, "\"Iteration: \" + CurrentIteration");

        var iterationCountStep = tutorial.Steps.Single(step => step.Id == "configure-iterations");
        Assert.AreEqual(TutorialCompletionKinds.Manual, iterationCountStep.Completion.Kind);
        Assert.IsNull(iterationCountStep.SkipAction);
        Assert.AreEqual(TutorialTargetIds.SaveButton, tutorial.Steps.Single(step => step.Id == "save").TargetId);
        Assert.AreEqual(TutorialTargetIds.RunButton, tutorial.Steps[^1].TargetId);
        Assert.AreEqual(
            TutorialCompletionKinds.ModelSystemRunCompleted,
            tutorial.Steps[^1].Completion.Kind);
        Assert.IsNull(tutorial.Steps[^1].SkipAction);
    }

    [TestMethod]
    public void FunctionTemplateTutorialExtractsAndReusesTheIterationLogger()
    {
        var tutorial = new BuiltInRuntimeModuleTutorialProvider()
            .GetTutorials()
            .Single(candidate => candidate.Order == 4);

        Assert.AreEqual("xtmf2.runtime.function-templates", tutorial.Id);
        Assert.AreEqual("Reuse an Iteration Logger", tutorial.Title);
        Assert.IsNotNull(tutorial.InitializeAction);
        var extractionStep = tutorial.Steps.Single(step => step.Id == "extract-function-template");
        Assert.AreEqual(TutorialCompletionKinds.FunctionTemplateAdded, extractionStep.Completion.Kind);
        Assert.AreEqual("Iteration Logger", extractionStep.Completion.Target);
        Assert.IsNull(extractionStep.SkipAction);

        var openTemplateStep = tutorial.Steps.Single(step => step.Id == "open-function-template");
        Assert.AreEqual(TutorialCompletionKinds.FunctionTemplateOpened, openTemplateStep.Completion.Kind);
        StringAssert.Contains(openTemplateStep.Instructions, "Function Instance node named Iteration Logger");
        StringAssert.Contains(openTemplateStep.Instructions, "different from the Function Template definition");
        Assert.AreEqual(
            TutorialCompletionKinds.FunctionTemplateClosed,
            tutorial.Steps.Single(step => step.Id == "close-function-template").Completion.Kind);

        var secondInstanceStep = tutorial.Steps.Single(step => step.Id == "add-second-instance");
        Assert.AreEqual(TutorialCompletionKinds.FunctionInstanceAdded, secondInstanceStep.Completion.Kind);
        Assert.AreEqual("Iteration Logger", secondInstanceStep.Completion.Target);
        Assert.IsNotNull(secondInstanceStep.SkipAction);
        Assert.IsNotNull(tutorial.Steps.Single(step => step.Id == "connect-second-instance-log").SkipAction);
        Assert.IsNotNull(tutorial.Steps.Single(step => step.Id == "connect-second-instance-execute").SkipAction);
        Assert.AreEqual(TutorialTargetIds.SaveButton, tutorial.Steps.Single(step => step.Id == "save").TargetId);
        Assert.AreEqual(TutorialTargetIds.RunButton, tutorial.Steps[^1].TargetId);
        Assert.AreEqual(TutorialCompletionKinds.ModelSystemRunCompleted, tutorial.Steps[^1].Completion.Kind);
        Assert.IsNull(tutorial.Steps[^1].SkipAction);
    }
}

public sealed class AnotherTestLibraryTutorialProvider : ITutorialProvider
{
    public IEnumerable<TutorialDefinition> GetTutorials()
    {
        yield return new TutorialDefinition(
            "tests.another-library-tutorial",
            "A Library tutorial",
            "A second tutorial contributed by an external module library.",
            [
                new TutorialStepDefinition(
                    "intro",
                    "Explore another library",
                    "Follow the library-specific instructions.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.Manual))
            ],
            order: 2);
    }
}

public sealed class UnorderedTestLibraryTutorialProvider : ITutorialProvider
{
    public IEnumerable<TutorialDefinition> GetTutorials()
    {
        yield return new TutorialDefinition(
            "tests.unordered-library-tutorial",
            "B Library tutorial",
            "A tutorial without an explicit order.",
            [
                new TutorialStepDefinition(
                    "intro",
                    "Explore the unordered library lesson",
                    "Follow the library-specific instructions.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.Manual))
            ]);
    }
}

public sealed class TestLibraryTutorialProvider : ITutorialProvider
{
    public IEnumerable<TutorialDefinition> GetTutorials()
    {
        yield return new TutorialDefinition(
            "tests.library-tutorial",
            "Library tutorial",
            "A tutorial contributed by an external module library.",
            [
                new TutorialStepDefinition(
                    "intro",
                    "Explore the library",
                    "Follow the library-specific instructions.",
                    new TutorialCompletionCondition(TutorialCompletionKinds.Manual))
            ],
            order: 1);
    }
}
