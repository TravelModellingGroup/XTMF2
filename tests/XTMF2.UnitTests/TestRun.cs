/*
    Copyright 2017 University of Toronto

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
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using XTMF2.ModelSystemConstruct;
using XTMF2.UnitTests.Modules;
using XTMF2.RuntimeModules;
using static XTMF2.UnitTests.TestHelper;
using XTMF2.Editing;

namespace XTMF2.UnitTests
{
    [TestClass]
    public class TestRun
    {
        [TestMethod]
        public void CreatingClient()
        {
            RunInModelSystemContext("CreatingClient", (user, pSession, msSession) =>
            {
                TestHelper.CreateRunClient(true, (runBus) =>
                {

                });
            });
        }

        [TestMethod]
        public void SendModelSystem()
        {
            RunInModelSystemContext("SendModelSystem", (user, pSession, msSession) =>
            {
                CreateRunClient(true, (runBus) =>
                {
                    CommandError error = null;
                    Assert.IsTrue(runBus.RunModelSystem(msSession, Path.Combine(Directory.GetCurrentDirectory(), "CreatingClient"), "Start", out var id, out error), error?.Message);
                });
            });
        }

        [TestMethod]
        public void RunModelSystemToComplete()
        {
            RunInModelSystemContext("RunModelSystemToComplete", (user, pSession, msSession) =>
            {
                CommandError error2 = null;
                var ms = msSession.ModelSystem;
                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", Rectangle.Hidden, out Start start, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "AnIgnore", typeof(IgnoreResult<string>), Rectangle.Hidden, out var ignoreMSS, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "MyFunction", typeof(SimpleTestModule), Rectangle.Hidden, out var stm, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddLink(user, start, start.Hooks[0], ignoreMSS, out var ignoreLink1, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddLink(user, ignoreMSS, ignoreMSS.Hooks[0], stm, out var ignoreLink2, out error2), error2?.Message);
                CreateRunClient(true, (runBus) =>
                {
                    CommandError error = null;
                    bool success = false;
                    using SemaphoreSlim sim = new SemaphoreSlim(0);
                    runBus.ClientFinishedModelSystem += (sender, e) =>
                    {
                        success = true;
                        sim.Release();
                    };
                    runBus.ClientErrorWhenRunningModelSystem += (sender, runId, e, stack, moduleName, elementId) =>
                    {
                        error = new CommandError(e + "\r\n" + stack);
                        sim.Release();
                    };
                    Assert.IsTrue(runBus.RunModelSystem(msSession, Path.Combine(pSession.RunsDirectory, "CreatingClient"), "Start", out var id, out error), error?.Message);
                    // give the models system some time to complete
                    if (!sim.Wait(2000))
                    {
                        Assert.Fail("The model system failed to execute in time!");
                    }
                    Assert.IsTrue(success, "The model system failed to execute to success! " + error);
                });
            });
        }

        [TestMethod]
        [DoNotParallelize]
        public void CancelModelSystemStopsActiveRun()
        {
            RunInModelSystemContext(nameof(CancelModelSystemStopsActiveRun), (user, pSession, msSession) =>
            {
                var modelSystem = msSession.ModelSystem;
                Assert.IsTrue(msSession.AddModelSystemStart(user, modelSystem.GlobalBoundary, "Start",
                    Rectangle.Hidden, out var start, out var error), error?.Message);
                Assert.IsTrue(msSession.AddNodeGenerateParameters(user, modelSystem.GlobalBoundary, "Report Started",
                    typeof(ReportActionInvocation), Rectangle.Hidden, out var report, out var reportParameters, out error),
                    error?.Message);
                Assert.IsTrue(msSession.AddNodeGenerateParameters(user, modelSystem.GlobalBoundary, "Long Sleep",
                    typeof(Sleep), Rectangle.Hidden, out var sleep, out var sleepParameters, out error), error?.Message);
                var reportMessage = reportParameters!.Single(parameter => parameter.Type == typeof(BasicParameter<string>));
                var sleepDuration = sleepParameters!.Single(parameter => parameter.Type == typeof(BasicParameter<int>));
                Assert.IsTrue(msSession.SetParameterValue(user, reportMessage,
                    "Cancellation test entered sleep.", out error), error?.Message);
                Assert.IsTrue(msSession.SetParameterValue(user, sleepDuration, "30000", out error), error?.Message);
                Assert.IsTrue(msSession.AddLink(user, start, GetHook(start.Hooks, "ToExecute"),
                    report, out _, out error), error?.Message);
                Assert.IsTrue(msSession.AddLink(user, report, GetHook(report.Hooks, "To Invoke"),
                    sleep, out _, out error), error?.Message);

                CreateRunClient(true, runBus =>
                {
                    using var started = new ManualResetEventSlim();
                    using var terminal = new ManualResetEventSlim();
                    string terminalState = string.Empty;
                    runBus.ClientReportedStatus += (_, _, status) =>
                    {
                        if (status == "Cancellation test entered sleep.")
                            started.Set();
                    };
                    runBus.ClientRunCancelled += (_, _) =>
                    {
                        terminalState = "Cancelled";
                        terminal.Set();
                    };
                    runBus.ClientFinishedModelSystem += (_, _) =>
                    {
                        terminalState = "Finished";
                        terminal.Set();
                    };
                    runBus.ClientErrorWhenRunningModelSystem += (_, _, message, _, _, _) =>
                    {
                        terminalState = $"Failed: {message}";
                        terminal.Set();
                    };

                    Assert.IsTrue(runBus.RunModelSystem(msSession,
                        Path.Combine(pSession.RunsDirectory, Guid.NewGuid().ToString("N")),
                        "Start", out var runId, out error), error?.Message);
                    Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(10)),
                        "The run did not reach the long-running action.");
                    Assert.IsTrue(runBus.CancelModelRun(runId!, out error), error?.Message);
                    Assert.IsTrue(terminal.Wait(TimeSpan.FromSeconds(10)),
                        "The run did not report a terminal state after cancellation.");
                    Assert.AreEqual("Cancelled", terminalState);
                });
            });
        }

        [TestMethod]
        [DoNotParallelize]
        public void TutorialStartSleepModelSystemRunsToCompletion()
        {
            RunInModelSystemContext("TutorialStartSleepModelSystemRunsToCompletion", (user, pSession, msSession) =>
            {
                var modelSystem = msSession.ModelSystem;
                Assert.IsTrue(msSession.AddModelSystemStart(
                    user,
                    modelSystem.GlobalBoundary,
                    "Start 1",
                    Rectangle.Hidden,
                    out var start,
                    out var error), error?.Message);
                Assert.IsTrue(msSession.AddNodeGenerateParameters(
                    user,
                    modelSystem.GlobalBoundary,
                    "Sleep",
                    typeof(Sleep),
                    Rectangle.Hidden,
                    out var sleep,
                    out _,
                    out error), error?.Message);
                Assert.IsTrue(msSession.AddLink(
                    user,
                    start,
                    start.Hooks.Single(hook => hook.Name == "ToExecute"),
                    sleep!,
                    out _,
                    out error), error?.Message);

                CreateRunClient(true, runBus =>
                {
                    using var completion = new SemaphoreSlim(0);
                    var succeeded = false;
                    string failure = string.Empty;
                    runBus.ClientFinishedModelSystem += (_, _) =>
                    {
                        succeeded = true;
                        completion.Release();
                    };
                    runBus.ClientErrorWhenRunningModelSystem += (_, _, message, stack, _, _) =>
                    {
                        failure = $"{message}{Environment.NewLine}{stack}";
                        completion.Release();
                    };

                    Assert.IsTrue(runBus.RunModelSystem(
                        msSession,
                        Path.Combine(pSession.RunsDirectory, "TutorialStartSleep"),
                        "Start 1",
                        out _,
                        out error), error?.Message);
                    Assert.IsTrue(
                        completion.Wait(50000),
                        string.IsNullOrEmpty(failure) ? "The tutorial model system did not finish in time." : failure);
                    Assert.IsTrue(succeeded, string.IsNullOrEmpty(failure) ? "The tutorial model system failed." : failure);
                });
            });
        }

        [TestMethod]
        [DoNotParallelize]
        public void TutorialLogMessageModelSystemRunsAndReportsBothMessages()
        {
            RunInModelSystemContext("TutorialLogMessageModelSystemRunsAndReportsBothMessages", (user, pSession, msSession) =>
            {
                var boundary = msSession.ModelSystem.GlobalBoundary;
                Assert.IsTrue(msSession.AddModelSystemStart(
                    user, boundary, "Start 1", Rectangle.Hidden, out var start, out var error), error?.Message);
                Assert.IsTrue(msSession.AddNodeGenerateParameters(
                    user, boundary, "Run Status Stream", typeof(OpenWriteStreamToRunStatus), Rectangle.Hidden,
                    out var statusStream, out _, out error), error?.Message);
                Assert.IsTrue(msSession.AddNodeGenerateParameters(
                    user, boundary, "Log", typeof(Log), Rectangle.Hidden,
                    out var log, out _, out error), error?.Message);
                Assert.IsTrue(msSession.AddNodeGenerateParameters(
                    user, boundary, "Write Message 1", typeof(WriteToLogA), Rectangle.Hidden,
                    out var firstMessageAction, out var firstMessageParameters, out error), error?.Message);
                Assert.IsTrue(msSession.AddNodeGenerateParameters(
                    user, boundary, "Write Message 2", typeof(WriteToLogA), Rectangle.Hidden,
                    out var secondMessageAction, out var secondMessageParameters, out error), error?.Message);

                var firstMessage = firstMessageParameters!.Single(node => node.Type == typeof(BasicParameter<string>));
                var secondMessage = secondMessageParameters!.Single(node => node.Type == typeof(BasicParameter<string>));
                Assert.IsTrue(msSession.SetParameterValue(user, firstMessage, "The run has started.", out error), error?.Message);
                Assert.IsTrue(msSession.SetParameterValue(user, secondMessage, "The logging sequence is complete.", out error), error?.Message);

                Assert.IsTrue(msSession.AddLink(
                    user, log!, log!.Hooks.Single(hook => hook.Name == "LogStream"), statusStream!,
                    out _, out error), error?.Message);
                Assert.IsTrue(msSession.AddLink(
                    user, firstMessageAction!, firstMessageAction!.Hooks.Single(hook => hook.Name == "Log"), log!,
                    out _, out error), error?.Message);
                Assert.IsTrue(msSession.AddLink(
                    user, secondMessageAction!, secondMessageAction!.Hooks.Single(hook => hook.Name == "Log"), log!,
                    out _, out error), error?.Message);
                Assert.IsTrue(msSession.AddLink(
                    user, start!, start!.Hooks.Single(hook => hook.Name == "ToExecute"), firstMessageAction!,
                    out _, out error), error?.Message);
                Assert.IsTrue(msSession.AddLink(
                    user, firstMessageAction!, firstMessageAction!.Hooks.Single(hook => hook.Name == "To Invoke"),
                    secondMessageAction!, out _, out error), error?.Message);

                CreateRunClient(true, runBus =>
                {
                    using var completion = new SemaphoreSlim(0);
                    var reportedStatuses = new ConcurrentQueue<string>();
                    var succeeded = false;
                    string failure = string.Empty;
                    runBus.ClientReportedStatus += (_, _, status) => reportedStatuses.Enqueue(status);
                    runBus.ClientFinishedModelSystem += (_, _) =>
                    {
                        succeeded = true;
                        completion.Release();
                    };
                    runBus.ClientErrorWhenRunningModelSystem += (_, _, message, stack, _, _) =>
                    {
                        failure = $"{message}{Environment.NewLine}{stack}";
                        completion.Release();
                    };

                    Assert.IsTrue(runBus.RunModelSystem(
                        msSession,
                        Path.Combine(pSession.RunsDirectory, "TutorialLogMessages"),
                        "Start 1",
                        out _,
                        out error), error?.Message);
                    Assert.IsTrue(
                        completion.Wait(50000),
                        string.IsNullOrEmpty(failure) ? "The tutorial logging model did not finish in time." : failure);
                    Assert.IsTrue(succeeded, string.IsNullOrEmpty(failure) ? "The tutorial logging model failed." : failure);

                    var statuses = reportedStatuses.ToArray();
                    Assert.IsTrue(
                        statuses.Any(status => status.EndsWith("The run has started.", StringComparison.Ordinal)),
                        "The first log message was not reported by the run.");
                    Assert.IsTrue(
                        statuses.Any(status => status.EndsWith("The logging sequence is complete.", StringComparison.Ordinal)),
                        "The second log message was not reported by the run.");
                });
            });
        }

        [TestMethod]
        [DoNotParallelize]
        public void ExecuteScriptedIterationModelSystemLogsCurrentIteration()
        {
            RunInModelSystemContext("ExecuteScriptedIterationModelSystemLogsCurrentIteration", (user, pSession, msSession) =>
            {
                var boundary = msSession.ModelSystem.GlobalBoundary;
                Assert.IsTrue(msSession.AddModelSystemStart(
                    user, boundary, "Start 1", Rectangle.Hidden, out var start, out var error), error?.Message);
                Assert.IsTrue(msSession.AddNodeGenerateParameters(
                    user, boundary, "Execute", typeof(Execute), Rectangle.Hidden,
                    out var execute, out var executeParameters, out error), error?.Message);
                Assert.IsTrue(msSession.AddNode(
                    user, boundary, "CurrentIteration", typeof(SetableParameter<int>), Rectangle.Hidden,
                    out var currentIteration, out error), error?.Message);
                Assert.IsTrue(msSession.SetParameterValue(user, currentIteration!, "0", out error), error?.Message);
                Assert.IsTrue(msSession.AddVariable(user, currentIteration!, out error), error?.Message);
                Assert.IsTrue(msSession.AddNodeGenerateParameters(
                    user, boundary, "Log", typeof(Log), Rectangle.Hidden,
                    out var log, out _, out error), error?.Message);
                Assert.IsTrue(msSession.AddNodeGenerateParameters(
                    user, boundary, "Run Status Stream", typeof(OpenWriteStreamToRunStatus), Rectangle.Hidden,
                    out var statusStream, out _, out error), error?.Message);
                Assert.IsTrue(msSession.AddNodeGenerateParameters(
                    user, boundary, "Log Iteration", typeof(WriteToLogA), Rectangle.Hidden,
                    out var logAction, out var logActionParameters, out error), error?.Message);

                var iterations = executeParameters!.Single(node => node.Name == "Iterations");
                var message = logActionParameters!.Single(node => node.Name == "Message");
                Assert.IsTrue(msSession.SetParameterValue(user, iterations, "3", out error), error?.Message);
                Assert.IsTrue(msSession.SetNodeType(
                    user, message, typeof(ScriptedParameter<string>), out error), error?.Message);
                Assert.IsTrue(msSession.SetParameterExpression(
                    user, message, "\"Iteration: \" + CurrentIteration", out error), error?.Message);

                Assert.IsTrue(msSession.AddLink(
                    user, start!, start!.Hooks.Single(hook => hook.Name == "ToExecute"), execute!,
                    out _, out error), error?.Message);
                Assert.IsTrue(msSession.AddLink(
                    user, execute!, execute!.Hooks.Single(hook => hook.Name == "Current Iteration"),
                    currentIteration!, out _, out error), error?.Message);
                Assert.IsTrue(msSession.AddLink(
                    user, execute!, execute!.Hooks.Single(hook => hook.Name == "To Execute"), logAction!,
                    out _, out error), error?.Message);
                Assert.IsTrue(msSession.AddLink(
                    user, logAction!, logAction!.Hooks.Single(hook => hook.Name == "Log"), log!,
                    out _, out error), error?.Message);
                Assert.IsTrue(msSession.AddLink(
                    user, log!, log!.Hooks.Single(hook => hook.Name == "LogStream"), statusStream!,
                    out _, out error), error?.Message);

                CreateRunClient(true, runBus =>
                {
                    using var completion = new SemaphoreSlim(0);
                    var reportedStatuses = new ConcurrentQueue<string>();
                    var succeeded = false;
                    string failure = string.Empty;
                    runBus.ClientReportedStatus += (_, _, status) => reportedStatuses.Enqueue(status);
                    runBus.ClientFinishedModelSystem += (_, _) =>
                    {
                        succeeded = true;
                        completion.Release();
                    };
                    runBus.ClientErrorWhenRunningModelSystem += (_, _, messageText, stack, _, _) =>
                    {
                        failure = $"{messageText}{Environment.NewLine}{stack}";
                        completion.Release();
                    };

                    Assert.IsTrue(runBus.RunModelSystem(
                        msSession,
                        Path.Combine(pSession.RunsDirectory, "ExecuteScriptedIterationLog"),
                        "Start 1",
                        out _,
                        out error), error?.Message);
                    Assert.IsTrue(
                        completion.Wait(100000),
                        string.IsNullOrEmpty(failure) ? "The iteration logging model did not finish in time." : failure);
                    Assert.IsTrue(succeeded, string.IsNullOrEmpty(failure) ? "The iteration logging model failed." : failure);

                    var iterationMessages = reportedStatuses
                        .Where(status => status.Contains("Iteration: ", StringComparison.Ordinal))
                        .Select(status =>
                        {
                            var startIndex = status.IndexOf("Iteration: ", StringComparison.Ordinal);
                            return status[startIndex..];
                        })
                        .ToArray();
                    CollectionAssert.AreEqual(
                        new[] { "Iteration: 0", "Iteration: 1", "Iteration: 2" },
                        iterationMessages);
                });
            });
        }

        [TestMethod]
        [DoNotParallelize]
        public void FunctionTemplateInstancesLogEachExecuteIteration()
        {
            RunInModelSystemContext(nameof(FunctionTemplateInstancesLogEachExecuteIteration), (user, pSession, msSession) =>
            {
                var boundary = msSession.ModelSystem.GlobalBoundary;
                Assert.IsTrue(msSession.AddModelSystemStart(
                    user, boundary, "Start 1", Rectangle.Hidden, out var start, out var error), error?.Message);
                Assert.IsTrue(msSession.AddNodeGenerateParameters(
                    user, boundary, "Execute", typeof(Execute), Rectangle.Hidden,
                    out var execute, out var executeParameters, out error), error?.Message);
                Assert.IsTrue(msSession.AddNode(
                    user, boundary, "CurrentIteration", typeof(SetableParameter<int>), Rectangle.Hidden,
                    out var currentIteration, out error), error?.Message);
                Assert.IsTrue(msSession.SetParameterValue(user, currentIteration!, "0", out error), error?.Message);
                Assert.IsTrue(msSession.AddVariable(user, currentIteration!, out error), error?.Message);
                Assert.IsTrue(msSession.AddNodeGenerateParameters(
                    user, boundary, "Log", typeof(Log), new Rectangle(500, 200, 120, 50),
                    out var log, out _, out error), error?.Message);
                Assert.IsTrue(msSession.AddNodeGenerateParameters(
                    user, boundary, "Run Status Stream", typeof(OpenWriteStreamToRunStatus), Rectangle.Hidden,
                    out var statusStream, out _, out error), error?.Message);
                Assert.IsTrue(msSession.AddNodeGenerateParameters(
                    user, boundary, "Log Iteration", typeof(WriteToLogA), Rectangle.Hidden,
                    out var logAction, out var logActionParameters, out error), error?.Message);

                var iterations = executeParameters!.Single(node => node.Name == "Iterations");
                var message = logActionParameters!.Single(node => node.Name == "Message");
                Assert.IsTrue(msSession.SetParameterValue(user, iterations, "3", out error), error?.Message);
                Assert.IsTrue(msSession.SetNodeType(
                    user, message, typeof(ScriptedParameter<string>), out error), error?.Message);
                Assert.IsTrue(msSession.SetParameterExpression(
                    user, message, "\"Iteration: \" + CurrentIteration", out error), error?.Message);

                Assert.IsTrue(msSession.AddLink(
                    user, start!, start!.Hooks.Single(hook => hook.Name == "ToExecute"), execute!,
                    out _, out error), error?.Message);
                Assert.IsTrue(msSession.AddLink(
                    user, execute!, execute!.Hooks.Single(hook => hook.Name == "Current Iteration"),
                    currentIteration!, out _, out error), error?.Message);
                Assert.IsTrue(msSession.AddLink(
                    user, execute!, execute!.Hooks.Single(hook => hook.Name == "To Execute"), logAction!,
                    out _, out error), error?.Message);
                Assert.IsTrue(msSession.AddLink(
                    user, logAction!, logAction!.Hooks.Single(hook => hook.Name == "Log"), log!,
                    out _, out error), error?.Message);
                Assert.IsTrue(msSession.AddLink(
                    user, log!, log!.Hooks.Single(hook => hook.Name == "LogStream"), statusStream!,
                    out _, out error), error?.Message);

                Assert.IsTrue(msSession.ExtractToFunctionTemplate(
                    user,
                    boundary,
                    [logAction!],
                    "Iteration Logger",
                    "Iteration Logger",
                    Rectangle.Hidden,
                    Rectangle.Hidden,
                    out var template,
                    out _,
                    out error), error?.Message);
                Assert.IsNotNull(template);
                Assert.AreEqual("Log", template!.FunctionParameters.Single().Name);
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
                Assert.IsTrue(msSession.AddLink(
                    user,
                    secondInstance!,
                    secondInstance!.Hooks.Single(hook => hook.Name == "Log"),
                    log!,
                    out _,
                    out error), error?.Message);
                Assert.IsTrue(msSession.AddLink(
                    user,
                    execute!,
                    execute!.Hooks.Single(hook => hook.Name == "To Execute"),
                    secondInstance!,
                    out _,
                    out error), error?.Message);

                CreateRunClient(true, runBus =>
                {
                    using var completion = new SemaphoreSlim(0);
                    var reportedStatuses = new ConcurrentQueue<string>();
                    var succeeded = false;
                    string failure = string.Empty;
                    runBus.ClientReportedStatus += (_, _, status) => reportedStatuses.Enqueue(status);
                    runBus.ClientFinishedModelSystem += (_, _) =>
                    {
                        succeeded = true;
                        completion.Release();
                    };
                    runBus.ClientErrorWhenRunningModelSystem += (_, _, messageText, stack, _, _) =>
                    {
                        failure = $"{messageText}{Environment.NewLine}{stack}";
                        completion.Release();
                    };

                    Assert.IsTrue(runBus.RunModelSystem(
                        msSession,
                        Path.Combine(pSession.RunsDirectory, "FunctionTemplateIterationLog"),
                        "Start 1",
                        out _,
                        out error), error?.Message);
                    Assert.IsTrue(
                        completion.Wait(100000),
                        string.IsNullOrEmpty(failure) ? "The function-template iteration model did not finish in time." : failure);
                    Assert.IsTrue(
                        succeeded,
                        string.IsNullOrEmpty(failure) ? "The function-template iteration model failed." : failure);

                    var iterationMessages = reportedStatuses
                        .Where(status => status.Contains("Iteration: ", StringComparison.Ordinal))
                        .Select(status =>
                        {
                            var startIndex = status.IndexOf("Iteration: ", StringComparison.Ordinal);
                            return status[startIndex..];
                        })
                        .ToArray();
                    CollectionAssert.AreEquivalent(
                        new[]
                        {
                            "Iteration: 0", "Iteration: 0",
                            "Iteration: 1", "Iteration: 1",
                            "Iteration: 2", "Iteration: 2"
                        },
                        iterationMessages);
                });
            });
        }

        [TestMethod]
        public void RunWithParameterModules()
        {
            RunInModelSystemContext("RunWithParameterModules", (user, pSession, msSession) =>
            {
                CommandError error2 = null;
                var ms = msSession.ModelSystem;
                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", Rectangle.Hidden, out Start start, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "AnIgnore", typeof(IgnoreResult<string>), Rectangle.Hidden, out var ignoreMSS, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "SPM", typeof(SimpleParameterModule), Rectangle.Hidden, out var spm, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "MyParameter", typeof(BasicParameter<string>), Rectangle.Hidden, out var basicParameter, out error2), error2?.Message);
                Assert.IsTrue(msSession.SetParameterValue(user, basicParameter, "Hello World Parameter", out error2), error2?.Message);
                Assert.IsTrue(msSession.AddLink(user, start, start.Hooks[0], ignoreMSS, out var ignoreLink1, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddLink(user, ignoreMSS, ignoreMSS.Hooks[0], spm, out var ignoreLink2, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddLink(user, spm, spm.Hooks[0], basicParameter, out var ignoreLink3, out error2), error2?.Message);
                CreateRunClient(true, (runBus) =>
                {
                    CommandError error = null;
                    bool success = false;
                    using SemaphoreSlim sim = new SemaphoreSlim(0);
                    runBus.ClientFinishedModelSystem += (sender, e) =>
                    {
                        success = true;
                        sim.Release();
                    };
                    runBus.ClientErrorWhenRunningModelSystem += (sender, runId, e, stack, moduleName, elementId) =>
                    {
                        error = new CommandError(e + "\r\n" + stack);
                        sim.Release();
                    };
                    Assert.IsTrue(runBus.RunModelSystem(msSession, Path.Combine(pSession.RunsDirectory, "CreatingClient"), "Start", out var id, out error), error?.Message);
                    // give the models system some time to complete
                    if (!sim.Wait(2000))
                    {
                        Assert.Fail("The model system failed to execute in time!");
                    }
                    Assert.IsTrue(success, "The model system failed to execute to success! " + error);

                });
            });
        }

        [TestMethod]
        public void RunWithMultiLink()
        {
            RunInModelSystemContext("RunWithMultiLink", (user, pSession, msSession) =>
            {
                CommandError error = null;
                var ms = msSession.ModelSystem;
                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "FirstStart", Rectangle.Hidden, out var start, out error), error?.Message);

                Assert.IsTrue(msSession.AddNodeGenerateParameters(user, ms.GlobalBoundary, "Execute", typeof(Execute), Rectangle.Hidden, out var mss, out var _, out error));
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Ignore1", typeof(IgnoreResult<string>), Rectangle.Hidden, out var ignore1, out error));
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Ignore2", typeof(IgnoreResult<string>), Rectangle.Hidden, out var ignore2, out error));
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Ignore3", typeof(IgnoreResult<string>), Rectangle.Hidden, out var ignore3, out error));
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Hello World", typeof(SimpleTestModule), Rectangle.Hidden, out var hello, out error));



                Assert.IsTrue(msSession.AddLink(user, start, GetHook(start.Hooks, "ToExecute"), mss, out var link, out error), error?.Message);
                Assert.IsTrue(msSession.AddLink(user, mss, GetHook(mss.Hooks, "To Execute"), ignore1, out var link1, out error), error?.Message);
                Assert.IsTrue(msSession.AddLink(user, mss, GetHook(mss.Hooks, "To Execute"), ignore2, out var link2, out error), error?.Message);
                Assert.IsTrue(msSession.AddLink(user, mss, GetHook(mss.Hooks, "To Execute"), ignore3, out var link3, out error), error?.Message);

                Assert.AreNotSame(link, link1);
                Assert.AreSame(link1, link2);
                Assert.AreSame(link1, link3);

                Assert.IsTrue(msSession.AddLink(user, ignore1, GetHook(ignore1.Hooks, "To Ignore"), hello, out var toSame1, out error), error?.Message);
                Assert.IsTrue(msSession.AddLink(user, ignore2, GetHook(ignore2.Hooks, "To Ignore"), hello, out var toSame2, out error), error?.Message);
                Assert.IsTrue(msSession.AddLink(user, ignore3, GetHook(ignore3.Hooks, "To Ignore"), hello, out var toSame3, out error), error?.Message);
                CreateRunClient(true, (runBus) =>
                {
                    CommandError error2 = null;
                    bool success = false;
                    using SemaphoreSlim sim = new SemaphoreSlim(0);

                    runBus.ClientFinishedModelSystem += (sender, e) =>
                    {
                        success = true;
                        sim.Release();
                    };
                    runBus.ClientErrorWhenRunningModelSystem += (sender, runId, e, stack, moduleName, elementId) =>
                    {
                        error2 = new CommandError(e + "\r\n" + stack);
                        sim.Release();
                    };
                    Assert.IsTrue(runBus.RunModelSystem(msSession, Path.Combine(pSession.RunsDirectory, "CreatingClient"), "FirstStart", out var id, out error2), error2?.Message);
                    // give the models system some time to complete
                    if (!sim.Wait(2000))
                    {
                        Assert.Fail("The model system failed to execute in time!");
                    }
                    Assert.IsTrue(success, "The model system failed to execute to success! " + error2?.Message);

                });
            });
        }

        [TestMethod]
        public void ReportRunProgress()
        {
            RunInModelSystemContext("ReportRunProgress", (user, pSession, msSession) =>
            {
                CommandError error2 = null;
                var ms = msSession.ModelSystem;
                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", Rectangle.Hidden, out Start start, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "AnIgnore", typeof(IgnoreResult<string>), Rectangle.Hidden, out var ignoreMSS, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "AnIgnore", typeof(ReportFunctionInvocation<string>), Rectangle.Hidden, out var report, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Message", typeof(BasicParameter<string>), Rectangle.Hidden, out var message, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "MyFunction", typeof(SimpleTestModule), Rectangle.Hidden, out var stm, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddLink(user, start, start.Hooks[0], ignoreMSS, out var ignoreLink1, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddLink(user, ignoreMSS, ignoreMSS.Hooks[0], report, out var ignoreLink2, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddLink(user, report, GetHook(report.Hooks, "To Invoke"), stm, out var ignoreLink3, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddLink(user, report, GetHook(report.Hooks, "Message"), message, out var ignoreLink4, out error2), error2?.Message);
                Assert.IsTrue(msSession.SetParameterValue(user, message, "Reporting through XTMF", out error2), error2?.Message);
                CreateRunClient(true, (clientBus) =>
                {
                    CommandError error = null;
                    bool success = false;
                    string reportedStatus = null;
                    using SemaphoreSlim sim = new SemaphoreSlim(0);

                    clientBus.ClientFinishedModelSystem += (sender, e) =>
                    {
                        success = true;
                        sim.Release();
                    };
                    clientBus.ClientErrorWhenRunningModelSystem += (sender, runId, e, stack, moduleName, elementId) =>
                    {
                        error = new CommandError(e + "\r\n" + stack);
                        sim.Release();
                    };
                    clientBus.ClientReportedStatus += (sender, runId, status) =>
                    {
                        reportedStatus = status;
                    };
                    Assert.IsTrue(clientBus.RunModelSystem(msSession, Path.Combine(pSession.RunsDirectory, "ReportRunProgress"), "Start", out var id, out error), error?.Message);
                    // give the models system some time to complete
                    if (!sim.Wait(15000))
                    {
                        Assert.Fail("The model system failed to execute in time!");
                    }
                    Assert.IsNotNull(reportedStatus);
                    Assert.AreEqual("Reporting through XTMF", reportedStatus);
                    Assert.IsTrue(success, "The model system failed to execute to success! " + error?.Message);
                });
            });
        }

        [TestMethod]
        public void RunResultsCompleted()
        {
            RunInModelSystemContext("RunResultsCompleted", (user, pSession, msSession) =>
            {
                CommandError error2 = null;
                var ms = msSession.ModelSystem;
                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", Rectangle.Hidden, out Start start, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "AnIgnore", typeof(IgnoreResult<string>), Rectangle.Hidden, out var ignoreMSS, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "MyFunction", typeof(SimpleTestModule), Rectangle.Hidden, out var stm, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddLink(user, start, start.Hooks[0], ignoreMSS, out var ignoreLink1, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddLink(user, ignoreMSS, ignoreMSS.Hooks[0], stm, out var ignoreLink2, out error2), error2?.Message);
                CreateRunClient(true, (runBus) =>
                {
                    CommandError error = null;
                    bool success = false;
                    using SemaphoreSlim sim = new SemaphoreSlim(0);

                    runBus.ClientFinishedModelSystem += (sender, e) =>
                    {
                        success = true;
                        sim.Release();
                    };
                    runBus.ClientErrorWhenRunningModelSystem += (sender, runId, e, stack, moduleName, elementId) =>
                    {
                        error = new CommandError(e + "\r\n" + stack);
                        sim.Release();
                    };
                    var runDirectory = Path.Combine(pSession.RunsDirectory, "RunResultsCompleted");
                    Assert.IsTrue(runBus.RunModelSystem(msSession, runDirectory, "Start", out var id, out error), error?.Message);
                    // give the models system some time to complete
                    if (!sim.Wait(2000))
                    {
                        Assert.Fail("The model system failed to execute in time!");
                    }
                    Assert.IsTrue(success, "The model system failed to execute to success! " + error?.Message);

                    // Check to make sure that the meta-data was stored in the run directory
                    var results = new RunResults(runDirectory);
                    Assert.IsTrue(results.Completed, "The run results stated that the run failed to complete!");
                    Assert.IsFalse(results.HasError, "The model system that successfully ran has an error!");
                    Assert.IsNull(results.Error, "There was an error for a successfully run model system!");
                    Assert.IsNull(results.ErrorMessage, "There was an error message for a successfully run model system!");
                    Assert.IsNull(results.ErrorStackTrace, "There was an error stack trace for a successfully run model system!");
                    Assert.IsNull(results.ErrorModuleName, "There was a module's name referred to as a source of an error in a successfully run model system.");
                });
            });
        }

        [TestMethod]
        public void RunResultsRuntimeError()
        {
            RunInModelSystemContext("RunResultsRuntimeError", (user, pSession, msSession) =>
            {
                CommandError error2 = null;
                var ms = msSession.ModelSystem;
                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", Rectangle.Hidden, out Start start, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "RequiresAChild",
                    typeof(FailsAtRuntime), Rectangle.Hidden, out var node, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddLink(user, start, start.Hooks[0], node, out var link, out error2), error2?.Message);
                CreateRunClient(true, (runBus) =>
                {
                    CommandError error = null;
                    bool success = false;
                    using SemaphoreSlim sim = new SemaphoreSlim(0);

                    runBus.ClientFinishedModelSystem += (sender, e) =>
                    {
                        success = true;
                        sim.Release();
                    };
                    runBus.ClientErrorWhenRunningModelSystem += (sender, runId, e, stack, moduleName, elementId) =>
                    {
                        error = new CommandError(e + "\r\n" + stack);
                        sim.Release();
                    };
                    var runDirectory = Path.Combine(pSession.RunsDirectory, "RunResultsRuntimeError");
                    Assert.IsTrue(runBus.RunModelSystem(msSession, runDirectory, "Start", out var id, out error), error?.Message);
                    // give the models system some time to complete
                    if (!sim.Wait(2000))
                    {
                        Assert.Fail("The model system failed to execute in time!");
                    }
                    Assert.IsFalse(success, "The model system reported that it was successful even though there should be an error.");

                    // Check to make sure that the meta-data was stored in the run directory
                    var results = new RunResults(runDirectory);
                    Assert.IsFalse(results.Completed, "A run that should have failed was marked as if it has completed successfully!");
                    Assert.IsTrue(results.HasError, "The model system that failed did not have an error!");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(results.Error), "The model system that failed did not have an error!");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(results.ErrorMessage), "The model system that failed did not have an error message!");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(results.ErrorStackTrace), "A run time validation error had a stack trace!");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(results.ErrorModuleName), "A run time validation error did not have an offending module name!");
                });
            });
        }

        [TestMethod]
        public void RunResultsValidationError()
        {
            RunInModelSystemContext("RunResultsValidationError", (user, pSession, msSession) =>
            {
                CommandError error2 = null;
                var ms = msSession.ModelSystem;
                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", Rectangle.Hidden, out Start start, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "RequiresAChild",
                    typeof(IgnoreResult<string>), Rectangle.Hidden, out var node, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddLink(user, start, start.Hooks[0], node, out var link, out error2), error2?.Message);
                CreateRunClient(true, (runBus) =>
                {
                    CommandError error = null;
                    bool success = false;
                    using SemaphoreSlim sim = new SemaphoreSlim(0);

                    runBus.ClientFinishedModelSystem += (sender, e) =>
                    {
                        success = true;
                        sim.Release();
                    };
                    runBus.ClientErrorWhenRunningModelSystem += (sender, runId, e, stack, moduleName, elementId) =>
                    {
                        error = new CommandError(e + "\r\n" + stack);
                        sim.Release();
                    };
                    var runDirectory = Path.Combine(pSession.RunsDirectory, "CheckRunModelSystemRunCompleteData");
                    Assert.IsTrue(runBus.RunModelSystem(msSession, runDirectory, "Start", out var id, out error), error?.Message);
                    // give the models system some time to complete
                    if (!sim.Wait(2000))
                    {
                        Assert.Fail("The model system failed to execute in time!");
                    }
                    Assert.IsFalse(success, "The model system reported that it was successful even though there should be an error.");

                    // Check to make sure that the meta-data was stored in the run directory
                    var results = new RunResults(runDirectory);
                    Assert.IsFalse(results.Completed, "A run that should have failed was marked as if it has completed successfully!");
                    Assert.IsTrue(results.HasError, "The model system that failed did not have an error!");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(results.Error), "The model system that failed did not have an error!");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(results.ErrorMessage), "The model system that failed did not have an error message!");
                    Assert.IsTrue(string.IsNullOrWhiteSpace(results.ErrorStackTrace), "A non run time validation error had a stack trace!");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(results.ErrorModuleName), "A non run time validation error did not have an offending module!");
                });
            });
        }

        [TestMethod]
        public void RunResultsRuntimeValidationError()
        {
            RunInModelSystemContext("RunResultsRuntimeValidationError", (user, pSession, msSession) =>
            {
                CommandError error2 = null;
                var ms = msSession.ModelSystem;
                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", Rectangle.Hidden, out Start start, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "RequiresAChild",
                    typeof(FailsAtRuntimeValidation), Rectangle.Hidden, out var node, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddLink(user, start, start.Hooks[0], node, out var link, out error2), error2?.Message);
                CreateRunClient(true, (runBus) =>
                {
                    CommandError error = null;
                    bool success = false;
                    using SemaphoreSlim sim = new SemaphoreSlim(0);

                    runBus.ClientFinishedModelSystem += (sender, e) =>
                    {
                        success = true;
                        sim.Release();
                    };
                    runBus.ClientErrorWhenRunningModelSystem += (sender, runId, e, stack, moduleName, elementId) =>
                    {
                        error = new CommandError(e + "\r\n" + stack);
                        sim.Release();
                    };
                    var runDirectory = Path.Combine(pSession.RunsDirectory, "CheckRunModelSystemRunCompleteData");
                    Assert.IsTrue(runBus.RunModelSystem(msSession, runDirectory, "Start", out var id, out error), error?.Message);
                    // give the models system some time to complete
                    if (!sim.Wait(2000))
                    {
                        Assert.Fail("The model system failed to execute in time!");
                    }
                    Assert.IsFalse(success, "The model system reported that it was successful even though there should be an error.");

                    // Check to make sure that the meta-data was stored in the run directory
                    var results = new RunResults(runDirectory);
                    Assert.IsFalse(results.Completed, "A run that should have failed was marked as if it has completed successfully!");
                    Assert.IsTrue(results.HasError, "The model system that failed did not have an error!");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(results.Error), "The model system that failed did not have an error!");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(results.ErrorMessage), "The model system that failed did not have an error message!");
                    Assert.IsTrue(string.IsNullOrWhiteSpace(results.ErrorStackTrace), "A non run time validation error had a stack trace!");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(results.ErrorModuleName), "A non run time validation error did not have an offending module!");
                });
            });
        }
    }
}
