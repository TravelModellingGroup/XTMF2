using System;

namespace XTMF2.Bus;

/// <summary>
/// Adapts one RunServer connection to the process-level remote estimation registry.
/// </summary>
public sealed class RemoteSharedEstimationCoordinatorSession : IDisposable
{
    private readonly RunServerBus _bus;
    private readonly RemoteSharedEstimationRegistry _registry;

    public RemoteSharedEstimationCoordinatorSession(RunServerBus bus, RemoteSharedEstimationRegistry registry)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _bus.SharedEstimationCoordinatorRequested += OnCoordinatorRequested;
        _bus.SharedEstimationCancellationRequested += OnCancellationRequested;
        _bus.SharedEstimationJobsQueryRequested += OnJobsQueryRequested;
        _bus.SharedEstimationCoordinatorWorkerRequested += OnCoordinatorWorkerRequested;
    }

    private void OnCoordinatorRequested(object _, SharedEstimationCoordinatorRequest request)
        => _registry.Start(_bus, request);

    private void OnCancellationRequested(object _, string runId, string? reason)
        => _registry.Cancel(runId, reason);

    private void OnJobsQueryRequested(object _)
        => _registry.SendSnapshots(_bus);

    private void OnCoordinatorWorkerRequested(object _, string runId, SharedEstimationWorkerEndpoint worker, bool remove)
    {
        var acknowledgement = _registry.ChangeWorker(
            runId, worker, add: !remove);
        try
        {
            _bus.SendSharedEstimationWorkerControlAcknowledgement(acknowledgement);
        }
        catch (System.IO.IOException)
        {
        }
    }

    public void Dispose()
    {
        _registry.Detach(_bus);
        _bus.SharedEstimationCoordinatorRequested -= OnCoordinatorRequested;
        _bus.SharedEstimationCancellationRequested -= OnCancellationRequested;
        _bus.SharedEstimationJobsQueryRequested -= OnJobsQueryRequested;
        _bus.SharedEstimationCoordinatorWorkerRequested -= OnCoordinatorWorkerRequested;
    }
}
