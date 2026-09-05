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
using System;

namespace XTMF2.RuntimeModules;

[Module(Name = "Get Module Name",
    Description = "Gets the name of the connected module, or FunctionInstance",
    DocumentationLink = "https://tmg.utoronto.ca/doc/2.0/xtmf2/modules/XTMF2/RuntimeModules/GetModuleName.html"
    )]
public sealed class GetModuleName : BaseFunction<string>
{
    [SubModule(Required = true, Description = "The module from which to get the name", Name = "GetNameFrom", Index = 0)]
    public IModule GetNameFrom = null!;

    public override string Invoke()
    {
        var moduleName = GetNameFrom.Name;
        return String.IsNullOrWhiteSpace(moduleName) ? string.Empty : moduleName;
    }
}
