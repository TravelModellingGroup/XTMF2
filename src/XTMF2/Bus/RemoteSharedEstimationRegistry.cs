using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using XTMF2.Bus.Optimization;

namespace XTMF2.Bus;

/// <summary>
/// Owns remote shared-estimation jobs for the lifetime of a RunServer process.
/// Connection sessions attach only as observers and never own job lifetime.
/// </summary>
public sealed class RemoteSharedEstimationRegistry : IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<string, RemoteJob> _jobs = new(StringComparer.Ordinal);
    private bool _disposed;

    public void Start(RunServerBus observer, SharedEstimationCoordinatorRequest request)
    {
        ArgumentNullException.ThrowIfNull(observer);
        ArgumentNullException.ThrowIfNull(request);

        RemoteJob job;
        lock (_sync)
        {
            if (_disposed || _jobs.ContainsKey(request.Run.RunId))
                return;

            job = new RemoteJob(this, observer, request);
            _jobs.Add(request.Run.RunId, job);
            job.Start();
        }
    }

    public void Cancel(string runId, string? reason)
    {
        lock (_sync)
        {
            if (_jobs.TryGetValue(runId, out var job))
                job.Cancel(reason ?? "Cancelled by host.");
        }
    }

    public SharedEstimationWorkerControlAcknowledgement ChangeWorker(
        string runId, SharedEstimationWorkerEndpoint worker, bool add)
    {
        lock (_sync)
        {
            if (!_jobs.TryGetValue(runId, out var job))
                return new(runId, worker.WorkerId, add, false, "The remote estimation job was not found.", 0);
            return job.ChangeWorker(worker, add);
        }
    }

    public void Detach(RunServerBus observer)
    {
        lock (_sync)
        {
            foreach (var job in _jobs.Values)
                job.Detach(observer);
        }
    }

    public void SendSnapshots(RunServerBus observer)
    {
        SharedEstimationJobSnapshot[] snapshots;
        lock (_sync)
        {
            snapshots = _jobs.Values.Select(job =>
            {
                job.Attach(observer);
                return job.GetSnapshot();
            }).ToArray();
        }
        observer.SendSharedEstimationJobSnapshots(snapshots);
    }

    public void Dispose()
    {
        RemoteJob[] jobs;
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            jobs = _jobs.Values.ToArray();
        }

        foreach (var job in jobs)
            job.Cancel("RunServer is shutting down.");
    }

    private void Complete(RemoteJob job)
    {
        lock (_sync)
        {
            if (_jobs.TryGetValue(job.RunId, out var current) && ReferenceEquals(current, job))
                job.MarkCompleted();
        }
    }

    private sealed class RemoteJob
    {
        private readonly RemoteSharedEstimationRegistry _registry;
        private readonly SharedEstimationCoordinatorRequest _request;
        private readonly object _sync = new();
        private readonly CancellationTokenSource _cancellation = new();
        private RunServerBus? _observer;
        private SharedEstimationWorkerPool? _pool;
        private Task? _task;
        private bool _completed;
        private SharedEstimationProgress? _progress;
        private SharedEstimationCompletion? _completion;

        public RemoteJob(RemoteSharedEstimationRegistry registry,
            RunServerBus observer, SharedEstimationCoordinatorRequest request)
        {
            _registry = registry;
            _observer = observer;
            _request = request;
        }

        public string RunId => _request.Run.RunId;

        public void Start()
        {
            _task = Task.Run(Execute, CancellationToken.None);
        }

        public void Detach(RunServerBus observer)
        {
            lock (_sync)
            {
                if (ReferenceEquals(_observer, observer))
                    _observer = null;
            }
        }

        public void Attach(RunServerBus observer)
        {
            lock (_sync)
                _observer = observer;
        }

        public SharedEstimationJobSnapshot GetSnapshot()
        {
            lock (_sync)
            {
                return new SharedEstimationJobSnapshot(RunId,
                    _completed ? SharedEstimationJobState.Completed : SharedEstimationJobState.Running,
                    _progress, _completion, _pool?.WorkerIds);
            }
        }

        public SharedEstimationWorkerControlAcknowledgement ChangeWorker(
            SharedEstimationWorkerEndpoint worker, bool add)
        {
            lock (_sync)
            {
                if (_completed || _pool is null)
                    return new(RunId, worker.WorkerId, add, false, "The remote estimation job is no longer active.", 0);
                if (add && _pool.WorkerIds.Contains(worker.WorkerId, StringComparer.Ordinal))
                    return new(RunId, worker.WorkerId, true, false, "The worker is already active.", _pool.WorkerCount);
                if (!add && !_pool.WorkerIds.Contains(worker.WorkerId, StringComparer.Ordinal))
                    return new(RunId, worker.WorkerId, false, false, "The worker is not active.", _pool.WorkerCount);
                if (!add && _pool.WorkerCount <= 1)
                    return new(RunId, worker.WorkerId, false, false, "At least one worker must remain active.", _pool.WorkerCount);

                var succeeded = add
                    ? _pool.AddWorker(worker.WorkerId, worker.EndpointId, worker.Address, worker.Port,
                        worker.Token, worker.CertificateFingerprint, out var error)
                    : _pool.RemoveWorker(worker.WorkerId, out error);
                return new(RunId, worker.WorkerId, add, succeeded,
                    succeeded ? null : error, _pool.WorkerCount);
            }
        }

        public void Cancel(string reason)
        {
            lock (_sync)
            {
                if (_completed)
                    return;
                _cancellation.Cancel();
                _pool?.CancelRun(reason);
            }
        }

        public void MarkCompleted()
        {
            lock (_sync)
            {
                _completed = true;
                _pool = null;
                _cancellation.Dispose();
            }
        }

        private void Execute()
        {
            SharedEstimationCompletion completion;
            SharedEstimationWorkerPool? pool = null;
            try
            {
                if (_request.Workers.Count == 0)
                    throw new InvalidOperationException("A remote estimation coordinator requires at least one worker.");
                if (_request.LowerBounds.Count != _request.UpperBounds.Count ||
                    _request.LowerBounds.Count != _request.InitialValues.Count)
                    throw new InvalidOperationException("The remote estimation parameter bounds are inconsistent.");

                var config = EstimationAlgorithmConfig.Create(_request.AlgorithmId)
                    ?? throw new InvalidOperationException($"Unknown estimation algorithm '{_request.AlgorithmId}'.");
                var configError = config.ApplyParameters(_request.AlgorithmParameters);
                if (configError is not null)
                    throw new InvalidOperationException(configError);

                pool = new SharedEstimationWorkerPool();
                lock (_sync)
                    _pool = pool;
                foreach (var worker in _request.Workers)
                {
                    if (!pool.AddWorker(worker.WorkerId, worker.EndpointId, worker.Address, worker.Port,
                            worker.Token, worker.CertificateFingerprint, out var workerError))
                        throw new InvalidOperationException(workerError ?? $"Unable to connect to worker '{worker.EndpointId}'.");
                }

                var overrides = _request.Workers
                    .Where(worker => worker.BasicParameterOverrides is { Count: > 0 })
                    .ToDictionary(worker => worker.WorkerId,
                        worker => worker.BasicParameterOverrides!, StringComparer.Ordinal);
                if (!pool.StartRun(_request.Run, out var startError, overrides))
                    throw new InvalidOperationException(startError ?? "Unable to start shared estimation workers.");

                var algorithm = config.CreateAlgorithm(_request.LowerBounds.Count,
                    _request.LowerBounds.ToArray(), _request.UpperBounds.ToArray(), _request.InitialValues.ToArray(),
                    _request.IsMaximize);
                var runner = new SharedEstimationCoordinatorRun(_request.Run.RunId, algorithm, pool.Coordinator);
                completion = runner.Execute(
                    progress: progress =>
                    {
                        try
                        {
                            RunServerBus? observer;
                            lock (_sync)
                            {
                                _progress = progress;
                                observer = _observer;
                            }
                            observer?.SendSharedEstimationProgress(progress);
                        }
                        catch (IOException) { }
                        catch (ObjectDisposedException) { }
                    },
                    cancellationToken: _cancellation.Token);
            }
            catch (Exception exception)
            {
                completion = new SharedEstimationCompletion(_request.Run.RunId, false, double.MaxValue,
                    Array.Empty<double>(), 0, 0, exception.Message);
            }
            finally
            {
                pool?.Dispose();
                lock (_sync)
                    _pool = null;
            }

            PersistCompletion(_request.Run, completion);
            try
            {
                RunServerBus? observer;
                lock (_sync)
                {
                    _completion = completion;
                    observer = _observer;
                }
                observer?.SendSharedEstimationCompletion(completion);
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }

            _registry.Complete(this);
        }

        private static void PersistCompletion(SharedEstimationRunRequest request,
            SharedEstimationCompletion completion)
        {
            try
            {
                Directory.CreateDirectory(request.WorkingDirectory);
                var target = Path.Combine(request.WorkingDirectory, "estimation-completion.json");
                var temporary = target + ".tmp";
                File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(completion));
                File.Move(temporary, target, true);
            }
            catch
            {
            }
        }
    }
}
