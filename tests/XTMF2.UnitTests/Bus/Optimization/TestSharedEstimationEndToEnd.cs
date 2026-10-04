using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Bus;
using XTMF2.Bus.Optimization;
using XTMF2.Editing;
using XTMF2.ModelSystemConstruct;
using XTMF2.RuntimeModules;
using XTMF2.UnitTests.Modules;
using static XTMF2.UnitTests.TestHelper;

namespace XTMF2.UnitTests.Bus.Optimization;

[TestClass]
public class TestSharedEstimationEndToEnd
{
    [TestMethod]
    public void LocalRunServerCoordinator_CanEvaluateAsItsOwnWorker()
    {
        RunInModelSystemContext(nameof(LocalRunServerCoordinator_CanEvaluateAsItsOwnWorker),
            (user, projectSession, session) =>
            {
                XTMF2.Editing.CommandError error = null;
                var modelSystem = session.ModelSystem;
                Assert.IsTrue(session.AddModelSystemStart(user, modelSystem.GlobalBoundary, "Start",
                    Rectangle.Hidden, out Start start, out error), error?.Message);
                Assert.IsTrue(session.AddNode(user, modelSystem.GlobalBoundary, "Ignore",
                    typeof(IgnoreResult<string>), Rectangle.Hidden, out var ignore, out error), error?.Message);
                Assert.IsTrue(session.AddNode(user, modelSystem.GlobalBoundary, "Action",
                    typeof(SimpleTestModule), Rectangle.Hidden, out var action, out error), error?.Message);
                Assert.IsTrue(session.AddLink(user, start, start.Hooks[0], ignore, out _, out error), error?.Message);
                Assert.IsTrue(session.AddLink(user, ignore, ignore.Hooks[0], action, out _, out error), error?.Message);
                Assert.IsTrue(session.AddNode(user, modelSystem.GlobalBoundary, "Fitness",
                    typeof(BasicParameter<float>), Rectangle.Hidden, out var fitness, out error), error?.Message);
                Assert.IsTrue(session.SetParameterValue(user, fitness, "2.5", out error), error?.Message);
                Assert.IsTrue(session.SetEstimationFitnessNode(user, fitness, out error), error?.Message);
                Assert.IsTrue(session.AddNode(user, modelSystem.GlobalBoundary, "Parameter",
                    typeof(SetableParameter<float>), Rectangle.Hidden, out var parameter, out error), error?.Message);
                Assert.IsTrue(session.SetParameterValue(user, parameter, "0.5", out error), error?.Message);
                Assert.IsTrue(session.AddEstimationGroup(user, "Group", out var group, out error), error?.Message);
                Assert.IsTrue(session.AddEstimationParameter(user, group!, parameter,
                    0.0, 1.0, 0.5, out _, out error), error?.Message);

                using var serialized = new MemoryStream();
                Assert.IsTrue(session.Save(out error, serialized), error?.Message);
                var runId = Guid.NewGuid().ToString("N");
                var runDirectory = Path.Combine(Path.GetTempPath(), "XTMF2-LocalCoordinator", runId);
                Directory.CreateDirectory(runDirectory);
                var config = modelSystem.EstimationAlgorithmConfig;
                var metadata = session.GetOptimizationParameterMeta(RunMode.Estimation);
                var request = new SharedEstimationCoordinatorRequest(
                    new SharedEstimationRunRequest(runId, runDirectory, "Start", serialized.ToArray(),
                        ProjectId: Guid.NewGuid(), ModelSystemId: Guid.NewGuid()),
                    Array.Empty<SharedEstimationWorkerEndpoint>(),
                    config.AlgorithmId,
                    config.GetParameters(),
                    metadata.Select(item => item.min).ToArray(),
                    metadata.Select(item => item.max).ToArray(),
                    metadata.Select(item => 0.5).ToArray(),
                    IsMaximize: false,
                    UseCoordinatorAsWorker: true,
                    Parameters: metadata.Select(item => new SharedEstimationParameterMetadata(
                        item.nodeIndex, item.name, item.min, item.max)).ToArray(),
                    CoordinatorConcurrentRuns: 2);

                try
                {
                    CreateRunClient(true, host =>
                    {
                        var statuses = new List<string>();
                        var progressUpdates = new List<SharedEstimationProgress>();
                        var completion = new TaskCompletionSource<SharedEstimationCompletion>(
                            TaskCreationOptions.RunContinuationsAsynchronously);
                        host.SharedEstimationStatusAvailable += (_, status) =>
                        {
                            if (status.RunId == runId)
                                statuses.Add(status.Message);
                        };
                        host.SharedEstimationProgressAvailable += (_, progress) =>
                        {
                            if (progress.RunId == runId)
                                progressUpdates.Add(progress);
                        };
                        host.SharedEstimationCompleted += (_, result) =>
                        {
                            if (result.RunId == runId)
                                completion.TrySetResult(result);
                        };
                        Assert.IsTrue(host.StartRemoteSharedEstimation(request, out var startError), startError?.Message);
                        Assert.IsTrue(completion.Task.Wait(TimeSpan.FromSeconds(30)),
                            $"The local coordinator did not report completion. Statuses: {string.Join(" | ", statuses)}; " +
                            $"progress updates: {progressUpdates.Count}.");
                        Assert.IsTrue(completion.Task.Result.Succeeded, completion.Task.Result.FailureReason);
                        Assert.AreEqual(2.5, completion.Task.Result.BestFitness, 0.0001);
                        Assert.IsTrue(progressUpdates.Any(progress => progress.ActiveWorkers == 2));
                        Assert.IsTrue(File.Exists(Path.Combine(runDirectory, "estimation_report.csv")));
                        Assert.IsFalse(Directory.Exists(Path.Combine(runDirectory,
                            ".xtmf-estimation-workers")), "Worker scratch directories should be removed before completion is reported.");
                    });
                }
                finally
                {
                    if (Directory.Exists(runDirectory))
                        Directory.Delete(runDirectory, recursive: true);
                }
            });
    }

    [TestMethod]
    public void RunServerActivityQuery_IncludesSharedEstimationWorkerAssignment()
    {
        RunInModelSystemContext(nameof(RunServerActivityQuery_IncludesSharedEstimationWorkerAssignment),
            (user, projectSession, session) =>
        {
            CommandError error = null;
            var modelSystem = session.ModelSystem;
            Assert.IsTrue(session.AddModelSystemStart(user, modelSystem.GlobalBoundary, "Start",
                Rectangle.Hidden, out Start start, out error), error?.Message);
            Assert.IsTrue(session.AddNode(user, modelSystem.GlobalBoundary, "Ignore",
                typeof(IgnoreResult<string>), Rectangle.Hidden, out var ignore, out error), error?.Message);
            Assert.IsTrue(session.AddNode(user, modelSystem.GlobalBoundary, "Action",
                typeof(SimpleTestModule), Rectangle.Hidden, out var action, out error), error?.Message);
            Assert.IsTrue(session.AddLink(user, start, start.Hooks[0], ignore, out _, out error), error?.Message);
            Assert.IsTrue(session.AddLink(user, ignore, ignore.Hooks[0], action, out _, out error), error?.Message);
            Assert.IsTrue(session.AddNode(user, modelSystem.GlobalBoundary, "Fitness",
                typeof(BasicParameter<float>), Rectangle.Hidden, out var fitness, out error), error?.Message);
            Assert.IsTrue(session.SetParameterValue(user, fitness, "2.5", out error), error?.Message);
            Assert.IsTrue(session.SetEstimationFitnessNode(user, fitness, out error), error?.Message);
            Assert.IsTrue(session.AddNode(user, modelSystem.GlobalBoundary, "Parameter",
                typeof(SetableParameter<float>), Rectangle.Hidden, out var parameter, out error), error?.Message);
            Assert.IsTrue(session.SetParameterValue(user, parameter, "0.5", out error), error?.Message);
            Assert.IsTrue(session.AddEstimationGroup(user, "Group", out var group, out error), error?.Message);
            Assert.IsTrue(session.AddEstimationParameter(user, group!, parameter,
                0.0, 1.0, 0.5, out _, out error), error?.Message);

            using var serialized = new MemoryStream();
            Assert.IsTrue(session.Save(out error, serialized), error?.Message);
            var workerRoot = Directory.CreateTempSubdirectory("xtmf-worker-cancel-");
            try
            {
                var request = new SharedEstimationRunRequest(
                    "worker-activity", workerRoot.FullName, "Start", serialized.ToArray());
                CreateRunClient(true, host =>
                {
                    var ready = new TaskCompletionSource<SharedEstimationWorkerReady>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    host.SharedEstimationWorkerReadyAvailable += (_, workerReady) =>
                    {
                        if (workerReady.RunId == request.RunId)
                            ready.TrySetResult(workerReady);
                    };
                    Assert.IsTrue(host.StartSharedEstimation(request, out var startError), startError?.Message);
                    Assert.IsTrue(ready.Task.Wait(TimeSpan.FromSeconds(10)), "The worker process did not become ready.");
                    Assert.IsTrue(ready.Task.Result.Succeeded, ready.Task.Result.Error);

                    var responses = Task.WhenAll(host.QueryServerActivityAsync(), host.QueryServerActivityAsync())
                        .GetAwaiter().GetResult();

                    Assert.AreNotEqual(responses[0].RequestId, responses[1].RequestId);
                    var activity = responses[0].Activities.Single(item => item.RunId == request.RunId);
                    Assert.AreEqual("Shared estimation worker", activity.Kind);
                    Assert.AreEqual(RunServerActivityState.Running, activity.State);
                    Assert.AreEqual("Shared estimation worker", activity.RunName);
                    Assert.IsTrue(host.CancelSharedEstimation(request.RunId, "test complete", out var cancelError),
                        cancelError?.Message);
                    Assert.IsTrue(SpinWait.SpinUntil(() => !Directory.Exists(Path.Combine(
                            workerRoot.FullName, ".xtmf-estimation-workers")),
                        TimeSpan.FromSeconds(5)), "Cancellation should delete worker scratch directories.");
                });
            }
            finally
            {
                workerRoot.Delete(true);
            }
        });
    }

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
                "shared-e2e", workerRoot.FullName, "Start", serialized.ToArray());
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
                        Assert.IsTrue(SpinWait.SpinUntil(() => pool.WorkerCount == 2,
                            TimeSpan.FromSeconds(10)), "Both RunServers should report readiness before evaluation starts.");

                        var algorithm = new FixedBatchAlgorithm();
                        var reportPath = Path.Combine(workerRoot.FullName, "estimation_report.csv");
                        var runner = new SharedEstimationCoordinatorRun(
                            request.RunId, algorithm, pool.Coordinator,
                            reportPath: reportPath, parameterNames: ["Fare, USD"]);
                        var progressUpdates = new List<SharedEstimationProgress>();
                        var completion = runner.Execute(progressUpdates.Add);

                        Assert.IsTrue(completion.Succeeded, completion.FailureReason);
                        Assert.AreEqual(4, completion.TotalEvaluations);
                        Assert.AreEqual(2.5, completion.BestFitness, 0.0001);
                        Assert.AreEqual(1, completion.Iterations);
                        Assert.IsGreaterThanOrEqualTo(4, progressUpdates.Count);
                        Assert.IsTrue(progressUpdates.All(progress => double.IsFinite(progress.BestFitness)));
                        Assert.AreEqual(2.5, progressUpdates[0].BestFitness, 0.0001);
                        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 },
                            progressUpdates.Take(4).Select(progress => progress.EvaluationsCompleted).ToArray());
                        var finalProgress = progressUpdates.Last();
                        Assert.AreEqual(4, finalProgress.EvaluationsByWorker.Values.Sum());
                        CollectionAssert.AreEquivalent(new[] { "worker-1", "worker-2" },
                            finalProgress.EvaluationsByWorker.Keys.ToArray());
                        CollectionAssert.AreEqual(new[] { 0.1 }, finalProgress.BestParameters.ToArray());
                        var reportLines = File.ReadAllLines(reportPath);
                        Assert.AreEqual("Iteration,Fitness,\"Fare, USD\"", reportLines[0]);
                        Assert.HasCount(5, reportLines);
                        Assert.IsTrue(reportLines.Skip(1).All(line => line.StartsWith("1,2.5,", StringComparison.Ordinal)));
                        CollectionAssert.AreEquivalent(new[] { "0.1", "0.2", "0.3", "0.4" },
                            reportLines.Skip(1).Select(line => line.Split(',')[2]).ToArray());
                        pool.Dispose();
                        Assert.IsTrue(File.Exists(reportPath), "Coordinator estimation output should remain in the run directory.");
                        Assert.IsTrue(SpinWait.SpinUntil(() => !Directory.Exists(Path.Combine(
                                workerRoot.FullName, ".xtmf-estimation-workers")),
                            TimeSpan.FromSeconds(5)), "Worker scratch directories should be deleted when workers terminate.");
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
