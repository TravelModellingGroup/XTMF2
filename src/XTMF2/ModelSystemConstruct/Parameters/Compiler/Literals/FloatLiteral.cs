﻿/*
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
/// Represents a float literal value
/// </summary>
internal sealed class FloatLiteral : Literal
{
    /// <summary>
    /// Create a new float literal.
    /// </summary>
    /// <param name="expression"></param>
    /// <param name="offset"></param>
    public FloatLiteral(ReadOnlyMemory<char> expression, int offset) : base(expression, offset) { }

    ///<inheritdoc/>
    internal override Result GetResult(IModule caller)
    {
        if(float.TryParse(Text.Span, out var result))
        {
            return new FloatResult(result);
        }
        else
        {
            return new ErrorResult($"Unable to process float literal, \"{Text}\" starting at position {Offset}!", Type);
        }
    }

    ///<inheritdoc/>
    public override Type Type => typeof(float);
}
