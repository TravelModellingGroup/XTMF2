/*
    Copyright 2017 Travel Modelling Group, Department of Civil Engineering, University of Toronto

    This file is part of XTMF2.

    XTMF2 is free software: you can redistribute it and/or modify
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
using System.Threading;
using System.Threading.Tasks;

namespace XTMF2.Bus
{
    /// <summary>
    /// This class is used to accept model system run requests and execute them in series.
    /// </summary>
    internal sealed class Scheduler : IDisposable
    {
        private ConcurrentQueue<RunContext> _ToRun = new ConcurrentQueue<RunContext>();
        private RunBusClient _Bus;
        private Task _ExecutionTask;
        private CancellationTokenSource _CancelExecutionEngine;
        private SemaphoreSlim _RunsToGo = new SemaphoreSlim(0);

        /// <summary>
        /// The currently executing RunContext.
        /// This property is null if there is nothing running.
        /// </summary>
        public RunContext Current { get; private set; }

        /// <summary>
        /// Create a new Scheduler to process the given client bus.
        /// </summary>
        /// <param name="bus">The bus to listen to.</param>
        public Scheduler(RunBusClient bus)
        {
            _Bus = bus;
            _CancelExecutionEngine = new CancellationTokenSource();
            var token = _CancelExecutionEngine.Token;
            _ExecutionTask = Task.Factory.StartNew(()=>
            {
                while(!token.IsCancellationRequested)
                {
                    Current = null;
                    _RunsToGo.Wait(token);
                    if(token.IsCancellationRequested)
                    {
                        return;
                    }
                    if (_ToRun.TryDequeue(out var context))
                    {
                        try
                        {
                            string error = null;
                            string stackTrace = null;
                            Current = context;
                            if (context.ValidateModelSystem(ref error))
                            {
                                if (!context.Run(ref error, ref stackTrace))
                                {
                                    _Bus.ModelRunFailed(context, error, stackTrace);
                                }
                                _Bus.ModelRunComplete(context);
                            }
                            else
                            {
                                _Bus.ModelRunFailedValidation(context, error);
                            }
                        }
                        catch (Exception e)
                        {
                            _Bus.ModelRunFailed(context, e.Message, e.StackTrace);
                        }
                    }
                    Interlocked.MemoryBarrier();
                }
            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        private void Dispose(bool managed)
        {
            if(managed)
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
            _ToRun.Enqueue(context);
            _RunsToGo.Release();
        }
    }
}