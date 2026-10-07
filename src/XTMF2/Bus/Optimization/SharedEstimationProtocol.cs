using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using XTMF2.Bus;

namespace XTMF2.Bus.Optimization;

public enum SharedEstimationMessageType
{
    StartRun = 0,
    AddWorker = 1,
    RemoveWorker = 2,
    EvaluateCandidate = 3,
    EvaluationResult = 4,
    Progress = 5,
    Complete = 6,
    Cancel = 7,
    StartCoordinator = 8,
    QueryJobs = 9,
    JobSnapshots = 10,
    AddCoordinatorWorker = 11,
    RemoveCoordinatorWorker = 12,
    WorkerControlAcknowledgement = 13,
    Status = 14,
    QueryRemoteRuns = 15,
    RemoteRunSnapshots = 16,
    GetRemoteRunArtifacts = 17,
    RemoteRunArtifacts = 18,
    AcknowledgeRemoteRun = 19,
    QueryServerActivity = 20,
    ServerActivitySnapshots = 21,
    DeleteRemoteRun = 22,
    RemoteRunDeleted = 23,
    WorkerReady = 24
}

public enum SharedEstimationJobState
{
    Running = 0,
    Completed = 1
}

public enum RunServerActivityState
{
    Running = 0,
    Queued = 1
}

public sealed record RunServerActivity(
    string RunId,
    string RunName,
    string Kind,
    RunServerActivityState State,
    string Status,
    int QueuePosition = 0,
    int Iteration = 0,
    double Fitness = double.NaN,
    int ActiveWorkers = 0,
    int EvaluationsCompleted = 0,
    int EvaluationsPending = 0);

public sealed record RunServerActivityResponse(string RequestId, IReadOnlyList<RunServerActivity> Activities);

public sealed record RemoteRunDeletionResponse(string RequestId, string RunId, bool Deleted, string? Error)
{
    public bool IsDeletedOrNotFound => Deleted ||
        string.Equals(Error, "The remote run was not found.", StringComparison.Ordinal);
}

public sealed record SharedEstimationParameterMetadata(int NodeIndex, string Name, double Min, double Max);

public sealed record SharedEstimationJobSnapshot(
    string RunId,
    SharedEstimationJobState State,
    SharedEstimationProgress? Progress,
    SharedEstimationCompletion? Completion,
    IReadOnlyList<string>? ActiveWorkerIds = null,
    string RunName = "",
    string WorkingDirectory = "",
    string ModelSystemHash = "",
    IReadOnlyList<SharedEstimationParameterMetadata>? Parameters = null,
    Guid? ProjectId = null,
    Guid? ModelSystemId = null,
    Guid? OwnerUserId = null,
    IReadOnlyDictionary<string, int>? ConfiguredWorkerCounts = null);

public sealed record SharedEstimationWorkerControlAcknowledgement(
    string RunId,
    string WorkerId,
    bool Add,
    bool Succeeded,
    string? Error,
    int ActiveWorkerCount);

public sealed record SharedEstimationStatus(string RunId, string Message);

public sealed record SharedEstimationWorkerReady(string RunId, bool Succeeded, string? Error);

public sealed record SharedEstimationRunRequest(
    string RunId,
    string WorkingDirectory,
    string StartToExecute,
    byte[] ModelSystem,
    IReadOnlyDictionary<int, string>? BasicParameterOverrides = null,
    Guid? ProjectId = null,
    Guid? ModelSystemId = null,
    Guid? OwnerUserId = null,
    int WorkerSlotCount = 1);

public sealed record SharedEstimationWorkerRegistration(
    string RunId,
    string WorkerId,
    string EndpointId);

public sealed record SharedEstimationWorkerEndpoint(
    string WorkerId,
    string EndpointId,
    string Address,
    int Port,
    string Token,
    string CertificateFingerprint,
    IReadOnlyDictionary<int, string>? BasicParameterOverrides = null,
    int ConcurrentRuns = 1)
{
    public const int MaximumConcurrentRuns = 32;

    public IReadOnlyList<SharedEstimationWorkerEndpoint> CreateWorkerSlots()
    {
        if (ConcurrentRuns < 1 || ConcurrentRuns > MaximumConcurrentRuns)
            throw new ArgumentOutOfRangeException(nameof(ConcurrentRuns));
        return Enumerable.Range(0, ConcurrentRuns)
            .Select(slotIndex => this with
            {
                WorkerId = slotIndex == 0 ? WorkerId : $"{WorkerId}#run-{slotIndex + 1}",
                ConcurrentRuns = 1
            })
            .ToArray();
    }
}

public sealed record SharedEstimationCoordinatorRequest(
    SharedEstimationRunRequest Run,
    IReadOnlyList<SharedEstimationWorkerEndpoint> Workers,
    string AlgorithmId,
    IReadOnlyList<AlgorithmParameterDescriptor> AlgorithmParameters,
    IReadOnlyList<double> LowerBounds,
    IReadOnlyList<double> UpperBounds,
    IReadOnlyList<double> InitialValues,
    bool IsMaximize,
    bool UseCoordinatorAsWorker = true,
    IReadOnlyList<SharedEstimationParameterMetadata>? Parameters = null,
    int CoordinatorConcurrentRuns = 1);

public sealed record SharedEstimationCandidate(
    string RunId,
    long BatchId,
    string CandidateId,
    IReadOnlyList<double> Parameters);

public sealed record SharedEstimationEvaluationResult(
    string RunId,
    long BatchId,
    string CandidateId,
    double Fitness,
    string? Error,
    string? ModuleName,
    Guid? ElementId);

public sealed record SharedEstimationProgress(
    string RunId,
    int Iteration,
    double BestFitness,
    int EvaluationsCompleted,
    int EvaluationsPending,
    int ActiveWorkers,
    int FitnessTestsThisIteration = 0,
    IReadOnlyDictionary<string, int>? EvaluationsByWorker = null,
    IReadOnlyList<double>? BestParameters = null,
    IReadOnlyList<string>? ActiveWorkerIds = null);

public sealed record SharedEstimationCompletion(
    string RunId,
    bool Succeeded,
    double BestFitness,
    IReadOnlyList<double> BestParameters,
    int TotalEvaluations,
    int Iterations,
    string? FailureReason);

public static class SharedEstimationProtocol
{
    public const int Version = 1;
    private const int MaxCollectionLength = 1_000_000;
    private const int MaxServerActivities = 10_000;

    public static void WriteHeader(BinaryWriter writer, SharedEstimationMessageType messageType)
    {
        writer.Write(Version);
        writer.Write((int)messageType);
    }

    public static void WriteCoordinatorRequest(BinaryWriter writer, SharedEstimationCoordinatorRequest request)
    {
        WriteHeader(writer, SharedEstimationMessageType.StartCoordinator);
        WriteRunRequestPayload(writer, request.Run);
        WriteCount(writer, request.Workers.Count);
        foreach (var worker in request.Workers)
        {
            WriteRequiredString(writer, worker.WorkerId);
            WriteRequiredString(writer, worker.EndpointId);
            WriteRequiredString(writer, worker.Address);
            writer.Write(worker.Port);
            WriteRequiredString(writer, worker.Token);
            WriteRequiredString(writer, worker.CertificateFingerprint);
            WriteOverrides(writer, worker.BasicParameterOverrides);
            WriteConcurrentRuns(writer, worker.ConcurrentRuns);
        }
        WriteRequiredString(writer, request.AlgorithmId);
        WriteCount(writer, request.AlgorithmParameters.Count);
        foreach (var parameter in request.AlgorithmParameters)
        {
            WriteRequiredString(writer, parameter.Key);
            WriteRequiredString(writer, parameter.Label);
            WriteRequiredString(writer, parameter.Hint);
            WriteRequiredString(writer, parameter.Value);
        }
        WriteDoubles(writer, request.LowerBounds);
        WriteDoubles(writer, request.UpperBounds);
        WriteDoubles(writer, request.InitialValues);
        writer.Write(request.IsMaximize);
        writer.Write(request.UseCoordinatorAsWorker);
        WriteConcurrentRuns(writer, request.CoordinatorConcurrentRuns);
        var parameterMetadata = request.Parameters ?? Array.Empty<SharedEstimationParameterMetadata>();
        WriteCount(writer, parameterMetadata.Count);
        foreach (var parameter in parameterMetadata)
        {
            if (parameter.NodeIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(request), "Parameter node indices must be non-negative.");
            writer.Write(parameter.NodeIndex);
            WriteRequiredString(writer, parameter.Name);
            writer.Write(parameter.Min);
            writer.Write(parameter.Max);
        }
    }

    public static SharedEstimationCoordinatorRequest ReadCoordinatorRequest(BinaryReader reader)
    {
        var (_, messageType) = ReadHeader(reader);
        if (messageType != SharedEstimationMessageType.StartCoordinator)
            throw new InvalidDataException($"Expected {SharedEstimationMessageType.StartCoordinator} message but received {messageType}.");
        return ReadCoordinatorRequestPayload(reader);
    }

    internal static SharedEstimationCoordinatorRequest ReadCoordinatorRequestPayload(BinaryReader reader)
    {
        var run = new SharedEstimationRunRequest(
            ReadRequiredString(reader), ReadRequiredString(reader), ReadRequiredString(reader),
            ReadBytes(reader), ReadOverrides(reader),
            ReadOptionalGuid(reader), ReadOptionalGuid(reader), ReadOptionalGuid(reader), ReadConcurrentRuns(reader));
        var workers = new List<SharedEstimationWorkerEndpoint>(ReadCount(reader));
        for (int i = 0; i < workers.Capacity; i++)
        {
            var workerId = ReadRequiredString(reader);
            var endpointId = ReadRequiredString(reader);
            var address = ReadRequiredString(reader);
            var port = reader.ReadInt32();
            var token = ReadRequiredString(reader);
            var fingerprint = ReadRequiredString(reader);
            var overrides = ReadOverrides(reader);
            workers.Add(new SharedEstimationWorkerEndpoint(
                workerId, endpointId, address, port, token, fingerprint, overrides, ReadConcurrentRuns(reader)));
        }
        var algorithmId = ReadRequiredString(reader);
        var parameters = new List<AlgorithmParameterDescriptor>(ReadCount(reader));
        for (int i = 0; i < parameters.Capacity; i++)
        {
            parameters.Add(new AlgorithmParameterDescriptor
            {
                Key = ReadRequiredString(reader),
                Label = ReadRequiredString(reader),
                Hint = ReadRequiredString(reader),
                Value = ReadRequiredString(reader)
            });
        }
        var lowerBounds = ReadDoubles(reader);
        var upperBounds = ReadDoubles(reader);
        var initialValues = ReadDoubles(reader);
        var isMaximize = reader.ReadBoolean();
        var useCoordinatorAsWorker = reader.ReadBoolean();
        var coordinatorConcurrentRuns = ReadConcurrentRuns(reader);
        var metadataCount = ReadCount(reader);
        var values = new SharedEstimationParameterMetadata[metadataCount];
        for (var index = 0; index < metadataCount; index++)
        {
            var nodeIndex = reader.ReadInt32();
            if (nodeIndex < 0)
                throw new InvalidDataException("Parameter node indices must be non-negative.");
            values[index] = new SharedEstimationParameterMetadata(nodeIndex, ReadRequiredString(reader),
                reader.ReadDouble(), reader.ReadDouble());
        }
        return new SharedEstimationCoordinatorRequest(run, workers, algorithmId, parameters,
            lowerBounds, upperBounds, initialValues, isMaximize, useCoordinatorAsWorker, values,
            coordinatorConcurrentRuns);
    }

    public static (int Version, SharedEstimationMessageType MessageType) ReadHeader(BinaryReader reader)
    {
        int version = reader.ReadInt32();
        var messageType = (SharedEstimationMessageType)reader.ReadInt32();
        if (version != Version)
            throw new InvalidDataException($"Unsupported shared estimation protocol version: {version}.");
        if (!Enum.IsDefined(messageType))
            throw new InvalidDataException($"Unknown shared estimation message type: {(int)messageType}.");
        return (version, messageType);
    }

    public static void WriteRunRequest(BinaryWriter writer, SharedEstimationRunRequest request)
    {
        WriteHeader(writer, SharedEstimationMessageType.StartRun);
        WriteRequiredString(writer, request.RunId);
        WriteRequiredString(writer, request.WorkingDirectory);
        WriteRequiredString(writer, request.StartToExecute);
        WriteBytes(writer, request.ModelSystem);
        var overrides = request.BasicParameterOverrides;
        WriteCount(writer, overrides?.Count ?? 0);
        if (overrides is not null)
        {
            foreach (var pair in overrides)
            {
                if (pair.Key < 0)
                    throw new ArgumentOutOfRangeException(nameof(request), "Override node indices must be non-negative.");
                writer.Write(pair.Key);
                WriteRequiredString(writer, pair.Value);
            }
        }
        WriteOptionalGuid(writer, request.ProjectId);
        WriteOptionalGuid(writer, request.ModelSystemId);
        WriteOptionalGuid(writer, request.OwnerUserId);
        WriteConcurrentRuns(writer, request.WorkerSlotCount);
    }

    private static void WriteRunRequestPayload(BinaryWriter writer, SharedEstimationRunRequest request)
    {
        WriteRequiredString(writer, request.RunId);
        WriteRequiredString(writer, request.WorkingDirectory);
        WriteRequiredString(writer, request.StartToExecute);
        WriteBytes(writer, request.ModelSystem);
        WriteOverrides(writer, request.BasicParameterOverrides);
        WriteOptionalGuid(writer, request.ProjectId);
        WriteOptionalGuid(writer, request.ModelSystemId);
        WriteOptionalGuid(writer, request.OwnerUserId);
        WriteConcurrentRuns(writer, request.WorkerSlotCount);
    }

    private static void WriteOverrides(BinaryWriter writer, IReadOnlyDictionary<int, string>? overrides)
    {
        WriteCount(writer, overrides?.Count ?? 0);
        if (overrides is null) return;
        foreach (var pair in overrides)
        {
            writer.Write(pair.Key);
            WriteRequiredString(writer, pair.Value);
        }
    }

    private static void WriteConcurrentRuns(BinaryWriter writer, int concurrentRuns)
    {
        if (concurrentRuns < 1 || concurrentRuns > SharedEstimationWorkerEndpoint.MaximumConcurrentRuns)
            throw new ArgumentOutOfRangeException(nameof(concurrentRuns),
                $"Concurrent runs must be between 1 and {SharedEstimationWorkerEndpoint.MaximumConcurrentRuns}.");
        writer.Write(concurrentRuns);
    }

    private static int ReadConcurrentRuns(BinaryReader reader)
    {
        var concurrentRuns = reader.ReadInt32();
        if (concurrentRuns < 1 || concurrentRuns > SharedEstimationWorkerEndpoint.MaximumConcurrentRuns)
            throw new InvalidDataException("The RunServer concurrent-run count is outside the supported range.");
        return concurrentRuns;
    }

    public static SharedEstimationRunRequest ReadRunRequest(BinaryReader reader)
    {
        var (_, messageType) = ReadHeader(reader);
        if (messageType != SharedEstimationMessageType.StartRun)
            throw new InvalidDataException($"Expected {SharedEstimationMessageType.StartRun} message but received {messageType}.");
        return ReadRunRequestPayload(reader);
    }

    internal static SharedEstimationRunRequest ReadRunRequestPayload(BinaryReader reader)
    {
        return new SharedEstimationRunRequest(
            ReadRequiredString(reader),
            ReadRequiredString(reader),
            ReadRequiredString(reader),
            ReadBytes(reader),
            ReadOverrides(reader), ReadOptionalGuid(reader), ReadOptionalGuid(reader), ReadOptionalGuid(reader),
            ReadConcurrentRuns(reader));
    }

    public static void WriteWorkerRegistration(BinaryWriter writer, SharedEstimationWorkerRegistration registration,
        bool remove)
    {
        WriteHeader(writer, remove
            ? SharedEstimationMessageType.RemoveWorker
            : SharedEstimationMessageType.AddWorker);
        WriteRequiredString(writer, registration.RunId);
        WriteRequiredString(writer, registration.WorkerId);
        WriteRequiredString(writer, registration.EndpointId);
    }

    public static SharedEstimationWorkerRegistration ReadWorkerRegistration(BinaryReader reader, bool remove)
    {
        ReadExpectedHeader(reader, remove
            ? SharedEstimationMessageType.RemoveWorker
            : SharedEstimationMessageType.AddWorker);
        return ReadWorkerRegistrationPayload(reader);
    }

    internal static SharedEstimationWorkerRegistration ReadWorkerRegistrationPayload(BinaryReader reader)
    {
        return new SharedEstimationWorkerRegistration(
            ReadRequiredString(reader),
            ReadRequiredString(reader),
            ReadRequiredString(reader));
    }

    public static void WriteCoordinatorWorkerRequest(BinaryWriter writer, string runId,
        SharedEstimationWorkerEndpoint worker, bool remove)
    {
        WriteHeader(writer, remove
            ? SharedEstimationMessageType.RemoveCoordinatorWorker
            : SharedEstimationMessageType.AddCoordinatorWorker);
        WriteRequiredString(writer, runId);
        WriteRequiredString(writer, worker.WorkerId);
        WriteRequiredString(writer, worker.EndpointId);
        WriteRequiredString(writer, worker.Address);
        writer.Write(worker.Port);
        WriteRequiredString(writer, worker.Token);
        WriteRequiredString(writer, worker.CertificateFingerprint);
        WriteOverrides(writer, worker.BasicParameterOverrides);
        WriteConcurrentRuns(writer, worker.ConcurrentRuns);
    }

    public static (string RunId, SharedEstimationWorkerEndpoint Worker) ReadCoordinatorWorkerRequest(BinaryReader reader, bool remove)
    {
        ReadExpectedHeader(reader, remove
            ? SharedEstimationMessageType.RemoveCoordinatorWorker
            : SharedEstimationMessageType.AddCoordinatorWorker);
        return ReadCoordinatorWorkerRequestPayload(reader);
    }

    internal static (string RunId, SharedEstimationWorkerEndpoint Worker) ReadCoordinatorWorkerRequestPayload(BinaryReader reader)
        => (ReadRequiredString(reader), new SharedEstimationWorkerEndpoint(ReadRequiredString(reader), ReadRequiredString(reader), ReadRequiredString(reader),
            reader.ReadInt32(), ReadRequiredString(reader), ReadRequiredString(reader), ReadOverrides(reader), ReadConcurrentRuns(reader)));

    public static void WriteWorkerControlAcknowledgement(BinaryWriter writer,
        SharedEstimationWorkerControlAcknowledgement acknowledgement)
    {
        WriteHeader(writer, SharedEstimationMessageType.WorkerControlAcknowledgement);
        WriteRequiredString(writer, acknowledgement.RunId);
        WriteRequiredString(writer, acknowledgement.WorkerId);
        writer.Write(acknowledgement.Add);
        writer.Write(acknowledgement.Succeeded);
        WriteOptionalString(writer, acknowledgement.Error);
        writer.Write(acknowledgement.ActiveWorkerCount);
    }

    public static SharedEstimationWorkerControlAcknowledgement ReadWorkerControlAcknowledgement(BinaryReader reader)
    {
        ReadExpectedHeader(reader, SharedEstimationMessageType.WorkerControlAcknowledgement);
        return ReadWorkerControlAcknowledgementPayload(reader);
    }

    internal static SharedEstimationWorkerControlAcknowledgement ReadWorkerControlAcknowledgementPayload(BinaryReader reader)
        => new(ReadRequiredString(reader), ReadRequiredString(reader), reader.ReadBoolean(), reader.ReadBoolean(),
            ReadOptionalString(reader), reader.ReadInt32());

    public static void WriteStatus(BinaryWriter writer, SharedEstimationStatus status)
    {
        WriteHeader(writer, SharedEstimationMessageType.Status);
        WriteRequiredString(writer, status.RunId);
        WriteRequiredString(writer, status.Message);
    }

    public static SharedEstimationStatus ReadStatus(BinaryReader reader)
    {
        ReadExpectedHeader(reader, SharedEstimationMessageType.Status);
        return ReadStatusPayload(reader);
    }

    internal static SharedEstimationStatus ReadStatusPayload(BinaryReader reader)
        => new(ReadRequiredString(reader), ReadRequiredString(reader));

    public static void WriteWorkerReady(BinaryWriter writer, SharedEstimationWorkerReady readiness)
    {
        WriteHeader(writer, SharedEstimationMessageType.WorkerReady);
        WriteRequiredString(writer, readiness.RunId);
        writer.Write(readiness.Succeeded);
        WriteOptionalString(writer, readiness.Error);
    }

    public static SharedEstimationWorkerReady ReadWorkerReady(BinaryReader reader)
    {
        ReadExpectedHeader(reader, SharedEstimationMessageType.WorkerReady);
        return ReadWorkerReadyPayload(reader);
    }

    internal static SharedEstimationWorkerReady ReadWorkerReadyPayload(BinaryReader reader)
        => new(ReadRequiredString(reader), reader.ReadBoolean(), ReadOptionalString(reader));

    public static void WriteCandidate(BinaryWriter writer, SharedEstimationCandidate candidate)
    {
        WriteHeader(writer, SharedEstimationMessageType.EvaluateCandidate);
        WriteRequiredString(writer, candidate.RunId);
        writer.Write(candidate.BatchId);
        WriteRequiredString(writer, candidate.CandidateId);
        WriteDoubles(writer, candidate.Parameters);
    }

    public static SharedEstimationCandidate ReadCandidate(BinaryReader reader)
    {
        ReadExpectedHeader(reader, SharedEstimationMessageType.EvaluateCandidate);
        return ReadCandidatePayload(reader);
    }

    internal static SharedEstimationCandidate ReadCandidatePayload(BinaryReader reader)
        => new(ReadRequiredString(reader), reader.ReadInt64(), ReadRequiredString(reader), ReadDoubles(reader));

    private static IReadOnlyDictionary<int, string>? ReadOverrides(BinaryReader reader)
    {
        int count = ReadCount(reader);
        if (count == 0)
            return null;

        var overrides = new Dictionary<int, string>(count);
        for (int i = 0; i < count; i++)
        {
            int nodeIndex = reader.ReadInt32();
            if (nodeIndex < 0 || !overrides.TryAdd(nodeIndex, ReadRequiredString(reader)))
                throw new InvalidDataException("Shared estimation contains an invalid parameter override.");
        }
        return overrides;
    }

    public static void WriteResult(BinaryWriter writer, SharedEstimationEvaluationResult result)
    {
        WriteHeader(writer, SharedEstimationMessageType.EvaluationResult);
        WriteRequiredString(writer, result.RunId);
        writer.Write(result.BatchId);
        WriteRequiredString(writer, result.CandidateId);
        writer.Write(result.Fitness);
        WriteOptionalString(writer, result.Error);
        WriteOptionalString(writer, result.ModuleName);
        writer.Write(result.ElementId?.ToString() ?? string.Empty);
    }

    public static SharedEstimationEvaluationResult ReadResult(BinaryReader reader)
    {
        ReadExpectedHeader(reader, SharedEstimationMessageType.EvaluationResult);
        return ReadResultPayload(reader);
    }

    internal static SharedEstimationEvaluationResult ReadResultPayload(BinaryReader reader)
    {
        var runId = ReadRequiredString(reader);
        var batchId = reader.ReadInt64();
        var candidateId = ReadRequiredString(reader);
        var fitness = reader.ReadDouble();
        var error = ReadOptionalString(reader);
        var moduleName = ReadOptionalString(reader);
        var elementIdText = reader.ReadString();
        return new SharedEstimationEvaluationResult(runId, batchId, candidateId, fitness, error, moduleName,
            Guid.TryParse(elementIdText, out var elementId) ? elementId : null);
    }

    public static void WriteProgress(BinaryWriter writer, SharedEstimationProgress progress)
    {
        WriteHeader(writer, SharedEstimationMessageType.Progress);
        WriteProgressPayload(writer, progress);
    }

    private static void WriteProgressPayload(BinaryWriter writer, SharedEstimationProgress progress)
    {
        WriteRequiredString(writer, progress.RunId);
        writer.Write(progress.Iteration);
        writer.Write(progress.BestFitness);
        writer.Write(progress.EvaluationsCompleted);
        writer.Write(progress.EvaluationsPending);
        writer.Write(progress.ActiveWorkers);
        writer.Write(progress.FitnessTestsThisIteration);
        var workerCounts = progress.EvaluationsByWorker ?? new Dictionary<string, int>();
        WriteCount(writer, workerCounts.Count);
        foreach (var workerCount in workerCounts)
        {
            WriteRequiredString(writer, workerCount.Key);
            writer.Write(workerCount.Value);
        }
        WriteDoubles(writer, progress.BestParameters ?? Array.Empty<double>());
        var activeWorkers = progress.ActiveWorkerIds ?? Array.Empty<string>();
        WriteCount(writer, activeWorkers.Count);
        foreach (var workerId in activeWorkers)
            WriteRequiredString(writer, workerId);
    }

    public static SharedEstimationProgress ReadProgress(BinaryReader reader)
    {
        var (_, messageType) = ReadHeader(reader);
        if (messageType != SharedEstimationMessageType.Progress)
            throw new InvalidDataException($"Expected {SharedEstimationMessageType.Progress} message but received {messageType}.");
        return ReadProgressPayload(reader);
    }

    internal static SharedEstimationProgress ReadProgressPayload(BinaryReader reader)
    {
        var runId = ReadRequiredString(reader);
        var iteration = reader.ReadInt32();
        var bestFitness = reader.ReadDouble();
        var evaluationsCompleted = reader.ReadInt32();
        var evaluationsPending = reader.ReadInt32();
        var activeWorkers = reader.ReadInt32();
        var fitnessTestsThisIteration = reader.ReadInt32();
        var count = ReadCount(reader);
        var workerCounts = new Dictionary<string, int>(count, StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
            workerCounts.Add(ReadRequiredString(reader), reader.ReadInt32());
        var bestParameters = ReadDoubles(reader);
        var activeWorkerIds = new string[ReadCount(reader)];
        for (var index = 0; index < activeWorkerIds.Length; index++)
            activeWorkerIds[index] = ReadRequiredString(reader);
        return new SharedEstimationProgress(runId, iteration, bestFitness, evaluationsCompleted,
            evaluationsPending, activeWorkers, fitnessTestsThisIteration, workerCounts, bestParameters,
            activeWorkerIds);
    }

    public static void WriteCompletion(BinaryWriter writer, SharedEstimationCompletion completion)
    {
        WriteHeader(writer, SharedEstimationMessageType.Complete);
        WriteCompletionPayload(writer, completion);
    }

    private static void WriteCompletionPayload(BinaryWriter writer, SharedEstimationCompletion completion)
    {
        WriteRequiredString(writer, completion.RunId);
        writer.Write(completion.Succeeded);
        writer.Write(completion.BestFitness);
        WriteDoubles(writer, completion.BestParameters);
        writer.Write(completion.TotalEvaluations);
        writer.Write(completion.Iterations);
        WriteOptionalString(writer, completion.FailureReason);
    }

    public static SharedEstimationCompletion ReadCompletion(BinaryReader reader)
    {
        ReadExpectedHeader(reader, SharedEstimationMessageType.Complete);
        return ReadCompletionPayload(reader);
    }

    internal static SharedEstimationCompletion ReadCompletionPayload(BinaryReader reader)
    {
        return new SharedEstimationCompletion(
            ReadRequiredString(reader),
            reader.ReadBoolean(),
            reader.ReadDouble(),
            ReadDoubles(reader),
            reader.ReadInt32(),
            reader.ReadInt32(),
            ReadOptionalString(reader));
    }

    public static void WriteCancel(BinaryWriter writer, string runId, string? reason)
    {
        WriteHeader(writer, SharedEstimationMessageType.Cancel);
        WriteRequiredString(writer, runId);
        WriteOptionalString(writer, reason);
    }

    public static (string RunId, string? Reason) ReadCancel(BinaryReader reader)
    {
        ReadExpectedHeader(reader, SharedEstimationMessageType.Cancel);
        return ReadCancelPayload(reader);
    }

    internal static (string RunId, string? Reason) ReadCancelPayload(BinaryReader reader)
    {
        return (ReadRequiredString(reader), ReadOptionalString(reader));
    }

    public static void WriteQueryJobs(BinaryWriter writer)
    {
        WriteHeader(writer, SharedEstimationMessageType.QueryJobs);
    }

    public static void ReadQueryJobs(BinaryReader reader)
    {
        ReadExpectedHeader(reader, SharedEstimationMessageType.QueryJobs);
    }

    public static void WriteJobSnapshots(BinaryWriter writer, IReadOnlyList<SharedEstimationJobSnapshot> snapshots)
    {
        WriteHeader(writer, SharedEstimationMessageType.JobSnapshots);
        WriteCount(writer, snapshots.Count);
        foreach (var snapshot in snapshots)
        {
            WriteRequiredString(writer, snapshot.RunId);
            writer.Write((int)snapshot.State);
            writer.Write(snapshot.Progress is not null);
            if (snapshot.Progress is not null)
                WriteProgressPayload(writer, snapshot.Progress);
            writer.Write(snapshot.Completion is not null);
            if (snapshot.Completion is not null)
                WriteCompletionPayload(writer, snapshot.Completion);
            WriteRequiredString(writer, snapshot.RunName);
            WriteRequiredString(writer, snapshot.WorkingDirectory);
            WriteRequiredString(writer, snapshot.ModelSystemHash);
            var workers = snapshot.ActiveWorkerIds ?? Array.Empty<string>();
            WriteCount(writer, workers.Count);
            foreach (var workerId in workers)
                WriteRequiredString(writer, workerId);
            var metadata = snapshot.Parameters ?? Array.Empty<SharedEstimationParameterMetadata>();
            WriteCount(writer, metadata.Count);
            foreach (var parameter in metadata)
            {
                writer.Write(parameter.NodeIndex);
                WriteRequiredString(writer, parameter.Name);
                writer.Write(parameter.Min);
                writer.Write(parameter.Max);
            }
            WriteOptionalGuid(writer, snapshot.ProjectId);
            WriteOptionalGuid(writer, snapshot.ModelSystemId);
            WriteOptionalGuid(writer, snapshot.OwnerUserId);
            var configuredWorkerCounts = snapshot.ConfiguredWorkerCounts ??
                new Dictionary<string, int>();
            WriteCount(writer, configuredWorkerCounts.Count);
            foreach (var workerCount in configuredWorkerCounts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                WriteRequiredString(writer, workerCount.Key);
                writer.Write(workerCount.Value);
            }
        }
    }

    public static IReadOnlyList<SharedEstimationJobSnapshot> ReadJobSnapshots(BinaryReader reader)
    {
        var (_, messageType) = ReadHeader(reader);
        if (messageType != SharedEstimationMessageType.JobSnapshots)
            throw new InvalidDataException($"Expected {SharedEstimationMessageType.JobSnapshots} message but received {messageType}.");
        return ReadJobSnapshotsPayload(reader);
    }

    internal static IReadOnlyList<SharedEstimationJobSnapshot> ReadJobSnapshotsPayload(BinaryReader reader)
    {
        var snapshots = new List<SharedEstimationJobSnapshot>(ReadCount(reader));
        for (int i = 0; i < snapshots.Capacity; i++)
        {
            var runId = ReadRequiredString(reader);
            var state = (SharedEstimationJobState)reader.ReadInt32();
            if (!Enum.IsDefined(state))
                throw new InvalidDataException($"Unknown shared estimation job state: {(int)state}.");
            var progress = reader.ReadBoolean() ? ReadProgressPayload(reader) : null;
            var completion = reader.ReadBoolean() ? ReadCompletionPayload(reader) : null;
            var runName = ReadRequiredString(reader);
            var workingDirectory = ReadRequiredString(reader);
            var modelSystemHash = ReadRequiredString(reader);
            var workerIds = new string[ReadCount(reader)];
            for (var workerIndex = 0; workerIndex < workerIds.Length; workerIndex++)
                workerIds[workerIndex] = ReadRequiredString(reader);
            var metadata = new SharedEstimationParameterMetadata[ReadCount(reader)];
            for (var parameterIndex = 0; parameterIndex < metadata.Length; parameterIndex++)
            {
                var nodeIndex = reader.ReadInt32();
                if (nodeIndex < 0)
                    throw new InvalidDataException("Parameter node indices must be non-negative.");
                metadata[parameterIndex] = new SharedEstimationParameterMetadata(nodeIndex,
                    ReadRequiredString(reader), reader.ReadDouble(), reader.ReadDouble());
            }
            var projectId = ReadOptionalGuid(reader);
            var modelSystemId = ReadOptionalGuid(reader);
            var ownerUserId = ReadOptionalGuid(reader);
            var configuredWorkerCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var workerCount = ReadCount(reader);
            for (var workerIndex = 0; workerIndex < workerCount; workerIndex++)
            {
                var workerId = ReadRequiredString(reader);
                var count = reader.ReadInt32();
                if (count < 1 || count > 32)
                    throw new InvalidDataException("Configured worker counts must be between 1 and 32.");
                configuredWorkerCounts.Add(workerId, count);
            }
            snapshots.Add(new SharedEstimationJobSnapshot(runId, state, progress, completion,
                workerIds, runName, workingDirectory, modelSystemHash, metadata, projectId, modelSystemId,
                ownerUserId, configuredWorkerCounts));
        }
        return snapshots;
    }

    public static void WriteQueryRemoteRuns(BinaryWriter writer)
        => WriteHeader(writer, SharedEstimationMessageType.QueryRemoteRuns);

    public static void ReadQueryRemoteRuns(BinaryReader reader)
        => ReadExpectedHeader(reader, SharedEstimationMessageType.QueryRemoteRuns);

    public static void WriteRemoteRunSnapshots(BinaryWriter writer, IReadOnlyList<RemoteRunSnapshot> snapshots)
    {
        WriteHeader(writer, SharedEstimationMessageType.RemoteRunSnapshots);
        WriteCount(writer, snapshots.Count);
        foreach (var snapshot in snapshots)
        {
            WriteRequiredString(writer, snapshot.RunId);
            WriteRequiredString(writer, snapshot.RunName);
            writer.Write((int)snapshot.RunMode);
            WriteRequiredString(writer, snapshot.WorkingDirectory);
            WriteRequiredString(writer, snapshot.StartToExecute);
            WriteRequiredString(writer, snapshot.ModelSystemHash);
            writer.Write((int)snapshot.State);
            WriteRequiredString(writer, snapshot.Status);
            writer.Write(snapshot.Iteration);
            writer.Write(snapshot.Fitness);
            WriteRemoteRunValues(writer, snapshot.ProgressParameters);
            writer.Write(snapshot.OptimizationResults is not null);
            if (snapshot.OptimizationResults is not null)
                WriteRemoteRunValues(writer, snapshot.OptimizationResults);
            WriteOptionalString(writer, snapshot.ErrorMessage);
            WriteOptionalString(writer, snapshot.ErrorStack);
            writer.Write(snapshot.ArtifactsAvailable);
            writer.Write(snapshot.UpdatedAt.UtcTicks);
            WriteOptionalGuid(writer, snapshot.ProjectId);
            WriteOptionalGuid(writer, snapshot.ModelSystemId);
            WriteOptionalGuid(writer, snapshot.OwnerUserId);
        }
    }

    public static IReadOnlyList<RemoteRunSnapshot> ReadRemoteRunSnapshots(BinaryReader reader)
    {
        var (_, messageType) = ReadHeader(reader);
        if (messageType != SharedEstimationMessageType.RemoteRunSnapshots)
            throw new InvalidDataException($"Expected {SharedEstimationMessageType.RemoteRunSnapshots} message but received {messageType}.");

        return ReadRemoteRunSnapshotsPayload(reader);
    }

    internal static IReadOnlyList<RemoteRunSnapshot> ReadRemoteRunSnapshotsPayload(BinaryReader reader)
    {
        int count = ReadCount(reader);
        var snapshots = new List<RemoteRunSnapshot>(count);
        for (int i = 0; i < count; i++)
        {
            var runId = ReadRequiredString(reader);
            var runName = ReadRequiredString(reader);
            var runMode = (RunMode)reader.ReadInt32();
            if (!Enum.IsDefined(runMode))
                throw new InvalidDataException($"Unknown remote run mode: {(int)runMode}.");
            var workingDirectory = ReadRequiredString(reader);
            var startToExecute = ReadRequiredString(reader);
            var modelSystemHash = ReadRequiredString(reader);
            var state = (RemoteRunState)reader.ReadInt32();
            if (!Enum.IsDefined(state))
                throw new InvalidDataException($"Unknown remote run state: {(int)state}.");
            var status = ReadRequiredString(reader);
            var iteration = reader.ReadInt32();
            if (iteration < 0)
                throw new InvalidDataException("Remote run iteration cannot be negative.");
            var fitness = reader.ReadDouble();
            var progress = ReadRemoteRunValues(reader);
            var optimization = reader.ReadBoolean() ? ReadRemoteRunValues(reader) : null;
            var errorMessage = ReadOptionalString(reader);
            var errorStack = ReadOptionalString(reader);
            var artifactsAvailable = reader.ReadBoolean();
            var updatedAt = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);
            var projectId = ReadOptionalGuid(reader);
            var modelSystemId = ReadOptionalGuid(reader);
            var ownerUserId = ReadOptionalGuid(reader);
            snapshots.Add(new RemoteRunSnapshot(runId, runName, runMode, workingDirectory,
                startToExecute, modelSystemHash, state, status, iteration, fitness,
                progress, optimization, errorMessage, errorStack, artifactsAvailable, updatedAt,
                projectId, modelSystemId, ownerUserId));
        }
        return snapshots;
    }

    public static void WriteGetRemoteRunArtifacts(BinaryWriter writer, string runId)
    {
        WriteHeader(writer, SharedEstimationMessageType.GetRemoteRunArtifacts);
        WriteRequiredString(writer, runId);
    }

    public static string ReadGetRemoteRunArtifacts(BinaryReader reader)
    {
        ReadExpectedHeader(reader, SharedEstimationMessageType.GetRemoteRunArtifacts);
        return ReadGetRemoteRunArtifactsPayload(reader);
    }

    internal static string ReadGetRemoteRunArtifactsPayload(BinaryReader reader)
        => ReadRequiredString(reader);

    public static void WriteRemoteRunArtifacts(BinaryWriter writer, string runId, Stream? archive,
        long archiveLength, string? error = null)
    {
        WriteHeader(writer, SharedEstimationMessageType.RemoteRunArtifacts);
        WriteRequiredString(writer, runId);
        writer.Write(archive is not null);
        if (archive is not null)
        {
            if (archiveLength < 0)
                throw new ArgumentOutOfRangeException(nameof(archiveLength));
            writer.Write(archiveLength);
            CopyExactly(archive, writer.BaseStream, archiveLength);
        }
        WriteOptionalString(writer, error);
    }

    public static RemoteRunArtifactsResponse ReadRemoteRunArtifacts(BinaryReader reader,
        Stream archiveDestination, string archivePath)
    {
        ReadExpectedHeader(reader, SharedEstimationMessageType.RemoteRunArtifacts);
        return ReadRemoteRunArtifactsPayload(reader, archiveDestination, archivePath);
    }

    internal static RemoteRunArtifactsResponse ReadRemoteRunArtifactsPayload(BinaryReader reader,
        Stream archiveDestination, string archivePath)
    {
        var runId = ReadRequiredString(reader);
        var hasArchive = reader.ReadBoolean();
        if (hasArchive)
        {
            var archiveLength = reader.ReadInt64();
            if (archiveLength < 0)
                throw new InvalidDataException($"Invalid remote run artifact length: {archiveLength}.");
            CopyExactly(reader.BaseStream, archiveDestination, archiveLength);
        }
        return new RemoteRunArtifactsResponse(runId, hasArchive ? archivePath : null,
            ReadOptionalString(reader));
    }

    public static void WriteAcknowledgeRemoteRun(BinaryWriter writer, string runId)
    {
        WriteHeader(writer, SharedEstimationMessageType.AcknowledgeRemoteRun);
        WriteRequiredString(writer, runId);
    }

    public static string ReadAcknowledgeRemoteRun(BinaryReader reader)
    {
        ReadExpectedHeader(reader, SharedEstimationMessageType.AcknowledgeRemoteRun);
        return ReadAcknowledgeRemoteRunPayload(reader);
    }

    internal static string ReadAcknowledgeRemoteRunPayload(BinaryReader reader)
        => ReadRequiredString(reader);

    public static void WriteDeleteRemoteRun(BinaryWriter writer, string requestId, string runId)
    {
        WriteHeader(writer, SharedEstimationMessageType.DeleteRemoteRun);
        WriteRequiredString(writer, requestId);
        WriteRequiredString(writer, runId);
    }

    public static (string RequestId, string RunId) ReadDeleteRemoteRun(BinaryReader reader)
    {
        ReadExpectedHeader(reader, SharedEstimationMessageType.DeleteRemoteRun);
        return ReadDeleteRemoteRunPayload(reader);
    }

    internal static (string RequestId, string RunId) ReadDeleteRemoteRunPayload(BinaryReader reader)
        => (ReadRequiredString(reader), ReadRequiredString(reader));

    public static void WriteRemoteRunDeleted(BinaryWriter writer, RemoteRunDeletionResponse response)
    {
        WriteHeader(writer, SharedEstimationMessageType.RemoteRunDeleted);
        WriteRequiredString(writer, response.RequestId);
        WriteRequiredString(writer, response.RunId);
        writer.Write(response.Deleted);
        WriteOptionalString(writer, response.Error);
    }

    public static RemoteRunDeletionResponse ReadRemoteRunDeleted(BinaryReader reader)
    {
        ReadExpectedHeader(reader, SharedEstimationMessageType.RemoteRunDeleted);
        return ReadRemoteRunDeletedPayload(reader);
    }

    internal static RemoteRunDeletionResponse ReadRemoteRunDeletedPayload(BinaryReader reader)
        => new(ReadRequiredString(reader), ReadRequiredString(reader), reader.ReadBoolean(), ReadOptionalString(reader));

    public static void WriteQueryServerActivity(BinaryWriter writer, string requestId)
    {
        WriteHeader(writer, SharedEstimationMessageType.QueryServerActivity);
        WriteRequiredString(writer, requestId);
    }

    public static string ReadQueryServerActivity(BinaryReader reader)
    {
        ReadExpectedHeader(reader, SharedEstimationMessageType.QueryServerActivity);
        return ReadQueryServerActivityPayload(reader);
    }

    public static string ReadQueryServerActivityPayload(BinaryReader reader)
        => ReadRequiredString(reader);

    public static void WriteServerActivitySnapshots(BinaryWriter writer, string requestId,
        IReadOnlyList<RunServerActivity> activities)
    {
        if (activities.Count > MaxServerActivities)
            throw new ArgumentOutOfRangeException(nameof(activities), $"Activity response cannot exceed {MaxServerActivities} entries.");

        WriteHeader(writer, SharedEstimationMessageType.ServerActivitySnapshots);
        WriteRequiredString(writer, requestId);
        writer.Write(activities.Count);
        foreach (var activity in activities)
        {
            WriteRequiredString(writer, activity.RunId);
            WriteRequiredString(writer, activity.RunName);
            WriteRequiredString(writer, activity.Kind);
            writer.Write((int)activity.State);
            WriteOptionalString(writer, activity.Status);
            writer.Write(activity.QueuePosition);
            writer.Write(activity.Iteration);
            writer.Write(activity.Fitness);
            writer.Write(activity.ActiveWorkers);
            writer.Write(activity.EvaluationsCompleted);
            writer.Write(activity.EvaluationsPending);
        }
    }

    public static RunServerActivityResponse ReadServerActivitySnapshots(BinaryReader reader)
    {
        var (_, messageType) = ReadHeader(reader);
        if (messageType != SharedEstimationMessageType.ServerActivitySnapshots)
            throw new InvalidDataException($"Expected {SharedEstimationMessageType.ServerActivitySnapshots} message but received {messageType}.");
        return ReadServerActivitySnapshotsPayload(reader);
    }

    internal static RunServerActivityResponse ReadServerActivitySnapshotsPayload(BinaryReader reader)
    {
        var requestId = ReadRequiredString(reader);
        int count = reader.ReadInt32();
        if (count < 0 || count > MaxServerActivities)
            throw new InvalidDataException($"Invalid RunServer activity count: {count}.");
        var activities = new RunServerActivity[count];
        for (int i = 0; i < count; i++)
        {
            var runId = ReadRequiredString(reader);
            var runName = ReadRequiredString(reader);
            var kind = ReadRequiredString(reader);
            var state = (RunServerActivityState)reader.ReadInt32();
            if (!Enum.IsDefined(state))
                throw new InvalidDataException($"Unknown RunServer activity state: {(int)state}.");
            var status = ReadOptionalString(reader) ?? string.Empty;
            int queuePosition = reader.ReadInt32();
            int iteration = reader.ReadInt32();
            double fitness = reader.ReadDouble();
            int activeWorkers = reader.ReadInt32();
            int evaluationsCompleted = reader.ReadInt32();
            int evaluationsPending = reader.ReadInt32();
            if (queuePosition < 0 || iteration < 0 || activeWorkers < 0 ||
                evaluationsCompleted < 0 || evaluationsPending < 0)
                throw new InvalidDataException("RunServer activity counters cannot be negative.");
            activities[i] = new RunServerActivity(runId, runName, kind, state, status,
                queuePosition, iteration, fitness, activeWorkers, evaluationsCompleted, evaluationsPending);
        }
        return new RunServerActivityResponse(requestId, activities);
    }

    private static void WriteRemoteRunValues(BinaryWriter writer, IReadOnlyList<RemoteRunParameterValue> values)
    {
        WriteCount(writer, values.Count);
        foreach (var value in values)
        {
            if (value.NodeIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(values), "Remote run parameter indices must be non-negative.");
            writer.Write(value.NodeIndex);
            writer.Write(value.Value);
        }
    }

    private static IReadOnlyList<RemoteRunParameterValue> ReadRemoteRunValues(BinaryReader reader)
    {
        int count = ReadCount(reader);
        var values = new RemoteRunParameterValue[count];
        for (int i = 0; i < count; i++)
        {
            int nodeIndex = reader.ReadInt32();
            if (nodeIndex < 0)
                throw new InvalidDataException("Remote run parameter indices must be non-negative.");
            values[i] = new RemoteRunParameterValue(nodeIndex, reader.ReadDouble());
        }
        return values;
    }

    private static void ReadExpectedHeader(BinaryReader reader, SharedEstimationMessageType expected)
    {
        var (_, actual) = ReadHeader(reader);
        if (actual != expected)
            throw new InvalidDataException($"Expected {expected} message but received {actual}.");
    }

    private static void WriteRequiredString(BinaryWriter writer, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Protocol strings must not be empty.", nameof(value));
        writer.Write(value);
    }

    private static string ReadRequiredString(BinaryReader reader)
    {
        var value = reader.ReadString();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException("Protocol strings must not be empty.");
        return value;
    }

    private static void WriteOptionalString(BinaryWriter writer, string? value)
        => writer.Write(value ?? string.Empty);

    private static string? ReadOptionalString(BinaryReader reader)
    {
        var value = reader.ReadString();
        return value.Length == 0 ? null : value;
    }

    private static void WriteOptionalGuid(BinaryWriter writer, Guid? value)
    {
        writer.Write(value.HasValue);
        if (value.HasValue)
            writer.Write(value.Value.ToByteArray());
    }

    private static Guid? ReadOptionalGuid(BinaryReader reader)
    {
        if (!reader.ReadBoolean())
            return null;
        var bytes = reader.ReadBytes(16);
        return bytes.Length == 16
            ? new Guid(bytes)
            : throw new EndOfStreamException("The shared estimation identity was truncated.");
    }

    private static void WriteBytes(BinaryWriter writer, byte[] bytes)
    {
        WriteCount(writer, bytes.Length);
        writer.Write(bytes);
    }

    private static byte[] ReadBytes(BinaryReader reader)
    {
        int count = ReadCount(reader);
        return reader.ReadBytes(count) is { Length: var actual } bytes && actual == count
            ? bytes
            : throw new EndOfStreamException("The shared estimation payload was truncated.");
    }

    private static void CopyExactly(Stream source, Stream destination, long length)
    {
        var buffer = new byte[81920];
        while (length > 0)
        {
            int read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, length));
            if (read == 0)
                throw new EndOfStreamException("The remote run artifact archive was truncated.");
            destination.Write(buffer, 0, read);
            length -= read;
        }
    }

    private static void WriteDoubles(BinaryWriter writer, IReadOnlyList<double> values)
    {
        WriteCount(writer, values.Count);
        for (int i = 0; i < values.Count; i++)
            writer.Write(values[i]);
    }

    private static IReadOnlyList<double> ReadDoubles(BinaryReader reader)
    {
        int count = ReadCount(reader);
        var values = new double[count];
        for (int i = 0; i < count; i++)
            values[i] = reader.ReadDouble();
        return values;
    }

    private static void WriteCount(BinaryWriter writer, int count)
    {
        if (count < 0 || count > MaxCollectionLength)
            throw new ArgumentOutOfRangeException(nameof(count));
        writer.Write(count);
    }

    private static int ReadCount(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > MaxCollectionLength)
            throw new InvalidDataException($"Invalid shared estimation collection length: {count}.");
        return count;
    }
}
