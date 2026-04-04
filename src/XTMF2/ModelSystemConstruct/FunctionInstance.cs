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
using System.Text.Json;
using XTMF2.Editing;

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
        // ── Additional JSON property names (NameProperty / X / Y / Width / HeightProperty /
        //    IndexProperty are inherited as protected constants from Node) ─────────────
        private const string TemplateNameProperty = "TemplateName";

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
        public FunctionInstance(string name, FunctionTemplate template, Boundary containedWithin, Rectangle location)
            : base(name, typeof(object), containedWithin, Array.Empty<NodeHook>(), location)
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
                        list.Add(new FunctionParameterHook(fp, idx++));
                    _cachedHooks = list.AsReadOnly();
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
            writer.WriteString(NameProperty, Name);
            var qualifiedName = Boundary.GetQualifiedTemplateName(ContainedWithin, Template) ?? Template.Name;
            writer.WriteString(TemplateNameProperty, qualifiedName);
            writer.WriteNumber(IndexProperty, myIndex);
            writer.WriteNumber(XProperty, Location.X);
            writer.WriteNumber(YProperty, Location.Y);
            writer.WriteNumber(WidthProperty, Location.Width);
            writer.WriteNumber(HeightProperty, Location.Height);
            writer.WriteEndObject();
        }

        /// <summary>
        /// Loads a <see cref="FunctionInstance"/> from JSON and registers it in
        /// <paramref name="node"/> so that subsequent link entries can reference it by index.
        /// The reader must be positioned at a <see cref="JsonTokenType.StartObject"/> token.
        /// </summary>
        internal static bool Load(ref Utf8JsonReader reader, Boundary parentBoundary,
            Dictionary<int, Node> node,
            [NotNullWhen(true)] out FunctionInstance? instance,
            [NotNullWhen(false)] ref string? error)
        {
            instance = null;
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                error = "Unexpected token when reading FunctionInstance.";
                return false;
            }

            string? name         = null;
            string? templateName = null;
            int     fiIndex      = -1;
            float   x = 40, y = 40, w = 120, h = 50;

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName) continue;

                if      (reader.ValueTextEquals(NameProperty))         { reader.Read(); name         = reader.GetString(); }
                else if (reader.ValueTextEquals(TemplateNameProperty)) { reader.Read(); templateName = reader.GetString(); }
                else if (reader.ValueTextEquals(IndexProperty))        { reader.Read(); fiIndex      = reader.GetInt32();  }
                else if (reader.ValueTextEquals(XProperty))            { reader.Read(); x            = reader.GetSingle(); }
                else if (reader.ValueTextEquals(YProperty))            { reader.Read(); y            = reader.GetSingle(); }
                else if (reader.ValueTextEquals(WidthProperty))        { reader.Read(); w            = reader.GetSingle(); }
                else if (reader.ValueTextEquals(HeightProperty))       { reader.Read(); h            = reader.GetSingle(); }
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

            var template = Boundary.ResolveTemplate(parentBoundary, templateName!);
            if (template is null)
            {
                error = $"FunctionInstance '{name}' references unknown FunctionTemplate '{templateName}'.";
                return false;
            }

            instance = new FunctionInstance(name, template, parentBoundary, new Rectangle(x, y, w, h));

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
        internal bool ConstructRuntimeModules(XTMFRuntime runtime, ref string? error)
        {
            _runtimeModules = new Dictionary<Node, IModule>(ReferenceEqualityComparer.Instance);
            _parameterBindings = new Dictionary<FunctionParameter, IModule?>(ReferenceEqualityComparer.Instance);
            var internals = Template.InternalModules;
            foreach (var start in internals.Starts)
            {
                if (!start.ConstructModuleInstance(runtime, out var m, ref error)) return false;
                _runtimeModules[start] = m!;
            }
            foreach (var node in internals.Modules)
            {
                if (!node.ConstructModuleInstance(runtime, out var m, ref error)) return false;
                _runtimeModules[node] = m!;
            }
            error = null;
            return true;
        }

        /// <summary>
        /// Wires the template's internal links using the per-instance cloned modules.
        /// Called by <see cref="Boundary.ConstructLinks"/> during a model-system run.
        /// </summary>
        internal bool ConstructRuntimeLinks(ref string? error)
        {
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
                if (sl.OriginHook.Cardinality == HookCardinality.Single && dest.IsDisabled)
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
                        if (!r.IsDisabled && ResolveRuntimeDestModule(d) is not null) enabled++;
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
                        if (!r.IsDisabled && dm is not null)
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
            if (_runtimeModules is null) { error = null; return true; }
            foreach (var (node, module) in _runtimeModules)
            {
                try
                {
                    if (!module.RuntimeValidation(ref error))
                    {
                        moduleName = Name + "." + node.Name;
                        return false;
                    }
                }
                catch (Exception e)
                {
                    moduleName = Name + "." + node.Name;
                    error = e.Message;
                    return false;
                }
            }
            return true;
        }
    }
}
