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
            ScanForInvalidBrackets(expresionText);
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
        if(
            GetEquals(nodes, text, offset, out expression)
            || GetNotEquals(nodes, text, offset, out expression)
            || GetGreaterThanOrEqual(nodes, text, offset, out expression)
            || GetGreaterThan(nodes, text, offset, out expression)
            || GetLessThanOrEqual(nodes, text, offset, out expression)
            || GetLessThan(nodes, text, offset, out expression)
            || GetAdd(nodes, text, offset, out expression)
            || GetSubtract(nodes, text, offset, out expression)
            || GetMultiply(nodes, text, offset, out expression)
            || GetDivide(nodes, text, offset, out expression)
            || GetNot(nodes, text, offset, out expression)
            || GetBracket(nodes, text, offset, out expression)
            || GetVariable(nodes, text, offset, out expression)
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

    private static bool GetAdd(IList<Node> nodes, ReadOnlyMemory<char> text, int offset, [NotNullWhen(true)] out Expression? expression)
    {
        expression = null;
        var span = text.Span;
        var startOfLHS = IndexOfFirstNonWhiteSpace(span);
        if(startOfLHS < 0)
        {
            return false;
        }
        int operatorIndex = IndexOfOutsideOfBrackets(span, startOfLHS + 1, '+');
        // if there was no plus we can exit
        if(operatorIndex <= -1)
        {
            return false;
        }
        if(!Compile(nodes, text.Slice(startOfLHS, operatorIndex - startOfLHS), offset + startOfLHS, out var lhs) 
            || !Compile(nodes, text[(operatorIndex + 1)..], offset + operatorIndex + 1, out var rhs))
        {
            return false;
        }
        expression = new AddOperator(lhs, rhs, text[startOfLHS..], startOfLHS + offset);
        return true;
    }

    private static bool GetSubtract(IList<Node> nodes, ReadOnlyMemory<char> text, int offset, [NotNullWhen(true)] out Expression? expression)
    {
        expression = null;
        var span = text.Span;
        var startOfLHS = IndexOfFirstNonWhiteSpace(span);
        if (startOfLHS < 0)
        {
            return false;
        }
        int operatorIndex = IndexOfOutsideOfBrackets(span, startOfLHS + 1, '-');
        // if there was no plus we can exit
        if (operatorIndex <= -1)
        {
            return false;
        }
        if (!Compile(nodes, text.Slice(startOfLHS, operatorIndex - startOfLHS), offset + startOfLHS, out var lhs)
            || !Compile(nodes, text[(operatorIndex + 1)..], offset + operatorIndex + 1, out var rhs))
        {
            return false;
        }
        expression = new SubtractOperator(lhs, rhs, text[startOfLHS..], startOfLHS + offset);
        return true;
    }

    private static bool GetMultiply(IList<Node> nodes, ReadOnlyMemory<char> text, int offset, [NotNullWhen(true)] out Expression? expression)
    {
        expression = null;
        var span = text.Span;
        var startOfLHS = IndexOfFirstNonWhiteSpace(span);
        if (startOfLHS < 0)
        {
            return false;
        }
        int operatorIndex = IndexOfOutsideOfBrackets(span, startOfLHS + 1, '*');
        // if there was no plus we can exit
        if (operatorIndex <= -1)
        {
            return false;
        }
        if (!Compile(nodes, text.Slice(startOfLHS, operatorIndex - startOfLHS), offset + startOfLHS, out var lhs)
            || !Compile(nodes, text[(operatorIndex + 1)..], offset + operatorIndex + 1, out var rhs))
        {
            return false;
        }
        expression = new MultiplyOperator(lhs, rhs, text[startOfLHS..], startOfLHS + offset);
        return true;
    }

    private static bool GetDivide(IList<Node> nodes, ReadOnlyMemory<char> text, int offset, [NotNullWhen(true)] out Expression? expression)
    {
        expression = null;
        var span = text.Span;
        var startOfLHS = IndexOfFirstNonWhiteSpace(span);
        if (startOfLHS < 0)
        {
            return false;
        }
        int operatorIndex = IndexOfOutsideOfBrackets(span, startOfLHS + 1, '/');
        // if there was no plus we can exit
        if (operatorIndex <= -1)
        {
            return false;
        }
        if (!Compile(nodes, text.Slice(startOfLHS, operatorIndex - startOfLHS), offset + startOfLHS, out var lhs)
            || !Compile(nodes, text[(operatorIndex + 1)..], offset + operatorIndex + 1, out var rhs))
        {
            return false;
        }
        expression = new DivideOperator(lhs, rhs, text[startOfLHS..], startOfLHS + offset);
        return true;
    }

    private static bool GetLessThan(IList<Node> nodes, ReadOnlyMemory<char> text, int offset, [NotNullWhen(true)] out Expression? expression)
    {
        expression = null;
        var span = text.Span;
        var startOfLHS = IndexOfFirstNonWhiteSpace(span);
        if (startOfLHS < 0)
        {
            return false;
        }
        int operatorIndex = IndexOfOutsideOfBrackets(span, startOfLHS + 1, '<');
        // if there was no plus we can exit
        if (operatorIndex <= -1)
        {
            return false;
        }
        if (!Compile(nodes, text.Slice(startOfLHS, operatorIndex - startOfLHS), offset + startOfLHS, out var lhs)
            || !Compile(nodes, text[(operatorIndex + 1)..], offset + operatorIndex + 1, out var rhs))
        {
            return false;
        }
        expression = new LessThanOperator(lhs, rhs, text[startOfLHS..], startOfLHS + offset);
        return true;
    }

    private static bool GetLessThanOrEqual(IList<Node> nodes, ReadOnlyMemory<char> text, int offset, [NotNullWhen(true)] out Expression? expression)
    {
        expression = null;
        var span = text.Span;
        var startOfLHS = IndexOfFirstNonWhiteSpace(span);
        if (startOfLHS < 0)
        {
            return false;
        }
        int operatorIndex = IndexOfOutsideOfBrackets(span, startOfLHS + 1, "<=");
        // if there was no plus we can exit
        if (operatorIndex <= -1)
        {
            return false;
        }
        if (!Compile(nodes, text.Slice(startOfLHS, operatorIndex - startOfLHS), offset + startOfLHS, out var lhs)
            || !Compile(nodes, text[(operatorIndex + 2)..], offset + operatorIndex + 2, out var rhs))
        {
            return false;
        }
        expression = new LessThanOrEqualOperator(lhs, rhs, text[startOfLHS..], startOfLHS + offset);
        return true;
    }

    private static bool GetGreaterThan(IList<Node> nodes, ReadOnlyMemory<char> text, int offset, [NotNullWhen(true)] out Expression? expression)
    {
        expression = null;
        var span = text.Span;
        var startOfLHS = IndexOfFirstNonWhiteSpace(span);
        if (startOfLHS < 0)
        {
            return false;
        }
        int operatorIndex = IndexOfOutsideOfBrackets(span, startOfLHS + 1, '>');
        // if there was no plus we can exit
        if (operatorIndex <= -1)
        {
            return false;
        }
        if (!Compile(nodes, text.Slice(startOfLHS, operatorIndex - startOfLHS), offset + startOfLHS, out var lhs)
            || !Compile(nodes, text[(operatorIndex + 1)..], offset + operatorIndex + 1, out var rhs))
        {
            return false;
        }
        expression = new GreaterThanOperator(lhs, rhs, text[startOfLHS..], startOfLHS + offset);
        return true;
    }

    private static bool GetGreaterThanOrEqual(IList<Node> nodes, ReadOnlyMemory<char> text, int offset, [NotNullWhen(true)] out Expression? expression)
    {
        expression = null;
        var span = text.Span;
        var startOfLHS = IndexOfFirstNonWhiteSpace(span);
        if (startOfLHS == -1)
        {
            return false;
        }
        int operatorIndex = IndexOfOutsideOfBrackets(span, startOfLHS + 1, ">=");
        // if there was no plus we can exit
        if (operatorIndex <= -1)
        {
            return false;
        }
        if (!Compile(nodes, text.Slice(startOfLHS, operatorIndex - startOfLHS), offset + startOfLHS, out var lhs)
            || !Compile(nodes, text[(operatorIndex + 2)..], offset + operatorIndex + 2, out var rhs))
        {
            return false;
        }
        expression = new GreaterThanOrEqualOperator(lhs, rhs, text[startOfLHS..], startOfLHS + offset);
        return true;
    }

    private static bool GetEquals(IList<Node> nodes, ReadOnlyMemory<char> text, int offset, [NotNullWhen(true)] out Expression? expression)
    {
        expression = null;
        var span = text.Span;
        var startOfLHS = IndexOfFirstNonWhiteSpace(span);
        if (startOfLHS == -1)
        {
            return false;
        }
        int operatorIndex = IndexOfOutsideOfBrackets(span, startOfLHS + 1, "==");
        // if there was no plus we can exit
        if (operatorIndex <= -1)
        {
            return false;
        }
        if (!Compile(nodes, text.Slice(startOfLHS, operatorIndex - startOfLHS), offset + startOfLHS, out var lhs)
            || !Compile(nodes, text[(operatorIndex + 2)..], offset + operatorIndex + 2, out var rhs))
        {
            return false;
        }
        expression = new EqualsOperator(lhs, rhs, text[startOfLHS..], startOfLHS + offset);
        return true;
    }

    private static bool GetNotEquals(IList<Node> nodes, ReadOnlyMemory<char> text, int offset, [NotNullWhen(true)] out Expression? expression)
    {
        expression = null;
        var span = text.Span;
        var startOfLHS = IndexOfFirstNonWhiteSpace(span);
        if (startOfLHS == -1)
        {
            return false;
        }
        int operatorIndex = IndexOfOutsideOfBrackets(span, startOfLHS + 1, "!=");
        // if there was no plus we can exit
        if (operatorIndex <= -1)
        {
            return false;
        }
        if (!Compile(nodes, text.Slice(startOfLHS, operatorIndex - startOfLHS), offset + startOfLHS, out var lhs)
            || !Compile(nodes, text[(operatorIndex + 2)..], offset + operatorIndex + 2, out var rhs))
        {
            return false;
        }
        expression = new NotEqualsOperator(lhs, rhs, text[startOfLHS..], startOfLHS + offset);
        return true;
    }

    private static bool GetNot(IList<Node> nodes, ReadOnlyMemory<char> text, int offset, [NotNullWhen(true)] out Expression? expression)
    {
        expression = null;
        var span = text.Span;
        var first = IndexOfFirstNonWhiteSpace(span);
        if (first == -1)
        { 
            return false;
        }
        if(span[first] != '!')
        {
            return false;
        }
        if(!Compile(nodes, text[(first + 1)..], offset + first + 1, out var inner))
        {
            return false;
        }
        if(inner.Type != typeof(bool))
        {
            throw new CompilerException($"Invalid expression type {inner.Type.FullName} passed into Not Operator!", first + offset);
        }
        expression = new NotOperator(inner, text[first..], offset + first);
        return true;
    }

    private static bool GetBracket(IList<Node> nodes, ReadOnlyMemory<char> text, int offset, [NotNullWhen(true)] out Expression? expression)
    {
        var span = text.Span;
        int bracketCount = 0;
        int first = -1;
        int second = -1;
        expression = null;
        for (int i = 0; i < span.Length; i++)
        {
            if(span[i] == '(')
            {
                if (first < 0)
                {
                    first = i;
                }
                bracketCount++;
            }
            else if(span[i] == ')')
            {
                bracketCount--;
                if(bracketCount < 0)
                {
                    throw new CompilerException("Unmatched close bracket found!", offset + i);
                }
                if(bracketCount == 0)
                {
                    second = i;
                    break;
                }
            }
            // If we come across a non-bracket non-white-space character before our bracket opens
            else if(first < 0 && !char.IsWhiteSpace(span[i]))
            {
                return false;
            }
        }
        if(first == -1)
        {
            return false;
        }
        if (second == -1)
        {
            throw new CompilerException("Unmatched bracket found!", offset + first);
        }
        // If there is anything after the second quote this is an invalid string literal.
        if (IndexOfFirstNonWhiteSpace(span[(second + 1)..]) >= 0)
        {
            return false;
        }
        // Optimize out the bracket
        return Compile(nodes, text.Slice(first + 1, second - first - 1), offset + first + 1, out expression);
    }

    private static bool GetVariable(IList<Node> nodes, ReadOnlyMemory<char> text, int offset, [NotNullWhen(true)] out Expression? expression)
    {
        expression = null;
        var span = text.Span;
        var start = IndexOfFirstNonWhiteSpace(span);
        if (start < 0)
        {
            return false;
        }
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
        if(first == -1 || first != IndexOfFirstNonWhiteSpace(span))
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
        if(IndexOfFirstNonWhiteSpace(span[(second + first + 2)..]) >= 0)
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
        if (start < 0)
        {
            return false;
        }
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
        if (start < 0)
        {
            return false;
        }
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
        if (start < 0)
        {
            return false;
        }
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

    /// <summary>
    /// Used for checking to see if the remaining text is empty or only white space.
    /// </summary>
    /// <param name="text">The text to check</param>
    /// <returns>True if the text is empty or only white space</returns>
    private static bool IsOnlyWhitespace(ReadOnlyMemory<char> text)
    {
        return IndexOfFirstNonWhiteSpace(text.Span) <= 0;
    }

    /// <summary>
    /// Gets the first index of a non-white space character.
    /// </summary>
    /// <param name="text">The text to search*</param>
    /// <returns>The index of the first non-white space character, or -1 if there are none.</returns>
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

    /// <summary>
    /// Gets the last index starting at the given position that is not inside of a bracket where the character is found.
    /// If the character does not exist, this will return negative one.
    /// </summary>
    /// <param name="text">The text to search.</param>
    /// <param name="start">The position in the text to start searching.</param>
    /// <param name="characterToFind">The character that we wish to find.</param>
    /// <returns>The position the character is found, outside of brackets, or -1 if it is not found.</returns>
    private static int IndexOfOutsideOfBrackets(ReadOnlySpan<char> text, int start, char characterToFind)
    {
        int bracketCounter = 0;
        int lastFound = -1;
        for (int i = start; i < text.Length; i++)
        {
            if (text[i] == '(')
            {
                bracketCounter++;
            }
            else if (text[i] == ')')
            {
                bracketCounter--;
            }
            else if (text[i] == characterToFind && bracketCounter == 0)
            {
                lastFound = i;
            }
        }
        return lastFound;
    }

    /// <summary>
    /// Gets the last index starting at the given position that is not inside of a bracket where the character is found.
    /// If the string does not exist, this will return negative one.
    /// </summary>
    /// <param name="text">The text to search.</param>
    /// <param name="start">The position in the text to start searching.</param>
    /// <param name="characterToFind">The character that we wish to find.</param>
    /// <returns>The position the string is found, outside of brackets, or -1 if it is not found.</returns>
    private static int IndexOfOutsideOfBrackets(ReadOnlySpan<char> text, int start, string stringToFind)
    {
        int bracketCounter = 0;
        int lastFound = -1;
        var firstCharacter = stringToFind[0];
        for (int i = start; i < text.Length - stringToFind.Length; i++)
        {
            if (text[i] == '(')
            {
                bracketCounter++;
            }
            else if (text[i] == ')')
            {
                bracketCounter--;
            }
            else if (bracketCounter == 0 && text[i] == firstCharacter)
            {
                bool allMatch = true;
                for (int j = 0; j < stringToFind.Length; j++)
                {
                    if(text[i + j] != stringToFind[j])
                    {
                        allMatch = false;
                    }
                }
                if (allMatch)
                {
                    lastFound = i;
                }
            }
        }
        return lastFound;
    }

    /// <summary>
    /// Detects
    /// </summary>
    /// <param name="expresionText"></param>
    /// <exception cref="CompilerException"></exception>
    private static void ScanForInvalidBrackets(string expresionText)
    {
        int bracketCount = 0;
        int mostOutsideBracketIndex = -1;
        var span = expresionText.AsSpan();
        for (int i = 0; i < span.Length; i++)
        {
            if (span[i] == '(')
            {
                if (bracketCount == 0)
                {
                    mostOutsideBracketIndex = i;
                }
                bracketCount++;
            }
            else if (span[i] == ')')
            {
                bracketCount--;
                if (bracketCount < 0)
                {
                    throw new CompilerException("Invalid closing bracket found!", i);
                }
            }
        }
        if (bracketCount > 0)
        {
            throw new CompilerException("Unmatched bracket found!", mostOutsideBracketIndex);
        }
    }

    [DoesNotReturn]
    private static void UnableToInterpret(ReadOnlyMemory<char> text, int offset)
    {
        throw new CompilerException($"Unable to interpret \"{text}\"", offset);
    }
}
