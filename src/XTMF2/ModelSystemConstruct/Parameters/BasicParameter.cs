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
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;

namespace XTMF2.ModelSystemConstruct.Parameters;

/// <summary>
/// Provides the backing for a simple parameter
/// </summary>
internal class BasicParameter : ParameterExpression
{
    protected const string ParameterProperty = "Parameter";

    /// <summary>
    /// The string presentation of the parameter
    /// </summary>
    private string _value;

    private readonly Type _type;

    /// <summary>
    /// Create a basic parameter
    /// </summary>
    /// <param name="value">The string value of the parameter.</param>
    public BasicParameter(string value, Type type)
    {
        _value = value;
        _type = type;
    }

    /// <inheritdoc/>
    public override string Representation
    {
        get => _value;
    }

    /// <inheritdoc/>
    public override bool IsCompatible(Type type, [NotNullWhen(false)] ref string? errorString)
    {
        return ArbitraryParameterParser.Check(type, _value, ref errorString);
    }

    public override object GetValue(IModule caller, Type type, ref string? errorString)
    {
        var (sucess, value) = ArbitraryParameterParser.ArbitraryParameterParse(type, _value, ref errorString);
        if (sucess)
        {
            errorString = null;
            return value!;
        }
        return false;
    }

    public override Type Type => _type;

    internal override void Save(Utf8JsonWriter writer)
    {
        writer.WriteString(ParameterProperty, Representation);
    }

    internal override bool AssignToParameter(IModule module, [NotNullWhen(false)] ref string? error)
    {
        var (success, value) = ArbitraryParameterParser.ArbitraryParameterParse(Type, _value, ref error);
        if (!success)
        {
            error = $"The parameter value {_value} is not compatible with the parameter type {Type}.";
            return false;
        }
        var fieldInfo = module.GetType().GetField("Value", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if(fieldInfo is null)
        {
            error = $"Unable to find a field named 'Value' in module {module.Name} to assign the parameter value to.";
            return false;
        }
        if (!fieldInfo.FieldType.IsAssignableFrom(value!.GetType()))
        {
            error = $"The parameter value {_value} is not compatible with the field type {fieldInfo.FieldType}.";
            return false;
        }
        fieldInfo.SetValue(module, value);
        return true;
    }
}
