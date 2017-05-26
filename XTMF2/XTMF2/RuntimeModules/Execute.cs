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
    public class Execute : BaseAction
    {
        [SubModule(Name = "To Execute", Description = "The modules in order to execute")]
        public IAction[] ToInvoke;

        [Parameter(DefaultValue = "false", Name = "Parallel Execution", Required = false)]
        public IFunction<bool> ParallelExecution;

        [Parameter(DefaultValue = "1", Name = "Iterations", Required = false)]
        public IFunction<int> Iterations;

        public override void Invoke()
        {
            var iterations = Iterations?.Invoke() ?? 1;
            var parallel = ParallelExecution?.Invoke() ?? false;
            if (parallel)
            {
                Parallel.ForEach(ToInvoke, (action) =>
                {
                    action?.Invoke();
                });
            }
            else
            {
                foreach (var module in ToInvoke)
                {
                    module.Invoke();
                }
            }
        }
    }

    public class Execute<Context> : BaseAction<Context>
    {
        [SubModule(Name = "To Execute", Description = "The modules in order to execute")]
        public IAction<Context>[] ToInvoke;

        [Parameter(DefaultValue = "false", Name = "Parallel Execution", Required = false)]
        public IFunction<bool> ParallelExecution;

        [Parameter(DefaultValue = "1", Name = "Iterations", Required = false)]
        public IFunction<int> Iterations;

        public override void Invoke(Context context)
        {
            var iterations = Iterations?.Invoke() ?? 1;
            var parallel = ParallelExecution?.Invoke() ?? false;
            if (parallel)
            {
                for (int iteration = 0; iteration < iterations; iteration++)
                {
                    Parallel.ForEach(ToInvoke, (action) =>
                    {
                        action?.Invoke(context);
                    });
                }
            }
            else
            {
                for (int iteration = 0; iteration < iterations; iteration++)
                {
                    foreach (var module in ToInvoke)
                    {
                        module.Invoke(context);
                    }
                }
            }
        }
    }
}
