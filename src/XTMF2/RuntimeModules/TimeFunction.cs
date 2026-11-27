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

[Module(Name = "Time Function", Description = "Measures the elapsed time taken by a function in milliseconds.",
DocumentationLink = "https://tmg.utoronto.ca/doc/2.0/xtmf2/modules/XTMF2/RuntimeModules/TimeFunction.html")]
[AiModuleInstructions("Connect To Time to the function to measure. Optionally connect Elapsed Time (ms) to an ISetableValue<double> to receive the duration; the function's original result is preserved.")]
public sealed class TimeFunction<Result> : BaseFunction<Result>
{
    [SubModule(Required = true, Name = "To Time", Description = "The function to invoke and time.", Index = 0)]
    public IFunction<Result>? ToTime;

    [SubModule(Required = false, Name = "Elapsed Time (ms)", Description = "Receives the measured duration in milliseconds.", Index = 1)]
    public ISetableValue<float>? ElapsedTime;

    public override Result Invoke()
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return ToTime!.Invoke();
        }
        finally
        {
            stopwatch.Stop();
            ElapsedTime?.Set((float)stopwatch.Elapsed.TotalMilliseconds);
        }
    }
}

[Module(Name = "Time Function", Description = "Measures the elapsed time taken by a context-aware function in milliseconds.",
DocumentationLink = "https://tmg.utoronto.ca/doc/2.0/xtmf2/modules/XTMF2/RuntimeModules/TimeFunction.html")]
[AiModuleInstructions("Connect To Time to the context-aware function to measure. Optionally connect Elapsed Time (ms) to an ISetableValue<double> to receive the duration; the function's original result and context are preserved.")]
public sealed class TimeFunction<Context, Result> : BaseFunction<Context, Result>
{
    [SubModule(Required = true, Name = "To Time", Description = "The context-aware function to invoke and time.", Index = 0)]
    public IFunction<Context, Result>? ToTime;

    [SubModule(Required = false, Name = "Elapsed Time (ms)", Description = "Receives the measured duration in milliseconds.", Index = 1)]
    public ISetableValue<float>? ElapsedTime;

    public override Result Invoke(Context context)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return ToTime!.Invoke(context);
        }
        finally
        {
            stopwatch.Stop();
            ElapsedTime?.Set((float)stopwatch.Elapsed.TotalMilliseconds);
        }
    }
}
