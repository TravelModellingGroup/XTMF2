using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using XTMF2.Bus;

namespace XTMF2.Bus.Optimization;

/// <summary>Evaluates shared-estimation candidates in the coordinator RunServer process.</summary>
public sealed class SharedEstimationLocalWorker : ISharedEstimationWorker
{
    private readonly SharedEstimationWorkerParticipant _participant;
    private bool _disposed;

    private SharedEstimationLocalWorker(string workerId, SharedEstimationWorkerParticipant participant)
    {
        WorkerId = workerId;
        _participant = participant;
    }

    public string WorkerId { get; }

    public event EventHandler<SharedEstimationEvaluationResult>? ResultReceived;

    public event EventHandler? Disconnected
    {
        add { }
        remove { }
    }

    public static bool TryCreate(
        string workerId,
        XTMFRuntime runtime,
        SharedEstimationRunRequest request,
        [NotNullWhen(true)] out SharedEstimationLocalWorker? worker,
        [NotNullWhen(false)] out string? error)
    {
        worker = null;
        if (string.IsNullOrWhiteSpace(workerId))
        {
            error = "A coordinator worker ID is required.";
            return false;
        }
        if (!SharedEstimationWorkerParticipant.TryCreate(runtime, request, out var participant, out var runError))
        {
            error = runError?.Message ?? "Unable to prepare the coordinator as an estimation worker.";
            return false;
        }

        worker = new SharedEstimationLocalWorker(workerId, participant!);
        error = null;
        return true;
    }

    public bool SendCandidate(SharedEstimationCandidate candidate, out string? error)
    {
        if (_disposed)
        {
            error = "The coordinator worker has been disposed.";
            return false;
        }

        var result = _participant.Evaluate(candidate);
        ResultReceived?.Invoke(this, result);
        error = null;
        return true;
    }

    public void Dispose() => _disposed = true;
}
