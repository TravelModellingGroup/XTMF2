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
    [Module(Name = "Fail", Description = "Crash the model run with a message.",
        DocumentationLink = "http://tmg.utoronto.ca/doc/2.0/")]
    public sealed class FailA : BaseAction
    {
        [Parameter(Name = "Message", Index = 0, Description = "The message to fail with.", DefaultValue = "Invalid state!")]
        public IFunction<string>? Message;

        public override void Invoke()
        {
            throw new XTMFRuntimeException(this, Message?.Invoke());
        }
    }

    [Module(Name = "Fail", Description = "Crash the model run with a message.",
        DocumentationLink = "http://tmg.utoronto.ca/doc/2.0/")]
    public sealed class FailA<Context> : BaseAction<Context>
    {
        [Parameter(Name = "Message", Index = 0, Description = "The message to fail with.", DefaultValue = "Invalid state!")]
        public IFunction<string>? Message;

        public override void Invoke(Context context)
        {
            throw new XTMFRuntimeException(this, Message?.Invoke());
        }
    }

    [Module(Name = "Fail", Description = "Crash the model run with a message.",
    DocumentationLink = "http://tmg.utoronto.ca/doc/2.0/")]
    public sealed class FailWithContextA<Context> : BaseAction<Context>
    {
        [Parameter(Name = "Message", Index = 0, Description = "The message to fail with.", DefaultValue = "Invalid state!")]
        public IFunction<Context, string>? Message;

        public override void Invoke(Context context)
        {
            throw new XTMFRuntimeException(this, Message?.Invoke(context));
        }
    }

    [Module(Name = "Fail", Description = "Crash the model run with a message.",
    DocumentationLink = "http://tmg.utoronto.ca/doc/2.0/")]
    public sealed class FailF<Return> : BaseFunction<Return>
    {
        [Parameter(Name = "Message", Index = 0, Description = "The message to fail with.", DefaultValue = "Invalid state!")]
        public IFunction<string>? Message;

        public override Return Invoke()
        {
            throw new XTMFRuntimeException(this, Message?.Invoke());
        }
    }

    [Module(Name = "Fail", Description = "Crash the model run with a message.",
        DocumentationLink = "http://tmg.utoronto.ca/doc/2.0/")]
    public sealed class FailF<Context, Return> : BaseFunction<Context, Return>
    {
        [Parameter(Name = "Message", Index = 0, Description = "The message to fail with.", DefaultValue = "Invalid state!")]
        public IFunction<string>? Message;

        public override Return Invoke(Context context)
        {
            throw new XTMFRuntimeException(this, Message?.Invoke());
        }
    }

    [Module(Name = "Fail", Description = "Crash the model run with a message.",
    DocumentationLink = "http://tmg.utoronto.ca/doc/2.0/")]
    public sealed class FailWithContextF<Context, Return> : BaseFunction<Context, Return>
    {
        [Parameter(Name = "Message", Index = 0, Description = "The message to fail with.", DefaultValue = "Invalid state!")]
        public IFunction<Context, string>? Message;

        public override Return Invoke(Context context)
        {
            throw new XTMFRuntimeException(this, Message?.Invoke(context));
        }
    }
}
