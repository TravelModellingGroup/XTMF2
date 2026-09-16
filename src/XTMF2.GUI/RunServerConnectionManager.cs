using System;
using System.Collections.Generic;
using System.IO;
using XTMF2.Bus;
using XTMF2.GUI.Properties;

namespace XTMF2.GUI;

/// <summary>
/// Owns the active TCP connections to configured RunServers.
/// </summary>
public sealed class RunServerConnectionManager : IDisposable
{
    private readonly Dictionary<string, HostBus> _connections = new(StringComparer.Ordinal);
    private bool _disposed;

    public IReadOnlyCollection<string> ConnectedEndpointIds => _connections.Keys;

    public bool AddConnection(RunServerEndpoint endpoint, Stream stream, out string? error)
    {
        error = null;
        if (_disposed)
        {
            error = "The RunServer connection manager has been disposed.";
            stream.Dispose();
            return false;
        }

        if (_connections.ContainsKey(endpoint.Id))
        {
            error = $"A connection already exists for RunServer '{endpoint.Name}'.";
            stream.Dispose();
            return false;
        }

        _connections.Add(endpoint.Id, new HostBus(stream, true));
        return true;
    }

    public bool AddConnection(RunServerEndpoint endpoint, HostBus hostBus, out string? error)
    {
        error = null;
        if (_disposed)
        {
            error = "The RunServer connection manager has been disposed.";
            hostBus.Dispose();
            return false;
        }

        if (_connections.ContainsKey(endpoint.Id))
        {
            error = $"A connection already exists for RunServer '{endpoint.Name}'.";
            hostBus.Dispose();
            return false;
        }

        _connections.Add(endpoint.Id, hostBus);
        return true;
    }

    public bool Connect(RunServerEndpoint endpoint, out string? error)
    {
        if (!CreateStreams.CreateTcpClient(endpoint.Address, endpoint.Port, out var stream, out error))
            return false;

        return AddConnection(endpoint, stream!, out error);
    }

    public bool TryGet(string endpointId, out HostBus? hostBus)
        => _connections.TryGetValue(endpointId, out hostBus);

    public bool Remove(string endpointId)
    {
        if (!_connections.Remove(endpointId, out var hostBus))
            return false;

        hostBus.RequestClientShutdown(out _);
        hostBus.Dispose();
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var hostBus in _connections.Values)
        {
            hostBus.RequestClientShutdown(out _);
            hostBus.Dispose();
        }
        _connections.Clear();
    }
}
