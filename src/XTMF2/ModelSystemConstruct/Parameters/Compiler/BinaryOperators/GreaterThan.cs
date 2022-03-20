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
/// This provides the implementation for comparing
/// </summary>
internal sealed class GreaterThanOperator : Expression
{
    /// <summary>
    /// The left hand side expression
    /// </summary>
    private readonly Expression _lhs;

    /// <summary>
    /// The right hand side expression.
    /// </summary>
    private readonly Expression _rhs;

    /// <summary>
    /// Create a new less than operation.
    /// </summary>
    /// <param name="lhs">The expression for the left hand side.</param>
    /// <param name="rhs">The expression for the right hand side.</param>
    /// <param name="expression">The string representation of the add.</param>
    /// <param name="offset">The offset into the full expression that this add starts at.</param>
    public GreaterThanOperator(Expression lhs, Expression rhs, ReadOnlyMemory<char> expression, int offset) : base(expression, offset)
    {
        _lhs = lhs;
        _rhs = rhs;
        TestTypes(_lhs, _rhs, offset);
    }

    /// <inheritdoc/>
    public override Type Type => typeof(bool);

    /// <inheritdoc/>
    internal override Result GetResult(IModule caller)
    {
        if (GetResult(caller, _lhs, out var lhs, out var errorResult)
            && GetResult(caller, _rhs, out var rhs, out errorResult))
        {
            if (lhs is int l && rhs is int r)
            {
                return new BooleanResult(l > r);
            }
            else if (lhs is float lf && rhs is float rf)
            {
                return new BooleanResult(lf > rf);
            }
            else
            {
                return new ErrorResult($"Unknown type pair for > operator, {_lhs.Type.FullName} and {_rhs.Type.FullName}!", _lhs.Type);
            }
        }
        return errorResult;
    }

    /// <summary>
    /// Gets the result for the given expression.
    /// </summary>
    /// <param name="caller">The module that has requested this expression to be evaluated.</param>
    /// <param name="child">The child expression to evaluate.</param>
    /// <param name="result">The result from the expression, 0 if false.</param>
    /// <param name="errorResult">If false it will contain the error message from the expression.</param>
    /// <returns>Returns true if we were able to get the result of the expression, false otherwise with the error result.</returns>
    private static bool GetResult(IModule caller, Expression child, out object? result, [NotNullWhen(false)] out Result? errorResult)
    {
        string? error = null;
        errorResult = null;
        var childResult = child.GetResult(caller);
        if (childResult.TryGetResult(out result, ref error))
        {
            return true;
        }
        result = default;
        errorResult = childResult;
        return false;
    }

    private static Type[] _SupportedTypes = new[] { typeof(int), typeof(float) };

    /// <summary>
    /// Test to see if the types are compatible
    /// </summary>
    /// <param name="lhs">The LHS of the binary operator.</param>
    /// <param name="rhs">The RHS of the binary operator.</param>
    /// <param name="offset">The offset into the full expression where this expression starts.</param>
    /// <exception cref="CompilerException">Throws a compiler exception of the types are not compatible.</exception>
    private static void TestTypes(Expression lhs, Expression rhs, int offset)
    {
        if (lhs.Type != rhs.Type)
        {
            throw new CompilerException($"The LHS and RHS of the > operation are not of the same type! LHS = {lhs.Type.FullName}, RHS = {rhs.Type.FullName}", offset);
        }
        else if (Array.IndexOf(_SupportedTypes, lhs.Type) < 0)
        {
            throw new CompilerException($"The > operator does not support the type {lhs.Type.FullName}!", offset);
        }
    }
}
