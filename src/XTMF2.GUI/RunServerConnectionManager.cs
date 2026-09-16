using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using XTMF2.Bus;
using XTMF2.GUI.Properties;

namespace XTMF2.GUI;

public enum RunServerConnectionState
{
    Connecting,
    Available,
    Disconnected
}

public sealed record RunServerConnectionInfo(
    RunServerEndpoint Endpoint,
    RunServerConnectionState State,
    string? Error);

/// <summary>
/// Owns active RunServer connections and retries disconnected endpoints.
/// </summary>
public sealed class RunServerConnectionManager : IDisposable
{
    private sealed class Entry
    {
        internal RunServerEndpoint Endpoint = null!;
        internal HostBus? HostBus;
        internal RunServerConnectionState State;
        internal string? Error;
    }

    private readonly object _sync = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Timer _retryTimer;
    private bool _disposed;

    public event Action<RunServerConnectionInfo>? StateChanged;

    public RunServerConnectionManager()
    {
        _retryTimer = new Timer(_ => RetryDisconnected(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public bool AddConnection(RunServerEndpoint endpoint, Stream stream, out string? error)
    {
        var hostBus = new HostBus(stream, true);
        return AddConnection(endpoint, hostBus, out error);
    }

    public bool AddConnection(RunServerEndpoint endpoint, HostBus hostBus, out string? error)
    {
        error = null;
        RunServerConnectionInfo? state = null;
        lock (_sync)
        {
            if (_disposed)
            {
                error = "The RunServer connection manager has been disposed.";
                hostBus.Dispose();
                return false;
            }

            if (_entries.TryGetValue(endpoint.Id, out var existing) && existing.HostBus is not null)
            {
                error = $"A connection already exists for RunServer '{endpoint.Name}'.";
                hostBus.Dispose();
                return false;
            }

            var entry = GetOrCreateEntry(endpoint);
            entry.HostBus = hostBus;
            entry.State = RunServerConnectionState.Available;
            entry.Error = null;
            hostBus.Disconnected += (_, _) => MarkDisconnected(endpoint.Id, "The RunServer connection was closed.");
            state = Snapshot(entry);
        }

        Publish(state);
        return true;
    }

    public bool Connect(RunServerEndpoint endpoint, out string? error)
    {
        RunServerConnectionInfo? state = null;
        lock (_sync)
        {
            if (_disposed)
            {
                error = "The RunServer connection manager has been disposed.";
                return false;
            }

            var entry = GetOrCreateEntry(endpoint);
            if (entry.HostBus is not null)
            {
                error = null;
                return true;
            }
            if (entry.State == RunServerConnectionState.Connecting)
            {
                error = "A connection attempt is already in progress.";
                return false;
            }
            entry.State = RunServerConnectionState.Connecting;
            entry.Error = null;
            state = Snapshot(entry);
        }

        Publish(state);

        if (!CreateStreams.CreateTcpClient(endpoint.Address, endpoint.Port, out var stream, out error))
        {
            MarkDisconnected(endpoint.Id, error ?? "Unable to connect to the RunServer.");
            return false;
        }

        return AddConnection(endpoint, stream!, out error);
    }

    public bool TryGet(string endpointId, out HostBus? hostBus)
    {
        lock (_sync)
        {
            if (_entries.TryGetValue(endpointId, out var entry) && entry.State == RunServerConnectionState.Available)
            {
                hostBus = entry.HostBus;
                return hostBus is not null;
            }
            hostBus = null;
            return false;
        }
    }

    public IReadOnlyList<RunServerConnectionInfo> GetStates()
    {
        lock (_sync)
        {
            return _entries.Values
                .Select(entry => new RunServerConnectionInfo(entry.Endpoint.Clone(), entry.State, entry.Error))
                .ToArray();
        }
    }

    public bool Remove(string endpointId)
    {
        Entry? entry;
        lock (_sync)
        {
            if (!_entries.Remove(endpointId, out entry))
                return false;
        }

        DisposeHostBus(entry.HostBus);
        return true;
    }

    public void Dispose()
    {
        Entry[] entries;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            entries = _entries.Values.ToArray();
            _entries.Clear();
        }

        _retryTimer.Dispose();
        foreach (var entry in entries)
            DisposeHostBus(entry.HostBus);
    }

    private Entry GetOrCreateEntry(RunServerEndpoint endpoint)
    {
        if (!_entries.TryGetValue(endpoint.Id, out var entry))
        {
            entry = new Entry { Endpoint = endpoint.Clone(), State = RunServerConnectionState.Disconnected };
            _entries.Add(endpoint.Id, entry);
        }
        return entry;
    }

    private void RetryDisconnected()
    {
        RunServerEndpoint[] endpoints;
        lock (_sync)
        {
            if (_disposed) return;
            endpoints = _entries.Values
                .Where(entry => entry.State == RunServerConnectionState.Disconnected && !entry.Endpoint.IsLocal)
                .Select(entry => entry.Endpoint.Clone())
                .ToArray();
        }

        foreach (var endpoint in endpoints)
            Connect(endpoint, out _);
    }

    private void MarkDisconnected(string endpointId, string error)
    {
        HostBus? hostBus;
        RunServerConnectionInfo? state;
        lock (_sync)
        {
            if (_disposed || !_entries.TryGetValue(endpointId, out var entry))
                return;

            hostBus = entry.HostBus;
            entry.HostBus = null;
            entry.State = RunServerConnectionState.Disconnected;
            entry.Error = error;
            state = Snapshot(entry);
        }

        Publish(state);

        if (hostBus is not null)
            ThreadPool.QueueUserWorkItem(_ => DisposeHostBus(hostBus));
    }

    private static RunServerConnectionInfo Snapshot(Entry entry)
        => new(entry.Endpoint.Clone(), entry.State, entry.Error);

    private void Publish(RunServerConnectionInfo? state)
        => StateChanged?.Invoke(state!);

    private static void DisposeHostBus(HostBus? hostBus)
    {
        if (hostBus is null) return;
        hostBus.RequestClientShutdown(out _);
        hostBus.Dispose();
    }
}
