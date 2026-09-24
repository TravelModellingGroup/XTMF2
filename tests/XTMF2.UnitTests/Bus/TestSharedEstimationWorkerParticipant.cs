using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Bus;
using XTMF2.ModelSystemConstruct;
using XTMF2.RuntimeModules;
using XTMF2.UnitTests.Modules;
using static XTMF2.UnitTests.TestHelper;

namespace XTMF2.UnitTests.Bus;

[TestClass]
public class TestSharedEstimationWorkerParticipant
{
    [TestMethod]
    public void Evaluate_UsesPreparedModelSystemAndReturnsFitness()
    {
        RunInModelSystemContext("SharedEstimationWorkerParticipant", (user, projectSession, session) =>
        {
            XTMF2.Editing.CommandError error = null;
            var modelSystem = session.ModelSystem;
            Assert.IsTrue(session.AddModelSystemStart(user, modelSystem.GlobalBoundary, "Start",
                Rectangle.Hidden, out Start start, out error), error?.Message);
            Assert.IsTrue(session.AddNode(user, modelSystem.GlobalBoundary, "Ignore",
                typeof(IgnoreResult<string>), Rectangle.Hidden, out var ignore, out error), error?.Message);
            Assert.IsTrue(session.AddNode(user, modelSystem.GlobalBoundary, "Action",
                typeof(SimpleTestModule), Rectangle.Hidden, out var action, out error), error?.Message);
            Assert.IsTrue(session.AddLink(user, start, start.Hooks[0], ignore, out var firstLink, out error), error?.Message);
            Assert.IsTrue(session.AddLink(user, ignore, ignore.Hooks[0], action, out var secondLink, out error), error?.Message);

            Assert.IsTrue(session.AddNode(user, modelSystem.GlobalBoundary, "Fitness",
                typeof(BasicParameter<float>), Rectangle.Hidden, out var fitness, out error), error?.Message);
            Assert.IsTrue(session.SetParameterValue(user, fitness, "2.5", out error), error?.Message);
            Assert.IsTrue(session.SetEstimationFitnessNode(user, fitness, out error), error?.Message);
            Assert.IsTrue(session.AddNode(user, modelSystem.GlobalBoundary, "Parameter",
                typeof(SetableParameter<float>), Rectangle.Hidden, out var parameter, out error), error?.Message);
            Assert.IsTrue(session.SetParameterValue(user, parameter, "0.5", out error), error?.Message);
            Assert.IsTrue(session.AddEstimationGroup(user, "Group", out var group, out error), error?.Message);
            Assert.IsTrue(session.AddEstimationParameter(user, group!, parameter,
                0.0, 1.0, 0.5, out var entry, out error), error?.Message);

            using var serialized = new MemoryStream();
            Assert.IsTrue(session.Save(out error, serialized), error?.Message);
            var request = new SharedEstimationRunRequest(
                "shared-run", Directory.GetCurrentDirectory(), "Start", serialized.ToArray());

            Assert.IsTrue(SharedEstimationWorkerParticipant.TryCreate(
                CreateRuntime(), request, out var participant, out var runError), runError?.Message);
            var result = participant!.Evaluate(new SharedEstimationCandidate(
                "shared-run", 1, "candidate-1", new[] { 0.75 }));

            Assert.AreEqual("candidate-1", result.CandidateId);
            Assert.AreEqual(2.5, result.Fitness, 0.0001);
            Assert.IsNull(result.Error);
        });
    }
}
