using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.Bus;
using XTMF2.Bus.Optimization;

namespace XTMF2.UnitTests.Bus;

[TestClass]
public class TestSharedEstimationProtocol
{
    [TestMethod]
    public void CoordinatorRequest_RoundTrips()
    {
        var request = new SharedEstimationCoordinatorRequest(
            new SharedEstimationRunRequest("run-remote", "/remote/runs", "Start", [1, 2, 3]),
            [new SharedEstimationWorkerEndpoint("worker-1", "endpoint-1", "worker.example", 5000,
                "token", "fingerprint", new Dictionary<int, string> { [7] = "/worker/input" })],
            "NelderMead",
            [new AlgorithmParameterDescriptor { Key = "MaxIterations", Label = "Max Iterations", Hint = "limit", Value = "12" }],
            [0.0], [1.0], [0.5], false);

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
        Assert.AreEqual(request.Workers[0].BasicParameterOverrides![7],
            read.Workers[0].BasicParameterOverrides![7]);
        Assert.AreEqual(request.AlgorithmId, read.AlgorithmId);
        Assert.AreEqual(request.AlgorithmParameters[0].Value, read.AlgorithmParameters[0].Value);
        Assert.AreEqual(request.InitialValues[0], read.InitialValues[0]);
    }

    [TestMethod]
    public void RunAndWorkerMessages_RoundTrip()
    {
        var run = new SharedEstimationRunRequest(
            "run-1", "/shared/runs", "Start", new byte[] { 1, 2, 3 },
            new Dictionary<int, string> { [4] = "/worker/input", [9] = "/worker/output" });
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
    public void EvaluationProgressAndCompletionMessages_RoundTrip()
    {
        var candidates = new[]
        {
            new SharedEstimationCandidate("run-1", 4, "candidate-1", new[] { 1.0, 2.0 }),
            new SharedEstimationCandidate("run-1", 4, "candidate-2", new[] { 3.0, 4.0 })
        };
        var results = new[]
        {
            new SharedEstimationEvaluationResult("run-1", 4, "candidate-1", 0.25,
                null, null, null),
            new SharedEstimationEvaluationResult("run-1", 4, "candidate-2", double.MaxValue,
                "worker failed", "Module", Guid.Parse("11111111-1111-1111-1111-111111111111"))
        };
        var progress = new SharedEstimationProgress("run-1", 2, 0.25, 3, 1, 2);
        var completion = new SharedEstimationCompletion("run-1", true, 0.25,
            new[] { 1.0, 2.0 }, 3, 2, null);

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            SharedEstimationProtocol.WriteCandidates(writer, candidates);
            SharedEstimationProtocol.WriteResults(writer, results);
            SharedEstimationProtocol.WriteProgress(writer, progress);
            SharedEstimationProtocol.WriteCompletion(writer, completion);
            SharedEstimationProtocol.WriteCancel(writer, "run-1", "user requested cancellation");
        }

        stream.Position = 0;
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
        var readCandidates = SharedEstimationProtocol.ReadCandidates(reader);
        Assert.HasCount(candidates.Length, readCandidates);
        for (int i = 0; i < candidates.Length; i++)
        {
            Assert.AreEqual(candidates[i].RunId, readCandidates[i].RunId);
            Assert.AreEqual(candidates[i].BatchId, readCandidates[i].BatchId);
            Assert.AreEqual(candidates[i].CandidateId, readCandidates[i].CandidateId);
            Assert.HasCount(candidates[i].Parameters.Count, readCandidates[i].Parameters);
            for (int j = 0; j < candidates[i].Parameters.Count; j++)
                Assert.AreEqual(candidates[i].Parameters[j], readCandidates[i].Parameters[j]);
        }

        var readResults = SharedEstimationProtocol.ReadResults(reader);
        Assert.HasCount(results.Length, readResults);
        for (int i = 0; i < results.Length; i++)
            Assert.AreEqual(results[i], readResults[i]);

        Assert.AreEqual(progress, SharedEstimationProtocol.ReadProgress(reader));
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
    public void LegacyRunRequestWithoutOverrides_RoundTripsAsVersionOnePayload()
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
            writer.Write(SharedEstimationProtocol.Version + 2);
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
    public void JobSnapshots_RoundTrip()
    {
        var progress = new SharedEstimationProgress("run-active", 4, 1.25, 8, 2, 3);
        var completion = new SharedEstimationCompletion("run-complete", true, 0.5,
            new[] { 0.25, 0.75 }, 12, 6, null);
        var snapshots = new[]
        {
            new SharedEstimationJobSnapshot("run-active", SharedEstimationJobState.Running, progress, null),
            new SharedEstimationJobSnapshot("run-complete", SharedEstimationJobState.Completed, null, completion)
        };

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            SharedEstimationProtocol.WriteJobSnapshots(writer, snapshots);

        stream.Position = 0;
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
        var read = SharedEstimationProtocol.ReadJobSnapshots(reader);
        Assert.HasCount(2, read);
        Assert.AreEqual(SharedEstimationJobState.Running, read[0].State);
        Assert.AreEqual(progress, read[0].Progress);
        Assert.IsNull(read[0].Completion);
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
            "token", "fingerprint", new Dictionary<int, string> { [3] = "/input" });
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
        Assert.AreEqual(worker.BasicParameterOverrides![3], readWorkerRequest.Worker.BasicParameterOverrides![3]);
        Assert.AreEqual(acknowledgement, SharedEstimationProtocol.ReadWorkerControlAcknowledgement(reader));
    }
}
