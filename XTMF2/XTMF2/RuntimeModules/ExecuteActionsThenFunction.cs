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
    [Module(Name = "Execute Actions Then Function", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
Description = "Allows you to execute actions before calling a function.  This allows you to ")]
    public class ExecuteActionsThenFunction<Return> : BaseFunction<Return>
    {
        [SubModule(Index = 0, Name = "Invoke First", Description = "Actions to invoke before invoking the function.")]
        public IAction[] InvokeFirst;

        [Parameter(Index = 1, Name = "Invoke Actions in Parallel", Description = "Should the actions be invoked in parallel?", DefaultValue = "false")]
        public IFunction<bool> InvokeActionsInParallel;

        [SubModule(Index = 2, Required = true, Name = "End With", Description = "The function to invoke and return the value of.")]
        public IFunction<Return> EndWith;

        public override Return Invoke()
        {
            if (InvokeActionsInParallel.Invoke())
            {
                Parallel.ForEach(InvokeFirst, (module) =>
                {
                    module.Invoke();
                });
            }
            else
            {
                foreach (var module in InvokeFirst)
                {
                    module.Invoke();
                }
            }
            return EndWith.Invoke();
        }
    }
}
