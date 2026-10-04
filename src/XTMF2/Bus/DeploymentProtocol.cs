using System;
using System.IO;

namespace XTMF2.Bus;

public enum DeploymentMessageType
{
    BeginDrain = 0,
    DrainStatus = 1,
    CancelDrain = 2,
    DrainCompleted = 3,
    DeploymentFailed = 4,
    DeployArchive = 5,
    DeploymentStaged = 6,
    ActivateDeployment = 7
}

public enum RunServerDrainState
{
    Draining = 0,
    Idle = 1,
    Cancelled = 2,
    Failed = 3,
    Staged = 4
}

public sealed record RunServerDrainStatus(
    string RequestId,
    RunServerDrainState State,
    int ActiveCount,
    int QueuedCount,
    string? Message);

public static class DeploymentProtocol
{
    private const int ProtocolVersion = 1;
    private const int MaxStringLength = 1024;

    public static void WriteBeginDrain(BinaryWriter writer, string requestId)
    {
        WriteHeader(writer, DeploymentMessageType.BeginDrain);
        WriteString(writer, requestId);
    }

    public static void WriteCancelDrain(BinaryWriter writer, string requestId)
    {
        WriteHeader(writer, DeploymentMessageType.CancelDrain);
        WriteString(writer, requestId);
    }

    public static void WriteDrainStatus(BinaryWriter writer, RunServerDrainStatus status)
    {
        WriteHeader(writer, DeploymentMessageType.DrainStatus);
        WriteStatusPayload(writer, status);
    }

    public static void WriteDrainCompleted(BinaryWriter writer, RunServerDrainStatus status)
    {
        WriteHeader(writer, DeploymentMessageType.DrainCompleted);
        WriteStatusPayload(writer, status);
    }

    public static void WriteDeploymentStaged(BinaryWriter writer, RunServerDrainStatus status)
    {
        WriteHeader(writer, DeploymentMessageType.DeploymentStaged);
        WriteStatusPayload(writer, status);
    }

    public static void WriteDeploymentFailed(BinaryWriter writer, string requestId, string message)
    {
        WriteHeader(writer, DeploymentMessageType.DeploymentFailed);
        WriteString(writer, requestId);
        WriteString(writer, message);
    }

    public static void WriteDeployArchive(BinaryWriter writer, string requestId, byte[] archive, string sha256)
    {
        WriteHeader(writer, DeploymentMessageType.DeployArchive);
        WriteString(writer, requestId);
        if (archive.Length > 512 * 1024 * 1024)
            throw new ArgumentException("Deployment archive exceeds the 512 MB limit.", nameof(archive));
        writer.Write(archive.Length);
        WriteString(writer, sha256);
        writer.Write(archive);
    }

    public static void WriteActivateDeployment(BinaryWriter writer, string requestId)
    {
        WriteHeader(writer, DeploymentMessageType.ActivateDeployment);
        WriteString(writer, requestId);
    }

    public static (int Version, DeploymentMessageType Type) ReadHeader(BinaryReader reader)
    {
        var version = reader.ReadInt32();
        if (version != ProtocolVersion)
            throw new InvalidDataException($"Unsupported deployment protocol version {version}.");
        return (version, (DeploymentMessageType)reader.ReadInt32());
    }

    public static string ReadRequestId(BinaryReader reader) => ReadString(reader);

    public static RunServerDrainStatus ReadStatusPayload(BinaryReader reader)
    {
        var requestId = ReadString(reader);
        var state = (RunServerDrainState)reader.ReadInt32();
        if (!Enum.IsDefined(state))
            throw new InvalidDataException("Unknown RunServer drain state.");
        var activeCount = reader.ReadInt32();
        var queuedCount = reader.ReadInt32();
        if (activeCount < 0 || queuedCount < 0)
            throw new InvalidDataException("RunServer activity counts cannot be negative.");
        var message = reader.ReadBoolean() ? ReadString(reader) : null;
        return new RunServerDrainStatus(requestId, state, activeCount, queuedCount, message);
    }

    public static string ReadFailureMessage(BinaryReader reader) => ReadString(reader);

    public static (string RequestId, byte[] Archive, string Sha256) ReadDeployArchive(BinaryReader reader)
    {
        var requestId = ReadString(reader);
        var length = reader.ReadInt32();
        if (length < 0 || length > 512 * 1024 * 1024)
            throw new InvalidDataException("Deployment archive length is invalid.");
        var sha256 = ReadString(reader);
        var archive = reader.ReadBytes(length);
        if (archive.Length != length)
            throw new EndOfStreamException("Deployment archive was truncated.");
        return (requestId, archive, sha256);
    }

    private static void WriteHeader(BinaryWriter writer, DeploymentMessageType type)
    {
        writer.Write(ProtocolVersion);
        writer.Write((int)type);
    }

    private static void WriteStatusPayload(BinaryWriter writer, RunServerDrainStatus status)
    {
        WriteString(writer, status.RequestId);
        writer.Write((int)status.State);
        writer.Write(status.ActiveCount);
        writer.Write(status.QueuedCount);
        writer.Write(status.Message is not null);
        if (status.Message is not null)
            WriteString(writer, status.Message);
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxStringLength)
            throw new ArgumentException("Deployment protocol strings must be non-empty and bounded.", nameof(value));
        writer.Write(value);
    }

    private static string ReadString(BinaryReader reader)
    {
        var value = reader.ReadString();
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxStringLength)
            throw new InvalidDataException("Deployment protocol string is empty or too large.");
        return value;
    }
}