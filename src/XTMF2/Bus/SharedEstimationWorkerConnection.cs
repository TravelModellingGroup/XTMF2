using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics.CodeAnalysis;
using XTMF2.Editing;

namespace XTMF2.Bus;

public sealed class SharedEstimationWorkerConnection : ISharedEstimationWorker
{
    private bool _disposed;
    private readonly bool _ownsBus;

    private SharedEstimationWorkerConnection(string workerId, string endpointId, HostBus bus, bool ownsBus)
    {
        WorkerId = workerId;
        EndpointId = endpointId;
        Bus = bus;
        _ownsBus = ownsBus;
        Bus.SharedEstimationResultsAvailable += OnResultsReceived;
        Bus.Disconnected += OnDisconnected;
    }

    public string WorkerId { get; }

    public string EndpointId { get; }

    public HostBus Bus { get; }

    public event EventHandler<IReadOnlyList<SharedEstimationEvaluationResult>>? ResultsReceived;

    public event EventHandler? Disconnected;

    public static bool TryConnect(
        string workerId,
        string endpointId,
        string address,
        int port,
        string token,
        string certificateFingerprint,
        [NotNullWhen(true)] out SharedEstimationWorkerConnection? connection,
        [NotNullWhen(false)] out string? error,
        int timeoutMilliseconds = 5000)
    {
        connection = null;
        if (string.IsNullOrWhiteSpace(workerId))
        {
            error = "A worker ID is required.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(endpointId))
        {
            error = "A worker endpoint ID is required.";
            return false;
        }

        if (!CreateStreams.CreateSecureTcpClient(address, port, token, certificateFingerprint,
            out var stream, out error, timeoutMilliseconds))
            return false;

        try
        {
            connection = new SharedEstimationWorkerConnection(workerId, endpointId, new HostBus(stream, true), true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            stream.Dispose();
            error = ex.Message;
            return false;
        }
    }

    internal static SharedEstimationWorkerConnection Attach(
        string workerId, string endpointId, HostBus bus)
        => new(workerId, endpointId, bus, false);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Bus.SharedEstimationResultsAvailable -= OnResultsReceived;
        Bus.Disconnected -= OnDisconnected;
        if (_ownsBus)
            Bus.Dispose();
    }

    public bool SendCandidates(IReadOnlyList<SharedEstimationCandidate> candidates, out string? error)
    {
        var sent = Bus.SendSharedEstimationCandidates(candidates, out CommandError? commandError);
        error = commandError?.Message;
        return sent;
    }

    private void OnResultsReceived(object? sender, IReadOnlyList<SharedEstimationEvaluationResult> results)
        => ResultsReceived?.Invoke(this, results);

    private void OnDisconnected(object? sender, EventArgs e)
        => Disconnected?.Invoke(this, e);
}
