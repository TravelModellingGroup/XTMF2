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


[Module(Name = "Set Parameter", Description = "Sets the value of a parameter to the provided value.",
    DocumentationLink = "https://tmg.utoronto.ca/doc/2.0/xtmf2/modules/XTMF2/RuntimeModules/SetParameter.html")]
public sealed class SetParameter<T> : BaseAction
{
    [Parameter(Required = true, Name = "Value", Description = "The value to set the parameter to.", Index = 0)]
    public ISetableValue<T> Value = null!;

    [Parameter(Required = true, Name = "New Value", Description = "The value to set the parameter to.", Index = 1)]
    public IFunction<T> NewValue = null!;

    public override void Invoke()
    {
        Value.Set(NewValue.Invoke());
    }

}
