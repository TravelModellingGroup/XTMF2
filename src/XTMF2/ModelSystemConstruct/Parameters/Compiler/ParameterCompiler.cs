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
using System.Linq;

namespace XTMF2.ModelSystemConstruct.Parameters.Compiler;

/// <summary>
/// Provides the entry point for compiling and evaluating expressions.
/// </summary>
public static class ParameterCompiler
{
    /// <summary>
    /// Evaluate the expression and return the value.
    /// </summary>
    /// <param name="module">The module that is requesting this expression to be evaluated.</param>
    /// <param name="expression">The expression to evaluate.</param>
    /// <param name="value">The returned object from the evaluation, null if the expression can not be evaluated.</param>*
    /// <param name="error">An error message if Evaluate returns false.</param>
    /// <returns>True if the expression was evaluated correctly.</returns>
    public static bool Evaluate(IModule module, Expression expression, [NotNullWhen(true)] out object? value, [NotNullWhen(false)] ref string? error)
    {
        var result = expression.GetResult(module);
        if (result.TryGetResult(out value, ref error))
        {
            return true;
        }
        throw new XTMFRuntimeException(module, error);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="nodes"></param>
    /// <param name="expresionText"></param>
    /// <param name="expression"></param>
    /// <param name="error"></param>
    /// <returns></returns>
    public static bool CreateExpression(IList<Node> nodes, string expresionText, [NotNullWhen(true)] out Expression? expression, [NotNullWhen(false)] ref string? error)
    {
        error = null;
        try
        {
            return Compile(nodes, expresionText.AsMemory(), 0, out expression);
        }
        catch(CompilerException exception)
        {
            error = exception.Message + $" Position: {exception.Position}";
            expression = null;
            return false;
        }
    }

    private static bool Compile(IList<Node> nodes, ReadOnlyMemory<char> text, int offset, [NotNullWhen(true)] out Expression? expression)
    {
        if( GetVariable(nodes, text, offset, out expression)
            || GetStringLiteral(text, offset, out expression)
            || GetBooleanLiteral(text, offset, out expression)  
            || GetIntegerLiteral(text, offset, out expression)
            || GetFloatingPointLiteral(text, offset, out expression)
        )
        {
            return true;
        }
        UnableToInterpret(text, offset);
        // This will never actually be executed
        return false; 
    }

    private static bool GetVariable(IList<Node> nodes, ReadOnlyMemory<char> text, int offset, [NotNullWhen(true)] out Expression? expression)
    {
        expression = null;
        var span = text.Span;
        var start = IndexOfFirstNonWhiteSpace(span);
        var end = start;
        for (; end < span.Length; end++)
        {
            if (char.IsWhiteSpace(span[end]))
            {
                break;
            }
        }
        // If there was nothing here or if we find any non-white space characters after the end of our variable name
        if (end == start
            || (end < span.Length && IndexOfFirstNonWhiteSpace(span[end..]) >= 0))
        {
            return false;
        }
        var innerText = text[start..end];
        var node = nodes.FirstOrDefault(n => innerText.Span.Equals(n.Name.AsSpan(), StringComparison.InvariantCulture));
        if (node is not null)
        {
            expression = Variable.CreateVariableForNode(node, innerText, offset + start);
            return true;
        }
        else
        {
            return false;
        }
    }

    private static bool GetStringLiteral(ReadOnlyMemory<char> text, int offset, [NotNullWhen(true)] out Expression? expression)
    {
        var span = text.Span;
        expression = null;
        int first = span.IndexOf('"');
        if(first == -1)
        {
            return false;
        }
        int second = span[(first + 1)..].IndexOf('"');
        if(second == -1)
        {
            throw new CompilerException("Unmatched string quote found!", offset + first);
        }
        // If there is anything after the second quote this is an invalid string literal.
        // +2 skips the ending quote
        if(IndexOfFirstNonWhiteSpace(span[(second + 2)..]) >= 0)
        {
            return false;
        }
        expression = new StringLiteral(text.Slice(first + 1, second), offset + first);
        return true;
    }

    private static bool GetBooleanLiteral(ReadOnlyMemory<char> text, int offset, [NotNullWhen(true)] out Expression? expression)
    {
        expression = null;
        var span = text.Span;
        var start = IndexOfFirstNonWhiteSpace(span);
        var end = start;
        for (; end < span.Length; end++)
        {
            if (char.IsWhiteSpace(span[end]))
            {
                break;
            }
        }
        // If there was nothing here or if we find any non-white space characters after the end of our number
        if (end == start
            || (end < span.Length && IndexOfFirstNonWhiteSpace(span[end..]) >= 0))
        {
            return false;
        }
        var innerText = span[start..end];
        if (bool.TryParse(innerText, out _))
        {
            expression = new BooleanLiteral(text[start..end], offset + start);
            return true;
        }
        return false;
    }

    private static bool GetIntegerLiteral(ReadOnlyMemory<char> text, int offset, [NotNullWhen(true)] out Expression? expression)
    {
        expression = null;
        var span = text.Span;
        var start = IndexOfFirstNonWhiteSpace(span);
        var end = start;
        for (; end < span.Length; end++)
        {
            if (!char.IsDigit(span[end]))
            {
                break;
            }
        }
        // If there was nothing here or if we find any non-white space characters after the end of our number
        if (end == start
            || (end < span.Length && IndexOfFirstNonWhiteSpace(span.Slice(end)) >= 0))
        {
            return false;
        }
        expression = new IntegerLiteral(text[start..end], offset + start);
        return true;
    }

    private static bool GetFloatingPointLiteral(ReadOnlyMemory<char> text, int offset, [NotNullWhen(true)] out Expression? expression)
    {
        expression = null;
        var span = text.Span;
        var start = IndexOfFirstNonWhiteSpace(span);
        var end = start;
        for (; end < span.Length; end++)
        {
            if(!(char.IsDigit(span[end]) || span[end] == '.'))
            {
                break;
            }
        }
        // If there was nothing here or if we find any non-white space characters after the end of our number
        if(end == start
            || (end < span.Length && IndexOfFirstNonWhiteSpace(span.Slice(end)) >= 0))
        {
            return false;
        }
        expression = new FloatLiteral(text[start..end], offset + start);
        return true;
    }

    private static int IndexOfFirstNonWhiteSpace(ReadOnlySpan<char> text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if(!char.IsWhiteSpace(text[i]))
            {
                return i;
            }
        }
        return -1;
    }

    [DoesNotReturn]
    private static void UnableToInterpret(ReadOnlyMemory<char> text, int offset)
    {
        throw new CompilerException($"Unable to interpret \"{text}\"", offset);
    }
}
