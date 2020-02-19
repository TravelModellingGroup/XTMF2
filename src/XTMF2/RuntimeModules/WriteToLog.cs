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
    [Module(Name = "Write to Log", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
Description = "Writes the provided mess to the log and then invokes the next step.")]
    public class WriteToLogF<Return> : BaseFunction<Return>
    {
        [SubModule(Required = true, Name = "Log", Description = "The log that will be written to.", Index = 0)]
        public IAction<string>? Log;

        [SubModule(Required = true, Name = "To Invoke", Description = "The function to execute after writing to the log.", Index = 1)]
        public IFunction<Return>? ToInvoke;

        [Parameter(Required = true, Name = "Message", Description = "The message to write to the log.", DefaultValue = "", Index = 2)]
        public IFunction<string>? Message;

        public override Return Invoke()
        {
            Log!.Invoke(Message!.Invoke());
            return ToInvoke!.Invoke();
        }
    }

    [Module(Name = "Write to Log", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
Description = "Writes the provided mess to the log and then invokes the next step.")]
    public class WriteToLogF<Context, Return> : BaseFunction<Context, Return>
    {
        [SubModule(Required = true, Name = "Log", Description = "The log that will be written to.", Index = 0)]
        public IAction<string>? Log;

        [SubModule(Required = true, Name = "To Invoke", Description = "The function to execute after writing to the log.", Index = 1)]
        public IFunction<Context, Return>? ToInvoke;

        [Parameter(Required = true, Name = "Message", Description = "The message to write to the log.", DefaultValue = "", Index = 2)]
        public IFunction<string>? Message;

        public override Return Invoke(Context context)
        {
            Log!.Invoke(Message!.Invoke());
            return ToInvoke!.Invoke(context);
        }
    }

    [Module(Name = "Write to Log", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
Description = "Writes the provided mess to the log and then invokes the next step.")]
    public class WriteToLogBasedOnContextF<Context, Return> : BaseFunction<Context, Return>
    {
        [SubModule(Required = true, Name = "Log", Description = "The log that will be written to.", Index = 0)]
        public IAction<string>? Log;

        [SubModule(Required = true, Name = "To Invoke", Description = "The function to execute after writing to the log.", Index = 1)]
        public IFunction<Context, Return>? ToInvoke;

        [Parameter(Required = true, Name = "Message", Description = "The message to write to the log.", DefaultValue = "", Index = 2)]
        public IFunction<Context, string>? Message;

        public override Return Invoke(Context context)
        {
            Log!.Invoke(Message!.Invoke(context));
            return ToInvoke!.Invoke(context);
        }
    }

    [Module(Name = "Write to Log", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
Description = "Writes the provided mess to the log and then invokes the next step.")]
    public class WriteToLogA : BaseAction
    {
        [SubModule(Required = true, Name = "Log", Description = "The log that will be written to.", Index = 0)]
        public IAction<string>? Log;

        [SubModule(Required = true, Name = "To Invoke", Description = "The function to execute after writing to the log.", Index = 1)]
        public IAction? ToInvoke;

        [Parameter(Required = true, Name = "Message", Description = "The message to write to the log.", DefaultValue = "", Index = 2)]
        public IFunction<string>? Message;

        public override void Invoke()
        {
            Log!.Invoke(Message!.Invoke());
            ToInvoke!.Invoke();
        }
    }

    [Module(Name = "Write to Log", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
Description = "Writes the provided mess to the log and then invokes the next step.")]
    public class WriteToLogA<Context> : BaseAction<Context>
    {
        [SubModule(Required = true, Name = "Log", Description = "The log that will be written to.", Index = 0)]
        public IAction<string>? Log;

        [SubModule(Required = true, Name = "To Invoke", Description = "The function to execute after writing to the log.", Index = 1)]
        public IAction<Context>? ToInvoke;

        [Parameter(Required = true, Name = "Message", Description = "The message to write to the log.", DefaultValue = "", Index = 2)]
        public IFunction<string>? Message;

        public override void Invoke(Context context)
        {
            Log!.Invoke(Message!.Invoke());
            ToInvoke!.Invoke(context);
        }
    }

    [Module(Name = "Write to Log", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
Description = "Writes the provided mess to the log and then invokes the next step.")]
    public class WriteToLogBasedOnContextA<Context> : BaseAction<Context>
    {
        [SubModule(Required = true, Name = "Log", Description = "The log that will be written to.", Index = 0)]
        public IAction<string>? Log;

        [SubModule(Required = true, Name = "To Invoke", Description = "The function to execute after writing to the log.", Index = 1)]
        public IAction<Context>? ToInvoke;

        [Parameter(Required = true, Name = "Message", Description = "The message to write to the log.", DefaultValue = "", Index = 2)]
        public IFunction<Context, string>? Message;

        public override void Invoke(Context context)
        {
            Log!.Invoke(Message!.Invoke(context));
            ToInvoke!.Invoke(context);
        }
    }
}
