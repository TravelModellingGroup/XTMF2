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
/// Contains the result of the expression.
/// </summary>
public abstract class Result
{
    /// <summary>
    /// The type that would be returned.
    /// </summary>
    public abstract Type ReturnType { get; }

    /// <summary>
    /// Gets the result of the computation.
    /// </summary>
    /// <param name="result">The result contained unless there was an error.</param>
    /// <param name="error">An error message if there was an error, null otherwise.</param>
    /// <returns>True if the expression succeeds, false otherwise with error message.</returns>
    public abstract bool TryGetResult([NotNullWhen(true)] out object? result, [NotNullWhen(false)] ref string? error);
}
