using System;
using System.Collections.Generic;
using System.Linq;

namespace XTMF2.Bus;

/// <summary>
/// Owns the shared-estimation coordinator and its authenticated worker connections.
/// </summary>
public sealed class SharedEstimationWorkerPool : IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<string, SharedEstimationWorkerConnection> _connections =
        new(StringComparer.Ordinal);
    private bool _disposed;
    private SharedEstimationRunRequest? _activeRun;
    private IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>>? _activeOverrides;

    public SharedEstimationWorkerPool()
    {
        Coordinator = new SharedEstimationCoordinator();
        Coordinator.WorkerRemoved += OnWorkerRemoved;
    }

    public SharedEstimationCoordinator Coordinator { get; }

    public int WorkerCount => Coordinator.ActiveWorkerCount;

    public IReadOnlyList<string> WorkerIds
    {
        get
        {
            lock (_sync)
                return _connections.Keys.ToArray();
        }
    }

    public bool AddExistingWorker(
        string workerId,
        string endpointId,
        HostBus bus,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(bus);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_connections.ContainsKey(workerId))
            {
                error = $"A worker with ID '{workerId}' is already registered.";
                return false;
            }
        }

        var connection = SharedEstimationWorkerConnection.Attach(workerId, endpointId, bus);
        if (!Coordinator.AddWorker(connection, out error))
        {
            connection.Dispose();
            return false;
        }

        lock (_sync)
        {
            if (_disposed)
            {
                Coordinator.RemoveWorker(workerId, out _);
                error = "The worker pool was disposed while adding the connection.";
                return false;
            }
            _connections.Add(workerId, connection);
            if (_activeRun is not null
                && !connection.Bus.StartSharedEstimation(CreateWorkerRequest(workerId), out var commandError))
            {
                _connections.Remove(workerId);
                Coordinator.RemoveWorker(workerId, out _);
                error = commandError?.Message ?? "Unable to start the active shared-estimation run.";
                return false;
            }
        }

        error = null;
        return true;
    }

    public bool AddWorker(
        string workerId,
        string endpointId,
        string address,
        int port,
        string token,
        string certificateFingerprint,
        out string? error,
        int timeoutMilliseconds = 5000)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_connections.ContainsKey(workerId))
            {
                error = $"A worker with ID '{workerId}' is already registered.";
                return false;
            }
        }

        if (!SharedEstimationWorkerConnection.TryConnect(
                workerId, endpointId, address, port, token, certificateFingerprint,
                out var connection, out error, timeoutMilliseconds))
            return false;

        if (!Coordinator.AddWorker(connection!, out error))
        {
            connection!.Dispose();
            return false;
        }

        lock (_sync)
        {
            if (_disposed)
            {
                Coordinator.RemoveWorker(workerId, out _);
                error = "The worker pool was disposed while connecting.";
                return false;
            }
            _connections.Add(workerId, connection!);
            if (_activeRun is not null
                && !connection.Bus.StartSharedEstimation(CreateWorkerRequest(workerId), out var commandError))
            {
                _connections.Remove(workerId);
                Coordinator.RemoveWorker(workerId, out _);
                error = commandError?.Message ?? "Unable to start the active shared-estimation run.";
                return false;
            }
        }

        error = null;
        return true;
    }

    public bool RemoveWorker(string workerId, out string? error)
    {
        SharedEstimationWorkerConnection? connection;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_connections.Remove(workerId, out connection))
            {
                error = $"Worker '{workerId}' is not registered.";
                return false;
            }
        }

        if (_activeRun is not null)
            connection.Bus.CancelSharedEstimation(_activeRun.RunId, "Worker removed.", out _);
        var removed = Coordinator.RemoveWorker(workerId, out error);
        if (!removed)
            connection.Dispose();
        return removed;
    }

    public bool StartRun(
        SharedEstimationRunRequest request,
        out string? error,
        IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>>? overridesByWorker = null)
    {
        List<SharedEstimationWorkerConnection> connections;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_activeRun is not null)
            {
                error = "A shared-estimation run is already active.";
                return false;
            }
            _activeRun = request;
            _activeOverrides = overridesByWorker;
            connections = [.. _connections.Values];
        }

        var startedConnections = new List<SharedEstimationWorkerConnection>(connections.Count);
        foreach (var connection in connections)
        {
            if (!connection.Bus.StartSharedEstimation(CreateWorkerRequest(connection.WorkerId), out var commandError))
            {
                lock (_sync)
                {
                    if (ReferenceEquals(_activeRun, request))
                    {
                        _activeRun = null;
                        _activeOverrides = null;
                    }
                }
                foreach (var startedConnection in startedConnections)
                    startedConnection.Bus.CancelSharedEstimation(request.RunId, "Shared-estimation startup failed.", out _);
                error = commandError?.Message ??
                    $"Unable to start shared-estimation run on worker '{connection.WorkerId}'.";
                return false;
            }
            startedConnections.Add(connection);
        }

        error = null;
        return true;
    }

    public void CancelRun(string? reason = null)
    {
        SharedEstimationRunRequest? activeRun;
        SharedEstimationWorkerConnection[] connections;
        lock (_sync)
        {
            if (_disposed)
                return;
            activeRun = _activeRun;
            connections = [.. _connections.Values];
            _activeRun = null;
            _activeOverrides = null;
        }

        if (activeRun is null)
            return;
        foreach (var connection in connections)
            connection.Bus.CancelSharedEstimation(activeRun.RunId, reason, out _);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            _activeRun = null;
            _activeOverrides = null;
            _connections.Clear();
        }
        Coordinator.Dispose();
    }

    private void OnWorkerRemoved(string workerId)
    {
        lock (_sync)
            _connections.Remove(workerId);
    }

    private SharedEstimationRunRequest CreateWorkerRequest(string workerId)
    {
        if (_activeRun is null || _activeOverrides is null
            || !_activeOverrides.TryGetValue(workerId, out var overrides))
            return _activeRun!;
        return _activeRun with { BasicParameterOverrides = overrides };
    }
}