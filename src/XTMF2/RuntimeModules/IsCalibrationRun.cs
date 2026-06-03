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

[Module(Name = "Is Calibration Run",
    Description = "Returns true when the currently executing run is in calibration mode.",
    DocumentationLink = "https://tmg.utoronto.ca/doc/2.0/xtmf2/modules/XTMF2/RuntimeModules/IsCalibrationRun.html")]
public sealed class IsCalibrationRun : BaseFunction<bool>
{
    private readonly XTMFRuntime _runtime;

    public IsCalibrationRun(XTMFRuntime runtime)
    {
        _runtime = runtime;
    }

    public override bool Invoke()
    {
        return _runtime.RunBus?.CurrentRun?.IsCalibrationRun ?? false;
    }
}
