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
using System.IO;
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
            RunInModelSystemContext("CreatingClient", (user, pSession, msSession) =>
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
                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", out Start start, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "AnIgnore", typeof(IgnoreResult<string>), out var ignoreMSS, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "MyFunction", typeof(SimpleTestModule), out var stm, out error2), error2?.Message);
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
                    runBus.ClientErrorWhenRunningModelSystem += (sender, runId, e, stack) =>
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
        public void ParameterModules()
        {
            RunInModelSystemContext("ParameterModules", (user, pSession, msSession) =>
            {
                CommandError error2 = null;
                var ms = msSession.ModelSystem;
                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", out Start start, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "AnIgnore", typeof(IgnoreResult<string>), out var ignoreMSS, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "SPM", typeof(SimpleParameterModule), out var spm, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "MyParameter", typeof(BasicParameter<string>), out var basicParameter, out error2), error2?.Message);
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
                    runBus.ClientErrorWhenRunningModelSystem += (sender, runId, e, stack) =>
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
            RunInModelSystemContext("ParameterModules", (user, pSession, msSession) =>
            {
                CommandError error = null;
                var ms = msSession.ModelSystem;
                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "FirstStart", out var start, out error), error?.Message);

                Assert.IsTrue(msSession.AddNodeGenerateParameters(user, ms.GlobalBoundary, "Execute", typeof(Execute), out var mss, out var _, out error));
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Ignore1", typeof(IgnoreResult<string>), out var ignore1, out error));
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Ignore2", typeof(IgnoreResult<string>), out var ignore2, out error));
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Ignore3", typeof(IgnoreResult<string>), out var ignore3, out error));
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Hello World", typeof(SimpleTestModule), out var hello, out error));
                


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
                    runBus.ClientErrorWhenRunningModelSystem += (sender, runId, e, stack) =>
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
                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", out Start start, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "AnIgnore", typeof(IgnoreResult<string>), out var ignoreMSS, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "AnIgnore", typeof(ReportFunctionInvocation<string>), out var report, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Message", typeof(BasicParameter<string>), out var message, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "MyFunction", typeof(SimpleTestModule), out var stm, out error2), error2?.Message);
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
                    clientBus.ClientErrorWhenRunningModelSystem += (sender, runId, e, stack) =>
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
                    if (!sim.Wait(2000))
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
                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", out Start start, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "AnIgnore", typeof(IgnoreResult<string>), out var ignoreMSS, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "MyFunction", typeof(SimpleTestModule), out var stm, out error2), error2?.Message);
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
                    runBus.ClientErrorWhenRunningModelSystem += (sender, runId, e, stack) =>
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
                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", out Start start, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "RequiresAChild",
                typeof(FailsAtRuntime), out var node, out error2), error2?.Message);
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
                    runBus.ClientErrorWhenRunningModelSystem += (sender, runId, e, stack) =>
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
                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", out Start start, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "RequiresAChild",
                typeof(IgnoreResult<string>), out var node, out error2), error2?.Message);
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
                    runBus.ClientErrorWhenRunningModelSystem += (sender, runId, e, stack) =>
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
                Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start", out Start start, out error2), error2?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "RequiresAChild",
                typeof(FailsAtRuntimeValidation), out var node, out error2), error2?.Message);
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
                    runBus.ClientErrorWhenRunningModelSystem += (sender, runId, e, stack) =>
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
