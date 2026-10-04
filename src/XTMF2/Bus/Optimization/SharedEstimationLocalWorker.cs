using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using XTMF2.Bus;

namespace XTMF2.Bus.Optimization;

/// <summary>Evaluates shared-estimation candidates in a dedicated Run process.</summary>
public sealed class SharedEstimationLocalWorker : ISharedEstimationWorker
{
    private readonly SharedEstimationWorkerProcess _workerProcess;
    private bool _disposed;

    private SharedEstimationLocalWorker(string workerId, SharedEstimationWorkerProcess workerProcess)
    {
        WorkerId = workerId;
        _workerProcess = workerProcess;
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
        ArgumentNullException.ThrowIfNull(runtime);
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var coreAssemblyPath = Path.GetFullPath(typeof(RunServerBus).Assembly.Location);
        var extraDlls = runtime.SystemConfiguration.Modules.LoadedModuleTypes
            .Select(moduleType => moduleType.Assembly)
            .Where(assembly => !assembly.IsDynamic)
            .Select(assembly => assembly.Location)
            .Where(path => !string.IsNullOrWhiteSpace(path)
                && !string.Equals(Path.GetFullPath(path), coreAssemblyPath, pathComparison))
            .Distinct(pathComparison == StringComparison.OrdinalIgnoreCase
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal)
            .ToArray();
        return TryCreate(workerId, request, extraDlls, out worker, out error);
    }

    public static bool TryCreate(
        string workerId,
        SharedEstimationRunRequest request,
        IReadOnlyList<string> extraDlls,
        [NotNullWhen(true)] out SharedEstimationLocalWorker? worker,
        [NotNullWhen(false)] out string? error)
    {
        worker = null;
        if (string.IsNullOrWhiteSpace(workerId))
        {
            error = "A coordinator worker ID is required.";
            return false;
        }
        if (!SharedEstimationWorkerProcess.TryStart(request, extraDlls, out var workerProcess, out error))
        {
            error ??= "Unable to start the coordinator estimation worker process.";
            return false;
        }

        worker = new SharedEstimationLocalWorker(workerId, workerProcess!);
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

        var result = _workerProcess.Evaluate(candidate);
        ResultReceived?.Invoke(this, result);
        error = null;
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _workerProcess.Dispose();
    }
}
