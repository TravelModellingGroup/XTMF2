﻿/*
    Copyright 2014 Travel Modelling Group, Department of Civil Engineering, University of Toronto

    This file is part of XTMF.

    XTMF is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    XTMF is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with XTMF.  If not, see <http://www.gnu.org/licenses/>.
*/
using System;
using System.Collections.Generic;
using System.Text;

namespace XTMF2.RuntimeModules
{
    [Module(Name = "If", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
Description = "Provides a way to conditionally execute.  If the condition is true or false different functions will be invoked.")]
    public sealed class IfF<Return> : BaseFunction<Return>
    {
        [SubModule(Required = true, Name = "If True", Description = "The logic to invoke if true")]
        public IFunction<Return> ToInvokeIfTrue;
        [SubModule(Required = true, Name = "If False", Description = "The logic to invoke if false")]
        public IFunction<Return> ToInvokeIfFalse;
        [Parameter(Required = true, Name = "Condition",
            Description = "The condition to invoke to see if the true or false path is taken.", DefaultValue = "true")]
        public IFunction<bool> Condition;

        public override Return Invoke()
        {
            if(Condition.Invoke())
            {
                return ToInvokeIfTrue.Invoke();
            }
            else
            {
                return ToInvokeIfFalse.Invoke();
            }
        }
    }

    [Module(Name = "If", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
Description = "Provides a way to conditionally execute.  If the condition is true or false different functions will be invoked.")]
    public sealed class IfF<Context,Return> : BaseFunction<Context,Return>
    {
        [SubModule(Required = true, Name = "If True", Description = "The logic to invoke if true")]
        public IFunction<Context, Return> ToInvokeIfTrue;
        [SubModule(Required = true, Name = "If False", Description = "The logic to invoke if false")]
        public IFunction<Context, Return> ToInvokeIfFalse;
        [Parameter(Required = true, Name = "Condition",
            Description = "The condition to invoke to see if the true or false path is taken.", DefaultValue = "true")]
        public IFunction<bool> Condition;

        public override Return Invoke(Context context)
        {
            if (Condition.Invoke())
            {
                return ToInvokeIfTrue.Invoke(context);
            }
            else
            {
                return ToInvokeIfFalse.Invoke(context);
            }
        }
    }

    [Module(Name = "If", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
Description = "Provides a way to conditionally execute.  If the condition is true or false different functions will be invoked.")]
    public sealed class IfWithContextF<Context, Return> : BaseFunction<Context, Return>
    {
        [SubModule(Required = true, Name = "If True", Description = "The logic to invoke if true")]
        public IFunction<Context, Return> ToInvokeIfTrue;
        [SubModule(Required = true, Name = "If False", Description = "The logic to invoke if false")]
        public IFunction<Context, Return> ToInvokeIfFalse;
        [Parameter(Required = true, Name = "Condition",
            Description = "The condition to invoke to see if the true or false path is taken.", DefaultValue = "true")]
        public IFunction<Context, bool> Condition;

        public override Return Invoke(Context context)
        {
            if (Condition.Invoke(context))
            {
                return ToInvokeIfTrue.Invoke(context);
            }
            else
            {
                return ToInvokeIfFalse.Invoke(context);
            }
        }
    }

    [Module(Name = "If", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
Description = "Provides a way to conditionally execute.  If the condition is true or false different functions will be invoked.")]
    public sealed class IfA : BaseAction
    {
        [SubModule(Required = true, Name = "If True", Description = "The logic to invoke if true")]
        public IAction ToInvokeIfTrue;
        [SubModule(Required = true, Name = "If False", Description = "The logic to invoke if false")]
        public IAction ToInvokeIfFalse;
        [Parameter(Required = true, Name = "Condition",
            Description = "The condition to invoke to see if the true or false path is taken.", DefaultValue = "true")]
        public IFunction<bool> Condition;

        public override void Invoke()
        {
            if (Condition.Invoke())
            {
                ToInvokeIfTrue.Invoke();
            }
            else
            {
                ToInvokeIfFalse.Invoke();
            }
        }
    }

    [Module(Name = "If", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
Description = "Provides a way to conditionally execute.  If the condition is true or false different functions will be invoked.")]
    public sealed class IfA<Context> : BaseAction<Context>
    {
        [SubModule(Required = true, Name = "If True", Description = "The logic to invoke if true")]
        public IAction<Context> ToInvokeIfTrue;
        [SubModule(Required = true, Name = "If False", Description = "The logic to invoke if false")]
        public IAction<Context> ToInvokeIfFalse;
        [Parameter(Required = true, Name = "Condition",
            Description = "The condition to invoke to see if the true or false path is taken.", DefaultValue = "true")]
        public IFunction<bool> Condition;

        public override void Invoke(Context context)
        {
            if (Condition.Invoke())
            {
                ToInvokeIfTrue.Invoke(context);
            }
            else
            {
                ToInvokeIfFalse.Invoke(context);
            }
        }
    }

    [Module(Name = "If", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
Description = "Provides a way to conditionally execute.  If the condition is true or false different functions will be invoked.")]
    public sealed class IfWithContextA<Context> : BaseAction<Context>
    {
        [SubModule(Required = true, Name = "If True", Description = "The logic to invoke if true")]
        public IAction<Context> ToInvokeIfTrue;
        [SubModule(Required = true, Name = "If False", Description = "The logic to invoke if false")]
        public IAction<Context> ToInvokeIfFalse;
        [Parameter(Required = true, Name = "Condition",
            Description = "The condition to invoke to see if the true or false path is taken.", DefaultValue = "true")]
        public IFunction<Context, bool> Condition;

        public override void Invoke(Context context)
        {
            if (Condition.Invoke(context))
            {
                ToInvokeIfTrue.Invoke(context);
            }
            else
            {
                ToInvokeIfFalse.Invoke(context);
            }
        }
    }
}
