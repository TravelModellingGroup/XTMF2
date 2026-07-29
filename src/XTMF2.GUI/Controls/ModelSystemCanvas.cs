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

/// <summary>
/// A custom Avalonia <see cref="Control"/> that renders the model system canvas
/// using a <see cref="DrawingContext"/>.
/// <para>
/// Nodes are drawn as rounded rectangles, Starts as circles, and Links as lines.
/// Click a node or start to select it; click empty space to deselect.
/// </para>
/// </summary>
public sealed partial class ModelSystemCanvas : Control
{

    // ── ViewModel ─────────────────────────────────────────────────────────
    private ModelSystemEditorViewModel? _vm;
    /// <summary>Set at the start of each <see cref="Render"/> call; <c>true</c> when the app is in light mode.</summary>
    private bool _isLight;
    /// <summary>Tracks the FunctionTemplate we are currently subscribed to for PropertyChanged,
    /// so we can unsubscribe when navigating away.</summary>
    private FunctionTemplateViewModel? _subscribedCurrentFunctionTemplate;

    // ── Per-frame hook anchor cache (rebuilt in BuildHookAnchorCache) ─────
    private readonly Dictionary<(NodeViewModel, NodeHook), Point> _hookAnchors = new();

    private readonly Dictionary<NodeViewModel, IReadOnlyList<NodeHook>> _nodeVisibleHooks = new();

    private readonly Dictionary<NodeViewModel, HashSet<NodeHook>> _nodeConnectedHooks = new();

    /// <summary>Anchor points for FunctionInstance FunctionParameterHook rows (right-edge dot).</summary>
    private readonly Dictionary<(FunctionInstanceViewModel, FunctionParameterHook), Point> _fiHookAnchors = new();

    /// <summary>Tracks which FunctionParameterHooks on each FunctionInstance have live links.</summary>
    private readonly Dictionary<FunctionInstanceViewModel, HashSet<FunctionParameterHook>> _fiConnectedHooks = new();

    /// <summary>
    /// Per-frame set of (node, hook) pairs where at least one link departs leftward
    /// (destination centre X is to the left of the origin node's left edge).
    /// Used to render a dot on the left face and to choose the correct exit anchor.
    /// Rebuilt each frame by <see cref="BuildHookAnchorCache"/>.
    /// </summary>
    private readonly HashSet<(NodeViewModel, NodeHook)> _leftGoingHooks = new();

    /// <summary>
    /// Per-frame set of (fi, hook) pairs for FunctionInstance hooks where at least one
    /// link departs leftward.  Rebuilt each frame by <see cref="BuildHookAnchorCache"/>.
    /// </summary>
    private readonly HashSet<(FunctionInstanceViewModel, FunctionParameterHook)> _leftGoingFiHooks = new();

    // ── Inline parameter editor ───────────────────────────────────────────
    /// <summary>Overlay TextBox used for in-canvas parameter value editing.</summary>
    private readonly TextBox _inlineEditor;
    /// <summary>Overlay ComboBox used for in-canvas enum parameter editing.</summary>
    private readonly ComboBox _inlineEnumEditor;
    /// <summary><c>true</c> when the node currently being edited has an enum inner type.</summary>
    private bool _editingParamIsEnum;
    /// <summary><c>true</c> while <see cref="_inlineEnumEditor"/> is being initialised (prevents premature commit).</summary>
    private bool _enumEditorLoading;
    /// <summary>The node whose parameter value row is currently being edited, or <c>null</c> when idle.</summary>
    private NodeViewModel? _editingParamNode;
    /// <summary>Screen position and width of the inline editor overlay (set in <see cref="BeginParamEdit"/>).</summary>
    private double _editingParamEditorX, _editingParamEditorY, _editingParamEditorW;
    /// <summary>The parent canvas element (Node or FunctionInstance) that owns the currently editing parameter.</summary>
    private ICanvasElement? _editingParamParentElement;
    /// <summary>The hook on the parent element that this parameter is attached to for inlined parameters.</summary>
    private object? _editingParamHook;
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
    private readonly Border _varDropdownBorder;
    /// <summary>Stack of <see cref="TextBlock"/> rows inside the dropdown.</summary>
    private readonly StackPanel _varDropdownStack;
    /// <summary><c>true</c> while the variable autocomplete dropdown is open.</summary>
    private bool _varDropdownVisible;
    /// <summary>Character offset in <see cref="TextBox.Text"/> where the current token starts.</summary>
    private int _varTokenStart;
    /// <summary>Index of the currently highlighted row in the dropdown.</summary>
    private int _varSelectedIndex;
    /// <summary>Maximum number of suggestions shown at once.</summary>
    private const int MaxVarDropdownItems = 8;

    // ── Inline comment editor ─────────────────────────────────────────────
    /// <summary>Overlay multi-line TextBox used for in-canvas comment block editing.</summary>
    private readonly TextBox _commentEditor;
    /// <summary>The comment block currently being edited, or <c>null</c> when idle.</summary>
    private CommentBlockViewModel? _editingCommentBlock;
    /// <summary>Model-space position and size of the comment editor overlay.</summary>
    private double _editingCommentEditorX, _editingCommentEditorY, _editingCommentEditorW, _editingCommentEditorH;
    // ── Inline comment header editor ───────────────────────────────
    /// <summary>Overlay single-line TextBox used for editing the comment block header.</summary>
    private readonly TextBox _commentHeaderEditor;
    /// <summary>The comment block whose header is being edited, or <c>null</c> when idle.</summary>
    private CommentBlockViewModel? _editingCommentHeaderBlock;
    /// <summary>Model-space position and size of the comment header editor overlay.</summary>
    private double _editingCommentHeaderEditorX, _editingCommentHeaderEditorY, _editingCommentHeaderEditorW, _editingCommentHeaderEditorH;
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
    /// Maps (origin FunctionInstance, FunctionParameterHook) → the BasicParameter/ScriptedParameter
    /// node that is currently inlined into that FI hook row.
    /// </summary>
    private readonly Dictionary<(FunctionInstanceViewModel, FunctionParameterHook), NodeViewModel>
        _fiHookInlinedParam = new();
    /// <summary>
    /// Stores the maximum scroll offset (model-space px) for each comment block, computed during
    /// render. Used by the wheel handler to clamp scroll without recomputing the text layout.
    /// </summary>
    private readonly Dictionary<CommentBlockViewModel, double> _commentMaxScrollOffsets = new();
    /// <summary>
    /// BasicParameter nodes that are visible on the canvas AND connected via a Single (or
    /// FunctionParameterHook) hook, so they can offer a "minimize to inline" button.
    /// </summary>
    private readonly HashSet<NodeViewModel> _canInlineNodes = new();

    // ── Canvas scale ───────────────────────────────────────────────────────
    private double _scale = 1.0;
    /// <summary>Current zoom scale factor (1.0 = 100%).</summary>
    public double Scale => _scale;
    // ── Zoom control overlay ───────────────────────────────────────────────
    private readonly Border _zoomBar;
    private readonly TextBox _zoomTextBox;
    private readonly Button _zoomMinusBtn;
    private readonly Button _zoomPlusBtn;
    private bool _zoomBarIsLight = false; // tracks last applied theme so we only update on change

    public ModelSystemCanvas()
    {
        Focusable = true;

        // Build the inline editor once; it lives as a visual child of this canvas.
        _inlineEditor = new TextBox
        {
            FontFamily = new Avalonia.Media.FontFamily("Segoe UI, Arial, sans-serif"),
            FontSize = HookFontSize,
            Foreground = ParamValueTextBrush,
            Background = new SolidColorBrush(Color.FromRgb(0x18, 0x28, 0x38)),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x88, 0xCC)),
            Padding = new Thickness(4, 0, 4, 0),
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            IsVisible = false,
        };
        _inlineEditor.KeyDown += OnInlineEditorKeyDown;
        _inlineEditor.LostFocus += OnInlineEditorLostFocus;
        _inlineEditor.TextChanged += OnInlineEditorTextChanged;

        LogicalChildren.Add(_inlineEditor);
        VisualChildren.Add(_inlineEditor);

        // Build the enum ComboBox overlay (shown instead of _inlineEditor for enum parameters).
        _inlineEnumEditor = new ComboBox
        {
            FontFamily = new Avalonia.Media.FontFamily("Segoe UI, Arial, sans-serif"),
            FontSize = HookFontSize,
            Background = new SolidColorBrush(Color.FromRgb(0x18, 0x28, 0x38)),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x88, 0xCC)),
            Padding = new Thickness(2, 0, 2, 0),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            IsVisible = false,
        };
        _inlineEnumEditor.DropDownClosed += OnInlineEnumEditorDropDownClosed;
        _inlineEnumEditor.LostFocus      += OnInlineEnumEditorLostFocus;
        _inlineEnumEditor.AddHandler(InputElement.KeyDownEvent, OnInlineEnumEditorKeyDown,
            Avalonia.Interactivity.RoutingStrategies.Tunnel);
        LogicalChildren.Add(_inlineEnumEditor);
        VisualChildren.Add(_inlineEnumEditor);

        // Syntax-highlight overlay – must be added AFTER _inlineEditor so it
        // renders on top of the TextBox, not underneath it.
        _scriptOverlay = new ScriptSyntaxOverlay();
        LogicalChildren.Add(_scriptOverlay);
        VisualChildren.Add(_scriptOverlay);
        _varDropdownStack = new StackPanel { Orientation = Orientation.Vertical };
        _varDropdownBorder = new Border
        {
            Child = _varDropdownStack,
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x2E, 0x3E)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x88, 0xCC)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            IsVisible = false,
        };
        LogicalChildren.Add(_varDropdownBorder);
        VisualChildren.Add(_varDropdownBorder);

        // Build the multi-line comment editor; Enter inserts a newline, Ctrl+Enter commits.
        _commentEditor = new TextBox
        {
            FontFamily = new Avalonia.Media.FontFamily("Segoe UI, Arial, sans-serif"),
            FontSize = CommentFontSize,
            Foreground = CommentTextBrush,
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xF0, 0x96)),
            BorderThickness = new Thickness(1),
            BorderBrush = CommentBorderBrush,
            Padding = new Thickness(6, 4, 6, 4),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };
        _commentEditor.LostFocus += OnCommentEditorLostFocus;
        // Use the tunneling phase so Ctrl+Enter is intercepted before AcceptsReturn
        // consumes the Enter keystroke and marks the event Handled.
        _commentEditor.AddHandler(InputElement.KeyDownEvent, OnCommentEditorKeyDown,
                                  Avalonia.Interactivity.RoutingStrategies.Tunnel);
        LogicalChildren.Add(_commentEditor);
        VisualChildren.Add(_commentEditor);

        // Build the single-line comment header editor; Enter commits, Escape cancels.
        _commentHeaderEditor = new TextBox
        {
            FontFamily = new Avalonia.Media.FontFamily("Segoe UI, Arial, sans-serif"),
            FontSize = CommentHeaderFontSize,
            FontWeight = Avalonia.Media.FontWeight.Bold,
            Foreground = CommentTextBrush,
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xD5, 0x1A)),
            BorderThickness = new Thickness(1),
            BorderBrush = CommentBorderBrush,
            Padding = new Thickness(6, 2, 6, 2),
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            IsVisible = false,
        };
        _commentHeaderEditor.LostFocus += OnCommentHeaderEditorLostFocus;
        _commentHeaderEditor.AddHandler(InputElement.KeyDownEvent, OnCommentHeaderEditorKeyDown,
                                        Avalonia.Interactivity.RoutingStrategies.Tunnel);
        LogicalChildren.Add(_commentHeaderEditor);
        VisualChildren.Add(_commentHeaderEditor);

        // Build the single-line name editor; Enter commits, Escape cancels.
        _nameEditor = new TextBox
        {
            FontFamily = new Avalonia.Media.FontFamily("Segoe UI, Arial, sans-serif"),
            FontSize = NodeFontSize,
            Foreground = NodeTextBrush,
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x2C, 0x40)),
            BorderThickness = new Thickness(1),
            BorderBrush = NodeSelBrush,
            Padding = new Thickness(4, 0, 4, 0),
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            IsVisible = false,
        };
        _nameEditor.AddHandler(InputElement.KeyDownEvent, OnNameEditorKeyDown,
                               Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _nameEditor.LostFocus += OnNameEditorLostFocus;
        LogicalChildren.Add(_nameEditor);
        VisualChildren.Add(_nameEditor);

        // ── Zoom control (pinned to viewport bottom-right) ────────────────
        _zoomTextBox = new TextBox
        {
            FontFamily = new Avalonia.Media.FontFamily("Segoe UI, Arial, sans-serif"),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xFF, 0xFF)),
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 1, 4, 1),
            Width = 52,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Text = "100%",
        };
        _zoomTextBox.KeyDown += OnZoomTextBoxKeyDown;
        _zoomTextBox.LostFocus += (_, _) => TryApplyZoomText();

        _zoomMinusBtn = new Button
        {
            Content = "\u2212",   // − (minus sign)
            FontSize = 13,
            Padding = new Thickness(6, 1, 6, 1),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0),
        };
        _zoomMinusBtn.Click += (_, _) => ApplyScale(_scale - ScaleStep);

        _zoomPlusBtn = new Button
        {
            Content = "+",
            FontSize = 13,
            Padding = new Thickness(6, 1, 6, 1),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0),
        };
        _zoomPlusBtn.Click += (_, _) => ApplyScale(_scale + ScaleStep);

        _zoomBar = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x05, 0x05, 0x10)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xD4, 0xFF)),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(4, 3),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 0,
                Children = { _zoomMinusBtn, _zoomTextBox, _zoomPlusBtn },
            },
        };
        LogicalChildren.Add(_zoomBar);
        VisualChildren.Add(_zoomBar);

        // Suppress Avalonia's automatic context-menu-on-right-click behaviour.
        // The canvas manages the context menu manually in OnPointerReleased so
        // that it only appears after a minimal-movement right-click, not after
        // a right-drag used to create a link connection.
        ContextRequested += SuppressContextRequested;

        // Continuous auto-scroll timer — fires at ~60 Hz while the pointer is
        // inside the scroll zone during an element drag, so the canvas keeps
        // scrolling even when the mouse is stationary.
        _autoScrollTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(16),
            DispatcherPriority.Input,
            OnAutoScrollTick);

        // Tunneling PointerPressed fires before any child TextBox overlay can
        // consume the event, giving resize-handle hits higher priority than
        // the inline editors.
        this.AddHandler(InputElement.PointerPressedEvent, OnPointerPressedTunnel,
                        Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    // ── Drag state ────────────────────────────────────────────────────────
    /// <summary>The element currently being dragged (left-button), or <c>null</c> when idle.</summary>
    private ICanvasElement? _dragging;
    /// <summary>Offset from the element's top-left corner to the pointer position at drag start.</summary>
    private Point _dragOffset;
    /// <summary>
    /// Fires at ~60 Hz while the cursor is inside the auto-scroll edge zone during a drag,
    /// so scrolling continues even when the mouse is stationary.
    /// </summary>
    private readonly DispatcherTimer _autoScrollTimer;
    /// <summary>Last ScrollViewer-local cursor position, updated on every pointer-move for the timer to reuse.</summary>
    private Point _lastSvPos;

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
    /// <summary>The canvas element being resized, or <c>null</c> when not resizing.</summary>
    private ICanvasElement? _resizing;
    /// <summary><c>true</c> while a drag or resize operation is in progress (used to prevent inline editor close on focus loss).</summary>
    private bool _inDragOrResize;
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
    private bool _rightClickPending;
    /// <summary>Canvas position where the right button was pressed.</summary>
    private Point _rightClickPressPos;
    /// <summary>Canvas element (node / start / comment) under the right-button press, if any.</summary>
    private ICanvasElement? _rightClickElement;
    /// <summary>Link under the right-button press when no element was hit.</summary>
    private LinkViewModel? _rightClickLink;
    /// <summary>Hook dot under the right-button press, if any (may be set alongside <see cref="_rightClickElement"/>).</summary>
    private (NodeViewModel Node, NodeHook Hook)? _rightClickHookHit;
    private (FunctionInstanceViewModel Fi, FunctionParameterHook Hook)? _rightClickFiHookHit;

    // ── Multi-selection set ───────────────────────────────────────────────
    /// <summary>
    /// All canvas elements currently in the extended multi-selection (each has
    /// <see cref="ICanvasElement.IsSelected"/> = <c>true</c>).
    /// </summary>
    private readonly HashSet<ICanvasElement> _multiSelection = new();
    /// <summary>
    /// All underlying links currently in the extended multi-selection.
    /// We track model links (not view models) so MultiLink groups are treated as one unit.
    /// </summary>
    private readonly HashSet<Link> _multiLinkSelection = new();
    /// <summary>Cursor model-coordinate recorded at the start of each group-drag frame, used to compute per-frame deltas.</summary>
    private Point _groupDragLastPos;

    // ── Copy / Paste clipboard ────────────────────────────────────────────
    // System clipboard is used — no in-memory clipboard field needed.

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
        _vm.Nodes.CollectionChanged += OnCollectionChanged;
        _vm.Starts.CollectionChanged += OnCollectionChanged;
        _vm.Links.CollectionChanged += OnCollectionChanged;
        _vm.CommentBlocks.CollectionChanged += OnCollectionChanged;
        _vm.GhostNodes.CollectionChanged += OnCollectionChanged;
        _vm.FunctionTemplates.CollectionChanged += OnCollectionChanged;
        _vm.FunctionInstances.CollectionChanged += OnCollectionChanged;
        _vm.FunctionParameterVMs.CollectionChanged += OnCollectionChanged;
        _vm.PropertyChanged += OnViewModelPropertyChanged;

        foreach (var n in _vm.Nodes) ((INotifyPropertyChanged)n).PropertyChanged += OnElementPropertyChanged;
        foreach (var s in _vm.Starts) ((INotifyPropertyChanged)s).PropertyChanged += OnElementPropertyChanged;
        foreach (var l in _vm.Links) ((INotifyPropertyChanged)l).PropertyChanged += OnElementPropertyChanged;
        foreach (var c in _vm.CommentBlocks) ((INotifyPropertyChanged)c).PropertyChanged += OnElementPropertyChanged;
        foreach (var g in _vm.GhostNodes) ((INotifyPropertyChanged)g).PropertyChanged += OnElementPropertyChanged;
        foreach (var f in _vm.FunctionTemplates) ((INotifyPropertyChanged)f).PropertyChanged += OnElementPropertyChanged;
        foreach (var fi in _vm.FunctionInstances) ((INotifyPropertyChanged)fi).PropertyChanged += OnElementPropertyChanged;
        foreach (var fp in _vm.FunctionParameterVMs) ((INotifyPropertyChanged)fp).PropertyChanged += OnElementPropertyChanged;
        _vm.RenderRequested += OnRenderRequested;
    }

    private void Detach()
    {
        if (_vm is null) return;
        _vm.RenderRequested -= OnRenderRequested;
        _vm.Nodes.CollectionChanged -= OnCollectionChanged;
        _vm.Starts.CollectionChanged -= OnCollectionChanged;
        _vm.Links.CollectionChanged -= OnCollectionChanged;
        _vm.CommentBlocks.CollectionChanged -= OnCollectionChanged;
        _vm.GhostNodes.CollectionChanged -= OnCollectionChanged;
        _vm.FunctionTemplates.CollectionChanged -= OnCollectionChanged;
        _vm.FunctionInstances.CollectionChanged -= OnCollectionChanged;
        _vm.FunctionParameterVMs.CollectionChanged -= OnCollectionChanged;
        _vm.PropertyChanged -= OnViewModelPropertyChanged;

        foreach (var n in _vm.Nodes) ((INotifyPropertyChanged)n).PropertyChanged -= OnElementPropertyChanged;
        foreach (var s in _vm.Starts) ((INotifyPropertyChanged)s).PropertyChanged -= OnElementPropertyChanged;
        foreach (var l in _vm.Links) ((INotifyPropertyChanged)l).PropertyChanged -= OnElementPropertyChanged;
        foreach (var c in _vm.CommentBlocks) ((INotifyPropertyChanged)c).PropertyChanged -= OnElementPropertyChanged;
        foreach (var g in _vm.GhostNodes) ((INotifyPropertyChanged)g).PropertyChanged -= OnElementPropertyChanged;
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
        {
            foreach (INotifyPropertyChanged item in e.NewItems)
            {
                item.PropertyChanged += OnElementPropertyChanged;
            }
        }
        if (e.OldItems is not null)
        {
            foreach (INotifyPropertyChanged item in e.OldItems)
            {
                item.PropertyChanged -= OnElementPropertyChanged;
            }
        }

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
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(InvalidateAndMeasure);
        }


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
                maxX = Math.Max(maxX, n.X + NodeRenderWidth(n) + 400);
                maxY = Math.Max(maxY, n.Y + NodeRenderHeight(n) + 400);
            }
            foreach (var s in _vm.Starts)
            {
                maxX = Math.Max(maxX, s.X + s.Diameter + 400);
                maxY = Math.Max(maxY, s.Y + s.Diameter + 400);
            }
            foreach (var c in _vm.CommentBlocks)
            {
                maxX = Math.Max(maxX, c.X + c.Width + 400);
                maxY = Math.Max(maxY, c.Y + c.Height + 400);
            }
            foreach (var fi in _vm.FunctionInstances)
            {
                maxX = Math.Max(maxX, fi.X + fi.Width + 400);
                maxY = Math.Max(maxY, fi.Y + fi.Height + 400);
            }
            foreach (var ft in _vm.FunctionTemplates)
            {
                maxX = Math.Max(maxX, ft.X + ft.Width + 400);
                maxY = Math.Max(maxY, ft.Y + ft.Height + 400);
            }
            foreach (var g in _vm.GhostNodes)
            {
                maxX = Math.Max(maxX, g.X + g.Width + 400);
                maxY = Math.Max(maxY, g.Y + g.Height + 400);
            }
        }
        // Measure the inline editor so Avalonia knows its desired size.
        if (_editingParamNode is not null)
        {
            double editorW = (_editingParamEditorW > 0 ? _editingParamEditorW : NodeRenderWidth(_editingParamNode)) * _scale;
            double editorH = HookRowHeight * _scale;
            if (_editingParamIsEnum)
                _inlineEnumEditor.Measure(new Size(editorW, editorH));
            else
                _inlineEditor.Measure(new Size(editorW, editorH));
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
        // Measure the comment header editor.
        if (_editingCommentHeaderBlock is not null)
        {
            _commentHeaderEditor.Measure(new Size(_editingCommentHeaderEditorW * _scale, _editingCommentHeaderEditorH * _scale));
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
            var editorRect = new Rect(
                _editingParamEditorX * _scale,
                _editingParamEditorY * _scale,
                _editingParamEditorW * _scale,
                HookRowHeight * _scale);
            if (_editingParamIsEnum)
            {
                _inlineEnumEditor.FontSize = HookFontSize * _scale;
                _inlineEnumEditor.Arrange(editorRect);
            }
            else
            {
                _inlineEditor.FontSize = HookFontSize * _scale;
                _inlineEditor.Arrange(editorRect);
            }
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
                    tb.Padding = new Thickness(itemPadH, itemPadV, itemPadH, itemPadV);
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
        // Position the comment header editor over the header band of the block being edited.
        if (_editingCommentHeaderBlock is not null)
        {
            _commentHeaderEditor.FontSize = CommentHeaderFontSize * _scale;
            _commentHeaderEditor.Arrange(new Rect(
                _editingCommentHeaderEditorX * _scale,
                _editingCommentHeaderEditorY * _scale,
                _editingCommentHeaderEditorW * _scale,
                _editingCommentHeaderEditorH * _scale));
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
            bx = sv.Offset.X + sv.Viewport.Width - zw - margin;
            by = sv.Offset.Y + sv.Viewport.Height - zh - margin;
        }
        _zoomBar.Arrange(new Rect(Math.Max(0, bx), Math.Max(0, by), zw, zh));
        return finalSize;
    }

    private void UpdateZoomBarColors(bool isLight)
    {
        _zoomBarIsLight = isLight;
        if (isLight)
        {
            // Light neon pill palette
            _zoomBar.Background = new SolidColorBrush(Color.FromArgb(0xF4, 0xF8, 0xFF, 0xEE));
            _zoomBar.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x66, 0xCC));
            _zoomTextBox.Background = new SolidColorBrush(Color.FromArgb(0x0A, 0x00, 0x00, 0x00));
            _zoomTextBox.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x30, 0x88));
            _zoomMinusBtn.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x30, 0x88));
            _zoomPlusBtn.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x30, 0x88));
        }
        else
        {
            // Dark neon pill palette
            _zoomBar.Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x05, 0x05, 0x10));
            _zoomBar.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xD4, 0xFF));
            _zoomTextBox.Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
            _zoomTextBox.Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xFF, 0xFF));
            _zoomMinusBtn.Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xFF, 0xFF));
            _zoomPlusBtn.Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xFF, 0xFF));
        }
    }

    /// <summary>
    /// Draws a neon glow halo around a rounded rectangle using 5 outward-expanding semi-transparent
    /// layers of <paramref name="glowColor"/>, fading from fully transparent at the outer edge to
    /// relatively vivid just inside the object border.
    /// </summary>
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
                double cx = sv.Offset.X + sv.Viewport.Width / 2.0;
                double cy = sv.Offset.Y + sv.Viewport.Height / 2.0;
                sv.Offset = new Vector(
                    Math.Max(0, cx * ratio - sv.Viewport.Width / 2.0),
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

    /// <summary>
    /// Scrolls the host <see cref="ScrollViewer"/> when the pointer is within
    /// <see cref="AutoScrollZone"/> pixels of any viewport edge during an element drag.
    /// The scroll delta is proportional to how far inside the zone the cursor sits,
    /// reaching <see cref="AutoScrollSpeed"/> at the very edge.
    /// Also starts/stops the continuous <see cref="_autoScrollTimer"/> based on whether
    /// the cursor is currently inside the scroll zone.
    /// </summary>
    private void TryAutoScrollForDrag(Point svPos)
    {
        _lastSvPos = svPos;
        var sv = GetScrollViewer();
        if (sv is null) return;

        double vw = sv.Bounds.Width;
        double vh = sv.Bounds.Height;

        double dx = 0.0, dy = 0.0;

        if (svPos.X < AutoScrollZone)
        {
            dx = -(AutoScrollZone - svPos.X) / AutoScrollZone * AutoScrollSpeed;
        }
        else if (svPos.X > vw - AutoScrollZone)
        {
            dx = (svPos.X - (vw - AutoScrollZone)) / AutoScrollZone * AutoScrollSpeed;
        }
        if (svPos.Y < AutoScrollZone)
        {
            dy = -(AutoScrollZone - svPos.Y) / AutoScrollZone * AutoScrollSpeed;
        }
        else if (svPos.Y > vh - AutoScrollZone)
        {
            dy = (svPos.Y - (vh - AutoScrollZone)) / AutoScrollZone * AutoScrollSpeed;
        }
        if (dx != 0.0 || dy != 0.0)
        {
            sv.Offset = new Vector(
                Math.Max(0, sv.Offset.X + dx),
                Math.Max(0, sv.Offset.Y + dy));
            if (!_autoScrollTimer.IsEnabled)
                _autoScrollTimer.Start();
        }
        else
        {
            if (_autoScrollTimer.IsEnabled)
                _autoScrollTimer.Stop();
        }
    }

    /// <summary>
    /// Called by <see cref="_autoScrollTimer"/> (~60 Hz) while the cursor is stationary
    /// inside the auto-scroll zone. Re-applies the scroll step so the canvas continues
    /// to move even without new pointer-move events.
    /// </summary>
    private void OnAutoScrollTick(object? sender, EventArgs e)
    {
        if (_dragging is null)
        {
            _autoScrollTimer.Stop();
            return;
        }
        TryAutoScrollForDrag(_lastSvPos);
    }

    private void OnZoomTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return)
        {
            TryApplyZoomText();
            e.Handled = true;
        }
    }

}
