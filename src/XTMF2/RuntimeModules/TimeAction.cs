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
using System.Diagnostics;

namespace XTMF2.RuntimeModules;

[Module(Name = "Time Action", Description = "Measures the elapsed time taken by an action in milliseconds.",
 DocumentationLink = "https://tmg.utoronto.ca/doc/2.0/xtmf2/modules/XTMF2/RuntimeModules/TimeAction.html")]
[AiModuleInstructions("Connect To Time to the action to measure. Optionally connect Elapsed Time (ms) to an ISetableValue<double> to receive the duration.")]
public sealed class TimeAction : BaseAction
{
    [SubModule(Required = true, Name = "To Time", Description = "The action to invoke and time.", Index = 0, PassesExecution = true)]
    public IAction? ToTime;

    [SubModule(Required = false, Name = "Elapsed Time (ms)", Description = "Receives the measured duration in milliseconds.", Index = 1)]
    public ISetableValue<float>? ElapsedTime;

    public override void Invoke()
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            ToTime!.Invoke();
        }
        finally
        {
            stopwatch.Stop();
            ElapsedTime?.Set((float)stopwatch.Elapsed.TotalMilliseconds);
        }
    }
}

[Module(Name = "Time Action", Description = "Measures the elapsed time taken by a context-aware action in milliseconds.",
DocumentationLink = "https://tmg.utoronto.ca/doc/2.0/xtmf2/modules/XTMF2/RuntimeModules/TimeAction.html")]
[AiModuleInstructions("Connect To Time to the context-aware action to measure. Optionally connect Elapsed Time (ms) to an ISetableValue<double> to receive the duration; the input context is forwarded unchanged.")]
public sealed class TimeAction<Context> : BaseAction<Context>
{
    [SubModule(Required = true, Name = "To Time", Description = "The context-aware action to invoke and time.", Index = 0, PassesExecution = true)]
    public IAction<Context>? ToTime;

    [SubModule(Required = false, Name = "Elapsed Time (ms)", Description = "Receives the measured duration in milliseconds.", Index = 1)]
    public ISetableValue<float>? ElapsedTime;

    public override void Invoke(Context context)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            ToTime!.Invoke(context);
        }
        finally
        {
            stopwatch.Stop();
            ElapsedTime?.Set((float)stopwatch.Elapsed.TotalMilliseconds);
        }
    }
}
