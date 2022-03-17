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
using System.Diagnostics.CodeAnalysis;

namespace XTMF2.ModelSystemConstruct.Parameters.Compiler;

/// <summary>
/// Implements the result that evaluates to a Boolean value.
/// </summary>
internal sealed class StringResult : Result
{
    /// <inheritdoc/>
    public override Type ReturnType => typeof(string);

    /// <summary>
    /// The contained result
    /// </summary>
    private readonly string _value;

    /// <summary>
    /// Create a new result.
    /// </summary>
    /// <param name="result">The value to return</param>
    public StringResult(string result)
    {
        _value = result;
    }

    /// <inheritdoc/>
    public override bool TryGetResult([NotNullWhen(true)] out object? result, [NotNullWhen(false)] ref string? error)
    {
        result = _value;
        return true;
    }
}
