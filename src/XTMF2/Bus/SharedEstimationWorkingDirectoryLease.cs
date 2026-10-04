using System;
using System.IO;
using System.Threading;

namespace XTMF2.Bus;

internal static class SharedEstimationWorkingDirectoryLease
{
    private static readonly object Sync = new();
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    private static string? _activeDirectory;
    private static string? _originalDirectory;
    private static int _leaseCount;

    internal static IDisposable Acquire(string directory)
    {
        var fullPath = Path.GetFullPath(directory);
        lock (Sync)
        {
            while (_leaseCount > 0 && !String.Equals(_activeDirectory, fullPath, PathComparison))
                Monitor.Wait(Sync);

            if (_leaseCount == 0)
            {
                _originalDirectory = Directory.GetCurrentDirectory();
                Directory.SetCurrentDirectory(fullPath);
                _activeDirectory = fullPath;
            }

            _leaseCount++;
            return new Lease();
        }
    }

    private static void Release()
    {
        lock (Sync)
        {
            if (--_leaseCount != 0)
                return;

            try
            {
                Directory.SetCurrentDirectory(_originalDirectory!);
            }
            finally
            {
                _originalDirectory = null;
                _activeDirectory = null;
                Monitor.PulseAll(Sync);
            }
        }
    }

    private sealed class Lease : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                Release();
        }
    }
}