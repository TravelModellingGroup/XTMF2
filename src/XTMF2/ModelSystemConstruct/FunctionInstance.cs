/*
    Copyright 2026, Travel Modelling Group, University of Toronto

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
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using XTMF2.Editing;
using XTMF2.ModelSystemConstruct.Parameters;

namespace XTMF2.ModelSystemConstruct
{
    /// <summary>
    /// Represents an instantiation of a <see cref="FunctionTemplate"/> placed on a boundary's
    /// canvas.  A <see cref="FunctionInstance"/> is a <see cref="Node"/>: its
    /// <see cref="Node.Type"/> mirrors the template's <see cref="FunctionTemplate.EntryNode"/>
    /// type, making it a valid link destination whenever the entry-node type is compatible with
    /// the originating hook.
    /// </summary>
    public sealed class FunctionInstance : Node
    {
        internal readonly record struct PendingLoad(
            Boundary ParentBoundary,
            string Name,
            string? TemplateName,
            Guid? TemplateId,
            int Index,
            Rectangle Location,
            Guid Id,
            bool Disabled);

        // ── Additional JSON property names (NameProperty / X / Y / Width / HeightProperty /
        //    IndexProperty are inherited as protected constants from Node) ─────────────
        private const string TemplateNameProperty = "TemplateName";
        private const string TemplateIdProperty = "TemplateId";

        /// <summary>
        /// The <see cref="FunctionTemplate"/> that this is an instantiation of.
        /// </summary>
        public FunctionTemplate Template { get; }

        /// <summary>
        /// Initialises a new <see cref="FunctionInstance"/>.
        /// The underlying <see cref="Node"/> is given an empty hooks list (FunctionInstances
        /// act as link destinations, never as link origins) and initially typed as
        /// <see cref="object"/>; the actual <see cref="Type"/> is computed from
        /// <see cref="FunctionTemplate.EntryNode"/> at access time.
        /// </summary>
        public FunctionInstance(string name, FunctionTemplate template, Boundary containedWithin, Rectangle location, Guid id = default)
            : base(name, typeof(object), containedWithin, Array.Empty<NodeHook>(), location, id)
        {
            Template = template;
            // Re-fire our Type property when the template's designated entry node changes.
            ((INotifyPropertyChanged)template).PropertyChanged += OnTemplatePropertyChanged;
            // Rebuild the cached Hooks list whenever FunctionParameters change.
            ((INotifyCollectionChanged)template.FunctionParameters).CollectionChanged += OnFunctionParametersChanged;
            // Track individual FunctionParameter renames so hook labels stay current.
            foreach (var fp in template.FunctionParameters)
                ((INotifyPropertyChanged)fp).PropertyChanged += OnFunctionParameterPropertyChanged;
        }

        private void OnFunctionParametersChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // Manage per-item subscriptions for newly added / removed parameters.
            if (e.OldItems is not null)
                foreach (FunctionParameter fp in e.OldItems)
                    ((INotifyPropertyChanged)fp).PropertyChanged -= OnFunctionParameterPropertyChanged;
            if (e.NewItems is not null)
                foreach (FunctionParameter fp in e.NewItems)
                    ((INotifyPropertyChanged)fp).PropertyChanged += OnFunctionParameterPropertyChanged;

            _cachedHooks = null;
            InvokePropertyChanged(nameof(Hooks));
        }

        private void OnFunctionParameterPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(FunctionParameter.Name))
            {
                // FunctionParameterHook.Name is a live computed property, so we only need
                // to notify observers that the Hooks collection's labels have changed.
                _cachedHooks = null;
                InvokePropertyChanged(nameof(Hooks));
            }
        }

        private void OnTemplatePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(FunctionTemplate.EntryNode) or nameof(FunctionTemplate.Type))
                InvokePropertyChanged(nameof(Type));
        }

        /// <summary>
        /// The effective module type of this function instance — mirrors the template's
        /// <see cref="FunctionTemplate.EntryNode"/> type.  Returns <see cref="typeof(object)"/>
        /// (no compatible hooks) when no entry node has been designated.
        /// </summary>
        public override Type Type => Template.EntryNode?.Type ?? typeof(object);

        // ── Dynamic hooks (one per FunctionParameter) ─────────────────────

        private IReadOnlyList<NodeHook>? _cachedHooks;
        private readonly Dictionary<FunctionParameter, FunctionParameterHook> _functionParameterHooks = new();

        /// <summary>
        /// The outgoing hooks of this function instance, one per
        /// <see cref="FunctionTemplate.FunctionParameters"/> in the referenced template.
        /// Each hook corresponds to a <see cref="FunctionParameter"/> placeholder inside the
        /// template; linking <c>FI.hookForP → ExternalNode</c> supplies the module that will
        /// fill parameter P at run-time.
        /// </summary>
        public override IReadOnlyList<NodeHook> Hooks
        {
            get
            {
                if (_cachedHooks is null)
                {
                    var list = new List<NodeHook>();
                    int idx = 0;
                    foreach (var fp in Template.FunctionParameters)
                    {
                        if (!_functionParameterHooks.TryGetValue(fp, out var hook))
                        {
                            hook = new FunctionParameterHook(fp, idx);
                            _functionParameterHooks.Add(fp, hook);
                        }
                        hook.RefreshCardinality();
                        list.Add(hook);
                        idx++;
                    }
                    _cachedHooks = list.AsReadOnly();
                }
                else
                {
                    foreach (var hook in _functionParameterHooks.Values)
                        hook.RefreshCardinality();
                }
                return _cachedHooks;
            }
        }

        // ── Persistence ───────────────────────────────────────────────────

        /// <summary>
        /// Serialises this function instance.  The <paramref name="nodeDictionary"/> index is
        /// written as "Index" so that links targeting this FI can be resolved on load.
        /// </summary>
        internal void Save(ref int index, Dictionary<Node, int> nodeDictionary, Utf8JsonWriter writer)
        {
            if (!nodeDictionary.TryGetValue(this, out int myIndex))
            {
                myIndex = index++;
                nodeDictionary[this] = myIndex;
            }
            writer.WriteStartObject();
            writer.WriteString(IdProperty, Id);
            writer.WriteString(NameProperty, Name);
            var qualifiedName = Boundary.GetQualifiedTemplateName(ContainedWithin, Template) ?? Template.Name;
            writer.WriteString(TemplateNameProperty, qualifiedName);
            writer.WriteString(TemplateIdProperty, Template.Id);
            writer.WriteNumber(IndexProperty, myIndex);
            writer.WriteNumber(XProperty, Location.X);
            writer.WriteNumber(YProperty, Location.Y);
            writer.WriteNumber(WidthProperty, Location.Width);
            writer.WriteNumber(HeightProperty, Location.Height);
            if (IsDisabled)
            {
                writer.WriteBoolean(DisabledProperty, true);
            }
            writer.WriteEndObject();
        }

        /// <summary>
        /// Loads a <see cref="FunctionInstance"/> from JSON and registers it in
        /// <paramref name="node"/> so that subsequent link entries can reference it by index.
        /// The reader must be positioned at a <see cref="JsonTokenType.StartObject"/> token.
        /// </summary>
        internal static bool Load(ref Utf8JsonReader reader, Boundary parentBoundary,
            Dictionary<int, Node> node,
            out FunctionInstance? instance,
            [NotNullWhen(false)] ref string? error,
            List<PendingLoad>? deferredLoads = null)
        {
            instance = null;
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                error = "Unexpected token when reading FunctionInstance.";
                return false;
            }

            string? name         = null;
            string? templateName = null;
            Guid?  templateId   = null;
            int     fiIndex      = -1;
            Guid    id           = Guid.Empty;
            float   x = 40, y = 40, w = 120, h = 50;
            bool    disabled     = false;

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName) continue;

                if      (reader.ValueTextEquals(IdProperty))            { reader.Read(); var s = reader.GetString(); if (s is not null) Guid.TryParse(s, out id); }
                else if (reader.ValueTextEquals(NameProperty))         { reader.Read(); name         = reader.GetString(); }
                else if (reader.ValueTextEquals(TemplateNameProperty)) { reader.Read(); templateName = reader.GetString(); }
                else if (reader.ValueTextEquals(TemplateIdProperty))   { reader.Read(); if (reader.TryGetGuid(out var parsedId)) templateId = parsedId; }
                else if (reader.ValueTextEquals(IndexProperty))        { reader.Read(); fiIndex      = reader.GetInt32();  }
                else if (reader.ValueTextEquals(XProperty))            { reader.Read(); x            = reader.GetSingle(); }
                else if (reader.ValueTextEquals(YProperty))            { reader.Read(); y            = reader.GetSingle(); }
                else if (reader.ValueTextEquals(WidthProperty))        { reader.Read(); w            = reader.GetSingle(); }
                else if (reader.ValueTextEquals(HeightProperty))       { reader.Read(); h            = reader.GetSingle(); }
                else if (reader.ValueTextEquals(DisabledProperty))     { reader.Read(); disabled     = reader.GetBoolean(); }
                else                                                              reader.Skip();
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                error = "FunctionInstance is missing its Name.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(templateName))
            {
                error = $"FunctionInstance '{name}' is missing its TemplateName.";
                return false;
            }

            if (deferredLoads is not null)
            {
                deferredLoads.Add(new PendingLoad(parentBoundary, name, templateName, templateId,
                    fiIndex, new Rectangle(x, y, w, h), id, disabled));
                error = null;
                return true;
            }

            var template = Boundary.ResolveTemplate(parentBoundary, templateName!);
            if (template is null && templateId.HasValue)
                template = Boundary.ResolveTemplate(parentBoundary, templateId.Value);
            if (template is null)
            {
                var modelSystemRoot = parentBoundary;
                while (modelSystemRoot.Parent is not null)
                    modelSystemRoot = modelSystemRoot.Parent;
                template = Boundary.ResolveUniqueTemplateByName(modelSystemRoot, templateName!);
            }
            if (template is null)
            {
                error = templateId.HasValue
                    ? $"FunctionInstance '{name}' references unknown FunctionTemplate '{templateName}' ({templateId.Value})."
                    : $"FunctionInstance '{name}' references unknown FunctionTemplate '{templateName}'.";
                return false;
            }

            instance = new FunctionInstance(name, template, parentBoundary, new Rectangle(x, y, w, h), id);
            if (disabled)
            {
                _ = instance.SetDisabled(true, out _);
            }

            // Register in the node dictionary so links can resolve this FI as a destination.
            if (fiIndex >= 0)
                node[fiIndex] = instance;

            error = null;
            return true;
        }

        // ── Runtime cloning ───────────────────────────────────────────────

        /// <summary>
        /// Maps each node in the template's <see cref="FunctionTemplate.InternalModules"/>
        /// to the per-instance <see cref="IModule"/> created for this run.
        /// Populated by <see cref="ConstructRuntimeModules"/>; null between runs.
        /// </summary>
        private Dictionary<Node, IModule>? _runtimeModules;

        /// <summary>
        /// Maps each <see cref="FunctionParameter"/> of the template to the external
        /// <see cref="IModule"/> that was wired to this instance via a
        /// <see cref="FunctionParameterHook"/> link.
        /// Populated during <see cref="ConstructLinks"/> on the enclosing boundary;
        /// null between runs.
        /// </summary>
        private Dictionary<FunctionParameter, IModule?>? _parameterBindings;

        // ── Per-instance execution context (thread-local) ─────────────────

        /// <summary>
        /// Thread-local stack of <see cref="FunctionInstance"/> objects that are currently
        /// evaluating a scripted-parameter expression on this thread.  Pushed by
        /// <see cref="FunctionInstanceExpression.GetValue"/> and popped in the finally block.
        /// </summary>
        [ThreadStatic]
        private static Stack<FunctionInstance>? _contextStack;

        /// <summary>
        /// The <see cref="FunctionInstance"/> whose scripted-parameter expression is currently
        /// being evaluated on the calling thread, or <c>null</c> if none.
        /// </summary>
        internal static FunctionInstance? Current
            => _contextStack?.Count > 0 ? _contextStack.Peek() : null;

        /// <summary>
        /// Returns the per-instance <see cref="IModule"/> bound to <paramref name="fp"/> on
        /// this instance, or <c>null</c> if no external module was wired to that parameter.
        /// </summary>
        internal IModule? GetBoundModule(FunctionParameter fp)
            => _parameterBindings?.TryGetValue(fp, out var m) == true ? m : null;

        /// <summary>
        /// Records <paramref name="module"/> as the runtime binding for
        /// <see cref="FunctionParameter"/> <paramref name="parameter"/> on this instance.
        /// Called by <see cref="SingleLink.Construct"/> when processing an outgoing
        /// <see cref="FunctionParameterHook"/> link.
        /// </summary>
        internal void BindParameter(FunctionParameter parameter, IModule? module)
        {
            _parameterBindings ??= new(ReferenceEqualityComparer.Instance);
            _parameterBindings[parameter] = module;
        }

        /// <summary>
        /// Returns the per-instance <see cref="IModule"/> cloned from
        /// <paramref name="templateNode"/> for this function instance, or <c>null</c>
        /// if the instance has not yet been constructed for a run.
        /// </summary>
        internal IModule? GetRuntimeModule(Node templateNode)
            => _runtimeModules is not null && _runtimeModules.TryGetValue(templateNode, out var m) ? m : null;

        /// <summary>
        /// Instantiates a fresh <see cref="IModule"/> for every node in the template's
        /// internal boundary, keyed by the template's original <see cref="Node"/>.
        /// Called by <see cref="Boundary.ConstructModules"/> during a model-system run.
        /// </summary>
        internal bool ConstructRuntimeModules(XTMFRuntime runtime, ref string? error, ref Guid? elementId)
        {
            if (IsDisabled)
            {
                _runtimeModules = null;
                _parameterBindings = null;
                error = null;
                return true;
            }
            _runtimeModules = new Dictionary<Node, IModule>(ReferenceEqualityComparer.Instance);
            _parameterBindings = new Dictionary<FunctionParameter, IModule?>(ReferenceEqualityComparer.Instance);
            var internals = Template.InternalModules;
            foreach (var start in internals.Starts)
            {
                if (!start.ConstructModuleInstance(runtime, out var m, ref error))
                {
                    elementId = start.Id;
                    return false;  
                } 
                if (m is RuntimeModules.GetFunctionInstanceName getInstanceName)
                    getInstanceName.FunctionInstanceName = Name;
                _runtimeModules[start] = m!;
                WrapScriptedExpression(start, m!);
            }
            foreach (var node in internals.Modules)
            {
                if (!node.ConstructModuleInstance(runtime, out var m, ref error)) 
                {
                    elementId = node.Id;
                    return false;
                }
                if (m is RuntimeModules.GetFunctionInstanceName getInstanceName)
                    getInstanceName.FunctionInstanceName = Name;
                _runtimeModules[node] = m!;
                WrapScriptedExpression(node, m!);
            }
            error = null;
            return true;
        }

        /// <summary>
        /// If <paramref name="node"/>'s ParameterValue is a compiled scripted expression,
        /// replaces the <c>Expression</c> field on the cloned <paramref name="module"/> with
        /// a <see cref="FunctionInstanceExpression"/> wrapper so that variable lookups inside
        /// the AST during evaluation can find this instance's per-instance modules and
        /// FunctionParameter bindings via <see cref="Current"/>.
        /// </summary>
        private void WrapScriptedExpression(Node node, IModule module)
        {
            if (node.ParameterValue is not ScriptedParameter) return;
            var exprField = module.GetType().GetField("Expression",
                BindingFlags.Public | BindingFlags.Instance);
            if (exprField?.GetValue(module) is ParameterExpression inner)
                exprField.SetValue(module, new FunctionInstanceExpression(inner, this));
        }

        /// <summary>
        /// A <see cref="ParameterExpression"/> wrapper that pushes this
        /// <see cref="FunctionInstance"/> onto <see cref="_contextStack"/> for the duration
        /// of <see cref="GetValue"/>, making it available to variable resolvers via
        /// <see cref="Current"/>.
        /// </summary>
        private sealed class FunctionInstanceExpression : ParameterExpression
        {
            private readonly ParameterExpression _inner;
            private readonly FunctionInstance _fi;

            internal FunctionInstanceExpression(ParameterExpression inner, FunctionInstance fi)
            {
                _inner = inner;
                _fi = fi;
            }

            public override bool IsCompatible(Type type, [NotNullWhen(false)] ref string? errorString)
                => _inner.IsCompatible(type, ref errorString);

            public override object? GetValue(IModule caller, Type type, ref string? errorString)
            {
                (_contextStack ??= new Stack<FunctionInstance>()).Push(_fi);
                try   { return _inner.GetValue(caller, type, ref errorString); }
                finally { _contextStack.Pop(); }
            }

            public override bool GetValueAtEditingTime(Type outputType, 
                [NotNullWhen(true)] out object? convertedValue,
                [NotNullWhen(false)] out string? errorString)
            {
                (_contextStack ??= new Stack<FunctionInstance>()).Push(_fi);
                try   { return _inner.GetValueAtEditingTime(outputType, out convertedValue, out errorString); }
                finally { _contextStack.Pop(); }
            }

            public override string Representation => _inner.Representation;
            public override Type Type => _inner.Type;

            internal override void Save(Utf8JsonWriter writer) => _inner.Save(writer);

            internal override bool AssignToParameter(IModule module, ref string? error)
                => _inner.AssignToParameter(module, ref error);
        }

        /// <summary>
        /// Wires the template's internal links using the per-instance cloned modules.
        /// Called by <see cref="Boundary.ConstructLinks"/> during a model-system run.
        /// </summary>
        internal bool ConstructRuntimeLinks(ref string? error)
        {
            if (IsDisabled)
            {
                error = null;
                return true;
            }
            if (_runtimeModules is null) { error = null; return true; }
            foreach (var link in Template.InternalModules.Links)
            {
                if (!ConstructRuntimeLink(link, ref error)) return false;
            }
            error = null;
            return true;
        }

        private IModule? ResolveRuntimeDestModule(Node dest)
        {
            var r = dest is GhostNode gn ? gn.ReferencedNode : dest;
            // A FunctionInstance on the outer boundary is not in _runtimeModules (those only
            // contain clones of nodes inside this template).  Node.Module is never set for
            // FunctionInstances, so we must call GetRuntimeModule on the target instance.
            if (r is FunctionInstance fi)
                return fi.Template.EntryNode is not null
                    ? fi.GetRuntimeModule(fi.Template.EntryNode)
                    : null;
            return _runtimeModules!.TryGetValue(r, out var m) ? m : r.Module;
        }

        private bool ConstructRuntimeLink(Link link, ref string? error)
        {
            if (!_runtimeModules!.TryGetValue(link.Origin, out var originModule))
            {
                error = $"FunctionInstance '{Name}': internal link origin '{link.Origin.Name}' has no cloned module.";
                return false;
            }
            if (link.IsDisabled) return true;

            if (link is SingleLink sl)
            {
                // ── Transitive FunctionParameter wiring ───────────────────────────────
                if (sl.Destination is FunctionParameter fp)
                {
                    if (_parameterBindings is not null
                        && _parameterBindings.TryGetValue(fp, out var boundModule)
                        && boundModule is not null)
                    {
                        sl.OriginHook.Install(originModule, boundModule, 0);
                    }
                    else if (sl.OriginHook.Cardinality == HookCardinality.Single)
                    {
                        error = $"FunctionInstance '{Name}': FunctionParameter '{fp.Name}' has no external binding " +
                                $"but hook '{sl.OriginHook.Name}' requires one (Single cardinality).";
                        return false;
                    }
                    return true;
                }

                var dest = sl.Destination is GhostNode gn ? gn.ReferencedNode : sl.Destination!;
                bool destDisabled = dest.IsDisabled
                    || (dest is FunctionInstance destFi
                        && (destFi.IsDisabled || destFi.Template.EntryNode?.IsDisabled == true));
                if (sl.OriginHook.Cardinality == HookCardinality.Single && destDisabled)
                {
                    error = "An internal FunctionInstance link targets a disabled node for a required hook.";
                    return false;
                }
                var destModule = ResolveRuntimeDestModule(sl.Destination!);
                if (destModule is not null)
                    sl.OriginHook.Install(originModule, destModule, 0);
            }
            else if (link is MultiLink ml)
            {
                int enabled = 0;
                foreach (var d in ml.Destinations)
                {
                    if (d is FunctionParameter fpDest)
                    {
                        if (_parameterBindings is not null
                            && _parameterBindings.TryGetValue(fpDest, out var bm) && bm is not null)
                            enabled++;
                    }
                    else
                    {
                        var r = d is GhostNode rGn ? rGn.ReferencedNode : d;
                        bool runtimeDisabled = r.IsDisabled
                            || (r is FunctionInstance nestedFi
                                && (nestedFi.IsDisabled || nestedFi.Template.EntryNode?.IsDisabled == true));
                        if (!runtimeDisabled && ResolveRuntimeDestModule(d) is not null) enabled++;
                    }
                }
                if (ml.OriginHook.Cardinality == HookCardinality.AtLeastOne && enabled == 0)
                {
                    error = "An internal FunctionInstance MultiLink requires at least one enabled destination.";
                    return false;
                }
                ml.OriginHook.CreateArray(originModule, enabled);
                int idx = 0;
                foreach (var d in ml.Destinations)
                {
                    if (d is FunctionParameter fpDest)
                    {
                        if (_parameterBindings is not null
                            && _parameterBindings.TryGetValue(fpDest, out var bm) && bm is not null)
                            ml.OriginHook.Install(originModule, bm, idx++);
                    }
                    else
                    {
                        var r = d is GhostNode rGn ? rGn.ReferencedNode : d;
                        var dm = ResolveRuntimeDestModule(d);
                        bool runtimeDisabled = r.IsDisabled
                            || (r is FunctionInstance nestedFi
                                && (nestedFi.IsDisabled || nestedFi.Template.EntryNode?.IsDisabled == true));
                        if (!runtimeDisabled && dm is not null)
                            ml.OriginHook.Install(originModule, dm, idx++);
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Fills any unset <c>AnyNumber</c> hooks on the cloned modules with empty arrays.
        /// Called by <see cref="Boundary.ConstructEmptyLinks"/> during a model-system run.
        /// </summary>
        internal void ConstructEmptyRuntimeLinks()
        {
            if (_runtimeModules is null) return;
            foreach (var (node, module) in _runtimeModules)
            {
                foreach (var hook in node.Hooks)
                {
                    if (hook.Cardinality == HookCardinality.AnyNumber && !hook.AnyInstalled(module))
                        hook.CreateArray(module, 0);
                }
            }
        }

        /// <summary>
        /// Runs <see cref="IModule.RuntimeValidation"/> on all per-instance cloned modules.
        /// Called by the run engine's runtime-validation phase.
        /// </summary>
        internal bool ValidateRuntimeModules(ref string? moduleName, ref string? error)
        {
            Guid? elementId = null;
            return ValidateRuntimeModules(ref moduleName, ref error, ref elementId);
        }

        internal bool ValidateRuntimeModules(ref string? moduleName, ref string? error, ref Guid? elementId)
        {
            if (_runtimeModules is null) { error = null; return true; }
            foreach (var (node, module) in _runtimeModules)
            {
                try
                {
                    if (!module.RuntimeValidation(ref error))
                    {
                        moduleName = Name + "." + node.Name;
                        // Internal template nodes are not directly visible from the boundary;
                        // navigate to the owning FunctionInstance on the canvas.
                        elementId = Id;
                        return false;
                    }
                }
                catch (Exception e)
                {
                    moduleName = Name + "." + node.Name;
                    elementId = Id;
                    error = e.Message;
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Disposes all per-instance cloned modules.  Called by the run engine's cleanup phase.
        /// </summary>
        internal void DisposeRuntimeModules()
        {
            if (_runtimeModules is null) return;

            foreach (var module in _runtimeModules.Values)
            {
                if (module is IDisposable disposable)
                {
                    try { disposable.Dispose(); }
                    catch { /* ignore */ }
                }
            }
        }
    }
}
