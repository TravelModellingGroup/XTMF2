using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace XTMF2.Bus;

public enum RemoteRunState
{
    Running,
    Completed,
    Failed,
    Interrupted
}

public sealed record RemoteRunParameterValue(int NodeIndex, double Value);

public sealed record RemoteRunArtifactsResponse(string RunId, byte[]? Archive, string? Error);

public sealed record RemoteRunSnapshot(
    string RunId,
    string RunName,
    RunMode RunMode,
    string WorkingDirectory,
    string StartToExecute,
    string ModelSystemHash,
    RemoteRunState State,
    string Status,
    int Iteration,
    double Fitness,
    IReadOnlyList<RemoteRunParameterValue> ProgressParameters,
    IReadOnlyList<RemoteRunParameterValue>? OptimizationResults,
    string? ErrorMessage,
    string? ErrorStack,
    bool ArtifactsAvailable,
    DateTimeOffset UpdatedAt,
    Guid? ProjectId = null,
    Guid? ModelSystemId = null,
    Guid? OwnerUserId = null);

/// <summary>
/// Owns remote run scheduling and retained run state for the lifetime of the RunServer process.
/// </summary>
public sealed class RemoteRunRegistry : IDisposable
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    private readonly object _sync = new();
    private readonly Dictionary<string, RemoteRunJob> _jobs = new(StringComparer.Ordinal);
    private readonly Scheduler _scheduler;
    private readonly string _storageDirectory;
    private bool _disposed;

    public RemoteRunRegistry(string? storageDirectory = null)
    {
        _storageDirectory = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XTMF2", "RemoteRuns");
        Directory.CreateDirectory(_storageDirectory);
        _scheduler = new Scheduler(runLocal: false);
        LoadPersistedJobs();
    }

    public bool Submit(RunServerBus observer, RunContext context, string runName,
        RunMode runMode, string workingDirectory, string startToExecute, byte[] modelSystem,
        Guid? projectId = null, Guid? modelSystemId = null, Guid? ownerUserId = null)
    {
        ArgumentNullException.ThrowIfNull(observer);
        ArgumentNullException.ThrowIfNull(context);

        var initial = new RemoteRunSnapshot(context.ID, runName, runMode, workingDirectory,
            startToExecute, Convert.ToHexString(SHA256.HashData(modelSystem)), RemoteRunState.Running,
            "Run submitted.", 0, double.NaN, Array.Empty<RemoteRunParameterValue>(), null,
            null, null, false, DateTimeOffset.UtcNow, projectId, modelSystemId, ownerUserId);
        RemoteRunJob job;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_jobs.ContainsKey(context.ID))
                return false;
            job = new RemoteRunJob(this, observer, context, initial);
            _jobs.Add(context.ID, job);
            job.Persist();
        }

        _scheduler.Run(context, job);
        return true;
    }

    internal Scheduler.Reservation ReserveWorkerSlot(RunContext context)
        => _scheduler.Reserve(context);

    public void Attach(RunServerBus observer)
    {
        lock (_sync)
        {
            foreach (var job in _jobs.Values)
                job.Attach(observer);
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

    public IReadOnlyList<RemoteRunSnapshot> GetSnapshots()
    {
        lock (_sync)
            return _jobs.Values.Select(job => job.GetSnapshot()).ToArray();
    }

    public IReadOnlyList<RunServerActivity> GetActiveActivities()
    {
        lock (_sync)
        {
            return _scheduler.GetInventory()
                .Select(item =>
                {
                    if (item.IsReservation)
                        return new RunServerActivity(item.Context.ID, "Shared estimation worker",
                            "Shared estimation worker",
                            item.IsRunning ? RunServerActivityState.Running : RunServerActivityState.Queued,
                            item.IsRunning ? "Worker has the RunServer execution slot." : "Waiting in the RunServer queue.",
                            item.QueuePosition, ActiveWorkers: item.IsRunning ? 1 : 0);
                    var snapshot = _jobs[item.Context.ID].GetSnapshot();
                    return new RunServerActivity(snapshot.RunId, snapshot.RunName,
                        snapshot.RunMode.ToString(),
                        item.IsRunning ? RunServerActivityState.Running : RunServerActivityState.Queued,
                        snapshot.Status, item.QueuePosition, snapshot.Iteration, snapshot.Fitness);
                })
                .ToArray();
        }
    }

    public bool TryReadArtifacts(string runId, out byte[]? archive)
    {
        RemoteRunJob? job;
        string path;
        lock (_sync)
        {
            if (!_jobs.TryGetValue(runId, out job))
            {
                archive = null;
                return false;
            }
            path = GetArtifactPath(runId);
        }

        try
        {
            if (!File.Exists(path))
            {
                var snapshot = job.GetSnapshot();
                if (snapshot.State == RemoteRunState.Running || !IsPrivateRunDirectory(runId, snapshot.WorkingDirectory) ||
                    !Directory.Exists(snapshot.WorkingDirectory))
                {
                    archive = null;
                    return false;
                }

                var temporary = path + ".tmp";
                if (File.Exists(temporary))
                    File.Delete(temporary);
                ZipFile.CreateFromDirectory(snapshot.WorkingDirectory, temporary, CompressionLevel.Fastest, false);
                File.Move(temporary, path, true);
                job.SetArtifactsAvailable();
            }
            archive = File.ReadAllBytes(path);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            archive = null;
            return false;
        }
    }

    public bool AcknowledgeReceived(string runId)
    {
        RemoteRunJob? job;
        lock (_sync)
        {
            if (!_jobs.TryGetValue(runId, out job) || job.GetSnapshot().State == RemoteRunState.Running)
                return false;
        }

        job.AcknowledgeReceived();
        try { File.Delete(GetArtifactPath(runId)); } catch (IOException) { }
        return true;
    }

    private static bool IsPrivateRunDirectory(string runId, string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(runId) || runId is "." or ".." ||
            runId.Contains(Path.DirectorySeparatorChar) || runId.Contains(Path.AltDirectorySeparatorChar) ||
            runId.Contains('\\'))
            return false;
        try
        {
            var actual = Path.GetFullPath(workingDirectory);
            var expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "XTMF2", "Runs", runId));
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!string.Equals(actual, expected, comparison))
                return false;
            if (File.Exists(actual))
                return false;
            return !Directory.Exists(actual) ||
                (File.GetAttributes(actual) & FileAttributes.ReparsePoint) == 0;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    public bool DeleteRun(string runId, out string? error)
    {
        error = null;
        lock (_sync)
        {
            if (!_jobs.TryGetValue(runId, out var job))
            {
                error = "The remote run was not found.";
                return false;
            }

            var snapshot = job.GetSnapshot();
            if (snapshot.State == RemoteRunState.Running)
            {
                error = "A running remote job cannot be deleted.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(runId) || runId is "." or ".." ||
                runId.Contains(Path.DirectorySeparatorChar) || runId.Contains(Path.AltDirectorySeparatorChar) ||
                runId.Contains('\\'))
            {
                error = "The remote run ID is invalid.";
                return false;
            }

            if (!IsPrivateRunDirectory(runId, snapshot.WorkingDirectory))
            {
                error = "The remote run directory is outside the RunServer's private run workspace.";
                return false;
            }

            try
            {
                if (Directory.Exists(snapshot.WorkingDirectory))
                    Directory.Delete(snapshot.WorkingDirectory, recursive: true);

                File.Delete(GetArtifactPath(runId));
                File.Delete(GetArtifactPath(runId) + ".tmp");
                File.Delete(GetManifestPath(runId));
                _jobs.Remove(runId);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                error = $"Unable to remove the remote run files: {exception.Message}";
                return false;
            }
        }
    }

    public void Cancel(string runId)
    {
        lock (_sync)
        {
            if (_jobs.TryGetValue(runId, out var job))
                job.Cancel();
        }
    }

    public bool Kill(string runId)
        => _scheduler.Kill(runId);

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
        }
        _scheduler.Dispose();
    }

    private string GetManifestPath(string runId)
        => Path.Combine(_storageDirectory, $"{Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(runId)))}.json");

    private string GetArtifactPath(string runId)
        => Path.Combine(_storageDirectory, $"{Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(runId)))}.zip");

    private void LoadPersistedJobs()
    {
        foreach (var path in Directory.EnumerateFiles(_storageDirectory, "*.json"))
        {
            try
            {
                var snapshot = JsonSerializer.Deserialize<RemoteRunSnapshot>(File.ReadAllBytes(path), SnapshotJsonOptions);
                if (snapshot is null)
                    continue;
                if (snapshot.State == RemoteRunState.Running)
                {
                    snapshot = snapshot with
                    {
                        State = RemoteRunState.Interrupted,
                        Status = "RunServer restarted; saved run details restored. The run did not resume.",
                        UpdatedAt = DateTimeOffset.UtcNow
                    };
                    File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(snapshot, SnapshotJsonOptions));
                }
                _jobs[snapshot.RunId] = new RemoteRunJob(this, snapshot);
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class RemoteRunJob : IRunOutputSink
    {
        private readonly RemoteRunRegistry _registry;
        private readonly object _sync = new();
        private readonly RunContext? _context;
        private readonly RunServerBus? _bridgeBus;
        private readonly IReadOnlyList<string> _extraDlls;
        private RemoteRunSnapshot _snapshot;
        private RunServerBus? _observer;

        public RemoteRunJob(RemoteRunRegistry registry, RunServerBus observer,
            RunContext context, RemoteRunSnapshot snapshot)
        {
            _registry = registry;
            _observer = observer;
            _bridgeBus = observer;
            _extraDlls = observer.ExtraDlls.ToArray();
            _context = context;
            _snapshot = snapshot;
        }

        public RemoteRunJob(RemoteRunRegistry registry, RemoteRunSnapshot snapshot)
        {
            _registry = registry;
            _extraDlls = Array.Empty<string>();
            _snapshot = snapshot;
        }

        public IReadOnlyList<string> ExtraDlls => _extraDlls;

        public RemoteRunSnapshot GetSnapshot()
        {
            lock (_sync)
                return _snapshot with
                {
                    ProgressParameters = _snapshot.ProgressParameters.ToArray(),
                    OptimizationResults = _snapshot.OptimizationResults?.ToArray()
                };
        }

        public void Attach(RunServerBus observer)
        {
            lock (_sync)
                _observer = observer;
        }

        public void Detach(RunServerBus observer)
        {
            lock (_sync)
            {
                if (ReferenceEquals(_observer, observer))
                    _observer = null;
            }
        }

        public void Cancel()
            => _context?.RequestCancelRun(_snapshot.RunId);

        public void AcknowledgeReceived()
            => Update(snapshot => snapshot with
            {
                Status = "Completion received by a GUI; remote output remains available.",
                UpdatedAt = DateTimeOffset.UtcNow
            });

        public void SetArtifactsAvailable()
            => Update(snapshot => snapshot with { ArtifactsAvailable = true, UpdatedAt = DateTimeOffset.UtcNow });

        public Task StartProcessingRequestFromRun(string id, Stream clientToRunStream)
        {
            if (_bridgeBus is null)
                throw new IOException("The RunServer session is unavailable to bridge this run.");
            return _bridgeBus.StartProcessingRequestFromRun(id, clientToRunStream, this);
        }

        public void ModelRunFailedValidation(string runId, string? error, string? moduleName = null, string? elementId = null)
        {
            Update(snapshot => snapshot with
            {
                State = RemoteRunState.Failed,
                Status = error ?? "Run validation failed.",
                ErrorMessage = error,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            Forward(observer => observer.ModelRunFailedValidation(runId, error, moduleName, elementId));
        }

        public void ModelRunFailed(string runId, string? message, string? stackTrace,
            string? moduleName = null, string? elementId = null)
        {
            Update(snapshot => snapshot with
            {
                State = RemoteRunState.Failed,
                Status = message ?? "Run failed.",
                ErrorMessage = message,
                ErrorStack = stackTrace,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            Forward(observer => observer.ModelRunFailed(runId, message, stackTrace, moduleName, elementId));
        }

        public void SendStatusMessage(string runId, string? message)
        {
            Update(snapshot => snapshot with { Status = message ?? string.Empty, UpdatedAt = DateTimeOffset.UtcNow });
            Forward(observer => observer.SendStatusMessage(runId, message));
        }

        public void SendOptimizationResults(string runId, IReadOnlyList<(int nodeIndex, double value)> results)
        {
            var values = results.Select(item => new RemoteRunParameterValue(item.nodeIndex, item.value)).ToArray();
            Update(snapshot => snapshot with { OptimizationResults = values, UpdatedAt = DateTimeOffset.UtcNow });
            Forward(observer => observer.SendOptimizationResults(runId, results));
        }

        public void SendIterationProgress(string runId, int iteration, double fitness,
            int fitnessTestsThisIteration, IReadOnlyList<(int nodeIndex, double value)> values)
        {
            var parameters = values.Select(item => new RemoteRunParameterValue(item.nodeIndex, item.value)).ToArray();
            Update(snapshot => snapshot with
            {
                Iteration = iteration,
                Fitness = fitness,
                ProgressParameters = parameters,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            Forward(observer => observer.SendIterationProgress(runId, iteration, fitness,
                fitnessTestsThisIteration, values));
        }

        public void ModelRunComplete(string runId)
        {
            Update(snapshot => snapshot with
            {
                State = RemoteRunState.Completed,
                Status = "Run completed.",
                UpdatedAt = DateTimeOffset.UtcNow
            });
            Forward(observer => observer.ModelRunComplete(runId));
        }

        public void SendRunArtifacts(string runId, string runDirectory)
        {
            var artifactPath = _registry.GetArtifactPath(runId);
            try
            {
                if (Directory.Exists(runDirectory))
                {
                    var temporary = artifactPath + ".tmp";
                    if (File.Exists(temporary))
                        File.Delete(temporary);
                    ZipFile.CreateFromDirectory(runDirectory, temporary, CompressionLevel.Fastest, false);
                    File.Move(temporary, artifactPath, true);
                    Update(snapshot => snapshot with { ArtifactsAvailable = true, UpdatedAt = DateTimeOffset.UtcNow });
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                SendStatusMessage(runId, $"Run output archive could not be retained: {exception.Message}");
            }
            Forward(observer => observer.SendRunArtifacts(runId, runDirectory));
        }

        private void Update(Func<RemoteRunSnapshot, RemoteRunSnapshot> update)
        {
            RunServerBus? observer;
            lock (_sync)
            {
                _snapshot = update(_snapshot);
                observer = _observer;
                Persist();
            }
            _ = observer;
        }

        private void Forward(Action<RunServerBus> send)
        {
            RunServerBus? observer;
            lock (_sync)
                observer = _observer;
            if (observer is null)
                return;
            try
            {
                send(observer);
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
            }
        }

        public void Persist()
        {
            var snapshot = GetSnapshot();
            var path = _registry.GetManifestPath(snapshot.RunId);
            var temporary = path + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(snapshot, SnapshotJsonOptions));
                File.Move(temporary, path, true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
