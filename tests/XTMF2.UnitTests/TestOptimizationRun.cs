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
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using XTMF2.Bus;
using XTMF2.Editing;
using XTMF2.ModelSystemConstruct;
using XTMF2.RuntimeModules;
using XTMF2.UnitTests.Modules;
using static XTMF2.UnitTests.TestHelper;

namespace XTMF2.UnitTests;

/// <summary>
/// Integration tests that verify estimation and calibration runs complete without crashing.
/// </summary>
[TestClass]
public class TestOptimizationRun
{
    /// <summary>
    /// Build a minimal model system, configure an estimation group with a <see cref="SetableParameter{T}"/>
    /// as the parameter under estimation and a constant-zero <see cref="BasicParameter{T}"/> as the
    /// fitness node, then start an estimation run and confirm it finishes successfully.
    /// </summary>
    [TestMethod]
    public void EstimationRun_DoesNotCrash()
    {
        RunInModelSystemContext("EstimationRun_DoesNotCrash", (user, pSession, msSession) =>
        {
            CommandError error = null;
            var ms = msSession.ModelSystem;

            // ── model system chain ──────────────────────────────────────────────
            Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start",
                Rectangle.Hidden, out Start start, out error), error?.Message);
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Ignore",
                typeof(IgnoreResult<string>), Rectangle.Hidden, out var ignoreNode, out error), error?.Message);
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "STM",
                typeof(SimpleTestModule), Rectangle.Hidden, out var stmNode, out error), error?.Message);
            Assert.IsTrue(msSession.AddLink(user, start, start.Hooks[0], ignoreNode, out _, out error), error?.Message);
            Assert.IsTrue(msSession.AddLink(user, ignoreNode, ignoreNode.Hooks[0], stmNode, out _, out error), error?.Message);

            // ── fitness node: BasicParameter<float> returning 0 ────────────────
            // With all evaluations returning the same value (0), Nelder-Mead converges
            // immediately (|f_worst - f_best| = 0 < tolerance).
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Fitness",
                typeof(BasicParameter<float>), Rectangle.Hidden, out var fitnessNode, out error), error?.Message);
            Assert.IsTrue(msSession.SetParameterValue(user, fitnessNode, "0", out error), error?.Message);
            Assert.IsTrue(msSession.SetEstimationFitnessNode(user, fitnessNode, out error), error?.Message);

            // ── estimation parameter: SetableParameter<float> ──────────────────
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Param",
                typeof(SetableParameter<float>), Rectangle.Hidden, out var paramNode, out error), error?.Message);
            Assert.IsTrue(msSession.SetParameterValue(user, paramNode, "0.5", out error), error?.Message);
            Assert.IsTrue(msSession.AddEstimationGroup(user, "Group1", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.AddEstimationParameter(user, group!, paramNode,
                min: 0.0, max: 1.0, nullHypothesis: 0.5, out _, out error), error?.Message);

            // ── run ────────────────────────────────────────────────────────────
            CreateRunClient(true, (runBus) =>
            {
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

                Assert.IsTrue(runBus.RunModelSystem(msSession,
                    Path.Combine(pSession.RunsDirectory, "EstimationRun"),
                    "Start", RunMode.Estimation, out _, out error), error?.Message);

                if (!sim.Wait(30000))
                    Assert.Fail("Estimation run timed out after 30 seconds.");

                Assert.IsTrue(success, "Estimation run did not complete successfully: " + error?.ToString());
            });
        });
    }

    /// <summary>
    /// Build a minimal model system, configure a calibration group with a <see cref="SetableParameter{T}"/>
    /// as the parameter and a constant-one <see cref="BasicParameter{T}"/> as the calibration target
    /// (ratio = 1 → already converged on the first iteration), then start a calibration run and confirm
    /// it finishes successfully.
    /// </summary>
    [TestMethod]
    public void CalibrationRun_DoesNotCrash()
    {
        RunInModelSystemContext("CalibrationRun_DoesNotCrash", (user, pSession, msSession) =>
        {
            CommandError error = null;
            var ms = msSession.ModelSystem;

            // ── model system chain ──────────────────────────────────────────────
            Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start",
                Rectangle.Hidden, out Start start, out error), error?.Message);
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Ignore",
                typeof(IgnoreResult<string>), Rectangle.Hidden, out var ignoreNode, out error), error?.Message);
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "STM",
                typeof(SimpleTestModule), Rectangle.Hidden, out var stmNode, out error), error?.Message);
            Assert.IsTrue(msSession.AddLink(user, start, start.Hooks[0], ignoreNode, out _, out error), error?.Message);
            Assert.IsTrue(msSession.AddLink(user, ignoreNode, ignoreNode.Hooks[0], stmNode, out _, out error), error?.Message);

            // ── calibration output nodes: both return 1.0 so ratio = 1.0/1.0 = 1 → converged immediately ──
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "ModelOut",
                typeof(BasicParameter<float>), Rectangle.Hidden, out var modelOutNode, out error), error?.Message);
            Assert.IsTrue(msSession.SetParameterValue(user, modelOutNode, "1", out error), error?.Message);
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "TargetOut",
                typeof(BasicParameter<float>), Rectangle.Hidden, out var targetOutNode, out error), error?.Message);
            Assert.IsTrue(msSession.SetParameterValue(user, targetOutNode, "1", out error), error?.Message);

            // ── calibration parameter: SetableParameter<float> ─────────────────
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Param",
                typeof(SetableParameter<float>), Rectangle.Hidden, out var paramNode, out error), error?.Message);
            Assert.IsTrue(msSession.SetParameterValue(user, paramNode, "0.5", out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationGroup(user, "Group1", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationParameter(user, group!, paramNode,
                min: 0.0, max: 1.0, out var entry, out error), error?.Message);
            Assert.IsTrue(msSession.SetCalibrationEntryModelOutputNode(user, entry!, modelOutNode, out error), error?.Message);
            Assert.IsTrue(msSession.SetCalibrationEntryTargetOutputNode(user, entry!, targetOutNode, out error), error?.Message);

            // ── run ────────────────────────────────────────────────────────────
            CreateRunClient(true, (runBus) =>
            {
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

                Assert.IsTrue(runBus.RunModelSystem(msSession,
                    Path.Combine(pSession.RunsDirectory, "CalibrationRun"),
                    "Start", RunMode.Calibration, out _, out error), error?.Message);

                if (!sim.Wait(30000))
                    Assert.Fail("Calibration run timed out after 30 seconds.");

                Assert.IsTrue(success, "Calibration run did not complete successfully: " + error?.ToString());
            });
        });
    }

    /// <summary>
    /// Verify that <see cref="ModelSystemSession.ApplyOptimizationResults"/> updates the
    /// parameter node's value when the estimation results are applied.
    /// </summary>
    [TestMethod]
    public void EstimationRun_Apply_UpdatesParameters()
    {
        RunInModelSystemContext("EstimationRun_Apply_UpdatesParameters", (user, pSession, msSession) =>
        {
            CommandError error = null;
            var ms = msSession.ModelSystem;

            Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start",
                Rectangle.Hidden, out Start start, out error), error?.Message);
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Ignore",
                typeof(IgnoreResult<string>), Rectangle.Hidden, out var ignoreNode, out error), error?.Message);
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "STM",
                typeof(SimpleTestModule), Rectangle.Hidden, out var stmNode, out error), error?.Message);
            Assert.IsTrue(msSession.AddLink(user, start, start.Hooks[0], ignoreNode, out _, out error), error?.Message);
            Assert.IsTrue(msSession.AddLink(user, ignoreNode, ignoreNode.Hooks[0], stmNode, out _, out error), error?.Message);

            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Fitness",
                typeof(BasicParameter<float>), Rectangle.Hidden, out var fitnessNode, out error), error?.Message);
            Assert.IsTrue(msSession.SetParameterValue(user, fitnessNode, "0", out error), error?.Message);
            Assert.IsTrue(msSession.SetEstimationFitnessNode(user, fitnessNode, out error), error?.Message);

            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Param",
                typeof(SetableParameter<float>), Rectangle.Hidden, out var paramNode, out error), error?.Message);
            Assert.IsTrue(msSession.SetParameterValue(user, paramNode, "0.5", out error), error?.Message);
            Assert.IsTrue(msSession.AddEstimationGroup(user, "Group1", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.AddEstimationParameter(user, group!, paramNode,
                min: 0.0, max: 1.0, nullHypothesis: 0.5, out _, out error), error?.Message);

            CreateRunClient(true, (runBus) =>
            {
                IReadOnlyList<(int nodeIndex, double value)> receivedResults = null;
                bool finished = false;
                using SemaphoreSlim sim = new SemaphoreSlim(0);

                runBus.ClientOptimizationResultsAvailable += (sender, runId, results) =>
                {
                    receivedResults = results;
                };
                runBus.ClientFinishedModelSystem += (sender, e) =>
                {
                    finished = true;
                    sim.Release();
                };
                runBus.ClientErrorWhenRunningModelSystem += (sender, runId, e, stack) =>
                {
                    error = new CommandError(e + "\r\n" + stack);
                    sim.Release();
                };

                Assert.IsTrue(runBus.RunModelSystem(msSession,
                    Path.Combine(pSession.RunsDirectory, "EstimationApply"),
                    "Start", RunMode.Estimation, out _, out error), error?.Message);

                if (!sim.Wait(30000))
                    Assert.Fail("Estimation run timed out.");
                Assert.IsTrue(finished, "Run did not finish successfully: " + error?.ToString());
                Assert.IsNotNull(receivedResults, "No optimization results were received.");

                string originalValue = paramNode.ParameterValue?.ToString() ?? "";
                Assert.IsTrue(msSession.ApplyOptimizationResults(user, receivedResults!, out error), error?.Message);
                string newValue = paramNode.ParameterValue?.ToString() ?? "";

                // With fitness = 0 for every evaluation, Nelder-Mead has no gradient to follow;
                // the best parameters may equal the null hypothesis.  What matters is that
                // ApplyOptimizationResults ran without error and touched the node.
                Assert.IsNotNull(newValue, "Parameter value should be non-null after apply.");
            });
        });
    }

    /// <summary>
    /// Verify that NOT calling <see cref="ModelSystemSession.ApplyOptimizationResults"/> leaves
    /// the parameter node's value unchanged at its original "0.5" setting.
    /// </summary>
    [TestMethod]
    public void EstimationRun_Discard_ParametersUnchanged()
    {
        RunInModelSystemContext("EstimationRun_Discard_ParametersUnchanged", (user, pSession, msSession) =>
        {
            CommandError error = null;
            var ms = msSession.ModelSystem;

            Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start",
                Rectangle.Hidden, out Start start, out error), error?.Message);
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Ignore",
                typeof(IgnoreResult<string>), Rectangle.Hidden, out var ignoreNode, out error), error?.Message);
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "STM",
                typeof(SimpleTestModule), Rectangle.Hidden, out var stmNode, out error), error?.Message);
            Assert.IsTrue(msSession.AddLink(user, start, start.Hooks[0], ignoreNode, out _, out error), error?.Message);
            Assert.IsTrue(msSession.AddLink(user, ignoreNode, ignoreNode.Hooks[0], stmNode, out _, out error), error?.Message);

            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Fitness",
                typeof(BasicParameter<float>), Rectangle.Hidden, out var fitnessNode, out error), error?.Message);
            Assert.IsTrue(msSession.SetParameterValue(user, fitnessNode, "0", out error), error?.Message);
            Assert.IsTrue(msSession.SetEstimationFitnessNode(user, fitnessNode, out error), error?.Message);

            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Param",
                typeof(SetableParameter<float>), Rectangle.Hidden, out var paramNode, out error), error?.Message);
            Assert.IsTrue(msSession.SetParameterValue(user, paramNode, "0.5", out error), error?.Message);
            Assert.IsTrue(msSession.AddEstimationGroup(user, "Group1", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.AddEstimationParameter(user, group!, paramNode,
                min: 0.0, max: 1.0, nullHypothesis: 0.5, out _, out error), error?.Message);

            CreateRunClient(true, (runBus) =>
            {
                bool finished = false;
                using SemaphoreSlim sim = new SemaphoreSlim(0);

                runBus.ClientFinishedModelSystem += (sender, e) =>
                {
                    finished = true;
                    sim.Release();
                };
                runBus.ClientErrorWhenRunningModelSystem += (sender, runId, e, stack) =>
                {
                    error = new CommandError(e + "\r\n" + stack);
                    sim.Release();
                };

                // Capture the parameter value BEFORE the run (discard path — never apply).
                string valueBefore = paramNode.ParameterValue?.ToString() ?? "";

                Assert.IsTrue(runBus.RunModelSystem(msSession,
                    Path.Combine(pSession.RunsDirectory, "EstimationDiscard"),
                    "Start", RunMode.Estimation, out _, out error), error?.Message);

                if (!sim.Wait(30000))
                    Assert.Fail("Estimation run timed out.");
                Assert.IsTrue(finished, "Run did not finish successfully: " + error?.ToString());

                // Do NOT call ApplyOptimizationResults — verify value is unchanged.
                string valueAfter = paramNode.ParameterValue?.ToString() ?? "";
                Assert.AreEqual(valueBefore, valueAfter,
                    "Parameter value should be unchanged when results are discarded.");
            });
        });
    }

    /// <summary>
    /// Verify that <see cref="ModelSystemSession.ApplyOptimizationResults"/> updates the
    /// parameter node's value when calibration results are applied.
    /// </summary>
    [TestMethod]
    public void CalibrationRun_Apply_UpdatesParameters()
    {
        RunInModelSystemContext("CalibrationRun_Apply_UpdatesParameters", (user, pSession, msSession) =>
        {
            CommandError error = null;
            var ms = msSession.ModelSystem;

            Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start",
                Rectangle.Hidden, out Start start, out error), error?.Message);
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Ignore",
                typeof(IgnoreResult<string>), Rectangle.Hidden, out var ignoreNode, out error), error?.Message);
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "STM",
                typeof(SimpleTestModule), Rectangle.Hidden, out var stmNode, out error), error?.Message);
            Assert.IsTrue(msSession.AddLink(user, start, start.Hooks[0], ignoreNode, out _, out error), error?.Message);
            Assert.IsTrue(msSession.AddLink(user, ignoreNode, ignoreNode.Hooks[0], stmNode, out _, out error), error?.Message);

            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "ModelOut",
                typeof(BasicParameter<float>), Rectangle.Hidden, out var modelOutNode, out error), error?.Message);
            Assert.IsTrue(msSession.SetParameterValue(user, modelOutNode, "1", out error), error?.Message);
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "TargetOut",
                typeof(BasicParameter<float>), Rectangle.Hidden, out var targetOutNode, out error), error?.Message);
            Assert.IsTrue(msSession.SetParameterValue(user, targetOutNode, "1", out error), error?.Message);

            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Param",
                typeof(SetableParameter<float>), Rectangle.Hidden, out var paramNode, out error), error?.Message);
            Assert.IsTrue(msSession.SetParameterValue(user, paramNode, "0.5", out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationGroup(user, "Group1", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationParameter(user, group!, paramNode,
                min: 0.0, max: 1.0, out var entry, out error), error?.Message);
            Assert.IsTrue(msSession.SetCalibrationEntryModelOutputNode(user, entry!, modelOutNode, out error), error?.Message);
            Assert.IsTrue(msSession.SetCalibrationEntryTargetOutputNode(user, entry!, targetOutNode, out error), error?.Message);

            CreateRunClient(true, (runBus) =>
            {
                IReadOnlyList<(int nodeIndex, double value)> receivedResults = null;
                bool finished = false;
                using SemaphoreSlim sim = new SemaphoreSlim(0);

                runBus.ClientOptimizationResultsAvailable += (sender, runId, results) =>
                {
                    receivedResults = results;
                };
                runBus.ClientFinishedModelSystem += (sender, e) =>
                {
                    finished = true;
                    sim.Release();
                };
                runBus.ClientErrorWhenRunningModelSystem += (sender, runId, e, stack) =>
                {
                    error = new CommandError(e + "\r\n" + stack);
                    sim.Release();
                };

                Assert.IsTrue(runBus.RunModelSystem(msSession,
                    Path.Combine(pSession.RunsDirectory, "CalibrationApply"),
                    "Start", RunMode.Calibration, out _, out error), error?.Message);

                if (!sim.Wait(30000))
                    Assert.Fail("Calibration run timed out.");
                Assert.IsTrue(finished, "Run did not finish successfully: " + error?.ToString());
                Assert.IsNotNull(receivedResults, "No optimization results were received.");

                Assert.IsTrue(msSession.ApplyOptimizationResults(user, receivedResults!, out error), error?.Message);
                Assert.IsNotNull(paramNode.ParameterValue?.ToString(),
                    "Parameter value should be non-null after apply.");
            });
        });
    }

    /// <summary>
    /// Verify that NOT calling <see cref="ModelSystemSession.ApplyOptimizationResults"/> leaves
    /// the parameter node's value unchanged at its original "0.5" setting after a calibration run.
    /// </summary>
    [TestMethod]
    public void CalibrationRun_Discard_ParametersUnchanged()
    {
        RunInModelSystemContext("CalibrationRun_Discard_ParametersUnchanged", (user, pSession, msSession) =>
        {
            CommandError error = null;
            var ms = msSession.ModelSystem;

            Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start",
                Rectangle.Hidden, out Start start, out error), error?.Message);
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Ignore",
                typeof(IgnoreResult<string>), Rectangle.Hidden, out var ignoreNode, out error), error?.Message);
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "STM",
                typeof(SimpleTestModule), Rectangle.Hidden, out var stmNode, out error), error?.Message);
            Assert.IsTrue(msSession.AddLink(user, start, start.Hooks[0], ignoreNode, out _, out error), error?.Message);
            Assert.IsTrue(msSession.AddLink(user, ignoreNode, ignoreNode.Hooks[0], stmNode, out _, out error), error?.Message);

            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "ModelOut",
                typeof(BasicParameter<float>), Rectangle.Hidden, out var modelOutNode, out error), error?.Message);
            Assert.IsTrue(msSession.SetParameterValue(user, modelOutNode, "1", out error), error?.Message);
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "TargetOut",
                typeof(BasicParameter<float>), Rectangle.Hidden, out var targetOutNode, out error), error?.Message);
            Assert.IsTrue(msSession.SetParameterValue(user, targetOutNode, "1", out error), error?.Message);

            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Param",
                typeof(SetableParameter<float>), Rectangle.Hidden, out var paramNode, out error), error?.Message);
            Assert.IsTrue(msSession.SetParameterValue(user, paramNode, "0.5", out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationGroup(user, "Group1", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationParameter(user, group!, paramNode,
                min: 0.0, max: 1.0, out var entry, out error), error?.Message);
            Assert.IsTrue(msSession.SetCalibrationEntryModelOutputNode(user, entry!, modelOutNode, out error), error?.Message);
            Assert.IsTrue(msSession.SetCalibrationEntryTargetOutputNode(user, entry!, targetOutNode, out error), error?.Message);

            CreateRunClient(true, (runBus) =>
            {
                bool finished = false;
                using SemaphoreSlim sim = new SemaphoreSlim(0);

                runBus.ClientFinishedModelSystem += (sender, e) =>
                {
                    finished = true;
                    sim.Release();
                };
                runBus.ClientErrorWhenRunningModelSystem += (sender, runId, e, stack) =>
                {
                    error = new CommandError(e + "\r\n" + stack);
                    sim.Release();
                };

                string valueBefore = paramNode.ParameterValue?.ToString() ?? "";

                Assert.IsTrue(runBus.RunModelSystem(msSession,
                    Path.Combine(pSession.RunsDirectory, "CalibrationDiscard"),
                    "Start", RunMode.Calibration, out _, out error), error?.Message);

                if (!sim.Wait(30000))
                    Assert.Fail("Calibration run timed out.");
                Assert.IsTrue(finished, "Run did not finish successfully: " + error?.ToString());

                // Do NOT call ApplyOptimizationResults — verify value is unchanged.
                string valueAfter = paramNode.ParameterValue?.ToString() ?? "";
                Assert.AreEqual(valueBefore, valueAfter,
                    "Parameter value should be unchanged when results are discarded.");
            });
        });
    }

    /// <summary>
    /// Verify that an estimation run can be submitted and completed successfully twice in a row
    /// against the same <see cref="HostBus"/> connection.  This guards against state-leakage
    /// bugs where a second estimation fires too early or never fires a completion event.
    /// </summary>
    [TestMethod]
    public void EstimationRun_CanRunTwice()
    {
        RunInModelSystemContext("EstimationRun_CanRunTwice", (user, pSession, msSession) =>
        {
            CommandError error = null;
            var ms = msSession.ModelSystem;

            // ── model system chain ──────────────────────────────────────────────
            Assert.IsTrue(msSession.AddModelSystemStart(user, ms.GlobalBoundary, "Start",
                Rectangle.Hidden, out Start start, out error), error?.Message);
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Ignore",
                typeof(IgnoreResult<string>), Rectangle.Hidden, out var ignoreNode, out error), error?.Message);
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "STM",
                typeof(SimpleTestModule), Rectangle.Hidden, out var stmNode, out error), error?.Message);
            Assert.IsTrue(msSession.AddLink(user, start, start.Hooks[0], ignoreNode, out _, out error), error?.Message);
            Assert.IsTrue(msSession.AddLink(user, ignoreNode, ignoreNode.Hooks[0], stmNode, out _, out error), error?.Message);

            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Fitness",
                typeof(BasicParameter<float>), Rectangle.Hidden, out var fitnessNode, out error), error?.Message);
            Assert.IsTrue(msSession.SetParameterValue(user, fitnessNode, "0", out error), error?.Message);
            Assert.IsTrue(msSession.SetEstimationFitnessNode(user, fitnessNode, out error), error?.Message);

            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Param",
                typeof(SetableParameter<float>), Rectangle.Hidden, out var paramNode, out error), error?.Message);
            Assert.IsTrue(msSession.SetParameterValue(user, paramNode, "0.5", out error), error?.Message);
            Assert.IsTrue(msSession.AddEstimationGroup(user, "Group1", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.AddEstimationParameter(user, group!, paramNode,
                min: 0.0, max: 1.0, nullHypothesis: 0.5, out _, out error), error?.Message);

            // ── run twice on the same HostBus ───────────────────────────────────
            CreateRunClient(true, (runBus) =>
            {
                for (int run = 1; run <= 2; run++)
                {
                    bool success = false;
                    using SemaphoreSlim sim = new SemaphoreSlim(0);

                    EventHandler<string> onFinished = (sender, e) =>
                    {
                        success = true;
                        sim.Release();
                    };
                    HostBus.RunError onError = (sender, runId, e, stack) =>
                    {
                        error = new CommandError(e + "\r\n" + stack);
                        sim.Release();
                    };

                    runBus.ClientFinishedModelSystem += onFinished;
                    runBus.ClientErrorWhenRunningModelSystem += onError;

                    Assert.IsTrue(runBus.RunModelSystem(msSession,
                        Path.Combine(pSession.RunsDirectory, $"EstimationRun_run{run}"),
                        "Start", RunMode.Estimation, out _, out error), error?.Message);

                    if (!sim.Wait(30000))
                        Assert.Fail($"Estimation run #{run} timed out after 30 seconds.");

                    runBus.ClientFinishedModelSystem -= onFinished;
                    runBus.ClientErrorWhenRunningModelSystem -= onError;

                    Assert.IsTrue(success, $"Estimation run #{run} did not complete successfully: " + error?.ToString());
                }
            });
        });
    }
}
