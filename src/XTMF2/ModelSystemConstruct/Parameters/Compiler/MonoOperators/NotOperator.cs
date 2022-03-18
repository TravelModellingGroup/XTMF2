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

namespace XTMF2.ModelSystemConstruct.Parameters.Compiler;

/// <summary>
/// Provides the ability to invert a boolean result.
/// </summary>
internal class NotOperator : Expression
{

    private readonly Expression _innerExpression;

    public NotOperator(Expression innerExpression, ReadOnlyMemory<char> text, int offset) : base(text, offset)
    {
        _innerExpression = innerExpression;
    }

    /// <inheritdoc/>
    public override Type Type => typeof(bool);

    /// <inheritdoc/>
    internal override Result GetResult(IModule caller)
    {
        string? error = null;
        var result = _innerExpression.GetResult(caller);
        if(result.TryGetResult(out var value, ref error))
        {
            if(value is bool b)
            {
                return new BooleanResult(!b);
            }
        }
        if (result is ErrorResult)
        {
            return result;
        }
        else
        {
            return new ErrorResult("Invalid result type passed into not operator!", typeof(bool));
        }
    }
}
