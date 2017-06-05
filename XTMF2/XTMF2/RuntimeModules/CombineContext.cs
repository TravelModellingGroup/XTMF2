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
    [Module(Name = "Combine Context From No Context", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
Description = "Combines the contexts as derived from First and Second and invokes To Invoke with the combined context.")]
    public sealed class CombineContextAFromNoContext<Context1, Context2> : BaseAction
    {
        [SubModule(Name = "First", Required = true, Index = 0, Description = "The first context to use.")]
        public IFunction<Context1> First;

        [SubModule(Name = "Second", Required = true, Index = 1, Description = "The second context to use.")]
        public IFunction<Context2> Second;

        [SubModule(Name = "To Invoke", Required = true, Index = 2, Description = "The module to invoke with the combined context.")]
        public IAction<(Context1, Context2)> ToInvoke;
        
        public override void Invoke()
        {
            ToInvoke.Invoke((First.Invoke(), Second.Invoke()));
        }
    }

    [Module(Name = "Combine Context From No Context", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
Description = "Combines the contexts as derived from First and Second and invokes To Invoke with the combined context.")]
    public sealed class CombineContextA<Context1, Context2> : BaseAction<Context1>
    {
        [SubModule(Name = "Second", Required = true, Index = 0, Description = "The second context to use.")]
        public IFunction<Context2> Second;
        [SubModule(Name = "To Invoke", Required = true, Index = 1, Description = "The module to invoke with the combined context.")]
        public IAction<(Context1, Context2)> ToInvoke;

        public override void Invoke(Context1 context)
        {
            ToInvoke.Invoke((context, Second.Invoke()));
        }
    }

    [Module(Name = "Combine Context From No Context", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
Description = "Combines the contexts as derived from First and Second and invokes To Invoke with the combined context.")]
    public sealed class CombineContextAFromContext<Context1, Context2> : BaseAction<Context1>
    {
        [SubModule(Name = "Second", Required = true, Index = 0, Description = "The second context to use.")]
        public IFunction<Context1, Context2> Second;
        [SubModule(Name = "To Invoke", Required = true, Index = 1, Description = "The module to invoke with the combined context.")]
        public IAction<(Context1, Context2)> ToInvoke;

        public override void Invoke(Context1 context)
        {
            ToInvoke.Invoke((context, Second.Invoke(context)));
        }
    }

    [Module(Name = "Combine Context From No Context", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
Description = "Combines the contexts as derived from First and Second and invokes To Invoke with the combined context.")]
    public sealed class CombineContextFFromNoContext<Context1, Context2, Return> : BaseFunction<Return>
    {
        [SubModule(Name = "First", Required = true, Index = 0, Description = "The first context to use.")]
        public IFunction<Context1> First;

        [SubModule(Name = "Second", Required = true, Index = 1, Description = "The second context to use.")]
        public IFunction<Context2> Second;

        [SubModule(Name = "To Invoke", Required = true, Index = 2, Description = "The module to invoke with the combined context.")]
        public IFunction<(Context1, Context2), Return> ToInvoke;

        public override Return Invoke()
        {
            return ToInvoke.Invoke((First.Invoke(), Second.Invoke()));
        }
    }

    [Module(Name = "Combine Context From No Context", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
Description = "Combines the contexts as derived from First and Second and invokes To Invoke with the combined context.")]
    public sealed class CombineContexF<Context1, Context2, Return> : BaseFunction<Context1, Return>
    {
        [SubModule(Name = "Second", Required = true, Index = 0, Description = "The second context to use.")]
        public IFunction<Context2> Second;
        [SubModule(Name = "To Invoke", Required = true, Index = 1, Description = "The module to invoke with the combined context.")]
        public IFunction<(Context1, Context2), Return> ToInvoke;

        public override Return Invoke(Context1 context)
        {
            return ToInvoke.Invoke((context, Second.Invoke()));
        }
    }

    [Module(Name = "Combine Context From No Context", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
Description = "Combines the contexts as derived from First and Second and invokes To Invoke with the combined context.")]
    public sealed class CombineContextFFromContext<Context1, Context2, Return> : BaseFunction<Context1, Return>
    {
        [SubModule(Name = "Second", Required = true, Index = 0, Description = "The second context to use.")]
        public IFunction<Context1, Context2> Second;
        [SubModule(Name = "To Invoke", Required = true, Index = 1, Description = "The module to invoke with the combined context.")]
        public IFunction<(Context1, Context2), Return> ToInvoke;

        public override Return Invoke(Context1 context)
        {
            return ToInvoke.Invoke((context, Second.Invoke(context)));
        }
    }
}
