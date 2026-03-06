/*
    Copyright 2026 University of Toronto

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
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;

namespace XTMF2.GUI.Controls;

/// <summary>
/// Converts a <see cref="Type"/> to a human-readable name string.
/// Non-generic types return <c>Type.Name</c>; constructed generics return
/// <c>ClassName&lt;Arg1, Arg2&gt;</c> (e.g. <c>BasicParameter&lt;Boolean&gt;</c>).
/// </summary>
public sealed class FriendlyTypeNameConverter : IValueConverter
{
    /// <summary>Singleton instance for use in AXAML.</summary>
    public static readonly FriendlyTypeNameConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Type t ? GetFriendlyName(t) : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    /// <summary>
    /// Returns a short, readable name for <paramref name="t"/>.
    /// Generic types are formatted as <c>BaseName&lt;T1, T2&gt;</c>.
    /// </summary>
    public static string GetFriendlyName(Type t)
    {
        if (t is null) return string.Empty;
        if (!t.IsGenericType) return t.Name;

        var baseName = t.GetGenericTypeDefinition().Name;
        // Strip the CLR arity suffix (e.g. "BasicParameter`1" → "BasicParameter").
        var backtick = baseName.IndexOf('`');
        if (backtick >= 0) baseName = baseName[..backtick];

        var args = string.Join(", ", t.GetGenericArguments().Select(GetFriendlyName));
        return $"{baseName}<{args}>";
    }
}
