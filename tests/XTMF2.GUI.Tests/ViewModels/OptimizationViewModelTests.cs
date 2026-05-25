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
using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Editing;
using XTMF2.GUI.Tests.Modules;
using XTMF2.GUI.ViewModels;
using XTMF2.ModelSystemConstruct;

namespace XTMF2.GUI.Tests.ViewModels;

/// <summary>
/// Unit tests for the estimation- and calibration-related view-models:
/// <see cref="EstimationGroupViewModel"/>, <see cref="EstimationEntryViewModel"/>,
/// <see cref="CalibrationGroupViewModel"/>, and <see cref="CalibrationEntryViewModel"/>.
/// </summary>
[TestClass]
public class OptimizationViewModelTests
{
    // ── EstimationGroupViewModel ──────────────────────────────────────────────

    [TestMethod]
    public void EstimationGroupVm_InitialisesFromGroup()
    {
        TestGuiHelper.RunInModelSystemContext("EstimGroupVm_Init", (user, _, msSession) =>
        {
            CommandError? error = null;
            Assert.IsTrue(msSession.AddEstimationGroup(user, "Alpha", out var group, out error), error?.Message);

            var vm = new EstimationGroupViewModel(group!);

            Assert.AreEqual("Alpha", vm.Name);
            Assert.IsTrue(vm.IsEnabled);
            Assert.IsEmpty(vm.Parameters);
        });
    }

    [TestMethod]
    public void EstimationGroupVm_NameSyncsFromModel()
    {
        TestGuiHelper.RunInModelSystemContext("EstimGroupVm_NameSync", (user, _, msSession) =>
        {
            CommandError? error = null;
            Assert.IsTrue(msSession.AddEstimationGroup(user, "Before", out var group, out error), error?.Message);
            var vm = new EstimationGroupViewModel(group!);

            Assert.IsTrue(msSession.RenameEstimationGroup(user, group!, "After", out error), error?.Message);

            Assert.AreEqual("After", vm.Name);
        });
    }

    [TestMethod]
    public void EstimationGroupVm_IsEnabledSyncsFromModel()
    {
        TestGuiHelper.RunInModelSystemContext("EstimGroupVm_EnabledSync", (user, _, msSession) =>
        {
            CommandError? error = null;
            Assert.IsTrue(msSession.AddEstimationGroup(user, "G", out var group, out error), error?.Message);
            var vm = new EstimationGroupViewModel(group!);

            Assert.IsTrue(msSession.SetEstimationGroupEnabled(user, group!, false, out error), error?.Message);

            Assert.IsFalse(vm.IsEnabled);
        });
    }

    [TestMethod]
    public void EstimationGroupVm_ParametersCollectionSyncsOnAdd()
    {
        TestGuiHelper.RunInModelSystemContext("EstimGroupVm_ParamsSync", (user, _, msSession) =>
        {
            CommandError? error = null;
            var ms = msSession.ModelSystem;
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "P1",
                typeof(SimpleGuiTestModule), Rectangle.Hidden, out var node, out error), error?.Message);
            Assert.IsTrue(msSession.AddEstimationGroup(user, "G", out var group, out error), error?.Message);
            var vm = new EstimationGroupViewModel(group!);

            Assert.IsTrue(msSession.AddEstimationParameter(user, group!, node!, 0.0, 1.0, 0.5,
                out var unusedEstEntry, out error), error?.Message);

            Assert.HasCount(1, vm.Parameters);
            Assert.AreEqual("P1", vm.Parameters[0].NodeName);
        });
    }

    [TestMethod]
    public void EstimationGroupVm_ParametersCollectionSyncsOnRemove()
    {
        TestGuiHelper.RunInModelSystemContext("EstimGroupVm_ParamsRemove", (user, _, msSession) =>
        {
            CommandError? error = null;
            var ms = msSession.ModelSystem;
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "P1",
                typeof(SimpleGuiTestModule), Rectangle.Hidden, out var node, out error), error?.Message);
            Assert.IsTrue(msSession.AddEstimationGroup(user, "G", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.AddEstimationParameter(user, group!, node!, 0.0, 1.0, 0.5,
                out var entry, out error), error?.Message);
            var vm = new EstimationGroupViewModel(group!);

            Assert.IsTrue(msSession.RemoveEstimationParameter(user, group!, entry!, out error), error?.Message);

            Assert.IsEmpty(vm.Parameters);
        });
    }

    [TestMethod]
    public void EstimationGroupVm_DetachStopsSync()
    {
        TestGuiHelper.RunInModelSystemContext("EstimGroupVm_Detach", (user, _, msSession) =>
        {
            CommandError? error = null;
            Assert.IsTrue(msSession.AddEstimationGroup(user, "G", out var group, out error), error?.Message);
            var vm = new EstimationGroupViewModel(group!);
            vm.Detach();

            Assert.IsTrue(msSession.RenameEstimationGroup(user, group!, "Renamed", out error), error?.Message);

            // After detach the VM should not have updated
            Assert.AreEqual("G", vm.Name);
        });
    }

    // ── EstimationEntryViewModel ──────────────────────────────────────────────

    [TestMethod]
    public void EstimationEntryVm_InitialisesFromEntry()
    {
        TestGuiHelper.RunInModelSystemContext("EstimEntryVm_Init", (user, _, msSession) =>
        {
            CommandError? error = null;
            var ms = msSession.ModelSystem;
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "MyParam",
                typeof(SimpleGuiTestModule), Rectangle.Hidden, out var node, out error), error?.Message);
            Assert.IsTrue(msSession.AddEstimationGroup(user, "G", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.AddEstimationParameter(user, group!, node!, -1.0, 2.0, 0.5,
                out var entry, out error), error?.Message);

            var vm = new EstimationEntryViewModel(entry!);

            Assert.AreEqual("MyParam", vm.NodeName);
            Assert.AreEqual(-1.0, vm.Min);
            Assert.AreEqual(2.0,  vm.Max);
            Assert.AreEqual(0.5,  vm.NullHypothesis);
            Assert.IsTrue(vm.IsEnabled);
            Assert.AreEqual((-1.0).ToString("G6", CultureInfo.InvariantCulture), vm.EditMin);
            Assert.AreEqual(2.0.ToString("G6",   CultureInfo.InvariantCulture), vm.EditMax);
            Assert.AreEqual(0.5.ToString("G6",   CultureInfo.InvariantCulture), vm.EditNullHypothesis);
        });
    }

    [TestMethod]
    public void EstimationEntryVm_SyncsOnModelUpdate()
    {
        TestGuiHelper.RunInModelSystemContext("EstimEntryVm_Sync", (user, _, msSession) =>
        {
            CommandError? error = null;
            var ms = msSession.ModelSystem;
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "P",
                typeof(SimpleGuiTestModule), Rectangle.Hidden, out var node, out error), error?.Message);
            Assert.IsTrue(msSession.AddEstimationGroup(user, "G", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.AddEstimationParameter(user, group!, node!, 0.0, 1.0, 0.0,
                out var entry, out error), error?.Message);
            var vm = new EstimationEntryViewModel(entry!);

            Assert.IsTrue(msSession.UpdateEstimationParameter(user, entry!, 3.0, 7.0, 4.0, false, out error),
                error?.Message);

            Assert.AreEqual(3.0, vm.Min);
            Assert.AreEqual(7.0, vm.Max);
            Assert.AreEqual(4.0, vm.NullHypothesis);
            Assert.IsFalse(vm.IsEnabled);
            Assert.AreEqual(3.0.ToString("G6", CultureInfo.InvariantCulture), vm.EditMin);
            Assert.AreEqual(7.0.ToString("G6", CultureInfo.InvariantCulture), vm.EditMax);
            Assert.AreEqual(4.0.ToString("G6", CultureInfo.InvariantCulture), vm.EditNullHypothesis);
        });
    }

    // ── CalibrationGroupViewModel ─────────────────────────────────────────────

    [TestMethod]
    public void CalibrationGroupVm_InitialisesFromGroup()
    {
        TestGuiHelper.RunInModelSystemContext("CalibGroupVm_Init", (user, _, msSession) =>
        {
            CommandError? error = null;
            Assert.IsTrue(msSession.AddCalibrationGroup(user, "Beta", out var group, out error), error?.Message);

            var vm = new CalibrationGroupViewModel(group!);

            Assert.AreEqual("Beta", vm.Name);
            Assert.IsTrue(vm.IsEnabled);
            Assert.IsEmpty(vm.Parameters);
            Assert.AreEqual(new XTMF2.Bus.Optimization.LogOddsAlgorithm(), vm.DefaultAlgorithm);
        });
    }

    [TestMethod]
    public void CalibrationGroupVm_DefaultAlgorithmSyncsFromModel()
    {
        TestGuiHelper.RunInModelSystemContext("CalibGroupVm_DefaultAlgorithmSync", (user, _, msSession) =>
        {
            CommandError? error = null;
            Assert.IsTrue(msSession.AddCalibrationGroup(user, "CG", out var group, out error), error?.Message);
            var vm = new CalibrationGroupViewModel(group!);

            Assert.IsTrue(msSession.SetCalibrationGroupDefaultAlgorithm(
                user, group!, new XTMF2.Bus.Optimization.LogOddsAlgorithm(), out error), error?.Message);

            Assert.AreEqual(new XTMF2.Bus.Optimization.LogOddsAlgorithm(), vm.DefaultAlgorithm);
        });
    }

    [TestMethod]
    public void CalibrationGroupVm_NameSyncsFromModel()
    {
        TestGuiHelper.RunInModelSystemContext("CalibGroupVm_NameSync", (user, _, msSession) =>
        {
            CommandError? error = null;
            Assert.IsTrue(msSession.AddCalibrationGroup(user, "Before", out var group, out error), error?.Message);
            var vm = new CalibrationGroupViewModel(group!);

            Assert.IsTrue(msSession.RenameCalibrationGroup(user, group!, "After", out error), error?.Message);

            Assert.AreEqual("After", vm.Name);
        });
    }

    [TestMethod]
    public void CalibrationGroupVm_IsEnabledSyncsFromModel()
    {
        TestGuiHelper.RunInModelSystemContext("CalibGroupVm_EnabledSync", (user, _, msSession) =>
        {
            CommandError? error = null;
            Assert.IsTrue(msSession.AddCalibrationGroup(user, "CG", out var group, out error), error?.Message);
            var vm = new CalibrationGroupViewModel(group!);

            Assert.IsTrue(msSession.SetCalibrationGroupEnabled(user, group!, false, out error), error?.Message);

            Assert.IsFalse(vm.IsEnabled);
        });
    }

    [TestMethod]
    public void CalibrationGroupVm_ParametersCollectionSyncsOnAdd()
    {
        TestGuiHelper.RunInModelSystemContext("CalibGroupVm_ParamsSync", (user, _, msSession) =>
        {
            CommandError? error = null;
            var ms = msSession.ModelSystem;
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "CP1",
                typeof(SimpleGuiTestModule), Rectangle.Hidden, out var node, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationGroup(user, "CG", out var group, out error), error?.Message);
            var vm = new CalibrationGroupViewModel(group!);

            Assert.IsTrue(msSession.AddCalibrationParameter(user, group!, node!, 0.0, 1.0,
                out var unusedCalEntry, out error), error?.Message);

            Assert.HasCount(1, vm.Parameters);
            Assert.AreEqual("CP1", vm.Parameters[0].NodeName);
        });
    }

    [TestMethod]
    public void CalibrationGroupVm_ParametersCollectionSyncsOnRemove()
    {
        TestGuiHelper.RunInModelSystemContext("CalibGroupVm_ParamsRemove", (user, _, msSession) =>
        {
            CommandError? error = null;
            var ms = msSession.ModelSystem;
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "CP1",
                typeof(SimpleGuiTestModule), Rectangle.Hidden, out var node, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationGroup(user, "CG", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationParameter(user, group!, node!, 0.0, 1.0,
                out var entry, out error), error?.Message);
            var vm = new CalibrationGroupViewModel(group!);

            Assert.IsTrue(msSession.RemoveCalibrationParameter(user, group!, entry!, out error), error?.Message);

            Assert.IsEmpty(vm.Parameters);
        });
    }

    [TestMethod]
    public void CalibrationGroupVm_DetachStopsSync()
    {
        TestGuiHelper.RunInModelSystemContext("CalibGroupVm_Detach", (user, _, msSession) =>
        {
            CommandError? error = null;
            Assert.IsTrue(msSession.AddCalibrationGroup(user, "CG", out var group, out error), error?.Message);
            var vm = new CalibrationGroupViewModel(group!);
            vm.Detach();

            Assert.IsTrue(msSession.RenameCalibrationGroup(user, group!, "Renamed", out error), error?.Message);

            // After detach the VM should not have updated
            Assert.AreEqual("CG", vm.Name);
        });
    }

    // ── CalibrationEntryViewModel ─────────────────────────────────────────────

    [TestMethod]
    public void CalibrationEntryVm_InitialisesFromEntry()
    {
        TestGuiHelper.RunInModelSystemContext("CalibEntryVm_Init", (user, _, msSession) =>
        {
            CommandError? error = null;
            var ms = msSession.ModelSystem;
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "MyCalibParam",
                typeof(SimpleGuiTestModule), Rectangle.Hidden, out var node, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationGroup(user, "CG", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationParameter(user, group!, node!, -2.0, 4.0,
                out var entry, out error), error?.Message);

            var vm = new CalibrationEntryViewModel(entry!);

            Assert.AreEqual("MyCalibParam", vm.NodeName);
            Assert.AreEqual(-2.0, vm.Min);
            Assert.AreEqual(4.0,  vm.Max);
            Assert.IsTrue(vm.IsEnabled);
            Assert.AreEqual("(none)", vm.ModelOutputNodeName);
            Assert.AreEqual("(none)", vm.TargetOutputNodeName);
            Assert.AreEqual((-2.0).ToString("G6", CultureInfo.InvariantCulture), vm.EditMin);
            Assert.AreEqual(4.0.ToString("G6",   CultureInfo.InvariantCulture), vm.EditMax);
            Assert.AreEqual(1.0, vm.StepSize);
            Assert.AreEqual(1.0.ToString("G4", CultureInfo.InvariantCulture), vm.EditStepSize);
        });
    }

    [TestMethod]
    public void CalibrationEntryVm_SyncsOnModelUpdate()
    {
        TestGuiHelper.RunInModelSystemContext("CalibEntryVm_Sync", (user, _, msSession) =>
        {
            CommandError? error = null;
            var ms = msSession.ModelSystem;
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "CP",
                typeof(SimpleGuiTestModule), Rectangle.Hidden, out var node, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationGroup(user, "CG", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationParameter(user, group!, node!, 0.0, 1.0,
                out var entry, out error), error?.Message);
            var vm = new CalibrationEntryViewModel(entry!);

            Assert.IsTrue(msSession.UpdateCalibrationParameter(user, entry!, 5.0, 9.0, false, 2e-3, 0.3, new XTMF2.Bus.Optimization.ProportionalScalingAlgorithm(), out error),
                error?.Message);

            Assert.AreEqual(5.0, vm.Min);
            Assert.AreEqual(9.0, vm.Max);
            Assert.IsFalse(vm.IsEnabled);
            Assert.AreEqual(2e-3, vm.ErrorTolerance);
            Assert.AreEqual(0.3, vm.StepSize);
            Assert.AreEqual(new XTMF2.Bus.Optimization.ProportionalScalingAlgorithm(), vm.Algorithm);
            Assert.AreEqual(5.0.ToString("G6", CultureInfo.InvariantCulture), vm.EditMin);
            Assert.AreEqual(9.0.ToString("G6", CultureInfo.InvariantCulture), vm.EditMax);
            Assert.AreEqual((2e-3).ToString("G4", CultureInfo.InvariantCulture), vm.EditErrorTolerance);
            Assert.AreEqual(0.3.ToString("G4", CultureInfo.InvariantCulture), vm.EditStepSize);
        });
    }

    [TestMethod]
    public void CalibrationEntryVm_ModelOutputNodeNameSyncsOnSet()
    {
        TestGuiHelper.RunInModelSystemContext("CalibEntryVm_ModelOutputNodeSync", (user, _, msSession) =>
        {
            CommandError? error = null;
            var ms = msSession.ModelSystem;
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Param",
                typeof(SimpleGuiTestModule), Rectangle.Hidden, out var paramNode, out error), error?.Message);
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "ModelFn",
                typeof(SimpleGuiTestModule), Rectangle.Hidden, out var modelNode, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationGroup(user, "CG", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationParameter(user, group!, paramNode!, 0.0, 1.0,
                out var entry, out error), error?.Message);
            var vm = new CalibrationEntryViewModel(entry!);

            Assert.IsTrue(msSession.SetCalibrationEntryModelOutputNode(user, entry!, modelNode, out error),
                error?.Message);

            Assert.AreEqual("ModelFn", vm.ModelOutputNodeName);
        });
    }

    [TestMethod]
    public void CalibrationEntryVm_TargetOutputNodeNameSyncsOnSet()
    {
        TestGuiHelper.RunInModelSystemContext("CalibEntryVm_TargetOutputNodeSync", (user, _, msSession) =>
        {
            CommandError? error = null;
            var ms = msSession.ModelSystem;
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Param",
                typeof(SimpleGuiTestModule), Rectangle.Hidden, out var paramNode, out error), error?.Message);
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "TargetFn",
                typeof(SimpleGuiTestModule), Rectangle.Hidden, out var targetNode, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationGroup(user, "CG", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationParameter(user, group!, paramNode!, 0.0, 1.0,
                out var entry, out error), error?.Message);
            var vm = new CalibrationEntryViewModel(entry!);

            Assert.IsTrue(msSession.SetCalibrationEntryTargetOutputNode(user, entry!, targetNode, out error),
                error?.Message);

            Assert.AreEqual("TargetFn", vm.TargetOutputNodeName);
        });
    }

    [TestMethod]
    public void CalibrationEntryVm_TargetOutputNodeNameNoneOnClear()
    {
        TestGuiHelper.RunInModelSystemContext("CalibEntryVm_TargetOutputNodeClear", (user, _, msSession) =>
        {
            CommandError? error = null;
            var ms = msSession.ModelSystem;
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "Param",
                typeof(SimpleGuiTestModule), Rectangle.Hidden, out var paramNode, out error), error?.Message);
            Assert.IsTrue(msSession.AddNode(user, ms.GlobalBoundary, "TargetFn",
                typeof(SimpleGuiTestModule), Rectangle.Hidden, out var targetNode, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationGroup(user, "CG", out var group, out error), error?.Message);
            Assert.IsTrue(msSession.AddCalibrationParameter(user, group!, paramNode!, 0.0, 1.0,
                out var entry, out error), error?.Message);
            var vm = new CalibrationEntryViewModel(entry!);
            Assert.IsTrue(msSession.SetCalibrationEntryTargetOutputNode(user, entry!, targetNode, out error),
                error?.Message);

            Assert.IsTrue(msSession.SetCalibrationEntryTargetOutputNode(user, entry!, null, out error), error?.Message);

            Assert.AreEqual("(none)", vm.TargetOutputNodeName);
        });
    }
}
