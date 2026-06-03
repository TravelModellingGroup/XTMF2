/*
    Copyright 2017-2021 University of Toronto

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


using System.Threading.Tasks;

namespace XTMF2.RuntimeModules;

[Module(Name = "Sleep", DocumentationLink = "https://tmg.utoronto.ca/doc/2.0/xtmf2/modules/XTMF2/RuntimeModules/Sleep.html", Description = "Pauses execution for a specified duration.")]
public sealed class Sleep : BaseAction
{
    [Parameter(DefaultValue = "1000", Description = "The number of milliseconds to sleep for.", Index = 0,
        Name = "Sleep Duration (ms)", Required = true)]
    public IFunction<int>? Duration;

    public override void Invoke()
    {
        var duration = Duration!.Invoke();
        if (duration < 0)
        {
            throw new XTMFRuntimeException(this, "Sleep duration cannot be negative!");
        }

        // We use Task.Delay here instead of Thread.Sleep to make sur ehte task does not get scheduled on another thread.
        Task.Delay(duration).GetAwaiter().GetResult();
    }

    public override bool RuntimeValidation(ref string? error)
    {
        var duration = Duration!.Invoke();
        if (duration < 0)
        {
            throw new XTMFRuntimeException(this, "Sleep duration cannot be negative!");
        }
        return base.RuntimeValidation(ref error);
    }
}