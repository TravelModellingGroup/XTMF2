using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using XTMF2.Bus;

namespace XTMF2.Bus.Optimization;

internal sealed class SharedEstimationWorkerProcess : IDisposable
{
    private const string WorkerDirectoriesName = ".xtmf-estimation-workers";
    private readonly string _runId;
    private readonly string _workerDirectory;
    private readonly string _workerDirectoriesRoot;
    private readonly Process _process;
    private readonly Stream _stream;
    private readonly BinaryReader _reader;
    private readonly BinaryWriter _writer;
    private readonly object _requestSync = new();
    private readonly object _writeSync = new();
    private int _disposed;

    private SharedEstimationWorkerProcess(string runId, string workerDirectory,
        string workerDirectoriesRoot, Process process, Stream stream)
    {
        _runId = runId;
        _workerDirectory = workerDirectory;
        _workerDirectoriesRoot = workerDirectoriesRoot;
        _process = process;
        _stream = stream;
        _reader = new BinaryReader(stream, Encoding.UTF8, true);
        _writer = new BinaryWriter(stream, Encoding.UTF8, true);
    }

    internal static bool TryStart(SharedEstimationRunRequest request, IReadOnlyList<string> extraDlls,
        out SharedEstimationWorkerProcess? worker, out string? error)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(extraDlls);
        worker = null;
        error = null;
        Process? process = null;
        Stream? stream = null;
        var workerDirectoriesRoot = Path.Combine(Path.GetFullPath(request.WorkingDirectory),
            WorkerDirectoriesName);
        var workerDirectory = Path.Combine(workerDirectoriesRoot, Guid.NewGuid().ToString("N"));
        try
        {
            var runAssemblyDirectory = Path.GetDirectoryName(typeof(RunServerBus).Assembly.Location)!;
            var runAssemblyPath = Path.Combine(runAssemblyDirectory, "XTMF2.Run.dll");
            if (!File.Exists(runAssemblyPath))
                throw new FileNotFoundException("Unable to locate the XTMF2.Run worker executable.", runAssemblyPath);

            if (!CreateStreams.CreateNewTcpHost("127.0.0.1", 0, out stream, out _, out error, port =>
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "dotnet",
                        UseShellExecute = false,
                        CreateNoWindow = OperatingSystem.IsWindows(),
                        WorkingDirectory = runAssemblyDirectory
                    };
                    startInfo.ArgumentList.Add(runAssemblyPath);
                    startInfo.ArgumentList.Add("-runID");
                    startInfo.ArgumentList.Add(request.RunId);
                    foreach (var extraDll in extraDlls.Distinct(StringComparer.Ordinal))
                    {
                        startInfo.ArgumentList.Add("-loadDLL");
                        startInfo.ArgumentList.Add(extraDll);
                    }
                    startInfo.ArgumentList.Add("-sharedEstimationWorker");
                    startInfo.ArgumentList.Add("-tcp");
                    startInfo.ArgumentList.Add("127.0.0.1");
                    startInfo.ArgumentList.Add(port.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    process = Process.Start(startInfo)
                        ?? throw new InvalidOperationException("Unable to start the XTMF2.Run worker process.");
                }, timeoutMilliseconds: 15000))
                throw new IOException(error ?? "Unable to connect to the XTMF2.Run worker process.");

            var candidateWorker = new SharedEstimationWorkerProcess(request.RunId,
                workerDirectory, workerDirectoriesRoot, process!, stream!);
            SharedEstimationProtocol.WriteRunRequest(candidateWorker._writer,
                request with { WorkingDirectory = workerDirectory });
            candidateWorker._writer.Flush();
            var readiness = SharedEstimationProtocol.ReadWorkerReady(candidateWorker._reader);
            if (readiness.RunId != request.RunId || !readiness.Succeeded)
            {
                error = readiness.Error ?? "The XTMF2.Run worker rejected its start request.";
                candidateWorker.Dispose();
                return false;
            }

            worker = candidateWorker;
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException
            or System.ComponentModel.Win32Exception or ArgumentException or ObjectDisposedException)
        {
            error = exception.Message;
            stream?.Dispose();
            if (process is not null)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch (Exception killException) when (killException is InvalidOperationException
                    or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                }
                process.Dispose();
            }
            DeleteWorkerDirectories(workerDirectory, workerDirectoriesRoot);
            return false;
        }
    }

    internal SharedEstimationEvaluationResult Evaluate(SharedEstimationCandidate candidate)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return CreateError(candidate, "The estimation worker process is no longer running.");

        lock (_requestSync)
        {
            try
            {
                lock (_writeSync)
                {
                    ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
                    SharedEstimationProtocol.WriteCandidate(_writer, candidate);
                    _writer.Flush();
                }

                var result = SharedEstimationProtocol.ReadResult(_reader);
                if (result.RunId != candidate.RunId || result.CandidateId != candidate.CandidateId ||
                    result.BatchId != candidate.BatchId)
                    throw new InvalidDataException("The worker process returned a result for a different candidate.");
                return result;
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException
                or ObjectDisposedException or ArgumentException)
            {
                StopProcess();
                return CreateError(candidate, exception.Message);
            }
        }
    }

    private static SharedEstimationEvaluationResult CreateError(SharedEstimationCandidate candidate, string message)
        => new(candidate.RunId, candidate.BatchId, candidate.CandidateId, double.NaN, message, null, null);

    private void StopProcess()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit();
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.ComponentModel.Win32Exception or NotSupportedException)
        {
        }
        _stream.Dispose();
        _reader.Dispose();
        _writer.Dispose();
        _process.Dispose();
        DeleteWorkerDirectories(_workerDirectory, _workerDirectoriesRoot);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        try
        {
            lock (_writeSync)
            {
                SharedEstimationProtocol.WriteCancel(_writer, _runId, "Worker slot released.");
                _writer.Flush();
            }
            if (!_process.WaitForExit(1000))
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit();
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException
            or System.ComponentModel.Win32Exception or ObjectDisposedException or NotSupportedException)
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit();
                }
            }
            catch (Exception killException) when (killException is InvalidOperationException
                or System.ComponentModel.Win32Exception or NotSupportedException)
            {
            }
        }
        finally
        {
            _stream.Dispose();
            _reader.Dispose();
            _writer.Dispose();
            _process.Dispose();
            DeleteWorkerDirectories(_workerDirectory, _workerDirectoriesRoot);
        }
    }

    private static void DeleteWorkerDirectories(string workerDirectory, string workerDirectoriesRoot)
    {
        try
        {
            if (Directory.Exists(workerDirectory))
                Directory.Delete(workerDirectory, recursive: true);
            if (Directory.Exists(workerDirectoriesRoot) &&
                !Directory.EnumerateFileSystemEntries(workerDirectoriesRoot).Any())
                Directory.Delete(workerDirectoriesRoot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
        }
    }
}