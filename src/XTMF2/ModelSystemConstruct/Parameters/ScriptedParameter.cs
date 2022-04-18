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
using System.Text.Json;
using XTMF2.ModelSystemConstruct.Parameters.Compiler;

namespace XTMF2.ModelSystemConstruct.Parameters;

internal class ScriptedParameter : ParameterExpression
{
    protected const string ParameterExpressionProperty = "ParameterExpression";
    private Expression _expression;

    public ScriptedParameter(Expression expression)
    {
        _expression = expression;
    }

    public override string Representation
    {
        get => new (_expression.AsString());
    }

    public override object? GetValue(IModule caller, Type type, ref string? errorString)
    {
        if(_expression.Type != type)
        {
            errorString = ThrowInvalidTypes(type, _expression.Type);
            return null;
        }
        if(ParameterCompiler.Evaluate(caller, _expression, out var ret, ref errorString))
        {
            return ret;
        }
        return null;
    }

    public override bool IsCompatible(Type type, [NotNullWhen(false)] ref string? errorString)
    {
        return type.IsAssignableFrom(_expression.Type);
    }

    
    private static string ThrowInvalidTypes(Type expected, Type expressionType)
    {
        return $"Invalid types, expected {expected.FullName} however the expression returned {expressionType.FullName}!";
    }

    public override Type Type => _expression.Type;

    internal override void Save(Utf8JsonWriter writer)
    {
        writer.WriteString(ParameterExpressionProperty, Representation);
    }
}
