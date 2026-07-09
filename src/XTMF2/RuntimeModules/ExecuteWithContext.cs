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

namespace XTMF2.RuntimeModules;

[Module(Name = "Execute With Context", DocumentationLink = "https://tmg.utoronto.ca/doc/2.0/xtmf2/modules/XTMF2/RuntimeModules/ExecuteWithContext.html",
    Description = "Provides a way to execute a series of actions with a context loaded from the provided context.")]
public sealed class ExecuteWithContext<Context> : BaseAction
{
    [SubModule(Required = true, Name = "Get Context", Description = "The function to get the context to execute with.", Index = 0)]
    public IFunction<Context> GetContext = null!;

    [SubModule(Required = true, Name = "To Execute", Description = "The actions to execute with the context.", Index = 1, PassesExecution = true)]
    public IAction<Context>[] ToInvoke = null!;

    override public void Invoke()
    {
        var context = GetContext.Invoke();
        foreach (var action in ToInvoke!)
        {
            action.Invoke(context);
        }
    }
}

[Module(Name = "Execute With Forwarded Context", DocumentationLink = "https://tmg.utoronto.ca/doc/2.0/xtmf2/modules/XTMF2/RuntimeModules/ExecuteWithForwardedContext.html",
    Description = "Provides a way to execute a series of actions using a context provided to it.")]
public sealed class ExecuteWithForwardedContext<Context> : BaseAction<Context>
{
    [SubModule(Required = true, Name = "To Execute", Description = "The actions to execute with the context.", Index = 0, PassesExecution = true)]
    public IAction<Context>[] ToInvoke = null!;

    override public void Invoke(Context context)
    {
        foreach (var action in ToInvoke!)
        {
            action.Invoke(context);
        }
    }
}