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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace XTMF2.Bus
{
    /// <summary>
    /// This class is used to accept model system run requests and execute them in series.
    /// </summary>
    internal sealed class Scheduler : IDisposable
    {
        internal sealed class Reservation : IDisposable
        {
            private readonly object _sync = new();
            private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private Action? _cancellationHandler;
            private bool _cancellationRequested;

            public Task Started => _started.Task;
            internal bool IsReleased => _released.Task.IsCompleted;
            internal Task Released => _released.Task;

            internal bool TryStart()
            {
                if (IsReleased)
                    return false;
                _started.TrySetResult();
                return true;
            }

            internal void SetCancellationHandler(Action handler)
            {
                ArgumentNullException.ThrowIfNull(handler);
                bool invoke;
                lock (_sync)
                {
                    _cancellationHandler = handler;
                    invoke = _cancellationRequested;
                }
                if (invoke)
                    handler();
            }

            internal void RequestCancellation()
            {
                Action? handler;
                lock (_sync)
                {
                    _cancellationRequested = true;
                    handler = _cancellationHandler;
                }
                if (handler is null)
                    Dispose();
                else
                    handler();
            }

            public void Dispose()
            {
                lock (_sync)
                    _cancellationHandler = null;
                _released.TrySetResult();
                _started.TrySetCanceled();
            }
        }

        internal sealed class ReservationLease : IDisposable
        {
            private readonly ReservationGroup _group;
            private Action? _cancellationHandler;
            private bool _disposed;

            internal ReservationLease(ReservationGroup group) => _group = group;

            internal Task Started => _group.Started;

            internal void SetCancellationHandler(Action handler)
            {
                ArgumentNullException.ThrowIfNull(handler);
                _group.SetCancellationHandler(this, handler);
            }

            internal void CancelGroup() => _group.Cancel();

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _group.Release(this);
            }

            internal void InvokeCancellationHandler() => _cancellationHandler?.Invoke();

            internal void SetHandler(Action handler) => _cancellationHandler = handler;
        }

        internal sealed class ReservationGroup
        {
            private readonly object _sync = new();
            private readonly Reservation _reservation;
            private readonly Action _completed;
            private readonly HashSet<ReservationLease> _leases = [];
            private int _expectedSlotCount;
            private int _joinedSlotCount;
            private bool _cancelled;
            private bool _released;

            internal ReservationGroup(Reservation reservation, int expectedSlotCount, Action completed)
            {
                _reservation = reservation;
                _expectedSlotCount = expectedSlotCount;
                _completed = completed;
                _reservation.SetCancellationHandler(Cancel);
            }

            internal Task Started => _reservation.Started;

            internal ReservationLease Join(int expectedSlotCount)
            {
                lock (_sync)
                {
                    if (_released || _cancelled)
                        throw new InvalidOperationException("The shared estimation scheduler task is no longer active.");
                    _expectedSlotCount = Math.Max(_expectedSlotCount, expectedSlotCount);
                    _joinedSlotCount++;
                    var lease = new ReservationLease(this);
                    _leases.Add(lease);
                    return lease;
                }
            }

            internal void SetCancellationHandler(ReservationLease lease, Action handler)
            {
                bool invoke;
                lock (_sync)
                {
                    lease.SetHandler(handler);
                    invoke = _cancelled;
                }
                if (invoke)
                    handler();
            }

            internal void Release(ReservationLease lease)
            {
                bool complete;
                lock (_sync)
                {
                    _leases.Remove(lease);
                    complete = !_released && _leases.Count == 0
                        && (_cancelled || _joinedSlotCount >= _expectedSlotCount);
                    if (complete)
                        _released = true;
                }
                if (complete)
                    Complete();
            }

            internal void Cancel()
            {
                Action[] handlers;
                bool complete;
                lock (_sync)
                {
                    _cancelled = true;
                    handlers = _leases.Select(lease => (Action?)lease.InvokeCancellationHandler)
                        .Where(handler => handler is not null)
                        .Cast<Action>()
                        .ToArray();
                    complete = !_released && _leases.Count == 0;
                    if (complete)
                        _released = true;
                }
                foreach (var handler in handlers)
                    handler();
                if (complete)
                    Complete();
            }

            private void Complete()
            {
                _reservation.Dispose();
                _completed();
            }
        }

        private readonly ConcurrentQueue<ScheduledWork> _ToRun = new();
        private readonly object _inventorySync = new();
        private readonly IRunOutputSink? _DefaultSink;
        private readonly CancellationTokenSource _CancelExecutionEngine = new();
        private readonly SemaphoreSlim _RunsToGo = new SemaphoreSlim(0);
        private readonly Dictionary<string, ReservationGroup> _reservationGroups = new(StringComparer.Ordinal);
        private Reservation? _currentReservation;

        private readonly record struct ScheduledWork(RunContext Context, IRunOutputSink? Sink, Reservation? Reservation);

        /// <summary>
        /// The currently executing RunContext.
        /// This property is null if there is nothing running.
        /// </summary>
        public RunContext? Current { get; private set; }

        internal IReadOnlyList<(RunContext Context, bool IsRunning, int QueuePosition, bool IsReservation)> GetInventory()
        {
            lock (_inventorySync)
            {
                var current = Current;
                var queued = _ToRun.ToArray().Where(work => work.Reservation?.IsReleased != true).ToArray();
                var result = new List<(RunContext Context, bool IsRunning, int QueuePosition, bool IsReservation)>(queued.Length + 1);
                if (current is not null)
                    result.Add((current, true, 0, _currentReservation is not null));

                var queuePosition = 0;
                foreach (var work in queued)
                {
                    if (current is not null && ReferenceEquals(work.Context, current))
                        continue;
                    result.Add((work.Context, false, ++queuePosition, work.Reservation is not null));
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
                        ScheduledWork work;
                        bool hasWork;
                        lock (_inventorySync)
                        {
                            Current = null;
                            hasWork = _ToRun.TryDequeue(out work);
                            while (hasWork && work.Reservation?.IsReleased == true)
                                hasWork = _ToRun.TryDequeue(out work);
                            if (hasWork)
                            {
                                Current = work.Context;
                                _currentReservation = work.Reservation;
                            }
                        }
                        if (hasWork)
                        {
                            if (work.Reservation is { } reservation)
                            {
                                try
                                {
                                    if (reservation.TryStart())
                                        reservation.Released.Wait(token);
                                }
                                finally
                                {
                                    lock (_inventorySync)
                                    {
                                        if (ReferenceEquals(Current, work.Context))
                                        {
                                            Current = null;
                                            _currentReservation = null;
                                        }
                                    }
                                }
                                continue;
                            }
                            try
                            {
                                var context = work.Context;
                                var sink = work.Sink!;
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
                                work.Sink!.ModelRunFailed(work.Context.ID, e.Message, e.StackTrace, null, null);
                            }
                            finally
                            {
                                lock (_inventorySync)
                                {
                                    if (ReferenceEquals(Current, work.Context))
                                    {
                                        Current = null;
                                        _currentReservation = null;
                                    }
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
                _ToRun.Enqueue(new ScheduledWork(context, sink, null));
                _RunsToGo.Release();
            }
        }

        internal Reservation Reserve(RunContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            var reservation = new Reservation();
            lock (_inventorySync)
            {
                _ToRun.Enqueue(new ScheduledWork(context, null, reservation));
                _RunsToGo.Release();
            }
            return reservation;
        }

        internal ReservationLease ReserveGroup(string groupId, RunContext context, int expectedSlotCount)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
            if (expectedSlotCount < 1)
                throw new ArgumentOutOfRangeException(nameof(expectedSlotCount));

            lock (_inventorySync)
            {
                if (_reservationGroups.TryGetValue(groupId, out var existing))
                    return existing.Join(expectedSlotCount);

                var reservation = new Reservation();
                ReservationGroup? group = null;
                group = new ReservationGroup(reservation, expectedSlotCount,
                    () => RemoveReservationGroup(groupId, group!));
                _reservationGroups.Add(groupId, group);
                _ToRun.Enqueue(new ScheduledWork(context, null, reservation));
                _RunsToGo.Release();
                return group.Join(expectedSlotCount);
            }
        }

        private void RemoveReservationGroup(string groupId, ReservationGroup group)
        {
            lock (_inventorySync)
            {
                if (_reservationGroups.TryGetValue(groupId, out var current) && ReferenceEquals(current, group))
                    _reservationGroups.Remove(groupId);
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
            Reservation? currentReservation = null;
            ScheduledWork? removed = null;
            lock (_inventorySync)
            {
                if (Current?.ID == runId)
                {
                    current = Current;
                    currentReservation = _currentReservation;
                }
                else
                {
                    var retained = new List<ScheduledWork>();
                    while (_ToRun.TryDequeue(out var work))
                    {
                        if (removed is null && work.Context.ID == runId)
                        {
                            removed = work;
                            work.Reservation?.RequestCancellation();
                        }
                        else
                            retained.Add(work);
                    }
                    foreach (var work in retained)
                        _ToRun.Enqueue(work);
                }
            }

            if (current is not null)
            {
                currentReservation?.RequestCancellation();
                current.Kill();
                return true;
            }
            if (removed is { } queued)
            {
                if (queued.Reservation is null)
                    queued.Sink!.ModelRunFailed(runId, "Run removed from the queue by RunServer Activity window.", string.Empty);
                return true;
            }
            return false;
        }
    }
}