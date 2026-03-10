/*
    Copyright 2026 University of Toronto

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

using System.Runtime.InteropServices;

namespace XTMF2.RuntimeModules;

[Module(Name = "With Context", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
    Description = "Provides a way to execute an action with a context loaded from the provided context.")]
public sealed class WithContext<Context1, Context2> : BaseAction where Context1 : Context2
{
    [SubModule(Required = true, Name = "Get Context", Description = "The function to get the context to execute with.", Index = 0)]
    public IFunction<Context1> GetContext = null!;

    [SubModule(Required = true, Name = "To Execute", Description = "The action to execute with the context.", Index = 1)]
    public IAction<Context2> ToInvoke = null!;

    override public void Invoke()
    {
        var context = GetContext.Invoke();
        ToInvoke.Invoke(context);
    }
}

[Module(Name = "Return Using Context", DocumentationLink = "http://tmg.utoronto.ca/doc/2.0",
    Description = "Provides a way to execute a function with a context loaded from the provided context and return the result.")]
public sealed class ReturnUsingContext<Context, Return> : BaseFunction<Return>
{
    [SubModule(Required = true, Name = "Get Context", Description = "The function to get the context to execute with.", Index = 0)]
    public IFunction<Context> GetContext = null!;

    [SubModule(Required = true, Name = "To Execute", Description = "The function to execute with the context.", Index = 1)]
    public IFunction<Context, Return> ToInvoke = null!;

    override public Return Invoke()
    {
        var context = GetContext.Invoke();
        return ToInvoke.Invoke(context);
    }
}
