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

namespace XTMF2.ModelSystemConstruct;

/// <summary>
/// This class represents a calculation
/// </summary>
public class Expression
{
    private readonly string _expression;

    public Expression(string expression)
    {
        _expression = expression;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="nodes"></param>
    /// <param name="expresionText"></param>
    /// <param name="expression"></param>
    /// <param name="error"></param>
    /// <returns></returns>
    public bool CreateExpression(IList<Node> nodes, string expresionText, [NotNullWhen(true)] out Expression? expression, [NotNullWhen(false)] ref string? error)
    {
        expression = null;
        error = "Method not implemented!";
        return false;
    }

    /// <summary>
    /// Evaluate the expression and return the value.
    /// </summary>
    /// <param name="nodes">The nodes required to evaluate the expression.</param>
    /// <param name="value">The returned object from the evaluation, null if the expression can not be evaluated.</param>
    /// <param name="error">An error</param>
    /// <returns>True if the expression was evaluated correctly.</returns>
    public bool Evaluate(IList<Node> nodes, [NotNullWhen(true)]out object? value, [NotNullWhen(false)] ref string? error)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Gets the string representation of the expression.
    /// </summary>
    /// <returns>The string representation of the expression.</returns>
    public string AsString()
    {
        return _expression;
    }

    /// <summary>
    /// The type that the expression will evaluate to.
    /// </summary>
    public Type Type
    {
        get => throw new NotImplementedException();
    }
}
