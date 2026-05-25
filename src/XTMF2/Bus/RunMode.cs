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

namespace XTMF2.Bus;

/// <summary>
/// Specifies the mode in which a model system will be run.
/// </summary>
public enum RunMode
{
    /// <summary>Execute the model system once with the current parameters.</summary>
    Normal = 0,

    /// <summary>
    /// Run the model system repeatedly with a search algorithm that adjusts the nominated
    /// estimation parameters to minimise the value returned by the fitness node.
    /// </summary>
    Estimation = 1,

    /// <summary>
    /// Run the model system repeatedly, reading each calibration target after execution and
    /// updating the corresponding parameter proportionally until convergence.
    /// </summary>
    Calibration = 2,
}
