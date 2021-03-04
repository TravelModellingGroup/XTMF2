/*
    Copyright 2017 University of Toronto

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
    along with XTMF2.  If not, see <http://www.gnu.org/licenses/>.
*/
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace XTMF2.RuntimeModules
{
    [Module(Name = "Execute", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
Description = "Provides a way to execute a series of actions in order, optionally in parallel or with multiple iterations.")]
    public class Execute : BaseAction
    {
        [Parameter(DefaultValue = "false", Name = "Parallel Execution", Required = false, Index = 0)]
        public IFunction<bool>? ParallelExecution;

        [Parameter(DefaultValue = "1", Name = "Iterations", Required = false, Index = 1)]
        public IFunction<int>? Iterations;

        [SubModule(Name = "Current Iteration", Required = false, Description = "Place to store the current iteration", Index = 2)]
        public ISetableValue<int>? CurrentIteration;

        [SubModule(Name = "To Execute", Description = "The modules in order to execute", Index = 3)]
        public IAction[]? ToInvoke;

        public override void Invoke()
        {
            var iterations = Iterations?.Invoke() ?? 1;
            var parallel = ParallelExecution?.Invoke() ?? false;
            if (parallel)
            {
                for (int iteration = 0; iteration < iterations; iteration++)
                {
                    CurrentIteration?.Set(iteration);
                    Parallel.ForEach(ToInvoke!, (action) =>
                    {
                        action?.Invoke();
                    });
                }
            }
            else
            {
                for (int iteration = 0; iteration < iterations; iteration++)
                {
                    CurrentIteration?.Set(iteration);
                    foreach (var module in ToInvoke!)
                    {
                        module?.Invoke();
                    }
                }
            }
        }
    }

    [Module(Name = "Execute", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
Description = "Provides a way to execute a series of actions in order, optionally in parallel or with multiple iterations.")]
    public class Execute<Context> : BaseAction<Context>
    {
        [Parameter(DefaultValue = "false", Name = "Parallel Execution", Required = false, Index = 0)]
        public IFunction<bool>? ParallelExecution;

        [Parameter(DefaultValue = "1", Name = "Iterations", Required = false, Index = 1)]
        public IFunction<int>? Iterations;

        [SubModule(Name = "Current Iteration", Required = false, Description = "Place to store the current iteration", Index = 2)]
        public ISetableValue<int>? CurrentIteration;

        [SubModule(Name = "To Execute", Description = "The modules in order to execute", Index = 3)]
        public IAction<Context>[]? ToInvoke;

        public override void Invoke(Context context)
        {
            var iterations = Iterations?.Invoke() ?? 1;
            var parallel = ParallelExecution?.Invoke() ?? false;
            if (parallel)
            {
                for (int iteration = 0; iteration < iterations; iteration++)
                {
                    CurrentIteration?.Set(iteration);
                    Parallel.ForEach(ToInvoke!, (action) =>
                    {
                        action?.Invoke(context);
                    });
                }
            }
            else
            {
                for (int iteration = 0; iteration < iterations; iteration++)
                {
                    CurrentIteration?.Set(iteration);
                    foreach (var module in ToInvoke!)
                    {
                        module?.Invoke(context);
                    }
                }
            }
        }
    }
}
