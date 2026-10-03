using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using XTMF2.Bus;
using XTMF2.Editing;

namespace XTMF2.Bus.Optimization;

public sealed class SharedEstimationWorkerConnection : ISharedEstimationWorker
{
    private bool _disposed;
    private int _disconnected;
    private readonly bool _ownsBus;

    private SharedEstimationWorkerConnection(string workerId, string endpointId, HostBus bus, bool ownsBus)
    {
        WorkerId = workerId;
        EndpointId = endpointId;
        Bus = bus;
        _ownsBus = ownsBus;
        Bus.SharedEstimationResultAvailable += OnResultReceived;
        Bus.SharedEstimationWorkerReadyAvailable += OnWorkerReady;
        Bus.Disconnected += OnDisconnected;
    }

    public string WorkerId { get; }

    public string EndpointId { get; }

    public HostBus Bus { get; }

    internal bool IsDisconnected => Volatile.Read(ref _disconnected) != 0;

    public event EventHandler<SharedEstimationEvaluationResult>? ResultReceived;

    public event EventHandler<SharedEstimationWorkerReady>? WorkerReadyReceived;

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
        Bus.SharedEstimationResultAvailable -= OnResultReceived;
        Bus.SharedEstimationWorkerReadyAvailable -= OnWorkerReady;
        Bus.Disconnected -= OnDisconnected;
        if (_ownsBus)
            Bus.Dispose();
    }

    public bool SendCandidate(SharedEstimationCandidate candidate, out string? error)
    {
        try
        {
            var sent = Bus.SendSharedEstimationCandidate(candidate, out CommandError? commandError);
            error = commandError?.Message;
            return sent;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            error = exception.Message;
            return false;
        }
    }

    private void OnResultReceived(object? sender, SharedEstimationEvaluationResult result)
        => ResultReceived?.Invoke(this, result);

    private void OnWorkerReady(object? sender, SharedEstimationWorkerReady readiness)
        => WorkerReadyReceived?.Invoke(this, readiness);

    private void OnDisconnected(object? sender, EventArgs e)
    {
        Interlocked.Exchange(ref _disconnected, 1);
        Disconnected?.Invoke(this, e);
    }
}
