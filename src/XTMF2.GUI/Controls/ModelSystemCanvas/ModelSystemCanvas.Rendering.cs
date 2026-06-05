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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Styling;
using XTMF2.GUI.ViewModels;
using XTMF2.ModelSystemConstruct;

namespace XTMF2.GUI.Controls;

partial class ModelSystemCanvas
{
    // ── Rendering ─────────────────────────────────────────────────────────
    public override void Render(DrawingContext ctx)
    {
        BuildHookAnchorCache();
        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        bool isLight = Application.Current?.ActualThemeVariant == ThemeVariant.Light;
        _isLight = isLight;
        if (isLight != _zoomBarIsLight)
        {
            bool capture = isLight;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => UpdateZoomBarColors(capture));
        }
        ctx.DrawRectangle(isLight ? CanvasBackgroundLight : CanvasBackground, null, bounds);
        DrawGridBackground(ctx, bounds, isLight);

        if (_vm is null) return;

        using (ctx.PushTransform(Matrix.CreateScale(_scale, _scale)))
        {
            RenderCommentBlocks(ctx);
            RenderFunctionTemplates(ctx);
            RenderFunctionInstances(ctx);
            RenderFunctionParameters(ctx);
            RenderLinks(ctx);
            RenderNodes(ctx);
            RenderGhostNodes(ctx);
            RenderStarts(ctx);
            RenderPendingLink(ctx);
            RenderSelectionRect(ctx);
        }
    }

    /// <summary>
    /// Draws a graph-paper grid over the canvas background. Grid lines are spaced 1 cm apart in
    /// logical pixels and shift with the <see cref="ScrollViewer"/> offset so they remain anchored
    /// to model space as the user pans.
    /// </summary>
    private void DrawGridBackground(DrawingContext ctx, Rect bounds, bool isLight = false)
    {
        var sv = GetScrollViewer();
        double scrollX = sv?.Offset.X ?? 0;
        double scrollY = sv?.Offset.Y ?? 0;

        // Scale the 1 cm spacing by the current zoom level so lines stay 1 cm apart in model space.
        double step = GridSpacingDip * _scale;

        // Grid lines live at canvas positions that are exact multiples of step (anchored to model
        // origin). No phase offset from scrollX/Y is needed — the ScrollViewer already translates
        // the whole canvas, so adding a scroll-derived phase would make lines move at 2× speed.
        // Only draw lines visible in the current viewport for performance.
        double vpWidth  = sv?.Viewport.Width  ?? bounds.Width;
        double vpHeight = sv?.Viewport.Height ?? bounds.Height;
        double startX = Math.Floor(scrollX / step) * step;
        double startY = Math.Floor(scrollY / step) * step;
        double endX = Math.Min(scrollX + vpWidth,  bounds.Width);
        double endY = Math.Min(scrollY + vpHeight, bounds.Height);

        var pen = isLight ? GridPenLight : GridPen;

        // Vertical lines
        for (double x = startX; x <= endX; x += step)
        {
            ctx.DrawLine(pen, new Point(x, scrollY), new Point(x, endY));
        }
        // Horizontal lines
        for (double y = startY; y <= endY; y += step)
        {
            ctx.DrawLine(pen, new Point(scrollX, y), new Point(endX, y));
        }
    }

    private void RenderCommentBlocks(DrawingContext ctx)
    {
        foreach (var comment in _vm!.CommentBlocks)
        {
            double x = comment.X;
            double y = comment.Y;
            double w = comment.Width;
            double h = comment.Height;
            bool sel = comment.IsSelected;
            double fold = CommentFoldSize;

            var fill = sel ? CommentSelFill : CommentFill;
            var borderPen = sel ? CommentSelBorderPen : CommentBorderPen;
            var foldPen   = sel ? CommentSelFoldPen   : CommentFoldPen;

            // ── 1. Drop shadow ────────────────────────────────────────────
            // Build a shadow polygon offset by (4, 5) to the bottom-right.
            {
                const double sx = 4, sy = 5;
                var shadowGeo = new StreamGeometry();
                using (var gc = shadowGeo.Open())
                {
                    gc.BeginFigure(new Point(x + sx, y + sy), isFilled: true);
                    gc.LineTo(new Point(x + w - fold + sx, y + sy));
                    gc.LineTo(new Point(x + w + sx, y + fold + sy));
                    gc.LineTo(new Point(x + w + sx, y + h + sy));
                    gc.LineTo(new Point(x + sx, y + h + sy));
                    gc.EndFigure(true);
                }
                ctx.DrawGeometry(CommentShadowBrush, null, shadowGeo);
            }

            // ── 2. Glow (selection / ambient) ─────────────────────────────
            DrawRectGlow(ctx, new Rect(x, y, w, h), NodeCornerRadius,
                         sel ? SelectionGlowBrushes : CommentGlowBrushes);

            // ── 3. Main note body (dog-ear polygon) ───────────────────────
            var bodyGeo = new StreamGeometry();
            using (var gc = bodyGeo.Open())
            {
                gc.BeginFigure(new Point(x, y), isFilled: true);
                gc.LineTo(new Point(x + w - fold, y));   // top edge  → fold start
                gc.LineTo(new Point(x + w, y + fold)); // fold crease end
                gc.LineTo(new Point(x + w, y + h));   // right edge
                gc.LineTo(new Point(x, y + h));   // bottom edge
                gc.EndFigure(true);
            }
            ctx.DrawGeometry(fill, borderPen, bodyGeo);

            // ── 4. Adhesive-tab header band ───────────────────────────────
            // Clipped to the body polygon so it doesn't bleed into the fold corner.
            {
                using var _ = ctx.PushGeometryClip(bodyGeo);
                // Header Rectangle: full width but tab stops short of the fold on top row.
                ctx.DrawRectangle(CommentHeaderBrush, null,
                    new Rect(x, y, w, CommentHeaderHeight));
                // Render header text in bold if present.
                string headerText = comment.Header;
                if (!string.IsNullOrEmpty(headerText))
                {
                    var headerTextArea = new Rect(
                        x + CommentPadding,
                        y + 2,
                        w - CommentPadding * 2 - fold,
                        CommentHeaderHeight - 4);
                    if (headerTextArea.Width > 4)
                    {
                        using var clipPush = ctx.PushClip(headerTextArea);
                        var headerLayout = new TextLayout(
                            headerText,
                            CommentHeaderTypeface,
                            CommentHeaderFontSize,
                            CommentTextBrush,
                            textAlignment: TextAlignment.Left,
                            textWrapping: TextWrapping.NoWrap,
                            maxWidth: headerTextArea.Width,
                            maxHeight: headerTextArea.Height);
                        headerLayout.Draw(ctx, new Point(headerTextArea.X, headerTextArea.Y));
                    }
                }
            }

            // ── 5. Faint ruled lines ──────────────────────────────────────
            {
                double ruleLeft = x + CommentPadding;
                double ruleRight = x + w - CommentPadding;
                double ruleStart = y + CommentHeaderHeight + CommentRuleSpacing;
                using var _ = ctx.PushGeometryClip(bodyGeo);
                for (double ry = ruleStart; ry < y + h - CommentPadding; ry += CommentRuleSpacing)
                    ctx.DrawLine(CommentRulePen,
                                 new Point(ruleLeft, ry),
                                 new Point(ruleRight, ry));
            }

            // ── 6. Fold-flap triangle (back of the turned corner) ─────────
            // Triangle: the three points of the folded-over corner area.
            var foldGeo = new StreamGeometry();
            using (var gc = foldGeo.Open())
            {
                gc.BeginFigure(new Point(x + w - fold, y), isFilled: true);
                gc.LineTo(new Point(x + w, y + fold));
                gc.LineTo(new Point(x + w - fold, y + fold));
                gc.EndFigure(true);
            }
            ctx.DrawGeometry(CommentFoldBackBrush, foldPen, foldGeo);

            // ── 7. Fold crease line ────────────────────────────────────────
            ctx.DrawLine(borderPen,
                         new Point(x + w - fold, y),
                         new Point(x + w, y + fold));

            // ── 8. Comment text (with scroll indicator) ───────────────────
            var textArea = new Rect(
                x + CommentPadding,
                y + CommentHeaderHeight + 2,
                w - CommentPadding * 2,
                h - CommentHeaderHeight - CommentPadding - 2);
            if (textArea.Width > 4 && textArea.Height > 4)
            {
                // Reserve space on the right for the scroll indicator.
                double textW = textArea.Width - CommentScrollBarWidth - 2;
                // Measure the full (unconstrained) text height to compute max scroll.
                var measureLayout = new TextLayout(
                    comment.Name,
                    DefaultTypeface,
                    CommentFontSize,
                    CommentTextBrush,
                    textAlignment: TextAlignment.Left,
                    textWrapping: TextWrapping.Wrap,
                    maxWidth: textW,
                    maxHeight: double.MaxValue);
                double totalTextHeight = measureLayout.Height;
                double maxScroll = Math.Max(0.0, totalTextHeight - textArea.Height);
                _commentMaxScrollOffsets[comment] = maxScroll;

                double scrollOffset = Math.Min(comment.CommentScrollOffset, maxScroll);

                using var clipPush = ctx.PushClip(textArea);
                var layout = new TextLayout(
                    comment.Name,
                    DefaultTypeface,
                    CommentFontSize,
                    CommentTextBrush,
                    textAlignment: TextAlignment.Left,
                    textWrapping: TextWrapping.Wrap,
                    maxWidth: textW,
                    maxHeight: textArea.Height + scrollOffset);
                layout.Draw(ctx, new Point(textArea.X, textArea.Y - scrollOffset));

                // Draw scroll indicator when content overflows.
                if (totalTextHeight > textArea.Height)
                {
                    double trackX = textArea.Right - CommentScrollBarWidth;
                    double trackH = textArea.Height;
                    ctx.DrawRectangle(CommentScrollTrackBrush, null,
                        new Rect(trackX, textArea.Y, CommentScrollBarWidth, trackH));
                    double thumbH = Math.Max(8.0, (textArea.Height / totalTextHeight) * trackH);
                    double thumbY = maxScroll > 0
                        ? textArea.Y + (scrollOffset / maxScroll) * (trackH - thumbH)
                        : textArea.Y;
                    ctx.DrawRectangle(CommentScrollThumbBrush, null,
                        new Rect(trackX, thumbY, CommentScrollBarWidth, thumbH), 2, 2);
                }
            }

            // ── 9. Resize grip dots (bottom-right) ────────────────────────
            {
                double dotR = 2.0;
                double bx = x + w;
                double by = y + h;
                for (int d = 0; d < 3; d++)
                {
                    double offset = 4.0 + d * 4.0;
                    ctx.DrawEllipse(_isLight ? ResizeHandleBrushL : ResizeHandleBrush, null,
                        new Point(bx - offset + dotR, by - dotR), dotR, dotR);
                    ctx.DrawEllipse(_isLight ? ResizeHandleBrushL : ResizeHandleBrush, null,
                        new Point(bx - dotR, by - offset + dotR), dotR, dotR);
                }
            }
        }
    }

    /// <summary>
    /// Draws function-template container boxes on the canvas. Each box shows the
    /// template name in a violet header, exposed-node hook rows below, and a
    /// module-count hint in the body. Double-clicking navigates into the template's
    /// InternalModules.
    /// </summary>
    private void RenderFunctionTemplates(DrawingContext ctx)
    {
        foreach (var ft in _vm!.FunctionTemplates)
        {
            double rw = ft.Width;
            double rh = ft.Height;
            var rect = new Rect(ft.X, ft.Y, rw, rh);

            // Neon glow + outer border (dashed to distinguish from a regular node or boundary)
            var border = ft.IsSelected ? FtSelBorderPen : (_isLight ? FtBorderPenL : FtBorderPen);
            DrawRectGlow(ctx, rect, FtCornerRadius, ft.IsSelected ? SelectionGlowBrushes : (_isLight ? FtGlowBrushesL : FtGlowBrushes));
            ctx.DrawRectangle(_isLight ? FtFillL : FtFill, border, rect, FtCornerRadius, FtCornerRadius);

            // ── Header band ────────────────────────────────────────────────
            // Draw header as a filled rectangle, then overdraw the bottom strip
            // with the body fill so we effectively clip the rounded bottom corners.
            ctx.DrawRectangle(_isLight ? FtHeaderFillL : FtHeaderFill, null, rect, FtCornerRadius, FtCornerRadius);
            ctx.DrawRectangle(_isLight ? FtFillL : FtFill, null,
                new Rect(ft.X, ft.Y + FtHeaderHeight, rw, rh - FtHeaderHeight));

            // Re-draw the border on top so the header fill doesn't erase it.
            ctx.DrawRectangle(null, border, rect, FtCornerRadius, FtCornerRadius);

            // "⊞ TemplateName" label in the header
            var labelText = "\u229e " + ft.Name;
            var labelFt = MakeText(labelText, FtNameFontSize, _isLight ? FtTextBrushL : FtTextBrush);
            var lx = ft.X + 8.0;
            var ly = ft.Y + (FtHeaderHeight - labelFt.Height) / 2.0;
            using (ctx.PushClip(new Rect(ft.X + 4, ft.Y, rw - 8, FtHeaderHeight)))
            {
                ctx.DrawText(labelFt, new Point(lx, ly));
            }

            // ── FunctionParameter hook rows ────────────────────────────────
            double rowY = ft.Y + FtHeaderHeight;
            foreach (var fp in ft.FunctionParameters)
            {
                ctx.DrawRectangle(InlineParamRowBg, null,
                    new Rect(ft.X, rowY, rw, FtHookRowHeight));
                ctx.DrawLine(_isLight ? HookDividerPenThinL : HookDividerPenThin,
                    new Point(ft.X, rowY),
                    new Point(ft.X + rw, rowY));

                // Dot on the left edge (acts like a hook anchor) — orange to signal FunctionParameter
                double dotCx = ft.X + HookDotRadius + 4.0;
                double dotCy = rowY + FtHookRowHeight / 2.0;
                ctx.DrawEllipse(Brushes.OrangeRed, null,
                    new Point(dotCx, dotCy), HookDotRadius, HookDotRadius);

                // "Parameter: <name>"
                var nodeNameFt = MakeText(
                    "Parameter: " + (fp.Name ?? string.Empty), HookFontSize, _isLight ? FtHookTextBrushL : FtHookTextBrush);
                double textLeft = ft.X + HookDotRadius * 2 + 9.0;
                using (ctx.PushClip(new Rect(textLeft, rowY, rw - textLeft + ft.X, FtHookRowHeight)))
                    ctx.DrawText(nodeNameFt,
                        new Point(textLeft, rowY + (FtHookRowHeight - nodeNameFt.Height) / 2.0));

                rowY += FtHookRowHeight;
            }

            // ── Body hint: module count ────────────────────────────────────
            double bodyH = ft.Y + rh - rowY;
            var moduleCount = ft.UnderlyingTemplate.InternalModules.Modules.Count;
            var hint = moduleCount == 0
                ? "Empty — double-click to edit"
                : $"{moduleCount} module(s) — double-click to edit";
            var hintFt = MakeText(hint, HookFontSize, _isLight ? FtCountTextBrushL : FtCountTextBrush);
            if (bodyH > FtHookRowHeight)
            {
                var hintX = ft.X + (rw - hintFt.Width) / 2.0;
                var hintY = rowY + (bodyH - hintFt.Height) / 2.0;
                using (ctx.PushClip(new Rect(ft.X + 4, rowY, rw - 8, bodyH)))
                    ctx.DrawText(hintFt, new Point(hintX, hintY));
            }

            // ── Resize grip (bottom-right corner) ─────────────────────────
            {
                double dotR = 2.0;
                double gx = ft.X + rw;
                double gy = ft.Y + rh;
                for (int d = 0; d < 3; d++)
                {
                    double off = 4.0 + d * 4.0;
                    ctx.DrawEllipse(_isLight ? ResizeHandleBrushL : ResizeHandleBrush, null,
                        new Point(gx - off + dotR, gy - dotR), dotR, dotR);
                    ctx.DrawEllipse(_isLight ? ResizeHandleBrushL : ResizeHandleBrush, null,
                        new Point(gx - dotR, gy - off + dotR), dotR, dotR);
                }
            }
        }
    }

    /// <summary>
    /// Draws function-instance boxes. Each box is styled in teal, shows the instance name
    /// in the header and the template name as a subtitle, then lists exposed hooks below.
    /// Hook rows use the same layout and type-glyph logic as regular Node hook rows.
    /// </summary>
    private void RenderFunctionInstances(DrawingContext ctx)
    {
        foreach (var fi in _vm!.FunctionInstances)
        {
            double rw = fi.Width;
            double rh = fi.Height;
            var rect = new Rect(fi.X, fi.Y, rw, rh);
            bool isDisabled = fi.UnderlyingInstance.IsDisabled;

            // ── Border / fill — original teal FI palette ────────────────
            var border = fi.IsSelected
                ? (_isLight ? FiSelBorderPenL : FiSelBorderPen)
                : isDisabled
                    ? (_isLight ? DisabledFiBorderPenL : DisabledFiBorderPen)
                    : (_isLight ? FiBorderPenL : FiBorderPen);
            DrawRectGlow(ctx, rect, FiCornerRadius,
                fi.IsSelected
                    ? SelectionGlowBrushes
                    : isDisabled
                        ? (_isLight ? DisabledGlowBrushesL : DisabledGlowBrushes)
                        : (_isLight ? FiGlowBrushesL : FiGlowBrushes));
            ctx.DrawRectangle(isDisabled ? (_isLight ? DisabledFiFillL : DisabledFiFill) : (_isLight ? FiFillL : FiFill), border, rect, FiCornerRadius, FiCornerRadius);

            // ── Header band ────────────────────────────────────────────────
            ctx.DrawRectangle(isDisabled ? (_isLight ? DisabledFiHeaderFillL : DisabledFiHeaderFill) : (_isLight ? FiHeaderFillL : FiHeaderFill), null, rect, FiCornerRadius, FiCornerRadius);
            ctx.DrawRectangle(isDisabled ? (_isLight ? DisabledFiFillL : DisabledFiFill) : (_isLight ? FiFillL : FiFill), null,
                new Rect(fi.X, fi.Y + FtHeaderHeight, rw, rh - FtHeaderHeight));
            ctx.DrawRectangle(null, border, rect, FiCornerRadius, FiCornerRadius);

            // "⊡ InstanceName" in header
            var labelText = "\u22A1 " + fi.Name;
            var labelFtText = MakeText(labelText, FtNameFontSize,
                isDisabled ? (_isLight ? DisabledFiTextBrushL : DisabledFiTextBrush) : (_isLight ? FiTextBrushL : FiTextBrush));
            double lx = fi.X + 8.0;
            double ly = fi.Y + (FtHeaderHeight - labelFtText.Height) / 2.0;
            using (ctx.PushClip(new Rect(fi.X + 4, fi.Y, rw - 8, FtHeaderHeight)))
                ctx.DrawText(labelFtText, new Point(lx, ly));

            // Template subtitle (small, muted) at the bottom of the header
            var subText = MakeText(
                "[" + fi.TemplateName + "]" + (fi.EntryNodeTypeName.Length > 0 ? " : " + fi.EntryNodeTypeName : ""),
                HookFontSize,
                isDisabled ? (_isLight ? DisabledSubTextBrushL : DisabledSubTextBrush) : (_isLight ? FiSubTextBrushL : FiSubTextBrush));
            double subX = fi.X + rw - subText.Width - 8.0;
            double subY = fi.Y + (FtHeaderHeight - subText.Height) / 2.0;
            using (ctx.PushClip(new Rect(fi.X + 4, fi.Y, rw - 8, FtHeaderHeight)))
                ctx.DrawText(subText, new Point(Math.Max(lx + labelFtText.Width + 4, subX), subY));

            // ── Resize grip (bottom-right corner) ─────────────────────────
            {
                double dotR = 2.0;
                double gx = fi.X + rw;
                double gy = fi.Y + rh;
                for (int d = 0; d < 3; d++)
                {
                    double off = 4.0 + d * 4.0;
                    ctx.DrawEllipse(isDisabled ? (_isLight ? DisabledHandleBrushL : DisabledHandleBrush) : (_isLight ? ResizeHandleBrushL : ResizeHandleBrush), null,
                        new Point(gx - off + dotR, gy - dotR), dotR, dotR);
                    ctx.DrawEllipse(isDisabled ? (_isLight ? DisabledHandleBrushL : DisabledHandleBrush) : (_isLight ? ResizeHandleBrushL : ResizeHandleBrush), null,
                        new Point(gx - dotR, gy - off + dotR), dotR, dotR);
                }
            }

            if (fi.FunctionParameters.Count == 0)
                continue;

            // ── FunctionParameter hook rows — same layout logic as Node hook rows ───
            ctx.DrawLine(isDisabled ? (_isLight ? DisabledDividerPenL : DisabledDividerPen) : (_isLight ? HookDividerPenL : HookDividerPen),
                new Point(fi.X + 1, fi.Y + FtHeaderHeight),
                new Point(fi.X + rw - 1, fi.Y + FtHeaderHeight));

            double rowY = fi.Y + FtHeaderHeight;
            var fiHooks = fi.UnderlyingInstance.Hooks;
            _fiConnectedHooks.TryGetValue(fi, out var fiConnected);
            for (int fi_i = 0; fi_i < fi.FunctionParameters.Count; fi_i++)
            {
                var fp = fi.FunctionParameters[fi_i];
                var fpHook = fi_i < fiHooks.Count ? fiHooks[fi_i] as FunctionParameterHook : null;
                bool fpConn = fiConnected is not null && fpHook is not null && fiConnected.Contains(fpHook);
                NodeViewModel? inlinedFiParam = null;
                bool hasInlinedFi = fpHook is not null
                    && _fiHookInlinedParam.TryGetValue((fi, fpHook), out inlinedFiParam);
                bool fpUnsatisfied = !fpConn && !hasInlinedFi;

                double rowTopY = rowY;
                double dotCy   = rowY + HookRowHeight / 2.0;

                // Row tint: amber for inlined param, red wash for unsatisfied (all FP hooks required).
                if (!isDisabled && hasInlinedFi)
                    ctx.DrawRectangle(InlineParamRowBg, null,
                        new Rect(fi.X + 1, rowTopY, rw - 2, HookRowHeight));
                else if (!isDisabled && fpUnsatisfied)
                    ctx.DrawRectangle(HookUnsatisfiedRowBg, null,
                        new Rect(fi.X + 1, rowTopY, rw - 2, HookRowHeight));

                ctx.DrawLine(isDisabled ? (_isLight ? DisabledDividerPenThinL : DisabledDividerPenThin) : (_isLight ? HookDividerPenThinL : HookDividerPenThin),
                    new Point(fi.X, rowY), new Point(fi.X + rw, rowY));

                // Dot on the RIGHT edge — green if satisfied, red if unsatisfied.
                var dotBrush = isDisabled
                    ? (_isLight ? DisabledDotBrushL : DisabledDotBrush)
                    : fpUnsatisfied
                    ? (_isLight ? HookUnsatisfiedBrushL : HookUnsatisfiedBrush)
                    : (_isLight ? HookConnectedBrushL   : HookConnectedBrush);
                ctx.DrawEllipse(dotBrush, null,
                    new Point(fi.X + rw, dotCy), HookDotRadius, HookDotRadius);

                // When at least one link from this FI hook departs leftward, also draw a dot
                // on the left edge so the visual anchor matches the link exit point.
                if (fpHook is not null && _leftGoingFiHooks.Contains((fi, fpHook)))
                {
                    ctx.DrawEllipse(dotBrush, null,
                        new Point(fi.X, dotCy), HookDotRadius, HookDotRadius);
                }

                // Label: prefix with ≡ (BasicParameter) or ƒ (ScriptedParameter) when inlined.
                const double textPad = 6.0;
                string hookLabel = hasInlinedFi && inlinedFiParam is not null
                    ? $"{(inlinedFiParam.IsBasicParameter ? "\u2261" : "\u0192")} {fp.Name}: {(string.IsNullOrEmpty(inlinedFiParam.ParameterValueRepresentation) ? "(no value)" : inlinedFiParam.ParameterValueRepresentation)}"
                    : fp.Name ?? string.Empty;
                IBrush hookTextBrush = isDisabled
                    ? (_isLight ? DisabledHookTextBrushL : DisabledHookTextBrush)
                    : fpUnsatisfied
                    ? (_isLight ? HookTextUnsatisfiedBrushL : HookTextUnsatisfiedBrush)
                    : fpConn
                        ? (_isLight ? HookTextConnBrushL : HookTextConnBrush)
                    : hasInlinedFi && inlinedFiParam is { IsScriptedParameter: true }
                        ? (_isLight ? ScriptParamAccentBrushL : ScriptParamAccentBrush)
                    : (_isLight ? ParamValueTextBrushL : ParamValueTextBrush);
                var hookNameFt = MakeText(hookLabel, HookFontSize, hookTextBrush);
                double maxW  = rw - textPad * 2 - HookDotRadius * 2;
                double hookTy = dotCy - hookNameFt.Height / 2.0;
                using (ctx.PushClip(new Rect(fi.X + textPad, hookTy, Math.Max(0, maxW), hookNameFt.Height + 1)))
                    ctx.DrawText(hookNameFt, new Point(fi.X + textPad, hookTy));

                rowY += HookRowHeight;
            }
        }
    }

    private void RenderFunctionParameters(DrawingContext ctx)
    {
        foreach (var fp in _vm!.FunctionParameterVMs)
        {
            double rw = fp.Width;
            double rh = fp.Height;
            var rect = new Rect(fp.X, fp.Y, rw, rh);

            // Amber/orange fill — switches between dark and light palettes.
            IBrush bodyFill, headerFill, textBrush;
            Pen border;
            IBrush[] fpGlowBrushes;
            if (_isLight)
            {
                bodyFill      = fp.IsSelected ? FpBodyFillSel : FpFillL;
                headerFill    = FpHeaderFillL;
                border        = fp.IsSelected ? FpSelBorderPenL : FpBorderPenL;
                textBrush     = FpTextBrushL;
                fpGlowBrushes = fp.IsSelected ? SelectionGlowBrushes : FpGlowBrushesL;
            }
            else
            {
                bodyFill      = FpBodyFill;
                headerFill    = FpHeaderFill;
                border        = fp.IsSelected ? FpSelBorderPen : FpBorderPen;
                textBrush     = Brushes.White;
                fpGlowBrushes = fp.IsSelected ? SelectionGlowBrushes : FpGlowBrushes;
            }

            DrawRectGlow(ctx, rect, FiCornerRadius, fpGlowBrushes);

            // Header band: draw header fill over entire rect (preserving rounded corners),
            // then overdraw the body area below the header — same pattern as FunctionTemplates.
            ctx.DrawRectangle(headerFill, border, rect, FiCornerRadius, FiCornerRadius);
            ctx.DrawRectangle(bodyFill, null,
                new Rect(fp.X, fp.Y + FtHeaderHeight, rw, rh - FtHeaderHeight));
            ctx.DrawRectangle(null, border, rect, FiCornerRadius, FiCornerRadius);

            // Name label in header.
            var labelFtText = MakeText(fp.Name, FtNameFontSize, textBrush);
            var lx = fp.X + 8.0;
            var ly = fp.Y + (FtHeaderHeight - labelFtText.Height) / 2.0;
            using (ctx.PushClip(new Rect(fp.X + 4, fp.Y, rw - 8, FtHeaderHeight)))
                ctx.DrawText(labelFtText, new Point(lx, ly));

            // Type name in smaller text below header.
            if (!string.IsNullOrEmpty(fp.TypeName))
            {
                var typeText = MakeText(fp.TypeName, HookFontSize, textBrush);
                using (ctx.PushClip(new Rect(fp.X + 4, fp.Y + FtHeaderHeight, rw - 8, rh - FtHeaderHeight)))
                    ctx.DrawText(typeText, new Point(lx, fp.Y + FtHeaderHeight + 4.0));
            }

            // Resize grip (same dot pattern as other elements).
            {
                double dotR = 2.0;
                double gx = fp.X + rw;
                double gy = fp.Y + rh;
                for (int d = 0; d < 3; d++)
                {
                    double off = 4.0 + d * 4.0;
                    ctx.DrawEllipse(_isLight ? ResizeHandleBrushL : ResizeHandleBrush, null,
                        new Point(gx - off + dotR, gy - dotR), dotR, dotR);
                    ctx.DrawEllipse(_isLight ? ResizeHandleBrushL : ResizeHandleBrush, null,
                        new Point(gx - dotR, gy - off + dotR), dotR, dotR);
                }
            }
        }
    }

    /// <summary>
    /// Spine-X cache: for each orthogonal <see cref="Link"/> that is a <see cref="MultiLink"/>
    /// (rendered as multiple <see cref="LinkViewModel"/>s), stores the shared vertical-trunk X
    /// so all destination branches overlap on the common horizontal exit segment.
    /// Built fresh at the start of every <see cref="RenderLinks"/> call.
    /// </summary>
    private readonly Dictionary<XTMF2.Link, double> _orthogonalSpineX = new();

    /// <summary>
    /// Tracks which orthogonal multi-link groups have already had their shared trunk
    /// geometry (glow + stroke) drawn during the current <see cref="RenderLinks"/> pass.
    /// Prevents the trunk glow from being painted N times causing it to appear too bright.
    /// </summary>
    private readonly HashSet<XTMF2.Link> _orthogonalTrunkDrawn = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// For each orthogonal multi-link group, stores the full vertical extent
    /// <c>(topY, bottomY)</c> of the shared spine — i.e. the min and max of all
    /// sibling branch Y values plus the origin Y — so the trunk is drawn long enough
    /// to reach every destination rather than stopping at the first one rendered.
    /// </summary>
    private readonly Dictionary<XTMF2.Link, (double TopY, double BottomY)> _orthogonalTrunkRange = new(ReferenceEqualityComparer.Instance);

    private void RenderLinks(DrawingContext ctx)
    {
        // ── Precompute shared spine-X for every orthogonal multi-link group ──────
        // Group all orthogonal LinkViewModels by their underlying Link object.
        // Multi-destination links share the same XTMF2.Link instance, so grouping
        // by reference identity collects all sibling arrows for the same hook.
        _orthogonalSpineX.Clear();
        _orthogonalTrunkDrawn.Clear();
        _orthogonalTrunkRange.Clear();
        var orthogonalGroups = _vm!.Links
            .Where(l => l.UnderlyingLink.IsOrthogonal && l.Destination is not null
                        && l.UnderlyingLink is MultiLink)
            .GroupBy(l => l.UnderlyingLink, ReferenceEqualityComparer.Instance);

        foreach (var group in orthogonalGroups)
        {
            var siblings = group.ToList();
            if (siblings.Count < 2) continue;

            // p1 is the same for every sibling (shared origin hook).
            var p1 = ComputeOrthogonalOriginPoint(siblings[0]);

            // Compute the individual spine X for each destination and take the
            // maximum so that every branch can be reached from the shared trunk.
            const double MinStub = 24.0;
            double sharedSpineX = p1.X + MinStub;
            double trunkTopY = p1.Y;
            double trunkBottomY = p1.Y;
            foreach (var sib in siblings)
            {
                if (sib.Destination is null) continue;
                // Approach the destination from the right of p1 for the initial estimate.
                var approachPt = new Point(p1.X + 1, p1.Y);
                var p2 = OrthogonalDestBorderPoint(sib.Destination, approachPt)
                         ?? new Point(sib.X2, sib.Y2);
                double indivMid = (p1.X + p2.X) * 0.5;
                if (indivMid < p1.X + MinStub) indivMid = p1.X + MinStub;
                if (indivMid > sharedSpineX) sharedSpineX = indivMid;

                // Track the full Y range so the spine covers every destination.
                if (p2.Y < trunkTopY) trunkTopY = p2.Y;
                if (p2.Y > trunkBottomY) trunkBottomY = p2.Y;
            }

            _orthogonalSpineX[(XTMF2.Link)group.Key!] = sharedSpineX;
            _orthogonalTrunkRange[(XTMF2.Link)group.Key!] = (trunkTopY, trunkBottomY);
        }

        foreach (var link in _vm!.Links)
        {
            // Don't render links whose destination is in a different boundary
            // or whose destination node is inlined (value shown in hook row instead).
            if (link.Destination is null) continue;
            if (link.Destination is NodeViewModel destNvm && destNvm.IsInlined) continue;

            bool isDisabled = link.UnderlyingLink.IsDisabled;
            var brush = link.IsSelected
                ? LinkSelBrush
                : isDisabled
                    ? (_isLight ? LinkDisabledBrushL : LinkDisabledBrush)
                    : (_isLight ? LinkBrushL : LinkBrush);
            var pen = link.IsSelected
                ? isDisabled ? LinkSelDisabledStrokePen : LinkSelStrokePen
                : isDisabled
                    ? (_isLight ? LinkDisabledStrokePenL : LinkDisabledStrokePen)
                    : (_isLight ? LinkStrokePenL : LinkStrokePen);

            // Neon glow: two wider transparent halos drawn beneath the main link line.
            Pen glowOuter, glowInner;
            if (link.IsSelected) { glowOuter = LinkSelGlowOuterPen; glowInner = LinkSelGlowInnerPen; }
            else if (isDisabled) { glowOuter = _isLight ? LinkDisabledGlowOuterPenL : LinkDisabledGlowOuterPen; glowInner = _isLight ? LinkDisabledGlowInnerPenL : LinkDisabledGlowInnerPen; }
            else if (_isLight)   { glowOuter = LinkGlowOuterPenL;   glowInner = LinkGlowInnerPenL; }
            else                 { glowOuter = LinkGlowOuterPen;     glowInner = LinkGlowInnerPen; }

            Point bp2, arrowFrom;
            Point shaftEnd;

            if (link.UnderlyingLink.IsOrthogonal)
            {
                // Orthogonal (right-angle) routing: horizontal exit → vertical jog → horizontal entry.
                // Use a shared spine X for multi-link groups so all branches overlap on the trunk.
                _orthogonalSpineX.TryGetValue(link.UnderlyingLink, out var spineX);
                bool hasSharedSpine = spineX > 0;
                var pts = ComputeOrthogonalPath(link, hasSharedSpine ? spineX : (double?)null);
                // pts = [p1, corner1, corner2, p2]  (always 4 points)
                bp2 = pts[^1];
                arrowFrom = BorderArrivalFrom(link.Destination, bp2);
                shaftEnd = DrawArrow(ctx, brush, arrowFrom, bp2);

                if (hasSharedSpine)
                {
                    // For multi-link groups the trunk is identical for every sibling.
                    // Draw trunk glow + stroke only once to avoid stacking alpha.
                    if (_orthogonalTrunkDrawn.Add(link.UnderlyingLink))
                    {
                        // Build the full-extent trunk from the precomputed range.
                        // The trunk is two segments that share the junction at (spineX, p1.Y):
                        //   1. Horizontal exit:  p1 → (spineX, p1.Y)
                        //   2. Full vertical:    (spineX, topY) → (spineX, bottomY)
                        // Drawing them as one polyline works when p1.Y is at one extreme;
                        // for the mixed case (branches above AND below) we draw two
                        // segments so the spine covers the complete range.
                        var p1Trunk = pts[0];   // hook anchor
                        var corner1 = pts[1];   // (spineX, p1.Y)
                        _orthogonalTrunkRange.TryGetValue(link.UnderlyingLink, out var range);
                        var spineTop = new Point(spineX, range.TopY);
                        var spineBot = new Point(spineX, range.BottomY);

                        // Horizontal exit + vertical spine as a joined polyline.
                        // The vertical goes from spineTop down to spineBot; corner1 is
                        // somewhere along it, so we route: p1 → corner1 → spineTop
                        // then a separate segment corner1 → spineBot (the other direction).
                        // This draws the T/L shape correctly with a single extra segment.
                        var mainTrunkGeo = MakePolyGeo([p1Trunk, corner1, spineTop]);
                        var extGeo = MakeSegGeo(corner1, spineBot);

                        foreach (var trunkPen in new[] { glowOuter, glowInner, pen })
                        {
                            ctx.DrawGeometry(null, trunkPen, mainTrunkGeo);
                            ctx.DrawGeometry(null, trunkPen, extGeo);
                        }
                    }

                    // Branch segment: corner2 → p2  (glow) and corner2 → shaftEnd (stroke).
                    var branchGlowGeo = MakeSegGeo(pts[^2], pts[^1]);          // corner2 → bp2
                    var branchShaftGeo = MakeSegGeo(pts[^2], shaftEnd);         // corner2 → shaftEnd
                    ctx.DrawGeometry(null, glowOuter, branchGlowGeo);
                    ctx.DrawGeometry(null, glowInner, branchGlowGeo);
                    ctx.DrawGeometry(null, pen, branchShaftGeo);
                }
                else
                {
                    // Single-destination orthogonal link: draw the full path normally.
                    var glowGeo = MakePolyGeo(pts);
                    var shaftGeo = ReplacePolyGeoLastPoint(pts, shaftEnd);
                    ctx.DrawGeometry(null, glowOuter, glowGeo);
                    ctx.DrawGeometry(null, glowInner, glowGeo);
                    ctx.DrawGeometry(null, pen, shaftGeo);
                }
            }
            else
            {
                // Draw an S-shaped cubic Bézier curve. Tension adapts to the span so short
                // links curve gently and long ones sweep broadly, with no elbow kinks.
                var (bp1, bc1, bc2, bp2c) = ComputeSCurve(link);
                bp2 = bp2c;
                arrowFrom = BorderArrivalFrom(link.Destination, bp2);
                shaftEnd = DrawArrow(ctx, brush, arrowFrom, bp2);

                // Build the geometry once; reuse for glow and main stroke.
                static StreamGeometry MakeCurveGeo(Point p1, Point c1, Point c2, Point end)
                {
                    var g = new StreamGeometry();
                    using var gc = g.Open();
                    gc.BeginFigure(p1, isFilled: false);
                    gc.CubicBezierTo(c1, c2, end);
                    gc.EndFigure(isClosed: false);
                    return g;
                }

                var glowGeo = MakeCurveGeo(bp1, bc1, bc2, bp2);
                var shaftGeo = MakeCurveGeo(bp1, bc1, bc2, shaftEnd);

                ctx.DrawGeometry(null, glowOuter, glowGeo);
                ctx.DrawGeometry(null, glowInner, glowGeo);
                ctx.DrawGeometry(null, pen, shaftGeo);
            }

            // For multi-link destinations draw a small 1-based index number
            // beside the arrowhead so the user can see the hook slot ordering.
            if (link.UnderlyingLink is MultiLink ml)
            {
                int idx = -1;
                if (link.Destination is NodeViewModel indexDestNode)
                    idx = ml.Destinations.IndexOf(indexDestNode.UnderlyingNode);
                else if (link.Destination is FunctionInstanceViewModel indexDestFi)
                    idx = ml.Destinations.IndexOf(indexDestFi.UnderlyingInstance);

                if (idx >= 0)
                {
                    var ft = MakeText((idx + 1).ToString(), LinkIndexFontSize, brush);
                    double adx = bp2.X - arrowFrom.X;
                    double ady = bp2.Y - arrowFrom.Y;
                    double dlen = Math.Sqrt(adx * adx + ady * ady);
                    if (dlen >= 1)
                    {
                        double ux = adx / dlen, uy = ady / dlen;
                        double nx = -uy, ny = ux; // 90° CCW perpendicular unit vector
                        double offset = ft.Height * 0.5 + 3;
                        double lx = shaftEnd.X - ft.Width * 0.5 + nx * offset;
                        double ly = shaftEnd.Y - ft.Height * 0.5 + ny * offset;
                        ctx.DrawText(ft, new Point(lx, ly));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Draws a filled arrowhead triangle at <paramref name="to"/> pointing from
    /// <paramref name="from"/> toward <paramref name="to"/>, using <paramref name="brush"/>.
    /// </summary>
    /// <returns>The shaft end point (base of the arrowhead triangle), so the caller
    /// can shorten the preceding line segment to avoid overlap with the filled head.</returns>
    private static Point DrawArrow(DrawingContext ctx, IBrush brush, Point from, Point to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return to;

        // Unit direction and perpendicular
        var ux = dx / len;
        var uy = dy / len;
        var px = -uy * (ArrowSize * 0.45);
        var py = ux * (ArrowSize * 0.45);

        var tip = to;
        var left = new Point(to.X - ux * ArrowSize + px, to.Y - uy * ArrowSize + py);
        var right = new Point(to.X - ux * ArrowSize - px, to.Y - uy * ArrowSize - py);
        var shaftEnd = new Point(to.X - ux * ArrowSize, to.Y - uy * ArrowSize);

        // Filled triangle for the head
        var geo = new StreamGeometry();
        using (var gc = geo.Open())
        {
            gc.BeginFigure(tip, isFilled: true);
            gc.LineTo(left);
            gc.LineTo(right);
            gc.EndFigure(isClosed: true);
        }
        ctx.DrawGeometry(brush, null, geo);

        return shaftEnd;
    }

    private void RenderPendingLink(DrawingContext ctx)
    {
        if (_linkOrigin is null) return;
        var pen = PendingLinkStrokePen;

        // p1 and exit direction follow the same rules as ComputeSCurve.
        Point p1;
        Vector exitDir;
        var cursor = _linkCurrentPos;
        if (_linkOrigin is StartViewModel pendingStart)
        {
            var oc = new Point(pendingStart.CenterX, pendingStart.CenterY);
            p1 = BorderPoint(_linkOrigin, cursor) ?? oc;
            var odx = p1.X - oc.X; var ody = p1.Y - oc.Y;
            var ol = Math.Sqrt(odx * odx + ody * ody);
            exitDir = ol < 0.1 ? new Vector(1, 0) : new Vector(odx / ol, ody / ol);
        }
        else
        {
            p1 = new Point(_linkOrigin.CenterX, _linkOrigin.CenterY);
            exitDir = new Vector(1, 0);
        }

        var p2 = cursor;
        // Approach direction for the free cursor end: from origin toward cursor.
        var aprDx = p1.X - p2.X; var aprDy = p1.Y - p2.Y;
        var aprLen = Math.Sqrt(aprDx * aprDx + aprDy * aprDy);
        Vector approachDir = aprLen < 0.1 ? new Vector(-1, 0) : new Vector(aprDx / aprLen, aprDy / aprLen);

        double dx = p2.X - p1.X, dy = p2.Y - p1.Y;
        double tension = Math.Max(Math.Sqrt(dx * dx + dy * dy) * 0.45, 50.0);
        var c1 = new Point(p1.X + exitDir.X * tension, p1.Y + exitDir.Y * tension);
        var c2 = new Point(p2.X + approachDir.X * tension, p2.Y + approachDir.Y * tension);

        // Sample near tip so the pending arrowhead angle is also accurate.
        var pendingArrowFrom = SampleCubicBezier(p1, c1, c2, p2, 0.97);
        var shaftEnd = DrawArrow(ctx, PendingLinkBrush, pendingArrowFrom, p2);

        var geo = new StreamGeometry();
        using (var gc = geo.Open())
        {
            gc.BeginFigure(p1, isFilled: false);
            gc.CubicBezierTo(c1, c2, shaftEnd);
            gc.EndFigure(isClosed: false);
        }
        ctx.DrawGeometry(null, PendingLinkGlowOuterPen, geo);
        ctx.DrawGeometry(null, PendingLinkGlowInnerPen, geo);
        ctx.DrawGeometry(null, pen, geo);
    }

    private void RenderNodes(DrawingContext ctx)
    {
        foreach (var node in _vm!.Nodes)
        {
            // Inlined nodes are hidden — skip canvas rendering entirely.
            if (node.IsInlined) continue;

            double rw = NodeRenderWidth(node);
            double rh = NodeRenderHeight(node);
            var rect = new Rect(node.X, node.Y, rw, rh);
            bool isDisabled = node.UnderlyingNode.IsDisabled;

            // Determine whether this node is the designated entry point of the current template.
            bool isEntryNode = _vm.IsInsideFunctionTemplate
                && _vm.CurrentFunctionTemplate?.UnderlyingTemplate.EntryNode == node.UnderlyingNode;

            var border = node.IsSelected
                ? NodeSelPen
                : isDisabled
                    ? (_isLight ? DisabledNodeBorderPenL : DisabledNodeBorderPen)
                    : (_isLight ? NodeBorderPenL : NodeBorderPen);

            // Node background + border
            DrawRectGlow(ctx, rect, NodeCornerRadius,
                node.IsSelected
                    ? SelectionGlowBrushes
                    : isDisabled
                        ? (_isLight ? DisabledGlowBrushesL : DisabledGlowBrushes)
                        : (_isLight ? NodeGlowBrushesL : NodeGlowBrushes));
            ctx.DrawRectangle(isDisabled ? (_isLight ? DisabledNodeFillL : DisabledNodeFill) : (_isLight ? NodeFillL : NodeFill), border, rect, NodeCornerRadius, NodeCornerRadius);

            // ── Entry-node gold ring (drawn over the normal border) ───────────
            if (isEntryNode)
            {
                var outerRect = new Rect(
                    node.X - EntryNodeRingExtra, node.Y - EntryNodeRingExtra,
                    rw + EntryNodeRingExtra * 2, rh + EntryNodeRingExtra * 2);
                ctx.DrawRectangle(null,
                    EntryNodeRingPen,
                    outerRect,
                    NodeCornerRadius + EntryNodeRingExtra,
                    NodeCornerRadius + EntryNodeRingExtra);
            }

            // ── Header: node name centred in the header band ──────────────
            // For parameter nodes prefix the name with a type glyph:
            //   ≡ (U+2261) → BasicParameter  (literal / constant value)
            //   ƒ (U+0192) → ScriptedParameter (formula / expression)
            double headerBottom = node.Y + NodeHeaderHeight;
            string nodeDisplayName = node.IsParameterNode
                ? (node.IsBasicParameter ? "≡ " : "ƒ ") + node.Name
                : node.Name;
            var ft = MakeText(nodeDisplayName, NodeFontSize,
                isDisabled ? (_isLight ? DisabledNodeTextBrushL : DisabledNodeTextBrush) : (_isLight ? NodeTextBrushL : NodeTextBrush));
            double tx = node.X + (rw - ft.Width) / 2;
            double ty = node.Y + (NodeHeaderHeight - ft.Height) / 2;
            ctx.DrawText(ft, new Point(tx, ty));

            // ── "▶ Entry Point" badge — left-aligned in the lower portion of the header ──
            if (isEntryNode)
            {
                var labelBrush = _isLight ? EntryNodeLabelBrushL : EntryNodeLabelBrush;
                var badge = MakeText("▶ Entry Point", EntryNodeLabelFontSize, labelBrush);
                double blx = node.X + 6.0;
                double bly = node.Y + NodeHeaderHeight - badge.Height - 2.5;
                using (ctx.PushClip(new Rect(node.X + 2, node.Y, rw - 4, NodeHeaderHeight)))
                {
                    ctx.DrawText(badge, new Point(blx, bly));
                }
            }

            // ── Resize handle (bottom-right corner) ───────────────────────
            // Three small diagonal dots — standard grip indicator.
            {
                double dotR = 2.0;
                double bx = node.X + rw;
                double by = node.Y + rh;
                for (int d = 0; d < 3; d++)
                {
                    double offset = 4.0 + d * 4.0;
                    ctx.DrawEllipse(isDisabled ? (_isLight ? DisabledHandleBrushL : DisabledHandleBrush) : (_isLight ? ResizeHandleBrushL : ResizeHandleBrush), null,
                        new Point(bx - offset + dotR, by - dotR), dotR, dotR);
                    ctx.DrawEllipse(isDisabled ? (_isLight ? DisabledHandleBrushL : DisabledHandleBrush) : (_isLight ? ResizeHandleBrushL : ResizeHandleBrush), null,
                        new Point(bx - dotR, by - offset + dotR), dotR, dotR);
                }
            }

            // ── Hook toggle icon (top-right of header) ─────────────────────
            // Only shown when the node has at least one hook and there is no
            // global ShowAllHooks override (per-node toggle would be redundant).
            if (!_vm.ShowAllHooks && node.UnderlyingNode.Hooks.Count > 0)
            {
                var iconRect = HookToggleIconRect(node, rw);
                var iconBg = isDisabled
                    ? (_isLight ? DisabledToggleBgL : DisabledToggleBg)
                    : node.ShowHooks ? (_isLight ? HookToggleActiveBgL : HookToggleActiveBg) : (_isLight ? HookToggleBgL : HookToggleBg);
                ctx.DrawRectangle(iconBg, null, iconRect, 3.0, 3.0);
                var glyph = node.ShowHooks ? "\u25BE" : "\u25B8";  // ▾ or ▸
                var iconFt = MakeText(glyph, HookFontSize + 1.0,
                    isDisabled ? (_isLight ? DisabledToggleTextL : DisabledToggleText) : (_isLight ? HookToggleTextL : HookToggleText));
                var glyphX = iconRect.X + (iconRect.Width - iconFt.Width) / 2.0;
                var glyphY = iconRect.Y + (iconRect.Height - iconFt.Height) / 2.0;
                ctx.DrawText(iconFt, new Point(glyphX, glyphY));
            }

            // ── Minimize-to-inline button (top-left of header) ───────────
            // Only on BasicParameter nodes that are wired to a Single hook
            // and can therefore be folded into the parent's hook row.
            if (_canInlineNodes.Contains(node))
            {
                var minRect = InlineMinimizeButtonRect(node);
                ctx.DrawRectangle(_isLight ? MinimizeBtnBgL : MinimizeBtnBg, null, minRect, 3.0, 3.0);
                var minFt = MakeText("\u229f", HookFontSize,
                    isDisabled ? (_isLight ? DisabledToggleTextL : DisabledToggleText) : (_isLight ? MinimizeBtnTextL : MinimizeBtnText));  // ⊟ minus-in-box
                var minGlX = minRect.X + (minRect.Width - minFt.Width) / 2.0;
                var minGlY = minRect.Y + (minRect.Height - minFt.Height) / 2.0;
                ctx.DrawText(minFt, new Point(minGlX, minGlY));
            }

            // ── Hook rows ─────────────────────────────────────────────────
            bool hasParamRow = node.IsParameterNode;
            bool hasHooks = _nodeVisibleHooks.TryGetValue(node, out var hooks) && hooks.Count > 0;

            if (!hasParamRow && !hasHooks)
                continue;

            // Divider line separating header from content rows
            var dividerPen = isDisabled ? (_isLight ? DisabledDividerPenL : DisabledDividerPen) : (_isLight ? HookDividerPenL : HookDividerPen);
            ctx.DrawLine(dividerPen,
                new Point(node.X + 1, headerBottom),
                new Point(node.X + rw - 1, headerBottom));

            int rowOffset = 0;

            // ── Parameter value row (first, for parameter nodes) ──────────
            if (hasParamRow)
            {
                var paramValue = node.ParameterValueRepresentation;
                double rowMidY = node.Y + NodeHeaderHeight + HookRowHeight / 2.0;

                // Subtle tinted background for readability
                ctx.DrawRectangle(isDisabled ? (_isLight ? DisabledDividerBrushL : DisabledDividerBrush) : (_isLight ? ParamValueBgL : ParamValueBg), null,
                    new Rect(node.X + 1, node.Y + NodeHeaderHeight, rw - 2, HookRowHeight));

                const double textPad = 6.0;
                // Skip drawing the text while this exact node is being edited —
                // the inline TextBox (and syntax overlay) already cover that row.
                if (node != _editingParamNode)
                {
                    var display = string.IsNullOrEmpty(paramValue) ? "(no value)" : paramValue;
                    var paramFt = MakeText(display, HookFontSize,
                        isDisabled ? (_isLight ? DisabledHookTextBrushL : DisabledHookTextBrush) : (_isLight ? ParamValueTextBrushL : ParamValueTextBrush));
                    double maxW = rw - textPad * 2;
                    double paramTy = rowMidY - paramFt.Height / 2.0;
                    using (ctx.PushClip(new Rect(node.X + textPad, paramTy, Math.Max(0, maxW), paramFt.Height + 1)))
                        ctx.DrawText(paramFt, new Point(node.X + textPad, paramTy));
                }

                rowOffset = 1;

                // Separator below the value row when hooks follow
                if (hasHooks)
                {
                    double sepY = node.Y + NodeHeaderHeight + HookRowHeight;
                    ctx.DrawLine(isDisabled ? (_isLight ? DisabledDividerPenThinL : DisabledDividerPenThin) : new Pen(_isLight ? HookDividerBrushL : HookDividerBrush, 0.5),
                        new Point(node.X + 1, sepY),
                        new Point(node.X + rw - 1, sepY));
                }
            }

            if (!hasHooks)
                continue;

            _nodeConnectedHooks.TryGetValue(node, out var connected);

            for (int i = 0; i < hooks!.Count; i++)
            {
                var hook = hooks[i];
                bool conn = connected is not null && connected.Contains(hook);
                // Is this hook occupied by an inlined BasicParameter?
                bool hasInlined = _hookInlinedParam.TryGetValue((node, hook), out var inlinedParam);

                // Unsatisfied: required cardinality with no connection at all.
                bool isRequired = hook.Cardinality == HookCardinality.Single
                                || hook.Cardinality == HookCardinality.AtLeastOne;
                bool unsatisfied = isRequired && !conn && !hasInlined;

                double rowMidY = node.Y + NodeHeaderHeight + (rowOffset + i) * HookRowHeight + HookRowHeight / 2.0;
                double rowTopY = node.Y + NodeHeaderHeight + (rowOffset + i) * HookRowHeight;

                // Tinted background: red for unsatisfied required hooks, amber for inlined params.
                if (!isDisabled && unsatisfied)
                {
                    ctx.DrawRectangle(HookUnsatisfiedRowBg, null,
                        new Rect(node.X + 1, rowTopY, rw - 2, HookRowHeight));
                }
                else if (!isDisabled && hasInlined)
                {
                    ctx.DrawRectangle(InlineParamRowBg, null,
                        new Rect(node.X + 1, rowTopY, rw - 2, HookRowHeight));
                }

                // Dot on the right edge (the link anchor).
                // Red for unsatisfied required hooks, green for connected/inlined, grey otherwise.
                var dotBrush = isDisabled ? (_isLight ? DisabledDotBrushL : DisabledDotBrush)
                             : unsatisfied ? (_isLight ? HookUnsatisfiedBrushL : HookUnsatisfiedBrush)
                             : (conn || hasInlined) ? (_isLight ? HookConnectedBrushL : HookConnectedBrush)
                             : (_isLight ? HookUnconnectedBrushL : HookUnconnectedBrush);
                ctx.DrawEllipse(dotBrush, null,
                    new Point(node.X + rw, rowMidY),
                    HookDotRadius, HookDotRadius);

                // When at least one link from this hook departs leftward, also draw a dot
                // on the left edge so the visual anchor matches the link exit point.
                if (_leftGoingHooks.Contains((node, hook)))
                {
                    ctx.DrawEllipse(dotBrush, null,
                        new Point(node.X, rowMidY),
                        HookDotRadius, HookDotRadius);
                }

                // Hook name + optional inlined value.
                // When a param is inlined, prefix with ≡ (BasicParameter) or ƒ (ScriptedParameter).
                // Inlined values use the same green as connected hooks; ScriptedParameter gets the
                // extra lavender accent on top so users can tell the two param kinds apart.
                const double textPad = 6.0;
                string hookLabel = hasInlined && inlinedParam is not null
                    ? $"{(inlinedParam.IsBasicParameter ? "≡" : "ƒ")} {hook.Name}: {(string.IsNullOrEmpty(inlinedParam.ParameterValueRepresentation) ? "(no value)" : inlinedParam.ParameterValueRepresentation)}"
                    : hook.Name;
                IBrush hookTextBrush = isDisabled ? (_isLight ? DisabledHookTextBrushL : DisabledHookTextBrush)
                                     : unsatisfied ? (_isLight ? HookTextUnsatisfiedBrushL : HookTextUnsatisfiedBrush)
                                     : hasInlined && inlinedParam is { IsScriptedParameter: true }
                                         ? (_isLight ? ScriptParamAccentBrushL : ScriptParamAccentBrush)
                                     : hasInlined ? (_isLight ? HookTextConnBrushL : HookTextConnBrush)
                                     : conn ? (_isLight ? HookTextConnBrushL : HookTextConnBrush)
                                     : (_isLight ? HookTextDimBrushL : HookTextDimBrush);
                var hookFt = MakeText(hookLabel, HookFontSize, hookTextBrush);
                double maxW = rw - textPad * 2 - HookDotRadius * 2;
                double hookTy = rowMidY - hookFt.Height / 2.0;
                using (ctx.PushClip(new Rect(node.X + textPad, hookTy, Math.Max(0, maxW), hookFt.Height + 1)))
                    ctx.DrawText(hookFt, new Point(node.X + textPad, hookTy));

                // Row separator (skip after last row)
                if (i < hooks.Count - 1)
                {
                    double sepY = node.Y + NodeHeaderHeight + (rowOffset + i + 1) * HookRowHeight;
                    ctx.DrawLine(isDisabled ? (_isLight ? DisabledDividerPenThinL : DisabledDividerPenThin) : (_isLight ? HookDividerPenThinL : HookDividerPenThin),
                        new Point(node.X + 1, sepY),
                        new Point(node.X + rw - 1, sepY));
                }
            }
        }
    }

    /// <summary>
    /// Draws ghost nodes — placeholder representations of nodes that live in another
    /// boundary. Rendered as semi-transparent dashed rectangles in the steel-blue
    /// ghost palette, with the remote node's name in the header.
    /// </summary>
    private void RenderGhostNodes(DrawingContext ctx)
    {
        foreach (var ghost in _vm!.GhostNodes)
        {
            double rw = ghost.Width;
            double rh = ghost.Height;
            var rect = new Rect(ghost.X, ghost.Y, rw, rh);

            // Fill is semi-transparent; border is dashed.
            var border = ghost.IsSelected ? GhostSelPen : (_isLight ? GhostBorderPenL : GhostBorderPen);

            DrawRectGlow(ctx, rect, NodeCornerRadius, ghost.IsSelected ? SelectionGlowBrushes : (_isLight ? GhostGlowBrushesL : GhostGlowBrushes));
            ctx.DrawRectangle(_isLight ? GhostNodeFillL : GhostNodeFill, border, rect, NodeCornerRadius, NodeCornerRadius);

            // Ghost icon prefix ("⊙ ") to distinguish from real nodes at a glance.
            var labelText = "\u2299 " + ghost.Name;
            var ft = MakeText(labelText, NodeFontSize, _isLight ? NodeTextBrushL : NodeTextBrush);
            var tx = ghost.X + (rw - ft.Width) / 2;
            var ty = ghost.Y + (NodeHeaderHeight - ft.Height) / 2;

            using (ctx.PushClip(new Rect(ghost.X + 4, ghost.Y, rw - 8, NodeHeaderHeight)))
            {
                ctx.DrawText(ft, new Point(tx, ty));
            }

            // Resize grip dots (bottom-right corner).
            {
                double dotR = 2.0;
                double bx = ghost.X + rw;
                double by = ghost.Y + rh;
                for (int d = 0; d < 3; d++)
                {
                    double offset = 4.0 + d * 4.0;
                    ctx.DrawEllipse(_isLight ? ResizeHandleBrushL : ResizeHandleBrush, null,
                        new Point(bx - offset + dotR, by - dotR), dotR, dotR);
                    ctx.DrawEllipse(_isLight ? ResizeHandleBrushL : ResizeHandleBrush, null,
                        new Point(bx - dotR, by - offset + dotR), dotR, dotR);
                }
            }
        }
    }

    private void RenderStarts(DrawingContext ctx)
    {
        foreach (var start in _vm!.Starts)
        {
            var fill = start.IsSelected ? StartSelFill : (_isLight ? StartFillL : StartFill);
            var center = new Point(start.CenterX, start.CenterY);
            var r = StartViewModel.Radius;
            var border = start.IsSelected ? NodeSelPen : (_isLight ? StartBorderPenL : NodeBorderPen);

            DrawEllipseGlow(ctx, center, r, r, start.IsSelected ? SelectionGlowBrushes : (_isLight ? StartGlowBrushesL : StartGlowBrushes));
            ctx.DrawEllipse(fill, border, center, r, r);

            // Label below the circle
            var isLight = _isLight;
            var ft = MakeText(start.Name, StartFontSize, isLight ? StartTextBrushLight : StartTextBrush);
            var lx = start.X + (start.Diameter - ft.Width) / 2;
            var ly = start.Y + start.Diameter + 3;
            ctx.DrawText(ft, new Point(lx, ly));
        }
    }

    /// <summary>
    /// Draws a neon glow halo around a rounded rectangle using 5 outward-expanding
    /// semi-transparent layers. <paramref name="glow"/> must be a 5-element array
    /// produced by <see cref="MakeGlowBrushes"/>; the expansions are [14, 9, 5, 2.5, 1].
    /// </summary>
    private static void DrawRectGlow(DrawingContext ctx, Rect rect, double cornerRadius, IBrush[] glow)
    {
        ctx.DrawRectangle(glow[0], null, rect.Inflate(14),  cornerRadius + 14,  cornerRadius + 14);
        ctx.DrawRectangle(glow[1], null, rect.Inflate(9),   cornerRadius + 9,   cornerRadius + 9);
        ctx.DrawRectangle(glow[2], null, rect.Inflate(5),   cornerRadius + 5,   cornerRadius + 5);
        ctx.DrawRectangle(glow[3], null, rect.Inflate(2.5), cornerRadius + 2.5, cornerRadius + 2.5);
        ctx.DrawRectangle(glow[4], null, rect.Inflate(1),   cornerRadius + 1,   cornerRadius + 1);
    }

    /// <summary>
    /// Draws a neon glow halo around an ellipse using 5 outward-expanding semi-transparent layers.
    /// <paramref name="glow"/> must be a 5-element array produced by <see cref="MakeGlowBrushes"/>;
    /// the expansions are [14, 9, 5, 2.5, 1].
    /// </summary>
    private static void DrawEllipseGlow(DrawingContext ctx, Point center, double rx, double ry, IBrush[] glow)
    {
        ctx.DrawEllipse(glow[0], null, center, rx + 14,  ry + 14);
        ctx.DrawEllipse(glow[1], null, center, rx + 9,   ry + 9);
        ctx.DrawEllipse(glow[2], null, center, rx + 5,   ry + 5);
        ctx.DrawEllipse(glow[3], null, center, rx + 2.5, ry + 2.5);
        ctx.DrawEllipse(glow[4], null, center, rx + 1,   ry + 1);
    }

    // ── FormattedText cache ────────────────────────────────────────────────
    // Keyed by (text, emSize, brush-reference). IBrush is a reference type and all
    // brushes in this codebase are static readonly fields, so reference equality
    // is a reliable proxy for visual identity across both theme variants.
    //
    // Growth risk: user-typed parameter values are arbitrary strings and would
    // accumulate indefinitely without a bound. We evict the entire cache once it
    // reaches TextCacheMaxEntries. A full eviction is O(1) and the common stable
    // labels (hook names, glyphs, link indices) will be re-warmed within one frame.
    // If we end up with over 1024 text elements being rendered we will lose the benifit of the cahce
    // so we will need to measure what a "normal" number of text elements is in a complex graph and adjust the cache size accordingly.
    private const int TextCacheMaxEntries = 1024;
    private static readonly Dictionary<(string, double, IBrush), FormattedText> _textCache = new(TextCacheMaxEntries + 1);

    private static FormattedText MakeText(string text, double size, IBrush foreground)
    {
        var key = (text, size, foreground);
        if (!_textCache.TryGetValue(key, out var ft))
        {
            if (_textCache.Count >= TextCacheMaxEntries)
                _textCache.Clear();
            _textCache[key] = ft = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                DefaultTypeface,
                size,
                foreground);
        }
        return ft;
    }

}