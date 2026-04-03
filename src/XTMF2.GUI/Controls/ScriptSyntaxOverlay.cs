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
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace XTMF2.GUI.Controls;

/// <summary>
/// A transparent, non-interactive overlay control that paints syntax-highlighted
/// tokens over the scripted-parameter <see cref="Avalonia.Controls.TextBox"/>. It is added to
/// <see cref="Control.VisualChildren"/> after the TextBox so it renders on top of it.
/// </summary>
internal sealed class ScriptSyntaxOverlay : Control
{
    private static readonly Typeface OverlayTypeface = new("Segoe UI, Arial, sans-serif");

    public (string text, IBrush brush)[] Tokens { get; set; } = Array.Empty<(string, IBrush)>();

    /// <summary>
    /// Horizontal pixel offset from the TextBox's internal ScrollViewer, used to
    /// align the rendered text with what the TextBox actually shows. Set by
    /// <see cref="ModelSystemCanvas"/> whenever the editor text changes.
    /// </summary>
    public double HorizontalScrollOffset { get; set; }

    /// <summary>
    /// Scaled font size to use when drawing; updated by <see cref="ModelSystemCanvas.ArrangeOverride"/>
    /// on every layout pass so the text matches the current zoom level.
    /// </summary>
    public double FontSize { get; set; } = 10.0;

    public ScriptSyntaxOverlay()
    {
        IsHitTestVisible = false;
        IsVisible        = false;
    }

    public override void Render(DrawingContext ctx)
    {
        if (Tokens.Length == 0) return;

        // 4 px left-pad matches the TextBox inner padding so the first character
        // lines up with the native cursor position.
        const double leftPad = 4.0;

        // Build the full expression string from all token segments so that
        // Avalonia measures it in one pass.  Per-token width accumulation is
        // unreliable: FormattedText.Width excludes trailing whitespace, so any
        // whitespace-only token advances x by 0 and every subsequent token is
        // shifted left.  Drawing as a single FormattedText with SetForegroundBrush
        // span colouring avoids the issue entirely.
        var sb = new StringBuilder();
        foreach (var (seg, _) in Tokens)
            sb.Append(seg);
        string fullText = sb.ToString();

        var ft = new FormattedText(
            fullText,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            OverlayTypeface,
            FontSize,
            Brushes.White); // default; overridden per span below

        int offset = 0;
        foreach (var (seg, brush) in Tokens)
        {
            ft.SetForegroundBrush(brush, offset, seg.Length);
            offset += seg.Length;
        }

        double ty = (Bounds.Height - ft.Height) / 2.0;

        // Clip to the overlay bounds so text never bleeds past the TextBox edge,
        // then translate left by the TextBox's horizontal scroll offset so the
        // visible window of characters matches what the TextBox itself shows.
        using var _clip = ctx.PushClip(new Rect(0, 0, Bounds.Width, Bounds.Height));
        ctx.DrawText(ft, new Point(leftPad - HorizontalScrollOffset, ty));
    }
}
