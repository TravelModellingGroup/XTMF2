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

namespace XTMF2.RuntimeModules
{
    public sealed class ReportFunctionInvocation<Return> : IFunction<Return> 
    {
        public string Name { get; set; }

        [SubModule(Required = true, Name = "ToInvoke", Description = "Invoke after signaling context")]
        public IFunction<Return> ToInvoke;

        public Return Invoke()
        {
            return ToInvoke.Invoke();
        }
    }

    public sealed class ReportFunctionInvocation<Context, Return> : IFunction<Context, Return>
    {
        public string Name { get; set; }

        [SubModule(Required = true, Name = "ToInvoke", Description = "Invoke after signaling context")]
        public IFunction<Context, Return> ToInvoke;

        public Return Invoke(Context context)
        {
            return ToInvoke.Invoke(context);
        }
    }

    public sealed class ReportActionInvocation : IAction
    {
        public string Name { get; set; }

        [SubModule(Required = true, Name = "ToInvoke", Description = "Invoke after signaling context")]
        public IAction ToInvoke;

        public void Invoke()
        {
            ToInvoke.Invoke();
        }
    }

    public sealed class ReportActionInvocation<Context> : IAction<Context>
    {
        public string Name { get; set; }

        [SubModule(Required = true, Name = "ToInvoke", Description = "Invoke after signaling context")]
        public IAction<Context> ToInvoke;

        public void Invoke(Context context)
        {
            ToInvoke.Invoke(context);
        }
    }
}
