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
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using XTMF2.ModelSystemConstruct.Parameters;

namespace XTMF2.ModelSystemConstruct;

/// <summary>
/// This class provides the interface for a Node to contain a value.
/// </summary>
public abstract class ParameterExpression : INotifyPropertyChanged
{
    /// <summary>
    /// Checks to see if this parameter is capable of being converted into the given type.
    /// </summary>
    /// <param name="type">The type to be converted to.</param>
    /// <param name="errorString">The error message of why the operation failed, null if it succeeds.</param>
    /// <returns>True if the type can be converted, false with error message if not.</returns>
    internal abstract bool IsCompatible(Type type, [NotNullWhen(false)] ref string? errorString);

    /// <summary>
    /// Tries to convert the parameter expression to the given type.
    /// </summary>
    /// <param name="type">The type to try to extract.</param>
    /// <param name="errorString">An error message if the extraction fails.</param>
    /// <returns>An object of the given type, or null with an error message if it fails.</returns>
    internal abstract object? GetValue(Type type, ref string? errorString);

    /// <summary>
    /// Gets a string based representation of the parameter
    /// </summary>
    public abstract string Representation { get; }

    /// <summary>
    /// Creates a parameter from a string value
    /// </summary>
    /// <param name="value">The string value of the parameter.</param>
    /// <returns>A new parameter using the value.</returns>
    internal static ParameterExpression CreateParameter(string value)
    {
        return new BasicParameter(value);
    }
    
    /// <summary>
    /// Create a new parameter using the expression.
    /// </summary>
    /// <param name="expression"></param>
    /// <returns>A new parameter using the expression.</returns>
    internal static ParameterExpression CreateParameter(Expression expression)
    {
        return new ScriptedParameter(expression);
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Update any GUI following this parameter letting it know the value changed.
    /// </summary>
    protected void InvokeRepresentationChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Representation)));
    }
}
