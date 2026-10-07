using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Bus;
using XTMF2.Bus.Optimization;

namespace XTMF2.UnitTests.Bus.Optimization;

[TestClass]
public class TestSharedEstimationProtocol
{
    [TestMethod]
    public void CoordinatorRequest_RoundTrips()
    {
        var projectId = Guid.NewGuid();
        var modelSystemId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var request = new SharedEstimationCoordinatorRequest(
            new SharedEstimationRunRequest("run-remote", "/remote/runs", "Start", [1, 2, 3],
            ProjectId: projectId, ModelSystemId: modelSystemId, OwnerUserId: ownerUserId, WorkerSlotCount: 3),
            [new SharedEstimationWorkerEndpoint("worker-1", "endpoint-1", "worker.example", 5000,
                "token", "fingerprint", new Dictionary<int, string> { [7] = "/worker/input" }, ConcurrentRuns: 3)],
            "NelderMead",
            [new AlgorithmParameterDescriptor { Key = "MaxIterations", Label = "Max Iterations", Hint = "limit", Value = "12" }],
            [0.0], [1.0], [0.5], false, UseCoordinatorAsWorker: false,
            Parameters: [new SharedEstimationParameterMetadata(7, "Demand", 0.0, 1.0)],
            CoordinatorConcurrentRuns: 2);

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            SharedEstimationProtocol.WriteCoordinatorRequest(writer, request);

        stream.Position = 0;
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
        var read = SharedEstimationProtocol.ReadCoordinatorRequest(reader);
        Assert.AreEqual(request.Run.RunId, read.Run.RunId);
        Assert.AreEqual(request.Workers[0].EndpointId, read.Workers[0].EndpointId);
        Assert.AreEqual(request.Workers[0].Address, read.Workers[0].Address);
        Assert.AreEqual(request.Workers[0].Port, read.Workers[0].Port);
        Assert.AreEqual(3, read.Workers[0].ConcurrentRuns);
        Assert.AreEqual(request.Workers[0].BasicParameterOverrides![7],
            read.Workers[0].BasicParameterOverrides![7]);
        Assert.AreEqual(request.AlgorithmId, read.AlgorithmId);
        Assert.AreEqual(request.AlgorithmParameters[0].Value, read.AlgorithmParameters[0].Value);
        Assert.AreEqual(request.InitialValues[0], read.InitialValues[0]);
        Assert.AreEqual(request.UseCoordinatorAsWorker, read.UseCoordinatorAsWorker);
        Assert.AreEqual(2, read.CoordinatorConcurrentRuns);
        Assert.AreEqual(request.Parameters![0], read.Parameters![0]);
        Assert.AreEqual(projectId, read.Run.ProjectId);
        Assert.AreEqual(modelSystemId, read.Run.ModelSystemId);
        Assert.AreEqual(ownerUserId, read.Run.OwnerUserId);
        Assert.AreEqual(3, read.Run.WorkerSlotCount);
        Assert.AreEqual(stream.Length, stream.Position);
    }

    [TestMethod]
    public void ServerActivityQueryAndSnapshot_RoundTrip()
    {
        var activities = new[]
        {
            new RunServerActivity("run-queued", "Queued model", "Normal",
                RunServerActivityState.Queued, "Waiting", QueuePosition: 2),
            new RunServerActivity("run-estimation", "Shared estimation", "Shared estimation",
                RunServerActivityState.Running, "Evaluating", Iteration: 4, Fitness: 0.25,
                ActiveWorkers: 3, EvaluationsCompleted: 12, EvaluationsPending: 2)
        };

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            SharedEstimationProtocol.WriteQueryServerActivity(writer, "request-17");
            SharedEstimationProtocol.WriteServerActivitySnapshots(writer, "request-17", activities);
        }

        stream.Position = 0;
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
        Assert.AreEqual("request-17", SharedEstimationProtocol.ReadQueryServerActivity(reader));
        var response = SharedEstimationProtocol.ReadServerActivitySnapshots(reader);
        Assert.AreEqual("request-17", response.RequestId);
        CollectionAssert.AreEqual(activities, response.Activities.ToArray());
    }

    [TestMethod]
    public void ServerActivitySnapshot_RejectsNegativeCounters()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            SharedEstimationProtocol.WriteHeader(writer, SharedEstimationMessageType.ServerActivitySnapshots);
            writer.Write("request-1");
            writer.Write(1);
            writer.Write("run-1");
            writer.Write("Run");
            writer.Write("Normal");
            writer.Write((int)RunServerActivityState.Queued);
            writer.Write("Waiting");
            writer.Write(-1);
            writer.Write(0);
            writer.Write(double.NaN);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
        }

        stream.Position = 0;
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
        Assert.Throws<InvalidDataException>(() => SharedEstimationProtocol.ReadServerActivitySnapshots(reader));
    }

    [TestMethod]
    public void RunAndWorkerMessages_RoundTrip()
    {
        var run = new SharedEstimationRunRequest(
            "run-1", "/shared/runs", "Start", new byte[] { 1, 2, 3 },
            new Dictionary<int, string> { [4] = "/worker/input", [9] = "/worker/output" },
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), WorkerSlotCount: 4);
        var registration = new SharedEstimationWorkerRegistration("run-1", "worker-1", "server-1");

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            SharedEstimationProtocol.WriteRunRequest(writer, run);
            SharedEstimationProtocol.WriteWorkerRegistration(writer, registration, remove: false);
            SharedEstimationProtocol.WriteWorkerRegistration(writer, registration, remove: true);
        }

        stream.Position = 0;
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
        var readRun = SharedEstimationProtocol.ReadRunRequest(reader);
        Assert.AreEqual(run.RunId, readRun.RunId);
        Assert.AreEqual(run.WorkingDirectory, readRun.WorkingDirectory);
        Assert.AreEqual(run.StartToExecute, readRun.StartToExecute);
        Assert.AreEqual(run.ProjectId, readRun.ProjectId);
        Assert.AreEqual(run.ModelSystemId, readRun.ModelSystemId);
        Assert.AreEqual(run.OwnerUserId, readRun.OwnerUserId);
        Assert.AreEqual(run.WorkerSlotCount, readRun.WorkerSlotCount);
        Assert.HasCount(run.ModelSystem.Length, readRun.ModelSystem);
        for (int i = 0; i < run.ModelSystem.Length; i++)
            Assert.AreEqual(run.ModelSystem[i], readRun.ModelSystem[i]);
        Assert.IsNotNull(readRun.BasicParameterOverrides);
        Assert.AreEqual(run.BasicParameterOverrides![4], readRun.BasicParameterOverrides[4]);
        Assert.AreEqual(run.BasicParameterOverrides[9], readRun.BasicParameterOverrides[9]);
        Assert.AreEqual(registration, SharedEstimationProtocol.ReadWorkerRegistration(reader, remove: false));
        Assert.AreEqual(registration, SharedEstimationProtocol.ReadWorkerRegistration(reader, remove: true));
    }

    [TestMethod]
    public void CandidateResultProgressAndCompletionMessages_RoundTrip()
    {
        var candidate = new SharedEstimationCandidate("run-1", 4, "candidate-1", new[] { 1.0, 2.0 });
        var result = new SharedEstimationEvaluationResult("run-1", 4, "candidate-1", 0.25,
            "worker failed", "Module", Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var progress = new SharedEstimationProgress("run-1", 2, 0.25, 3, 1, 2, 2,
            new Dictionary<string, int> { ["coordinator"] = 1, ["worker-1"] = 2 },
            new[] { 1.0, 2.0 }, ["worker-1", "worker-1#run-2"]);
        var status = new SharedEstimationStatus("run-1", "Estimation started with 2 workers.");
        var completion = new SharedEstimationCompletion("run-1", true, 0.25,
            new[] { 1.0, 2.0 }, 3, 2, null);

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            SharedEstimationProtocol.WriteCandidate(writer, candidate);
            SharedEstimationProtocol.WriteResult(writer, result);
            SharedEstimationProtocol.WriteProgress(writer, progress);
            SharedEstimationProtocol.WriteStatus(writer, status);
            SharedEstimationProtocol.WriteCompletion(writer, completion);
            SharedEstimationProtocol.WriteCancel(writer, "run-1", "user requested cancellation");
        }

        stream.Position = 0;
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
        var readCandidate = SharedEstimationProtocol.ReadCandidate(reader);
        Assert.AreEqual(candidate.RunId, readCandidate.RunId);
        Assert.AreEqual(candidate.BatchId, readCandidate.BatchId);
        Assert.AreEqual(candidate.CandidateId, readCandidate.CandidateId);
        CollectionAssert.AreEqual(candidate.Parameters.ToArray(), readCandidate.Parameters.ToArray());
        Assert.AreEqual(result, SharedEstimationProtocol.ReadResult(reader));

        var readProgress = SharedEstimationProtocol.ReadProgress(reader);
        Assert.AreEqual(progress.RunId, readProgress.RunId);
        Assert.AreEqual(progress.Iteration, readProgress.Iteration);
        Assert.AreEqual(progress.BestFitness, readProgress.BestFitness);
        Assert.AreEqual(progress.EvaluationsCompleted, readProgress.EvaluationsCompleted);
        Assert.AreEqual(progress.EvaluationsPending, readProgress.EvaluationsPending);
        Assert.AreEqual(progress.ActiveWorkers, readProgress.ActiveWorkers);
        Assert.AreEqual(progress.FitnessTestsThisIteration, readProgress.FitnessTestsThisIteration);
        CollectionAssert.AreEquivalent(progress.EvaluationsByWorker.ToArray(),
            readProgress.EvaluationsByWorker.ToArray());
        CollectionAssert.AreEqual(progress.BestParameters.ToArray(), readProgress.BestParameters.ToArray());
        CollectionAssert.AreEquivalent(progress.ActiveWorkerIds.ToArray(), readProgress.ActiveWorkerIds.ToArray());
        Assert.AreEqual(status, SharedEstimationProtocol.ReadStatus(reader));
        var readCompletion = SharedEstimationProtocol.ReadCompletion(reader);
        Assert.AreEqual(completion.RunId, readCompletion.RunId);
        Assert.AreEqual(completion.Succeeded, readCompletion.Succeeded);
        Assert.AreEqual(completion.BestFitness, readCompletion.BestFitness);
        Assert.HasCount(completion.BestParameters.Count, readCompletion.BestParameters);
        for (int i = 0; i < completion.BestParameters.Count; i++)
            Assert.AreEqual(completion.BestParameters[i], readCompletion.BestParameters[i]);
        Assert.AreEqual(completion.TotalEvaluations, readCompletion.TotalEvaluations);
        Assert.AreEqual(completion.Iterations, readCompletion.Iterations);
        Assert.AreEqual(completion.FailureReason, readCompletion.FailureReason);
        Assert.AreEqual(("run-1", "user requested cancellation"), SharedEstimationProtocol.ReadCancel(reader));
    }

    [TestMethod]
    public void WorkerReadyMessage_RoundTripsSuccessAndFailure()
    {
        var readiness = new[]
        {
            new SharedEstimationWorkerReady("run-ready", true, null),
            new SharedEstimationWorkerReady("run-failed", false, "Model preparation failed.")
        };
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            foreach (var item in readiness)
                SharedEstimationProtocol.WriteWorkerReady(writer, item);
        }

        stream.Position = 0;
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
        foreach (var item in readiness)
            Assert.AreEqual(item, SharedEstimationProtocol.ReadWorkerReady(reader));
        Assert.AreEqual(stream.Length, stream.Position);
    }

    [TestMethod]
    public void RunRequestWithoutOverrides_RoundTripsAsVersionOnePayload()
    {
        var run = new SharedEstimationRunRequest("run-legacy", "/shared/runs", "Start", new byte[] { 4, 5 });
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            SharedEstimationProtocol.WriteRunRequest(writer, run);

        stream.Position = 0;
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
        var readRun = SharedEstimationProtocol.ReadRunRequest(reader);

        Assert.AreEqual(run.RunId, readRun.RunId);
        Assert.IsNull(readRun.BasicParameterOverrides);
    }

    [TestMethod]
    public void Header_RejectsUnsupportedVersionAndUnknownMessage()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            writer.Write(SharedEstimationProtocol.Version + 1);
            writer.Write((int)SharedEstimationMessageType.StartRun);
        }

        stream.Position = 0;
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
        Assert.Throws<InvalidDataException>(() => SharedEstimationProtocol.ReadHeader(reader));

        stream.SetLength(0);
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            writer.Write(SharedEstimationProtocol.Version);
            writer.Write(int.MaxValue);
        }

        stream.Position = 0;
        Assert.Throws<InvalidDataException>(() => SharedEstimationProtocol.ReadHeader(reader));
    }

    [TestMethod]
    public void RemoteRunReconnectMessages_RoundTrip()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var projectId = Guid.NewGuid();
        var modelSystemId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var snapshots = new[]
        {
            new RemoteRunSnapshot("run-1", "forecast", RunMode.Normal, "/runs/forecast", "Start",
                "model-hash", RemoteRunState.Running, "Iteration 4", 4, 1.25,
                [new RemoteRunParameterValue(3, 0.75)], null, null, null, false, timestamp,
                projectId, modelSystemId, ownerUserId),
            new RemoteRunSnapshot("run-2", "calibration", RunMode.Calibration, "/runs/calibration", "Start",
                "model-hash-2", RemoteRunState.Completed, "Complete", 8, 0.01,
                [], [new RemoteRunParameterValue(5, 1.1)], null, null, true, timestamp)
        };
        var archive = Enumerable.Range(0, 1_000_001).Select(value => (byte)value).ToArray();
        using var archiveSource = new MemoryStream(archive, writable: false);

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            SharedEstimationProtocol.WriteQueryRemoteRuns(writer);
            SharedEstimationProtocol.WriteRemoteRunSnapshots(writer, snapshots);
            SharedEstimationProtocol.WriteGetRemoteRunArtifacts(writer, "run-2");
            SharedEstimationProtocol.WriteRemoteRunArtifacts(writer, "run-2", archiveSource,
                archiveSource.Length);
            SharedEstimationProtocol.WriteAcknowledgeRemoteRun(writer, "run-2");
        }

        stream.Position = 0;
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
        using var receivedArchive = new MemoryStream();
        SharedEstimationProtocol.ReadQueryRemoteRuns(reader);
        var readSnapshots = SharedEstimationProtocol.ReadRemoteRunSnapshots(reader);
        Assert.HasCount(2, readSnapshots);
        Assert.AreEqual(snapshots[0].RunId, readSnapshots[0].RunId);
        Assert.AreEqual(snapshots[0].RunMode, readSnapshots[0].RunMode);
        Assert.AreEqual(snapshots[0].ModelSystemHash, readSnapshots[0].ModelSystemHash);
        Assert.AreEqual(snapshots[0].ProgressParameters[0], readSnapshots[0].ProgressParameters[0]);
        Assert.AreEqual(snapshots[1].State, readSnapshots[1].State);
        Assert.AreEqual(snapshots[1].OptimizationResults![0], readSnapshots[1].OptimizationResults![0]);
        Assert.AreEqual(snapshots[1].UpdatedAt, readSnapshots[1].UpdatedAt);
        Assert.AreEqual(projectId, readSnapshots[0].ProjectId);
        Assert.AreEqual(modelSystemId, readSnapshots[0].ModelSystemId);
        Assert.AreEqual(ownerUserId, readSnapshots[0].OwnerUserId);
        Assert.AreEqual("run-2", SharedEstimationProtocol.ReadGetRemoteRunArtifacts(reader));
        var response = SharedEstimationProtocol.ReadRemoteRunArtifacts(reader, receivedArchive, "archive.zip");
        Assert.AreEqual("run-2", response.RunId);
        Assert.AreEqual("archive.zip", response.ArchivePath);
        CollectionAssert.AreEqual(archive, receivedArchive.ToArray());
        Assert.IsNull(response.Error);
        Assert.AreEqual("run-2", SharedEstimationProtocol.ReadAcknowledgeRemoteRun(reader));
    }

    [TestMethod]
    public void RemoteRunDeletionMessages_RoundTrip()
    {
        var expected = new RemoteRunDeletionResponse("request-1", "run-2", false,
            "The run output directory could not be removed.");
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            SharedEstimationProtocol.WriteDeleteRemoteRun(writer, expected.RequestId, expected.RunId);
            SharedEstimationProtocol.WriteRemoteRunDeleted(writer, expected);
        }

        stream.Position = 0;
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
        var request = SharedEstimationProtocol.ReadDeleteRemoteRun(reader);
        Assert.AreEqual(expected.RequestId, request.RequestId);
        Assert.AreEqual(expected.RunId, request.RunId);
        Assert.AreEqual(expected, SharedEstimationProtocol.ReadRemoteRunDeleted(reader));
    }

    [TestMethod]
    public void JobSnapshots_RoundTrip()
    {
        var projectId = Guid.NewGuid();
        var modelSystemId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var progress = new SharedEstimationProgress("run-active", 4, 1.25, 8, 2, 3);
        var completion = new SharedEstimationCompletion("run-complete", true, 0.5,
            new[] { 0.25, 0.75 }, 12, 6, null);
        var snapshots = new[]
        {
            new SharedEstimationJobSnapshot("run-active", SharedEstimationJobState.Running, progress, null,
                ["worker-1"], "active-estimation", "/runs/active", "active-hash",
                [new SharedEstimationParameterMetadata(4, "Transit cost", 0, 10)], projectId, modelSystemId, ownerUserId,
                new Dictionary<string, int> { ["worker-1"] = 3 }),
            new SharedEstimationJobSnapshot("run-complete", SharedEstimationJobState.Completed, null, completion,
                ["worker-2"], "completed-estimation", "/runs/completed", "completed-hash",
                [new SharedEstimationParameterMetadata(8, "Wait time", 0, 20)])
        };

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            SharedEstimationProtocol.WriteJobSnapshots(writer, snapshots);

        stream.Position = 0;
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
        var read = SharedEstimationProtocol.ReadJobSnapshots(reader);
        Assert.HasCount(2, read);
        Assert.AreEqual(SharedEstimationJobState.Running, read[0].State);
        Assert.AreEqual(progress.RunId, read[0].Progress!.RunId);
        Assert.AreEqual(progress.Iteration, read[0].Progress.Iteration);
        Assert.AreEqual(progress.BestFitness, read[0].Progress.BestFitness);
        Assert.AreEqual(progress.EvaluationsCompleted, read[0].Progress.EvaluationsCompleted);
        Assert.IsNull(read[0].Completion);
        Assert.AreEqual("active-estimation", read[0].RunName);
        Assert.AreEqual("/runs/active", read[0].WorkingDirectory);
        Assert.AreEqual("active-hash", read[0].ModelSystemHash);
        CollectionAssert.AreEqual(new[] { "worker-1" }, read[0].ActiveWorkerIds!.ToArray());
        Assert.AreEqual(new SharedEstimationParameterMetadata(4, "Transit cost", 0, 10), read[0].Parameters![0]);
        Assert.AreEqual(projectId, read[0].ProjectId);
        Assert.AreEqual(ownerUserId, read[0].OwnerUserId);
        Assert.AreEqual(modelSystemId, read[0].ModelSystemId);
        Assert.AreEqual(3, read[0].ConfiguredWorkerCounts!["worker-1"]);
        Assert.AreEqual(SharedEstimationJobState.Completed, read[1].State);
        Assert.IsNull(read[1].Progress);
        Assert.IsNotNull(read[1].Completion);
        Assert.AreEqual(completion.RunId, read[1].Completion.RunId);
        Assert.AreEqual(completion.Succeeded, read[1].Completion.Succeeded);
        Assert.AreEqual(completion.BestFitness, read[1].Completion.BestFitness);
        Assert.AreEqual(completion.TotalEvaluations, read[1].Completion.TotalEvaluations);
        Assert.AreEqual(completion.Iterations, read[1].Completion.Iterations);
        Assert.HasCount(completion.BestParameters.Count, read[1].Completion.BestParameters);
        for (int i = 0; i < completion.BestParameters.Count; i++)
            Assert.AreEqual(completion.BestParameters[i], read[1].Completion.BestParameters[i]);
    }

    [TestMethod]
    public void CoordinatorWorkerControl_RoundTrips()
    {
        var worker = new SharedEstimationWorkerEndpoint("worker-2", "server-2", "host", 5001,
            "token", "fingerprint", new Dictionary<int, string> { [3] = "/input" }, ConcurrentRuns: 4);
        var acknowledgement = new SharedEstimationWorkerControlAcknowledgement(
            "run-1", "worker-2", true, false, "already active", 2);

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            SharedEstimationProtocol.WriteCoordinatorWorkerRequest(writer, "run-1", worker, remove: false);
            SharedEstimationProtocol.WriteWorkerControlAcknowledgement(writer, acknowledgement);
        }

        stream.Position = 0;
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
        var readWorkerRequest = SharedEstimationProtocol.ReadCoordinatorWorkerRequest(reader, remove: false);
        Assert.AreEqual("run-1", readWorkerRequest.RunId);
        Assert.AreEqual(worker.WorkerId, readWorkerRequest.Worker.WorkerId);
        Assert.AreEqual(worker.Address, readWorkerRequest.Worker.Address);
        Assert.AreEqual(worker.ConcurrentRuns, readWorkerRequest.Worker.ConcurrentRuns);
        Assert.AreEqual(worker.BasicParameterOverrides![3], readWorkerRequest.Worker.BasicParameterOverrides![3]);
        Assert.AreEqual(acknowledgement, SharedEstimationProtocol.ReadWorkerControlAcknowledgement(reader));
    }

    [TestMethod]
    public void WorkerEndpoint_CreatesOneSlotPerConfiguredRun()
    {
        var endpoint = new SharedEstimationWorkerEndpoint("worker-a", "server-a", "host", 5001,
            "token", "fingerprint", ConcurrentRuns: 3);

        var slots = endpoint.CreateWorkerSlots();

        Assert.HasCount(3, slots);
        CollectionAssert.AreEqual(new[] { "worker-a", "worker-a#run-2", "worker-a#run-3" },
            slots.Select(slot => slot.WorkerId).ToArray());
        Assert.IsTrue(slots.All(slot => slot.EndpointId == endpoint.EndpointId));
        Assert.IsTrue(slots.All(slot => slot.ConcurrentRuns == 1));

        var configuredWorker = endpoint with
        {
            BasicParameterOverrides = new Dictionary<int, string> { [8] = "/worker/input" }
        };
        Assert.IsTrue(configuredWorker.CreateWorkerSlots()
            .All(slot => slot.BasicParameterOverrides![8] == "/worker/input"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (endpoint with { ConcurrentRuns = SharedEstimationWorkerEndpoint.MaximumConcurrentRuns + 1 })
                .CreateWorkerSlots());
    }
}
