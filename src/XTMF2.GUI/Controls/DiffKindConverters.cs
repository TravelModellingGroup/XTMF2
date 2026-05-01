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
using Avalonia.Data.Converters;
using Avalonia.Media;
using XTMF2.Diff;

namespace XTMF2.GUI.Controls;

/// <summary>
/// Converts an <see cref="ElementDiffKind"/> value to an <see cref="IBrush"/> suitable
/// for colour-coding diff rows.
/// </summary>
public sealed class DiffKindToBrushConverter : IValueConverter
{
    /// <summary>Singleton instance for use in AXAML.</summary>
    public static readonly DiffKindToBrushConverter Instance = new();

    // Colour palette: muted neon tones matching the app's dark theme
    private static readonly IBrush AddedBrush    = new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0xCC, 0x66)); // dim green
    private static readonly IBrush RemovedBrush  = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0x33, 0x55)); // dim red
    private static readonly IBrush ModifiedBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xAA, 0x00)); // dim amber
    private static readonly IBrush UnchangedBrush = Brushes.Transparent;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is ElementDiffKind kind ? KindToBrush(kind) : UnchangedBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    /// <summary>Returns the background brush for a given <see cref="ElementDiffKind"/>.</summary>
    public static IBrush KindToBrush(ElementDiffKind kind) => kind switch
    {
        ElementDiffKind.Added    => AddedBrush,
        ElementDiffKind.Removed  => RemovedBrush,
        ElementDiffKind.Modified => ModifiedBrush,
        _                        => UnchangedBrush,
    };
}

/// <summary>
/// Converts an <see cref="ElementDiffKind"/> value to a short icon character
/// suitable for prefixing diff row labels.
/// </summary>
public sealed class DiffKindToIconConverter : IValueConverter
{
    /// <summary>Singleton instance for use in AXAML.</summary>
    public static readonly DiffKindToIconConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is ElementDiffKind kind ? KindToIcon(kind) : string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    /// <summary>Returns the icon character for a given <see cref="ElementDiffKind"/>.</summary>
    public static string KindToIcon(ElementDiffKind kind) => kind switch
    {
        ElementDiffKind.Added    => "+",
        ElementDiffKind.Removed  => "−",
        ElementDiffKind.Modified => "~",
        _                        => " ",
    };
}

/// <summary>
/// Converts an <see cref="ElementDiffKind"/> value to the foreground colour used for
/// the diff icon/label.
/// </summary>
public sealed class DiffKindToForegroundConverter : IValueConverter
{
    /// <summary>Singleton instance for use in AXAML.</summary>
    public static readonly DiffKindToForegroundConverter Instance = new();

    private static readonly IBrush AddedFg    = new SolidColorBrush(Color.FromArgb(0xFF, 0x44, 0xEE, 0x88));
    private static readonly IBrush RemovedFg  = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x55, 0x66));
    private static readonly IBrush ModifiedFg = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xBB, 0x33));
    private static readonly IBrush UnchangedFg = new SolidColorBrush(Color.FromArgb(0x88, 0xCC, 0xCC, 0xCC));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is ElementDiffKind kind ? KindToForeground(kind) : UnchangedFg;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    /// <summary>Returns the foreground brush for a given <see cref="ElementDiffKind"/>.</summary>
    public static IBrush KindToForeground(ElementDiffKind kind) => kind switch
    {
        ElementDiffKind.Added    => AddedFg,
        ElementDiffKind.Removed  => RemovedFg,
        ElementDiffKind.Modified => ModifiedFg,
        _                        => UnchangedFg,
    };
}

/// <summary>
/// Returns <c>true</c> when the bound value is not null; <c>false</c> when null.
/// Used to conditionally show elements that depend on a nullable property.
/// </summary>
public sealed class NotNullConverter : IValueConverter
{
    /// <summary>Singleton instance for use in AXAML.</summary>
    public static readonly NotNullConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
