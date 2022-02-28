/*
    Copyright 2022 University of Toronto
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

namespace XTMF2.ModelSystemConstruct.Parameters;

internal class ScriptedParameter : ParameterExpression
{
    private Expression _expression;

    public ScriptedParameter(Expression expression)
    {
        _expression = expression;
    }

    public override string GetRepresentation()
    {
        return _expression.AsString();
    }

    public override string? ToString()
    {
        return base.ToString();
    }

    internal override object GetValue(Type type, ref string? errorString)
    {
        throw new NotImplementedException();
    }

    internal override bool IsCompatible(Type type, ref string? errorString)
    {
        throw new NotImplementedException();
    }
}
