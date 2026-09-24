using System;
using System.Collections.Generic;
using System.IO;
using XTMF2.Bus.Optimization;

namespace XTMF2.Bus;

public enum SharedEstimationMessageType
{
    StartRun = 0,
    AddWorker = 1,
    RemoveWorker = 2,
    EvaluateCandidates = 3,
    EvaluationResults = 4,
    Progress = 5,
    Complete = 6,
    Cancel = 7,
    StartCoordinator = 8,
    QueryJobs = 9,
    JobSnapshots = 10,
    AddCoordinatorWorker = 11,
    RemoveCoordinatorWorker = 12,
    WorkerControlAcknowledgement = 13
}

public enum SharedEstimationJobState
{
    Running = 0,
    Completed = 1
}

public sealed record SharedEstimationJobSnapshot(
    string RunId,
    SharedEstimationJobState State,
    SharedEstimationProgress? Progress,
    SharedEstimationCompletion? Completion,
    IReadOnlyList<string>? ActiveWorkerIds = null);

public sealed record SharedEstimationWorkerControlAcknowledgement(
    string RunId,
    string WorkerId,
    bool Add,
    bool Succeeded,
    string? Error,
    int ActiveWorkerCount);

public sealed record SharedEstimationRunRequest(
    string RunId,
    string WorkingDirectory,
    string StartToExecute,
    byte[] ModelSystem,
    IReadOnlyDictionary<int, string>? BasicParameterOverrides = null);

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
    IReadOnlyDictionary<int, string>? BasicParameterOverrides = null);

public sealed record SharedEstimationCoordinatorRequest(
    SharedEstimationRunRequest Run,
    IReadOnlyList<SharedEstimationWorkerEndpoint> Workers,
    string AlgorithmId,
    IReadOnlyList<AlgorithmParameterDescriptor> AlgorithmParameters,
    IReadOnlyList<double> LowerBounds,
    IReadOnlyList<double> UpperBounds,
    IReadOnlyList<double> InitialValues,
    bool IsMaximize);

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
    int ActiveWorkers);

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
    private const int ExtendedRunRequestVersion = 2;
    private const int MaxCollectionLength = 1_000_000;

    public static void WriteHeader(BinaryWriter writer, SharedEstimationMessageType messageType, int version = Version)
    {
        writer.Write(version);
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
    }

    public static SharedEstimationCoordinatorRequest ReadCoordinatorRequest(BinaryReader reader)
    {
        ReadExpectedHeader(reader, SharedEstimationMessageType.StartCoordinator);
        return ReadCoordinatorRequestPayload(reader);
    }

    internal static SharedEstimationCoordinatorRequest ReadCoordinatorRequestPayload(BinaryReader reader)
    {
        var run = new SharedEstimationRunRequest(
            ReadRequiredString(reader), ReadRequiredString(reader), ReadRequiredString(reader),
            ReadBytes(reader), ReadOverrides(reader));
        var workers = new List<SharedEstimationWorkerEndpoint>(ReadCount(reader));
        for (int i = 0; i < workers.Capacity; i++)
        {
            workers.Add(new SharedEstimationWorkerEndpoint(
                ReadRequiredString(reader), ReadRequiredString(reader), ReadRequiredString(reader),
                reader.ReadInt32(), ReadRequiredString(reader), ReadRequiredString(reader), ReadOverrides(reader)));
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
        return new SharedEstimationCoordinatorRequest(run, workers, algorithmId,
            parameters, ReadDoubles(reader), ReadDoubles(reader), ReadDoubles(reader), reader.ReadBoolean());
    }

    public static (int Version, SharedEstimationMessageType MessageType) ReadHeader(BinaryReader reader)
    {
        int version = reader.ReadInt32();
        var messageType = (SharedEstimationMessageType)reader.ReadInt32();
        if (version != Version && version != ExtendedRunRequestVersion)
            throw new InvalidDataException($"Unsupported shared estimation protocol version: {version}.");
        if (!Enum.IsDefined(messageType))
            throw new InvalidDataException($"Unknown shared estimation message type: {(int)messageType}.");
        return (version, messageType);
    }

    public static void WriteRunRequest(BinaryWriter writer, SharedEstimationRunRequest request)
    {
        WriteHeader(writer, SharedEstimationMessageType.StartRun,
            request.BasicParameterOverrides is { Count: > 0 } ? ExtendedRunRequestVersion : Version);
        WriteRequiredString(writer, request.RunId);
        WriteRequiredString(writer, request.WorkingDirectory);
        WriteRequiredString(writer, request.StartToExecute);
        WriteBytes(writer, request.ModelSystem);
        var overrides = request.BasicParameterOverrides;
        if (overrides is null or { Count: 0 })
            return;
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
    }

    private static void WriteRunRequestPayload(BinaryWriter writer, SharedEstimationRunRequest request)
    {
        WriteRequiredString(writer, request.RunId);
        WriteRequiredString(writer, request.WorkingDirectory);
        WriteRequiredString(writer, request.StartToExecute);
        WriteBytes(writer, request.ModelSystem);
        WriteOverrides(writer, request.BasicParameterOverrides);
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

    public static SharedEstimationRunRequest ReadRunRequest(BinaryReader reader)
    {
        var (version, messageType) = ReadHeader(reader);
        if (messageType != SharedEstimationMessageType.StartRun)
            throw new InvalidDataException($"Expected {SharedEstimationMessageType.StartRun} message but received {messageType}.");
        return ReadRunRequestPayload(reader, version);
    }

    internal static SharedEstimationRunRequest ReadRunRequestPayload(BinaryReader reader, int version = Version)
    {
        return new SharedEstimationRunRequest(
            ReadRequiredString(reader),
            ReadRequiredString(reader),
            ReadRequiredString(reader),
            ReadBytes(reader),
            version == ExtendedRunRequestVersion ? ReadOverrides(reader) : null);
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
            reader.ReadInt32(), ReadRequiredString(reader), ReadRequiredString(reader), ReadOverrides(reader)));

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

    public static void WriteCandidates(BinaryWriter writer, IReadOnlyList<SharedEstimationCandidate> candidates)
    {
        WriteHeader(writer, SharedEstimationMessageType.EvaluateCandidates);
        WriteCount(writer, candidates.Count);
        foreach (var candidate in candidates)
        {
            WriteRequiredString(writer, candidate.RunId);
            writer.Write(candidate.BatchId);
            WriteRequiredString(writer, candidate.CandidateId);
            WriteDoubles(writer, candidate.Parameters);
        }
    }

    public static IReadOnlyList<SharedEstimationCandidate> ReadCandidates(BinaryReader reader)
    {
        ReadExpectedHeader(reader, SharedEstimationMessageType.EvaluateCandidates);
        return ReadCandidatesPayload(reader);
    }

    internal static IReadOnlyList<SharedEstimationCandidate> ReadCandidatesPayload(BinaryReader reader)
    {
        int count = ReadCount(reader);
        var candidates = new List<SharedEstimationCandidate>(count);
        for (int i = 0; i < count; i++)
        {
            candidates.Add(new SharedEstimationCandidate(
                ReadRequiredString(reader),
                reader.ReadInt64(),
                ReadRequiredString(reader),
                ReadDoubles(reader)));
        }
        return candidates;
    }

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

    public static void WriteResults(BinaryWriter writer, IReadOnlyList<SharedEstimationEvaluationResult> results)
    {
        WriteHeader(writer, SharedEstimationMessageType.EvaluationResults);
        WriteCount(writer, results.Count);
        foreach (var result in results)
        {
            WriteRequiredString(writer, result.RunId);
            writer.Write(result.BatchId);
            WriteRequiredString(writer, result.CandidateId);
            writer.Write(result.Fitness);
            WriteOptionalString(writer, result.Error);
            WriteOptionalString(writer, result.ModuleName);
            writer.Write(result.ElementId?.ToString() ?? string.Empty);
        }
    }

    public static IReadOnlyList<SharedEstimationEvaluationResult> ReadResults(BinaryReader reader)
    {
        ReadExpectedHeader(reader, SharedEstimationMessageType.EvaluationResults);
        return ReadResultsPayload(reader);
    }

    internal static IReadOnlyList<SharedEstimationEvaluationResult> ReadResultsPayload(BinaryReader reader)
    {
        int count = ReadCount(reader);
        var results = new List<SharedEstimationEvaluationResult>(count);
        for (int i = 0; i < count; i++)
        {
            var runId = ReadRequiredString(reader);
            var batchId = reader.ReadInt64();
            var candidateId = ReadRequiredString(reader);
            var fitness = reader.ReadDouble();
            var error = ReadOptionalString(reader);
            var moduleName = ReadOptionalString(reader);
            var elementIdText = reader.ReadString();
            results.Add(new SharedEstimationEvaluationResult(
                runId,
                batchId,
                candidateId,
                fitness,
                error,
                moduleName,
                Guid.TryParse(elementIdText, out var elementId) ? elementId : null));
        }
        return results;
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
    }

    public static SharedEstimationProgress ReadProgress(BinaryReader reader)
    {
        ReadExpectedHeader(reader, SharedEstimationMessageType.Progress);
        return ReadProgressPayload(reader);
    }

    internal static SharedEstimationProgress ReadProgressPayload(BinaryReader reader)
    {
        return new SharedEstimationProgress(
            ReadRequiredString(reader),
            reader.ReadInt32(),
            reader.ReadDouble(),
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadInt32());
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
        }
    }

    public static IReadOnlyList<SharedEstimationJobSnapshot> ReadJobSnapshots(BinaryReader reader)
    {
        ReadExpectedHeader(reader, SharedEstimationMessageType.JobSnapshots);
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
            snapshots.Add(new SharedEstimationJobSnapshot(runId, state, progress, completion));
        }
        return snapshots;
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
