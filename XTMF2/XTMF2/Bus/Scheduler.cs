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
    internal sealed class Scheduler : IDisposable
    {

        private ConcurrentQueue<RunContext> toRun = new ConcurrentQueue<RunContext>();

        private RunBusClient Bus;

        private Task ExecutionTask;
        private CancellationTokenSource CancelExecutionEngine;
        private SemaphoreSlim RunsToGo = new SemaphoreSlim(0);

        public Scheduler(RunBusClient bus)
        {
            Bus = bus;
            CancelExecutionEngine = new CancellationTokenSource();
            var token = CancelExecutionEngine.Token;
            ExecutionTask = Task.Factory.StartNew(()=>
            {
                while(!token.IsCancellationRequested)
                {
                    RunsToGo.Wait(token);
                    if(token.IsCancellationRequested)
                    {
                        return;
                    }
                    if (toRun.TryDequeue(out var context))
                    {
                        try
                        {
                            string error = null;
                            string stackTrace = null;
                            if (context.ValidateModelSystem(ref error))
                            {
                                if(!context.Run(ref error, ref stackTrace))
                                {
                                    Bus.ModelRunFailed(context, error, stackTrace);
                                }
                                Bus.ModelRunComplete(context);
                            }
                            else
                            {
                                Bus.ModelRunFailedValidation(context, error);
                            }
                        }
                        catch (Exception e)
                        {
                            Bus.ModelRunFailed(context, e.Message, e.StackTrace);
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
            CancelExecutionEngine.Cancel();
            CancelExecutionEngine.Dispose();
            RunsToGo.Dispose();
        }

        ~Scheduler()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
        }

        internal void Run(RunContext context)
        {
            toRun.Enqueue(context);
            RunsToGo.Release();
        }
    }
}