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

/// <summary>
/// Provides the backing for a simple parameter
/// </summary>
internal class BasicParameter : ParameterExpression
{
    /// <summary>
    /// The string presentation of the parameter
    /// </summary>
    private string _value;

    /// <summary>
    /// Create a basic parameter
    /// </summary>
    /// <param name="value">The string value of the parameter.</param>
    public BasicParameter(string value)
    {
        _value = value;
    }

    /// <inheritdoc/>
    public override string GetRepresentation()
    {
        return _value;
    }

    /// <inheritdoc/>
    internal override bool IsCompatible(Type type, ref string? errorString)
    {
        return !ArbitraryParameterParser.Check(type, _value, ref errorString);
    }

    internal override object GetValue(Type type, ref string? errorString)
    {
        var (sucess, value) = ArbitraryParameterParser.ArbitraryParameterParse(type, _value, ref errorString);
        if (sucess)
        {
            errorString = null;
            return value!;
        }
        return false;
    }
}
