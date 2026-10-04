using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using XTMF2.Bus;

namespace XTMF2.Bus.Optimization;

/// <summary>
/// Owns the shared-estimation coordinator and its authenticated worker connections.
/// </summary>
public sealed class SharedEstimationWorkerPool : IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<string, SharedEstimationWorkerConnection> _connections =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _awaitingReadiness = new(StringComparer.Ordinal);
    private bool _disposed;
    private bool _preparationFailureReported;
    private SharedEstimationRunRequest? _activeRun;
    private IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>>? _activeOverrides;

    public SharedEstimationWorkerPool()
    {
        Coordinator = new SharedEstimationCoordinator();
        Coordinator.WorkerRemoved += OnWorkerRemoved;
    }

    public SharedEstimationCoordinator Coordinator { get; }

    public event Action<string>? WorkerPreparationFailed;

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
        var connection = SharedEstimationWorkerConnection.Attach(workerId, endpointId, bus);
        return RegisterConnection(connection, out error);
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

        return RegisterConnection(connection!, out error);
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
            connection.WorkerReadyReceived -= OnWorkerReadyReceived;
            _awaitingReadiness.Remove(workerId);
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
            _preparationFailureReported = false;
            connections = [.. _connections.Values];
            _awaitingReadiness.Clear();
            foreach (var connection in connections)
            {
                _awaitingReadiness.Add(connection.WorkerId);
                Coordinator.SetWorkerReady(connection.WorkerId, false, out _);
            }
        }

        var failedEndpointIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var connection in connections)
        {
            bool started;
            try
            {
                started = connection.Bus.StartSharedEstimation(CreateWorkerRequest(connection.WorkerId), out _);
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException
                or InvalidOperationException or ArgumentException)
            {
                started = false;
            }
            if (!started)
            {
                failedEndpointIds.Add(connection.EndpointId);
                lock (_sync)
                    _awaitingReadiness.Remove(connection.WorkerId);
                Coordinator.RemoveWorker(connection.WorkerId, out _);
            }
        }

        foreach (var endpointId in failedEndpointIds)
        {
            foreach (var connection in connections.Where(connection => connection.EndpointId == endpointId))
            {
                connection.Bus.CancelSharedEstimation(request.RunId,
                    "A worker slot on this RunServer failed to start.", out _);
                Coordinator.RemoveWorker(connection.WorkerId, out _);
            }
        }

        error = null;
        return true;
    }

    private bool RegisterConnection(SharedEstimationWorkerConnection connection, out string? error)
    {
        var registered = false;
        lock (_sync)
        {
            if (_disposed)
            {
                error = "The worker pool has been disposed.";
            }
            else if (_connections.ContainsKey(connection.WorkerId))
            {
                error = $"A worker with ID '{connection.WorkerId}' is already registered.";
            }
            else
            {
                _connections.Add(connection.WorkerId, connection);
                connection.WorkerReadyReceived += OnWorkerReadyReceived;
                if (_activeRun is not null)
                    _awaitingReadiness.Add(connection.WorkerId);
                if (!Coordinator.AddWorker(connection, out error, isReady: _activeRun is null))
                {
                    _connections.Remove(connection.WorkerId);
                    _awaitingReadiness.Remove(connection.WorkerId);
                    connection.WorkerReadyReceived -= OnWorkerReadyReceived;
                }
                else if (_activeRun is not null &&
                         !connection.Bus.StartSharedEstimation(CreateWorkerRequest(connection.WorkerId), out var commandError))
                {
                    _awaitingReadiness.Remove(connection.WorkerId);
                    Coordinator.RemoveWorker(connection.WorkerId, out _);
                    error = commandError?.Message ?? "Unable to start the active shared-estimation run.";
                }
                else if (connection.IsDisconnected || !_connections.ContainsKey(connection.WorkerId))
                {
                    Coordinator.RemoveWorker(connection.WorkerId, out _);
                    error = "The worker disconnected while being added.";
                }
                else
                {
                    registered = true;
                    error = null;
                }
            }
        }

        if (!registered)
            connection.Dispose();
        return registered;
    }

    private void OnWorkerReadyReceived(object? sender, SharedEstimationWorkerReady readiness)
    {
        if (sender is not SharedEstimationWorkerConnection connection)
            return;

        bool removeWorker = false;
        lock (_sync)
        {
            if (!_connections.TryGetValue(connection.WorkerId, out var registered)
                || !ReferenceEquals(registered, connection)
                || _activeRun?.RunId != readiness.RunId)
                return;
            _awaitingReadiness.Remove(connection.WorkerId);

            if (readiness.Succeeded)
                Coordinator.SetWorkerReady(connection.WorkerId, true, out _);
            else
                removeWorker = true;
        }

        if (removeWorker)
        {
            ReportPreparationFailureIfNoWorkers(readiness.Error
                ?? $"Worker '{connection.WorkerId}' failed to prepare for shared estimation.");
            Coordinator.RemoveWorker(connection.WorkerId, out _);
        }
    }

    private void ReportPreparationFailureIfNoWorkers(string reason)
    {
        bool failed;
        lock (_sync)
        {
            failed = _activeRun is not null && !_preparationFailureReported
                && _awaitingReadiness.Count == 0 && Coordinator.ActiveWorkerCount == 0;
            if (failed)
                _preparationFailureReported = true;
        }
        if (failed)
            WorkerPreparationFailed?.Invoke(reason);
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
            _awaitingReadiness.Clear();
        }

        if (activeRun is null)
            return;
        foreach (var connection in connections)
            connection.Bus.CancelSharedEstimation(activeRun.RunId, reason, out _);
    }

    public void Dispose()
    {
        SharedEstimationRunRequest? activeRun;
        SharedEstimationWorkerConnection[] connections;
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            activeRun = _activeRun;
            connections = _connections.Values.ToArray();
            _activeRun = null;
            _activeOverrides = null;
            _awaitingReadiness.Clear();
            foreach (var connection in _connections.Values)
                connection.WorkerReadyReceived -= OnWorkerReadyReceived;
            _connections.Clear();
        }

        if (activeRun is not null)
        {
            foreach (var connection in connections)
                connection.Bus.CancelSharedEstimation(activeRun.RunId, "Shared estimation worker released.", out _);
        }
        Coordinator.Dispose();
    }

    private void OnWorkerRemoved(string workerId)
    {
        lock (_sync)
        {
            if (_connections.Remove(workerId, out var connection))
            {
                _awaitingReadiness.Remove(workerId);
                connection.WorkerReadyReceived -= OnWorkerReadyReceived;
            }
        }
        ReportPreparationFailureIfNoWorkers($"Worker '{workerId}' was removed before becoming ready.");
    }

    private SharedEstimationRunRequest CreateWorkerRequest(string workerId)
    {
        lock (_sync)
        {
            if (_activeRun is null)
                throw new InvalidOperationException("There is no active shared-estimation run.");

            if (!_connections.TryGetValue(workerId, out var worker))
                return _activeRun;
            var slotCount = _connections.Values.Count(connection => connection.EndpointId == worker.EndpointId);
            IReadOnlyDictionary<int, string>? overrides = null;
            _activeOverrides?.TryGetValue(workerId, out overrides);
            return _activeRun with
            {
                BasicParameterOverrides = overrides ?? _activeRun.BasicParameterOverrides,
                WorkerSlotCount = Math.Max(1, slotCount)
            };
        }
    }
}