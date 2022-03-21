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
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using XTMF2.ModelSystemConstruct.Parameters.Compiler;

namespace XTMF2.ModelSystemConstruct;

/// <summary>
/// This class represents a calculation
/// </summary>
public abstract class Expression
{
    /// <summary>
    /// The string representation of this expression.
    /// </summary>
    protected readonly ReadOnlyMemory<char> Text;
    
    /// <summary>
    /// The offset into the full expression that this starts at.
    /// </summary>
    protected readonly int Offset;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="expression">The text form of the expression that this represents.</param>
    /// <param name="offset">The offset into the full expression that we start at.</param>
    protected Expression(ReadOnlyMemory<char> expression, int offset)
    {
        Text = expression;
        Offset = offset;
    }

    /// <summary>
    /// Gets the string representation of the expression.
    /// </summary>
    /// <returns>The string representation of the expression.</returns>
    public ReadOnlySpan<char> AsString()
    {
        return Text.Span;
    }

    /// <summary>
    /// The type that the expression will evaluate to.
    /// </summary>
    public abstract Type Type { get; }

    /// <summary>
    /// Gets a result with the literal's value.
    /// </summary>
    /// <returns>The result that this literal represents.</returns>
    /// <param name="caller">The module that is asking for this expression to be resolved.</param>
    internal abstract Result GetResult(IModule caller);
}
