﻿/*
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
        private readonly ConcurrentQueue<RunContext> _ToRun = new ConcurrentQueue<RunContext>();
        private readonly ClientBus _Bus;
        private readonly CancellationTokenSource _CancelExecutionEngine;
        private readonly SemaphoreSlim _RunsToGo = new SemaphoreSlim(0);

        /// <summary>
        /// The currently executing RunContext.
        /// This property is null if there is nothing running.
        /// </summary>
        public RunContext? Current { get; private set; }

        /// <summary>
        /// Create a new Scheduler to process the given client bus.
        /// </summary>
        /// <param name="bus">The bus to listen to.</param>
        public Scheduler(ClientBus bus)
        {
            _Bus = bus;
            _CancelExecutionEngine = new CancellationTokenSource();
            var token = _CancelExecutionEngine.Token;
            Task.Factory.StartNew(()=>
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
                            Current = context;
                            context.RunInNewProcess(_Bus);
                        }
                        catch (Exception e)
                        {
                            _Bus.ModelRunFailed(context.ID, e.Message, e.StackTrace);
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