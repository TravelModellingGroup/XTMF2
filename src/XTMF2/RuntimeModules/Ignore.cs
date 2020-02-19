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
using XTMF2;

namespace XTMF2.RuntimeModules
{
    [Module(Name = "Ignore Result", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
Description = "Ignore the result of a function call.  This allows you to call functions from an action.")]
    public class IgnoreResult<FuncReturn> : BaseAction
    {
        [SubModule(Description = "The module to ignore the results of.", Name = "To Ignore", Required = true, Index = 0)]
        public IFunction<FuncReturn>? ToExecute;

        public override void Invoke()
        {
            ToExecute!.Invoke();
        }
    }

    [Module(Name = "Ignore Result", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
Description = "Ignore the result of a function call.  This allows you to call functions from an action.")]
    public class IgnoreResult<Context, FuncReturn> : BaseAction<Context>
    {
        [SubModule(Description = "The module to ignore the results of.", Name = "To Ignore", Required = true, Index = 0)]
        public IFunction<Context, FuncReturn>? ToExecute;

        public override void Invoke(Context context)
        {
            ToExecute!.Invoke(context);
        }
    }

    [Module(Name = "Ignore Context", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
Description = "Ignore the context of a function call.  This allows you to call functions that don't require a context.")]
    public class IgnoreContext<Context> : BaseAction<Context>
    {
        [SubModule(Description = "The module to invoke ignoring context.", Name = "To Ignore", Required = true, Index = 0)]
        public IAction? ToInvoke;

        public override void Invoke(Context context)
        {
            ToInvoke!.Invoke();
        }
    }

    [Module(Name = "Ignore Context", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
Description = "Ignore the context of a function call.  This allows you to call functions that don't require a context.")]
    public class IgnoreContext<Context,Return> : BaseFunction<Context,Return>
    {
        [SubModule(Description = "The module to invoke ignoring context.", Name = "To Ignore", Required = true, Index = 0)]
        public IFunction<Return>? ToInvoke;

        public override Return Invoke(Context context)
        {
            return ToInvoke!.Invoke();
        }
    }
}
