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
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.VisualTree;
using XTMF2;
using XTMF2.Editing;
using XTMF2.GUI.ViewModels;
using XTMF2.ModelSystemConstruct;

namespace XTMF2.GUI.Controls;

/// <summary>
/// A custom Avalonia <see cref="Control"/> that renders the model system canvas
/// using a <see cref="DrawingContext"/>.
/// <para>
/// Nodes are drawn as rounded rectangles, Starts as circles, and Links as lines.
/// Click a node or start to select it; click empty space to deselect.
/// </para>
/// </summary>
public sealed class ModelSystemCanvas : Control
{
    // ── Brushes / pens (shared, immutable) ───────────────────────────────
    private static readonly IBrush CanvasBackground      = new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x18)); // matches DlgBg dark token
    private static readonly IBrush CanvasBackgroundLight = new SolidColorBrush(Color.FromRgb(0xF0, 0xF4, 0xF8));
    private static readonly IBrush NodeFill           = new SolidColorBrush(Color.FromRgb(0x0E, 0x22, 0x38)); // deep dark blue
    private static readonly IBrush NodeBorderBrush    = new SolidColorBrush(Color.FromRgb(0x00, 0xCC, 0xFF)); // neon cyan
    private static readonly IBrush NodeSelBrush       = Brushes.DodgerBlue;
    private static readonly IBrush NodeTextBrush      = Brushes.White;
    private static readonly IBrush StartFill          = new SolidColorBrush(Color.FromRgb(0xFF, 0x77, 0x00)); // vivid orange
    private static readonly IBrush StartSelFill       = Brushes.DodgerBlue;
    private static readonly IBrush StartTextBrush      = Brushes.White;
    private static readonly IBrush StartTextBrushLight  = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E));
    private static readonly IBrush LinkBrush          = new SolidColorBrush(Color.FromRgb(0x22, 0xBB, 0xDD)); // teal-cyan
    private static readonly IBrush LinkSelBrush       = Brushes.OrangeRed;
    private static readonly IBrush PendingLinkBrush   = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71));
    private static readonly DashStyle PendingLinkDash = new DashStyle([6, 4], 0);
    // Ghost node styling
    private static readonly IBrush GhostNodeFill      = new SolidColorBrush(Color.FromArgb(0x50, 0x0E, 0x22, 0x38));
    private static readonly IBrush GhostNodeBorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x99, 0xDD)); // steel-blue neon
    private static readonly IBrush GhostNodeSelBrush  = Brushes.DodgerBlue;
    private static readonly DashStyle GhostNodeDash   = new DashStyle([6, 4], 0);

    // Scripted-parameter syntax-highlight token colours
    private static readonly IBrush ScriptVarKnownBrush   = new SolidColorBrush(Color.FromRgb(0x44, 0xDD, 0x88)); // known variable → green
    private static readonly IBrush ScriptVarUnknownBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44)); // unrecognised identifier → red
    private static readonly IBrush ScriptOperatorBrush   = new SolidColorBrush(Color.FromRgb(0xAA, 0xBB, 0xCC)); // operators / punctuation → steel-blue
    private static readonly IBrush ScriptNumberBrush     = new SolidColorBrush(Color.FromRgb(0xB8, 0xD7, 0xFF)); // numeric literals → light blue
    private static readonly IBrush ScriptStringBrush     = new SolidColorBrush(Color.FromRgb(0xFF, 0xB8, 0x60)); // string literals → orange
    private static readonly IBrush ScriptKeywordBrush    = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x82)); // true / false → gold

    // Parameter value row
    private static readonly IBrush ParamValueTextBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x82));
    private static readonly IBrush ParamValueBg        = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));

    // Comment block colours (sticky-note style)
    private static readonly IBrush CommentFill        = new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xF2, 0x90));
    private static readonly IBrush CommentSelFill     = new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xE0, 0x50));
    private static readonly IBrush CommentBorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xBB, 0x00));   // warm gold
    private static readonly IBrush CommentSelBorder   = Brushes.DodgerBlue;
    private static readonly IBrush CommentTextBrush   = new SolidColorBrush(Color.FromRgb(0x22, 0x1E, 0x00));
    /// <summary>Slightly deeper/more saturated yellow for the adhesive-tab band at the top of the sticky note.</summary>
    private static readonly IBrush CommentHeaderBrush = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xD5, 0x1A));
    /// <summary>Cream colour for the fold-flap back face (the bit of paper you see when the corner is turned).</summary>
    private static readonly IBrush CommentFoldBackBrush = new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xFA, 0xD0));
    /// <summary>Semi-transparent black drop shadow for the sticky note.</summary>
    private static readonly IBrush CommentShadowBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x00, 0x00, 0x00));
    /// <summary>Faint pen for horizontal ruled lines on the note body.</summary>
    private static readonly Pen    CommentRulePen     = new Pen(new SolidColorBrush(Color.FromArgb(0x50, 0xA0, 0x8A, 0x00)), 0.6);
    // Hook colours
    private static readonly IBrush HookConnectedBrush   = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71));
    private static readonly IBrush HookUnconnectedBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x66, 0x77));
    private static readonly IBrush HookDividerBrush     = new SolidColorBrush(Color.FromRgb(0x44, 0x55, 0x66));
    private static readonly IBrush HookTextConnBrush    = new SolidColorBrush(Color.FromRgb(0xAA, 0xEE, 0xBB));
    private static readonly IBrush HookTextDimBrush     = new SolidColorBrush(Color.FromRgb(0x77, 0x88, 0x99));
    // Unsatisfied required hook (Single / AtLeastOne with no connection)
    private static readonly IBrush HookUnsatisfiedBrush    = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
    private static readonly IBrush HookTextUnsatisfiedBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x99, 0x88));
    private static readonly IBrush HookUnsatisfiedRowBg    = new SolidColorBrush(Color.FromArgb(0x30, 0xE7, 0x4C, 0x3C));
    // Hook toggle icon
    private static readonly IBrush HookToggleBg         = new SolidColorBrush(Color.FromArgb(0x60, 0x55, 0x88, 0xCC));
    private static readonly IBrush HookToggleActiveBg   = new SolidColorBrush(Color.FromArgb(0x90, 0x33, 0x99, 0xFF));
    private static readonly IBrush HookToggleText       = new SolidColorBrush(Color.FromRgb(0xBB, 0xCC, 0xEE));
    // Resize handle
    private static readonly IBrush ResizeHandleBrush    = new SolidColorBrush(Color.FromArgb(0x80, 0xAA, 0xBB, 0xCC));
    // Inline parameter hook row tint
    private static readonly IBrush InlineParamRowBg     = new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xE0, 0x80));
    // Minimize-to-inline button on BasicParameter nodes
    private static readonly IBrush MinimizeBtnBg        = new SolidColorBrush(Color.FromArgb(0x60, 0x88, 0xCC, 0x55));
    private static readonly IBrush MinimizeBtnText      = new SolidColorBrush(Color.FromRgb(0xCC, 0xFF, 0xAA));
    // Rubber-band (Ctrl+drag) multi-selection rectangle
    private static readonly IBrush SelectionRectFill = new SolidColorBrush(Color.FromArgb(0x2E, 0x44, 0x88, 0xFF));
    private static readonly DashStyle SelectionRectDash = new DashStyle([5, 4], 0);

    // ── Light-mode palette ────────────────────────────────────────────────
    // Each entry below is the light-mode counterpart of a dark-mode brush above.
    // Render methods select between the two sets via _isLight.
    private static readonly IBrush NodeFillL             = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)); // white card
    private static readonly IBrush NodeBorderBrushL      = new SolidColorBrush(Color.FromRgb(0x00, 0x66, 0xBB)); // strong blue
    private static readonly IBrush NodeTextBrushL        = new SolidColorBrush(Color.FromRgb(0x0D, 0x1B, 0x2A)); // near-black
    private static readonly IBrush GhostNodeFillL        = new SolidColorBrush(Color.FromArgb(0x50, 0xB4, 0xC8, 0xDC));
    private static readonly IBrush GhostNodeBorderBrushL = new SolidColorBrush(Color.FromRgb(0x33, 0x77, 0xBB));
    private static readonly IBrush LinkBrushL            = new SolidColorBrush(Color.FromRgb(0x00, 0x55, 0xAA));
    private static readonly IBrush PendingLinkBrushL     = new SolidColorBrush(Color.FromRgb(0x1A, 0x7A, 0x40));
    // Script syntax highlight (light)
    private static readonly IBrush ScriptVarKnownBrushL   = new SolidColorBrush(Color.FromRgb(0x1A, 0x7A, 0x40));
    private static readonly IBrush ScriptVarUnknownBrushL = new SolidColorBrush(Color.FromRgb(0xCC, 0x00, 0x00));
    private static readonly IBrush ScriptOperatorBrushL   = new SolidColorBrush(Color.FromRgb(0x44, 0x55, 0x66));
    private static readonly IBrush ScriptNumberBrushL     = new SolidColorBrush(Color.FromRgb(0x00, 0x44, 0xAA));
    private static readonly IBrush ScriptStringBrushL     = new SolidColorBrush(Color.FromRgb(0x8B, 0x45, 0x00));
    private static readonly IBrush ScriptKeywordBrushL    = new SolidColorBrush(Color.FromRgb(0x7B, 0x50, 0x00));
    // Parameter value row (light)
    private static readonly IBrush ParamValueTextBrushL   = new SolidColorBrush(Color.FromRgb(0x6B, 0x4A, 0x00));
    private static readonly IBrush ParamValueBgL          = new SolidColorBrush(Color.FromArgb(0x18, 0x00, 0x00, 0x00));
    // Hook row (light)
    private static readonly IBrush HookConnectedBrushL    = new SolidColorBrush(Color.FromRgb(0x1A, 0x7A, 0x40));
    private static readonly IBrush HookUnconnectedBrushL  = new SolidColorBrush(Color.FromRgb(0x88, 0x99, 0xAA));
    private static readonly IBrush HookUnsatisfiedBrushL  = new SolidColorBrush(Color.FromRgb(0xCC, 0x00, 0x00));
    private static readonly IBrush HookDividerBrushL      = new SolidColorBrush(Color.FromRgb(0xBC, 0xCD, 0xE0));
    private static readonly IBrush HookTextConnBrushL     = new SolidColorBrush(Color.FromRgb(0x0D, 0x5A, 0x28));
    private static readonly IBrush HookTextDimBrushL      = new SolidColorBrush(Color.FromRgb(0x5A, 0x70, 0x80));
    private static readonly IBrush HookTextUnsatisfiedBrushL = new SolidColorBrush(Color.FromRgb(0xCC, 0x00, 0x00));
    // Hook toggle icon (light)
    private static readonly IBrush HookToggleBgL          = new SolidColorBrush(Color.FromArgb(0x60, 0x88, 0xAA, 0xCC));
    private static readonly IBrush HookToggleActiveBgL    = new SolidColorBrush(Color.FromArgb(0xA0, 0x11, 0x66, 0xFF));
    private static readonly IBrush HookToggleTextL        = new SolidColorBrush(Color.FromRgb(0x22, 0x44, 0x66));
    // Resize handle (light)
    private static readonly IBrush ResizeHandleBrushL     = new SolidColorBrush(Color.FromArgb(0x80, 0x77, 0x88, 0xAA));
    // Minimize-to-inline button (light)
    private static readonly IBrush MinimizeBtnBgL         = new SolidColorBrush(Color.FromArgb(0x60, 0x44, 0x88, 0x22));
    private static readonly IBrush MinimizeBtnTextL       = new SolidColorBrush(Color.FromRgb(0x22, 0x55, 0x00));
    // Function-template (light)
    private static readonly IBrush FtFillL          = new SolidColorBrush(Color.FromRgb(0xF6, 0xEE, 0xFF));
    private static readonly IBrush FtHeaderFillL    = new SolidColorBrush(Color.FromRgb(0xC8, 0xA0, 0xE0));
    private static readonly IBrush FtBorderBrushL   = new SolidColorBrush(Color.FromRgb(0x77, 0x22, 0xCC));
    private static readonly IBrush FtTextBrushL     = new SolidColorBrush(Color.FromRgb(0x2A, 0x00, 0x50));
    private static readonly IBrush FtHookTextBrushL = new SolidColorBrush(Color.FromRgb(0x55, 0x11, 0xAA));
    private static readonly IBrush FtCountTextBrushL = new SolidColorBrush(Color.FromArgb(0xA0, 0x66, 0x44, 0xAA));
    // Function-instance (light)
    private static readonly IBrush FiFillL           = new SolidColorBrush(Color.FromRgb(0xE8, 0xFF, 0xF8));
    private static readonly IBrush FiHeaderFillL     = new SolidColorBrush(Color.FromRgb(0x7B, 0xCF, 0xC0));
    private static readonly IBrush FiBorderBrushL    = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0x6B));
    private static readonly IBrush FiSelBorderBrushL = new SolidColorBrush(Color.FromRgb(0x00, 0x55, 0x48));
    private static readonly IBrush FiTextBrushL      = new SolidColorBrush(Color.FromRgb(0x00, 0x33, 0x28));
    private static readonly IBrush FiSubTextBrushL   = new SolidColorBrush(Color.FromArgb(0xC0, 0x3A, 0x55, 0x50));
    private static readonly IBrush FiHookTextBrushL  = new SolidColorBrush(Color.FromRgb(0x00, 0x57, 0x4E));
    // Glow colours (light mode — more subdued than dark-mode neons)
    private static readonly Color NodeGlowColorL  = Color.FromRgb(0x00, 0x66, 0xBB);
    private static readonly Color FtGlowColorL   = Color.FromRgb(0x88, 0x22, 0xCC);
    private static readonly Color FiGlowColorL   = Color.FromRgb(0x00, 0x7A, 0x6B);
    private static readonly Color GhostGlowColorL = Color.FromRgb(0x33, 0x77, 0xBB);
    private static readonly Color LinkGlowColorL  = Color.FromRgb(0x00, 0x66, 0xBB);

    // Function-template container box
    private static readonly IBrush FtFill            = new SolidColorBrush(Color.FromRgb(0x20, 0x12, 0x38));
    private static readonly IBrush FtHeaderFill      = new SolidColorBrush(Color.FromRgb(0x4A, 0x28, 0x6E));
    private static readonly IBrush FtBorderBrush     = new SolidColorBrush(Color.FromRgb(0xAA, 0x55, 0xFF)); // vivid purple
    private static readonly IBrush FtSelBorderBrush  = Brushes.DodgerBlue;
    private static readonly IBrush FtTextBrush       = new SolidColorBrush(Color.FromRgb(0xDD, 0xCC, 0xFF));
    private static readonly IBrush FtHookTextBrush   = new SolidColorBrush(Color.FromRgb(0xCC, 0xAA, 0xFF));
    private static readonly IBrush FtCountTextBrush  = new SolidColorBrush(Color.FromArgb(0x90, 0xCC, 0xAA, 0xFF));
    private static readonly DashStyle FtBorderDash   = new DashStyle([8, 3], 0);
    private const double FtHeaderHeight  = 28.0;
    private const double FtHookRowHeight = 16.0;
    private const double FtNameFontSize  = 11.0;
    private const double FtCornerRadius  = 6.0;
    // Function-instance box (teal/green palette, solid border to distinguish from template)
    private static readonly IBrush FiFill           = new SolidColorBrush(Color.FromRgb(0x07, 0x24, 0x24));
    private static readonly IBrush FiHeaderFill     = new SolidColorBrush(Color.FromRgb(0x0E, 0x4A, 0x44));
    private static readonly IBrush FiBorderBrush    = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0xCC)); // vivid mint-teal
    private static readonly IBrush FiSelBorderBrush = new SolidColorBrush(Color.FromRgb(0x24, 0xCF, 0xCA));
    private static readonly IBrush FiTextBrush      = new SolidColorBrush(Color.FromRgb(0xB2, 0xFF, 0xF0));
    private static readonly IBrush FiSubTextBrush   = new SolidColorBrush(Color.FromArgb(0xB0, 0x80, 0xE8, 0xD0));
    private static readonly IBrush FiHookTextBrush  = new SolidColorBrush(Color.FromRgb(0x80, 0xCB, 0xC4));
    private const double FiCornerRadius = 6.0;
    // Entry-node highlight: gold ring + label (shown when viewing InternalModules of a FunctionTemplate)
    private static readonly IBrush EntryNodeRingBrush  = new SolidColorBrush(Color.FromRgb(0xFF, 0xD0, 0x00));
    private static readonly IBrush EntryNodeLabelBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD0, 0x00));
    private const double EntryNodeRingExtra     = 3.0;   // px of expansion each side beyond node rect
    private const double EntryNodeRingThick     = 2.5;   // pen width of the outer ring
    private const double EntryNodeLabelFontSize = 8.0;   // font size for the "▶ Entry Point" badge
    // Graph-paper background grid
    private static readonly Pen GridPen      = new Pen(new SolidColorBrush(Color.FromArgb(0x38, 0x55, 0x77, 0xAA)), 0.5);
    private static readonly Pen GridPenLight = new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0x88, 0xAA, 0xCC)), 0.5);
    /// <summary>1 cm expressed in Avalonia logical pixels (96 DPI basis).</summary>
    private const double GridSpacingDip = 96.0 / 2.54;
    // Neon glow colours — applied as outward-expanding alpha halos in DrawRectGlow / DrawEllipseGlow.
    private static readonly Color NodeGlowColor      = Color.FromRgb(0x00, 0xCC, 0xFF); // electric cyan
    private static readonly Color StartGlowColor     = Color.FromRgb(0xFF, 0x88, 0x00); // vivid orange
    private static readonly Color FtGlowColor        = Color.FromRgb(0xAA, 0x44, 0xFF); // neon purple
    private static readonly Color FiGlowColor        = Color.FromRgb(0x00, 0xFF, 0xCC); // mint-teal
    private static readonly Color CommentGlowColor   = Color.FromRgb(0xFF, 0xD7, 0x00); // gold
    private static readonly Color GhostGlowColor     = Color.FromRgb(0x44, 0x99, 0xDD); // steel-blue
    private static readonly Color LinkGlowColor      = Color.FromRgb(0x00, 0xCC, 0xFF); // cyan
    private static readonly Color LinkSelGlowColor   = Color.FromRgb(0xFF, 0x55, 0x00); // orange-red
    private static readonly Color SelectionGlowColor = Color.FromRgb(0x22, 0xAA, 0xFF); // bright blue (selected objects)

    // ── Drawing constants ─────────────────────────────────────────────────
    private const double NodeCornerRadius    = 4.0;
    private const double NodeBorderThickness = 2.0;
    private const double LinkThickness       = 2.0;
    private const double ArrowSize           = 10.0;
    private const double NodeFontSize        = 12.0;
    private const double StartFontSize       = 11.0;
    private const double CommentFontSize     = 11.5;
    private const double CommentPadding      = 6.0;
    /// <summary>Size of the dog-ear fold cut at the top-right corner of a sticky note.</summary>
    private const double CommentFoldSize     = 22.0;
    /// <summary>Height of the adhesive-tab band drawn at the top of the sticky note.</summary>
    private const double CommentHeaderHeight = 20.0;
    /// <summary>Vertical spacing between faint ruled lines on the note body.</summary>
    private const double CommentRuleSpacing  = 17.0;
    // Hook layout
    private const double NodeHeaderHeight   = 28.0;
    private const double HookRowHeight      = 16.0;
    private const double HookDotRadius      = 3.5;
    private const double HookFontSize       = 10.0;
    private const double NodeMinWidth       = 120.0;
    // Hook toggle icon button in the node header top-right
    private const double HookToggleIconSize = NodeHeaderHeight - 8.0;
    // Resize handle: square target area at node bottom-right corner
    private const double ResizeHandleSize         = 14.0;
    // Minimize-to-inline button on BasicParameter node header top-left
    private const double InlineMinimizeButtonSize = NodeHeaderHeight - 8.0;
    // Multi-link destination index label
    private const double LinkIndexFontSize = 9.0;
    // Elbow routing
    private const double ElbowMinOffset   = 16.0;
    private const double MaxStraightLineDistance = 50.0;
    private const double LinkHitTolerance = 6.0;
    // Canvas scaling
    private const double ScaleStep = 0.10;
    private const double ScaleMin  = 0.10;
    private const double ScaleMax  = 4.0;

    private static readonly Typeface DefaultTypeface = new Typeface("Segoe UI, Arial, sans-serif");

    // ── ViewModel ─────────────────────────────────────────────────────────
    private ModelSystemEditorViewModel?      _vm;
    /// <summary>Set at the start of each <see cref="Render"/> call; <c>true</c> when the app is in light mode.</summary>
    private bool _isLight;
    /// <summary>Tracks the FunctionTemplate we are currently subscribed to for PropertyChanged,
    /// so we can unsubscribe when navigating away.</summary>
    private FunctionTemplateViewModel? _subscribedCurrentFunctionTemplate;

    // ── Per-frame hook anchor cache (rebuilt in BuildHookAnchorCache) ─────
    private readonly Dictionary<(NodeViewModel, NodeHook), Point>
        _hookAnchors = new();
    private readonly Dictionary<NodeViewModel, IReadOnlyList<NodeHook>>
        _nodeVisibleHooks = new();
    private readonly Dictionary<NodeViewModel, HashSet<NodeHook>>
        _nodeConnectedHooks = new();
    /// <summary>Anchor points for FunctionInstance FunctionParameterHook rows (right-edge dot).</summary>
    private readonly Dictionary<(FunctionInstanceViewModel, FunctionParameterHook), Point>
        _fiHookAnchors = new();
    /// <summary>Tracks which FunctionParameterHooks on each FunctionInstance have live links.</summary>
    private readonly Dictionary<FunctionInstanceViewModel, HashSet<FunctionParameterHook>>
        _fiConnectedHooks = new();

    // ── Inline parameter editor ───────────────────────────────────────────
    /// <summary>Overlay TextBox used for in-canvas parameter value editing.</summary>
    private readonly TextBox _inlineEditor;
    /// <summary>The node whose parameter value row is currently being edited, or <c>null</c> when idle.</summary>
    private NodeViewModel? _editingParamNode;
    /// <summary>Screen position and width of the inline editor overlay (set in <see cref="BeginParamEdit"/>).</summary>
    private double _editingParamEditorX, _editingParamEditorY, _editingParamEditorW;
    /// <summary>
    /// Transparent, non-interactive overlay that draws syntax-highlighted tokens
    /// on top of the scripted-parameter TextBox. Added to VisualChildren after
    /// <see cref="_inlineEditor"/> so it renders last (on top).
    /// </summary>
    private readonly ScriptSyntaxOverlay _scriptOverlay;
    /// <summary>
    /// Syntax-highlighted tokens for the scripted-parameter editor.
    /// Each entry is a (text segment, brush) pair; painted left-to-right inside the TextBox region.
    /// Empty when not editing a ScriptedParameter.
    /// </summary>
    private (string text, IBrush brush)[] _scriptTokens = Array.Empty<(string, IBrush)>();
    /// <summary>Internal ScrollViewer of <see cref="_inlineEditor"/>, cached to allow unsubscription.</summary>
    private ScrollViewer? _inlineEditorSv;
    /// <summary>PropertyChanged handler subscribed to <see cref="_inlineEditorSv"/> while editing a scripted parameter.</summary>
    private EventHandler<AvaloniaPropertyChangedEventArgs>? _inlineEditorSvHandler;

    /// <summary><c>true</c> while <see cref="CommitParamEdit"/> is executing, used to suppress re-entrant LostFocus commits.</summary>
    private bool _commitParamEditInProgress;

    // ── Scripted-parameter variable autocomplete dropdown ─────────────────
    /// <summary>Overlay border that contains the variable-name suggestion list.</summary>
    private readonly Border     _varDropdownBorder;
    /// <summary>Stack of <see cref="TextBlock"/> rows inside the dropdown.</summary>
    private readonly StackPanel _varDropdownStack;
    /// <summary><c>true</c> while the variable autocomplete dropdown is open.</summary>
    private bool _varDropdownVisible;
    /// <summary>Character offset in <see cref="TextBox.Text"/> where the current token starts.</summary>
    private int  _varTokenStart;
    /// <summary>Index of the currently highlighted row in the dropdown.</summary>
    private int  _varSelectedIndex;
    /// <summary>Maximum number of suggestions shown at once.</summary>
    private const int MaxVarDropdownItems = 8;

    // ── Inline comment editor ─────────────────────────────────────────────
    /// <summary>Overlay multi-line TextBox used for in-canvas comment block editing.</summary>
    private readonly TextBox _commentEditor;
    /// <summary>The comment block currently being edited, or <c>null</c> when idle.</summary>
    private CommentBlockViewModel? _editingCommentBlock;
    /// <summary>Model-space position and size of the comment editor overlay.</summary>
    private double _editingCommentEditorX, _editingCommentEditorY, _editingCommentEditorW, _editingCommentEditorH;

    // ── Inline name editor ───────────────────────────────────────────────────
    /// <summary>Overlay single-line TextBox used for renaming nodes and starts.</summary>
    private readonly TextBox _nameEditor;
    /// <summary>The element currently being renamed, or <c>null</c> when idle.</summary>
    private ICanvasElement? _editingNameElement;
    /// <summary>Model-space position and size of the name editor overlay.</summary>
    private double _nameEditorX, _nameEditorY, _nameEditorW, _nameEditorH;

    // ── Inlined BasicParameter caches (rebuilt by BuildHookAnchorCache) ───
    /// <summary>
    /// Maps (origin node, hook) → the BasicParameter node that is currently inlined
    /// into that hook row (node location is <see cref="Rectangle.Hidden"/>).
    /// </summary>
    private readonly Dictionary<(NodeViewModel, NodeHook), NodeViewModel>
        _hookInlinedParam = new();
    /// <summary>
    /// BasicParameter nodes that are visible on the canvas AND connected via a Single hook,
    /// so they can offer a "minimize to inline" button.
    /// </summary>
    private readonly HashSet<NodeViewModel> _canInlineNodes = new();

    // ── Canvas scale ───────────────────────────────────────────────────────
    private double _scale = 1.0;
    // ── Zoom control overlay ───────────────────────────────────────────────
    private readonly Border  _zoomBar;
    private readonly TextBox _zoomTextBox;
    private readonly Button  _zoomMinusBtn;
    private readonly Button  _zoomPlusBtn;
    private bool _zoomBarIsLight = false; // tracks last applied theme so we only update on change

    public ModelSystemCanvas()
    {
        Focusable = true;

        // Build the inline editor once; it lives as a visual child of this canvas.
        _inlineEditor = new TextBox
        {
            FontFamily        = new Avalonia.Media.FontFamily("Segoe UI, Arial, sans-serif"),
            FontSize          = HookFontSize,
            Foreground        = ParamValueTextBrush,
            Background        = new SolidColorBrush(Color.FromRgb(0x18, 0x28, 0x38)),
            BorderThickness   = new Thickness(1),
            BorderBrush       = new SolidColorBrush(Color.FromRgb(0x44, 0x88, 0xCC)),
            Padding           = new Thickness(4, 0, 4, 0),
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            IsVisible         = false,
        };
        _inlineEditor.KeyDown    += OnInlineEditorKeyDown;
        _inlineEditor.LostFocus  += OnInlineEditorLostFocus;
        _inlineEditor.TextChanged += OnInlineEditorTextChanged;

        LogicalChildren.Add(_inlineEditor);
        VisualChildren.Add(_inlineEditor);

        // Syntax-highlight overlay – must be added AFTER _inlineEditor so it
        // renders on top of the TextBox, not underneath it.
        _scriptOverlay = new ScriptSyntaxOverlay();
        LogicalChildren.Add(_scriptOverlay);
        VisualChildren.Add(_scriptOverlay);
        _varDropdownStack  = new StackPanel { Orientation = Orientation.Vertical };
        _varDropdownBorder = new Border
        {
            Child           = _varDropdownStack,
            Background      = new SolidColorBrush(Color.FromRgb(0x1E, 0x2E, 0x3E)),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0x44, 0x88, 0xCC)),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(3),
            IsVisible       = false,
        };
        LogicalChildren.Add(_varDropdownBorder);
        VisualChildren.Add(_varDropdownBorder);

        // Build the multi-line comment editor; Enter inserts a newline, Ctrl+Enter commits.
        _commentEditor = new TextBox
        {
            FontFamily      = new Avalonia.Media.FontFamily("Segoe UI, Arial, sans-serif"),
            FontSize        = CommentFontSize,
            Foreground      = CommentTextBrush,
            Background      = new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xF0, 0x96)),
            BorderThickness = new Thickness(1),
            BorderBrush     = CommentBorderBrush,
            Padding         = new Thickness(6, 4, 6, 4),
            AcceptsReturn   = true,
            TextWrapping    = TextWrapping.Wrap,
            IsVisible       = false,
        };
        _commentEditor.LostFocus += OnCommentEditorLostFocus;
        // Use the tunneling phase so Ctrl+Enter is intercepted before AcceptsReturn
        // consumes the Enter keystroke and marks the event Handled.
        _commentEditor.AddHandler(InputElement.KeyDownEvent, OnCommentEditorKeyDown,
                                  Avalonia.Interactivity.RoutingStrategies.Tunnel);
        LogicalChildren.Add(_commentEditor);
        VisualChildren.Add(_commentEditor);

        // Build the single-line name editor; Enter commits, Escape cancels.
        _nameEditor = new TextBox
        {
            FontFamily               = new Avalonia.Media.FontFamily("Segoe UI, Arial, sans-serif"),
            FontSize                 = NodeFontSize,
            Foreground               = NodeTextBrush,
            Background               = new SolidColorBrush(Color.FromRgb(0x1A, 0x2C, 0x40)),
            BorderThickness          = new Thickness(1),
            BorderBrush              = NodeSelBrush,
            Padding                  = new Thickness(4, 0, 4, 0),
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            IsVisible                = false,
        };
        _nameEditor.AddHandler(InputElement.KeyDownEvent, OnNameEditorKeyDown,
                               Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _nameEditor.LostFocus += OnNameEditorLostFocus;
        LogicalChildren.Add(_nameEditor);
        VisualChildren.Add(_nameEditor);

        // ── Zoom control (pinned to viewport bottom-right) ────────────────
        _zoomTextBox = new TextBox
        {
            FontFamily               = new Avalonia.Media.FontFamily("Segoe UI, Arial, sans-serif"),
            FontSize                 = 11,
            Foreground               = new SolidColorBrush(Color.FromRgb(0xEE, 0xFF, 0xFF)),
            Background               = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            BorderThickness          = new Thickness(0),
            Padding                  = new Thickness(4, 1, 4, 1),
            Width                    = 52,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Text                     = "100%",
        };
        _zoomTextBox.KeyDown   += OnZoomTextBoxKeyDown;
        _zoomTextBox.LostFocus += (_, _) => TryApplyZoomText();

        _zoomMinusBtn = new Button
        {
            Content         = "\u2212",   // − (minus sign)
            FontSize        = 13,
            Padding         = new Thickness(6, 1, 6, 1),
            Background      = Brushes.Transparent,
            Foreground      = new SolidColorBrush(Color.FromRgb(0xEE, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0),
        };
        _zoomMinusBtn.Click += (_, _) => ApplyScale(_scale - ScaleStep);

        _zoomPlusBtn = new Button
        {
            Content         = "+",
            FontSize        = 13,
            Padding         = new Thickness(6, 1, 6, 1),
            Background      = Brushes.Transparent,
            Foreground      = new SolidColorBrush(Color.FromRgb(0xEE, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0),
        };
        _zoomPlusBtn.Click += (_, _) => ApplyScale(_scale + ScaleStep);

        _zoomBar = new Border
        {
            Background      = new SolidColorBrush(Color.FromArgb(0xE6, 0x05, 0x05, 0x10)),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0x00, 0xD4, 0xFF)),
            BorderThickness = new Thickness(1.5),
            CornerRadius    = new CornerRadius(16),
            Padding         = new Thickness(4, 3),
            Child           = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing     = 0,
                Children    = { _zoomMinusBtn, _zoomTextBox, _zoomPlusBtn },
            },
        };
        LogicalChildren.Add(_zoomBar);
        VisualChildren.Add(_zoomBar);

        // Suppress Avalonia's automatic context-menu-on-right-click behaviour.
        // The canvas manages the context menu manually in OnPointerReleased so
        // that it only appears after a minimal-movement right-click, not after
        // a right-drag used to create a link connection.
        ContextRequested += SuppressContextRequested;
    }

    // ── Drag state ────────────────────────────────────────────────────────
    /// <summary>The element currently being dragged (left-button), or <c>null</c> when idle.</summary>
    private ICanvasElement? _dragging;
    /// <summary>Offset from the element's top-left corner to the pointer position at drag start.</summary>
    private Point _dragOffset;

    // ── Canvas pan state (left-drag on empty space) ───────────────────────
    /// <summary><c>true</c> while the user is panning by dragging empty canvas space.</summary>
    private bool _panning;
    /// <summary>Pointer position (in ScrollViewer coordinates) where the pan started.</summary>
    private Point _panStartScrollPos;
    /// <summary>ScrollViewer.Offset value at the moment the pan started.</summary>
    private Vector _panStartOffset;

    /// <summary>Returns the ancestor <see cref="ScrollViewer"/> that hosts this canvas, lazily resolved.</summary>
    private ScrollViewer? _scrollViewer;
    private ScrollViewer? GetScrollViewer()
    {
        if (_scrollViewer is null)
        {
            _scrollViewer = this.FindAncestorOfType<ScrollViewer>();
            if (_scrollViewer is not null)
                _scrollViewer.ScrollChanged += (_, _) => InvalidateMeasure();
        }
        return _scrollViewer;
    }

    // ── Resize drag state ─────────────────────────────────────────────────
    /// <summary>The node being resized, or <c>null</c> when not resizing.</summary>
    private ICanvasElement? _resizing;
    /// <summary>Pointer position at the start of the resize drag.</summary>
    private Point _resizeStartPos;
    /// <summary>Node rendered width at the start of the resize drag.</summary>
    private double _resizeStartW;
    /// <summary>Node rendered height at the start of the resize drag.</summary>
    private double _resizeStartH;

    // ── Link-creation drag state (right-button) ────────────────────────────
    /// <summary>The origin element for a pending link, or <c>null</c> when not drawing.</summary>
    private ICanvasElement? _linkOrigin;
    /// <summary>Current cursor position while drawing a pending link.</summary>
    private Point _linkCurrentPos;

    // ── Right-click context-menu tracking ────────────────────────────────
    /// <summary>Set when a right-button press is outstanding, so release can compare displacement.</summary>
    private bool            _rightClickPending;
    /// <summary>Canvas position where the right button was pressed.</summary>
    private Point           _rightClickPressPos;
    /// <summary>Canvas element (node / start / comment) under the right-button press, if any.</summary>
    private ICanvasElement? _rightClickElement;
    /// <summary>Link under the right-button press when no element was hit.</summary>
    private LinkViewModel?  _rightClickLink;
    /// <summary>Hook dot under the right-button press, if any (may be set alongside <see cref="_rightClickElement"/>).</summary>
    private (NodeViewModel Node, NodeHook Hook)? _rightClickHookHit;
    private (FunctionInstanceViewModel Fi, FunctionParameterHook Hook)? _rightClickFiHookHit;

    // ── Multi-selection set ───────────────────────────────────────────────
    /// <summary>
    /// All canvas elements currently in the extended multi-selection (each has
    /// <see cref="ICanvasElement.IsSelected"/> = <c>true</c>).
    /// </summary>
    private readonly HashSet<ICanvasElement> _multiSelection = new();
    /// <summary>Cursor model-coordinate recorded at the start of each group-drag frame, used to compute per-frame deltas.</summary>
    private Point _groupDragLastPos;

    // ── Rubber-band (Ctrl+drag) selection rectangle ───────────────────────
    /// <summary>Model-coord anchor of the in-progress Ctrl+drag selection rect, or <c>null</c> when idle.</summary>
    private Point? _selRectStart;
    /// <summary>Model-coord live end-point of the Ctrl+drag selection rect.</summary>
    private Point _selRectCurrent;

    // ── DataContext wiring ────────────────────────────────────────────────
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Detach();
        _vm = DataContext as ModelSystemEditorViewModel;
        Attach();
        InvalidateAndMeasure();
    }

    private void Attach()
    {
        if (_vm is null) return;
        _vm.Nodes.CollectionChanged             += OnCollectionChanged;
        _vm.Starts.CollectionChanged            += OnCollectionChanged;
        _vm.Links.CollectionChanged             += OnCollectionChanged;
        _vm.CommentBlocks.CollectionChanged     += OnCollectionChanged;
        _vm.GhostNodes.CollectionChanged        += OnCollectionChanged;
        _vm.FunctionTemplates.CollectionChanged += OnCollectionChanged;
        _vm.FunctionInstances.CollectionChanged += OnCollectionChanged;
        _vm.FunctionParameterVMs.CollectionChanged += OnCollectionChanged;
        _vm.PropertyChanged                     += OnViewModelPropertyChanged;

        foreach (var n in _vm.Nodes)             ((INotifyPropertyChanged)n).PropertyChanged += OnElementPropertyChanged;
        foreach (var s in _vm.Starts)            ((INotifyPropertyChanged)s).PropertyChanged += OnElementPropertyChanged;
        foreach (var l in _vm.Links)             ((INotifyPropertyChanged)l).PropertyChanged += OnElementPropertyChanged;
        foreach (var c in _vm.CommentBlocks)     ((INotifyPropertyChanged)c).PropertyChanged += OnElementPropertyChanged;
        foreach (var g in _vm.GhostNodes)        ((INotifyPropertyChanged)g).PropertyChanged += OnElementPropertyChanged;
        foreach (var f in _vm.FunctionTemplates) ((INotifyPropertyChanged)f).PropertyChanged += OnElementPropertyChanged;
        foreach (var fi in _vm.FunctionInstances) ((INotifyPropertyChanged)fi).PropertyChanged += OnElementPropertyChanged;
        foreach (var fp in _vm.FunctionParameterVMs) ((INotifyPropertyChanged)fp).PropertyChanged += OnElementPropertyChanged;
        _vm.RenderRequested += OnRenderRequested;
    }

    private void Detach()
    {
        if (_vm is null) return;
        _vm.RenderRequested -= OnRenderRequested;
        _vm.Nodes.CollectionChanged             -= OnCollectionChanged;
        _vm.Starts.CollectionChanged            -= OnCollectionChanged;
        _vm.Links.CollectionChanged             -= OnCollectionChanged;
        _vm.CommentBlocks.CollectionChanged     -= OnCollectionChanged;
        _vm.GhostNodes.CollectionChanged        -= OnCollectionChanged;
        _vm.FunctionTemplates.CollectionChanged -= OnCollectionChanged;
        _vm.FunctionInstances.CollectionChanged -= OnCollectionChanged;
        _vm.FunctionParameterVMs.CollectionChanged -= OnCollectionChanged;
        _vm.PropertyChanged                     -= OnViewModelPropertyChanged;

        foreach (var n in _vm.Nodes)             ((INotifyPropertyChanged)n).PropertyChanged -= OnElementPropertyChanged;
        foreach (var s in _vm.Starts)            ((INotifyPropertyChanged)s).PropertyChanged -= OnElementPropertyChanged;
        foreach (var l in _vm.Links)             ((INotifyPropertyChanged)l).PropertyChanged -= OnElementPropertyChanged;
        foreach (var c in _vm.CommentBlocks)     ((INotifyPropertyChanged)c).PropertyChanged -= OnElementPropertyChanged;
        foreach (var g in _vm.GhostNodes)        ((INotifyPropertyChanged)g).PropertyChanged -= OnElementPropertyChanged;
        foreach (var f in _vm.FunctionTemplates) ((INotifyPropertyChanged)f).PropertyChanged -= OnElementPropertyChanged;
        foreach (var fi in _vm.FunctionInstances) ((INotifyPropertyChanged)fi).PropertyChanged -= OnElementPropertyChanged;
        foreach (var fp in _vm.FunctionParameterVMs) ((INotifyPropertyChanged)fp).PropertyChanged -= OnElementPropertyChanged;

        if (_subscribedCurrentFunctionTemplate is not null)
        {
            ((INotifyPropertyChanged)_subscribedCurrentFunctionTemplate).PropertyChanged -= OnElementPropertyChanged;
            _subscribedCurrentFunctionTemplate = null;
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (INotifyPropertyChanged item in e.NewItems)
                item.PropertyChanged += OnElementPropertyChanged;
        if (e.OldItems is not null)
            foreach (INotifyPropertyChanged item in e.OldItems)
                item.PropertyChanged -= OnElementPropertyChanged;

        Avalonia.Threading.Dispatcher.UIThread.Post(InvalidateAndMeasure);
    }

    private void OnElementPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(InvalidateVisual);
    }

    private void OnRenderRequested(object? sender, EventArgs e)
        => Avalonia.Threading.Dispatcher.UIThread.Post(InvalidateVisual);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ModelSystemEditorViewModel.SelectedElement)
                           or nameof(ModelSystemEditorViewModel.SelectedLink)
                           or nameof(ModelSystemEditorViewModel.ShowAllHooks))
            Avalonia.Threading.Dispatcher.UIThread.Post(InvalidateAndMeasure);


        // When we navigate into or out of a FunctionTemplate, maintain a direct subscription
        // to the template VM so that property changes (e.g. EntryNode after undo) still
        // trigger InvalidateVisual even though the VM is no longer in FunctionTemplates.
        if (e.PropertyName is nameof(ModelSystemEditorViewModel.IsInsideFunctionTemplate) && _vm is not null)
        {
            if (_subscribedCurrentFunctionTemplate is not null)
            {
                ((INotifyPropertyChanged)_subscribedCurrentFunctionTemplate).PropertyChanged -= OnElementPropertyChanged;
                _subscribedCurrentFunctionTemplate = null;
            }
            if (_vm.CurrentFunctionTemplate is { } current)
            {
                _subscribedCurrentFunctionTemplate = current;
                ((INotifyPropertyChanged)current).PropertyChanged += OnElementPropertyChanged;
            }
            Avalonia.Threading.Dispatcher.UIThread.Post(InvalidateVisual);
        }
    }

    private void InvalidateAndMeasure()
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    // ── Layout ────────────────────────────────────────────────────────────
    protected override Size MeasureOverride(Size availableSize)
    {
        BuildHookAnchorCache();
        double maxX = 1600, maxY = 900;
        if (_vm is not null)
        {
            foreach (var n in _vm.Nodes)
            {
                if (n.IsInlined) continue;  // hidden nodes don't contribute to canvas extents
                maxX = Math.Max(maxX, n.X + NodeRenderWidth(n)  + 400);
                maxY = Math.Max(maxY, n.Y + NodeRenderHeight(n) + 400);
            }
            foreach (var s in _vm.Starts)
            {
                maxX = Math.Max(maxX, s.X + s.Diameter + 400);
                maxY = Math.Max(maxY, s.Y + s.Diameter + 400);
            }
            foreach (var c in _vm.CommentBlocks)
            {
                maxX = Math.Max(maxX, c.X + c.Width  + 400);
                maxY = Math.Max(maxY, c.Y + c.Height + 400);
            }
            foreach (var fi in _vm.FunctionInstances)
            {
                maxX = Math.Max(maxX, fi.X + fi.Width  + 400);
                maxY = Math.Max(maxY, fi.Y + fi.Height + 400);
            }
            foreach (var ft in _vm.FunctionTemplates)
            {
                maxX = Math.Max(maxX, ft.X + ft.Width  + 400);
                maxY = Math.Max(maxY, ft.Y + ft.Height + 400);
            }
            foreach (var g in _vm.GhostNodes)
            {
                maxX = Math.Max(maxX, g.X + g.Width  + 400);
                maxY = Math.Max(maxY, g.Y + g.Height + 400);
            }
        }
        // Measure the inline editor so Avalonia knows its desired size.
        if (_editingParamNode is not null)
        {
            _inlineEditor.Measure(new Size(_editingParamEditorW > 0 ? _editingParamEditorW * _scale
                                                                     : NodeRenderWidth(_editingParamNode) * _scale,
                                           HookRowHeight * _scale));
        }
        // Measure the syntax-highlight overlay (same footprint as the TextBox).
        if (_editingParamNode is { IsScriptedParameter: true })
            _scriptOverlay.Measure(new Size(_editingParamEditorW * _scale, HookRowHeight * _scale));
        // Measure the variable autocomplete dropdown.
        if (_varDropdownVisible && _editingParamNode is not null)
        {
            double ddW = Math.Max(180.0, _editingParamEditorW) * _scale;
            _varDropdownBorder.Measure(new Size(ddW, 200 * _scale));
        }
        // Measure the comment editor.
        if (_editingCommentBlock is not null)
        {
            _commentEditor.Measure(new Size(_editingCommentEditorW * _scale, _editingCommentEditorH * _scale));
        }
        // Measure the name editor.
        if (_editingNameElement is not null)
        {
            _nameEditor.Measure(new Size(_nameEditorW * _scale, _nameEditorH * _scale));
        }
        // Measure the zoom bar so ArrangeOverride can use its desired size.
        _zoomBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return new Size(maxX * _scale, maxY * _scale);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Position the inline editor at the stored row location (scaled to screen coords).
        if (_editingParamNode is not null && _editingParamEditorW > 0)
        {
            _inlineEditor.FontSize = HookFontSize * _scale;
            _inlineEditor.Arrange(new Rect(
                _editingParamEditorX * _scale,
                _editingParamEditorY * _scale,
                _editingParamEditorW * _scale,
                HookRowHeight * _scale));
        }
        // Position the syntax-highlight overlay exactly over the TextBox.
        if (_editingParamNode is { IsScriptedParameter: true })
        {
            _scriptOverlay.FontSize = HookFontSize * _scale;
            _scriptOverlay.Arrange(new Rect(
                _editingParamEditorX * _scale,
                _editingParamEditorY * _scale,
                _editingParamEditorW * _scale,
                HookRowHeight * _scale));
        }
        // Position the variable autocomplete dropdown just below the inline editor.
        if (_varDropdownVisible && _editingParamNode is not null)
        {
            double ddW = Math.Max(180.0, _editingParamEditorW) * _scale;
            double ddX = _editingParamEditorX * _scale;
            double ddY = (_editingParamEditorY + HookRowHeight) * _scale;
            // Scale font size and padding of every suggestion row to match the current zoom.
            double itemPadH = 8.0 * _scale;
            double itemPadV = 3.0 * _scale;
            foreach (var child in _varDropdownStack.Children)
            {
                if (child is TextBlock tb)
                {
                    tb.FontSize = HookFontSize * _scale;
                    tb.Padding  = new Thickness(itemPadH, itemPadV, itemPadH, itemPadV);
                }
            }
            _varDropdownBorder.Arrange(new Rect(ddX, ddY, ddW, _varDropdownBorder.DesiredSize.Height));
        }
        // Position the comment editor over the comment block being edited.
        if (_editingCommentBlock is not null)
        {
            _commentEditor.FontSize = CommentFontSize * _scale;
            _commentEditor.Arrange(new Rect(
                _editingCommentEditorX * _scale,
                _editingCommentEditorY * _scale,
                _editingCommentEditorW * _scale,
                _editingCommentEditorH * _scale));
        }
        // Position the name editor over the element header being renamed.
        if (_editingNameElement is not null)
        {
            _nameEditor.FontSize = (_editingNameElement is FunctionTemplateViewModel
                                  or FunctionInstanceViewModel
                                  or FunctionParameterViewModel
                ? FtNameFontSize : NodeFontSize) * _scale;
            _nameEditor.Arrange(new Rect(
                _nameEditorX * _scale,
                _nameEditorY * _scale,
                _nameEditorW * _scale,
                _nameEditorH * _scale));
        }
        // Pin the zoom control to the bottom-right of the visible viewport.
        var sv = GetScrollViewer();
        var zw = _zoomBar.DesiredSize.Width;
        var zh = _zoomBar.DesiredSize.Height;
        const double margin = 10.0;
        double bx = margin, by = margin;
        if (sv is not null)
        {
            bx = sv.Offset.X + sv.Viewport.Width  - zw - margin;
            by = sv.Offset.Y + sv.Viewport.Height - zh - margin;
        }
        _zoomBar.Arrange(new Rect(Math.Max(0, bx), Math.Max(0, by), zw, zh));
        return finalSize;
    }

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

        // Phase offset: shift lines so they stay aligned to model-space origin as the user pans.
        double phaseX = scrollX % step;
        double phaseY = scrollY % step;

        var pen = isLight ? GridPenLight : GridPen;

        // Vertical lines
        for (double x = -phaseX; x < bounds.Width; x += step)
            ctx.DrawLine(pen, new Point(x, 0), new Point(x, bounds.Height));

        // Horizontal lines
        for (double y = -phaseY; y < bounds.Height; y += step)
            ctx.DrawLine(pen, new Point(0, y), new Point(bounds.Width, y));
    }

    private void RenderCommentBlocks(DrawingContext ctx)
    {
        foreach (var comment in _vm!.CommentBlocks)
        {
            double x    = comment.X;
            double y    = comment.Y;
            double w    = comment.Width;
            double h    = comment.Height;
            bool   sel  = comment.IsSelected;
            double fold = CommentFoldSize;

            var fill      = sel ? CommentSelFill   : CommentFill;
            var borderBrush = sel ? (IBrush)CommentSelBorder : CommentBorderBrush;
            var borderPen   = new Pen(borderBrush, NodeBorderThickness);
            var foldPen     = new Pen(borderBrush, 1.0);

            // ── 1. Drop shadow ────────────────────────────────────────────
            // Build a shadow polygon offset by (4, 5) to the bottom-right.
            {
                const double sx = 4, sy = 5;
                var shadowGeo = new StreamGeometry();
                using (var gc = shadowGeo.Open())
                {
                    gc.BeginFigure(new Point(x + sx,              y + sy             ), isFilled: true);
                    gc.LineTo     (new Point(x + w - fold + sx,   y + sy             ));
                    gc.LineTo     (new Point(x + w + sx,          y + fold + sy      ));
                    gc.LineTo     (new Point(x + w + sx,          y + h + sy         ));
                    gc.LineTo     (new Point(x + sx,              y + h + sy         ));
                    gc.EndFigure(true);
                }
                ctx.DrawGeometry(CommentShadowBrush, null, shadowGeo);
            }

            // ── 2. Glow (selection / ambient) ─────────────────────────────
            DrawRectGlow(ctx, new Rect(x, y, w, h), NodeCornerRadius,
                         sel ? SelectionGlowColor : CommentGlowColor);

            // ── 3. Main note body (dog-ear polygon) ───────────────────────
            var bodyGeo = new StreamGeometry();
            using (var gc = bodyGeo.Open())
            {
                gc.BeginFigure(new Point(x,              y     ), isFilled: true);
                gc.LineTo     (new Point(x + w - fold,   y     ));   // top edge  → fold start
                gc.LineTo     (new Point(x + w,          y + fold)); // fold crease end
                gc.LineTo     (new Point(x + w,          y + h ));   // right edge
                gc.LineTo     (new Point(x,              y + h ));   // bottom edge
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
            }

            // ── 5. Faint ruled lines ──────────────────────────────────────
            {
                double ruleLeft  = x + CommentPadding;
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
                gc.BeginFigure(new Point(x + w - fold, y      ), isFilled: true);
                gc.LineTo     (new Point(x + w,        y + fold));
                gc.LineTo     (new Point(x + w - fold, y + fold));
                gc.EndFigure(true);
            }
            ctx.DrawGeometry(CommentFoldBackBrush, foldPen, foldGeo);

            // ── 7. Fold crease line ────────────────────────────────────────
            ctx.DrawLine(borderPen,
                         new Point(x + w - fold, y),
                         new Point(x + w,        y + fold));

            // ── 8. Comment text ───────────────────────────────────────────
            var textArea = new Rect(
                x + CommentPadding,
                y + CommentHeaderHeight + 2,
                w - CommentPadding * 2,
                h - CommentHeaderHeight - CommentPadding - 2);
            if (textArea.Width > 4 && textArea.Height > 4)
            {
                using var clipPush = ctx.PushClip(textArea);
                var layout = new TextLayout(
                    comment.Name,
                    DefaultTypeface,
                    CommentFontSize,
                    CommentTextBrush,
                    textAlignment: TextAlignment.Left,
                    textWrapping: TextWrapping.Wrap,
                    maxWidth: textArea.Width,
                    maxHeight: textArea.Height);
                layout.Draw(ctx, new Point(textArea.X, textArea.Y));
            }

            // ── 9. Resize grip dots (bottom-right) ────────────────────────
            {
                double dotR = 2.0;
                double bx   = x + w;
                double by   = y + h;
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
            double rw  = ft.Width;
            double rh  = ft.Height;
            var rect   = new Rect(ft.X, ft.Y, rw, rh);

            // Neon glow + outer border (dashed to distinguish from a regular node or boundary)
            var borderBrush = ft.IsSelected ? FtSelBorderBrush : (_isLight ? FtBorderBrushL : FtBorderBrush);
            var border      = new Pen(borderBrush, NodeBorderThickness + 0.5, dashStyle: FtBorderDash);
            DrawRectGlow(ctx, rect, FtCornerRadius, ft.IsSelected ? SelectionGlowColor : (_isLight ? FtGlowColorL : FtGlowColor));
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
            var labelFt   = MakeText(labelText, FtNameFontSize, _isLight ? FtTextBrushL : FtTextBrush);
            var lx        = ft.X + 8.0;
            var ly        = ft.Y + (FtHeaderHeight - labelFt.Height) / 2.0;
            using (ctx.PushClip(new Rect(ft.X + 4, ft.Y, rw - 8, FtHeaderHeight)))
                ctx.DrawText(labelFt, new Point(lx, ly));

            // ── FunctionParameter hook rows ────────────────────────────────
            double rowY = ft.Y + FtHeaderHeight;
            foreach (var fp in ft.FunctionParameters)
            {
                ctx.DrawRectangle(InlineParamRowBg, null,
                    new Rect(ft.X, rowY, rw, FtHookRowHeight));
                ctx.DrawLine(new Pen(_isLight ? HookDividerBrushL : HookDividerBrush, 0.5),
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
                double gx   = ft.X + rw;
                double gy   = ft.Y + rh;
                for (int d = 0; d < 3; d++)
                {
                    double off = 4.0 + d * 4.0;
                    ctx.DrawEllipse(_isLight ? ResizeHandleBrushL : ResizeHandleBrush, null,
                        new Point(gx - off + dotR, gy - dotR), dotR, dotR);
                    ctx.DrawEllipse(_isLight ? ResizeHandleBrushL : ResizeHandleBrush, null,
                        new Point(gx - dotR,        gy - off + dotR), dotR, dotR);
                }
            }
        }
    }

    /// <summary>
    /// Draws function-instance boxes. Each box is styled in teal, shows the instance name
    /// in the header and the template name as a subtitle, then lists exposed hooks below.
    /// </summary>
    private void RenderFunctionInstances(DrawingContext ctx)
    {
        foreach (var fi in _vm!.FunctionInstances)
        {
            double rw  = fi.Width;
            double rh  = fi.Height;
            var rect   = new Rect(fi.X, fi.Y, rw, rh);

            var borderBrush = fi.IsSelected ? (_isLight ? FiSelBorderBrushL : FiSelBorderBrush) : (_isLight ? FiBorderBrushL : FiBorderBrush);
            var border      = new Pen(borderBrush, NodeBorderThickness);
            DrawRectGlow(ctx, rect, FiCornerRadius, fi.IsSelected ? SelectionGlowColor : (_isLight ? FiGlowColorL : FiGlowColor));
            ctx.DrawRectangle(_isLight ? FiFillL : FiFill, border, rect, FiCornerRadius, FiCornerRadius);

            // ── Header band ────────────────────────────────────────────────
            ctx.DrawRectangle(_isLight ? FiHeaderFillL : FiHeaderFill, null, rect, FiCornerRadius, FiCornerRadius);
            ctx.DrawRectangle(_isLight ? FiFillL : FiFill, null,
                new Rect(fi.X, fi.Y + FtHeaderHeight, rw, rh - FtHeaderHeight));
            ctx.DrawRectangle(null, border, rect, FiCornerRadius, FiCornerRadius);

            // "⊡ InstanceName" in header
            var labelText = "\u22A1 " + fi.Name;
            var labelFtText = MakeText(labelText, FtNameFontSize, _isLight ? FiTextBrushL : FiTextBrush);
            var lx  = fi.X + 8.0;
            var ly  = fi.Y + (FtHeaderHeight - labelFtText.Height) / 2.0;
            using (ctx.PushClip(new Rect(fi.X + 4, fi.Y, rw - 8, FtHeaderHeight)))
                ctx.DrawText(labelFtText, new Point(lx, ly));

            // Template subtitle (small, muted) at the bottom of the header
            var subText = MakeText("[" + fi.TemplateName + "]" + (fi.EntryNodeTypeName.Length > 0 ? " : " + fi.EntryNodeTypeName : ""), HookFontSize, _isLight ? FiSubTextBrushL : FiSubTextBrush);
            var subX = fi.X + rw - subText.Width - 8.0;
            var subY = fi.Y + (FtHeaderHeight - subText.Height) / 2.0;
            using (ctx.PushClip(new Rect(fi.X + 4, fi.Y, rw - 8, FtHeaderHeight)))
                ctx.DrawText(subText, new Point(Math.Max(lx + labelFtText.Width + 4, subX), subY));

            // ── FunctionParameter hook rows ────────────────────────────────
            double rowY = fi.Y + FtHeaderHeight;
            var fiHooks = fi.UnderlyingInstance.Hooks;
            _fiConnectedHooks.TryGetValue(fi, out var fiConnected);
            for (int fi_i = 0; fi_i < fi.FunctionParameters.Count; fi_i++)
            {
                var fp     = fi.FunctionParameters[fi_i];
                var fpHook = fi_i < fiHooks.Count ? fiHooks[fi_i] as FunctionParameterHook : null;
                bool fpConn = fiConnected is not null && fpHook is not null && fiConnected.Contains(fpHook);

                // Tinted background matching unsatisfied hook style (FP hooks are always required).
                if (!fpConn)
                    ctx.DrawRectangle(InlineParamRowBg, null,
                        new Rect(fi.X, rowY, rw, FtHookRowHeight));

                ctx.DrawLine(new Pen(_isLight ? HookDividerBrushL : HookDividerBrush, 0.5),
                    new Point(fi.X, rowY), new Point(fi.X + rw, rowY));

                // Dot on the RIGHT edge — green if connected, red if not (FP hooks are required).
                double dotCy = rowY + FtHookRowHeight / 2.0;
                var dotBrush = fpConn ? (_isLight ? HookConnectedBrushL   : HookConnectedBrush)
                                      : (_isLight ? HookUnsatisfiedBrushL : HookUnsatisfiedBrush);
                ctx.DrawEllipse(dotBrush, null,
                    new Point(fi.X + rw, dotCy), HookDotRadius, HookDotRadius);

                const double textPad = 6.0;
                var hookNameFt = MakeText(fp.Name ?? string.Empty, HookFontSize,
                    fpConn ? (_isLight ? HookTextConnBrushL : HookTextConnBrush)
                           : (_isLight ? HookTextUnsatisfiedBrushL : HookTextUnsatisfiedBrush));
                double maxW    = rw - textPad * 2 - HookDotRadius * 2;
                double hookTy  = dotCy - hookNameFt.Height / 2.0;
                using (ctx.PushClip(new Rect(fi.X + textPad, hookTy, Math.Max(0, maxW), hookNameFt.Height + 1)))
                    ctx.DrawText(hookNameFt, new Point(fi.X + textPad, hookTy));

                rowY += FtHookRowHeight;
            }

            // ── Resize grip ───────────────────────────────────────────────
            {
                double dotR = 2.0;
                double gx   = fi.X + rw;
                double gy   = fi.Y + rh;
                for (int d = 0; d < 3; d++)
                {
                    double off = 4.0 + d * 4.0;
                    ctx.DrawEllipse(_isLight ? ResizeHandleBrushL : ResizeHandleBrush, null,
                        new Point(gx - off + dotR, gy - dotR), dotR, dotR);
                    ctx.DrawEllipse(_isLight ? ResizeHandleBrushL : ResizeHandleBrush, null,
                        new Point(gx - dotR,        gy - off + dotR), dotR, dotR);
                }
            }
        }
    }

    private void RenderFunctionParameters(DrawingContext ctx)
    {
        foreach (var fp in _vm!.FunctionParameterVMs)
        {
            double rw  = fp.Width;
            double rh  = fp.Height;
            var rect   = new Rect(fp.X, fp.Y, rw, rh);

            // Orange-red fill to visually distinguish FunctionParameter nodes.
            var borderColor = fp.IsSelected ? Colors.OrangeRed : Colors.DarkOrange;
            var border      = new Pen(new SolidColorBrush(borderColor), NodeBorderThickness);
            DrawRectGlow(ctx, rect, FiCornerRadius, fp.IsSelected ? SelectionGlowColor : Colors.OrangeRed);
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0x8C, 0x00)), border,
                rect, FiCornerRadius, FiCornerRadius);

            // Header band in a darker orange.
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xFF, 0xC0, 0x50, 0x00)), null,
                new Rect(fp.X, fp.Y, rw, FtHeaderHeight), FiCornerRadius, FiCornerRadius);
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0x8C, 0x00)), null,
                new Rect(fp.X, fp.Y + FtHeaderHeight / 2.0, rw, rh - FtHeaderHeight / 2.0));
            ctx.DrawRectangle(null, border, rect, FiCornerRadius, FiCornerRadius);

            // Name label in header (no prefix so the full name fits).
            var labelFtText = MakeText(fp.Name, FtNameFontSize, Brushes.White);
            var lx = fp.X + 8.0;
            var ly = fp.Y + (FtHeaderHeight - labelFtText.Height) / 2.0;
            using (ctx.PushClip(new Rect(fp.X + 4, fp.Y, rw - 8, FtHeaderHeight)))
                ctx.DrawText(labelFtText, new Point(lx, ly));

            // Type name in smaller text below header.
            if (!string.IsNullOrEmpty(fp.TypeName))
            {
                var typeText = MakeText(fp.TypeName, HookFontSize, Brushes.White);
                using (ctx.PushClip(new Rect(fp.X + 4, fp.Y + FtHeaderHeight, rw - 8, rh - FtHeaderHeight)))
                    ctx.DrawText(typeText, new Point(lx, fp.Y + FtHeaderHeight + 4.0));
            }

            // Resize grip (same dot pattern as other elements).
            {
                double dotR = 2.0;
                double gx   = fp.X + rw;
                double gy   = fp.Y + rh;
                for (int d = 0; d < 3; d++)
                {
                    double off = 4.0 + d * 4.0;
                    ctx.DrawEllipse(_isLight ? ResizeHandleBrushL : ResizeHandleBrush, null,
                        new Point(gx - off + dotR, gy - dotR), dotR, dotR);
                    ctx.DrawEllipse(_isLight ? ResizeHandleBrushL : ResizeHandleBrush, null,
                        new Point(gx - dotR,        gy - off + dotR), dotR, dotR);
                }
            }
        }
    }

    private void RenderLinks(DrawingContext ctx)
    {
        foreach (var link in _vm!.Links)
        {
            // Don't render links whose destination is in a different boundary
            // or whose destination node is inlined (value shown in hook row instead).
            if (link.Destination is null) continue;
            if (link.Destination is NodeViewModel destNvm && destNvm.IsInlined) continue;

            var brush = link.IsSelected ? LinkSelBrush : (_isLight ? LinkBrushL : LinkBrush);
            var pen   = new Pen(brush, LinkThickness);

            // Neon glow: two wider transparent halos drawn beneath the main link line.
            var glowColor  = link.IsSelected ? LinkSelGlowColor : (_isLight ? LinkGlowColorL : LinkGlowColor);
            var glowOuter  = new Pen(new SolidColorBrush(Color.FromArgb(0x10, glowColor.R, glowColor.G, glowColor.B)), LinkThickness + 8);
            var glowInner  = new Pen(new SolidColorBrush(Color.FromArgb(0x26, glowColor.R, glowColor.G, glowColor.B)), LinkThickness + 3);

            // Draw an S-shaped cubic Bézier curve. Tension adapts to the span so short
            // links curve gently and long ones sweep broadly, with no elbow kinks.
            var (bp1, bc1, bc2, bp2) = ComputeSCurve(link);
            // Derive arrowhead direction from the destination border normal so the
            // head always arrives perfectly perpendicular to the face it hits.
            var arrowFrom = BorderArrivalFrom(link.Destination, bp2);
            Point approachFrom = arrowFrom, arrowTip = bp2;
            var shaftEnd = DrawArrow(ctx, brush, arrowFrom, bp2);

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

            // Glow halos extend to bp2 so the halo wraps the arrowhead too.
            var glowGeo  = MakeCurveGeo(bp1, bc1, bc2, bp2);
            ctx.DrawGeometry(null, glowOuter, glowGeo);
            ctx.DrawGeometry(null, glowInner, glowGeo);

            // Main shaft stops at shaftEnd so it doesn't overlap the filled arrowhead.
            var shaftGeo = MakeCurveGeo(bp1, bc1, bc2, shaftEnd);
            ctx.DrawGeometry(null, pen, shaftGeo);

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
                    double dx = arrowTip.X - approachFrom.X;
                    double dy = arrowTip.Y - approachFrom.Y;
                    double dlen = Math.Sqrt(dx * dx + dy * dy);
                    if (dlen >= 1)
                    {
                        double ux = dx / dlen, uy = dy / dlen;
                        double nx = -uy, ny = ux; // 90° CCW perpendicular unit vector
                        double offset = ft.Height * 0.5 + 3;
                        double lx = shaftEnd.X - ft.Width  * 0.5 + nx * offset;
                        double ly = shaftEnd.Y - ft.Height * 0.5 + ny * offset;
                        ctx.DrawText(ft, new Point(lx, ly));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Computes the four elbow points (start, bend1, bend2, end) for a link's
    /// three-segment orthogonal routing. Uses the hook anchor cache and border-clip logic.
    /// </summary>
    private (Point p1, Point mid1, Point mid2, Point p2) ComputeElbow(LinkViewModel link)
    {
        var originCenter = new Point(link.X1, link.Y1);
        var destCenter   = new Point(link.X2, link.Y2);

        // p1: hook anchor when origin is a Node or FunctionInstance, else border-clip.
        Point p1;
        bool  hookOrigin = false;
        if (link.Origin is NodeViewModel originNvm
            && _hookAnchors.TryGetValue((originNvm, link.UnderlyingLink.OriginHook), out var hookPt))
        {
            p1         = hookPt;
            hookOrigin = true;
        }
        else if (link.Origin is FunctionInstanceViewModel fiOriginElbow
            && link.UnderlyingLink.OriginHook is FunctionParameterHook fphElbow
            && _fiHookAnchors.TryGetValue((fiOriginElbow, fphElbow), out var fiHookPtElbow))
        {
            p1         = fiHookPtElbow;
            hookOrigin = true;
        }
        else
        {
            p1 = BorderPoint(link.Origin, destCenter) ?? originCenter;
        }

        // H-V-H when the link exits a hook (rightward) or horizontal span >= vertical span.
        // V-H-V otherwise.
        bool hvh = hookOrigin ||
                   Math.Abs(destCenter.X - p1.X) >= Math.Abs(destCenter.Y - p1.Y);

        Point mid1, mid2, p2;
        if (hvh)
        {
            double midX    = Math.Max(p1.X + ElbowMinOffset, (p1.X + destCenter.X) / 2.0);
            mid1           = new Point(midX, p1.Y);

            // Determine whether the vertical middle segment will intersect the
            // destination's top or bottom border rather than a side border.
            // This happens when midX falls inside the destination's horizontal span.
            // In that case the old approach of using (midX, destCenter.Y) as the
            // approach point is wrong: destCenter.Y equals the centre Y, so dy=0
            // in ClipLineToRect and only side borders are checked.  Worse, when
            // the approach point itself is inside the rect ClipLineToRect returns
            // an exit intersection rather than an entry, misplacing the arrowhead.
            bool midXInHSpan = false;
            double borderY   = 0;
            if (link.Destination is NodeViewModel destNode)
            {
                var dRect    = new Rect(destNode.X, destNode.Y,
                                        NodeRenderWidth(destNode), NodeRenderHeight(destNode));
                midXInHSpan  = midX >= dRect.X && midX <= dRect.Right;
                if (midXInHSpan)
                    borderY  = p1.Y <= destCenter.Y ? dRect.Y : dRect.Bottom;
            }
            else if (link.Destination is GhostNodeViewModel ghostDestH)
            {
                var dRect    = new Rect(ghostDestH.X, ghostDestH.Y, ghostDestH.Width, ghostDestH.Height);
                midXInHSpan  = midX >= dRect.X && midX <= dRect.Right;
                if (midXInHSpan)
                    borderY  = p1.Y <= destCenter.Y ? dRect.Y : dRect.Bottom;
            }
            else if (link.Destination is FunctionInstanceViewModel fiDestH)
            {
                var dRect    = new Rect(fiDestH.X, fiDestH.Y, fiDestH.Width, fiDestH.Height);
                midXInHSpan  = midX >= dRect.X && midX <= dRect.Right;
                if (midXInHSpan)
                    borderY  = p1.Y <= destCenter.Y ? dRect.Y : dRect.Bottom;
            }

            if (midXInHSpan)
            {
                // Vertical approach: arrow arrives straight down (or up) at the
                // top (or bottom) border.  Collapse mid2 onto mid1 so the second
                // segment has zero length and the full arrow is the vertical shaft.
                p2   = new Point(midX, borderY);
                mid2 = mid1;
            }
            else
            {
                // Normal case: midX is outside the destination's horizontal span,
                // so the final segment is horizontal into a side border.
                var approachPt = new Point(midX, destCenter.Y);
                p2             = BorderPoint(link.Destination, approachPt) ?? destCenter;
                mid2           = new Point(midX, p2.Y);
            }
        }
        else
        {
            double midY    = (p1.Y + destCenter.Y) / 2.0;
            mid1           = new Point(p1.X, midY);

            // Determine whether the horizontal middle segment will intersect the
            // destination's left or right border rather than a top/bottom border.
            // This happens when midY falls inside the destination's vertical span.
            // In that case the approach point (destCenter.X, midY) is inside the
            // destination box, which causes BorderPoint/ClipLineToRect to return
            // an exit intersection rather than an entry, misplacing the arrowhead.
            bool midYInVSpan = false;
            double borderX   = 0;
            if (link.Destination is NodeViewModel destNodeV)
            {
                var dRect    = new Rect(destNodeV.X, destNodeV.Y,
                                        NodeRenderWidth(destNodeV), NodeRenderHeight(destNodeV));
                midYInVSpan  = midY >= dRect.Y && midY <= dRect.Bottom;
                if (midYInVSpan)
                    borderX  = p1.X <= destCenter.X ? dRect.X : dRect.Right;
            }
            else if (link.Destination is GhostNodeViewModel ghostDestV)
            {
                var dRect    = new Rect(ghostDestV.X, ghostDestV.Y, ghostDestV.Width, ghostDestV.Height);
                midYInVSpan  = midY >= dRect.Y && midY <= dRect.Bottom;
                if (midYInVSpan)
                    borderX  = p1.X <= destCenter.X ? dRect.X : dRect.Right;
            }
            else if (link.Destination is FunctionInstanceViewModel fiDestV)
            {
                var dRect    = new Rect(fiDestV.X, fiDestV.Y, fiDestV.Width, fiDestV.Height);
                midYInVSpan  = midY >= dRect.Y && midY <= dRect.Bottom;
                if (midYInVSpan)
                    borderX  = p1.X <= destCenter.X ? dRect.X : dRect.Right;
            }

            if (midYInVSpan)
            {
                // Horizontal approach: arrow arrives from the left or right border.
                // Collapse mid2 onto mid1 so the second segment has zero length
                // and the full arrow is the horizontal shaft.
                p2   = new Point(borderX, midY);
                mid2 = mid1;
            }
            else
            {
                var approachPt = new Point(destCenter.X, midY);
                p2             = BorderPoint(link.Destination, approachPt) ?? destCenter;
                mid2           = new Point(p2.X, midY);
            }
        }
        return (p1, mid1, mid2, p2);
    }

    /// <summary>
    /// Computes cubic Bézier control points for a direction-aware link curve.
    /// <para>
    /// c1 is placed along the <em>exit</em> tangent at p1 (rightward for hook anchors,
    /// radially outward for Start nodes). c2 is placed along the <em>entry</em> tangent
    /// at p2, derived from the inward normal of the destination border face, so the
    /// curve arrives smoothly perpendicular to that face. Tension is proportional to
    /// the Euclidean distance between p1 and p2 so the curve scales naturally at any
    /// zoom and in any direction.
    /// </para>
    /// </summary>
    private (Point p1, Point c1, Point c2, Point p2) ComputeSCurve(LinkViewModel link)
    {
        var destCenter = new Point(link.X2, link.Y2);

        // p1 and exit direction.
        Point  p1;
        Vector exitDir;
        if (link.Origin is NodeViewModel originNvm
            && _hookAnchors.TryGetValue((originNvm, link.UnderlyingLink.OriginHook), out var hookPt))
        {
            p1      = hookPt;
            exitDir = new Vector(1, 0); // hooks always face right
        }
        else if (link.Origin is FunctionInstanceViewModel fiOriginSC
            && link.UnderlyingLink.OriginHook is FunctionParameterHook fphSC
            && _fiHookAnchors.TryGetValue((fiOriginSC, fphSC), out var fiHookPtSC))
        {
            p1      = fiHookPtSC;
            exitDir = new Vector(1, 0); // FP hook dots always face right
        }
        else if (link.Origin is StartViewModel startOrigin)
        {
            var oc  = new Point(startOrigin.CenterX, startOrigin.CenterY);
            p1      = BorderPoint(link.Origin, destCenter) ?? oc;
            var odx = p1.X - oc.X;  var ody = p1.Y - oc.Y;
            var ol  = Math.Sqrt(odx * odx + ody * ody);
            exitDir = ol < 0.1 ? new Vector(1, 0) : new Vector(odx / ol, ody / ol);
        }
        else
        {
            p1      = BorderPoint(link.Origin, destCenter) ?? new Point(link.X1, link.Y1);
            exitDir = new Vector(1, 0);
        }

        // p2: destination border point approached from p1's direction.
        var p2 = BorderPoint(link.Destination, p1) ?? destCenter;

        // c1 follows the exit tangent; c2 steps back from p2 along the entry tangent.
        var    entryDir = BorderInwardNormal(link.Destination, p2);
        double dx       = p2.X - p1.X, dy = p2.Y - p1.Y;
        double tension  = Math.Max(Math.Sqrt(dx * dx + dy * dy) * 0.45, 50.0);

        var c1 = new Point(p1.X + exitDir.X  * tension, p1.Y + exitDir.Y  * tension);
        var c2 = new Point(p2.X - entryDir.X * tension, p2.Y - entryDir.Y * tension);

        return (p1, c1, c2, p2);
    }

    /// <summary>Evaluates a cubic Bézier curve at parameter <paramref name="t"/> ∈ [0, 1].</summary>
    private static Point SampleCubicBezier(Point p1, Point c1, Point c2, Point p2, double t)
    {
        double u = 1 - t;
        return new Point(
            u*u*u * p1.X + 3*u*u*t * c1.X + 3*u*t*t * c2.X + t*t*t * p2.X,
            u*u*u * p1.Y + 3*u*u*t * c1.Y + 3*u*t*t * c2.Y + t*t*t * p2.Y);
    }

    /// <summary>
    /// Returns the unit vector pointing <em>into</em> <paramref name="dest"/> through
    /// the border face that <paramref name="borderPt"/> sits on.
    /// For axis-aligned rect borders this is always one of ±X or ±Y.
    /// For circles (Start nodes) it is the inward radius direction.
    /// </summary>
    private Vector BorderInwardNormal(ICanvasElement? dest, Point borderPt)
    {
        const double eps = 1.5;

        Rect? r = dest switch {
            NodeViewModel nvm             => new Rect(nvm.X,  nvm.Y,  NodeRenderWidth(nvm), NodeRenderHeight(nvm)),
            GhostNodeViewModel gnvm       => new Rect(gnvm.X, gnvm.Y, gnvm.Width,  gnvm.Height),
            FunctionInstanceViewModel fiv => new Rect(fiv.X,  fiv.Y,  fiv.Width,   fiv.Height),
            _                             => (Rect?)null
        };

        if (r is { } rect)
        {
            if (Math.Abs(borderPt.X - rect.X)      < eps) return new Vector( 1,  0); // left face  → rightward
            if (Math.Abs(borderPt.X - rect.Right)  < eps) return new Vector(-1,  0); // right face → leftward
            if (Math.Abs(borderPt.Y - rect.Y)      < eps) return new Vector( 0,  1); // top face   → downward
            if (Math.Abs(borderPt.Y - rect.Bottom) < eps) return new Vector( 0, -1); // bottom face → upward
        }

        // Circle or unknown: inward radial direction.
        var cx  = dest?.CenterX ?? borderPt.X;
        var cy  = dest?.CenterY ?? borderPt.Y;
        var ddx = cx - borderPt.X;  var ddy = cy - borderPt.Y;
        var len = Math.Sqrt(ddx * ddx + ddy * ddy);
        return len < 0.1 ? new Vector(-1, 0) : new Vector(ddx / len, ddy / len);
    }

    /// <summary>
    /// Returns a point one arrowhead-length back from <paramref name="borderPt"/> along the
    /// inward-facing normal of the destination border face, so the arrowhead always
    /// arrives perpendicular to that face.
    /// </summary>
    private Point BorderArrivalFrom(ICanvasElement? dest, Point borderPt)
    {
        double back   = ArrowSize * 1.5;
        var    normal = BorderInwardNormal(dest, borderPt);
        return new Point(borderPt.X - normal.X * back, borderPt.Y - normal.Y * back);
    }

    /// <summary>
    /// Computes a straight-line (p1, p2) pair for a link:
    /// p1 is the hook anchor (or origin border point), and p2 is the destination
    /// border point along the direct p1→destination-centre direction.
    /// </summary>
    private (Point p1, Point p2) ComputeDirectLine(LinkViewModel link)
    {
        var destCenter = new Point(link.X2, link.Y2);

        // p1: hook anchor when available, else origin border point toward dest centre.
        Point p1;
        if (link.Origin is NodeViewModel originNvm
            && _hookAnchors.TryGetValue((originNvm, link.UnderlyingLink.OriginHook), out var hookPt))
            p1 = hookPt;
        else
            p1 = BorderPoint(link.Origin, destCenter) ?? new Point(link.X1, link.Y1);

        // p2: destination border point along the p1→dest direction.
        var p2 = BorderPoint(link.Destination, p1) ?? destCenter;
        return (p1, p2);
    }

    /// <summary>
    /// Returns the point on <paramref name="element"/>'s visual border that lies on
    /// the line between the element's centre and <paramref name="other"/>.
    /// Returns <c>null</c> when <paramref name="element"/> is <c>null</c>.
    /// </summary>
    private Point? BorderPoint(ICanvasElement? element, Point other)
    {
        if (element is null) return null;

        if (element is NodeViewModel nvm)
        {
            var rect = new Rect(nvm.X, nvm.Y, NodeRenderWidth(nvm), NodeRenderHeight(nvm));
            return ClipLineToRect(other, rect);
        }

        if (element is GhostNodeViewModel gnvm)
        {
            var rect = new Rect(gnvm.X, gnvm.Y, gnvm.Width, gnvm.Height);
            return ClipLineToRect(other, rect);
        }

        if (element is FunctionInstanceViewModel fivm)
        {
            var rect = new Rect(fivm.X, fivm.Y, fivm.Width, fivm.Height);
            return ClipLineToRect(other, rect);
        }

        if (element is FunctionParameterViewModel fpvmBP)
        {
            var rect = new Rect(fpvmBP.X, fpvmBP.Y, fpvmBP.Width, fpvmBP.Height);
            return ClipLineToRect(other, rect);
        }

        if (element is StartViewModel)
        {
            var center = new Point(element.CenterX, element.CenterY);
            var dx = other.X - center.X;
            var dy = other.Y - center.Y;
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1) return center;
            var r = StartViewModel.Radius;
            return new Point(center.X + r * dx / len, center.Y + r * dy / len);
        }

        return new Point(element.CenterX, element.CenterY);
    }

    /// <summary>
    /// Given a line from an external point <paramref name="outside"/> toward the
    /// centre of <paramref name="rect"/>, returns the first intersection with the
    /// rectangle border (i.e. the entry edge point closest to <paramref name="outside"/>).
    /// Falls back to the rect centre when no intersection is found.
    /// </summary>
    private static Point ClipLineToRect(Point outside, Rect rect)
    {
        var center = new Point(rect.X + rect.Width / 2.0, rect.Y + rect.Height / 2.0);
        double dx = center.X - outside.X;
        double dy = center.Y - outside.Y;

        double tBest = double.MaxValue;

        void TryT(double t, bool horizontal, double coord)
        {
            if (t <= 0 || t >= tBest) return;
            double other = horizontal
                ? outside.X + t * dx   // x coordinate when checking horizontal side
                : outside.Y + t * dy;  // y coordinate when checking vertical side
            if (horizontal && other >= rect.X && other <= rect.Right)  tBest = t;
            if (!horizontal && other >= rect.Y && other <= rect.Bottom) tBest = t;
        }

        if (Math.Abs(dx) > 1e-10)
        {
            TryT((rect.X       - outside.X) / dx, horizontal: false, 0);
            TryT((rect.Right   - outside.X) / dx, horizontal: false, 0);
        }
        if (Math.Abs(dy) > 1e-10)
        {
            TryT((rect.Y       - outside.Y) / dy, horizontal: true, 0);
            TryT((rect.Bottom  - outside.Y) / dy, horizontal: true, 0);
        }

        if (tBest == double.MaxValue) return center;
        return new Point(outside.X + tBest * dx, outside.Y + tBest * dy);
    }

    /// <summary>
    /// Draws a filled triangular arrowhead at <paramref name="to"/> pointing away from
    /// <paramref name="from"/>, and returns the base-centre of the triangle so the
    /// caller can terminate the shaft line there without overlapping the head.
    /// </summary>
    private static Point DrawArrow(DrawingContext ctx, IBrush brush, Point from, Point to)
    {
        var dx  = to.X - from.X;
        var dy  = to.Y - from.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return to;

        // Unit direction and perpendicular
        var ux = dx / len;
        var uy = dy / len;
        var px = -uy * (ArrowSize * 0.45);
        var py =  ux * (ArrowSize * 0.45);

        var tip   = to;
        var left  = new Point(to.X - ux * ArrowSize + px, to.Y - uy * ArrowSize + py);
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
        var pen = new Pen(PendingLinkBrush, LinkThickness, dashStyle: PendingLinkDash);

        // p1 and exit direction follow the same rules as ComputeSCurve.
        Point p1;
        Vector exitDir;
        var cursor = _linkCurrentPos;
        if (_linkOrigin is StartViewModel pendingStart)
        {
            var oc  = new Point(pendingStart.CenterX, pendingStart.CenterY);
            p1      = BorderPoint(_linkOrigin, cursor) ?? oc;
            var odx = p1.X - oc.X;  var ody = p1.Y - oc.Y;
            var ol  = Math.Sqrt(odx * odx + ody * ody);
            exitDir = ol < 0.1 ? new Vector(1, 0) : new Vector(odx / ol, ody / ol);
        }
        else
        {
            p1      = new Point(_linkOrigin.CenterX, _linkOrigin.CenterY);
            exitDir = new Vector(1, 0);
        }

        var p2 = cursor;
        // Approach direction for the free cursor end: from origin toward cursor.
        var aprDx = p1.X - p2.X;  var aprDy = p1.Y - p2.Y;
        var aprLen = Math.Sqrt(aprDx * aprDx + aprDy * aprDy);
        Vector approachDir = aprLen < 0.1 ? new Vector(-1, 0) : new Vector(aprDx / aprLen, aprDy / aprLen);

        double dx = p2.X - p1.X, dy = p2.Y - p1.Y;
        double tension = Math.Max(Math.Sqrt(dx * dx + dy * dy) * 0.45, 50.0);
        var c1 = new Point(p1.X + exitDir.X    * tension, p1.Y + exitDir.Y    * tension);
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
        var pgColor = Color.FromRgb(0x2E, 0xCC, 0x71);
        ctx.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(0x10, pgColor.R, pgColor.G, pgColor.B)), LinkThickness + 8), geo);
        ctx.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(0x26, pgColor.R, pgColor.G, pgColor.B)), LinkThickness + 3), geo);
        ctx.DrawGeometry(null, pen, geo);
    }

    // ── Link hit-testing ──────────────────────────────────────────────────

    /// <summary>
    /// Returns the first <see cref="LinkViewModel"/> whose rendered segments pass within
    /// <see cref="LinkHitTolerance"/> pixels of <paramref name="pos"/>, or <c>null</c>.
    /// </summary>
    private LinkViewModel? HitTestLink(Point pos)
    {
        if (_vm is null) return null;
        foreach (var link in _vm.Links)
        {
            // Skip inter-boundary links — they are not rendered.
            if (link.Destination is null) continue;
            // Skip links to inlined nodes — no line is drawn for them.
            if (link.Destination is NodeViewModel dlNvm && dlNvm.IsInlined) continue;

            // Sample the S-curve at 12 chords; any chord within tolerance is a hit.
            var (hp1, hc1, hc2, hp2) = ComputeSCurve(link);
            const int HitSamples = 12;
            var prev = hp1;
            bool curveHit = false;
            for (int s = 1; s <= HitSamples && !curveHit; s++)
            {
                double t    = s / (double)HitSamples;
                var    next = SampleCubicBezier(hp1, hc1, hc2, hp2, t);
                if (DistToSeg(pos, prev, next) <= LinkHitTolerance)
                    curveHit = true;
                prev = next;
            }
            if (curveHit) return link;
        }
        return null;
    }

    /// <summary>
    /// Returns the <see cref="NodeViewModel"/> and <see cref="NodeHook"/> whose rendered
    /// row rectangle contains <paramref name="pos"/>, or <c>null</c> when the point lies
    /// outside all hook rows.
    /// <para>
    /// The hit area is the full horizontal extent of the node multiplied by
    /// <see cref="HookRowHeight"/>, so any click anywhere on a hook row registers —
    /// not just the small anchor dot on the right edge.
    /// </para>
    /// </summary>
    private (NodeViewModel node, NodeHook hook, Point anchor)? HitTestHook(Point pos)
    {
        foreach (var (node, hooks) in _nodeVisibleHooks)
        {
            double rw = NodeRenderWidth(node);

            // Quick reject: x must be within the node's horizontal extent.
            if (pos.X < node.X || pos.X > node.X + rw) continue;

            for (int i = 0; i < hooks.Count; i++)
            {
                double rowTop = node.Y + NodeHeaderHeight + i * HookRowHeight;
                if (pos.Y >= rowTop && pos.Y < rowTop + HookRowHeight)
                {
                    var anchor = new Point(node.X + rw, rowTop + HookRowHeight / 2.0);
                    return (node, hooks[i], anchor);
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Returns the <see cref="FunctionInstanceViewModel"/> and its <see cref="FunctionParameterHook"/>
    /// whose hook row contains <paramref name="pos"/>, or <c>null</c> if none.
    /// </summary>
    private (FunctionInstanceViewModel fi, FunctionParameterHook hook)? HitTestFiHook(Point pos)
    {
        if (_vm is null) return null;
        foreach (var fi in _vm.FunctionInstances)
        {
            if (pos.X < fi.X || pos.X > fi.X + fi.Width) continue;
            var fps = fi.FunctionParameters;
            for (int i = 0; i < fps.Count; i++)
            {
                double rowTop = fi.Y + FtHeaderHeight + i * FtHookRowHeight;
                if (pos.Y >= rowTop && pos.Y < rowTop + FtHookRowHeight)
                {
                    // Retrieve the corresponding FunctionParameterHook from the underlying instance.
                    var hooks = fi.UnderlyingInstance.Hooks;
                    if (i < hooks.Count && hooks[i] is FunctionParameterHook fph)
                        return (fi, fph);
                }
            }
        }
        return null;
    }

    /// <summary>Minimum distance from point <paramref name="p"/> to segment AB.</summary>
    private static double DistToSeg(Point p, Point a, Point b)
    {
        var dx    = b.X - a.X;
        var dy    = b.Y - a.Y;
        var lenSq = dx * dx + dy * dy;
        double nx, ny;
        if (lenSq < 1e-10)
        {
            nx = p.X - a.X;
            ny = p.Y - a.Y;
        }
        else
        {
            var t = Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq));
            nx = a.X + t * dx - p.X;
            ny = a.Y + t * dy - p.Y;
        }
        return Math.Sqrt(nx * nx + ny * ny);
    }

    // ── Hook anchor cache ──────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds the per-frame lookup tables used by <see cref="RenderNodes"/>,
    /// <see cref="RenderLinks"/>, and hit-testing.
    /// </summary>
    private void BuildHookAnchorCache()
    {
        _hookAnchors.Clear();
        _nodeVisibleHooks.Clear();
        _nodeConnectedHooks.Clear();
        _hookInlinedParam.Clear();
        _canInlineNodes.Clear();
        _fiHookAnchors.Clear();
        _fiConnectedHooks.Clear();
        if (_vm is null) return;

        // Which hooks on each node have a live link?
        foreach (var link in _vm.Links)
        {
            if (link.Origin is NodeViewModel originVm)
            {
                if (!_nodeConnectedHooks.TryGetValue(originVm, out var set))
                    _nodeConnectedHooks[originVm] = set = new HashSet<NodeHook>();
                set.Add(link.UnderlyingLink.OriginHook);
            }
            // Which FunctionParameterHooks on each FI have a live link?
            if (link.Origin is FunctionInstanceViewModel fiOriginVm
                && link.UnderlyingLink.OriginHook is FunctionParameterHook fphConnected)
            {
                if (!_fiConnectedHooks.TryGetValue(fiOriginVm, out var fiSet))
                    _fiConnectedHooks[fiOriginVm] = fiSet = new HashSet<FunctionParameterHook>();
                fiSet.Add(fphConnected);
            }
        }

        // Identify inlined BasicParameter nodes and which hook rows they occupy.
        // Also identify canvas-visible BasicParameter nodes eligible for the minimize button.
        foreach (var link in _vm.Links)
        {
            if (link.Origin is NodeViewModel originVm2
                && link.Destination is NodeViewModel destVm
                && destVm.IsParameterNode
                && link.UnderlyingLink.OriginHook.Cardinality == HookCardinality.Single)
            {
                if (destVm.IsInlined)
                    _hookInlinedParam[(originVm2, link.UnderlyingLink.OriginHook)] = destVm;
                else
                    _canInlineNodes.Add(destVm);
            }
        }

        foreach (var node in _vm.Nodes)
        {
            // Inlined nodes are hidden — no anchor rows needed.
            if (node.IsInlined) continue;

            _nodeConnectedHooks.TryGetValue(node, out var connected);
            connected ??= new HashSet<NodeHook>();

            IReadOnlyList<NodeHook> visible;
            if (_vm.ShowAllHooks)
            {
                visible = node.UnderlyingNode.Hooks;
            }
            else
            {
                // Required hooks (Single / AtLeastOne) are always visible.
                // Optional hooks are shown when the per-node toggle is on.
                // Connected hooks are always shown so live links remain visible.
                visible = node.UnderlyingNode.Hooks
                    .Where(h =>
                        h.Cardinality == HookCardinality.Single ||
                        h.Cardinality == HookCardinality.AtLeastOne ||
                        node.ShowHooks ||
                        connected.Contains(h))
                    .ToList();
            }

            _nodeVisibleHooks[node] = visible;

            double rw = NodeRenderWidth(node);
            for (int i = 0; i < visible.Count; i++)
            {
                double ay = node.Y + NodeHeaderHeight + i * HookRowHeight + HookRowHeight / 2.0;
                _hookAnchors[(node, visible[i])] = new Point(node.X + rw, ay);
            }
        }

        // Register FunctionInstance FunctionParameterHook anchors (right edge of each hook row).
        foreach (var fi in _vm.FunctionInstances)
        {
            var fiHooks = fi.UnderlyingInstance.Hooks;
            for (int i = 0; i < fiHooks.Count; i++)
            {
                if (fiHooks[i] is FunctionParameterHook fph)
                {
                    double rowMidY = fi.Y + FtHeaderHeight + i * FtHookRowHeight + FtHookRowHeight / 2.0;
                    _fiHookAnchors[(fi, fph)] = new Point(fi.X + fi.Width, rowMidY);
                }
            }
        }
    }

    private static double NodeRenderWidth(NodeViewModel node) =>
        Math.Max(node.Width, NodeMinWidth);

    private double NodeRenderHeight(NodeViewModel node)
    {
        bool hasParamRow = node.IsParameterNode;
        int extraRows    = hasParamRow ? 1 : 0;
        if (_nodeVisibleHooks.TryGetValue(node, out var hooks) && hooks.Count > 0)
            return Math.Max(node.Height, NodeHeaderHeight + (hooks.Count + extraRows) * HookRowHeight);
        if (hasParamRow)
            return Math.Max(node.Height, NodeHeaderHeight + HookRowHeight);
        // No visible hooks — keep at least NodeHeaderHeight so the name always fits.
        return Math.Max(node.Height, NodeHeaderHeight);
    }

    /// <summary>Returns the rendered width of any resizable canvas element.</summary>
    private double ElementRenderWidth(ICanvasElement el) =>
        el is NodeViewModel nvm ? NodeRenderWidth(nvm)
        : el is CommentBlockViewModel cvm ? cvm.Width
        : el is GhostNodeViewModel gnvm ? gnvm.Width
        : el is FunctionTemplateViewModel ftvm ? ftvm.Width
        : el is FunctionInstanceViewModel fivm ? fivm.Width
        : el is FunctionParameterViewModel fpvm ? fpvm.Width
        : 0;

    /// <summary>Returns the rendered height of any resizable canvas element.</summary>
    private double ElementRenderHeight(ICanvasElement el) =>
        el is NodeViewModel nvm ? NodeRenderHeight(nvm)
        : el is CommentBlockViewModel cvm ? cvm.Height
        : el is GhostNodeViewModel gnvm ? gnvm.Height
        : el is FunctionTemplateViewModel ftvm ? ftvm.Height
        : el is FunctionInstanceViewModel fivm ? fivm.Height
        : el is FunctionParameterViewModel fpvm ? fpvm.Height
        : 0;

    private void RenderNodes(DrawingContext ctx)
    {
        foreach (var node in _vm!.Nodes)
        {
            // Inlined nodes are hidden — skip canvas rendering entirely.
            if (node.IsInlined) continue;

            double rw = NodeRenderWidth(node);
            double rh = NodeRenderHeight(node);
            var rect   = new Rect(node.X, node.Y, rw, rh);

            // Determine whether this node is the designated entry point of the current template.
            bool isEntryNode = _vm.IsInsideFunctionTemplate
                && _vm.CurrentFunctionTemplate?.UnderlyingTemplate.EntryNode == node.UnderlyingNode;

            var border = new Pen(node.IsSelected ? NodeSelBrush : (_isLight ? NodeBorderBrushL : NodeBorderBrush), NodeBorderThickness);

            // Node background + border
            DrawRectGlow(ctx, rect, NodeCornerRadius, node.IsSelected ? SelectionGlowColor : (_isLight ? NodeGlowColorL : NodeGlowColor));
            ctx.DrawRectangle(_isLight ? NodeFillL : NodeFill, border, rect, NodeCornerRadius, NodeCornerRadius);

            // ── Entry-node gold ring (drawn over the normal border) ───────────
            if (isEntryNode)
            {
                var outerRect = new Rect(
                    node.X - EntryNodeRingExtra, node.Y - EntryNodeRingExtra,
                    rw + EntryNodeRingExtra * 2, rh + EntryNodeRingExtra * 2);
                ctx.DrawRectangle(null,
                    new Pen(EntryNodeRingBrush, EntryNodeRingThick),
                    outerRect,
                    NodeCornerRadius + EntryNodeRingExtra,
                    NodeCornerRadius + EntryNodeRingExtra);
            }

            // ── Header: node name centred in the header band ──────────────
            double headerBottom = node.Y + NodeHeaderHeight;
            var ft = MakeText(node.Name, NodeFontSize, _isLight ? NodeTextBrushL : NodeTextBrush);
            double tx = node.X + (rw - ft.Width) / 2;
            // Shift name to the upper portion of the header when a badge will be drawn below it.
            double ty = isEntryNode
                ? node.Y + 3.0
                : node.Y + (NodeHeaderHeight - ft.Height) / 2;
            ctx.DrawText(ft, new Point(tx, ty));

            // ── "▶ Entry Point" badge in the lower portion of the header ──────
            if (isEntryNode)
            {
                var badge  = MakeText("▶ Entry Point", EntryNodeLabelFontSize, EntryNodeLabelBrush);
                double blx = node.X + (rw - badge.Width) / 2.0;
                double bly = node.Y + NodeHeaderHeight - badge.Height - 2.5;
                using (ctx.PushClip(new Rect(node.X + 2, node.Y, rw - 4, NodeHeaderHeight)))
                    ctx.DrawText(badge, new Point(blx, bly));
            }

            // ── Resize handle (bottom-right corner) ───────────────────────
            // Three small diagonal dots — standard grip indicator.
            {
                double dotR = 2.0;
                double bx   = node.X + rw;
                double by   = node.Y + rh;
                for (int d = 0; d < 3; d++)
                {
                    double offset = 4.0 + d * 4.0;
                    ctx.DrawEllipse(_isLight ? ResizeHandleBrushL : ResizeHandleBrush, null,
                        new Point(bx - offset + dotR, by - dotR), dotR, dotR);
                    ctx.DrawEllipse(_isLight ? ResizeHandleBrushL : ResizeHandleBrush, null,
                        new Point(bx - dotR,           by - offset + dotR), dotR, dotR);
                }
            }

            // ── Hook toggle icon (top-right of header) ─────────────────────
            // Only shown when the node has at least one hook and there is no
            // global ShowAllHooks override (per-node toggle would be redundant).
            if (!_vm.ShowAllHooks && node.UnderlyingNode.Hooks.Count > 0)
            {
                var iconRect  = HookToggleIconRect(node, rw);
                var iconBg    = node.ShowHooks ? (_isLight ? HookToggleActiveBgL : HookToggleActiveBg) : (_isLight ? HookToggleBgL : HookToggleBg);
                ctx.DrawRectangle(iconBg, null, iconRect, 3.0, 3.0);
                var glyph     = node.ShowHooks ? "\u25BE" : "\u25B8";  // ▾ or ▸
                var iconFt    = MakeText(glyph, HookFontSize + 1.0, _isLight ? HookToggleTextL : HookToggleText);
                var glyphX    = iconRect.X + (iconRect.Width  - iconFt.Width)  / 2.0;
                var glyphY    = iconRect.Y + (iconRect.Height - iconFt.Height) / 2.0;
                ctx.DrawText(iconFt, new Point(glyphX, glyphY));
            }

            // ── Minimize-to-inline button (top-left of header) ───────────
            // Only on BasicParameter nodes that are wired to a Single hook
            // and can therefore be folded into the parent's hook row.
            if (_canInlineNodes.Contains(node))
            {
                var minRect  = InlineMinimizeButtonRect(node);
                ctx.DrawRectangle(_isLight ? MinimizeBtnBgL : MinimizeBtnBg, null, minRect, 3.0, 3.0);
                var minFt    = MakeText("\u229f", HookFontSize, _isLight ? MinimizeBtnTextL : MinimizeBtnText);  // ⊟ minus-in-box
                var minGlX   = minRect.X + (minRect.Width  - minFt.Width)  / 2.0;
                var minGlY   = minRect.Y + (minRect.Height - minFt.Height) / 2.0;
                ctx.DrawText(minFt, new Point(minGlX, minGlY));
            }

            // ── Hook rows ─────────────────────────────────────────────────
            bool hasParamRow = node.IsParameterNode;
            bool hasHooks    = _nodeVisibleHooks.TryGetValue(node, out var hooks) && hooks.Count > 0;

            if (!hasParamRow && !hasHooks)
                continue;

            // Divider line separating header from content rows
            var dividerPen = new Pen(_isLight ? HookDividerBrushL : HookDividerBrush, 1.0);
            ctx.DrawLine(dividerPen,
                new Point(node.X + 1,      headerBottom),
                new Point(node.X + rw - 1, headerBottom));

            int rowOffset = 0;

            // ── Parameter value row (first, for parameter nodes) ──────────
            if (hasParamRow)
            {
                var paramValue = node.ParameterValueRepresentation;
                double rowMidY = node.Y + NodeHeaderHeight + HookRowHeight / 2.0;

                // Subtle tinted background for readability
                ctx.DrawRectangle(_isLight ? ParamValueBgL : ParamValueBg, null,
                    new Rect(node.X + 1, node.Y + NodeHeaderHeight, rw - 2, HookRowHeight));

                const double textPad = 6.0;
                // Skip drawing the text while this exact node is being edited —
                // the inline TextBox (and syntax overlay) already cover that row.
                if (node != _editingParamNode)
                {
                    var display  = string.IsNullOrEmpty(paramValue) ? "(no value)" : paramValue;
                    var paramFt  = MakeText(display, HookFontSize, _isLight ? ParamValueTextBrushL : ParamValueTextBrush);
                    double maxW  = rw - textPad * 2;
                    double paramTy = rowMidY - paramFt.Height / 2.0;
                    using (ctx.PushClip(new Rect(node.X + textPad, paramTy, Math.Max(0, maxW), paramFt.Height + 1)))
                        ctx.DrawText(paramFt, new Point(node.X + textPad, paramTy));
                }

                rowOffset = 1;

                // Separator below the value row when hooks follow
                if (hasHooks)
                {
                    double sepY = node.Y + NodeHeaderHeight + HookRowHeight;
                    ctx.DrawLine(new Pen(_isLight ? HookDividerBrushL : HookDividerBrush, 0.5),
                        new Point(node.X + 1,      sepY),
                        new Point(node.X + rw - 1, sepY));
                }
            }

            if (!hasHooks)
                continue;

            _nodeConnectedHooks.TryGetValue(node, out var connected);

            for (int i = 0; i < hooks!.Count; i++)
            {
                var hook  = hooks[i];
                bool conn = connected is not null && connected.Contains(hook);
                // Is this hook occupied by an inlined BasicParameter?
                bool hasInlined = _hookInlinedParam.TryGetValue((node, hook), out var inlinedParam);

                // Unsatisfied: required cardinality with no connection at all.
                bool isRequired  = hook.Cardinality == HookCardinality.Single
                                || hook.Cardinality == HookCardinality.AtLeastOne;
                bool unsatisfied = isRequired && !conn && !hasInlined;

                double rowMidY = node.Y + NodeHeaderHeight + (rowOffset + i) * HookRowHeight + HookRowHeight / 2.0;
                double rowTopY = node.Y + NodeHeaderHeight + (rowOffset + i) * HookRowHeight;

                // Tinted background: red for unsatisfied required hooks, amber for inlined params.
                if (unsatisfied)
                    ctx.DrawRectangle(HookUnsatisfiedRowBg, null,
                        new Rect(node.X + 1, rowTopY, rw - 2, HookRowHeight));
                else if (hasInlined)
                    ctx.DrawRectangle(InlineParamRowBg, null,
                        new Rect(node.X + 1, rowTopY, rw - 2, HookRowHeight));

                // Dot on the right edge (the link anchor).
                // Red for unsatisfied required hooks, green for connected/inlined, grey otherwise.
                var dotBrush = unsatisfied          ? (_isLight ? HookUnsatisfiedBrushL : HookUnsatisfiedBrush)
                             : (conn || hasInlined)  ? (_isLight ? HookConnectedBrushL   : HookConnectedBrush)
                             :                         (_isLight ? HookUnconnectedBrushL  : HookUnconnectedBrush);
                ctx.DrawEllipse(dotBrush, null,
                    new Point(node.X + rw, rowMidY),
                    HookDotRadius, HookDotRadius);

                // Hook name + optional inlined value
                const double textPad = 6.0;
                string hookLabel = hasInlined && inlinedParam is not null
                    ? $"{hook.Name}: {(string.IsNullOrEmpty(inlinedParam.ParameterValueRepresentation) ? "(no value)" : inlinedParam.ParameterValueRepresentation)}"
                    : hook.Name;
                IBrush hookTextBrush = unsatisfied ? (_isLight ? HookTextUnsatisfiedBrushL : HookTextUnsatisfiedBrush)
                                     : hasInlined  ? (_isLight ? ParamValueTextBrushL       : ParamValueTextBrush)
                                     : conn        ? (_isLight ? HookTextConnBrushL          : HookTextConnBrush)
                                     :               (_isLight ? HookTextDimBrushL           : HookTextDimBrush);
                var hookFt   = MakeText(hookLabel, HookFontSize, hookTextBrush);
                double maxW  = rw - textPad * 2 - HookDotRadius * 2;
                double hookTy = rowMidY - hookFt.Height / 2.0;
                using (ctx.PushClip(new Rect(node.X + textPad, hookTy, Math.Max(0, maxW), hookFt.Height + 1)))
                    ctx.DrawText(hookFt, new Point(node.X + textPad, hookTy));

                // Row separator (skip after last row)
                if (i < hooks.Count - 1)
                {
                    double sepY = node.Y + NodeHeaderHeight + (rowOffset + i + 1) * HookRowHeight;
                    ctx.DrawLine(new Pen(_isLight ? HookDividerBrushL : HookDividerBrush, 0.5),
                        new Point(node.X + 1,      sepY),
                        new Point(node.X + rw - 1, sepY));
                }
            }
        }
    }

    /// <summary>
    /// Updates <see cref="_hoveredParameterNode"/> based on which node (if any) the
    /// pointer currently sits over, and invalidates the visual when the value changes.
    /// </summary>
    private void RenderGhostNodes(DrawingContext ctx)
    {
        foreach (var ghost in _vm!.GhostNodes)
        {
            double rw = ghost.Width;
            double rh = ghost.Height;
            var rect  = new Rect(ghost.X, ghost.Y, rw, rh);

            // Fill is semi-transparent; border is dashed.
            var borderBrush = ghost.IsSelected ? GhostNodeSelBrush : (_isLight ? GhostNodeBorderBrushL : GhostNodeBorderBrush);
            var border      = new Pen(borderBrush, NodeBorderThickness, dashStyle: GhostNodeDash);

            DrawRectGlow(ctx, rect, NodeCornerRadius, ghost.IsSelected ? SelectionGlowColor : (_isLight ? GhostGlowColorL : GhostGlowColor));
            ctx.DrawRectangle(_isLight ? GhostNodeFillL : GhostNodeFill, border, rect, NodeCornerRadius, NodeCornerRadius);

            // Ghost icon prefix ("⊙ ") to distinguish from real nodes at a glance.
            var labelText = "\u2299 " + ghost.Name;
            var ft = MakeText(labelText, NodeFontSize, _isLight ? NodeTextBrushL : NodeTextBrush);
            var tx = ghost.X + (rw      - ft.Width)  / 2;
            var ty = ghost.Y + (NodeHeaderHeight - ft.Height) / 2;

            using (ctx.PushClip(new Rect(ghost.X + 4, ghost.Y, rw - 8, NodeHeaderHeight)))
                ctx.DrawText(ft, new Point(tx, ty));

            // Resize grip dots (bottom-right corner).
            {
                double dotR = 2.0;
                double bx   = ghost.X + rw;
                double by   = ghost.Y + rh;
                for (int d = 0; d < 3; d++)
                {
                    double offset = 4.0 + d * 4.0;
                    ctx.DrawEllipse(_isLight ? ResizeHandleBrushL : ResizeHandleBrush, null,
                        new Point(bx - offset + dotR, by - dotR), dotR, dotR);
                    ctx.DrawEllipse(_isLight ? ResizeHandleBrushL : ResizeHandleBrush, null,
                        new Point(bx - dotR,           by - offset + dotR), dotR, dotR);
                }
            }
        }
    }

    private void RenderStarts(DrawingContext ctx)
    {
        foreach (var start in _vm!.Starts)
        {
            var fill   = start.IsSelected ? StartSelFill : StartFill;
            var center = new Point(start.CenterX, start.CenterY);
            var r      = StartViewModel.Radius;
            var border = new Pen(start.IsSelected ? NodeSelBrush : (_isLight ? NodeBorderBrushL : NodeBorderBrush), NodeBorderThickness);

            DrawEllipseGlow(ctx, center, r, r, start.IsSelected ? SelectionGlowColor : StartGlowColor);
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
    /// <summary>Updates the zoom bar's colours to match the current light/dark theme.</summary>
    private void UpdateZoomBarColors(bool isLight)
    {
        _zoomBarIsLight = isLight;
        if (isLight)
        {
            // Light neon pill palette
            _zoomBar.Background     = new SolidColorBrush(Color.FromArgb(0xF4, 0xF8, 0xFF, 0xEE));
            _zoomBar.BorderBrush    = new SolidColorBrush(Color.FromRgb(0x00, 0x66, 0xCC));
            _zoomTextBox.Background = new SolidColorBrush(Color.FromArgb(0x0A, 0x00, 0x00, 0x00));
            _zoomTextBox.Foreground  = new SolidColorBrush(Color.FromRgb(0x00, 0x30, 0x88));
            _zoomMinusBtn.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x30, 0x88));
            _zoomPlusBtn.Foreground  = new SolidColorBrush(Color.FromRgb(0x00, 0x30, 0x88));
        }
        else
        {
            // Dark neon pill palette
            _zoomBar.Background     = new SolidColorBrush(Color.FromArgb(0xE6, 0x05, 0x05, 0x10));
            _zoomBar.BorderBrush    = new SolidColorBrush(Color.FromRgb(0x00, 0xD4, 0xFF));
            _zoomTextBox.Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
            _zoomTextBox.Foreground  = new SolidColorBrush(Color.FromRgb(0xEE, 0xFF, 0xFF));
            _zoomMinusBtn.Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xFF, 0xFF));
            _zoomPlusBtn.Foreground  = new SolidColorBrush(Color.FromRgb(0xEE, 0xFF, 0xFF));
        }
    }

    /// <summary>
    /// Draws a neon glow halo around a rounded rectangle using 5 outward-expanding semi-transparent
    /// layers of <paramref name="glowColor"/>, fading from fully transparent at the outer edge to
    /// relatively vivid just inside the object border.
    /// </summary>
    private static void DrawRectGlow(DrawingContext ctx, Rect rect, double cornerRadius, Color glowColor)
    {
        ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x08, glowColor.R, glowColor.G, glowColor.B)), null, rect.Inflate(14), cornerRadius + 14, cornerRadius + 14);
        ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x10, glowColor.R, glowColor.G, glowColor.B)), null, rect.Inflate(9),  cornerRadius + 9,  cornerRadius + 9);
        ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x1E, glowColor.R, glowColor.G, glowColor.B)), null, rect.Inflate(5),  cornerRadius + 5,  cornerRadius + 5);
        ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x34, glowColor.R, glowColor.G, glowColor.B)), null, rect.Inflate(2.5), cornerRadius + 2.5, cornerRadius + 2.5);
        ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x50, glowColor.R, glowColor.G, glowColor.B)), null, rect.Inflate(1),  cornerRadius + 1,  cornerRadius + 1);
    }

    /// <summary>
    /// Draws a neon glow halo around an ellipse using 5 outward-expanding semi-transparent layers of
    /// <paramref name="glowColor"/>, fading from fully transparent at the outer edge inward.
    /// </summary>
    private static void DrawEllipseGlow(DrawingContext ctx, Point center, double rx, double ry, Color glowColor)
    {
        ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x08, glowColor.R, glowColor.G, glowColor.B)), null, center, rx + 14, ry + 14);
        ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x10, glowColor.R, glowColor.G, glowColor.B)), null, center, rx + 9,  ry + 9);
        ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x1E, glowColor.R, glowColor.G, glowColor.B)), null, center, rx + 5,  ry + 5);
        ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x34, glowColor.R, glowColor.G, glowColor.B)), null, center, rx + 2.5, ry + 2.5);
        ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x50, glowColor.R, glowColor.G, glowColor.B)), null, center, rx + 1,  ry + 1);
    }

    private static FormattedText MakeText(string text, double size, IBrush foreground) =>
        new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            DefaultTypeface,
            size,
            foreground);

    // ── Hit testing / mouse interaction ──────────────────────────────────
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_vm is null) return;
        else if (e.Key is Key.Delete or Key.Back)
        {
            if (_multiSelection.Count > 1)
            {
                // Snapshot the set before clearing so deletions don't mutate it mid-loop.
                var toDelete = _multiSelection.ToList();
                ClearMultiSelection();
                _ = _vm.DeleteMultipleAsync(toDelete);
            }
            else
            {
                _vm.DeleteSelectedCommand.Execute(null);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.D0 && (e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            ApplyScale(1.0);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ClearMultiSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.F2 && _vm?.SelectedElement is not null)
        {
            var sel = _vm.SelectedElement;
            if (sel is NodeViewModel or StartViewModel
                     or FunctionTemplateViewModel or FunctionInstanceViewModel
                     or FunctionParameterViewModel)
            {
                BeginNameEdit(sel);
                e.Handled = true;
            }
            else if (sel is CommentBlockViewModel cmt)
            {
                BeginCommentEdit(cmt);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Left && (e.KeyModifiers & KeyModifiers.Alt) != 0)
        {
            // Alt+Left: navigate to parent boundary / exit function template.
            _vm?.NavigateUpCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ── Scaling helpers ───────────────────────────────────────────────────
    /// <summary>
    /// Converts a pointer position in this control's coordinate space (screen pixels) to
    /// model/canvas coordinates by dividing by the current scale factor.
    /// </summary>
    private Point ToCanvasPos(Point screenPos) => new Point(screenPos.X / _scale, screenPos.Y / _scale);

    /// <summary>
    /// Sets a new scale factor, clamped to [<see cref="ScaleMin"/>, <see cref="ScaleMax"/>].
    /// Adjusts the scroll offset so the viewport centre remains on the same model coordinate.
    /// </summary>
    /// <summary>
    /// Change the canvas scale to <paramref name="newScale"/>.
    /// When <paramref name="canvasPivot"/> is supplied the scroll offset is adjusted so that
    /// the model coordinate under the pivot point stays fixed (zoom-to-cursor).  When it is
    /// <c>null</c> the viewport centre is kept fixed instead.
    /// <paramref name="canvasPivot"/> must be in canvas-local coordinates
    /// (i.e. <c>e.GetPosition(this)</c> from a pointer event).
    /// </summary>
    private void ApplyScale(double newScale, Point? canvasPivot = null)
    {
        newScale = Math.Clamp(Math.Round(newScale, 2), ScaleMin, ScaleMax);
        if (Math.Abs(newScale - _scale) < 0.005) return;
        var sv = GetScrollViewer();
        double prevScale = _scale;
        _scale = newScale;
        _zoomTextBox.Text = $"{(int)Math.Round(_scale * 100)}%";
        if (sv is not null)
        {
            double ratio = newScale / prevScale;
            if (canvasPivot.HasValue)
            {
                // Keep the model point under the cursor fixed in the viewport.
                // canvasPivot is in canvas-local pixels (scroll-offset included).
                // newOffset = pivot * (ratio - 1) + oldOffset
                sv.Offset = new Vector(
                    Math.Max(0, canvasPivot.Value.X * (ratio - 1) + sv.Offset.X),
                    Math.Max(0, canvasPivot.Value.Y * (ratio - 1) + sv.Offset.Y));
            }
            else
            {
                // Keep the viewport centre fixed on the same model coordinate.
                double cx = sv.Offset.X + sv.Viewport.Width  / 2.0;
                double cy = sv.Offset.Y + sv.Viewport.Height / 2.0;
                sv.Offset = new Vector(
                    Math.Max(0, cx * ratio - sv.Viewport.Width  / 2.0),
                    Math.Max(0, cy * ratio - sv.Viewport.Height / 2.0));
            }
        }
        InvalidateAndMeasure();
    }

    private void TryApplyZoomText()
    {
        var text = (_zoomTextBox.Text ?? string.Empty).TrimEnd('%').Trim();
        if (double.TryParse(text, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double pct) && pct >= 1)
            ApplyScale(pct / 100.0);
        else
            _zoomTextBox.Text = $"{(int)Math.Round(_scale * 100)}%";
    }

    private void OnZoomTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return)
        {
            TryApplyZoomText();
            e.Handled = true;
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if ((e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            // Pass the canvas-local mouse position so the zoom is centred on the cursor.
            var pivot = e.GetPosition(this);
            ApplyScale(_scale + (e.Delta.Y > 0 ? ScaleStep : -ScaleStep), pivot);
            e.Handled = true;
            return;
        }
        base.OnPointerWheelChanged(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_vm is null) return;

        // ── Mouse back button (XButton1): navigate to parent scope ───────────────
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.XButton1Pressed)
        {
            _vm.NavigateUpCommand.Execute(null);
            e.Handled = true;
            return;
        }

        var point = e.GetCurrentPoint(this);
        var pos   = point.Position;           // screen coords
        var mpos  = ToCanvasPos(pos);         // model coords
        bool isRightButton = point.Properties.IsRightButtonPressed;
        bool isCtrlLeft    = !isRightButton
                             && point.Properties.IsLeftButtonPressed
                             && (e.KeyModifiers & KeyModifiers.Control) != 0;

        // Right-click begins a link-creation drag.  Ctrl+left-click is reserved for multi-selection.
        bool isLinkDrag = isRightButton;

        // ── Resize handle press (left button) ────────────────────────────
        if (!isLinkDrag && !isCtrlLeft)
        {
            var resizeHit = HitTestResizeHandle(mpos);
            if (resizeHit is not null)
            {
                if (_editingParamNode    is not null) CommitParamEdit();
                if (_editingCommentBlock is not null) CommitCommentEdit();
                if (_editingNameElement  is not null) CommitNameEdit();
                ClearMultiSelection();
                _resizing       = resizeHit;
                _resizeStartPos = mpos;
                _resizeStartW   = ElementRenderWidth(resizeHit);
                _resizeStartH   = ElementRenderHeight(resizeHit);
                _vm.SelectElementCommand.Execute(resizeHit);
                e.Pointer.Capture(this);
                Focus();
                e.Handled = true;
                return;
            }
        }

        // ── Minimize-to-inline button (BasicParameter header top-left) ───
        if (!isLinkDrag && !isCtrlLeft)
        {
            var minimizeHit = HitTestMinimizeButton(mpos);
            if (minimizeHit is not null)
            {
                if (_editingParamNode    is not null) CommitParamEdit();
                if (_editingCommentBlock is not null) CommitCommentEdit();
                if (_editingNameElement  is not null) CommitNameEdit();
                minimizeHit.InlineBasicParameter();
                InvalidateAndMeasure();
                e.Handled = true;
                return;
            }
        }

        // ── Inline parameter value edit (single left click on param row) ──
        if (!isLinkDrag && !isCtrlLeft)
        {
            // Regular parameter value row (node is visible on canvas).
            var paramRowHit = HitTestParamValueRow(mpos);
            if (paramRowHit is not null)
            {
                _vm.SelectElementCommand.Execute(paramRowHit);
                BeginParamEdit(paramRowHit);
                e.Handled = true;
                return;
            }
            // Inlined BasicParameter hook row inside the origin node.
            var inlinedRowHit = HitTestInlinedParamRow(mpos);
            if (inlinedRowHit is not null)
            {
                var (originNode, _, inlinedParam, rx, ry, rw2) = inlinedRowHit.Value;
                _vm.SelectElementCommand.Execute(originNode);
                BeginParamEdit(inlinedParam, rx, ry, rw2);
                e.Handled = true;
                return;
            }
            // Clicking elsewhere commits any open edit.
            // Guard: if the click was already handled by a child (e.g. the variable
            // autocomplete dropdown's TextBlock items), do not commit the edit.
            if (!e.Handled)
            {
                if (_editingParamNode    is not null) CommitParamEdit();
                if (_editingCommentBlock is not null) CommitCommentEdit();
                if (_editingNameElement  is not null) CommitNameEdit();
            }
        }

        // ── Hook toggle icon click (left button, any click count) ─────────
        if (!isLinkDrag && !isCtrlLeft)
        {
            var toggleHit = HitTestHookToggleIcon(mpos);
            if (toggleHit is not null)
            {
                toggleHit.ShowHooks = !toggleHit.ShowHooks;
                InvalidateAndMeasure();
                e.Handled = true;
                return;
            }
        }

        // ── Double-click on a hook dot: create + auto-link a new node ─────
        if (!isLinkDrag && !isCtrlLeft && e.ClickCount == 2)
        {
            var hookHit = HitTestHook(mpos);
            if (hookHit is { } hh)
            {
                _ = _vm.CreateNodeFromHookAsync(hh.node, hh.hook, hh.anchor.X, hh.anchor.Y);
                e.Handled = true;
                return;
            }

            // ── Double-click on a parameter node: open the value editor ────
            var nodeHit = HitTest(mpos, testComments: false) as NodeViewModel;
            if (nodeHit is { IsParameterNode: true })
            {
                _ = _vm.EditParameterNodeAsync(nodeHit);
                e.Handled = true;
                return;
            }
            // ── Double-click on a regular node: begin inline rename ────────
            if (nodeHit is not null)
            {
                _vm.SelectElementCommand.Execute(nodeHit);
                BeginNameEdit(nodeHit);
                e.Handled = true;
                return;
            }
            // ── Double-click on a start: begin inline rename ──────────────
            var startHit = HitTest(mpos, testComments: false) as StartViewModel;
            if (startHit is not null)
            {
                _vm.SelectElementCommand.Execute(startHit);
                BeginNameEdit(startHit);
                e.Handled = true;
                return;
            }

            // ── Double-click on a comment block: open the inline comment editor ──
            var commentHit = HitTest(mpos, testComments: true) as CommentBlockViewModel;
            if (commentHit is not null)
            {
                _vm.SelectElementCommand.Execute(commentHit);
                BeginCommentEdit(commentHit);
                e.Handled = true;
                return;
            }

            // ── Double-click on a function template: navigate into it ─────
            var ftHit = HitTest(mpos, testComments: false) as FunctionTemplateViewModel;
            if (ftHit is not null)
            {
                _vm.NavigateIntoFunctionTemplate(ftHit);
                e.Handled = true;
                return;
            }

            // ── Double-click on a function instance: begin inline rename ──
            var fiHit = HitTest(mpos, testComments: false) as FunctionInstanceViewModel;
            if (fiHit is not null)
            {
                _vm.SelectElementCommand.Execute(fiHit);
                BeginNameEdit(fiHit);
                e.Handled = true;
                return;
            }

            // ── Double-click on a function parameter: begin inline rename ──
            var fpHit = HitTest(mpos, testComments: false) as FunctionParameterViewModel;
            if (fpHit is not null)
            {
                _vm.SelectElementCommand.Execute(fpHit);
                BeginNameEdit(fpHit);
                e.Handled = true;
                return;
            }
        }

        // For right-click (link creation) we exclude comment blocks; for all other paths we include them.
        ICanvasElement? hit = HitTest(mpos, testComments: !isRightButton);

        if (isLinkDrag)
        {
            // Track right-button press so we can detect a "no-drag" context-menu click on release.
            if (isRightButton)
            {
                _rightClickPending  = true;
                _rightClickPressPos = pos;                              // screen coords for distance threshold
                _rightClickElement  = HitTest(mpos, testComments: true);
                _rightClickLink     = _rightClickElement is null ? HitTestLink(mpos) : null;
                // Also check whether a hook dot was right-clicked on a node.
                var hookHit = HitTestHook(mpos);
                _rightClickHookHit  = hookHit.HasValue ? (hookHit.Value.node, hookHit.Value.hook) : null;
                var fiHookHit = HitTestFiHook(mpos);
                _rightClickFiHookHit = fiHookHit;
            }

            // Begin link-creation drag from a node, start, or function instance (via its FunctionParameterHooks).
            // Comment blocks are not valid link origins.
            if (hit is NodeViewModel or StartViewModel
                || (hit is FunctionInstanceViewModel hitFi && hitFi.FunctionParameters.Count > 0))
            {
                _linkOrigin     = hit;
                _linkCurrentPos = mpos;
                e.Pointer.Capture(this);
                Focus();
                e.Handled = true;
            }
            return;
        }

        // ── Ctrl+left-click: multi-selection or rubber-band rectangle ─────
        if (isCtrlLeft)
        {
            // Always commit any open inline edit first.
            if (_editingParamNode    is not null) CommitParamEdit();
            if (_editingCommentBlock is not null) CommitCommentEdit();
            if (_editingNameElement  is not null) CommitNameEdit();

            if (hit is NodeViewModel or CommentBlockViewModel or GhostNodeViewModel or FunctionTemplateViewModel)
            {
                // On the very first Ctrl+click, absorb the existing primary selection into the set.
                if (_multiSelection.Count == 0 && _vm.SelectedElement is not null
                    && !ReferenceEquals(_vm.SelectedElement, hit))
                {
                    _multiSelection.Add(_vm.SelectedElement);
                    // _vm.SelectedElement.IsSelected is already true — no change needed
                }

                if (_multiSelection.Contains(hit))
                {
                    // Toggle off: remove from multi-selection.
                    _multiSelection.Remove(hit);
                    hit.IsSelected = false;
                    // If the deselected element was the primary, pick the next available.
                    if (ReferenceEquals(_vm.SelectedElement, hit))
                        _vm.SelectedElement = _multiSelection.FirstOrDefault();
                }
                else
                {
                    // Toggle on: add to multi-selection.
                    _multiSelection.Add(hit);
                    hit.IsSelected = true;
                    // Reflect the most recently touched element in the property panel.
                    _vm.SelectedElement = hit;
                }
                InvalidateVisual();
            }
            else
            {
                // Ctrl+drag on empty space → begin a rubber-band selection rectangle.
                ClearMultiSelection();
                _vm.SelectElementCommand.Execute(null);
                _selRectStart   = mpos;
                _selRectCurrent = mpos;
                e.Pointer.Capture(this);
            }

            Focus();
            e.Handled = true;
            return;
        }

        // ── Left button: normal select + drag ─────────────────────────────
        if (hit is not null)
        {
            bool hitIsInMultiSel = _multiSelection.Contains(hit);
            if (!hitIsInMultiSel)
            {
                // Clicking an element that is not part of the current group resets the selection.
                ClearMultiSelection();
                _vm.SelectElementCommand.Execute(hit);
            }
            _dragging         = hit;
            _dragOffset       = new Point(mpos.X - hit.X, mpos.Y - hit.Y);
            _groupDragLastPos = mpos;
            e.Pointer.Capture(this);
        }
        else
        {
            // No element hit — try links.
            var linkHit = HitTestLink(mpos);
            if (linkHit is not null)
            {
                ClearMultiSelection();
                _vm.SelectLinkCommand.Execute(linkHit);
            }
            else
            {
                // Truly empty space — deselect all and begin canvas pan.
                ClearMultiSelection();
                _vm.SelectElementCommand.Execute(null);
                var sv = GetScrollViewer();
                if (sv is not null)
                {
                    _panning           = true;
                    _panStartScrollPos = e.GetCurrentPoint(sv).Position;
                    _panStartOffset    = sv.Offset;
                    Cursor = new Cursor(StandardCursorType.SizeAll);
                    e.Pointer.Capture(this);
                }
            }
        }

        Focus();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos  = e.GetCurrentPoint(this).Position;  // screen coords
        var mpos = ToCanvasPos(pos);                  // model coords

        // Right-drag: update pending link preview.
        if (_linkOrigin is not null)
        {
            _linkCurrentPos = mpos;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        // ── Resize drag ───────────────────────────────────────────────────
        if (_resizing is not null)
        {
            var dw = mpos.X - _resizeStartPos.X;
            var dh = mpos.Y - _resizeStartPos.Y;
            if (_resizing is NodeViewModel resizingNode)
                resizingNode.ResizeToPreview(_resizeStartW + dw, _resizeStartH + dh);
            else if (_resizing is CommentBlockViewModel resizingComment)
                resizingComment.ResizeToPreview(_resizeStartW + dw, _resizeStartH + dh);
            else if (_resizing is GhostNodeViewModel resizingGhost)
                resizingGhost.ResizeToPreview(_resizeStartW + dw, _resizeStartH + dh);
            else if (_resizing is FunctionTemplateViewModel resizingFt)
                resizingFt.ResizeToPreview(_resizeStartW + dw, _resizeStartH + dh);
            else if (_resizing is FunctionInstanceViewModel resizingFi)
                resizingFi.ResizeToPreview(_resizeStartW + dw, _resizeStartH + dh);
            else if (_resizing is FunctionParameterViewModel resizingFp)
                resizingFp.ResizeToPreview(_resizeStartW + dw, _resizeStartH + dh);
            InvalidateAndMeasure();
            e.Handled = true;
            return;
        }

        // ── Canvas pan drag ───────────────────────────────────────────────
        if (_panning)
        {
            var sv = GetScrollViewer();
            if (sv is not null)
            {
                var currentScrollPos = e.GetCurrentPoint(sv).Position;
                var dx = currentScrollPos.X - _panStartScrollPos.X;
                var dy = currentScrollPos.Y - _panStartScrollPos.Y;
                sv.Offset = new Vector(
                    Math.Max(0, _panStartOffset.X - dx),
                    Math.Max(0, _panStartOffset.Y - dy));
            }
            e.Handled = true;
            return;
        }

        // ── Rubber-band selection rectangle (Ctrl+drag on empty space) ────
        if (_selRectStart is not null)
        {
            _selRectCurrent = mpos;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        // ── Cursor feedback while idle ────────────────────────────────────
        if (_dragging is null)
        {
            Cursor = HitTestResizeHandle(mpos) is not null
                ? new Cursor(StandardCursorType.SizeAll)
                : Cursor.Default;
        }

        if (_dragging is null) return;

        // ── Element drag (single or group) ────────────────────────────────
        if (_multiSelection.Count > 1 && _multiSelection.Contains(_dragging))
        {
            // Group drag: preview every element in the multi-selection by the per-frame delta.
            var dx = mpos.X - _groupDragLastPos.X;
            var dy = mpos.Y - _groupDragLastPos.Y;
            _groupDragLastPos = mpos;
            foreach (var el in _multiSelection)
            {
                double nx = Math.Max(0, el.X + dx);
                double ny = Math.Max(0, el.Y + dy);
                if      (el is NodeViewModel       gnvm) gnvm.MoveToPreview(nx, ny);
                else if (el is StartViewModel       gsvm) gsvm.MoveToPreview(nx, ny);
                else if (el is CommentBlockViewModel gcvm) gcvm.MoveToPreview(nx, ny);
                else if (el is GhostNodeViewModel   ggvm) ggvm.MoveToPreview(nx, ny);
                else if (el is FunctionTemplateViewModel gftvm) gftvm.MoveToPreview(nx, ny);
                else if (el is FunctionInstanceViewModel gfivm) gfivm.MoveToPreview(nx, ny);
                else if (el is FunctionParameterViewModel gfpvm) gfpvm.MoveToPreview(nx, ny);
            }
        }
        else
        {
            // Single-element drag: preview only, no session command issued yet.
            var newX = Math.Max(0, mpos.X - _dragOffset.X);
            var newY = Math.Max(0, mpos.Y - _dragOffset.Y);
            if (_dragging is NodeViewModel         nvm) nvm.MoveToPreview(newX, newY);
            if (_dragging is StartViewModel         svm) svm.MoveToPreview(newX, newY);
            if (_dragging is CommentBlockViewModel  cvm) cvm.MoveToPreview(newX, newY);
            if (_dragging is GhostNodeViewModel    gvm) gvm.MoveToPreview(newX, newY);
            if (_dragging is FunctionTemplateViewModel ftvm) ftvm.MoveToPreview(newX, newY);
            if (_dragging is FunctionInstanceViewModel fivm) fivm.MoveToPreview(newX, newY);
            if (_dragging is FunctionParameterViewModel fpvm2) fpvm2.MoveToPreview(newX, newY);
        }

        InvalidateAndMeasure();
        e.Handled = true;
    }

    /// <summary>
    /// Suppress Avalonia's automatic context-menu opening on right-click release.
    /// The canvas shows the context menu manually in <see cref="OnPointerReleased"/>
    /// only when the pointer has moved less than the drag threshold, so we must
    /// prevent the framework from opening it independently.
    /// </summary>
    private void SuppressContextRequested(object? sender, ContextRequestedEventArgs e)
        => e.Handled = true;

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        // ── Right-click with minimal movement: show context menu ──────────
        if (_rightClickPending)
        {
            _rightClickPending = false;
            var rPos = e.GetCurrentPoint(this).Position;
            var rdx  = rPos.X - _rightClickPressPos.X;
            var rdy  = rPos.Y - _rightClickPressPos.Y;

            if (Math.Sqrt(rdx * rdx + rdy * rdy) < 3.0)
            {
                _linkOrigin = null;
                e.Pointer.Capture(null);
                InvalidateVisual();
                ShowContextMenu(_rightClickElement, _rightClickLink);
                e.Handled = true;
                return;
            }
        }

        // ── Right-button release: complete link creation ──────────────────
        if (_linkOrigin is not null)
        {
            var origin = _linkOrigin;
            _linkOrigin = null;
            e.Pointer.Capture(null);
            InvalidateVisual();

            if (_vm is not null)
            {
                var pos = e.GetCurrentPoint(this).Position;
                var hit = HitTest(ToCanvasPos(pos), testComments: false);
                // Accepts NodeViewModel or FunctionInstanceViewModel as a link destination.
                if (hit is NodeViewModel dest && !ReferenceEquals(dest, origin))
                    _ = _vm.CreateLinkAsync(origin, dest);
                else if (hit is FunctionInstanceViewModel fiDest
                    && !ReferenceEquals(fiDest, origin)
                    && fiDest.UnderlyingInstance.Template.EntryNode is not null)
                    _ = _vm.CreateLinkAsync(origin, fiDest);
                else if (hit is FunctionParameterViewModel fpDest
                    && !ReferenceEquals(fpDest, origin))
                    _ = _vm.CreateLinkAsync(origin, fpDest);
            }

            e.Handled = true;
            return;
        }

        // ── Left-button release: end resize drag ─────────────────────────
        if (_resizing is not null)
        {
            // Commit the final size to the session (single undo entry).
            if (_resizing is NodeViewModel committingNode)
                committingNode.CommitResize();
            else if (_resizing is CommentBlockViewModel committingComment)
                committingComment.CommitResize();
            else if (_resizing is GhostNodeViewModel committingGhost)
                committingGhost.CommitResize();
            else if (_resizing is FunctionTemplateViewModel committingFt)
                committingFt.CommitResize();
            else if (_resizing is FunctionInstanceViewModel committingFi)
                committingFi.CommitResize();
            else if (_resizing is FunctionParameterViewModel committingFp)
                committingFp.CommitResize();
            _resizing = null;
            e.Pointer.Capture(null);
            Cursor = Cursor.Default;
            InvalidateAndMeasure();
            e.Handled = true;
            return;
        }

        // ── Left-button release: end canvas pan ──────────────────────────
        if (_panning)
        {
            _panning = false;
            e.Pointer.Capture(null);
            Cursor = Cursor.Default;
            e.Handled = true;
            return;
        }

        // ── Left-button release: finalize rubber-band selection ───────────
        if (_selRectStart is not null)
        {
            var finalRect = NormalizeRect(_selRectStart.Value, _selRectCurrent);
            _selRectStart = null;
            e.Pointer.Capture(null);

            if (_vm is not null && (finalRect.Width > 2 || finalRect.Height > 2))
            {
                ICanvasElement? firstHit = null;
                foreach (var node in _vm.Nodes)
                {
                    if (node.IsInlined) continue;
                    var nr = new Rect(node.X, node.Y, NodeRenderWidth(node), NodeRenderHeight(node));
                    if (finalRect.Intersects(nr))
                    {
                        _multiSelection.Add(node);
                        node.IsSelected = true;
                        firstHit ??= node;
                    }
                }
                foreach (var comment in _vm.CommentBlocks)
                {
                    var cr = new Rect(comment.X, comment.Y, comment.Width, comment.Height);
                    if (finalRect.Intersects(cr))
                    {
                        _multiSelection.Add(comment);
                        comment.IsSelected = true;
                        firstHit ??= comment;
                    }
                }
                foreach (var ghost in _vm.GhostNodes)
                {
                    var gr = new Rect(ghost.X, ghost.Y, ghost.Width, ghost.Height);
                    if (finalRect.Intersects(gr))
                    {
                        _multiSelection.Add(ghost);
                        ghost.IsSelected = true;
                        firstHit ??= ghost;
                    }
                }
                foreach (var start in _vm.Starts)
                {
                    var sr = new Rect(start.X, start.Y, start.Diameter, start.Diameter);
                    if (finalRect.Intersects(sr))
                    {
                        _multiSelection.Add(start);
                        start.IsSelected = true;
                        firstHit ??= start;
                    }
                }
                foreach (var ft in _vm.FunctionTemplates)
                {
                    var ftr = new Rect(ft.X, ft.Y, ft.Width, ft.Height);
                    if (finalRect.Intersects(ftr))
                    {
                        _multiSelection.Add(ft);
                        ft.IsSelected = true;
                        firstHit ??= ft;
                    }
                }
                if (firstHit is not null)
                    _vm.SelectedElement = firstHit;
            }

            InvalidateVisual();
            e.Handled = true;
            return;
        }

        // ── Left-button release: end element drag ─────────────────────────
        if (_dragging is null) return;

        // Commit the preview position as a single session command (one undo entry).
        if (_multiSelection.Count > 1 && _multiSelection.Contains(_dragging))
        {
            foreach (var el in _multiSelection)
            {
                if      (el is NodeViewModel       gnvm) gnvm.CommitMove();
                else if (el is StartViewModel       gsvm) gsvm.CommitMove();
                else if (el is CommentBlockViewModel gcvm) gcvm.CommitMove();
                else if (el is GhostNodeViewModel   ggvm) ggvm.CommitMove();
                else if (el is FunctionTemplateViewModel gftvm) gftvm.CommitMove();
                else if (el is FunctionInstanceViewModel gfivm) gfivm.CommitMove();
                else if (el is FunctionParameterViewModel gfpvm) gfpvm.CommitMove();
            }
        }
        else
        {
            if      (_dragging is NodeViewModel        nvm) nvm.CommitMove();
            else if (_dragging is StartViewModel        svm) svm.CommitMove();
            else if (_dragging is CommentBlockViewModel cvm) cvm.CommitMove();
            else if (_dragging is GhostNodeViewModel   gvm) gvm.CommitMove();
            else if (_dragging is FunctionTemplateViewModel ftvm) ftvm.CommitMove();
            else if (_dragging is FunctionInstanceViewModel fivm) fivm.CommitMove();
            else if (_dragging is FunctionParameterViewModel fpvm) fpvm.CommitMove();
        }

        _dragging = null;
        e.Pointer.Capture(null);
        InvalidateAndMeasure();
        e.Handled = true;
    }

    /// <summary>
    /// Opens a context menu for the given canvas element or link.
    /// The element/link is selected on the VM before the menu opens so that
    /// the Delete command operates on the correct target.
    /// </summary>
    private void ShowContextMenu(ICanvasElement? element, LinkViewModel? link)
    {
        if (_vm is null) return;

        // ── Background right-click: offer Add items when nothing was hit ──
        if (element is null && link is null)
        {
            var bgMenu = new ContextMenu();
            var vm2 = _vm;
            var spawnPt = ToCanvasPos(_rightClickPressPos);

            var addStartItem = new MenuItem { Header = "Add Start…" };
            addStartItem.Click += (_, _) => vm2.AddStartAt(spawnPt.X, spawnPt.Y);
            bgMenu.Items.Add(addStartItem);

            var addModuleItem = new MenuItem { Header = "Add Module…" };
            addModuleItem.Click += (_, _) => _ = vm2.AddModuleAtAsync(spawnPt.X, spawnPt.Y);
            bgMenu.Items.Add(addModuleItem);

            var addCommentItem = new MenuItem { Header = "Add Comment" };
            addCommentItem.Click += (_, _) => vm2.AddCommentBlockAt(spawnPt.X, spawnPt.Y);
            bgMenu.Items.Add(addCommentItem);

            var addFtItem = new MenuItem { Header = "Add Function Template…" };
            addFtItem.Click += (_, _) => _ = vm2.AddFunctionTemplateAtAsync(spawnPt.X, spawnPt.Y);
            bgMenu.Items.Add(addFtItem);

            var addFiItem = new MenuItem { Header = "Add Function Instance…" };
            addFiItem.Click += (_, _) => _ = vm2.AddFunctionInstanceAtAsync(spawnPt.X, spawnPt.Y);
            bgMenu.Items.Add(addFiItem);

            ContextMenu = bgMenu;
            ContextMenu.Open(this);
            return;
        }

        var vm = _vm; // capture for closure

        var menu = new ContextMenu();

        // ── Hook-specific items ───────────────────────────────────────────
        if (_rightClickHookHit is { } hookEntry)
        {
            var capturedNode = hookEntry.Node;
            var capturedHook = hookEntry.Hook;

            // "Expand parameter to its own module" when the hook has an inlined BasicParam.
            if (_hookInlinedParam.TryGetValue((capturedNode, capturedHook), out var inlinedParamNode))
            {
                var capturedParam = inlinedParamNode;
                var expandItem = new MenuItem { Header = "Expand parameter to its own module" };
                expandItem.Click += (_, _) =>
                {
                    double rw = NodeRenderWidth(capturedNode);
                    capturedParam.ExpandToCanvas(capturedNode.X + rw + 30.0, capturedNode.Y);
                };
                menu.Items.Add(expandItem);

                // Offer switching between BasicParameter and ScriptedParameter.
                if (capturedParam.IsBasicParameter || capturedParam.IsScriptedParameter)
                {
                    var switchHeader = capturedParam.IsBasicParameter
                        ? "Switch to Scripted Parameter"
                        : "Switch to Basic Parameter";
                    var switchItem = new MenuItem { Header = switchHeader };
                    switchItem.Click += (_, _) =>
                    {
                        if (!capturedParam.SwitchParameterType(out var err))
                            vm.ShowToast(err?.Message ?? "Could not switch parameter type.",
                                         isError: true, durationMs: 6000);
                    };
                    menu.Items.Add(switchItem);
                }

                menu.Items.Add(new Separator());
            }

            var interBoundaryItem = new MenuItem { Header = "Link to node in another boundary…" };
            interBoundaryItem.Click += (_, _) =>
                _ = vm.CreateInterBoundaryLinkAsync(capturedNode, capturedHook);
            menu.Items.Add(interBoundaryItem);

            // Inside a function template, offer creating a FunctionParameter from a regular node hook.
            if (vm.IsInsideFunctionTemplate && capturedHook is not FunctionParameterHook)
            {
                var spawnPt2 = ToCanvasPos(_rightClickPressPos);
                var addFpItem = new MenuItem { Header = "Add Function Parameter" };
                addFpItem.Click += (_, _) =>
                    _ = vm.AddFunctionParameterFromHookAsync(
                        capturedNode, capturedHook, spawnPt2.X, spawnPt2.Y);
                menu.Items.Add(addFpItem);
            }

            menu.Items.Add(new Separator());
        }

        // ── FunctionInstance hook right-click items ────────────────────────────
        if (_rightClickFiHookHit is { } fiHookEntry)
        {
            var capturedFiOrigin   = fiHookEntry.Fi;
            var capturedFpHook = fiHookEntry.Hook;

            var allBoundariesItem = new MenuItem { Header = "Link to node in another boundary…" };
            allBoundariesItem.Click += (_, _) =>
                _ = vm.CreateInterBoundaryLinkAsync(capturedFiOrigin, capturedFpHook);
            menu.Items.Add(allBoundariesItem);
            menu.Items.Add(new Separator());
        }

        // ── Standard Delete ───────────────────────────────────────────────
        var deleteItem = new MenuItem { Header = "Delete" };
        deleteItem.Click += (_, _) =>
        {
            if (element is not null)
                vm.SelectElementCommand.Execute(element);
            else
                vm.SelectLinkCommand.Execute(link);
            _ = vm.DeleteSelectedCommand.ExecuteAsync(null);
        };

        // ── Variable list management + inline option ────────────────────
        if (element is NodeViewModel paramNode && paramNode.IsParameterNode)
        {
            // "Inline parameter" — only when this BasicParam is wired to a Single hook.
            if (_canInlineNodes.Contains(paramNode))
            {
                var inlineItem = new MenuItem { Header = "Inline parameter into parent hook" };
                inlineItem.Click += (_, _) => paramNode.InlineBasicParameter();
                menu.Items.Add(inlineItem);
            }

            // Offer switching between BasicParameter and ScriptedParameter.
            if (paramNode.IsBasicParameter || paramNode.IsScriptedParameter)
            {
                var switchHeader = paramNode.IsBasicParameter
                    ? "Switch to Scripted Parameter"
                    : "Switch to Basic Parameter";
                var capturedParamNode = paramNode;
                var switchItem = new MenuItem { Header = switchHeader };
                switchItem.Click += (_, _) =>
                {
                    if (!capturedParamNode.SwitchParameterType(out var err))
                        vm.ShowToast(err?.Message ?? "Could not switch parameter type.",
                                     isError: true, durationMs: 6000);
                };
                menu.Items.Add(switchItem);
            }

            menu.Items.Add(new Separator());

            if (vm.IsInsideFunctionTemplate)
            {
                // Inside a FunctionTemplate: only offer local variables for eligible node types.
                if (FunctionTemplate.IsValidLocalVariableNode(paramNode.UnderlyingNode, out _))
                {
                    bool alreadyLocalVar = vm.IsNodeInLocalVariables(paramNode);
                    var localVarHeader = alreadyLocalVar
                        ? "Remove from Local Variables"
                        : "Add as Local Variable";
                    var localVarItem = new MenuItem { Header = localVarHeader };
                    localVarItem.Click += (_, _) =>
                    {
                        if (vm.IsNodeInLocalVariables(paramNode))
                            _ = vm.RemoveNodeFromLocalVariablesAsync(paramNode);
                        else
                            _ = vm.AddNodeToLocalVariablesAsync(paramNode);
                    };
                    menu.Items.Add(localVarItem);
                    menu.Items.Add(new Separator());
                }
            }
            else
            {
                bool alreadyVar = vm.IsNodeInVariables(paramNode);
                var varHeader = alreadyVar
                    ? "Remove from Model System Variables"
                    : "Add to Model System Variables";
                var varItem = new MenuItem { Header = varHeader };
                varItem.Click += (_, _) =>
                {
                    if (vm.IsNodeInVariables(paramNode))
                        _ = vm.RemoveNodeFromVariablesAsync(paramNode);
                    else
                        _ = vm.AddNodeToVariablesAsync(paramNode);
                };
                menu.Items.Add(varItem);
                menu.Items.Add(new Separator());
            }
        }

        // ── IFunction<T> → Create linked ExecuteWithContext ───────────────
        if (element is NodeViewModel funcNode)
        {
            var nodeType = funcNode.UnderlyingNode.Type;
            var iFunctionOpen = typeof(IFunction<>);
            Type? returnType = null;
            if (nodeType is not null)
            {
                foreach (var iface in nodeType.GetInterfaces())
                {
                    if (iface.IsGenericType && iface.GetGenericTypeDefinition() == iFunctionOpen)
                    {
                        returnType = iface.GetGenericArguments()[0];
                        break;
                    }
                }
            }

            if (returnType is not null)
            {
                var capturedFuncNode = funcNode;
                var wrapItem = new MenuItem
                {
                    Header = $"Create ExecuteWithContext<{returnType.Name}> (linked)"
                };
                wrapItem.Click += (_, _) => _ = vm.CreateExecuteWithContextAsync(capturedFuncNode);
                menu.Items.Add(wrapItem);
                menu.Items.Add(new Separator());
            }
        }

        // ── Create Ghost Node + Move to Boundary (regular nodes) ───────────────────
        if (element is NodeViewModel ghostSourceNode)
        {
            var capturedGhostSource = ghostSourceNode;

            var ghostItem = new MenuItem { Header = "Create Ghost Node" };
            ghostItem.Click += (_, _) =>
            {
                double rw = NodeRenderWidth(capturedGhostSource);
                double rh = NodeRenderHeight(capturedGhostSource);
                const double gap = 30.0;
                int gx = (int)(capturedGhostSource.X + rw + gap);
                int gy = (int)capturedGhostSource.Y;
                int gw = (int)rw;
                int gh = (int)rh;
                vm.CreateGhostNode(capturedGhostSource, gx, gy, gw, gh);
            };

            var moveNodeItem = new MenuItem { Header = "Move to Boundary…" };
            moveNodeItem.Click += async (_, _) =>
            {
                await vm.MoveNodeToBoundaryAsync(capturedGhostSource);
                InvalidateAndMeasure();
            };

            menu.Items.Add(new Separator());
            menu.Items.Add(ghostItem);
            menu.Items.Add(moveNodeItem);
        }

        // ── Move to Boundary (ghost nodes) ────────────────────────────────────
        if (element is GhostNodeViewModel capturedGhost)
        {
            var moveGhostItem = new MenuItem { Header = "Move to Boundary…" };
            moveGhostItem.Click += async (_, _) =>
            {
                await vm.MoveGhostNodeToBoundaryAsync(capturedGhost);
                InvalidateAndMeasure();
            };
            menu.Items.Add(new Separator());
            menu.Items.Add(moveGhostItem);
        }

        // ── Function template – specific items ─────────────────────────────
        if (element is FunctionTemplateViewModel capturedFt)
        {
            // Enter: navigate into the template's InternalModules
            var enterItem = new MenuItem { Header = "Edit Contents (double-click)" };
            enterItem.Click += (_, _) =>
            {
                vm.NavigateIntoFunctionTemplate(capturedFt);
                InvalidateAndMeasure();
            };

            // Rename
            var renameItem = new MenuItem { Header = "Rename…" };
            renameItem.Click += async (_, _) =>
            {
                await vm.RenameFunctionTemplateAsync(capturedFt);
                InvalidateAndMeasure();
            };

            menu.Items.Add(new Separator());
            menu.Items.Add(enterItem);
            menu.Items.Add(renameItem);
        }

        // ── Function instance – specific items ─────────────────────────────
        if (element is FunctionInstanceViewModel capturedFi)
        {
            var renameItem = new MenuItem { Header = "Rename…" };
            renameItem.Click += async (_, _) =>
            {
                await vm.RenameFunctionInstanceAsync(capturedFi);
                InvalidateAndMeasure();
            };

            menu.Items.Add(new Separator());
            menu.Items.Add(renameItem);
        }

        // ── "Add Function Parameter" — when we're inside a function template ─────
        if (_vm.IsInsideFunctionTemplate && element is FunctionParameterViewModel)
        {
            // Right-clicking an existing FunctionParameter: offer rename or remove.
            var fpvm = (FunctionParameterViewModel)element;
            var template = _vm.CurrentFunctionTemplate?.UnderlyingTemplate;

            // ── Local-variable toggle (only for IFunction<basicType> parameters) ──
            if (template is not null
                && FunctionTemplate.IsValidLocalVariableNode(fpvm.UnderlyingParameter, out _))
            {
                bool isAlreadyVar = template.LocalVariables.Contains(fpvm.UnderlyingParameter);
                var localVarHeader = isAlreadyVar
                    ? "Remove from Local Variables"
                    : "Add as Local Variable";
                var localVarItem = new MenuItem { Header = localVarHeader };
                localVarItem.Click += async (_, _) =>
                {
                    await vm.ToggleFunctionTemplateVariableAsync(fpvm.UnderlyingParameter);
                    InvalidateAndMeasure();
                };
                menu.Items.Add(new Separator());
                menu.Items.Add(localVarItem);
            }

            var fpRenameItem = new MenuItem { Header = "Rename…" };
            fpRenameItem.Click += async (_, _) =>
            {
                await vm.RenameFunctionParameterAsync(fpvm);
                InvalidateAndMeasure();
            };

            var removeItem = new MenuItem { Header = "Remove Function Parameter" };
            removeItem.Click += async (_, _) =>
            {
                await vm.RemoveFunctionParameterAsync(fpvm.UnderlyingParameter);
                InvalidateAndMeasure();
            };
            menu.Items.Add(new Separator());
            menu.Items.Add(fpRenameItem);
            menu.Items.Add(removeItem);
        }
        // ── "Set as Entry Node" — any node while viewing InternalModules ─────────
        if (_vm.IsInsideFunctionTemplate && element is NodeViewModel entryNodeCandidate)
        {
            var currentEntry  = _vm.CurrentFunctionTemplate?.UnderlyingTemplate.EntryNode;
            bool alreadyEntry = ReferenceEquals(currentEntry, entryNodeCandidate.UnderlyingNode);
            var entryHeader   = alreadyEntry ? "Clear Entry Node" : "Set as Entry Node";
            var capturedEntryCandidate = entryNodeCandidate;
            var entryItem = new MenuItem { Header = entryHeader };
            entryItem.Click += async (_, _) =>
            {
                await vm.SetFunctionTemplateEntryNodeAsync(capturedEntryCandidate);
                InvalidateAndMeasure();
            };
            menu.Items.Add(new Separator());
            menu.Items.Add(entryItem);
        }

        menu.Items.Add(deleteItem);

        ContextMenu = menu;
        ContextMenu.Open(this);
    }

    // ── Resize handle hit-testing ─────────────────────────────────────────

    // ── Inline parameter editor ────────────────────────────────────────────────

    /// <summary>
    /// Returns the <see cref="NodeViewModel"/> whose parameter value row contains
    /// <paramref name="pos"/>, or <c>null</c> if none.
    /// </summary>
    private NodeViewModel? HitTestParamValueRow(Point pos)
    {
        if (_vm is null) return null;
        foreach (var node in _vm.Nodes)
        {
            if (!node.IsParameterNode || node.IsInlined) continue;
            double rw = NodeRenderWidth(node);
            var rowRect = new Rect(node.X, node.Y + NodeHeaderHeight, rw, HookRowHeight);
            if (rowRect.Contains(pos))
                return node;
        }
        return null;
    }

    /// <summary>Shows the inline editor over the parameter row of <paramref name="node"/>.</summary>
    /// <param name="node">The BasicParameter node whose value is being edited.</param>
    /// <param name="rowX">Override X position of the editor overlay (use -1 to auto-derive).</param>
    /// <param name="rowY">Override Y position of the editor overlay (use -1 to auto-derive).</param>
    /// <param name="rowW">Override width of the editor overlay (use -1 to auto-derive).</param>
    /// <summary>
    /// Unsubscribes from the inline editor's internal ScrollViewer PropertyChanged event
    /// and clears the cached references. Safe to call when not subscribed.
    /// </summary>
    private void UnsubscribeInlineEditorScroll()
    {
        if (_inlineEditorSv is not null && _inlineEditorSvHandler is not null)
            _inlineEditorSv.PropertyChanged -= _inlineEditorSvHandler;
        _inlineEditorSv      = null;
        _inlineEditorSvHandler = null;
        _scriptOverlay.HorizontalScrollOffset = 0;
    }

    private void BeginParamEdit(NodeViewModel node, double rowX = -1, double rowY = -1, double rowW = -1)
    {
        HideVarDropdown();
        _editingParamNode = node;
        _editingParamEditorX = rowX >= 0 ? rowX : node.X;
        _editingParamEditorY = rowY >= 0 ? rowY : node.Y + NodeHeaderHeight;
        _editingParamEditorW = rowW >= 0 ? rowW : NodeRenderWidth(node);
        _inlineEditor.Text  = node.ParameterValueRepresentation;

        // For scripted parameters the text is rendered by Render() with syntax colours;
        // make the TextBox itself transparent so the coloured tokens show through.
        if (node.IsScriptedParameter)
        {
            _inlineEditor.Foreground = Brushes.Transparent;
            bool isLight = Application.Current?.ActualThemeVariant == ThemeVariant.Light;
            _inlineEditor.CaretBrush = isLight ? Brushes.Black : Brushes.White;
            _scriptTokens = TokenizeScript(node.ParameterValueRepresentation);
            _scriptOverlay.Tokens    = _scriptTokens;
            _scriptOverlay.IsVisible = true;

            // Subscribe to the TextBox's internal ScrollViewer so the overlay shifts
            // horizontally in lockstep with the TextBox after every caret move or
            // text change (the scroll happens during layout, after TextChanged fires).
            UnsubscribeInlineEditorScroll();
            // The internal SV may not exist until after the first layout pass, so
            // we post the subscription to run once the visual tree is populated.
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var sv = _inlineEditor.GetVisualDescendants()
                                      .OfType<ScrollViewer>()
                                      .FirstOrDefault();
                if (sv is not null)
                {
                    _inlineEditorSvHandler = (_, args) =>
                    {
                        if (args.Property == ScrollViewer.OffsetProperty)
                        {
                            _scriptOverlay.HorizontalScrollOffset = sv.Offset.X;
                            _scriptOverlay.InvalidateVisual();
                        }
                    };
                    _inlineEditorSv = sv;
                    sv.PropertyChanged += _inlineEditorSvHandler;
                }
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
        else
        {
            _inlineEditor.Foreground = ParamValueTextBrush;
            _inlineEditor.Background = new SolidColorBrush(Color.FromRgb(0x18, 0x28, 0x38));
            _inlineEditor.CaretBrush = null; // default (uses Foreground)
            _scriptTokens            = Array.Empty<(string, IBrush)>();
            _scriptOverlay.Tokens    = _scriptTokens;
            _scriptOverlay.IsVisible = false;
        }

        _inlineEditor.IsVisible = true;
        // Re-layout so ArrangeOverride positions the TextBox at the right row.
        InvalidateMeasure();
        // Focus + select-all after the layout pass completes.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _inlineEditor.Focus();
            _inlineEditor.SelectAll();
        }, Avalonia.Threading.DispatcherPriority.Render);
    }

    /// <summary>Commits the current editor text as the new parameter value.</summary>
    /// <remarks>
    /// For ScriptedParameter nodes the value is validated before the editor is closed.
    /// If the save fails the editor remains open so the user can correct the expression,
    /// and an error toast is shown instead.
    /// </remarks>
    private void CommitParamEdit()
    {
        if (_commitParamEditInProgress) return;
        _commitParamEditInProgress = true;
        try
        {
            HideVarDropdown();
            if (_editingParamNode is null) return;
            var node  = _editingParamNode;
            var value = _inlineEditor.Text ?? string.Empty;

            // Attempt to save. For ScriptedParameter this validates the expression first.
            if (!node.SetParameterValue(value, out var error))
            {
                // Save failed – keep the editor open, restore focus, show the error.
                _vm?.ShowToast(error?.Message ?? "Failed to set parameter value.",
                               isError: true, durationMs: 5000);
                Avalonia.Threading.Dispatcher.UIThread.Post(
                    () => _inlineEditor.Focus(),
                    Avalonia.Threading.DispatcherPriority.Input);
                return;
            }

            // Save succeeded – close the editor.
            UnsubscribeInlineEditorScroll();
            _editingParamNode        = null;
            _inlineEditor.IsVisible  = false;
            _inlineEditor.Foreground = ParamValueTextBrush;
            _inlineEditor.Background = new SolidColorBrush(Color.FromRgb(0x18, 0x28, 0x38));
            _inlineEditor.CaretBrush = null;
            _scriptTokens            = Array.Empty<(string, IBrush)>();
            _scriptOverlay.Tokens    = _scriptTokens;
            _scriptOverlay.IsVisible = false;
            InvalidateAndMeasure();
        }
        finally
        {
            _commitParamEditInProgress = false;
        }
    }

    /// <summary>Discards the current edit without saving.</summary>
    private void CancelParamEdit()
    {
        HideVarDropdown();
        UnsubscribeInlineEditorScroll();
        _editingParamNode        = null;
        _inlineEditor.IsVisible  = false;
        _inlineEditor.Foreground = ParamValueTextBrush;
        _inlineEditor.Background = new SolidColorBrush(Color.FromRgb(0x18, 0x28, 0x38));
        _inlineEditor.CaretBrush = null;
        _scriptTokens            = Array.Empty<(string, IBrush)>();
        _scriptOverlay.Tokens    = _scriptTokens;
        _scriptOverlay.IsVisible = false;
        InvalidateAndMeasure();
        Focus();
    }

    private void OnInlineEditorKeyDown(object? sender, KeyEventArgs e)
    {
        // ── Variable autocomplete dropdown navigation ─────────────────────
        if (_varDropdownVisible)
        {
            int count = _varDropdownStack.Children.Count;
            if (e.Key == Key.Down)
            {
                _varSelectedIndex = Math.Min(_varSelectedIndex + 1, count - 1);
                UpdateDropdownHighlight();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Up)
            {
                _varSelectedIndex = Math.Max(_varSelectedIndex - 1, 0);
                UpdateDropdownHighlight();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Tab)
            {
                SelectCurrentDropdownItem();
                e.Handled = true;
                return;
            }
            if (e.Key is Key.Return or Key.Enter)
            {
                // Complete with the highlighted item; do NOT commit the whole edit.
                SelectCurrentDropdownItem();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape)
            {
                HideVarDropdown();
                e.Handled = true;
                return;
            }
        }

        // ── Standard inline-editor keys ───────────────────────────────────
        if (e.Key is Key.Return or Key.Enter)
        {
            CommitParamEdit();
            Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelParamEdit();
            e.Handled = true;
        }
    }

    // ── Variable autocomplete helpers ─────────────────────────────────────

    // ── Script syntax tokenizer ───────────────────────────────────────────

    /// <summary>
    /// Breaks <paramref name="text"/> into coloured segments for display in the
    /// scripted-parameter inline editor.
    /// <list type="bullet">
    ///   <item>Known model-system variables → <see cref="ScriptVarKnownBrush"/> (green)</item>
    ///   <item>Unrecognised identifiers   → <see cref="ScriptVarUnknownBrush"/> (red)</item>
    ///   <item>Numeric literals            → <see cref="ScriptNumberBrush"/> (light-blue)</item>
    ///   <item>String literals             → <see cref="ScriptStringBrush"/> (orange)</item>
    ///   <item><c>true</c> / <c>false</c>  → <see cref="ScriptKeywordBrush"/> (gold)</item>
    ///   <item>Operators &amp; punctuation → <see cref="ScriptOperatorBrush"/> (steel-blue)</item>
    ///   <item>Whitespace                  → <see cref="ParamValueTextBrush"/> (neutral)</item>
    /// </list>
    /// </summary>
    private (string text, IBrush brush)[] TokenizeScript(string text)
    {
        if (_vm is null || string.IsNullOrEmpty(text))
            return Array.Empty<(string, IBrush)>();

        var knownNames = new HashSet<string>(
            _vm.ModelSystemVariables.Select(v => v.Name)
                .Concat(_vm.LocalVariables.Select(v => v.Name)),
            StringComparer.OrdinalIgnoreCase);

        var tokens = new List<(string, IBrush)>();
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];

            // ── String literal ─────────────────────────────────────────────
            if (c == '"')
            {
                int start = i++;
                while (i < text.Length && text[i] != '"') i++;
                if (i < text.Length) i++; // consume closing quote
                tokens.Add((text[start..i], _isLight ? ScriptStringBrushL : ScriptStringBrush));
                continue;
            }

            // ── Whitespace ────────────────────────────────────────────────
            if (char.IsWhiteSpace(c))
            {
                int start = i;
                while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                tokens.Add((text[start..i], _isLight ? ParamValueTextBrushL : ParamValueTextBrush));
                continue;
            }

            // ── Identifier / keyword ───────────────────────────────────────
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                var word = text[start..i];
                IBrush brush = word switch
                {
                    "true" or "false" => _isLight ? ScriptKeywordBrushL : ScriptKeywordBrush,
                    _                 => knownNames.Contains(word)
                                         ? (_isLight ? ScriptVarKnownBrushL   : ScriptVarKnownBrush)
                                         : (_isLight ? ScriptVarUnknownBrushL : ScriptVarUnknownBrush),
                };
                tokens.Add((word, brush));
                continue;
            }

            // ── Numeric literal ──────────────────────────────────────────
            if (char.IsDigit(c))
            {
                int start = i;
                while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.')) i++;
                tokens.Add((text[start..i], _isLight ? ScriptNumberBrushL : ScriptNumberBrush));
                continue;
            }

            // ── Operator / punctuation (single or double char) ────────────────
            {
                int start = i++;
                // Absorb two-char operators: &&, ||, ==, !=, >=, <=
                if (i < text.Length && (
                    (c == '&' && text[i] == '&') ||
                    (c == '|' && text[i] == '|') ||
                    (c == '=' && text[i] == '=') ||
                    (c == '!' && text[i] == '=') ||
                    (c == '>' && text[i] == '=') ||
                    (c == '<' && text[i] == '=')))
                {
                    i++;
                }
                tokens.Add((text[start..i], _isLight ? ScriptOperatorBrushL : ScriptOperatorBrush));
            }
        }
        return tokens.ToArray();
    }

    /// <summary>
    /// Returns <c>true</c> for characters that terminate a variable token
    /// in a scripted-parameter expression.
    /// </summary>
    private static bool IsExpressionSpecialChar(char c) =>
        c is '+' or '-' or '*' or '/' or '^' or '?' or ':'
           or '&' or '|' or '<' or '>' or '=' or '!' or '(' or ')' or '"';

    /// <summary>
    /// Called whenever the inline-editor text changes.  When editing a
    /// <see cref="NodeViewModel.IsScriptedParameter"/> node, extracts the
    /// token at the caret and populates (or hides) the autocomplete dropdown.
    /// </summary>
    private void OnInlineEditorTextChanged(object? sender, TextChangedEventArgs e)
    {
        // ── Syntax-highlight tokens for scripted params ─────────────────────────
        if (_editingParamNode is { IsScriptedParameter: true })
        {
            _scriptTokens = TokenizeScript(_inlineEditor.Text ?? string.Empty);
            _scriptOverlay.Tokens = _scriptTokens;
            // Offset is kept current by the _inlineEditorScrollSub observable subscription;
            // just redraw with the already-known offset.
            _scriptOverlay.InvalidateVisual();
        }
        else
        {
            _scriptTokens = Array.Empty<(string, IBrush)>();
            _scriptOverlay.Tokens = _scriptTokens;
        }

        // ── Variable autocomplete dropdown ──────────────────────────────────
        if (_editingParamNode is null || !_editingParamNode.IsScriptedParameter || _vm is null)
        {
            HideVarDropdown();
            return;
        }

        var text  = _inlineEditor.Text ?? string.Empty;
        var caret = Math.Clamp(_inlineEditor.CaretIndex, 0, text.Length);

        // Walk backwards from the caret to find the start of the current token.
        int tokenStart = caret;
        while (tokenStart > 0)
        {
            char ch = text[tokenStart - 1];
            if (char.IsWhiteSpace(ch) || IsExpressionSpecialChar(ch))
                break;
            tokenStart--;
        }

        var token = text[tokenStart..caret];
        _varTokenStart = tokenStart;

        if (token.Length == 0)
        {
            HideVarDropdown();
            return;
        }

        bool isLight = Application.Current?.ActualThemeVariant == ThemeVariant.Light;

        var matches = _vm.ModelSystemVariables
            .Concat(_vm.LocalVariables)
            .Where(v => v.Name.Contains(token, StringComparison.OrdinalIgnoreCase))
            .Select(v => v.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Take(MaxVarDropdownItems)
            .ToList();

        if (matches.Count == 0)
        {
            HideVarDropdown();
            return;
        }

        var normalBg  = isLight
            ? new SolidColorBrush(Color.FromRgb(0xF8, 0xF9, 0xFF))
            : new SolidColorBrush(Color.FromRgb(0x1E, 0x2E, 0x3E));
        IBrush normalFg = isLight ? Brushes.Black  : Brushes.White;

        _varDropdownBorder.Background = normalBg;
        _varDropdownBorder.BorderBrush = isLight
            ? new SolidColorBrush(Color.FromRgb(0x88, 0xAA, 0xCC))
            : new SolidColorBrush(Color.FromRgb(0x44, 0x88, 0xCC));

        _varDropdownStack.Children.Clear();
        foreach (var name in matches)
        {
            var captured = name;
            var tb = new TextBlock
            {
                Text       = captured,
                Padding    = new Thickness(8, 3, 8, 3),
                Foreground = normalFg,
                Background = normalBg,
                FontSize   = HookFontSize,
                FontFamily = new Avalonia.Media.FontFamily("Segoe UI, Arial, sans-serif"),
            };
            tb.PointerEntered += (_, _) =>
            {
                tb.Background = new SolidColorBrush(Color.FromRgb(0x20, 0x60, 0xA0));
                tb.Foreground = Brushes.White;
            };
            tb.PointerExited += (_, _) =>
            {
                // UpdateDropdownHighlight will repaint based on _varSelectedIndex.
                UpdateDropdownHighlight();
            };
            tb.PointerPressed += (_, pe) =>
            {
                CompleteVariable(captured);
                pe.Handled = true;
            };
            _varDropdownStack.Children.Add(tb);
        }

        _varSelectedIndex = 0;
        UpdateDropdownHighlight();
        _varDropdownBorder.IsVisible = true;
        _varDropdownVisible = true;
        InvalidateMeasure();
    }

    /// <summary>Repaints the selection highlight so only the row at <see cref="_varSelectedIndex"/> is highlighted.</summary>
    private void UpdateDropdownHighlight()
    {
        bool isLight = Application.Current?.ActualThemeVariant == ThemeVariant.Light;
        var normalBg  = isLight
            ? new SolidColorBrush(Color.FromRgb(0xF8, 0xF9, 0xFF))
            : new SolidColorBrush(Color.FromRgb(0x1E, 0x2E, 0x3E));
        var selBg = new SolidColorBrush(Color.FromRgb(0x20, 0x60, 0xA0));
        IBrush normalFg = isLight ? Brushes.Black  : Brushes.White;

        for (int i = 0; i < _varDropdownStack.Children.Count; i++)
        {
            if (_varDropdownStack.Children[i] is not TextBlock tb) continue;
            bool sel = i == _varSelectedIndex;
            tb.Background = sel ? selBg  : normalBg;
            tb.Foreground = sel ? Brushes.White : normalFg;
        }
    }

    /// <summary>Completes the current token with the currently highlighted dropdown item.</summary>
    private void SelectCurrentDropdownItem()
    {
        int count = _varDropdownStack.Children.Count;
        if (_varSelectedIndex < 0 || _varSelectedIndex >= count) return;
        if (_varDropdownStack.Children[_varSelectedIndex] is TextBlock tb && tb.Text is { } name)
            CompleteVariable(name);
    }

    /// <summary>
    /// Replaces the token starting at <see cref="_varTokenStart"/> up to the current
    /// caret position with <paramref name="name"/>, then closes the dropdown.
    /// The editor remains open so the user can continue typing.
    /// </summary>
    private void CompleteVariable(string name)
    {
        var text  = _inlineEditor.Text ?? string.Empty;
        var caret = Math.Clamp(_inlineEditor.CaretIndex, 0, text.Length);
        _inlineEditor.Text       = text[.._varTokenStart] + name + text[caret..];
        _inlineEditor.CaretIndex = _varTokenStart + name.Length;
        HideVarDropdown();
        // Clicking a TextBlock item shifted focus to the canvas; return it to the
        // inline editor so the user can keep typing without clicking again.
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () => _inlineEditor.Focus(),
            Avalonia.Threading.DispatcherPriority.Input);
    }

    /// <summary>Hides and clears the variable autocomplete dropdown.</summary>
    private void HideVarDropdown()
    {
        if (!_varDropdownVisible) return;
        _varDropdownVisible          = false;
        _varDropdownBorder.IsVisible = false;
        _varDropdownStack.Children.Clear();
        InvalidateMeasure();
    }

    private void OnInlineEditorLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // When the variable autocomplete dropdown is visible the user may have clicked
        // a suggestion item. TextBlock items are non-focusable, so focus falls to the
        // canvas. We must NOT commit here; CompleteVariable() keeps the session open
        // and will immediately return focus to the inline editor.
        if (_varDropdownVisible) return;

        // Commit on focus loss (e.g. user clicks away to another element).
        if (_editingParamNode is not null)
            CommitParamEdit();
    }

    /// <summary>
    /// Opens the inline comment editor for the currently selected comment block.
    /// Called externally (e.g. from the F2 key handler in the editor view).
    /// Does nothing if the selected element is not a <see cref="CommentBlockViewModel"/>.
    /// </summary>
    // ── Inline name editor helpers ───────────────────────────────────────────

    /// <summary>Opens the single-line name editor over <paramref name="element"/> (node or start).</summary>
    private void BeginNameEdit(ICanvasElement element)
    {
        CommitParamEdit();
        CommitCommentEdit();

        bool isLight = Application.Current?.ActualThemeVariant == ThemeVariant.Light;
        _nameEditor.Foreground = isLight ? Brushes.Black : Brushes.White;
        _nameEditor.Background = isLight
            ? new SolidColorBrush(Color.FromRgb(0xE8, 0xF0, 0xFE))
            : new SolidColorBrush(Color.FromRgb(0x1A, 0x2C, 0x40));

        if (element is NodeViewModel nvm)
        {
            _nameEditorX = nvm.X;
            _nameEditorY = nvm.Y;
            _nameEditorW = NodeRenderWidth(nvm);
            _nameEditorH = NodeHeaderHeight;
        }
        else if (element is StartViewModel svm)
        {
            _nameEditorX = svm.X - StartViewModel.Radius;
            _nameEditorY = svm.Y + StartViewModel.Radius + 2;
            _nameEditorW = svm.Diameter + 20;
            _nameEditorH = NodeHeaderHeight;
        }
        else if (element is FunctionTemplateViewModel ftvm)
        {
            _nameEditorX = ftvm.X;
            _nameEditorY = ftvm.Y;
            _nameEditorW = ftvm.Width;
            _nameEditorH = FtHeaderHeight;
        }
        else if (element is FunctionInstanceViewModel fivm)
        {
            _nameEditorX = fivm.X;
            _nameEditorY = fivm.Y;
            _nameEditorW = fivm.Width;
            _nameEditorH = FtHeaderHeight;
        }
        else if (element is FunctionParameterViewModel fpvmEdit)
        {
            _nameEditorX = fpvmEdit.X;
            _nameEditorY = fpvmEdit.Y;
            _nameEditorW = fpvmEdit.Width;
            _nameEditorH = FtHeaderHeight;
        }
        else
        {
            return;
        }

        _editingNameElement   = element;
        _nameEditor.Text      = element.Name;
        _nameEditor.IsVisible = true;
        InvalidateMeasure();
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _nameEditor.Focus();
            _nameEditor.SelectAll();
        }, Avalonia.Threading.DispatcherPriority.Render);
    }

    /// <summary>Saves the name editor text and closes the editor.</summary>
    private void CommitNameEdit()
    {
        if (_editingNameElement is null) return;
        var element = _editingNameElement;
        var name    = (_nameEditor.Text ?? string.Empty).Trim();
        _editingNameElement   = null;
        _nameEditor.IsVisible = false;
        if (!string.IsNullOrWhiteSpace(name))
        {
            CommandError? renameError = null;
            bool ok = element switch
            {
                NodeViewModel             nvm  => nvm.SetName(name, out _),
                StartViewModel            svm  => svm.SetName(name, out _),
                FunctionTemplateViewModel ftvm => ftvm.SetName(name, out renameError),
                FunctionInstanceViewModel fivm => fivm.SetName(name, out renameError),
                FunctionParameterViewModel fpvmC => fpvmC.SetName(name, out renameError),
                _                             => true,
            };
            if (!ok)
                _vm?.ShowToast(renameError?.Message ?? "Failed to rename.", isError: true, durationMs: 4000);
        }
        InvalidateAndMeasure();
    }

    /// <summary>Discards the name edit without saving.</summary>
    private void CancelNameEdit()
    {
        _editingNameElement   = null;
        _nameEditor.IsVisible = false;
        InvalidateAndMeasure();
        Focus();
    }

    private void OnNameEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Return or Key.Enter)
        {
            CommitNameEdit();
            Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelNameEdit();
            e.Handled = true;
        }
    }

    private void OnNameEditorLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_editingNameElement is not null)
            CommitNameEdit();
    }

    /// <summary>
    /// Opens the inline name editor for the currently selected node or start.
    /// Called from the editor view when F2 is pressed and a node/start is selected.
    /// </summary>
    public void BeginNameEditForSelected()
    {
        if (_vm?.SelectedElement is NodeViewModel or StartViewModel
                                 or FunctionTemplateViewModel or FunctionInstanceViewModel
                                 or FunctionParameterViewModel)
            BeginNameEdit(_vm.SelectedElement);
    }

    // ── Inline comment editor helpers ────────────────────────────────────────

    public void BeginCommentEditForSelected()
    {
        if (_vm?.SelectedElement is CommentBlockViewModel comment)
            BeginCommentEdit(comment);
    }

    /// <summary>Shows the multi-line comment editor over <paramref name="comment"/>.</summary>
    private void BeginCommentEdit(CommentBlockViewModel comment)
    {
        _editingCommentBlock   = comment;
        _editingCommentEditorX = comment.X;
        _editingCommentEditorY = comment.Y;
        _editingCommentEditorW = comment.Width;
        _editingCommentEditorH = comment.Height;
        _commentEditor.Text    = comment.Name;   // Name returns the underlying Comment text.
        // Pick colours based on the active theme.
        bool isLight = Application.Current?.ActualThemeVariant == ThemeVariant.Light;
        _commentEditor.Foreground = isLight
            ? CommentTextBrush   // dark text on sticky-note yellow
            : Brushes.White;     // white text on dark background
        _commentEditor.Background = isLight
            ? new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xF0, 0x96))   // sticky-note yellow
            : new SolidColorBrush(Color.FromRgb(0x18, 0x28, 0x18));          // dark green-tinted panel
        _commentEditor.IsVisible = true;
        InvalidateMeasure();
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _commentEditor.Focus();
        }, Avalonia.Threading.DispatcherPriority.Render);
    }

    /// <summary>Saves the comment editor text and closes the editor.</summary>
    private void CommitCommentEdit()
    {
        if (_editingCommentBlock is null) return;
        var comment = _editingCommentBlock;
        var text    = _commentEditor.Text ?? string.Empty;
        _editingCommentBlock     = null;
        _commentEditor.IsVisible = false;
        comment.SetText(text);
        InvalidateAndMeasure();
    }

    /// <summary>Discards the comment edit without saving.</summary>
    private void CancelCommentEdit()
    {
        _editingCommentBlock     = null;
        _commentEditor.IsVisible = false;
        InvalidateAndMeasure();
        Focus();
    }

    private void OnCommentEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if ((e.Key is Key.Return or Key.Enter) && (e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            // Ctrl+Enter commits; plain Enter inserts a newline (default).
            CommitCommentEdit();
            Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelCommentEdit();
            e.Handled = true;
        }
    }

    private void OnCommentEditorLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_editingCommentBlock is not null)
            CommitCommentEdit();
    }

    /// <summary>
    /// Returns the <see cref="NodeViewModel"/> whose resize handle (bottom-right
    /// corner square) contains <paramref name="pos"/>, or <c>null</c> if none.
    /// </summary>
    /// <summary>
    /// Returns the <see cref="ICanvasElement"/> whose resize handle (bottom-right
    /// corner square) contains <paramref name="pos"/>, or <c>null</c> if none.
    /// Checks nodes first, then comment blocks.
    /// </summary>
    private ICanvasElement? HitTestResizeHandle(Point pos)
    {
        if (_vm is null) return null;
        foreach (var node in _vm.Nodes)
        {
            double rw = NodeRenderWidth(node);
            double rh = NodeRenderHeight(node);
            var handle = new Rect(
                node.X + rw - ResizeHandleSize,
                node.Y + rh - ResizeHandleSize,
                ResizeHandleSize,
                ResizeHandleSize);
            if (handle.Contains(pos))
                return node;
        }
        foreach (var comment in _vm.CommentBlocks)
        {
            var handle = new Rect(
                comment.X + comment.Width  - ResizeHandleSize,
                comment.Y + comment.Height - ResizeHandleSize,
                ResizeHandleSize,
                ResizeHandleSize);
            if (handle.Contains(pos))
                return comment;
        }
        foreach (var ghost in _vm.GhostNodes)
        {
            var handle = new Rect(
                ghost.X + ghost.Width  - ResizeHandleSize,
                ghost.Y + ghost.Height - ResizeHandleSize,
                ResizeHandleSize,
                ResizeHandleSize);
            if (handle.Contains(pos))
                return ghost;
        }
        foreach (var ft in _vm.FunctionTemplates)
        {
            var handle = new Rect(
                ft.X + ft.Width  - ResizeHandleSize,
                ft.Y + ft.Height - ResizeHandleSize,
                ResizeHandleSize,
                ResizeHandleSize);
            if (handle.Contains(pos))
                return ft;
        }
        foreach (var fi in _vm.FunctionInstances)
        {
            var handle = new Rect(
                fi.X + fi.Width  - ResizeHandleSize,
                fi.Y + fi.Height - ResizeHandleSize,
                ResizeHandleSize,
                ResizeHandleSize);
            if (handle.Contains(pos))
                return fi;
        }
        foreach (var fp in _vm.FunctionParameterVMs)
        {
            var handle = new Rect(
                fp.X + fp.Width  - ResizeHandleSize,
                fp.Y + fp.Height - ResizeHandleSize,
                ResizeHandleSize,
                ResizeHandleSize);
            if (handle.Contains(pos))
                return fp;
        }
        return null;
    }

    // ── Hook toggle icon hit-testing ─────────────────────────────────────

    /// <summary>
    /// Returns the <see cref="NodeViewModel"/> whose hook-toggle icon button contains
    /// <paramref name="pos"/>, or <c>null</c> if none.
    /// </summary>
    private NodeViewModel? HitTestHookToggleIcon(Point pos)
    {
        if (_vm is null || _vm.ShowAllHooks) return null;
        foreach (var node in _vm.Nodes)
        {
            if (node.UnderlyingNode.Hooks.Count == 0) continue;
            double rw    = NodeRenderWidth(node);
            var iconRect = HookToggleIconRect(node, rw);
            if (iconRect.Contains(pos))
                return node;
        }
        return null;
    }

    /// <summary>
    /// Returns the bounding rectangle of the hook-toggle icon button for
    /// <paramref name="node"/> given its rendered width <paramref name="rw"/>.
    /// </summary>
    private static Rect HookToggleIconRect(NodeViewModel node, double rw)
    {
        const double margin = 4.0;
        double size = HookToggleIconSize;
        return new Rect(
            node.X + rw - size - margin,
            node.Y + (NodeHeaderHeight - size) / 2.0,
            size,
            size);
    }

    /// <summary>
    /// Returns the bounding rectangle of the "minimize to inline" button that appears
    /// in the top-left header of a <see cref="_canInlineNodes"/> BasicParameter node.
    /// </summary>
    private static Rect InlineMinimizeButtonRect(NodeViewModel node)
    {
        const double margin = 4.0;
        double size = InlineMinimizeButtonSize;
        return new Rect(
            node.X + margin,
            node.Y + (NodeHeaderHeight - size) / 2.0,
            size,
            size);
    }

    /// <summary>
    /// Returns the <see cref="NodeViewModel"/> whose minimize-to-inline button
    /// (top-left of header) contains <paramref name="pos"/>, or <c>null</c>.
    /// Only nodes in <see cref="_canInlineNodes"/> have this button.
    /// </summary>
    private NodeViewModel? HitTestMinimizeButton(Point pos)
    {
        if (_vm is null) return null;
        foreach (var node in _canInlineNodes)
        {
            if (InlineMinimizeButtonRect(node).Contains(pos))
                return node;
        }
        return null;
    }

    /// <summary>
    /// Returns information about an inlined-param hook row that contains
    /// <paramref name="pos"/>, or <c>null</c> when no such row is hit.
    /// </summary>
    private (NodeViewModel originNode, NodeHook hook, NodeViewModel paramNode,
             double rowX, double rowY, double rowW)?
        HitTestInlinedParamRow(Point pos)
    {
        foreach (var ((originNode, hook), paramNode) in _hookInlinedParam)
        {
            if (!_nodeVisibleHooks.TryGetValue(originNode, out var hooks)) continue;

            int hookIdx = -1;
            for (int j = 0; j < hooks.Count; j++)
                if (ReferenceEquals(hooks[j], hook)) { hookIdx = j; break; }
            if (hookIdx < 0) continue;

            int rowOffset = originNode.IsParameterNode ? 1 : 0;
            double rw     = NodeRenderWidth(originNode);
            double rowTop = originNode.Y + NodeHeaderHeight + (rowOffset + hookIdx) * HookRowHeight;
            var rowRect   = new Rect(originNode.X, rowTop, rw, HookRowHeight);

            if (rowRect.Contains(pos))
                return (originNode, hook, paramNode, originNode.X, rowTop, rw);
        }
        return null;
    }

    /// <summary>Finds the topmost canvas element under <paramref name="pos"/>.</summary>
    /// <param name="testComments">When <c>false</c>, comment blocks are excluded (link creation).</param>
    private ICanvasElement? HitTest(Point pos, bool testComments)
    {
        if (_vm is null) return null;

        // Starts (highest z-order)
        foreach (var start in _vm.Starts)
        {
            var dx = pos.X - start.CenterX;
            var dy = pos.Y - start.CenterY;
            if (Math.Sqrt(dx * dx + dy * dy) <= StartViewModel.Radius)
                return start;
        }

        // Nodes
        foreach (var node in _vm.Nodes)
        {
            if (node.IsInlined) continue;  // hidden — not clickable directly
            if (new Rect(node.X, node.Y, NodeRenderWidth(node), NodeRenderHeight(node)).Contains(pos))
                return node;
        }

        // FunctionParameter nodes (shown only inside InternalModules of a template)
        foreach (var fp in _vm.FunctionParameterVMs)
        {
            if (new Rect(fp.X, fp.Y, fp.Width, fp.Height).Contains(pos))
                return fp;
        }

        // Ghost nodes
        foreach (var ghost in _vm.GhostNodes)
        {
            if (new Rect(ghost.X, ghost.Y, ghost.Width, ghost.Height).Contains(pos))
                return ghost;
        }

        // Function-template containers (behind nodes but above comment blocks)
        foreach (var ft in _vm.FunctionTemplates)
        {
            if (new Rect(ft.X, ft.Y, ft.Width, ft.Height).Contains(pos))
                return ft;
        }

        // Function-instance boxes (between function templates and comment blocks)
        foreach (var fi in _vm.FunctionInstances)
        {
            if (new Rect(fi.X, fi.Y, fi.Width, fi.Height).Contains(pos))
                return fi;
        }

        // Comment blocks (background layer)
        if (testComments)
        {
            foreach (var comment in _vm.CommentBlocks)
            {
                if (new Rect(comment.X, comment.Y, comment.Width, comment.Height).Contains(pos))
                    return comment;
            }
        }

        return null;
    }

    // ── Multi-selection helpers ────────────────────────────────────────────────
    /// <summary>
    /// Clears the multi-selection set, restoring <see cref="ICanvasElement.IsSelected"/> to
    /// <c>false</c> on every element that was in the set.
    /// Call this before initiating a new single-element or empty-space selection.
    /// </summary>
    private void ClearMultiSelection()
    {
        foreach (var el in _multiSelection)
            el.IsSelected = false;
        _multiSelection.Clear();
        // Also clear IsSelected on the primary selected element (which may not be in
        // _multiSelection when using single-select).  This must happen before zeroing
        // SelectedElement so callers that immediately invoke SelectElementCommand or
        // SelectLinkCommand don't skip the IsSelected reset (those commands guard on
        // SelectedElement being non-null, but it will already be null after this method).
        if (_vm?.SelectedElement is { } primary)
            primary.IsSelected = false;
        if (_vm is not null)
            _vm.SelectedElement = null;
    }

    /// <summary>Returns a <see cref="Rect"/> that always has non-negative width and height,
    /// regardless of the relative order of <paramref name="p1"/> and <paramref name="p2"/>.</summary>
    private static Rect NormalizeRect(Point p1, Point p2) =>
        new Rect(
            Math.Min(p1.X, p2.X), Math.Min(p1.Y, p2.Y),
            Math.Abs(p2.X - p1.X), Math.Abs(p2.Y - p1.Y));

    /// <summary>
    /// Draws the in-progress rubber-band selection rectangle (Ctrl+drag).
    /// Uses a dashed blue outline with a semi-transparent fill tint.
    /// Must be called inside a <see cref="DrawingContext.PushTransform"/> that maps model coords to
    /// screen coords (i.e. the existing scale transform in <see cref="Render"/>).
    /// </summary>
    private void RenderSelectionRect(DrawingContext ctx)
    {
        if (_selRectStart is not { } start) return;
        var rect = NormalizeRect(start, _selRectCurrent);
        var pen  = new Pen(Brushes.CornflowerBlue, 1.5 / _scale, SelectionRectDash);
        ctx.DrawRectangle(SelectionRectFill, pen, rect, 2 / _scale, 2 / _scale);
    }

}
