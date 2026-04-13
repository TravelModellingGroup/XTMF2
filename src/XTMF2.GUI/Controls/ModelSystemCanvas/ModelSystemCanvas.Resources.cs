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
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.VisualTree;
using XTMF2.Editing;
using XTMF2.GUI.ViewModels;
using XTMF2.ModelSystemConstruct;
using System.Collections.ObjectModel;

namespace XTMF2.GUI.Controls;

partial class ModelSystemCanvas
{
    // ── Brushes / pens (shared, immutable) ───────────────────────────────
    private static readonly IBrush CanvasBackground = new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x18)); // matches DlgBg dark token
    private static readonly IBrush CanvasBackgroundLight = new SolidColorBrush(Color.FromRgb(0xF0, 0xF4, 0xF8));
    private static readonly IBrush NodeFill = new SolidColorBrush(Color.FromRgb(0x0E, 0x22, 0x38)); // deep dark blue
    private static readonly IBrush NodeBorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xCC, 0xFF)); // neon cyan
    private static readonly IBrush NodeSelBrush = Brushes.DodgerBlue;
    private static readonly IBrush NodeTextBrush = Brushes.White;
    private static readonly IBrush StartFill = new SolidColorBrush(Color.FromRgb(0xFF, 0x77, 0x00)); // vivid orange
    private static readonly IBrush StartSelFill = Brushes.DodgerBlue;
    private static readonly IBrush StartTextBrush = Brushes.White;
    private static readonly IBrush StartTextBrushLight = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E));
    private static readonly IBrush LinkBrush = new SolidColorBrush(Color.FromRgb(0x22, 0xBB, 0xDD)); // teal-cyan
    private static readonly IBrush LinkSelBrush = Brushes.OrangeRed;
    private static readonly IBrush PendingLinkBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71));
    private static readonly DashStyle PendingLinkDash = new DashStyle([6, 4], 0);
    // Ghost node styling
    private static readonly IBrush GhostNodeFill = new SolidColorBrush(Color.FromArgb(0x50, 0x0E, 0x22, 0x38));
    private static readonly IBrush GhostNodeBorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x99, 0xDD)); // steel-blue neon
    private static readonly IBrush GhostNodeSelBrush = Brushes.DodgerBlue;
    private static readonly DashStyle GhostNodeDash = new DashStyle([6, 4], 0);

    // Scripted-parameter syntax-highlight token colours
    private static readonly IBrush ScriptVarKnownBrush = new SolidColorBrush(Color.FromRgb(0x44, 0xDD, 0x88)); // known variable → green
    private static readonly IBrush ScriptVarUnknownBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44)); // unrecognised identifier → red
    private static readonly IBrush ScriptOperatorBrush = new SolidColorBrush(Color.FromRgb(0xAA, 0xBB, 0xCC)); // operators / punctuation → steel-blue
    private static readonly IBrush ScriptNumberBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0xD7, 0xFF)); // numeric literals → light blue
    private static readonly IBrush ScriptStringBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xB8, 0x60)); // string literals → orange
    private static readonly IBrush ScriptKeywordBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x82)); // true / false → gold

    // Parameter value row
    private static readonly IBrush ParamValueTextBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x82));
    private static readonly IBrush ParamValueBg = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));

    // Comment block colours (sticky-note style)
    private static readonly IBrush CommentFill = new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xF2, 0x90));
    private static readonly IBrush CommentSelFill = new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xE0, 0x50));
    private static readonly IBrush CommentBorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xBB, 0x00));   // warm gold
    private static readonly IBrush CommentSelBorder = Brushes.DodgerBlue;
    private static readonly IBrush CommentTextBrush = new SolidColorBrush(Color.FromRgb(0x22, 0x1E, 0x00));
    /// <summary>Slightly deeper/more saturated yellow for the adhesive-tab band at the top of the sticky note.</summary>
    private static readonly IBrush CommentHeaderBrush = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xD5, 0x1A));
    /// <summary>Cream colour for the fold-flap back face (the bit of paper you see when the corner is turned).</summary>
    private static readonly IBrush CommentFoldBackBrush = new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xFA, 0xD0));
    /// <summary>Semi-transparent black drop shadow for the sticky note.</summary>
    private static readonly IBrush CommentShadowBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x00, 0x00, 0x00));
    /// <summary>Faint pen for horizontal ruled lines on the note body.</summary>
    private static readonly Pen CommentRulePen = new Pen(new SolidColorBrush(Color.FromArgb(0x50, 0xA0, 0x8A, 0x00)), 0.6);
    // Hook colours
    private static readonly IBrush HookConnectedBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71));
    private static readonly IBrush HookUnconnectedBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x66, 0x77));
    private static readonly IBrush HookDividerBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x55, 0x66));
    private static readonly IBrush HookTextConnBrush = new SolidColorBrush(Color.FromRgb(0xAA, 0xEE, 0xBB));
    private static readonly IBrush HookTextDimBrush = new SolidColorBrush(Color.FromRgb(0x77, 0x88, 0x99));
    // Unsatisfied required hook (Single / AtLeastOne with no connection)
    private static readonly IBrush HookUnsatisfiedBrush = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
    private static readonly IBrush HookTextUnsatisfiedBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x99, 0x88));
    private static readonly IBrush HookUnsatisfiedRowBg = new SolidColorBrush(Color.FromArgb(0x30, 0xE7, 0x4C, 0x3C));
    // Hook toggle icon
    private static readonly IBrush HookToggleBg = new SolidColorBrush(Color.FromArgb(0x60, 0x55, 0x88, 0xCC));
    private static readonly IBrush HookToggleActiveBg = new SolidColorBrush(Color.FromArgb(0x90, 0x33, 0x99, 0xFF));
    private static readonly IBrush HookToggleText = new SolidColorBrush(Color.FromRgb(0xBB, 0xCC, 0xEE));
    // Resize handle
    private static readonly IBrush ResizeHandleBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xAA, 0xBB, 0xCC));
    // Inline parameter hook row tint
    private static readonly IBrush InlineParamRowBg = new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xE0, 0x80));
    // ScriptedParameter accent — used to visually distinguish ScriptedParameter from BasicParameter
    // in inline hook-row labels (dark mode: lavender-purple; light mode: deep violet).
    private static readonly IBrush ScriptParamAccentBrush  = new SolidColorBrush(Color.FromRgb(0xAA, 0x88, 0xFF));
    private static readonly IBrush ScriptParamAccentBrushL = new SolidColorBrush(Color.FromRgb(0x66, 0x00, 0xCC));
    // Minimize-to-inline button on BasicParameter nodes
    private static readonly IBrush MinimizeBtnBg = new SolidColorBrush(Color.FromArgb(0x60, 0x88, 0xCC, 0x55));
    private static readonly IBrush MinimizeBtnText = new SolidColorBrush(Color.FromRgb(0xCC, 0xFF, 0xAA));
    // Rubber-band (Ctrl+drag) multi-selection rectangle
    private static readonly IBrush SelectionRectFill = new SolidColorBrush(Color.FromArgb(0x2E, 0x44, 0x88, 0xFF));
    private static readonly DashStyle SelectionRectDash = new DashStyle([5, 4], 0);

    // ── Light-mode palette ────────────────────────────────────────────────
    // Each entry below is the light-mode counterpart of a dark-mode brush above.
    // Render methods select between the two sets via _isLight.
    private static readonly IBrush NodeFillL = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)); // white card
    private static readonly IBrush NodeBorderBrushL = new SolidColorBrush(Color.FromRgb(0x00, 0x66, 0xBB)); // strong blue
    private static readonly IBrush NodeTextBrushL = new SolidColorBrush(Color.FromRgb(0x0D, 0x1B, 0x2A)); // near-black
    private static readonly IBrush GhostNodeFillL = new SolidColorBrush(Color.FromArgb(0x50, 0xB4, 0xC8, 0xDC));
    private static readonly IBrush GhostNodeBorderBrushL = new SolidColorBrush(Color.FromRgb(0x33, 0x77, 0xBB));
    private static readonly IBrush LinkBrushL = new SolidColorBrush(Color.FromRgb(0x00, 0x55, 0xAA));
    private static readonly IBrush PendingLinkBrushL = new SolidColorBrush(Color.FromRgb(0x1A, 0x7A, 0x40));
    // Script syntax highlight (light)
    private static readonly IBrush ScriptVarKnownBrushL = new SolidColorBrush(Color.FromRgb(0x1A, 0x7A, 0x40));
    private static readonly IBrush ScriptVarUnknownBrushL = new SolidColorBrush(Color.FromRgb(0xCC, 0x00, 0x00));
    private static readonly IBrush ScriptOperatorBrushL = new SolidColorBrush(Color.FromRgb(0x44, 0x55, 0x66));
    private static readonly IBrush ScriptNumberBrushL = new SolidColorBrush(Color.FromRgb(0x00, 0x44, 0xAA));
    private static readonly IBrush ScriptStringBrushL = new SolidColorBrush(Color.FromRgb(0x8B, 0x45, 0x00));
    private static readonly IBrush ScriptKeywordBrushL = new SolidColorBrush(Color.FromRgb(0x7B, 0x50, 0x00));
    // Parameter value row (light)
    private static readonly IBrush ParamValueTextBrushL = new SolidColorBrush(Color.FromRgb(0x6B, 0x4A, 0x00));
    private static readonly IBrush ParamValueBgL = new SolidColorBrush(Color.FromArgb(0x18, 0x00, 0x00, 0x00));
    // Hook row (light)
    private static readonly IBrush HookConnectedBrushL = new SolidColorBrush(Color.FromRgb(0x1A, 0x7A, 0x40));
    private static readonly IBrush HookUnconnectedBrushL = new SolidColorBrush(Color.FromRgb(0x88, 0x99, 0xAA));
    private static readonly IBrush HookUnsatisfiedBrushL = new SolidColorBrush(Color.FromRgb(0xCC, 0x00, 0x00));
    private static readonly IBrush HookDividerBrushL = new SolidColorBrush(Color.FromRgb(0xBC, 0xCD, 0xE0));
    private static readonly IBrush HookTextConnBrushL = new SolidColorBrush(Color.FromRgb(0x0D, 0x5A, 0x28));
    private static readonly IBrush HookTextDimBrushL = new SolidColorBrush(Color.FromRgb(0x5A, 0x70, 0x80));
    private static readonly IBrush HookTextUnsatisfiedBrushL = new SolidColorBrush(Color.FromRgb(0xCC, 0x00, 0x00));
    // Hook toggle icon (light)
    private static readonly IBrush HookToggleBgL = new SolidColorBrush(Color.FromArgb(0x60, 0x88, 0xAA, 0xCC));
    private static readonly IBrush HookToggleActiveBgL = new SolidColorBrush(Color.FromArgb(0xA0, 0x11, 0x66, 0xFF));
    private static readonly IBrush HookToggleTextL = new SolidColorBrush(Color.FromRgb(0x22, 0x44, 0x66));
    // Resize handle (light)
    private static readonly IBrush ResizeHandleBrushL = new SolidColorBrush(Color.FromArgb(0x80, 0x77, 0x88, 0xAA));
    // Minimize-to-inline button (light)
    private static readonly IBrush MinimizeBtnBgL = new SolidColorBrush(Color.FromArgb(0x60, 0x44, 0x88, 0x22));
    private static readonly IBrush MinimizeBtnTextL = new SolidColorBrush(Color.FromRgb(0x22, 0x55, 0x00));
    // Function-template (light)
    private static readonly IBrush FtFillL = new SolidColorBrush(Color.FromRgb(0xF6, 0xEE, 0xFF));
    private static readonly IBrush FtHeaderFillL = new SolidColorBrush(Color.FromRgb(0xC8, 0xA0, 0xE0));
    private static readonly IBrush FtBorderBrushL = new SolidColorBrush(Color.FromRgb(0x77, 0x22, 0xCC));
    private static readonly IBrush FtTextBrushL = new SolidColorBrush(Color.FromRgb(0x2A, 0x00, 0x50));
    private static readonly IBrush FtHookTextBrushL = new SolidColorBrush(Color.FromRgb(0x55, 0x11, 0xAA));
    private static readonly IBrush FtCountTextBrushL = new SolidColorBrush(Color.FromArgb(0xA0, 0x66, 0x44, 0xAA));
    // Start (light) — pale amber fill, dark amber border, no neon glow
    private static readonly IBrush StartFillL = new SolidColorBrush(Color.FromRgb(0xFF, 0xF0, 0xD9)); // pale cream-amber
    private static readonly IBrush StartBorderBrushL = new SolidColorBrush(Color.FromRgb(0xCC, 0x66, 0x00)); // rich amber border
    private static readonly Color StartGlowColorL = Color.FromRgb(0xBB, 0x55, 0x00);                      // subdued amber glow
    // FunctionParameter / Function Variable (light) — same amber hue family as Start
    private static readonly IBrush FpFillL = new SolidColorBrush(Color.FromRgb(0xFF, 0xF0, 0xD9)); // pale cream-amber body
    private static readonly IBrush FpHeaderFillL = new SolidColorBrush(Color.FromRgb(0xE8, 0x9A, 0x1A)); // warm amber header
    private static readonly IBrush FpBorderBrushL = new SolidColorBrush(Color.FromRgb(0xB8, 0x62, 0x00)); // dark amber border
    private static readonly IBrush FpTextBrushL = new SolidColorBrush(Color.FromRgb(0x33, 0x1A, 0x00)); // near-black brown text
    private static readonly Color FpGlowColorL = Color.FromRgb(0xBB, 0x55, 0x00);                      // subdued amber glow
    // Function-instance (light)
    private static readonly IBrush FiFillL = new SolidColorBrush(Color.FromRgb(0xE8, 0xFF, 0xF8));
    private static readonly IBrush FiHeaderFillL = new SolidColorBrush(Color.FromRgb(0x7B, 0xCF, 0xC0));
    private static readonly IBrush FiBorderBrushL = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0x6B));
    private static readonly IBrush FiSelBorderBrushL = new SolidColorBrush(Color.FromRgb(0x00, 0x55, 0x48));
    private static readonly IBrush FiTextBrushL = new SolidColorBrush(Color.FromRgb(0x00, 0x33, 0x28));
    private static readonly IBrush FiSubTextBrushL = new SolidColorBrush(Color.FromArgb(0xC0, 0x3A, 0x55, 0x50));
    private static readonly IBrush FiHookTextBrushL = new SolidColorBrush(Color.FromRgb(0x00, 0x57, 0x4E));
    // Glow colours (light mode — more subdued than dark-mode neons)
    private static readonly Color NodeGlowColorL = Color.FromRgb(0x00, 0x66, 0xBB);
    private static readonly Color FtGlowColorL = Color.FromRgb(0x88, 0x22, 0xCC);
    private static readonly Color FiGlowColorL = Color.FromRgb(0x00, 0x7A, 0x6B);
    private static readonly Color GhostGlowColorL = Color.FromRgb(0x33, 0x77, 0xBB);
    private static readonly Color LinkGlowColorL = Color.FromRgb(0x00, 0x66, 0xBB);

    // Function-template container box
    private static readonly IBrush FtFill = new SolidColorBrush(Color.FromRgb(0x20, 0x12, 0x38));
    private static readonly IBrush FtHeaderFill = new SolidColorBrush(Color.FromRgb(0x4A, 0x28, 0x6E));
    private static readonly IBrush FtBorderBrush = new SolidColorBrush(Color.FromRgb(0xAA, 0x55, 0xFF)); // vivid purple
    private static readonly IBrush FtSelBorderBrush = Brushes.DodgerBlue;
    private static readonly IBrush FtTextBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xCC, 0xFF));
    private static readonly IBrush FtHookTextBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xAA, 0xFF));
    private static readonly IBrush FtCountTextBrush = new SolidColorBrush(Color.FromArgb(0x90, 0xCC, 0xAA, 0xFF));
    private static readonly DashStyle FtBorderDash = new DashStyle([8, 3], 0);
    private const double FtHeaderHeight = 28.0;
    private const double FtHookRowHeight = 16.0;
    private const double FtNameFontSize = 11.0;
    private const double FtCornerRadius = 6.0;
    // Function-instance box (teal/green palette, solid border to distinguish from template)
    private static readonly IBrush FiFill = new SolidColorBrush(Color.FromRgb(0x07, 0x24, 0x24));
    private static readonly IBrush FiHeaderFill = new SolidColorBrush(Color.FromRgb(0x0E, 0x4A, 0x44));
    private static readonly IBrush FiBorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0xCC)); // vivid mint-teal
    private static readonly IBrush FiSelBorderBrush = new SolidColorBrush(Color.FromRgb(0x24, 0xCF, 0xCA));
    private static readonly IBrush FiTextBrush = new SolidColorBrush(Color.FromRgb(0xB2, 0xFF, 0xF0));
    private static readonly IBrush FiSubTextBrush = new SolidColorBrush(Color.FromArgb(0xB0, 0x80, 0xE8, 0xD0));
    private static readonly IBrush FiHookTextBrush = new SolidColorBrush(Color.FromRgb(0x80, 0xCB, 0xC4));
    private const double FiCornerRadius = 6.0;
    // Entry-node highlight: gold ring + label (shown when viewing InternalModules of a FunctionTemplate)
    private static readonly IBrush EntryNodeRingBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD0, 0x00));
    private static readonly IBrush EntryNodeLabelBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD0, 0x00));
    /// <summary>Darker amber used for the "▶ Entry Point" badge in light mode.</summary>
    private static readonly IBrush EntryNodeLabelBrushL = new SolidColorBrush(Color.FromRgb(0x88, 0x55, 0x00));
    private const double EntryNodeRingExtra = 3.0;   // px of expansion each side beyond node rect
    private const double EntryNodeRingThick = 2.5;   // pen width of the outer ring
    private const double EntryNodeLabelFontSize = 8.0;   // font size for the "▶ Entry Point" badge
    // Graph-paper background grid
    private static readonly Pen GridPen = new Pen(new SolidColorBrush(Color.FromArgb(0x38, 0x55, 0x77, 0xAA)), 0.5);
    private static readonly Pen GridPenLight = new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0x88, 0xAA, 0xCC)), 0.5);
    /// <summary>1 cm expressed in Avalonia logical pixels (96 DPI basis).</summary>
    private const double GridSpacingDip = 96.0 / 2.54;
    // Neon glow colours — applied as outward-expanding alpha halos in DrawRectGlow / DrawEllipseGlow.
    private static readonly Color NodeGlowColor = Color.FromRgb(0x00, 0xCC, 0xFF); // electric cyan
    private static readonly Color StartGlowColor = Color.FromRgb(0xFF, 0x88, 0x00); // vivid orange
    private static readonly Color FtGlowColor = Color.FromRgb(0xAA, 0x44, 0xFF); // neon purple
    private static readonly Color FiGlowColor = Color.FromRgb(0x00, 0xFF, 0xCC); // mint-teal
    private static readonly Color CommentGlowColor = Color.FromRgb(0xFF, 0xD7, 0x00); // gold
    private static readonly Color GhostGlowColor = Color.FromRgb(0x44, 0x99, 0xDD); // steel-blue
    private static readonly Color LinkGlowColor = Color.FromRgb(0x00, 0xCC, 0xFF); // cyan
    private static readonly Color LinkSelGlowColor = Color.FromRgb(0xFF, 0x55, 0x00); // orange-red
    private static readonly Color SelectionGlowColor = Color.FromRgb(0x22, 0xAA, 0xFF); // bright blue (selected objects)

    // ── Drawing constants ─────────────────────────────────────────────────
    private const double NodeCornerRadius = 4.0;
    private const double NodeBorderThickness = 2.0;
    private const double LinkThickness = 2.0;
    private const double ArrowSize = 10.0;
    private const double NodeFontSize = 12.0;
    private const double StartFontSize = 11.0;
    private const double CommentFontSize = 11.5;
    private const double CommentPadding = 6.0;
    /// <summary>Size of the dog-ear fold cut at the top-right corner of a sticky note.</summary>
    private const double CommentFoldSize = 22.0;
    /// <summary>Height of the adhesive-tab band drawn at the top of the sticky note.</summary>
    private const double CommentHeaderHeight = 20.0;
    /// <summary>Vertical spacing between faint ruled lines on the note body.</summary>
    private const double CommentRuleSpacing = 17.0;
    // Hook layout
    private const double NodeHeaderHeight = 28.0;
    private const double HookRowHeight = 16.0;
    private const double HookDotRadius = 3.5;
    private const double HookFontSize = 10.0;
    private const double NodeMinWidth = 120.0;
    // Hook toggle icon button in the node header top-right
    private const double HookToggleIconSize = NodeHeaderHeight - 8.0;
    // Resize handle: square target area at node bottom-right corner
    private const double ResizeHandleSize = 14.0;
    // Minimize-to-inline button on BasicParameter node header top-left
    private const double InlineMinimizeButtonSize = NodeHeaderHeight - 8.0;
    // Multi-link destination index label
    private const double LinkIndexFontSize = 10.0;
    private const double LinkHitTolerance = 6.0;
    // Canvas scaling
    private const double ScaleStep = 0.10;
    private const double ScaleMin = 0.10;
    private const double ScaleMax = 4.0;
    // Auto-scroll while dragging: activate within this many screen-pixels from the viewport edge.
    private const double AutoScrollZone = 48.0 * 2.0;
    /// <summary>Maximum scroll delta (screen pixels) applied per pointer-move event at the very edge.</summary>
    private const double AutoScrollSpeed = 14.0 / 2.0;

    private static readonly Typeface DefaultTypeface = new Typeface("Segoe UI, Arial, sans-serif");
    
}
