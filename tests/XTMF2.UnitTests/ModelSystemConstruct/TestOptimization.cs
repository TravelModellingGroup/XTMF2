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
using System.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Bus.Optimization;
using XTMF2.Editing;
using XTMF2.ModelSystemConstruct;
using XTMF2.UnitTests.Modules;

namespace XTMF2.UnitTests.ModelSystemConstruct;

/// <summary>
/// Unit tests for the estimation and calibration backend:
/// group CRUD, parameter CRUD, group-enabled toggle, undo/redo, and persistence roundtrip.
/// </summary>
[TestClass]
public class TestOptimization
{
    // ── Estimation – group management ────────────────────────────────────────

    [TestMethod]
    public void Estimation_AddGroup()
    {
        TestHelper.RunInModelSystemContext("Estimation_AddGroup", (user, pSession, msSession) =>
        {
            CommandError error = null;
            Assert.IsTrue(msSession.AddEstimationGroup(user, "G1", out var group, out error), error?.Message);
            Assert.IsNotNull(group);
            Assert.AreEqual("G1", group.Name);
            Assert.IsTrue(group.IsEnabled);
            Assert.HasCount(1, msSession.ModelSystem.EstimationGroups);
        });
    }

    [TestMethod]
    public void Estimation_RemoveGroup()
    {
        TestHelper.RunInModelSystemContext("Estimation_RemoveGroup", (user, pSession, msSession) =>
        {
            CommandError error = null;
            Assert.IsTrue(msSession.AddEstimationGroup(user, "G1", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.RemoveEstimationGroup(user, group!, out error), error?.Message);
            Assert.HasCount(0, msSession.ModelSystem.EstimationGroups);
        });
    }

    [TestMethod]
    public void Estimation_RemoveGroup_NotFound_ReturnsFalse()
    {
        TestHelper.RunInModelSystemContext("Estimation_RemoveGroup_NotFound", (user, pSession, msSession) =>
        {
            CommandError error = null;
            var orphan = new EstimationGroup("Orphan");
            Assert.IsFalse(msSession.RemoveEstimationGroup(user, orphan, out error));
            Assert.IsNotNull(error);
        });
    }

    [TestMethod]
    public void Estimation_RenameGroup()
    {
        TestHelper.RunInModelSystemContext("Estimation_RenameGroup", (user, pSession, msSession) =>
        {
            CommandError error = null;
            Assert.IsTrue(msSession.AddEstimationGroup(user, "OldName", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.RenameEstimationGroup(user, group!, "NewName", out error), error?.Message);
            Assert.AreEqual("NewName", group!.Name);
        });
    }

    [TestMethod]
    public void Estimation_RenameGroup_Undo()
    {
        TestHelper.RunInModelSystemContext("Estimation_RenameGroup_Undo", (user, pSession, msSession) =>
        {
            CommandError error = null;
            Assert.IsTrue(msSession.AddEstimationGroup(user, "OldName", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.RenameEstimationGroup(user, group!, "NewName", out error), error?.Message);
            Assert.IsTrue(msSession.Undo(user, out error), error?.Message);
            Assert.AreEqual("OldName", group!.Name);
        });
    }

    [TestMethod]
    public void Estimation_SetGroupEnabled()
    {
        TestHelper.RunInModelSystemContext("Estimation_SetGroupEnabled", (user, pSession, msSession) =>
        {
            CommandError error = null;
            Assert.IsTrue(msSession.AddEstimationGroup(user, "G1", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.SetEstimationGroupEnabled(user, group!, false, out error), error?.Message);
            Assert.IsFalse(group!.IsEnabled);
            Assert.IsTrue(msSession.SetEstimationGroupEnabled(user, group!, true, out error), error?.Message);
            Assert.IsTrue(group!.IsEnabled);
        });
    }

    [TestMethod]
    public void Estimation_SetGroupEnabled_Undo()
    {
        TestHelper.RunInModelSystemContext("Estimation_SetGroupEnabled_Undo", (user, pSession, msSession) =>
        {
            CommandError error = null;
            Assert.IsTrue(msSession.AddEstimationGroup(user, "G1", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.SetEstimationGroupEnabled(user, group!, false, out error), error?.Message);
            Assert.IsTrue(msSession.Undo(user, out error), error?.Message);
            Assert.IsTrue(group!.IsEnabled);
        });
    }

    // ── Estimation – parameter management ────────────────────────────────────

    [TestMethod]
    public void Estimation_AddParameter()
    {
        TestHelper.RunInModelSystemContext("Estimation_AddParameter", (user, pSession, msSession) =>
        {
            CommandError error = null;
            var ms = msSession.ModelSystem;
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Param1",
                typeof(SimpleTestModule), Rectangle.Hidden, out var node, out error), error?.Message);
            Assert.IsTrue(msSession.AddEstimationGroup(user, "G1", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.AddEstimationParameter(user, group!, node!, 0.0, 1.0, 0.5,
                out var entry, out error), error?.Message);
            Assert.IsNotNull(entry);
            Assert.AreEqual(node, entry!.Node);
            Assert.AreEqual(0.0, entry.Min);
            Assert.AreEqual(1.0, entry.Max);
            Assert.AreEqual(0.5, entry.NullHypothesis);
            Assert.IsTrue(entry.IsEnabled);
            Assert.HasCount(1, group!.Parameters);
        });
    }

    [TestMethod]
    public void Estimation_AddParameter_DuplicateNode_ReturnsFalse()
    {
        TestHelper.RunInModelSystemContext("Estimation_AddParameter_Duplicate", (user, pSession, msSession) =>
        {
            CommandError error = null;
            var ms = msSession.ModelSystem;
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Param1",
                typeof(SimpleTestModule), Rectangle.Hidden, out var node, out error), error?.Message);
            Assert.IsTrue(msSession.AddEstimationGroup(user, "G1", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.AddEstimationParameter(user, group!, node!, 0.0, 1.0, 0.5,
                out _, out error), error?.Message);
            // Second add of the same node should fail
            Assert.IsFalse(msSession.AddEstimationParameter(user, group!, node!, 0.0, 1.0, 0.5,
                out _, out error));
            Assert.IsNotNull(error);
        });
    }

    [TestMethod]
    public void Estimation_RemoveParameter()
    {
        TestHelper.RunInModelSystemContext("Estimation_RemoveParameter", (user, pSession, msSession) =>
        {
            CommandError error = null;
            var ms = msSession.ModelSystem;
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Param1",
                typeof(SimpleTestModule), Rectangle.Hidden, out var node, out error), error?.Message);
            Assert.IsTrue(msSession.AddEstimationGroup(user, "G1", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.AddEstimationParameter(user, group!, node!, 0.0, 1.0, 0.5,
                out var entry, out error), error?.Message);
            Assert.IsTrue(msSession.RemoveEstimationParameter(user, group!, entry!, out error), error?.Message);
            Assert.HasCount(0, group!.Parameters);
        });
    }

    [TestMethod]
    public void Estimation_RemoveParameter_Undo()
    {
        TestHelper.RunInModelSystemContext("Estimation_RemoveParameter_Undo", (user, pSession, msSession) =>
        {
            CommandError error = null;
            var ms = msSession.ModelSystem;
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Param1",
                typeof(SimpleTestModule), Rectangle.Hidden, out var node, out error), error?.Message);
            Assert.IsTrue(msSession.AddEstimationGroup(user, "G1", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.AddEstimationParameter(user, group!, node!, 0.0, 1.0, 0.5,
                out var entry, out error), error?.Message);
            Assert.IsTrue(msSession.RemoveEstimationParameter(user, group!, entry!, out error), error?.Message);
            Assert.IsTrue(msSession.Undo(user, out error), error?.Message);
            Assert.HasCount(1, group!.Parameters);
        });
    }

    [TestMethod]
    public void Estimation_UpdateParameter()
    {
        TestHelper.RunInModelSystemContext("Estimation_UpdateParameter", (user, pSession, msSession) =>
        {
            CommandError error = null;
            var ms = msSession.ModelSystem;
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Param1",
                typeof(SimpleTestModule), Rectangle.Hidden, out var node, out error), error?.Message);
            Assert.IsTrue(msSession.AddEstimationGroup(user, "G1", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.AddEstimationParameter(user, group!, node!, 0.0, 1.0, 0.5,
                out var entry, out error), error?.Message);
            Assert.IsTrue(msSession.UpdateEstimationParameter(user, entry!, -5.0, 5.0, 0.0, false, out error),
                error?.Message);
            Assert.AreEqual(-5.0, entry!.Min);
            Assert.AreEqual(5.0,  entry!.Max);
            Assert.AreEqual(0.0,  entry!.NullHypothesis);
            Assert.IsFalse(entry!.IsEnabled);
        });
    }

    [TestMethod]
    public void Estimation_UpdateParameter_Undo()
    {
        TestHelper.RunInModelSystemContext("Estimation_UpdateParameter_Undo", (user, pSession, msSession) =>
        {
            CommandError error = null;
            var ms = msSession.ModelSystem;
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Param1",
                typeof(SimpleTestModule), Rectangle.Hidden, out var node, out error), error?.Message);
            Assert.IsTrue(msSession.AddEstimationGroup(user, "G1", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.AddEstimationParameter(user, group!, node!, 0.0, 1.0, 0.5,
                out var entry, out error), error?.Message);
            Assert.IsTrue(msSession.UpdateEstimationParameter(user, entry!, -5.0, 5.0, 0.0, false, out error),
                error?.Message);
            Assert.IsTrue(msSession.Undo(user, out error), error?.Message);
            Assert.AreEqual(0.0, entry!.Min);
            Assert.AreEqual(1.0, entry!.Max);
            Assert.AreEqual(0.5, entry!.NullHypothesis);
            Assert.IsTrue(entry!.IsEnabled);
        });
    }

    // ── Estimation – fitness node ─────────────────────────────────────────────

    [TestMethod]
    public void Estimation_SetFitnessNode()
    {
        TestHelper.RunInModelSystemContext("Estimation_SetFitnessNode", (user, pSession, msSession) =>
        {
            CommandError error = null;
            var ms = msSession.ModelSystem;
            Assert.IsNull(ms.EstimationFitnessNode);
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "FitnessNode",
                typeof(SimpleTestModule), Rectangle.Hidden, out var node, out error), error?.Message);
            Assert.IsTrue(msSession.SetEstimationFitnessNode(user, node, out error), error?.Message);
            Assert.AreEqual(node, ms.EstimationFitnessNode);
        });
    }

    [TestMethod]
    public void Estimation_ClearFitnessNode()
    {
        TestHelper.RunInModelSystemContext("Estimation_ClearFitnessNode", (user, pSession, msSession) =>
        {
            CommandError error = null;
            var ms = msSession.ModelSystem;
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "FitnessNode",
                typeof(SimpleTestModule), Rectangle.Hidden, out var node, out error), error?.Message);
            Assert.IsTrue(msSession.SetEstimationFitnessNode(user, node, out error), error?.Message);
            Assert.IsTrue(msSession.SetEstimationFitnessNode(user, null, out error), error?.Message);
            Assert.IsNull(ms.EstimationFitnessNode);
        });
    }

    [TestMethod]
    public void Estimation_SetFitnessNode_Undo()
    {
        TestHelper.RunInModelSystemContext("Estimation_SetFitnessNode_Undo", (user, pSession, msSession) =>
        {
            CommandError error = null;
            var ms = msSession.ModelSystem;
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "FitnessNode",
                typeof(SimpleTestModule), Rectangle.Hidden, out var node, out error), error?.Message);
            Assert.IsTrue(msSession.SetEstimationFitnessNode(user, node, out error), error?.Message);
            Assert.IsTrue(msSession.Undo(user, out error), error?.Message);
            Assert.IsNull(ms.EstimationFitnessNode);
        });
    }

    // ── Estimation – persistence ──────────────────────────────────────────────

    [TestMethod]
    public void Estimation_GroupsPersistence()
    {
        TestHelper.RunInModelSystemContext("Estimation_GroupsPersistence",
            (user, pSession, msSession) =>
            {
                CommandError error = null;
                var ms = msSession.ModelSystem;
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "P1",
                    typeof(SimpleTestModule), Rectangle.Hidden, out var node, out error), error?.Message);
                Assert.IsTrue(msSession.AddEstimationGroup(user, "GroupA", out var group, out error), error?.Message);
                Assert.IsTrue(msSession.AddEstimationParameter(user, group!, node!, -1.0, 2.0, 0.5,
                    out _, out error), error?.Message);
                Assert.IsTrue(msSession.SetEstimationGroupEnabled(user, group!, false, out error), error?.Message);
            },
            (user, pSession, msSession) =>
            {
                var ms = msSession.ModelSystem;
                Assert.HasCount(1, ms.EstimationGroups);
                var group = ms.EstimationGroups[0];
                Assert.AreEqual("GroupA", group.Name);
                Assert.IsFalse(group.IsEnabled);
                Assert.HasCount(1, group.Parameters);
                var entry = group.Parameters[0];
                Assert.AreEqual("P1", entry.Node.Name);
                Assert.AreEqual(-1.0, entry.Min);
                Assert.AreEqual(2.0,  entry.Max);
                Assert.AreEqual(0.5,  entry.NullHypothesis);
            });
    }

    [TestMethod]
    public void Estimation_EntryIsEnabledPersistence()
    {
        TestHelper.RunInModelSystemContext("Estimation_EntryIsEnabledPersistence",
            (user, pSession, msSession) =>
            {
                CommandError error = null;
                var ms = msSession.ModelSystem;
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "P1",
                    typeof(SimpleTestModule), Rectangle.Hidden, out var node, out error), error?.Message);
                Assert.IsTrue(msSession.AddEstimationGroup(user, "G1", out var group, out error), error?.Message);
                Assert.IsTrue(msSession.AddEstimationParameter(user, group!, node!, 0.0, 1.0, 0.0,
                    out var entry, out error), error?.Message);
                Assert.IsTrue(msSession.UpdateEstimationParameter(user, entry!, 0.0, 1.0, 0.0, false, out error),
                    error?.Message);
            },
            (user, pSession, msSession) =>
            {
                var entry = msSession.ModelSystem.EstimationGroups[0].Parameters[0];
                Assert.IsFalse(entry.IsEnabled);
            });
    }

    // ── Calibration – group management ───────────────────────────────────────

    [TestMethod]
    public void Calibration_AddGroup()
    {
        TestHelper.RunInModelSystemContext("Calibration_AddGroup", (user, pSession, msSession) =>
        {
            CommandError error = null;
            Assert.IsTrue(msSession.AddCalibrationGroup(user, "CG1", out var group, out error), error?.Message);
            Assert.IsNotNull(group);
            Assert.AreEqual("CG1", group.Name);
            Assert.IsTrue(group.IsEnabled);
            Assert.HasCount(1, msSession.ModelSystem.CalibrationGroups);
        });
    }

    [TestMethod]
    public void Calibration_RemoveGroup()
    {
        TestHelper.RunInModelSystemContext("Calibration_RemoveGroup", (user, pSession, msSession) =>
        {
            CommandError error = null;
            Assert.IsTrue(msSession.AddCalibrationGroup(user, "CG1", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.RemoveCalibrationGroup(user, group!, out error), error?.Message);
            Assert.HasCount(0, msSession.ModelSystem.CalibrationGroups);
        });
    }

    [TestMethod]
    public void Calibration_RemoveGroup_NotFound_ReturnsFalse()
    {
        TestHelper.RunInModelSystemContext("Calibration_RemoveGroup_NotFound", (user, pSession, msSession) =>
        {
            CommandError error = null;
            var orphan = new CalibrationGroup("Orphan");
            Assert.IsFalse(msSession.RemoveCalibrationGroup(user, orphan, out error));
            Assert.IsNotNull(error);
        });
    }

    [TestMethod]
    public void Calibration_RenameGroup()
    {
        TestHelper.RunInModelSystemContext("Calibration_RenameGroup", (user, pSession, msSession) =>
        {
            CommandError error = null;
            Assert.IsTrue(msSession.AddCalibrationGroup(user, "OldCG", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.RenameCalibrationGroup(user, group!, "NewCG", out error), error?.Message);
            Assert.AreEqual("NewCG", group!.Name);
        });
    }

    [TestMethod]
    public void Calibration_RenameGroup_Undo()
    {
        TestHelper.RunInModelSystemContext("Calibration_RenameGroup_Undo", (user, pSession, msSession) =>
        {
            CommandError error = null;
            Assert.IsTrue(msSession.AddCalibrationGroup(user, "OldCG", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.RenameCalibrationGroup(user, group!, "NewCG", out error), error?.Message);
            Assert.IsTrue(msSession.Undo(user, out error), error?.Message);
            Assert.AreEqual("OldCG", group!.Name);
        });
    }

    [TestMethod]
    public void Calibration_SetGroupEnabled()
    {
        TestHelper.RunInModelSystemContext("Calibration_SetGroupEnabled", (user, pSession, msSession) =>
        {
            CommandError error = null;
            Assert.IsTrue(msSession.AddCalibrationGroup(user, "CG1", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.SetCalibrationGroupEnabled(user, group!, false, out error), error?.Message);
            Assert.IsFalse(group!.IsEnabled);
            Assert.IsTrue(msSession.SetCalibrationGroupEnabled(user, group!, true, out error), error?.Message);
            Assert.IsTrue(group!.IsEnabled);
        });
    }

    [TestMethod]
    public void Calibration_SetGroupEnabled_Undo()
    {
        TestHelper.RunInModelSystemContext("Calibration_SetGroupEnabled_Undo", (user, pSession, msSession) =>
        {
            CommandError error = null;
            Assert.IsTrue(msSession.AddCalibrationGroup(user, "CG1", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.SetCalibrationGroupEnabled(user, group!, false, out error), error?.Message);
            Assert.IsTrue(msSession.Undo(user, out error), error?.Message);
            Assert.IsTrue(group!.IsEnabled);
        });
    }

    // ── Calibration – parameter management ───────────────────────────────────

    [TestMethod]
    public void Calibration_AddParameter()
    {
        TestHelper.RunInModelSystemContext("Calibration_AddParameter", (user, pSession, msSession) =>
        {
            CommandError error = null;
            var ms = msSession.ModelSystem;
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "CParam1",
                typeof(SimpleTestModule), Rectangle.Hidden, out var node, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationGroup(user, "CG1", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationParameter(user, group!, node!, 0.0, 10.0,
                out var entry, out error), error?.Message);
            Assert.IsNotNull(entry);
            Assert.AreEqual(node, entry!.Node);
            Assert.AreEqual(0.0, entry.Min);
            Assert.AreEqual(10.0, entry.Max);
            Assert.IsTrue(entry.IsEnabled);
            Assert.IsNull(entry.ModelOutputNode);
            Assert.IsNull(entry.TargetOutputNode);
            Assert.HasCount(1, group!.Parameters);
        });
    }

    [TestMethod]
    public void Calibration_AddParameter_DuplicateNode_ReturnsFalse()
    {
        TestHelper.RunInModelSystemContext("Calibration_AddParameter_Duplicate", (user, pSession, msSession) =>
        {
            CommandError error = null;
            var ms = msSession.ModelSystem;
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "CParam1",
                typeof(SimpleTestModule), Rectangle.Hidden, out var node, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationGroup(user, "CG1", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationParameter(user, group!, node!, 0.0, 10.0,
                out _, out error), error?.Message);
            Assert.IsFalse(msSession.AddCalibrationParameter(user, group!, node!, 0.0, 10.0,
                out _, out error));
            Assert.IsNotNull(error);
        });
    }

    [TestMethod]
    public void Calibration_RemoveParameter()
    {
        TestHelper.RunInModelSystemContext("Calibration_RemoveParameter", (user, pSession, msSession) =>
        {
            CommandError error = null;
            var ms = msSession.ModelSystem;
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "CParam1",
                typeof(SimpleTestModule), Rectangle.Hidden, out var node, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationGroup(user, "CG1", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationParameter(user, group!, node!, 0.0, 10.0,
                out var entry, out error), error?.Message);
            Assert.IsTrue(msSession.RemoveCalibrationParameter(user, group!, entry!, out error), error?.Message);
            Assert.HasCount(0, group!.Parameters);
        });
    }

    [TestMethod]
    public void Calibration_RemoveParameter_Undo()
    {
        TestHelper.RunInModelSystemContext("Calibration_RemoveParameter_Undo", (user, pSession, msSession) =>
        {
            CommandError error = null;
            var ms = msSession.ModelSystem;
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "CParam1",
                typeof(SimpleTestModule), Rectangle.Hidden, out var node, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationGroup(user, "CG1", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationParameter(user, group!, node!, 0.0, 10.0,
                out var entry, out error), error?.Message);
            Assert.IsTrue(msSession.RemoveCalibrationParameter(user, group!, entry!, out error), error?.Message);
            Assert.IsTrue(msSession.Undo(user, out error), error?.Message);
            Assert.HasCount(1, group!.Parameters);
        });
    }

    [TestMethod]
    public void Calibration_UpdateParameter()
    {
        TestHelper.RunInModelSystemContext("Calibration_UpdateParameter", (user, pSession, msSession) =>
        {
            CommandError error = null;
            var ms = msSession.ModelSystem;
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "CParam1",
                typeof(SimpleTestModule), Rectangle.Hidden, out var node, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationGroup(user, "CG1", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationParameter(user, group!, node!, 0.0, 10.0,
                out var entry, out error), error?.Message);
            Assert.IsTrue(msSession.UpdateCalibrationParameter(user, entry!, -2.0, 8.0, false, 5e-4, 0.25, new XTMF2.Bus.Optimization.ProportionalScalingAlgorithm(), out error),
                error?.Message);
            Assert.AreEqual(-2.0, entry!.Min);
            Assert.AreEqual(8.0,  entry!.Max);
            Assert.IsFalse(entry!.IsEnabled);
            Assert.AreEqual(5e-4, entry!.ErrorTolerance);
            Assert.AreEqual(0.25, entry!.StepSize);
            Assert.AreEqual(new XTMF2.Bus.Optimization.ProportionalScalingAlgorithm(), entry!.Algorithm);
        });
    }

    [TestMethod]
    public void Calibration_UpdateParameter_Undo()
    {
        TestHelper.RunInModelSystemContext("Calibration_UpdateParameter_Undo", (user, pSession, msSession) =>
        {
            CommandError error = null;
            var ms = msSession.ModelSystem;
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "CParam1",
                typeof(SimpleTestModule), Rectangle.Hidden, out var node, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationGroup(user, "CG1", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationParameter(user, group!, node!, 0.0, 10.0,
                out var entry, out error), error?.Message);
            Assert.IsTrue(msSession.UpdateCalibrationParameter(user, entry!, -2.0, 8.0, false, 1e-4, 0.75, new XTMF2.Bus.Optimization.ProportionalScalingAlgorithm(), out error),
                error?.Message);
            Assert.IsTrue(msSession.Undo(user, out error), error?.Message);
            Assert.AreEqual(0.0,  entry!.Min);
            Assert.AreEqual(10.0, entry!.Max);
            Assert.IsTrue(entry!.IsEnabled);
            Assert.AreEqual(1.0, entry!.StepSize);
        });
    }

    [TestMethod]
    public void Calibration_NewEntryInheritsGroupDefaultAlgorithm()
    {
        TestHelper.RunInModelSystemContext("Calibration_NewEntryInheritsGroupDefaultAlgorithm", (user, pSession, msSession) =>
        {
            CommandError error = null;
            var ms = msSession.ModelSystem;
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "CParam1",
                typeof(SimpleTestModule), Rectangle.Hidden, out var node, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationGroup(user, "CG1", out var group, out error), error?.Message);
            // Default is Log Odds
            Assert.AreEqual(new XTMF2.Bus.Optimization.LogOddsAlgorithm(), group!.DefaultAlgorithm);
            Assert.IsTrue(msSession.AddCalibrationParameter(user, group!, node!, 0.0, 1.0,
                out var entry, out error), error?.Message);
            Assert.AreEqual(new XTMF2.Bus.Optimization.LogOddsAlgorithm(), entry!.Algorithm);
            Assert.AreEqual(1.0, entry!.StepSize);
        });
    }

    [TestMethod]
    public void Calibration_SetGroupDefaultAlgorithm()
    {
        TestHelper.RunInModelSystemContext("Calibration_SetGroupDefaultAlgorithm", (user, pSession, msSession) =>
        {
            CommandError error = null;
            Assert.IsTrue(msSession.AddCalibrationGroup(user, "CG1", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.SetCalibrationGroupDefaultAlgorithm(
                user, group!, new XTMF2.Bus.Optimization.LogOddsAlgorithm(), out error), error?.Message);
            Assert.AreEqual(new XTMF2.Bus.Optimization.LogOddsAlgorithm(), group!.DefaultAlgorithm);
        });
    }

    [TestMethod]
    public void Calibration_SetGroupDefaultAlgorithm_Undo()
    {
        TestHelper.RunInModelSystemContext("Calibration_SetGroupDefaultAlgorithm_Undo", (user, pSession, msSession) =>
        {
            CommandError error = null;
            Assert.IsTrue(msSession.AddCalibrationGroup(user, "CG1", out var group, out error), error?.Message);
            var initial = group!.DefaultAlgorithm;
            Assert.IsTrue(msSession.SetCalibrationGroupDefaultAlgorithm(
                user, group!, new XTMF2.Bus.Optimization.LogOddsAlgorithm(), out error), error?.Message);
            Assert.IsTrue(msSession.Undo(user, out error), error?.Message);
            Assert.AreEqual(initial, group!.DefaultAlgorithm);
        });
    }

    [TestMethod]
    public void Calibration_LogOdds_ApplyAndRegistration()
    {
        var logOdds = new XTMF2.Bus.Optimization.LogOddsAlgorithm();
        var updated = logOdds.Apply(currentValue: 1.25, modelled: 0.25, target:0.5, min: -5.0, max: 5.0);

        Assert.IsGreaterThan(1.25, updated, "The parameter was not updated to be larger!");
        bool foundLogOdds = false;
        foreach (var algorithm in CalibrationAlgorithmBase.AvailableAlgorithms)
            foundLogOdds |= algorithm is XTMF2.Bus.Optimization.LogOddsAlgorithm;
        Assert.IsTrue(foundLogOdds);
    }

    [TestMethod]
    public void Calibration_LogOddsPersistence()
    {
        TestHelper.RunInModelSystemContext("Calibration_LogOddsPersistence",
            (user, pSession, msSession) =>
            {
                CommandError error = null;
                var ms = msSession.ModelSystem;
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "CP_LogOdds",
                    typeof(SimpleTestModule), Rectangle.Hidden, out var node, out error), error?.Message);
                Assert.IsTrue(msSession.AddCalibrationGroup(user, "CG_LogOdds", out var group, out error), error?.Message);
                Assert.IsTrue(msSession.SetCalibrationGroupDefaultAlgorithm(
                    user, group!, new XTMF2.Bus.Optimization.LogOddsAlgorithm(), out error), error?.Message);
                Assert.IsTrue(msSession.AddCalibrationParameter(user, group!, node!, -1.0, 2.0,
                    out var entry, out error), error?.Message);
                Assert.IsTrue(msSession.UpdateCalibrationParameter(
                    user, entry!, -1.0, 2.0, true, 1e-4, 0.5, new XTMF2.Bus.Optimization.LogOddsAlgorithm(), out error), error?.Message);
            },
            (user, pSession, msSession) =>
            {
                var group = msSession.ModelSystem.CalibrationGroups[0];
                var entry = group.Parameters[0];

                Assert.IsInstanceOfType<XTMF2.Bus.Optimization.LogOddsAlgorithm>(group.DefaultAlgorithm);
                Assert.IsInstanceOfType<XTMF2.Bus.Optimization.LogOddsAlgorithm>(entry.Algorithm);
                Assert.AreEqual(0.5, entry.StepSize);
            });
    }

    // ── Calibration – model output and target output nodes ─────────────────────────

    [TestMethod]
    public void Calibration_SetModelOutputNode()
    {
        TestHelper.RunInModelSystemContext("Calibration_SetModelOutputNode", (user, pSession, msSession) =>
        {
            CommandError error = null;
            var ms = msSession.ModelSystem;
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "CParam1",
                typeof(SimpleTestModule), Rectangle.Hidden, out var paramNode, out error), error?.Message);
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "ModelFn",
                typeof(SimpleTestModule), Rectangle.Hidden, out var modelNode, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationGroup(user, "CG1", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationParameter(user, group!, paramNode!, 0.0, 10.0,
                out var entry, out error), error?.Message);
            Assert.IsTrue(msSession.SetCalibrationEntryModelOutputNode(user, entry!, modelNode, out error),
                error?.Message);
            Assert.AreEqual(modelNode, entry!.ModelOutputNode);
        });
    }

    [TestMethod]
    public void Calibration_SetTargetOutputNode()
    {
        TestHelper.RunInModelSystemContext("Calibration_SetTargetOutputNode", (user, pSession, msSession) =>
        {
            CommandError error = null;
            var ms = msSession.ModelSystem;
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "CParam1",
                typeof(SimpleTestModule), Rectangle.Hidden, out var paramNode, out error), error?.Message);
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "TargetFn",
                typeof(SimpleTestModule), Rectangle.Hidden, out var targetNode, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationGroup(user, "CG1", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationParameter(user, group!, paramNode!, 0.0, 10.0,
                out var entry, out error), error?.Message);
            Assert.IsTrue(msSession.SetCalibrationEntryTargetOutputNode(user, entry!, targetNode, out error),
                error?.Message);
            Assert.AreEqual(targetNode, entry!.TargetOutputNode);
        });
    }

    [TestMethod]
    public void Calibration_ClearTargetOutputNode()
    {
        TestHelper.RunInModelSystemContext("Calibration_ClearTargetOutputNode", (user, pSession, msSession) =>
        {
            CommandError error = null;
            var ms = msSession.ModelSystem;
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "CParam1",
                typeof(SimpleTestModule), Rectangle.Hidden, out var paramNode, out error), error?.Message);
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "TargetFn",
                typeof(SimpleTestModule), Rectangle.Hidden, out var targetNode, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationGroup(user, "CG1", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationParameter(user, group!, paramNode!, 0.0, 10.0,
                out var entry, out error), error?.Message);
            Assert.IsTrue(msSession.SetCalibrationEntryTargetOutputNode(user, entry!, targetNode, out error),
                error?.Message);
            Assert.IsTrue(msSession.SetCalibrationEntryTargetOutputNode(user, entry!, null, out error), error?.Message);
            Assert.IsNull(entry!.TargetOutputNode);
        });
    }

    [TestMethod]
    public void Calibration_SetTargetOutputNode_Undo()
    {
        TestHelper.RunInModelSystemContext("Calibration_SetTargetOutputNode_Undo", (user, pSession, msSession) =>
        {
            CommandError error = null;
            var ms = msSession.ModelSystem;
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "CParam1",
                typeof(SimpleTestModule), Rectangle.Hidden, out var paramNode, out error), error?.Message);
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "TargetFn",
                typeof(SimpleTestModule), Rectangle.Hidden, out var targetNode, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationGroup(user, "CG1", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationParameter(user, group!, paramNode!, 0.0, 10.0,
                out var entry, out error), error?.Message);
            Assert.IsTrue(msSession.SetCalibrationEntryTargetOutputNode(user, entry!, targetNode, out error),
                error?.Message);
            Assert.IsTrue(msSession.Undo(user, out error), error?.Message);
            Assert.IsNull(entry!.TargetOutputNode);
        });
    }

    // ── Calibration – persistence ─────────────────────────────────────────────

    [TestMethod]
    public void Calibration_GroupsPersistence()
    {
        TestHelper.RunInModelSystemContext("Calibration_GroupsPersistence",
            (user, pSession, msSession) =>
            {
                CommandError error = null;
                var ms = msSession.ModelSystem;
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "CP1",
                    typeof(SimpleTestModule), Rectangle.Hidden, out var paramNode, out error), error?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "TFn",
                    typeof(SimpleTestModule), Rectangle.Hidden, out var targetNode, out error), error?.Message);
                Assert.IsTrue(msSession.AddCalibrationGroup(user, "GroupB", out var group, out error), error?.Message);
                Assert.IsTrue(msSession.AddCalibrationParameter(user, group!, paramNode!, -3.0, 7.0,
                    out var entry, out error), error?.Message);
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "ModelFn",
                    typeof(SimpleTestModule), Rectangle.Hidden, out var modelNode, out error), error?.Message);
                Assert.IsTrue(msSession.SetCalibrationEntryModelOutputNode(user, entry!, modelNode, out error),
                    error?.Message);
                Assert.IsTrue(msSession.SetCalibrationEntryTargetOutputNode(user, entry!, targetNode, out error),
                    error?.Message);
                Assert.IsTrue(msSession.SetCalibrationGroupEnabled(user, group!, false, out error), error?.Message);
                Assert.IsTrue(msSession.UpdateCalibrationParameter(user, entry!, -3.0, 7.0, true, 1e-4, 0.4, new XTMF2.Bus.Optimization.LogOddsAlgorithm(), out error),
                    error?.Message);
            },
            (user, pSession, msSession) =>
            {
                var ms = msSession.ModelSystem;
                Assert.HasCount(1, ms.CalibrationGroups);
                var group = ms.CalibrationGroups[0];
                Assert.AreEqual("GroupB", group.Name);
                Assert.IsFalse(group.IsEnabled);
                Assert.HasCount(1, group.Parameters);
                var entry = group.Parameters[0];
                Assert.AreEqual("CP1", entry.Node.Name);
                Assert.AreEqual(-3.0, entry.Min);
                Assert.AreEqual(7.0,  entry.Max);
                Assert.IsNotNull(entry.ModelOutputNode);
                Assert.AreEqual("ModelFn", entry.ModelOutputNode!.Name);
                Assert.IsNotNull(entry.TargetOutputNode);
                Assert.AreEqual("TFn", entry.TargetOutputNode!.Name);
                Assert.AreEqual(0.4, entry.StepSize);
            });
    }

    [TestMethod]
    public void Calibration_EntryIsEnabledPersistence()
    {
        TestHelper.RunInModelSystemContext("Calibration_EntryIsEnabledPersistence",
            (user, pSession, msSession) =>
            {
                CommandError error = null;
                var ms = msSession.ModelSystem;
                Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "CP1",
                    typeof(SimpleTestModule), Rectangle.Hidden, out var node, out error), error?.Message);
                Assert.IsTrue(msSession.AddCalibrationGroup(user, "CG1", out var group, out error), error?.Message);
                Assert.IsTrue(msSession.AddCalibrationParameter(user, group!, node!, 0.0, 1.0,
                    out var entry, out error), error?.Message);
                Assert.IsTrue(msSession.UpdateCalibrationParameter(user, entry!, 0.0, 1.0, false, 1e-4, 0.5, new XTMF2.Bus.Optimization.ProportionalScalingAlgorithm(), out error),
                    error?.Message);
            },
            (user, pSession, msSession) =>
            {
                var entry = msSession.ModelSystem.CalibrationGroups[0].Parameters[0];
                Assert.IsFalse(entry.IsEnabled);
                Assert.AreEqual(0.5, entry.StepSize);
            });
    }

    // ── Estimation – algorithm config and objective persistence ───────────────

    [TestMethod]
    public void Estimation_AlgorithmConfig_DefaultIsNelderMead()
    {
        TestHelper.RunInModelSystemContext("Estimation_AlgorithmConfig_DefaultIsNelderMead",
            (user, pSession, msSession) =>
            {
                var ms = msSession.ModelSystem;
                Assert.IsInstanceOfType<NelderMeadConfig>(ms.EstimationAlgorithmConfig);
                Assert.AreEqual(EstimationObjective.Minimize, ms.EstimationObjective);
            });
    }

    [TestMethod]
    public void Estimation_AlgorithmConfig_NelderMeadPersistence()
    {
        TestHelper.RunInModelSystemContext("Estimation_AlgorithmConfig_NelderMeadPersistence",
            (user, pSession, msSession) =>
            {
                CommandError error = null;
                var cfg = new NelderMeadConfig { MaxIterations = 999, ConvergenceTolerance = 1e-9 };
                Assert.IsTrue(msSession.SetEstimationAlgorithmConfig(user, cfg, out error), error?.Message);
            },
            (user, pSession, msSession) =>
            {
                var ms = msSession.ModelSystem;
                Assert.IsInstanceOfType<NelderMeadConfig>(ms.EstimationAlgorithmConfig);
                var loaded = (NelderMeadConfig)ms.EstimationAlgorithmConfig;
                Assert.AreEqual(999, loaded.MaxIterations);
                Assert.AreEqual(1e-9, loaded.ConvergenceTolerance);
            });
    }

    [TestMethod]
    public void Estimation_AlgorithmConfig_ParticleSwarmPersistence()
    {
        TestHelper.RunInModelSystemContext("Estimation_AlgorithmConfig_ParticleSwarmPersistence",
            (user, pSession, msSession) =>
            {
                CommandError error = null;
                var cfg = new ParticleSwarmConfig
                {
                    SwarmSize = 42,
                    MaxIterations = 200,
                    Inertia = 0.5,
                    CognitiveCoeff = 1.2,
                    SocialCoeff = 1.3,
                    ConvergenceTolerance = 1e-8
                };
                Assert.IsTrue(msSession.SetEstimationAlgorithmConfig(user, cfg, out error), error?.Message);
            },
            (user, pSession, msSession) =>
            {
                var ms = msSession.ModelSystem;
                Assert.IsInstanceOfType<ParticleSwarmConfig>(ms.EstimationAlgorithmConfig);
                var loaded = (ParticleSwarmConfig)ms.EstimationAlgorithmConfig;
                Assert.AreEqual(42,   loaded.SwarmSize);
                Assert.AreEqual(200,  loaded.MaxIterations);
                Assert.AreEqual(0.5,  loaded.Inertia);
                Assert.AreEqual(1.2,  loaded.CognitiveCoeff);
                Assert.AreEqual(1.3,  loaded.SocialCoeff);
                Assert.AreEqual(1e-8, loaded.ConvergenceTolerance);
            });
    }

    [TestMethod]
    public void Estimation_AlgorithmConfig_GeneticAlgorithmPersistence()
    {
        TestHelper.RunInModelSystemContext("Estimation_AlgorithmConfig_GeneticAlgorithmPersistence",
            (user, pSession, msSession) =>
            {
                CommandError error = null;
                var cfg = new GeneticAlgorithmConfig
                {
                    PopulationSize = 100,
                    MaxGenerations = 250,
                    CrossoverRate = 0.9,
                    MutationRate = 0.02,
                    ElitismCount = 4,
                    TournamentSize = 5,
                    ConvergenceTolerance = 1e-7
                };
                Assert.IsTrue(msSession.SetEstimationAlgorithmConfig(user, cfg, out error), error?.Message);
            },
            (user, pSession, msSession) =>
            {
                var ms = msSession.ModelSystem;
                Assert.IsInstanceOfType<GeneticAlgorithmConfig>(ms.EstimationAlgorithmConfig);
                var loaded = (GeneticAlgorithmConfig)ms.EstimationAlgorithmConfig;
                Assert.AreEqual(100,  loaded.PopulationSize);
                Assert.AreEqual(250,  loaded.MaxGenerations);
                Assert.AreEqual(0.9,  loaded.CrossoverRate);
                Assert.AreEqual(0.02, loaded.MutationRate);
                Assert.AreEqual(4,    loaded.ElitismCount);
                Assert.AreEqual(5,    loaded.TournamentSize);
                Assert.AreEqual(1e-7, loaded.ConvergenceTolerance);
            });
    }

    [TestMethod]
    public void Estimation_ObjectiveMaximizePersistence()
    {
        TestHelper.RunInModelSystemContext("Estimation_ObjectiveMaximizePersistence",
            (user, pSession, msSession) =>
            {
                CommandError error = null;
                Assert.IsTrue(msSession.SetEstimationObjective(user, EstimationObjective.Maximize, out error), error?.Message);
            },
            (user, pSession, msSession) =>
            {
                Assert.AreEqual(EstimationObjective.Maximize, msSession.ModelSystem.EstimationObjective);
            });
    }

    [TestMethod]
    public void Estimation_AlgorithmConfig_SetConfig_Undo()
    {
        TestHelper.RunInModelSystemContext("Estimation_AlgorithmConfig_SetConfig_Undo",
            (user, pSession, msSession) =>
            {
                CommandError error = null;
                var original = msSession.ModelSystem.EstimationAlgorithmConfig;
                var pso = new ParticleSwarmConfig { SwarmSize = 10 };
                Assert.IsTrue(msSession.SetEstimationAlgorithmConfig(user, pso, out error), error?.Message);
                Assert.IsInstanceOfType<ParticleSwarmConfig>(msSession.ModelSystem.EstimationAlgorithmConfig);
                Assert.IsTrue(msSession.Undo(user, out error), error?.Message);
                Assert.AreEqual(original.AlgorithmId, msSession.ModelSystem.EstimationAlgorithmConfig.AlgorithmId);
            });
    }

    [TestMethod]
    public void Estimation_Objective_SetObjective_Undo()
    {
        TestHelper.RunInModelSystemContext("Estimation_Objective_SetObjective_Undo",
            (user, pSession, msSession) =>
            {
                CommandError error = null;
                Assert.AreEqual(EstimationObjective.Minimize, msSession.ModelSystem.EstimationObjective);
                Assert.IsTrue(msSession.SetEstimationObjective(user, EstimationObjective.Maximize, out error), error?.Message);
                Assert.AreEqual(EstimationObjective.Maximize, msSession.ModelSystem.EstimationObjective);
                Assert.IsTrue(msSession.Undo(user, out error), error?.Message);
                Assert.AreEqual(EstimationObjective.Minimize, msSession.ModelSystem.EstimationObjective);
            });
    }
}
