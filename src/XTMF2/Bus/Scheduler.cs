/*
    Copyright 2017 Travel Modelling Group, Department of Civil Engineering, University of Toronto

    This file is part of XTMF2.

                            finally
                            {
                                try
                                {
                                    _Bus.SendRunArtifacts(context.ID, context.WorkingDirectory);
                                }
                                catch
                                {
                                    // The execution result has already been reported; a failed
                                    // artifact transfer must not replace that result.
                                }
                            }
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    XTMF2 is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with XTMF.  If not, see <http://www.gnu.org/licenses/>.
*/
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace XTMF2.Bus
{
    /// <summary>
    /// This class is used to accept model system run requests and execute them in series.
    /// </summary>
    internal sealed class Scheduler : IDisposable
    {
        private readonly ConcurrentQueue<(RunContext Context, IRunOutputSink Sink)> _ToRun = new();
        private readonly object _inventorySync = new();
        private readonly IRunOutputSink? _DefaultSink;
        private readonly CancellationTokenSource _CancelExecutionEngine = new();
        private readonly SemaphoreSlim _RunsToGo = new SemaphoreSlim(0);

        /// <summary>
        /// The currently executing RunContext.
        /// This property is null if there is nothing running.
        /// </summary>
        public RunContext? Current { get; private set; }

        internal IReadOnlyList<(RunContext Context, bool IsRunning, int QueuePosition)> GetInventory()
        {
            lock (_inventorySync)
            {
                var current = Current;
                var queued = _ToRun.ToArray();
                var result = new List<(RunContext Context, bool IsRunning, int QueuePosition)>(queued.Length + 1);
                if (current is not null)
                    result.Add((current, true, 0));

                var queuePosition = 0;
                foreach (var work in queued)
                {
                    if (current is not null && ReferenceEquals(work.Context, current))
                        continue;
                    result.Add((work.Context, false, ++queuePosition));
                }
                return result;
            }
        }

        /// <summary>
        /// Create a new Scheduler to process the given client bus.
        /// </summary>
        /// <param name="bus">The bus to listen to.</param>
        public Scheduler(IRunOutputSink bus, bool runLocal)
        {
            _DefaultSink = bus;
            Start(runLocal);
        }

        internal Scheduler(bool runLocal)
        {
            Start(runLocal);
        }

        private void Start(bool runLocal)
        {
            var token = _CancelExecutionEngine.Token;
            Task.Factory.StartNew(() =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        _RunsToGo.Wait(token);
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }
                        (RunContext Context, IRunOutputSink Sink) work;
                        bool hasWork;
                        lock (_inventorySync)
                        {
                            Current = null;
                            hasWork = _ToRun.TryDequeue(out work);
                            if (hasWork)
                                Current = work.Context;
                        }
                        if (hasWork)
                        {
                            try
                            {
                                var context = work.Context;
                                var sink = work.Sink;
                                Console.WriteLine($"RunServer model system run started processing: {context.ID}");
                                Console.Out.Flush();
                                if (runLocal)
                                {
                                    context.RunInCurrentProcess(sink);
                                }
                                else
                                {
                                    context.RunInNewProcess(sink);
                                }
                                if (context.KillRequested)
                                    sink.ModelRunFailed(context.ID, "Run stopped from RunServer Activity window.", string.Empty);
                                else
                                    sink.SendRunArtifacts(context.ID, context.WorkingDirectory);
                            }
                            catch (Exception e)
                            {
                                work.Sink.ModelRunFailed(work.Context.ID, e.Message, e.StackTrace, null, null);
                            }
                            finally
                            {
                                lock (_inventorySync)
                                {
                                    if (ReferenceEquals(Current, work.Context))
                                        Current = null;
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    Interlocked.MemoryBarrier();
                }
            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        private void Dispose(bool managed)
        {
            if (managed)
            {
                GC.SuppressFinalize(this);
            }
            _CancelExecutionEngine.Cancel();
            _CancelExecutionEngine.Dispose();
            _RunsToGo.Dispose();
        }

        ~Scheduler()
        {
            Dispose(false);
        }

        /// <summary>
        /// Shutdown the scheduler
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// Add the given run context to the end of the queue
        /// </summary>
        /// <param name="context">The context to execute.</param>
        internal void Run(RunContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (_DefaultSink is null)
                throw new InvalidOperationException("A run output sink is required.");
            Run(context, _DefaultSink);
        }

        internal void Run(RunContext context, IRunOutputSink sink)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(sink);
            lock (_inventorySync)
            {
                _ToRun.Enqueue((context, sink));
                _RunsToGo.Release();
            }
        }

        /// <summary>
        /// Requests cancellation of the currently executing run if its ID matches.
        /// Also cancels a queued run with the matching ID.
        /// </summary>
        internal void RequestCancel(string runId)
        {
            Current?.RequestCancelRun(runId);
            foreach (var queued in _ToRun)
                queued.Context.RequestCancelRun(runId);
        }

        internal bool Kill(string runId)
        {
            RunContext? current = null;
            (RunContext Context, IRunOutputSink Sink)? removed = null;
            lock (_inventorySync)
            {
                if (Current?.ID == runId)
                {
                    current = Current;
                }
                else
                {
                    var retained = new List<(RunContext Context, IRunOutputSink Sink)>();
                    while (_ToRun.TryDequeue(out var work))
                    {
                        if (removed is null && work.Context.ID == runId)
                            removed = work;
                        else
                            retained.Add(work);
                    }
                    foreach (var work in retained)
                        _ToRun.Enqueue(work);
                }
            }

            if (current is not null)
            {
                current.Kill();
                return true;
            }
            if (removed is { } queued)
            {
                queued.Sink.ModelRunFailed(runId, "Run removed from the queue by RunServer Activity window.", string.Empty);
                return true;
            }
            return false;
        }
    }
}