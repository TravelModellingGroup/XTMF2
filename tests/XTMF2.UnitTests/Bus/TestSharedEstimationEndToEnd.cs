using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Bus;
using XTMF2.Bus.Optimization;
using XTMF2.Editing;
using XTMF2.ModelSystemConstruct;
using XTMF2.RuntimeModules;
using XTMF2.UnitTests.Modules;
using static XTMF2.UnitTests.TestHelper;

namespace XTMF2.UnitTests.Bus;

[TestClass]
public class TestSharedEstimationEndToEnd
{
    [TestMethod]
    public void CoordinatorEvaluatesBatchAcrossTwoRunServers()
    {
        RunInModelSystemContext("SharedEstimationEndToEnd", (user, _, session) =>
        {
            CommandError error = null;
            var modelSystem = session.ModelSystem;
            Assert.IsTrue(session.AddModelSystemStart(user, modelSystem.GlobalBoundary, "Start",
                Rectangle.Hidden, out Start start, out error), error?.Message);
            Assert.IsTrue(session.AddNode(user, modelSystem.GlobalBoundary, "Ignore",
                typeof(IgnoreResult<string>), Rectangle.Hidden, out var ignore, out error), error?.Message);
            Assert.IsTrue(session.AddNodeGenerateParameters(user, modelSystem.GlobalBoundary, "Action",
                typeof(PathValidationModule), Rectangle.Hidden, out var action, out var actionParameters, out error), error?.Message);
            var pathParameter = actionParameters.Single(parameter => parameter.Name == "Path");
            var workerRoot = Directory.CreateTempSubdirectory("xtmf-shared-e2e-");
            var firstPath = Path.Combine(workerRoot.FullName, "worker-one.input");
            var secondPath = Path.Combine(workerRoot.FullName, "worker-two.input");
            File.WriteAllText(firstPath, "worker one");
            File.WriteAllText(secondPath, "worker two");
            Assert.IsTrue(session.SetParameterValue(user, pathParameter, "missing.input", out error), error?.Message);
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
            var pathNodeIndex = FindSerializedNodeIndex(serialized.ToArray(), "Path");
            var request = new SharedEstimationRunRequest(
                "shared-e2e", Directory.GetCurrentDirectory(), "Start", serialized.ToArray());
            var overrides = new Dictionary<string, IReadOnlyDictionary<int, string>>
            {
                ["worker-1"] = new Dictionary<int, string> { [pathNodeIndex] = firstPath },
                ["worker-2"] = new Dictionary<int, string> { [pathNodeIndex] = secondPath }
            };

            try
            {
                CreateRunClient(true, firstHost =>
                {
                    CreateRunClient(true, secondHost =>
                    {
                        using var pool = new SharedEstimationWorkerPool();
                        Assert.IsTrue(pool.AddExistingWorker("worker-1", "worker-1", firstHost, out var poolError), poolError);
                        Assert.IsTrue(pool.AddExistingWorker("worker-2", "worker-2", secondHost, out poolError), poolError);
                        Assert.IsTrue(pool.StartRun(request, out poolError, overrides), poolError);

                        var algorithm = new FixedBatchAlgorithm();
                        var runner = new SharedEstimationCoordinatorRun(
                            request.RunId, algorithm, pool.Coordinator);
                        var completion = runner.Execute();

                        Assert.IsTrue(completion.Succeeded, completion.FailureReason);
                        Assert.AreEqual(4, completion.TotalEvaluations);
                        Assert.AreEqual(2.5, completion.BestFitness, 0.0001);
                        Assert.AreEqual(1, completion.Iterations);
                    });
                });
            }
            finally
            {
                workerRoot.Delete(true);
            }
        });
    }

    private static int FindSerializedNodeIndex(byte[] modelSystem, string nodeName)
    {
        using var document = JsonDocument.Parse(modelSystem);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (TryFindNodeIndex(property.Value, nodeName, out var index))
                return index;
        }
        Assert.Fail($"Unable to find serialized node '{nodeName}'.");
        return -1;
    }

    private static bool TryFindNodeIndex(JsonElement element, string nodeName, out int index)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("Name", out var name)
                && name.GetString() == nodeName
                && element.TryGetProperty("Index", out var serializedIndex))
            {
                index = serializedIndex.GetInt32();
                return true;
            }
            foreach (var property in element.EnumerateObject())
            {
                if (TryFindNodeIndex(property.Value, nodeName, out index))
                    return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryFindNodeIndex(item, nodeName, out index))
                    return true;
            }
        }
        index = -1;
        return false;
    }

    private sealed class FixedBatchAlgorithm : IEstimationAlgorithm
    {
        private double[] _bestParameters = Array.Empty<double>();
        private double _bestFitness = double.MaxValue;

        public string Name => "Fixed batch test algorithm";
        public double[] BestParameters => _bestParameters;
        public double BestFitness => _bestFitness;

        public void Initialize(int dimensions, double[] lowerBounds, double[] upperBounds,
            double[] initialPoint, int maxIterations = 500, double convergenceTolerance = 1e-6,
            bool isMaximize = false)
        {
            _bestParameters = (double[])initialPoint.Clone();
        }

        public void Run(Func<double[], double> fitnessEvaluator,
            Action<int, double> progressCallback = null,
            Func<bool> shouldCancel = null)
            => RunBatch(fitnessEvaluator, candidates => candidates.Select(fitnessEvaluator).ToArray(),
                progressCallback, shouldCancel);

        public void RunBatch(Func<double[], double> fitnessEvaluator,
            Func<IReadOnlyList<double[]>, IReadOnlyList<double>> batchFitnessEvaluator,
            Action<int, double> progressCallback = null,
            Func<bool> shouldCancel = null)
        {
            var candidates = new[] { new[] { 0.1 }, new[] { 0.2 }, new[] { 0.3 }, new[] { 0.4 } };
            var fitnesses = batchFitnessEvaluator(candidates);
            var bestIndex = 0;
            for (int i = 1; i < fitnesses.Count; i++)
            {
                if (fitnesses[i] < fitnesses[bestIndex])
                    bestIndex = i;
            }
            _bestFitness = fitnesses[bestIndex];
            _bestParameters = (double[])candidates[bestIndex].Clone();
            progressCallback?.Invoke(1, _bestFitness);
        }
    }
}
