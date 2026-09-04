/*
    Copyright 2017-2026 University of Toronto
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
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.ComponentModel;
using XTMF2.ModelSystemConstruct;
using XTMF2.ModelSystemConstruct.Parameters;
using XTMF2.ModelSystemConstruct.Parameters.Compiler;
using XTMF2.Repository;
using XTMF2.Bus.Optimization;

namespace XTMF2.Editing
{
    public sealed class ModelSystemSession : IDisposable, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private int _References = 0;

        public int References => _References;

        public ModelSystem ModelSystem { get; internal set; }

        private readonly ProjectSession _session;

        private readonly Lock _sessionLock = new ();

        public ModelSystemHeader ModelSystemHeader { get; private set; }

        /// <summary>
        /// The project that this model system session belongs to.
        /// </summary>
        public Project Project => _session.Project;

        private readonly CommandBuffer Buffer = new CommandBuffer();

        /// <summary>
        /// Starts collecting all subsequent undoable operations into a single batch entry
        /// so that the entire group can be undone with one Ctrl+Z.
        /// Must always be paired with <see cref="CommitBatch"/>.
        /// </summary>
        public void BeginBatch() => Buffer.BeginAggregateBatch();

        /// <summary>
        /// Closes the active aggregate batch and pushes it as one undoable entry.
        /// All operations recorded since the matching <see cref="BeginBatch"/> call
        /// will be reversed together by a single undo.
        /// </summary>
        public void CommitBatch() => Buffer.CommitAggregateBatch();

        private const string FunctionTemplateSnapshotSource = "XTMF2FunctionTemplate";
        private const int FunctionTemplateSnapshotVersion = 1;

        public ModelSystemSession(ProjectSession session, ModelSystem modelSystem)
        {
            ModelSystem = modelSystem;
            ModelSystemHeader = modelSystem.Header;
            _session = session.AddReference();
            ((INotifyPropertyChanged)Buffer).PropertyChanged += OnBufferPropertyChanged;
        }

        /// <summary>
        /// Exports <paramref name="template"/> as a portable JSON snapshot that includes
        /// all internal modules, links, parameters, entry-node designation, and local variables.
        /// </summary>
        public bool ExportFunctionTemplateSnapshot(FunctionTemplate template,
            [NotNullWhen(true)] out string? snapshot,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(template);
            try
            {
                lock (_sessionLock)
                {
                    var moduleTypes = new Dictionary<Type, int>();
                    var nodeIndices = new Dictionary<Node, int>();
                    int index = 0;

                    CollectTypesForFunctionTemplateSnapshot(template, moduleTypes);

                    var buffer = new ArrayBufferWriter<byte>();
                    using var writer = new Utf8JsonWriter(buffer);
                    writer.WriteStartObject();
                    writer.WriteString("source", FunctionTemplateSnapshotSource);
                    writer.WriteNumber("version", FunctionTemplateSnapshotVersion);
                    writer.WritePropertyName("template");
                    template.Save(ref index, nodeIndices, moduleTypes, writer);

                    var typeByIndex = new string?[moduleTypes.Count];
                    foreach (var kvp in moduleTypes)
                    {
                        // AssemblyQualifiedName can be null for some runtime-generated/generic type forms.
                        // Persist the strongest stable token we can resolve on import.
                        typeByIndex[kvp.Value] = kvp.Key.AssemblyQualifiedName
                            ?? kvp.Key.FullName
                            ?? kvp.Key.Name;
                    }

                    writer.WritePropertyName("types");
                    writer.WriteStartArray();
                    foreach (var aqn in typeByIndex)
                        writer.WriteStringValue(aqn);
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                    writer.Flush();

                    snapshot = Encoding.UTF8.GetString(buffer.WrittenSpan);
                    error = null;
                    return true;
                }
            }
            catch (Exception ex)
            {
                snapshot = null;
                error = new CommandError($"Failed to export function-template snapshot: {ex.Message}");
                return false;
            }
        }

        private static void CollectTypesForFunctionTemplateSnapshot(
            FunctionTemplate template,
            Dictionary<Type, int> types)
        {
            foreach (var fp in template.FunctionParameters)
                EnsureSnapshotType(types, fp.Type);

            CollectTypesForBoundarySnapshot(template.InternalModules, types);
        }

        private static void CollectTypesForBoundarySnapshot(
            Boundary boundary,
            Dictionary<Type, int> types)
        {
            foreach (var node in boundary.Modules)
                EnsureSnapshotType(types, node.Type);

            foreach (var child in boundary.Boundaries)
                CollectTypesForBoundarySnapshot(child, types);

            foreach (var ft in boundary.FunctionTemplates)
                CollectTypesForFunctionTemplateSnapshot(ft, types);
        }

        private static void EnsureSnapshotType(Dictionary<Type, int> types, Type? type)
        {
            if (type is null) return;
            if (!types.ContainsKey(type))
                types[type] = types.Count;
        }

        /// <summary>
        /// Imports a function-template snapshot into <paramref name="targetBoundary"/> as a new
        /// template, optionally overriding its name and location.
        /// </summary>
        public bool ImportFunctionTemplateSnapshot(
            User user,
            Boundary targetBoundary,
            string snapshot,
            string? nameOverride,
            Rectangle? locationOverride,
            [NotNullWhen(true)] out FunctionTemplate? template,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(targetBoundary);
            ArgumentNullException.ThrowIfNull(snapshot);
            template = null;

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }

                if (!TryLoadFunctionTemplateFromSnapshot(targetBoundary, snapshot, out var loadedTemplate, out var loadError))
                {
                    error = loadError;
                    return false;
                }

                var desiredName = string.IsNullOrWhiteSpace(nameOverride)
                    ? loadedTemplate!.Name
                    : nameOverride!.Trim();
                if (string.IsNullOrWhiteSpace(desiredName))
                    desiredName = "Function Template";

                desiredName = MakeUniqueFunctionTemplateName(desiredName);
                loadedTemplate!.Name = desiredName;
                loadedTemplate.SetParent(targetBoundary);
                if (locationOverride.HasValue)
                    loadedTemplate.SetLocation(locationOverride.Value);

                if (!targetBoundary.AddFunctionTemplate(loadedTemplate, out error))
                    return false;

                template = loadedTemplate;
                var captured = loadedTemplate;
                Buffer.AddUndo(new Command(() =>
                {
                    return (targetBoundary.RemoveFunctionTemplate(captured, out var e), e);
                }, () =>
                {
                    return (targetBoundary.AddFunctionTemplate(captured, out var e), e);
                }));
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Finds a function template in the current model system that is equivalent to the
        /// supplied snapshot, ignoring layout coordinates and persisted identifiers.
        /// </summary>
        public bool TryFindEquivalentFunctionTemplateSnapshot(
            string snapshot,
            out FunctionTemplate? template,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            template = null;
            lock (_sessionLock)
            {
                if (!TryCanonicalizeFunctionTemplateSnapshot(snapshot, out var targetCanonical, out error))
                    return false;

                foreach (var candidate in EnumerateAllFunctionTemplates())
                {
                    if (!ExportFunctionTemplateSnapshot(candidate, out var candidateSnapshot, out error))
                        return false;

                    if (!TryCanonicalizeFunctionTemplateSnapshot(candidateSnapshot!, out var candidateCanonical, out error))
                        return false;

                    if (string.Equals(targetCanonical, candidateCanonical, StringComparison.Ordinal))
                    {
                        template = candidate;
                        error = null;
                        return true;
                    }
                }

                error = null;
                return true;
            }
        }

        private string MakeUniqueFunctionTemplateName(string baseName)
        {
            var name = baseName;
            int suffix = 2;
            while (ModelSystem.GlobalBoundary.ContainsFunctionTemplateName(name))
                name = $"{baseName} ({suffix++})";
            return name;
        }

        private IEnumerable<FunctionTemplate> EnumerateAllFunctionTemplates()
        {
            var stack = new Stack<Boundary>();
            stack.Push(ModelSystem.GlobalBoundary);
            while (stack.Count > 0)
            {
                var boundary = stack.Pop();
                foreach (var template in boundary.FunctionTemplates)
                {
                    yield return template;
                    stack.Push(template.InternalModules);
                }
                foreach (var child in boundary.Boundaries)
                    stack.Push(child);
            }
        }

        private bool TryLoadFunctionTemplateFromSnapshot(
            Boundary targetBoundary,
            string snapshot,
            [NotNullWhen(true)] out FunctionTemplate? template,
            [NotNullWhen(false)] out CommandError? error)
        {
            template = null;
            try
            {
                using var document = JsonDocument.Parse(snapshot);
                var root = document.RootElement;
                if (!root.TryGetProperty("source", out var sourceEl)
                    || sourceEl.GetString() != FunctionTemplateSnapshotSource)
                {
                    error = new CommandError("Invalid function-template snapshot source.");
                    return false;
                }
                if (!root.TryGetProperty("template", out var templateEl)
                    || !root.TryGetProperty("types", out var typesEl)
                    || typesEl.ValueKind != JsonValueKind.Array)
                {
                    error = new CommandError("The function-template snapshot is missing required fields.");
                    return false;
                }

                var typeLookup = new Dictionary<int, Type>();
                int typeIndex = 0;
                foreach (var typeNameEl in typesEl.EnumerateArray())
                {
                    var aqn = typeNameEl.GetString();
                    if (string.IsNullOrWhiteSpace(aqn))
                    {
                        error = new CommandError($"Invalid type entry at index {typeIndex} in function-template snapshot.");
                        return false;
                    }
                    var type = ResolveSnapshotTypeToken(aqn);
                    if (type is null)
                    {
                        error = new CommandError($"Unable to resolve type '{aqn}' while importing a function-template snapshot.");
                        return false;
                    }
                    typeLookup[typeIndex++] = type;
                }

                var nodeLookup = new Dictionary<int, Node>();
                var scriptedParameters = new List<(Node toAssignTo, string parameterExpression)>();
                var deferredGhostNodes = new List<(Boundary ContainedIn, int RefIndex, int SelfIndex, Rectangle Location, Guid Id)>();

                var templateJson = templateEl.GetRawText();
                var utf8 = Encoding.UTF8.GetBytes(templateJson);
                var reader = new Utf8JsonReader(utf8);
                if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                {
                    error = new CommandError("Invalid function-template payload in snapshot.");
                    return false;
                }

                string? loadError = null;
                if (!FunctionTemplate.Load(GetModuleRepository(), typeLookup, nodeLookup, scriptedParameters,
                    deferredGhostNodes, ref reader, targetBoundary, out template, ref loadError))
                {
                    error = new CommandError(loadError ?? "Unable to load function-template snapshot.");
                    return false;
                }

                foreach (var (containedIn, refIndex, selfIndex, location, id) in deferredGhostNodes)
                {
                    if (!GhostNode.Resolve(nodeLookup, containedIn, refIndex, selfIndex, location, id, out var ghost, ref loadError))
                    {
                        continue;
                    }
                    containedIn.AddGhostNode(ghost!, out _);
                }

                foreach (var (toAssignTo, parameterExpression) in scriptedParameters)
                {
                    var localVars = toAssignTo.ContainedWithin?.OwningFunctionTemplate?.LocalVariables;
                    IList<Node> allVars = localVars is { Count: > 0 }
                        ? localVars.Concat(ModelSystem.Variables).ToList()
                        : (IList<Node>)ModelSystem.Variables;
                    _ = toAssignTo.SetParameterExpression(allVars, parameterExpression, out _);
                }

                error = null;
                return true;
            }
            catch (JsonException ex)
            {
                error = new CommandError($"Invalid function-template snapshot JSON: {ex.Message}");
                return false;
            }
        }

        private Type? ResolveSnapshotTypeToken(string token)
        {
            // First try runtime resolution (works for assembly-qualified names and many core types).
            var direct = Type.GetType(token, throwOnError: false);
            if (direct is not null)
                return direct;

            // Fallback: search known loaded module types and all discovered types.
            foreach (var t in GetModuleRepository().LoadedModuleTypes)
            {
                if (string.Equals(t.AssemblyQualifiedName, token, StringComparison.Ordinal)
                    || string.Equals(t.FullName, token, StringComparison.Ordinal)
                    || string.Equals(t.Name, token, StringComparison.Ordinal))
                {
                    return t;
                }
            }

            foreach (var t in _session.GetTypeRepository().Store)
            {
                if (string.Equals(t.AssemblyQualifiedName, token, StringComparison.Ordinal)
                    || string.Equals(t.FullName, token, StringComparison.Ordinal)
                    || string.Equals(t.Name, token, StringComparison.Ordinal))
                {
                    return t;
                }
            }

            return null;
        }

        private static bool TryCanonicalizeFunctionTemplateSnapshot(
            string snapshot,
            [NotNullWhen(true)] out string? canonical,
            [NotNullWhen(false)] out CommandError? error)
        {
            canonical = null;
            try
            {
                using var document = JsonDocument.Parse(snapshot);
                var root = document.RootElement;
                if (!root.TryGetProperty("source", out var sourceEl)
                    || sourceEl.GetString() != FunctionTemplateSnapshotSource
                    || !root.TryGetProperty("template", out var templateEl))
                {
                    error = new CommandError("Invalid function-template snapshot source.");
                    return false;
                }

                var buffer = new ArrayBufferWriter<byte>();
                using (var writer = new Utf8JsonWriter(buffer))
                {
                    WriteCanonicalJson(writer, templateEl);
                    writer.Flush();
                }

                canonical = Encoding.UTF8.GetString(buffer.WrittenSpan);
                error = null;
                return true;
            }
            catch (JsonException ex)
            {
                error = new CommandError($"Invalid function-template snapshot JSON: {ex.Message}");
                return false;
            }
        }

        private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    var properties = element.EnumerateObject()
                        .Where(p => !ShouldSkipCanonicalProperty(p.Name))
                        .OrderBy(p => p.Name, StringComparer.Ordinal)
                        .ToList();
                    foreach (var property in properties)
                    {
                        writer.WritePropertyName(property.Name);
                        WriteCanonicalJson(writer, property.Value);
                    }
                    writer.WriteEndObject();
                    break;

                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    foreach (var item in element.EnumerateArray())
                        WriteCanonicalJson(writer, item);
                    writer.WriteEndArray();
                    break;

                default:
                    element.WriteTo(writer);
                    break;
            }
        }

        private static bool ShouldSkipCanonicalProperty(string propertyName)
        {
            return propertyName is "Id"
                or "X"
                or "Y"
                or "Width"
                or "Height"
                or "Location";
        }

        private sealed class DesignTimeModule : IModule
        {
            public string? Name { get; set; }

            public DesignTimeModule(string? name) => Name = name;

            public bool RuntimeValidation(ref string? error)
            {
                error = null;
                return true;
            }
        }

        private void OnBufferPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(CanUndo) or nameof(CanRedo) or nameof(ChangeCount))
                PropertyChanged?.Invoke(this, e);
        }

        public void Dispose()
        {
            _session.ModelSystemSessionDecrementing(this, ref _References);
        }

        internal ModuleRepository GetModuleRepository()
        {
            return _session.GetModuleRepository();
        }

        /// <summary>
        /// A live, observable read-only view of all module types registered in the runtime.
        /// GUI components can bind to this to populate type-picker lists.
        /// </summary>
        public System.Collections.ObjectModel.ReadOnlyObservableCollection<Type> LoadedModuleTypes
            => GetModuleRepository().LoadedModuleTypes;

        /// <summary>
        /// Snapshot of open-generic module types registered in the runtime (e.g.
        /// <c>BasicParameter&lt;&gt;</c>).  Use <see cref="GetCompatibleModuleTypes"/> to
        /// obtain the full set of types (closed + constructed) that satisfy a specific hook.
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<Type> OpenGenericModuleTypes
            => GetModuleRepository().OpenGenericModuleTypes;

        /// <summary>
        /// All types exported from every assembly loaded into this runtime, including
        /// non-IModule types.  Use this as the candidate pool when the user needs to pick
        /// a context type for <c>IAction&lt;Context&gt;</c> or
        /// <c>IFunction&lt;Context, ReturnType&gt;</c>.
        /// </summary>
        public System.Collections.ObjectModel.ReadOnlyObservableCollection<Type> AllAvailableTypes
            => _session.GetTypeRepository().Store;

        /// <summary>
        /// Returns every module type that is compatible with <paramref name="hookType"/>:
        /// closed types already in <see cref="LoadedModuleTypes"/> that are directly assignable,
        /// plus any closed generics that can be constructed from open-generic module types.
        /// </summary>
        public System.Collections.Generic.IEnumerable<Type> GetCompatibleModuleTypes(Type hookType)
        {
            var repo = GetModuleRepository();
            return repo.LoadedModuleTypes
                       .Where(t => hookType.IsAssignableFrom(t))
                       .Concat(repo.GetCompatibleConstructedTypes(hookType));
        }

        /// <summary>
        /// Change the type of an existing node, with full undo/redo support.
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="node">The node whose type should change.</param>
        /// <param name="type">The new module type.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeded, false with a message otherwise.</returns>
        public bool SetNodeType(User user, Node node, Type type, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(node);
            ArgumentNullException.ThrowIfNull(type);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var previousType = node.Type;
                string? err = null;
                if (node.SetType(GetModuleRepository(), type, ref err))
                {
                    Buffer.AddUndo(new Command(() =>
                    {
                        string? e = null;
                        _ = node.SetType(GetModuleRepository(), previousType, ref e);
                        return (true, e is null ? null : new CommandError(e));
                    }, () =>
                    {
                        string? e = null;
                        _ = node.SetType(GetModuleRepository(), type, ref e);
                        return (true, e is null ? null : new CommandError(e));
                    }));
                    error = null;
                    return true;
                }
                error = new CommandError(err ?? "Failed to set the node type.");
                return false;
            }
        }

        /// <summary>
        /// Set the name of a given boundary.
        /// </summary>
        /// <param name="user">The user issuing the action.</param>
        /// <param name="boundary">The boundary to change.</param>
        /// <param name="name">The new name to assign to the boundary, must be unique.</param>
        /// <param name="error">An error message if the operation fails with the reason why.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message stored in error.</returns>
        public bool SetBoundaryName(User user, Boundary boundary, string name, [NotNullWhen(false)] out CommandError? error)
        {
            error = null;
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(boundary);

            if (String.IsNullOrWhiteSpace(name))
            {
                error = new CommandError("A boundary requires a unique name");
                return false;
            }
            if (!_session.HasAccess(user))
            {
                error = new CommandError("The user does not have access to this project.", true);
                return false;
            }
            lock (_sessionLock)
            {
                var oldName = boundary.Name;
                if (boundary.SetName(name, out error))
                {
                    Buffer.AddUndo(new Command(() =>
                    {
                        return (boundary.SetName(oldName, out var e), e);
                    }, () =>
                    {
                        return (boundary.SetName(name, out var e), e);
                    }));
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Sets the description for the given boundary.
        /// </summary>
        /// <param name="user">The user issuing the action.</param>
        /// <param name="boundary">The boundary to change.</param>
        /// <param name="description">The new description to assign to the boundary.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message stored in error.</returns>
        public bool SetBoundaryDescription(User user, Boundary boundary, string description, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(boundary);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var oldDescription = boundary.Description;
                if (boundary.SetDescription(description, out error))
                {
                    Buffer.AddUndo(new Command(() =>
                    {
                        return (boundary.SetDescription(oldDescription, out var e), e);
                    }, () =>
                    {
                        return (boundary.SetDescription(description, out var e), e);
                    }));
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Add a new boundary to a parent boundary
        /// </summary>
        /// <param name="user">The user requesting the action</param>
        /// <param name="parentBoundary">The boundary that will gain the child</param>
        /// <param name="name">The name of the new boundary</param>
        /// <param name="boundary">The resulting boundary</param>
        /// <param name="error">An error message if the operation fails</param>
        /// <returns>True if the operation works, false otherwise with an error message.</returns>
        public bool AddBoundary(User user, Boundary parentBoundary, string name, out Boundary? boundary, [NotNullWhen(false)] out CommandError? error)
        {
            boundary = null;
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(parentBoundary);

            if (String.IsNullOrWhiteSpace(name))
            {
                error = new CommandError("A boundary requires a unique name");
                return false;
            }
            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                if (parentBoundary.AddBoundary(name, out boundary, out error))
                {
                    var _b = boundary!;
                    Buffer.AddUndo(new Command(() =>
                    {
                        return (parentBoundary.RemoveBoundary(_b, out var e), e);
                    }, () =>
                    {
                        return (parentBoundary.AddBoundary(_b, out var e), e);
                    }));
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Add a new comment block to a boundary at the given location.
        /// </summary>
        /// <param name="localUser">The user requesting this operation.</param>
        /// <param name="location">The location to send the request to.</param>
        /// <param name="block">The newly generated comment block, null if the operation fails.</param>
        /// <param name="error">The error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false with an error message otherwise.</returns>
        public bool AddCommentBlock(User user, Boundary boundary, string comment, Rectangle location, out CommentBlock? block,
            [NotNullWhen(false)] out CommandError? error)
        {
            block = null;
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(boundary);

            comment ??= string.Empty;
            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                if (boundary.AddCommentBlock(comment, location, out block, out error))
                {
                    var _block = block!;
                    Buffer.AddUndo(new Command(() =>
                    {
                        return (boundary.RemoveCommentBlock(_block, out var e), e);
                    }, () =>
                    {
                        return (boundary.AddCommentBlock(_block, out var e), e);
                    }));
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Add a new reference to the model system session
        /// </summary>
        internal void AddReference()
        {
            Interlocked.Increment(ref _References);
        }

        /// <summary>
        /// Remove the comment block from the given boundary.
        /// </summary>
        /// <param name="user">The user requesting the action.</param>
        /// <param name="boundary">The containing boundary</param>
        /// <param name="block">The comment block to remove.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false with an error message otherwise.</returns>
        public bool RemoveCommentBlock(User user, Boundary boundary, CommentBlock block, [NotNullWhen(false)]out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(boundary);
            ArgumentNullException.ThrowIfNull(block);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                if (boundary.RemoveCommentBlock(block, out error))
                {
                    Buffer.AddUndo(new Command(() =>
                    {
                        return (boundary.AddCommentBlock(block, out var e), e);
                    }, () =>
                    {
                        return (boundary.RemoveCommentBlock(block, out var e), e);
                    }));
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Sets the location of the CommentBlock
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="commentBlock">The comment block to change.</param>
        /// <param name="newLocation">The location to set the comment block to.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        public bool SetCommentBlockLocation(User user, CommentBlock commentBlock, Rectangle newLocation, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(commentBlock);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var oldLocation = commentBlock.Location;
                commentBlock.Location = newLocation;
                Buffer.AddUndo(new Command(() =>
                {
                    commentBlock.Location = oldLocation;
                    return (true, null);
                }, () =>
                {
                    commentBlock.Location = newLocation;
                    return (true, null);
                }));
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Set the comment text within a comment block
        /// </summary>
        /// <param name="user">The using issuing the command.</param>
        /// <param name="commentBlock">The comment block to edit.</param>
        /// <param name="newText">The new text to set.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        public bool SetCommentBlockText(User user, CommentBlock commentBlock, string newText, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(commentBlock);

            newText ??= string.Empty;
            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var oldComment = commentBlock.Comment;
                commentBlock.Comment = newText;
                Buffer.AddUndo(new Command(() =>
                {
                    commentBlock.Comment = oldComment;
                    return (true, null);
                }, () =>
                {
                    commentBlock.Comment = newText;
                    return (true, null);
                }));
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Set the header text of a comment block, supporting undo/redo.
        /// An empty header is allowed (it simply removes the header display).
        /// </summary>
        public bool SetCommentBlockHeader(User user, CommentBlock commentBlock, string newHeader, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(commentBlock);
            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var oldHeader = commentBlock.Header;
                commentBlock.Header = newHeader;
                Buffer.AddUndo(new Command(() =>
                {
                    commentBlock.Header = oldHeader;
                    return (true, null);
                }, () =>
                {
                    commentBlock.Header = newHeader;
                    return (true, null);
                }));
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Remove a boundary from a model system
        /// </summary>
        /// <param name="user">The user that removed the boundary</param>
        /// <param name="parentBoundary">The parent boundary of this one to remove</param>
        /// <param name="boundary">The boundary to remove</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false with an error message otherwise.</returns>
        public bool RemoveBoundary(User user, Boundary parentBoundary, Boundary boundary, [NotNullWhen(false)]out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(parentBoundary);
            ArgumentNullException.ThrowIfNull(boundary);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var linksGoingToRemovedBoundary = ModelSystem.GlobalBoundary.GetLinksGoingToBoundary(boundary);
                if (parentBoundary.RemoveBoundary(boundary, out error))
                {
                    var multiLinkHelper = new Dictionary<MultiLink, List<(int Index, Node MSS)>>();
                    foreach (var link in linksGoingToRemovedBoundary)
                    {
                        if (link is MultiLink ml)
                        {
                            var list = new List<(int Index, Node MSS)>();
                            var dests = ml.Destinations;
                            for (int i = 0; i < dests.Count; i++)
                            {
                                if (dests[i].ContainedWithin == boundary)
                                {
                                    list.Add((i, dests[i]));
                                }
                            }
                            multiLinkHelper[ml] = list;
                        }
                    }
                    bool RemoveLinks([NotNullWhen(false)] out CommandError? error2)
                    {
                        foreach (var link in linksGoingToRemovedBoundary)
                        {
                            if (link is SingleLink sl)
                            {
                                if (!link.Origin!.ContainedWithin!.RemoveLink(link, out error2))
                                {
                                    return false;
                                }
                            }
                            else if (link is MultiLink ml)
                            {
                                var dests = ml.Destinations;
                                for (int i = 0; i < dests.Count; i++)
                                {
                                    if (dests[i].ContainedWithin == boundary)
                                    {
                                        ml.RemoveDestination(i);
                                        i--;
                                    }
                                }
                            }
                        }
                        error2 = null;
                        return true;
                    }
                    if (!RemoveLinks(out error))
                    {
                        return false;
                    }
                    Buffer.AddUndo(new Command(() =>
                    {
                        if (parentBoundary.AddBoundary(boundary, out var e))
                        {
                            foreach (var link in linksGoingToRemovedBoundary)
                            {
                                if (link is SingleLink sl)
                                {
                                    link.Origin!.ContainedWithin!.AddLink(link, out e);
                                }
                                else if (link is MultiLink ml)
                                {
                                    var list = multiLinkHelper[ml];
                                    foreach (var (Index, MSS) in list)
                                    {
                                        if(!ml.AddDestination(MSS, Index, out e))
                                        {                                            
                                            return (false, e);
                                        }
                                    }
                                }
                            }
                            return (true, null);
                        }
                        return (false, e);
                    }, () =>
                    {
                        if (!RemoveLinks(out var e))
                        {
                            return (false, e);
                        }
                        return (parentBoundary.RemoveBoundary(boundary, out e), e);
                    }));
                    return true;
                }
            }
            error = new CommandError("Failed to remove the boundary.");
            return false;
        }

        /// <summary>
        /// Adds a new model system start element.
        /// </summary>
        /// <param name="user">The user requesting to add the new start node.</param>
        /// <param name="startName">The name of the start element.  This must be unique in the model system.</param>
        /// <param name="location">The location to put the start.</param>
        /// <param name="start">The newly created start node</param>
        /// <param name="error">A message describing why the start node was rejected.</param>
        /// <returns>True if the operation succeeds, false otherwise.</returns>
        public bool AddModelSystemStart(User user, Boundary boundary, string startName, Rectangle location, out Start? start, [NotNullWhen(false)]out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(boundary);

            const string badStartName = "The start name must be unique within the model system and not empty.";
            start = null;
            if (String.IsNullOrWhiteSpace(startName))
            {
                error = new CommandError(badStartName);
                return false;
            }
            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                if (!ModelSystem.Contains(boundary))
                {
                    error = new CommandError("The passed in boundary is not part of the model system!");
                    return false;
                }
                var success = boundary.AddStart(this, startName, location, out start, out error);
                if (success)
                {
                    Start _start = start!;
                    Buffer.AddUndo(new Command(() =>
                    {
                        return (boundary.RemoveStart(_start, out var e), e);
                    }, () =>
                    {
                        return (boundary.AddStart(startName, _start, out var e), e);
                    }));
                }
                return success;
            }
        }

        /// <summary>
        /// Remove the given start from the given boundary.
        /// </summary>
        /// <param name="user"></param>
        /// <param name="start"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        public bool RemoveStart(User user, Start start, [NotNullWhen(false)]out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(start);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var boundary = start.ContainedWithin!;
                if (boundary.RemoveStart(start, out error))
                {
                    Buffer.AddUndo(new Command(() =>
                    {
                        return (boundary.AddStart(start, out var e), e);
                    }, () =>
                    {
                        return (boundary.RemoveStart(start, out var e), e);
                    }));
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Creates a new node in the given boundary.
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="boundary">The boundary that the new node will be created in.</param>
        /// <param name="name">The name to give to the new node.</param>
        /// <param name="type">The type of module to assign to the new node.</param>
        /// <param name="location">The location to create the new node.</param>
        /// <param name="node">The resulting node if the operation succeeds, null if the operation fails.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message stored in error.</returns>
        public bool AddNode(User user, Boundary boundary, string name, Type type, Rectangle location, out Node? node, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(boundary);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    node = null;
                    return false;
                }
                if (boundary.AddNode(GetModuleRepository(), name, type, location, out node, out error))
                {
                    Node _node = node!;
                    Buffer.AddUndo(new Command(() =>
                    {
                        return (boundary.RemoveNode(_node, out var e), e);
                    }, () =>
                    {
                        return (boundary.AddNode(_node, out var e), e);
                    }));
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Add a node to the boundary in addition to generating all of the parameters as BasicParameters with their default values.
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="boundary">The boundary to add the module to.</param>
        /// <param name="name">The name of the node to add.</param>
        /// <param name="type">The type of the module to use.</param>
        /// <param name="node">The resulting node object.</param>
        /// <param name="children"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        public bool AddNodeGenerateParameters(User user, Boundary boundary, string name, Type type,
            Rectangle location, out Node? node, out List<Node>? children, [NotNullWhen(false)]out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(boundary);

            children = null;
            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    node = null;
                    return false;
                }
                bool success = boundary.AddNode(GetModuleRepository(), name, type, location, out node, out error);
                if (success)
                {
                    // now generate the children
                    List<Link> links;
                    (children, links) = GetChidren(node!, boundary);
                    var localChildren = children;
                    void Add()
                    {
                        CommandError? e = null;
                        foreach (var child in localChildren)
                        {
                            boundary.AddNode(child, out e);
                        }
                        foreach (var link in links)
                        {
                            boundary.AddLink(link, out e);
                        }
                    }
                    void Remove()
                    {
                        CommandError? e = null;
                        foreach (var link in links)
                        {
                            boundary.RemoveLink(link, out e);
                        }
                        foreach (var child in localChildren!)
                        {
                            boundary.RemoveNode(child, out e);
                        }
                    }
                    Add();
                    Node _node = node!;
                    Buffer.AddUndo(new Command(() =>
                    {
                        Remove();
                        return (boundary.RemoveNode(_node, out var e), e);
                    }, () =>
                    {
                        if (boundary.AddNode(_node, out var e))
                        {
                            Add();
                            return (true, null);
                        }
                        return (false, e);
                    }));
                }
                return success;
            }
        }

        private (List<Node> children, List<Link> links) GetChidren(Node baseNode, Boundary boundary)
        {
            var t = baseNode.Type;

            var nodes = new List<Node>();
            var links = new List<Link>();
            // If the type of the node is null then there are no children nor links.
            if (t is null)
            {
                return (nodes, links);
            }
            (var description, var typeinfo, var hooks) = GetModuleRepository()[t];

            foreach (var hook in hooks.Where(h => h.IsParameter))
            {
                // we can only add children for references to a generic
                var type = hook.Type;
                var genericParameters = type.GetGenericArguments();
                if (genericParameters.Length == 1)
                {
                    var funcType     = typeof(RuntimeModules.BasicParameter<>).MakeGenericType(genericParameters[0]);
                    var setableType  = typeof(RuntimeModules.SetableParameter<>).MakeGenericType(genericParameters[0]);

                    // Use SetableParameter when BasicParameter cannot satisfy the hook
                    // (e.g. the hook requires ISetableValue<T>) but SetableParameter can.
                    // Otherwise fall back to BasicParameter for plain IFunction<T> hooks.
                    Type? selectedType = null;
                    if (!type.IsAssignableFrom(funcType) && type.IsAssignableFrom(setableType))
                        selectedType = setableType;
                    else if (type.IsAssignableFrom(funcType))
                        selectedType = funcType;

                    if (selectedType is not null)
                    {
                        var child = Node.Create(this.GetModuleRepository(), hook.Name, selectedType, boundary, Rectangle.Hidden);

                        if (child?.SetParameterValue(ParameterExpression.CreateParameter(hook.DefaultValue!, genericParameters[0]), out var error) == true)
                        {
                            nodes.Add(child);
                            // Construct the link object directly without adding it to the boundary.
                            // Add() will first add child nodes (creating NodeViewModels) and THEN
                            // add links, so that TryAddLinkViewModel can resolve the destination.
                            links.Add(new SingleLink(baseNode, hook, child, false));
                        }
                    }
                }
            }
            // Return null for now so it doesn't pass tests
            return (nodes, links);
        }

        /// <summary>
        /// Remove the given node.
        /// All outgoing links (where this node is the origin) and all incoming links (where
        /// this node is a destination) are also removed, keeping the model consistent.
        /// The removal — including all affected links — is registered as a single undoable command.
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="node">The node to be removed.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        /// <summary>
        /// Add a ghost node — a visual alias for <paramref name="referencedNode"/> —
        /// to <paramref name="boundary"/> at <paramref name="location"/>.
        /// Ghost nodes mirror their referenced node's name, have no hooks, and are
        /// rendered with a dashed outline.  When the real node is deleted all ghost
        /// nodes referencing it are automatically deleted as well.
        /// </summary>
        public bool AddGhostNode(User user, Boundary boundary, Node referencedNode, Rectangle location,
            [NotNullWhen(true)] out GhostNode? ghostNode,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(boundary);
            ArgumentNullException.ThrowIfNull(referencedNode);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    ghostNode = null;
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }

                var ghost = new GhostNode(referencedNode, boundary, location);
                if (!boundary.AddGhostNode(ghost, out error))
                {
                    ghostNode = null;
                    return false;
                }
                ghostNode = ghost;

                Buffer.AddUndo(new Command(() =>
                {
                    return (boundary.RemoveGhostNode(ghost, out var e), e);
                }, () =>
                {
                    return (boundary.AddGhostNode(ghost, out var e), e);
                }));
                return true;
            }
        }

        /// <summary>
        /// Remove a ghost node from its boundary, also removing any incoming links.
        /// </summary>
        public bool RemoveGhostNode(User user, GhostNode ghostNode,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(ghostNode);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }

                var boundary = ghostNode.ContainedWithin!;
                var incomingLinks = GetLinksGoingTo(ghostNode);
                var multiLinkInfo = BuildMultiLinkRestoreInfo(incomingLinks, ghostNode);

                RemoveIncomingLinks(incomingLinks, ghostNode, multiLinkInfo);

                if (boundary.RemoveGhostNode(ghostNode, out error))
                {
                    Buffer.AddUndo(new Command(() =>
                    {
                        if (boundary.AddGhostNode(ghostNode, out var e))
                        {
                            RestoreIncomingLinks(incomingLinks, ghostNode, multiLinkInfo);
                            return (true, null);
                        }
                        return (false, e);
                    }, () =>
                    {
                        RemoveIncomingLinks(incomingLinks, ghostNode, multiLinkInfo);
                        return (boundary.RemoveGhostNode(ghostNode, out var e), e);
                    }));
                    return true;
                }
                else
                {
                    RestoreIncomingLinks(incomingLinks, ghostNode, multiLinkInfo);
                    return false;
                }
            }
        }

        /// <summary>
        /// Moves a regular node (and all of its outgoing links) from its current boundary to
        /// <paramref name="targetBoundary"/>.  The operation is undoable.
        /// </summary>
        public bool MoveNodeToBoundary(User user, Node node, Boundary targetBoundary,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(node);
            ArgumentNullException.ThrowIfNull(targetBoundary);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }

                var oldBoundary = node.ContainedWithin!;
                if (ReferenceEquals(oldBoundary, targetBoundary)) { error = null; return true; }

                // A node that lives inside a FunctionTemplate's InternalModules may only be moved
                // to other boundaries within the *same* FunctionTemplate, never outside it.
                var oldFt = FindOwningFunctionTemplate(ModelSystem.GlobalBoundary, oldBoundary);
                var newFt = FindOwningFunctionTemplate(ModelSystem.GlobalBoundary, targetBoundary);
                if (!ReferenceEquals(oldFt, newFt))
                {
                    error = new CommandError(
                        oldFt is not null
                            ? $"Node '{node.Name}' is contained within FunctionTemplate '{oldFt.Name}' and cannot be moved outside of it."
                            : $"Cannot move a node from the global scope into FunctionTemplate '{newFt!.Name}'.");
                    return false;
                }

                // Outgoing links live in the origin node's boundary and must follow the node.
                var outgoingLinks = oldBoundary.Links.Where(l => l.Origin == node).ToList();

                // Collect hidden nodes: destination nodes of outgoing links whose location is
                // Rectangle.Hidden (i.e. inlined parameter nodes).  These must travel with the node.
                var hiddenNodes = outgoingLinks
                    .SelectMany(l => l is SingleLink sl
                        ? (sl.Destination is not null ? new[] { sl.Destination } : Array.Empty<Node>())
                        : (l is MultiLink ml ? ml.Destinations.ToArray() : Array.Empty<Node>()))
                    .Where(n => n.Location.Equals(Rectangle.Hidden) && ReferenceEquals(n.ContainedWithin, oldBoundary))
                    .Distinct()
                    .ToList();

                if (!oldBoundary.RemoveNode(node, out error)) return false;

                foreach (var link in outgoingLinks)
                    oldBoundary.RemoveLink(link, out _);

                foreach (var hidden in hiddenNodes)
                    oldBoundary.RemoveNode(hidden, out _);

                node.UpdateContainedWithin(targetBoundary);
                foreach (var hidden in hiddenNodes)
                    hidden.UpdateContainedWithin(targetBoundary);

                if (!targetBoundary.AddNode(node, out error))
                {
                    // Roll back hidden nodes and links before returning.
                    foreach (var hidden in hiddenNodes)
                    {
                        hidden.UpdateContainedWithin(oldBoundary);
                        oldBoundary.AddNode(hidden, out _);
                    }
                    node.UpdateContainedWithin(oldBoundary);
                    oldBoundary.AddNode(node, out _);
                    foreach (var link in outgoingLinks)
                        oldBoundary.AddLink(link, out _);
                    return false;
                }

                foreach (var hidden in hiddenNodes)
                    targetBoundary.AddNode(hidden, out _);

                foreach (var link in outgoingLinks)
                    targetBoundary.AddLink(link, out _);

                Buffer.AddUndo(new Command(() =>
                {
                    foreach (var link in outgoingLinks) targetBoundary.RemoveLink(link, out _);
                    foreach (var hidden in hiddenNodes)
                    {
                        targetBoundary.RemoveNode(hidden, out _);
                        hidden.UpdateContainedWithin(oldBoundary);
                        oldBoundary.AddNode(hidden, out _);
                    }
                    targetBoundary.RemoveNode(node, out _);
                    node.UpdateContainedWithin(oldBoundary);
                    oldBoundary.AddNode(node, out _);
                    foreach (var link in outgoingLinks) oldBoundary.AddLink(link, out _);
                    return (true, null);
                }, () =>
                {
                    foreach (var link in outgoingLinks) oldBoundary.RemoveLink(link, out _);
                    foreach (var hidden in hiddenNodes)
                    {
                        oldBoundary.RemoveNode(hidden, out _);
                        hidden.UpdateContainedWithin(targetBoundary);
                    }
                    oldBoundary.RemoveNode(node, out _);
                    node.UpdateContainedWithin(targetBoundary);
                    targetBoundary.AddNode(node, out _);
                    foreach (var hidden in hiddenNodes) targetBoundary.AddNode(hidden, out _);
                    foreach (var link in outgoingLinks) targetBoundary.AddLink(link, out _);
                    return (true, null);
                }));

                error = null;
                return true;
            }
        }

        /// <summary>
        /// Moves a ghost node from its current boundary to <paramref name="targetBoundary"/>.
        /// The operation is undoable.
        /// </summary>
        public bool MoveGhostNodeToBoundary(User user, GhostNode ghostNode, Boundary targetBoundary,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(ghostNode);
            ArgumentNullException.ThrowIfNull(targetBoundary);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }

                var oldBoundary = ghostNode.ContainedWithin!;
                if (ReferenceEquals(oldBoundary, targetBoundary)) { error = null; return true; }

                // Ghost nodes are subject to the same FunctionTemplate scope restriction as nodes.
                var oldFt = FindOwningFunctionTemplate(ModelSystem.GlobalBoundary, oldBoundary);
                var newFt = FindOwningFunctionTemplate(ModelSystem.GlobalBoundary, targetBoundary);
                if (!ReferenceEquals(oldFt, newFt))
                {
                    error = new CommandError(
                        oldFt is not null
                            ? $"Ghost node '{ghostNode.Name}' is contained within FunctionTemplate '{oldFt.Name}' and cannot be moved outside of it."
                            : $"Cannot move a ghost node from the global scope into FunctionTemplate '{newFt!.Name}'.");
                    return false;
                }

                if (!oldBoundary.RemoveGhostNode(ghostNode, out error)) return false;

                ghostNode.UpdateContainedWithin(targetBoundary);

                if (!targetBoundary.AddGhostNode(ghostNode, out error))
                {
                    ghostNode.UpdateContainedWithin(oldBoundary);
                    oldBoundary.AddGhostNode(ghostNode, out _);
                    return false;
                }

                Buffer.AddUndo(new Command(() =>
                {
                    targetBoundary.RemoveGhostNode(ghostNode, out _);
                    ghostNode.UpdateContainedWithin(oldBoundary);
                    oldBoundary.AddGhostNode(ghostNode, out _);
                    return (true, null);
                }, () =>
                {
                    oldBoundary.RemoveGhostNode(ghostNode, out _);
                    ghostNode.UpdateContainedWithin(targetBoundary);
                    targetBoundary.AddGhostNode(ghostNode, out _);
                    return (true, null);
                }));

                error = null;
                return true;
            }
        }

        public bool RemoveNode(User user, Node node, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(node);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var boundary = node.ContainedWithin!;

                // Collect all outgoing links (this node is the origin; stored in its own boundary).
                var outgoingLinks = boundary.Links.Where(l => l.Origin == node).ToList();

                // Collect all links that point TO this node from any boundary.
                var incomingLinks = GetLinksGoingTo(node);

                // For multi-links in the incoming set, record the exact destination indices
                // that refer to 'node' so they can be faithfully restored on undo.
                var multiLinkRestoreInfo = BuildMultiLinkRestoreInfo(incomingLinks, node);

                // Cascade: collect all ghost nodes referencing this node, plus their links.
                var ghostsOfNode = GetAllGhostNodesOf(node);
                // Per ghost: (ghost, incomingLinksToGhost, multiLinkRestoreInfo for ghost)
                var ghostCascadeData = ghostsOfNode
                    .Select(g =>
                    {
                        var gLinks = GetLinksGoingTo(g);
                        return (Ghost: g, Links: gLinks, MultiInfo: BuildMultiLinkRestoreInfo(gLinks, g));
                    })
                    .ToList();

                // Collect hidden (embedded) nodes: destination nodes of outgoing links that
                // reside in the same boundary and carry a Rectangle.Hidden location.
                // These are visually embedded within the owning node and must be deleted with it.
                var hiddenNodes = outgoingLinks
                    .SelectMany<Link, Node>(l =>
                        l is SingleLink sl && sl.Destination is not null ? new[] { sl.Destination }
                        : l is MultiLink ml ? ml.Destinations.ToArray()
                        : Array.Empty<Node>())
                    .Where(n => n.Location.Equals(Rectangle.Hidden) && ReferenceEquals(n.ContainedWithin, boundary))
                    .Distinct()
                    .ToList();

                // For each hidden node, capture any OTHER incoming links (not the owner→hidden
                // links already in outgoingLinks) plus its ghost cascade, for clean undo/redo.
                var hiddenCascadeData = hiddenNodes
                    .Select(hn =>
                    {
                        var hnLinks     = GetLinksGoingTo(hn).Where(l => !outgoingLinks.Contains(l)).ToList();
                        var hnMulti     = BuildMultiLinkRestoreInfo(hnLinks, hn);
                        var hnGhosts    = GetAllGhostNodesOf(hn);
                        var hnGhostData = hnGhosts.Select(g =>
                        {
                            var gLinks = GetLinksGoingTo(g);
                            return (Ghost: g, Links: gLinks, MultiInfo: BuildMultiLinkRestoreInfo(gLinks, g));
                        }).ToList();
                        return (Node: hn, OtherIncoming: hnLinks, MultiInfo: hnMulti, GhostData: hnGhostData);
                    })
                    .ToList();

                // Remove all incoming links (or just the relevant destination entries).
                void RemoveIncoming()
                {
                    RemoveIncomingLinks(incomingLinks, node, multiLinkRestoreInfo);
                }

                // Restore all incoming links (inverse of RemoveIncoming).
                void RestoreIncoming()
                {
                    RestoreIncomingLinks(incomingLinks, node, multiLinkRestoreInfo);
                }

                void RemoveGhostCascade()
                {
                    foreach (var (ghost, gLinks, gMultiInfo) in ghostCascadeData)
                    {
                        RemoveIncomingLinks(gLinks, ghost, gMultiInfo);
                        ghost.ContainedWithin!.RemoveGhostNode(ghost, out _);
                    }
                }

                void RestoreGhostCascade()
                {
                    foreach (var (ghost, gLinks, gMultiInfo) in ghostCascadeData)
                    {
                        ghost.ContainedWithin!.AddGhostNode(ghost, out _);
                        RestoreIncomingLinks(gLinks, ghost, gMultiInfo);
                    }
                }

                void RemoveHiddenCascade()
                {
                    foreach (var (hn, hnLinks, hnMulti, hnGhostData) in hiddenCascadeData)
                    {
                        foreach (var (ghost, gLinks, gMulti) in hnGhostData)
                        {
                            RemoveIncomingLinks(gLinks, ghost, gMulti);
                            ghost.ContainedWithin!.RemoveGhostNode(ghost, out _);
                        }
                        RemoveIncomingLinks(hnLinks, hn, hnMulti);
                        boundary.RemoveNode(hn, out _);
                    }
                }

                void RestoreHiddenCascade()
                {
                    foreach (var (hn, hnLinks, hnMulti, hnGhostData) in hiddenCascadeData)
                    {
                        boundary.AddNode(hn, out _);
                        RestoreIncomingLinks(hnLinks, hn, hnMulti);
                        foreach (var (ghost, gLinks, gMulti) in hnGhostData)
                        {
                            ghost.ContainedWithin!.AddGhostNode(ghost, out _);
                            RestoreIncomingLinks(gLinks, ghost, gMulti);
                        }
                    }
                }

                // Remove incoming links, then ghost cascade, then outgoing links,
                // then the hidden embedded nodes, then the node itself.
                RemoveIncoming();
                RemoveGhostCascade();
                foreach (var link in outgoingLinks)
                    boundary.RemoveLink(link, out _);
                RemoveHiddenCascade();

                // Also remove from model system variables if present, capturing position for undo.
                var variableIndex = ModelSystem.Variables.IndexOf(node);
                if (variableIndex >= 0)
                    ModelSystem.Variables.RemoveAt(variableIndex);

                if (boundary.RemoveNode(node, out error))
                {
                    Buffer.AddUndo(new Command(() =>
                    {
                        // Undo: restore node first, then hidden nodes, then outgoing links, then incoming links + ghosts.
                        if (boundary.AddNode(node, out var e))
                        {
                            RestoreHiddenCascade();
                            foreach (var link in outgoingLinks)
                                boundary.AddLink(link, out e);
                            RestoreIncoming();
                            RestoreGhostCascade();
                            if (variableIndex >= 0)
                            {
                                var restoreIdx = Math.Min(variableIndex, ModelSystem.Variables.Count);
                                ModelSystem.Variables.Insert(restoreIdx, node);
                            }
                            return (true, null);
                        }
                        return (false, e);
                    }, () =>
                    {
                        // Redo: same sequence as the original removal.
                        RemoveIncoming();
                        RemoveGhostCascade();
                        foreach (var link in outgoingLinks)
                            boundary.RemoveLink(link, out _);
                        RemoveHiddenCascade();
                        ModelSystem.Variables.Remove(node);
                        return (boundary.RemoveNode(node, out var e), e);
                    }));
                    return true;
                }
                else
                {
                    // Node removal failed; roll back the link removals, hidden cascade, and ghost cascade.
                    RestoreHiddenCascade();
                    foreach (var link in outgoingLinks)
                        boundary.AddLink(link, out _);
                    RestoreGhostCascade();
                    RestoreIncoming();
                    if (variableIndex >= 0)
                    {
                        var restoreIdx = Math.Min(variableIndex, ModelSystem.Variables.Count);
                        ModelSystem.Variables.Insert(restoreIdx, node);
                    }
                    return false;
                }
            }
        }

        /// <summary>
        /// Remove the given node and all of its generic parameters.
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="node">The node to remove.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, False with an error message otherwise.</returns>
        public bool RemoveNodeGenerateParameters(User user, Node node, [NotNullWhen(false)]out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(node);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var boundary = node.ContainedWithin!;
                // Find all of the basic parameters for the node that we need to remove.
                var basicParameters = new List<(Node basicParameterNode, SingleLink basicParameterLink)>();
                var advancedParameters = new List<Link>();
                foreach (var link in boundary.Links.Where(link => link.Origin == node))
                {
                    if (link.OriginHook!.IsParameter && link is SingleLink singleLink)
                    {
                        var destNode = singleLink.Destination!;
                        var destType = destNode.Type;
                        if (destType != null && destType.IsGenericType &&
                            (destType.GetGenericTypeDefinition() == typeof(RuntimeModules.BasicParameter<>) ||
                             destType.GetGenericTypeDefinition() == typeof(RuntimeModules.SetableParameter<>)))
                        {
                            // check to see if this would be the only link referencing it.
                            List<Link> linksGoingTo = GetLinksGoingTo(destNode);
                            if (linksGoingTo?.Count == 1)
                            {
                                basicParameters.Add((destNode, singleLink));
                            }
                            else
                            {
                                advancedParameters.Add(singleLink);
                            }
                        }
                    }
                }
                if (boundary.RemoveNode(node, out error))
                {
                    void RemoveParameters()
                    {
                        CommandError? e;
                        foreach (var p in advancedParameters)
                        {
                            boundary.RemoveLink(p, out e);
                        }
                        foreach (var (basicParameterNode, basicParameterLink) in basicParameters)
                        {
                            boundary.RemoveLink(basicParameterLink, out e);
                            boundary.RemoveNode(basicParameterNode, out e);
                        }
                    }
                    void AddParameters()
                    {
                        CommandError? e;
                        if (boundary is object)
                        {
                            foreach (var (basicParameterNode, basicParameterLink) in basicParameters!)
                            {
                                boundary.AddNode(basicParameterNode, out e);
                                boundary.AddLink(basicParameterLink, out e);
                            }
                            foreach (var p in advancedParameters!)
                            {
                                boundary.AddLink(p, out e);
                            }
                        }
                    }
                    RemoveParameters();
                    Buffer.AddUndo(new Command(() =>
                    {
                        if (boundary.AddNode(node, out var e))
                        {
                            AddParameters();
                            return (true, null);
                        }
                        return (false, e);
                    }, () =>
                    {
                        if (boundary.RemoveNode(node, out var e))
                        {
                            RemoveParameters();
                            return (true, null);
                        }
                        return (false, e);
                    }));
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Renames the given node (or start) within the model system, with undo support.
        /// </summary>
        /// <param name="user">The user issuing the action.</param>
        /// <param name="node">The node (or start) to rename.</param>
        /// <param name="name">The new name to assign.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise.</returns>
        public bool SetNodeName(User user, Node node, string name, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(node);

            if (string.IsNullOrWhiteSpace(name))
            {
                error = new CommandError("A node name must not be empty or whitespace.");
                return false;
            }

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }

                var oldName = node.Name;
                // Collect affected scripted parameters BEFORE the rename so we know the old name.
                var affected = CollectScriptedParamsReferencingVariable(node, oldName);
                var savedOldExprs = affected.Select(n => n.ParameterValue).ToList();

                if (node.SetName(name, out error))
                {
                    // Recompile every affected scripted parameter with the updated expression text.
                    var savedNewExprs = new List<ParameterExpression?>(affected.Count);
                    foreach (var paramNode in affected)
                    {
                        var oldText = paramNode.ParameterValue!.Representation;
                        var newText = ReplaceVariableNameInExpression(oldText, oldName, name);
                        var localVars = paramNode.ContainedWithin?.OwningFunctionTemplate?.LocalVariables;
                        IList<Node> allVars = localVars is { Count: > 0 }
                            ? localVars.Concat(ModelSystem.Variables).ToList()
                            : (IList<Node>)ModelSystem.Variables;
                        paramNode.SetParameterExpression(allVars, newText, out _);
                        savedNewExprs.Add(paramNode.ParameterValue);
                    }

                    Buffer.AddUndo(new Command(() =>
                    {
                        node.SetName(oldName, out _);
                        for (int i = 0; i < affected.Count; i++)
                            affected[i].SetParameterValue(savedOldExprs[i], out _);
                        return (true, null);
                    }, () =>
                    {
                        node.SetName(name, out _);
                        for (int i = 0; i < affected.Count; i++)
                            affected[i].SetParameterValue(savedNewExprs[i], out _);
                        return (true, null);
                    }));
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Returns all nodes whose <see cref="ParameterExpression"/> is a
        /// <see cref="ScriptedParameter"/> that textually references <paramref name="oldName"/>
        /// as a variable token, scoped to the visibility of <paramref name="variableNode"/>:
        /// the entire model system for global model-system variables, or only the owning
        /// <see cref="FunctionTemplate"/>'s internal boundary for local variables.
        /// </summary>
        private List<Node> CollectScriptedParamsReferencingVariable(Node variableNode, string oldName)
        {
            bool isGlobalVar = ModelSystem.Variables.Contains(variableNode);
            FunctionTemplate? ownerTemplate = null;

            if (!isGlobalVar)
            {
                // Check whether the node is a local variable of any FunctionTemplate.
                var stack = new Stack<Boundary>();
                stack.Push(ModelSystem.GlobalBoundary);
                while (stack.Count > 0 && ownerTemplate is null)
                {
                    var current = stack.Pop();
                    foreach (var child in current.Boundaries) stack.Push(child);
                    foreach (var ft in current.FunctionTemplates)
                    {
                        if (ft.LocalVariables.Contains(variableNode))
                        {
                            ownerTemplate = ft;
                            break;
                        }
                        stack.Push(ft.InternalModules);
                    }
                }
            }

            // If the node is neither a global variable nor a local variable, nothing to update.
            if (!isGlobalVar && ownerTemplate is null)
                return [];

            var result = new List<Node>();
            var searchRoot = ownerTemplate is null
                ? ModelSystem.GlobalBoundary
                : ownerTemplate.InternalModules;

            // For global variable renaming we skip any FunctionTemplate whose LocalVariables
            // already contain a node with the same old name (shadowing the global).
            CollectScriptedParamsInBoundary(
                searchRoot, oldName, variableNode, isGlobalVar, result);
            return result;
        }

        /// <summary>
        /// Recursively collects nodes with scripted parameters that reference
        /// <paramref name="oldName"/> as a token inside <paramref name="boundary"/>.
        /// When <paramref name="guardShadowing"/> is <see langword="true"/> (global-variable
        /// rename), FunctionTemplate sub-trees whose LocalVariables shadow <paramref name="oldName"/>
        /// are skipped.
        /// </summary>
        private static void CollectScriptedParamsInBoundary(
            Boundary boundary,
            string oldName,
            Node variableNode,
            bool guardShadowing,
            List<Node> result)
        {
            foreach (var node in boundary.Modules)
            {
                if (node.ParameterValue is ScriptedParameter sp
                    && ContainsVariableToken(sp.Representation, oldName))
                {
                    result.Add(node);
                }
            }

            foreach (var child in boundary.Boundaries)
                CollectScriptedParamsInBoundary(child, oldName, variableNode, guardShadowing, result);

            foreach (var ft in boundary.FunctionTemplates)
            {
                // If this is a global-variable rename and the template already has a local
                // variable named oldName (that is NOT the node being renamed), the local
                // variable shadows the global one inside this template — skip it.
                if (guardShadowing
                    && ft.LocalVariables.Any(lv => lv.Name == oldName && !ReferenceEquals(lv, variableNode)))
                {
                    continue;
                }
                CollectScriptedParamsInBoundary(ft.InternalModules, oldName, variableNode, guardShadowing, result);
            }
        }

        /// <summary>
        /// Returns <see langword="true"/> when <paramref name="expression"/> contains
        /// <paramref name="name"/> as a complete variable token (not a substring of a
        /// larger token and not inside a quoted string literal).
        /// </summary>
        private static bool ContainsVariableToken(string expression, string name)
            => !string.IsNullOrEmpty(name)
            && Regex.IsMatch(expression,
                BuildVariableTokenPattern(Regex.Escape(name.Trim())));

        /// <summary>
        /// Replaces all occurrences of <paramref name="oldName"/> as a complete variable
        /// token in <paramref name="expression"/> with <paramref name="newName"/>,
        /// preserving surrounding whitespace.
        /// </summary>
        internal static string ReplaceVariableNameInExpression(
            string expression, string oldName, string newName)
        {
            if (string.IsNullOrEmpty(expression) || string.IsNullOrEmpty(oldName))
                return expression;
            var pattern = BuildVariableTokenPattern(Regex.Escape(oldName.Trim()));
            return Regex.Replace(expression, pattern,
                m => m.Groups[1].Value + newName + m.Groups[2].Value);
        }

        /// <summary>
        /// Builds a regex pattern that matches the escaped variable name <paramref name="escapedName"/>
        /// (capturing surrounding optional whitespace in groups 1 and 2) only when it
        /// appears as a whole token — bounded by a special character, whitespace,
        /// or a string boundary on each side.
        /// Special characters are the same set as <c>ParameterCompiler.IsSpecialCharacter</c>:
        /// <c>? : &amp; | + - * / ^ &lt; &gt; = ! ( ) "</c>.
        /// </summary>
        private static string BuildVariableTokenPattern(string escapedName)
        {
            const string delimiters = @"[\s?:&|+\-*/^<>=!()""]";
            // Group 1: optional leading whitespace after a delimiter or string start.
            // Group 2: optional trailing whitespace before a delimiter or string end.
            return $@"(?<=\A|{delimiters})(\s*){escapedName}(\s*)(?=\z|{delimiters})";
        }

        public bool SetNodeLocation(User user, Node mss, Rectangle newLocation, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(mss);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var oldLocation = mss.Location;
                mss.SetLocation(newLocation);
                Buffer.AddUndo(new Command(() =>
                {
                    mss.SetLocation(oldLocation);
                    return (true, null);
                }, () =>
                {
                    mss.SetLocation(newLocation);
                    return (true, null);
                }));
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Walks the entire boundary tree rooted at <paramref name="root"/> and returns the
        /// <see cref="FunctionTemplate"/> whose <see cref="FunctionTemplate.InternalModules"/>
        /// subtree contains <paramref name="boundary"/>, or <see langword="null"/> when the
        /// boundary belongs to the global (non-FunctionTemplate) scope.
        /// </summary>
        private static FunctionTemplate? FindOwningFunctionTemplate(Boundary root, Boundary boundary)
        {
            foreach (var ft in root.FunctionTemplates)
            {
                if (ReferenceEquals(ft.InternalModules, boundary) || ft.InternalModules.Contains(boundary))
                    return ft;
            }
            foreach (var child in root.Boundaries)
            {
                var result = FindOwningFunctionTemplate(child, boundary);
                if (result is not null)
                    return result;
            }
            return null;
        }

        /// <summary>
        /// Returns <c>true</c> if any <see cref="FunctionInstance"/> of <paramref name="template"/>
        /// anywhere in the model system has an active link originating at the
        /// <see cref="FunctionParameterHook"/> that corresponds to <paramref name="parameter"/>.
        /// Traverses every reachable boundary (including <see cref="FunctionTemplate.InternalModules"/>).
        /// </summary>
        private static bool HasActiveFunctionParameterLink(
            Boundary root, FunctionTemplate template, FunctionParameter parameter)
        {
            var stack = new Stack<Boundary>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                foreach (var child in current.Boundaries)
                    stack.Push(child);
                foreach (var ft in current.FunctionTemplates)
                    stack.Push(ft.InternalModules);

                foreach (var link in current.Links)
                {
                    if (link.Origin is FunctionInstance fi
                        && ReferenceEquals(fi.Template, template)
                        && link.OriginHook is FunctionParameterHook fph
                        && ReferenceEquals(fph.Parameter, parameter))
                        return true;
                }
            }
            return false;
        }

        private List<Link> GetLinksGoingTo(Node destNode)
        {
            var ret = new List<Link>();
            var destBoundary = destNode.ContainedWithin!;
            ret.AddRange(destBoundary.Links.Where(l => l.HasDestination(destNode)));
            ret.AddRange(ModelSystem.GlobalBoundary.GetLinksGoingToBoundary(destBoundary).Where(l => l.HasDestination(destNode)));
            return ret;
        }

        /// <summary>
        /// Builds a restore-info dictionary for MultiLink entries that point to
        /// <paramref name="destNode"/> in <paramref name="incomingLinks"/>.
        /// </summary>
        private static Dictionary<MultiLink, List<(int Index, Node Dest)>> BuildMultiLinkRestoreInfo(
            List<Link> incomingLinks, Node destNode)
        {
            var info = new Dictionary<MultiLink, List<(int Index, Node Dest)>>();
            foreach (var link in incomingLinks)
            {
                if (link is MultiLink ml)
                {
                    var list = new List<(int Index, Node Dest)>();
                    var dests = ml.Destinations;
                    for (int i = 0; i < dests.Count; i++)
                    {
                        if (dests[i] == destNode)
                            list.Add((i, dests[i]));
                    }
                    info[ml] = list;
                }
            }
            return info;
        }

        /// <summary>
        /// Removes incoming links that target <paramref name="destNode"/> from their
        /// respective boundaries, using pre-computed multi-link restore info.
        /// </summary>
        private static void RemoveIncomingLinks(
            List<Link> incomingLinks, Node destNode,
            Dictionary<MultiLink, List<(int Index, Node Dest)>> multiLinkInfo)
        {
            foreach (var link in incomingLinks)
            {
                if (link is SingleLink)
                {
                    link.Origin!.ContainedWithin!.RemoveLink(link, out _);
                }
                else if (link is MultiLink ml && multiLinkInfo.TryGetValue(ml, out var list))
                {
                    // Remove back-to-front so indices remain valid.
                    for (int i = list.Count - 1; i >= 0; i--)
                        ml.RemoveDestination(list[i].Index);

                    if (ml.Destinations.Count == 0)
                        ml.Origin!.ContainedWithin!.RemoveLink(ml, out _);
                }
            }
        }

        /// <summary>
        /// Restores incoming links that were previously removed by
        /// <see cref="RemoveIncomingLinks"/>.
        /// </summary>
        private static void RestoreIncomingLinks(
            List<Link> incomingLinks, Node destNode,
            Dictionary<MultiLink, List<(int Index, Node Dest)>> multiLinkInfo)
        {
            foreach (var link in incomingLinks)
            {
                if (link is SingleLink)
                {
                    link.Origin!.ContainedWithin!.AddLink(link, out _);
                }
                else if (link is MultiLink ml && multiLinkInfo.TryGetValue(ml, out var list))
                {
                    // Re-add the link object if it was fully removed.
                    if (!ml.Origin!.ContainedWithin!.Links.Contains(ml))
                        ml.Origin.ContainedWithin.AddLink(ml, out _);

                    // Re-insert destinations in original order.
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (!ml.AddDestination(list[i].Dest, list[i].Index, out _))
                            return;
                    }
                }
            }
        }

        /// <summary>
        /// Returns every <see cref="GhostNode"/> anywhere in the model system that
        /// references <paramref name="realNode"/>.
        /// </summary>
        /// <summary>
        /// Returns every <see cref="FunctionInstance"/> anywhere in the model system that
        /// references <paramref name="template"/>.
        /// </summary>
        private List<FunctionInstance> GetAllFunctionInstancesOf(FunctionTemplate template)
        {
            var result = new List<FunctionInstance>();
            var stack = new Stack<Boundary>();
            stack.Push(ModelSystem.GlobalBoundary);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                foreach (var child in current.Boundaries)
                    stack.Push(child);
                foreach (var ft in current.FunctionTemplates)
                    stack.Push(ft.InternalModules);
                foreach (var fi in current.FunctionInstances)
                    if (fi.Template == template)
                        result.Add(fi);
            }
            return result;
        }

        private List<GhostNode> GetAllGhostNodesOf(Node realNode)
        {
            var result = new List<GhostNode>();
            var stack = new Stack<Boundary>();
            stack.Push(ModelSystem.GlobalBoundary);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                foreach (var child in current.Boundaries)
                    stack.Push(child);
                // Also traverse the InternalModules of every FunctionTemplate in this
                // boundary — they are not exposed through Boundaries and would otherwise
                // be missed, leaving orphaned ghost nodes after the real node is deleted.
                foreach (var ft in current.FunctionTemplates)
                    stack.Push(ft.InternalModules);
                foreach (var ghost in current.GhostNodes)
                    if (ghost.ReferencedNode == realNode)
                        result.Add(ghost);
            }
            return result;
        }

        /// <summary>
        /// Evaluates a node's current parameter expression using the same expression engine
        /// that the runtime uses, without mutating the model system.
        /// </summary>
        /// <param name="node">The parameter-bearing node whose expression should be evaluated.</param>
        /// <param name="value">The evaluated value, or <c>null</c> when evaluation fails.</param>
        /// <returns><c>true</c> when the expression evaluates successfully.</returns>
        public bool EvaluateParameterExpression(Node node, out object? value)
        {
            ArgumentNullException.ThrowIfNull(node);
            value = null;

            lock (_sessionLock)
            {
                if (node.ParameterValue is null)
                    return false;

                var designTimeModule = new DesignTimeModule(node.Name ?? "DesignTimeModule");
                var expectedType = node.ParameterValue.Type;
                if (node.ParameterValue is ScriptedParameter scripted)
                {
                    string? error = null;
                    var localVars = node.ContainedWithin?.OwningFunctionTemplate?.LocalVariables;
                    IList<Node> allVars = localVars is { Count: > 0 }
                        ? localVars.Concat(ModelSystem.Variables).ToList()
                        : (IList<Node>)ModelSystem.Variables;
                    if (!ParameterCompiler.CreateExpression(allVars, scripted.Representation, out var expression, ref error))
                        return false;

                    if (!ParameterCompiler.Evaluate(designTimeModule, expression, out value, ref error))
                        return false;

                    return true;
                }

                string? parseError = null;
                var result = node.ParameterValue.GetValue(designTimeModule, expectedType, ref parseError);
                if (parseError is not null)
                    return false;

                value = result;
                return true;
            }
        }

        /// <summary>
        /// Set the value of a parameter
        /// </summary>
        /// <param name="user">The user issuing the command</param>
        /// <param name="basicParameter">The parameter to set.</param>
        /// <param name="value">The value to set the parameter to.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        public bool SetParameterValue(User user, Node basicParameter, string value, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(basicParameter);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                return InnerSetBasicParameter(basicParameter, value, out error);
            }
        }

        /// <summary>
        /// Set the value of a parameter without checking user access (for internal use only)
        /// <b>Must already own the session lock.</b>
        /// </summary>
        /// <param name="basicParameter">The parameter to set.</param>
        /// <param name="value">The value to set the parameter to.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns
        private bool InnerSetBasicParameter(Node basicParameter, string value, out CommandError? error)
        {
            var previousValue = basicParameter.ParameterValue;
            var newValue = ParameterExpression.CreateParameter(value, basicParameter.Type.GetGenericArguments()[0]);
            if (basicParameter.SetParameterValue(newValue, out error))
            {
                Buffer.AddUndo(new Command(() =>
                {
                    return (basicParameter.SetParameterValue(previousValue!, out var e), e);
                }, () =>
                {
                    return (basicParameter.SetParameterValue(newValue, out var e), e);
                }));
                return true;
            }
            return false;
        }



        /// <summary>
        /// Set the value of a parameter from a file path
        /// </summary>
        /// <param name="user">The user issuing the command</param>
        /// <param name="nodeToAssign">The parameter to set.</param>
        /// <param name="filePath">The file path to set the parameter to.</param
        /// <param name="isDirectory">Whether the file path is a directory or not.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns
        public bool SetParameterValueFromFilePath(User user, Node nodeToAssign, string filePath, bool isDirectory, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(nodeToAssign);
            if(isDirectory)
            {
                if (filePath is null || !Directory.Exists(filePath))
                {
                    error = new CommandError($"The directory path '{filePath}' does not exist.", false);
                    return false;
                }
            }
            else
            {
                if (filePath is null || !File.Exists(filePath))
                {
                    error = new CommandError($"The file path '{filePath}' does not exist.", false);
                    return false;
                }
            }

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }

                // Check the type of the current node:
                // InnerType == typeof(OpenReadStream), then we need to get the node referenced by the hook "File Path" and set the value of that node to the file path.
                // BasicParameter, we can set the value from the file path
                // ScriptedParaemeter, we need to explore its AST and see if we can update the right hand side addition and then only update that script's constant.
                if (nodeToAssign.Type == typeof(RuntimeModules.OpenReadStreamFromFile))
                {
                    var links = nodeToAssign.ContainedWithin?.Links;
                    var link = links?.FirstOrDefault(l => l.Origin == nodeToAssign && l.OriginHook?.Name == "File Path");
                    // link is null if there is no BasicParameter or ScriptedParameter to save the value to.
                    // TODO: resolve this by creating a new BasicParameter and linking it to the OpenReadStreamFromFile node.
                    if (link is null)
                    {
                        error = new CommandError("There is no link for the OpenReadStreamFromFile to set the file path to.", false);
                        return false;
                    }

                    if(!link.TryGetFirstDestination(out var destination))
                    {
                        error = new CommandError("The link for the OpenReadStreamFromFile does not have a valid destination.", false);
                        return false;
                    }

                    if (destination is Node destNode)
                    {
                        nodeToAssign = destNode;                        
                    }
                    else
                    {
                        error = new CommandError("Only Nodes are currently supported for storing the file path to.", false);
                        return false;
                    }
                }
                // At this point nodeToAssign should be a BasicParameter or ScriptedParameter that we can set the value of.
                if (nodeToAssign?.Type == typeof(RuntimeModules.ScriptedParameter<string>))
                {
                    var originalParameterValue = nodeToAssign.ParameterValue;

                    List<Node> availableVariables = [];
                    // Put the local variables first so they get resolved first.
                    var localVariables = nodeToAssign.ContainedWithin?.OwningFunctionTemplate?.LocalVariables;
                    if(localVariables is not null && localVariables.Count > 0)
                    {
                        availableVariables = [.. availableVariables, .. localVariables];
                    }
                    // append the higher order model system variables second so they get resolved last.
                    availableVariables = [.. availableVariables, .. this.ModelSystem.Variables];
                    string? e = null;
                    if (!ParameterCompiler.CreateExpression(availableVariables, originalParameterValue?.Representation!, out var expression, ref e))
                    {
                        error = new CommandError($"Failed to parse the ScriptedParameter expression: {e}", false);
                        return false;
                    }
                    // MAke sure it resolves to a string before we try to explore it.
                    if (expression.Type != typeof(string))
                    {
                        error = new CommandError($"The ScriptedParameter expression did not evaluate to a string.", false);
                        return false;
                    }
                    // Check if we have the pattern where the LHS are variables and the RHS is a string literal.
                    if (expression is AddOperator add)
                    {
                        var lhs = add._lhs as StringVariable;
                        var rhs = add._rhs as StringLiteral;
                        // If we have this pattern then we can try to solve it
                        if (lhs is not null && rhs is not null)
                        {
                            var lhsResult = lhs.GetResult(null!);
                            if (lhsResult.ReturnType == typeof(string))
                            {
                                if (lhsResult.TryGetResult(out var lhsr2, ref e) && lhsr2 is string lhsPath)
                                {
                                    // Check if the lhsPath is a subset of the current path
                                    if (filePath.StartsWith(lhsPath))
                                    {
                                        filePath = filePath[lhsPath.Length..];
                                        StringBuilder sb = new ();
                                        sb.Append(lhs.AsString());
                                        sb.Append(" + \"");
                                        sb.Append(filePath);
                                        sb.Append('\"');
                                        if (ParameterCompiler.CreateExpression(availableVariables, sb.ToString(), out var updatedExpression, ref e))
                                        {
                                            var newParameter = ParameterExpression.CreateParameter(updatedExpression);
                                            if (nodeToAssign.SetParameterValue(newParameter, out error))
                                            {
                                                Buffer.AddUndo(new Command(() =>
                                                {
                                                    return (nodeToAssign.SetParameterValue(originalParameterValue!, out var e), e);
                                                }, () =>
                                                {
                                                    return (nodeToAssign.SetParameterValue(newParameter, out var e), e);
                                                }));
                                                return true;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    // If we get here then we were not able to match a pattern that we can solve, so we will just assign the scriped parameter as a literal.
                    if (ParameterCompiler.CreateExpression(availableVariables, $"\"{filePath}\"", out var ue, ref e))
                    {
                        var newExpression = ParameterExpression.CreateParameter(ue);
                        if (nodeToAssign.SetParameterValue(newExpression, out error))
                        {
                            Buffer.AddUndo(new Command(() =>
                            {
                                return (nodeToAssign.SetParameterValue(originalParameterValue!, out var e), e);
                            }, () =>
                            {
                                return (nodeToAssign.SetParameterValue(newExpression, out var e), e);
                            }));
                            return true;
                        }
                        return true;
                    }
                }
                else if(nodeToAssign?.Type == typeof(RuntimeModules.BasicParameter<string>))
                {
                    return InnerSetBasicParameter(nodeToAssign, filePath, out error);
                }
                else
                {
                    error = new CommandError($"The node type '{nodeToAssign?.Type}' is not supported for setting a file path.", false);
                    return false;
                }
            }
            
            error = new CommandError("Failed to set the parameter value from file path.", false);
            return false;
        }

        /// <summary>
        /// Applies a set of optimised parameter values (as produced by an estimation or
        /// calibration run) back to the live model system, wrapped in a single undoable
        /// batch command so the entire change can be reverted in one step.
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="results">
        ///   Ordered list of (node serialisation index, new value) pairs produced by the run.
        /// </param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if all updates succeeded; false with <paramref name="error"/> on failure.</returns>
        public bool ApplyOptimizationResults(User user, IReadOnlyList<(int nodeIndex, double value)> results, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(results);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var nodesByIndex = ModelSystem.NodesByLoadIndex;
                if (nodesByIndex is null)
                {
                    error = new CommandError("The model system has not been loaded from disk; node indices are unavailable.");
                    return false;
                }
                Buffer.BeginAggregateBatch();
                foreach (var (nodeIndex, value) in results)
                {
                    if (!nodesByIndex.TryGetValue(nodeIndex, out var node))
                        continue;
                    var previousValue = node.ParameterValue;
                    var newValue = ParameterExpression.CreateParameter(
                        value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        node.Type.GetGenericArguments()[0]);
                    if (!node.SetParameterValue(newValue, out error))
                    {
                        Buffer.CommitAggregateBatch();
                        return false;
                    }
                    var capturedPrev = previousValue;
                    var capturedNew = newValue;
                    var capturedNode = node;
                    Buffer.AddUndo(new Command(() =>
                    {
                        return (capturedNode.SetParameterValue(capturedPrev!, out var e), e);
                    }, () =>
                    {
                        return (capturedNode.SetParameterValue(capturedNew, out var e), e);
                    }));
                }
                Buffer.CommitAggregateBatch();
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Returns metadata about all enabled optimisation parameters for use in the live
        /// progress dialog.  Each entry contains the node's serialisation index, its name,
        /// and its min/max bounds.
        /// </summary>
        public IReadOnlyList<(int nodeIndex, string name, double min, double max)> GetOptimizationParameterMeta(Bus.RunMode runMode)
        {
            var result = new System.Collections.Generic.List<(int, string, double, double)>();
            lock (_sessionLock)
            {
                var nodesByIndex = ModelSystem.NodesByLoadIndex;
                if (nodesByIndex is null) return result;
                var nodeToIndex = nodesByIndex.ToDictionary(kv => kv.Value, kv => kv.Key);

                if (runMode == Bus.RunMode.Estimation)
                {
                    foreach (var group in ModelSystem.EstimationGroups)
                    foreach (var entry in group.Parameters)
                    {
                        if (!entry.IsEnabled) continue;
                        if (nodeToIndex.TryGetValue(entry.Node, out var idx))
                            result.Add((idx, entry.Node.Name ?? "", entry.Min, entry.Max));
                    }
                }
                else if (runMode == Bus.RunMode.Calibration)
                {
                    foreach (var group in ModelSystem.CalibrationGroups)
                    foreach (var entry in group.Parameters)
                    {
                        if (!entry.IsEnabled || entry.ModelOutputNode is null || entry.TargetOutputNode is null) continue;
                        if (nodeToIndex.TryGetValue(entry.Node, out var idx))
                            result.Add((idx, entry.Node.Name ?? "", entry.Min, entry.Max));
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Set the value of a parameter to an expression
        /// </summary>
        /// <param name="user">The user issuing the command</param>
        /// <param name="basicParameter">The parameter to set.</param>
        /// <param name="value">The value to set the parameter to.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        public bool SetParameterExpression(User user, Node basicParameter, string expression, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(basicParameter);

            lock(_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var previousType = basicParameter.Type;
                var previousValue = basicParameter.ParameterValue;
                // Nodes inside a FunctionTemplate's InternalModules have template-local
                // variables that shadow the global model-system variables.
                var localVars = basicParameter.ContainedWithin?.OwningFunctionTemplate?.LocalVariables;
                IList<Node> allVars = localVars is { Count: > 0 }
                    ? localVars.Concat(ModelSystem.Variables).ToList()
                    : (IList<Node>)ModelSystem.Variables;
                if(basicParameter.SetParameterExpression(allVars, expression, out error))
                {
                    var newType = basicParameter.Type;
                    var newExpression = basicParameter.ParameterValue;
                    Buffer.AddUndo(new Command(()=>
                    {
                        string? error = null;
                        _ = basicParameter.SetType(GetModuleRepository(), previousType, ref error);
                        _ = basicParameter.SetParameterValue(previousValue, out var e);
                        return (true, e);
                    }, ()=>
                    {
                        string? error = null;
                        _ = basicParameter.SetType(GetModuleRepository(), newType, ref error);
                        _ = basicParameter.SetParameterValue(newExpression, out var e);
                        return (true, e);
                    }));
                    return true;
                }
                return false;
            }
        }     

        /// <summary>
        /// Add a node to the model system's variable list, making it available for use
        /// in parameter expressions.
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="node">The node to add as a variable.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if successful, false otherwise with an error message.</returns>
        public bool AddVariable(User user, Node node, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(node);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                if (ModelSystem.Variables.Contains(node))
                {
                    error = new CommandError("The node is already a model system variable.");
                    return false;
                }
                ModelSystem.Variables.Add(node);
                Buffer.AddUndo(new Command(
                    () => { ModelSystem.Variables.Remove(node); return (true, null); },
                    () => { ModelSystem.Variables.Add(node);    return (true, null); }));
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Remove a node from the model system's variable list.
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="node">The node to remove.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if successful, false otherwise with an error message.</returns>
        public bool RemoveVariable(User user, Node node, [NotNullWhen(false)] out CommandError? error)        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(node);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var idx = ModelSystem.Variables.IndexOf(node);
                if (idx < 0)
                {
                    error = new CommandError("The node is not a model system variable.");
                    return false;
                }
                ModelSystem.Variables.RemoveAt(idx);
                Buffer.AddUndo(new Command(
                    () => {
                        var restoreIdx = Math.Min(idx, ModelSystem.Variables.Count);
                        ModelSystem.Variables.Insert(restoreIdx, node);
                        return (true, null);
                    },
                    () => { ModelSystem.Variables.Remove(node); return (true, null); }));
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Designates a node inside a <see cref="FunctionTemplate"/>'s
        /// <see cref="FunctionTemplate.InternalModules"/> as a template-local variable.
        /// Local variables are resolved before global model-system variables when compiling
        /// scripted parameter expressions for nodes inside the same template.
        /// Scripts outside the template cannot reference these variables.
        /// </summary>
        public bool AddFunctionTemplateVariable(User user, FunctionTemplate template, Node node,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(template);
            ArgumentNullException.ThrowIfNull(node);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                if (!template.AddLocalVariable(node, out error)) return false;
                Buffer.AddUndo(new Command(
                    () => { template.RemoveLocalVariable(node, out _); return (true, null); },
                    () => { template.AddLocalVariable(node, out _);    return (true, null); }));
                return true;
            }
        }

        /// <summary>
        /// Removes a node from a <see cref="FunctionTemplate"/>'s local variable list.
        /// </summary>
        public bool RemoveFunctionTemplateVariable(User user, FunctionTemplate template, Node node,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(template);
            ArgumentNullException.ThrowIfNull(node);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                if (!template.RemoveLocalVariable(node, out error)) return false;
                Buffer.AddUndo(new Command(
                    () => { template.AddLocalVariable(node, out _);    return (true, null); },
                    () => { template.RemoveLocalVariable(node, out _); return (true, null); }));
                return true;
            }
        }

        private IEnumerable<Boundary> EnumerateAllBoundaries()
        {
            var stack = new Stack<Boundary>();
            stack.Push(ModelSystem.GlobalBoundary);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                yield return current;

                foreach (var child in current.Boundaries)
                    stack.Push(child);
                foreach (var ft in current.FunctionTemplates)
                    stack.Push(ft.InternalModules);
            }
        }

        private bool IsNodeUsedAsVariable(Node node, out string usageDescription)
        {
            if (ModelSystem.Variables.Contains(node))
            {
                usageDescription = "it is used as a Model System variable";
                return true;
            }

            foreach (var boundary in EnumerateAllBoundaries())
            {
                foreach (var ft in boundary.FunctionTemplates)
                {
                    if (ft.LocalVariables.Contains(node))
                    {
                        usageDescription = $"it is used as a local variable in function template '{ft.Name}'";
                        return true;
                    }
                }
            }

            usageDescription = string.Empty;
            return false;
        }

        private static Node ResolveDestinationNode(Node destination)
            => destination is GhostNode gn ? gn.ReferencedNode : destination;

        private static bool DestinationWouldBeDisabledAfterChanging(Node destination, Node nodeBeingDisabled)
        {
            var resolved = ResolveDestinationNode(destination);

            if (ReferenceEquals(resolved, nodeBeingDisabled))
                return true;

            if (resolved is FunctionInstance fi)
            {
                if (ReferenceEquals(fi, nodeBeingDisabled))
                    return true;
                if (ReferenceEquals(fi.Template.EntryNode, nodeBeingDisabled))
                    return true;
                return fi.IsDisabled || fi.Template.EntryNode?.IsDisabled == true;
            }

            return resolved.IsDisabled;
        }

        private bool IsNodeRequiredByEnabledLink(Node node, out string reason)
        {
            foreach (var boundary in EnumerateAllBoundaries())
            {
                foreach (var link in boundary.Links)
                {
                    if (link.IsDisabled || link.Origin.IsDisabled)
                        continue;

                    if (link is SingleLink sl)
                    {
                        bool targetsNode = ReferenceEquals(ResolveDestinationNode(sl.Destination), node)
                            || (ResolveDestinationNode(sl.Destination) is FunctionInstance fi
                                && ReferenceEquals(fi.Template.EntryNode, node));
                        if (!targetsNode)
                            continue;

                        if (sl.OriginHook.Cardinality == HookCardinality.Single)
                        {
                            reason = $"it is required by hook '{sl.OriginHook.Name}' on node '{sl.Origin.Name}'";
                            return true;
                        }

                        continue;
                    }

                    if (link is MultiLink ml && ml.OriginHook.Cardinality == HookCardinality.AtLeastOne)
                    {
                        int enabledAfterChange = 0;
                        bool thisNodeWasReferenced = false;

                        foreach (var dest in ml.Destinations)
                        {
                            var resolved = ResolveDestinationNode(dest);
                            if (ReferenceEquals(resolved, node)
                                || (resolved is FunctionInstance fi && ReferenceEquals(fi.Template.EntryNode, node)))
                            {
                                thisNodeWasReferenced = true;
                            }

                            if (!DestinationWouldBeDisabledAfterChanging(dest, node))
                                enabledAfterChange++;
                        }

                        if (thisNodeWasReferenced && enabledAfterChange == 0)
                        {
                            reason = $"it is the last enabled destination of required hook '{ml.OriginHook.Name}' on node '{ml.Origin.Name}'";
                            return true;
                        }
                    }
                }
            }

            reason = string.Empty;
            return false;
        }

        /// <summary>
        /// Set the node to the disabled state.
        /// </summary>
        /// <param name="user">The user issuing the command</param>
        /// <param name="node">The node</param>
        /// <param name="disabled">If it should be disabled (true) or not (false).</param>
        /// <param name="error">An error message explaining why the operation failed.</param>
        /// <returns>True if the operation completed successfully, false otherwise.</returns>
        public bool SetNodeDisabled(User user, Node node, bool disabled, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(node);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                if (node.IsDisabled == disabled)
                {
                    error = null;
                    return true;
                }

                if (disabled)
                {
                    if (IsNodeUsedAsVariable(node, out var variableUsage))
                    {
                        error = new CommandError($"Unable to disable '{node.Name}' because {variableUsage}.");
                        return false;
                    }

                    if (IsNodeRequiredByEnabledLink(node, out var requiredReason))
                    {
                        error = new CommandError($"Unable to disable '{node.Name}' because {requiredReason}.");
                        return false;
                    }
                }

                error = null;
                if (node.SetDisabled(disabled, out error))
                {
                    Buffer.AddUndo(new Command(() =>
                    {
                        return (node.SetDisabled(!disabled, out var error), error);
                    }, () =>
                    {
                        return (node.SetDisabled(disabled, out var error), error);
                    }));
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Set multiple nodes to the same disabled state as a single undoable action.
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="nodes">Nodes to update. Duplicate entries are ignored.</param>
        /// <param name="disabled">If nodes should be disabled (true) or enabled (false).</param>
        /// <param name="error">An error message explaining why the operation failed.</param>
        /// <returns>True if the operation completed successfully, false otherwise.</returns>
        public bool SetNodesDisabled(User user, IEnumerable<Node> nodes, bool disabled,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(nodes);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }

                var uniqueTargets = nodes
                    .Where(n => n is not null)
                    .Distinct()
                    .ToList();

                // No-op for empty input to simplify caller logic.
                if (uniqueTargets.Count == 0)
                {
                    error = null;
                    return true;
                }

                var nodesToChange = uniqueTargets
                    .Where(n => n.IsDisabled != disabled)
                    .ToList();

                if (nodesToChange.Count == 0)
                {
                    error = null;
                    return true;
                }

                if (disabled)
                {
                    foreach (var node in nodesToChange)
                    {
                        if (IsNodeUsedAsVariable(node, out var variableUsage))
                        {
                            error = new CommandError($"Unable to disable '{node.Name}' because {variableUsage}.");
                            return false;
                        }

                        if (IsNodeRequiredByEnabledLink(node, out var requiredReason))
                        {
                            error = new CommandError($"Unable to disable '{node.Name}' because {requiredReason}.");
                            return false;
                        }
                    }
                }

                var changedNodes = new List<Node>(nodesToChange.Count);
                var batch = new CommandBatch();

                foreach (var node in nodesToChange)
                {
                    if (!node.SetDisabled(disabled, out error))
                    {
                        // Best-effort rollback for nodes already changed in this operation.
                        foreach (var changed in changedNodes)
                            _ = changed.SetDisabled(!disabled, out _);
                        return false;
                    }

                    changedNodes.Add(node);
                    batch.Add(new Command(() =>
                    {
                        return (node.SetDisabled(!disabled, out var undoError), undoError);
                    }, () =>
                    {
                        return (node.SetDisabled(disabled, out var redoError), redoError);
                    }));
                }

                Buffer.AddUndo(batch);
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Set if the given link should be disabled.
        /// </summary>
        /// <param name="user">The user issuing the command</param>
        /// <param name="link">The link to operate on.</param>
        /// <param name="disabled">If it should be disabled (true) or not (false).</param>
        /// <param name="error">An error message explaining why the operation failed.</param>
        /// <returns>True if the operation completed successfully, false otherwise.</returns>
        public bool SetLinkDisabled(User user, Link link, bool disabled, [NotNullWhen(false)]out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(link);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                if (link.SetDisabled(disabled, out error))
                {
                    Buffer.AddUndo(new Command(() =>
                    {
                        return (link.SetDisabled(!disabled, out var error), error);
                    }, () =>
                    {
                        return (link.SetDisabled(disabled, out var error), error);
                    }));
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Set multiple links to the same disabled state as a single undoable action.
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="links">Links to update. Duplicate entries are ignored.</param>
        /// <param name="disabled">If links should be disabled (true) or enabled (false).</param>
        /// <param name="error">An error message explaining why the operation failed.</param>
        /// <returns>True if the operation completed successfully, false otherwise.</returns>
        public bool SetLinksDisabled(User user, IEnumerable<Link> links, bool disabled,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(links);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }

                var uniqueTargets = links
                    .Where(l => l is not null)
                    .Distinct()
                    .ToList();

                // No-op for empty input to simplify caller logic.
                if (uniqueTargets.Count == 0)
                {
                    error = null;
                    return true;
                }

                var linksToChange = uniqueTargets
                    .Where(l => l.IsDisabled != disabled)
                    .ToList();

                if (linksToChange.Count == 0)
                {
                    error = null;
                    return true;
                }

                var changedLinks = new List<Link>(linksToChange.Count);
                var batch = new CommandBatch();

                foreach (var link in linksToChange)
                {
                    if (!link.SetDisabled(disabled, out error))
                    {
                        // Best-effort rollback for links already changed in this operation.
                        foreach (var changed in changedLinks)
                            _ = changed.SetDisabled(!disabled, out _);
                        return false;
                    }

                    changedLinks.Add(link);
                    batch.Add(new Command(() =>
                    {
                        return (link.SetDisabled(!disabled, out var undoError), undoError);
                    }, () =>
                    {
                        return (link.SetDisabled(disabled, out var redoError), redoError);
                    }));
                }

                Buffer.AddUndo(batch);
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Sets visibility state for one destination branch on a link.
        /// For <see cref="SingleLink"/> the only valid index is 0.
        /// For <see cref="MultiLink"/>, index maps to the destination slot.
        /// </summary>
        public bool SetLinkDestinationHidden(User user, Link link, int destinationIndex, bool hidden,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(link);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }

                if (destinationIndex < 0 || destinationIndex >= link.DestinationCount)
                {
                    error = new CommandError("Destination index is out of range.");
                    return false;
                }

                bool previous = link.IsDestinationHidden(destinationIndex);
                if (previous == hidden)
                {
                    error = null;
                    return true;
                }

                if (!link.SetDestinationHidden(destinationIndex, hidden, out error))
                    return false;

                Buffer.AddUndo(new Command(() =>
                {
                    return (link.SetDestinationHidden(destinationIndex, previous, out var undoErr), undoErr);
                }, () =>
                {
                    return (link.SetDestinationHidden(destinationIndex, hidden, out var redoErr), redoErr);
                }));

                error = null;
                return true;
            }
        }

        /// <summary>
        /// Sets visibility state for specific destination branches (link + destination index)
        /// as one undoable action.
        /// </summary>
        public bool SetLinkDestinationBranchesHidden(User user,
            IEnumerable<(Link Link, int DestinationIndex)> branches, bool hidden,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(branches);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }

                var normalized = branches
                    .Where(b => b.Link is not null)
                    .Distinct()
                    .ToList();

                if (normalized.Count == 0)
                {
                    error = null;
                    return true;
                }

                var snapshots = new List<(Link Link, int DestinationIndex, bool Previous)>();
                foreach (var (link, destinationIndex) in normalized)
                {
                    if (destinationIndex < 0 || destinationIndex >= link.DestinationCount)
                    {
                        error = new CommandError("Destination index is out of range.");
                        return false;
                    }

                    bool previous = link.IsDestinationHidden(destinationIndex);
                    if (previous != hidden)
                        snapshots.Add((link, destinationIndex, previous));
                }

                if (snapshots.Count == 0)
                {
                    error = null;
                    return true;
                }

                var applied = new List<(Link Link, int DestinationIndex, bool Previous)>(snapshots.Count);
                foreach (var snap in snapshots)
                {
                    if (!snap.Link.SetDestinationHidden(snap.DestinationIndex, hidden, out error))
                    {
                        foreach (var rollback in applied)
                            _ = rollback.Link.SetDestinationHidden(rollback.DestinationIndex, rollback.Previous, out _);
                        return false;
                    }
                    applied.Add(snap);
                }

                var batch = new CommandBatch();
                foreach (var snap in snapshots)
                {
                    var captured = snap;
                    batch.Add(new Command(() =>
                    {
                        return (captured.Link.SetDestinationHidden(captured.DestinationIndex, captured.Previous, out var undoErr), undoErr);
                    }, () =>
                    {
                        return (captured.Link.SetDestinationHidden(captured.DestinationIndex, hidden, out var redoErr), redoErr);
                    }));
                }

                Buffer.AddUndo(batch);
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Sets visibility state for every destination branch on one link as a single undoable action.
        /// </summary>
        public bool SetLinkDestinationsHidden(User user, Link link, bool hidden,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(link);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }

                if (link.DestinationCount == 0)
                {
                    error = null;
                    return true;
                }

                var previous = new bool[link.DestinationCount];
                bool hasChange = false;
                for (int i = 0; i < previous.Length; i++)
                {
                    previous[i] = link.IsDestinationHidden(i);
                    if (previous[i] != hidden) hasChange = true;
                }

                if (!hasChange)
                {
                    error = null;
                    return true;
                }

                if (!link.SetAllDestinationsHidden(hidden, out error))
                    return false;

                Buffer.AddUndo(new Command(() =>
                {
                    for (int i = 0; i < previous.Length; i++)
                    {
                        if (!link.SetDestinationHidden(i, previous[i], out var e))
                            return (false, e);
                    }
                    return (true, null);
                }, () =>
                {
                    return (link.SetAllDestinationsHidden(hidden, out var redoErr), redoErr);
                }));

                error = null;
                return true;
            }
        }

        /// <summary>
        /// Sets visibility state for all destination branches of every provided link as one undoable action.
        /// </summary>
        public bool SetLinksDestinationsHidden(User user, IEnumerable<Link> links, bool hidden,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(links);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }

                var uniqueTargets = links
                    .Where(l => l is not null)
                    .Distinct()
                    .ToList();

                if (uniqueTargets.Count == 0)
                {
                    error = null;
                    return true;
                }

                var snapshots = new List<(Link Link, bool[] Previous)>(uniqueTargets.Count);
                foreach (var link in uniqueTargets)
                {
                    var previous = new bool[link.DestinationCount];
                    bool hasChange = false;
                    for (int i = 0; i < previous.Length; i++)
                    {
                        previous[i] = link.IsDestinationHidden(i);
                        if (previous[i] != hidden) hasChange = true;
                    }

                    if (hasChange)
                        snapshots.Add((link, previous));
                }

                if (snapshots.Count == 0)
                {
                    error = null;
                    return true;
                }

                var applied = new List<(Link Link, bool[] Previous)>(snapshots.Count);
                foreach (var (targetLink, previous) in snapshots)
                {
                    if (!targetLink.SetAllDestinationsHidden(hidden, out error))
                    {
                        foreach (var (rollbackLink, rollbackPrevious) in applied)
                        {
                            for (int i = 0; i < rollbackPrevious.Length; i++)
                                _ = rollbackLink.SetDestinationHidden(i, rollbackPrevious[i], out _);
                        }
                        return false;
                    }
                    applied.Add((targetLink, previous));
                }

                var batch = new CommandBatch();
                foreach (var (targetLink, previous) in snapshots)
                {
                    batch.Add(new Command(() =>
                    {
                        for (int i = 0; i < previous.Length; i++)
                        {
                            if (!targetLink.SetDestinationHidden(i, previous[i], out var e))
                                return (false, e);
                        }
                        return (true, null);
                    }, () =>
                    {
                        return (targetLink.SetAllDestinationsHidden(hidden, out var redoErr), redoErr);
                    }));
                }

                Buffer.AddUndo(batch);
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Sets destination-link visibility for all links in the model system as a single undoable action.
        /// </summary>
        public bool SetAllLinkDestinationsHidden(User user, bool hidden, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);

            var allLinks = EnumerateAllBoundaries()
                .SelectMany(b => b.Links)
                .Distinct()
                .ToList();

            return SetLinksDestinationsHidden(user, allLinks, hidden, out error);
        }

        /// <summary>
        /// Sets the orthogonal-routing flag on a link and records the change in the undo buffer.
        /// </summary>
        public bool SetLinkOrthogonal(User user, Link link, bool orthogonal, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(link);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                if (link.SetOrthogonal(orthogonal, out error))
                {
                    Buffer.AddUndo(new Command(() =>
                    {
                        return (link.SetOrthogonal(!orthogonal, out var error), error);
                    }, () =>
                    {
                        return (link.SetOrthogonal(orthogonal, out var error), error);
                    }));
                    return true;
                }
                return false;
            }
        }

        /// <summary>Sets the orthogonal-routing flag on multiple links as one undoable action.</summary>
        public bool SetLinksOrthogonal(User user, IEnumerable<Link> links, bool orthogonal,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(links);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }

                var linksToChange = links.Where(l => l is not null)
                    .Distinct()
                    .Where(l => l.IsOrthogonal != orthogonal)
                    .ToList();
                if (linksToChange.Count == 0)
                {
                    error = null;
                    return true;
                }

                var changedLinks = new List<Link>(linksToChange.Count);
                var batch = new CommandBatch();
                foreach (var linkToChange in linksToChange)
                {
                    if (!linkToChange.SetOrthogonal(orthogonal, out error))
                    {
                        foreach (var changed in changedLinks)
                            _ = changed.SetOrthogonal(!orthogonal, out _);
                        return false;
                    }

                    changedLinks.Add(linkToChange);
                    batch.Add(new Command(() =>
                    {
                        return (linkToChange.SetOrthogonal(!orthogonal, out var undoError), undoError);
                    }, () =>
                    {
                        return (linkToChange.SetOrthogonal(orthogonal, out var redoError), redoError);
                    }));
                }

                Buffer.AddUndo(batch);
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Save the model system
        /// </summary>
        /// <param name="error">An error message in case the save fails.</param>
        /// <returns>True if it succeeds, false with an error message otherwise.</returns>
        public bool Save(out CommandError? error)
        {
            lock (_sessionLock)
            {
                string? errorString = null;
                if (!ModelSystem.Save(ref errorString))
                {
                    error = new CommandError(errorString ?? "No error message given when failing to save the model system!");
                    return false;
                }
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Save the model system to the given stream
        /// </summary>
        /// <param name="error">An error message if something goes wrong saving the model system.</param>
        /// <param name="saveTo">The stream to save the model system to.</param>
        /// <returns>True if the model system was saved successfully.</returns>
        public bool Save([NotNullWhen(false)] out CommandError? error, Stream saveTo)
        {
            lock (_sessionLock)
            {
                string? errorString = null;
                if (!ModelSystem.Save(ref errorString, saveTo))
                {
                    error = new CommandError(errorString ?? "No error message given when failing to save the model system!");
                    return false;
                }
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Remove the given link from the model system.
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="link">The link to remove.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        public bool RemoveLink(User user, Link link, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(link);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var boundary = link.Origin!.ContainedWithin!;
                if (boundary.RemoveLink(link, out error))
                {
                    Buffer.AddUndo(new Command(() =>
                    {
                        return (boundary.AddLink(link, out var e), e);
                    }, () =>
                    {
                        return (boundary.RemoveLink(link, out var e), e);
                    }));
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Removes multiple links as one undoable action.
        /// </summary>
        public bool RemoveLinks(User user, IEnumerable<Link> links, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(links);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }

                var targets = links.Where(l => l is not null).Distinct().ToList();
                if (targets.Count == 0)
                {
                    error = null;
                    return true;
                }

                var removed = new List<(Link Link, Boundary Boundary)>(targets.Count);
                var batch = new CommandBatch();
                foreach (var linkToRemove in targets)
                {
                    var boundary = linkToRemove.Origin?.ContainedWithin;
                    if (boundary is null)
                    {
                        error = new CommandError("The link does not belong to a boundary.");
                        foreach (var (removedLink, removedBoundary) in removed)
                            removedBoundary.AddLink(removedLink, out _);
                        return false;
                    }
                    if (!boundary.RemoveLink(linkToRemove, out error))
                    {
                        foreach (var (removedLink, removedBoundary) in removed)
                            removedBoundary.AddLink(removedLink, out _);
                        return false;
                    }

                    removed.Add((linkToRemove, boundary));
                    batch.Add(new Command(() =>
                    {
                        return (boundary.AddLink(linkToRemove, out var undoError), undoError);
                    }, () =>
                    {
                        return (boundary.RemoveLink(linkToRemove, out var redoError), redoError);
                    }));
                }

                Buffer.AddUndo(batch);
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Add a new link originating at a hook and going to the destination node
        /// </summary>
        /// <param name="user"></param>
        /// <param name="origin"></param>
        /// <param name="originHook"></param>
        /// <param name="destination"></param>
        /// <param name="link"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        public bool AddLink(User user, Node origin, NodeHook originHook,
            Node destination, out Link? link, out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(origin);
            ArgumentNullException.ThrowIfNull(originHook);
            ArgumentNullException.ThrowIfNull(destination);
            link = null;

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                bool success = false;
                if (originHook.Cardinality == HookCardinality.Single
                    || originHook.Cardinality == HookCardinality.SingleOptional)
                {
                    if (origin.GetLink(originHook, out Link? _link))
                    {
                        if (_link is SingleLink sl)
                        {
                            var originalDestination = sl.Destination!;
                            success = sl.SetDestination(destination, out error);
                            if (success)
                            {
                                Buffer.AddUndo(new Command(() =>
                                {
                                    return (sl.SetDestination(originalDestination, out var e), e);
                                }, () =>
                                {
                                    return (sl.SetDestination(destination, out var e), e);
                                }
                                ));
                            }
                        }
                        else
                        {
                            throw new InvalidOperationException("A single cardinality link was not a SingleLink!");
                        }
                    }
                    else
                    {
                        success = origin.ContainedWithin!.AddLink(origin, originHook, destination, out link, out error);
                        if (success)
                        {
                            _link = link!;
                            Buffer.AddUndo(new Command(() =>
                            {
                                return (origin.ContainedWithin.RemoveLink(_link, out var e), e);
                            }, () =>
                            {
                                return (origin.ContainedWithin.AddLink(origin, originHook, destination, _link, out var e), e);
                            }));
                        }
                    }
                }
                else
                {
                    success = origin.ContainedWithin!.AddLink(origin, originHook, destination, out link, out error);
                    if (success)
                    {
                        Link _link = link!;
                        Buffer.AddUndo(new Command(() =>
                        {
                            return (origin.ContainedWithin.RemoveLink(_link, out var e), e);
                        }, () =>
                        {
                            return (origin.ContainedWithin.AddLink(origin, originHook, destination, _link, out var e), e);
                        }));
                    }
                }
                return success;
            }
        }

        /// <summary>
        /// Add a new function template to a boundary.
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="boundary">The boundary that the function template is being added to.</param>
        /// <param name="functionTemplateName">The name of the function template. This must be not null or whitespace.</param>
        /// <param name="functionTemplate">The newly created functionTemplate if successful, null otherwise.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        /// <exception cref="ArgumentNullException">Thrown if the user or boundary are null.</exception>
        public bool AddFunctionTemplate(User user, Boundary boundary, string functionTemplateName, out FunctionTemplate? functionTemplate, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(boundary);
            error = null;
            functionTemplate = null;

            if (string.IsNullOrWhiteSpace(functionTemplateName))
            {
                error = new CommandError($"You can not add a function template with an empty name!");
                return false;
            }
            lock(_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                if (ModelSystem.GlobalBoundary.ContainsFunctionTemplateName(functionTemplateName))
                {
                    error = new CommandError($"A function template named '{functionTemplateName}' already exists in the model system.");
                    return false;
                }
                if (!boundary.AddFunctionTemplate(functionTemplateName, out var template, out error))
                {
                    return false;
                }
                functionTemplate = template!;
                Buffer.AddUndo(new Command(() =>
                {
                    return (boundary.RemoveFunctionTemplate(template!, out var error), error);
                }, () =>
                {
                    return (boundary.AddFunctionTemplate(template!, out var error), error);
                }));
                return true;
            }
        }

        /// <summary>
        /// Remove the given function template from the boundary.
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="boundary">The boundary that the function template is being added to.</param>
        /// <param name="functionTemplate">The function template to remove from the given boundary.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        /// <exception cref="ArgumentNullException">Thrown if the user, boundary, or function template are null.</exception>
        public bool RemoveFunctionTemplate(User user, Boundary boundary, FunctionTemplate functionTemplate, [NotNullWhen(false)]  out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(boundary);
            ArgumentNullException.ThrowIfNull(functionTemplate);
            error = null;

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }

                // Refuse the removal if at least one FunctionInstance still references this template.
                var referencing = GetAllFunctionInstancesOf(functionTemplate);
                if (referencing.Count > 0)
                {
                    var names = string.Join(", ", referencing.Select(fi => $"'{fi.Name}'"));
                    error = new CommandError(
                        $"Cannot remove FunctionTemplate '{functionTemplate.Name}' because it is still " +
                        $"referenced by the following function instance(s): {names}. " +
                        $"Remove those instances first.");
                    return false;
                }

                if (!boundary.RemoveFunctionTemplate(functionTemplate, out error))
                {
                    error = new CommandError($"Failed to remove function template {functionTemplate.Name} from boundary {boundary.Name}: {error?.Message}");
                    return false;
                }
                Buffer.AddUndo(new Command(() =>
                {
                    return (boundary.AddFunctionTemplate(functionTemplate, out var error), error);
                }, () =>
                {
                    return (boundary.RemoveFunctionTemplate(functionTemplate, out var error), error);
                }));
                return true;
            }
        }

        /// <summary>
        /// Renames a function template within a boundary.
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="template">The function template to rename.</param>
        /// <param name="newName">The new name. Must not be null or whitespace.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        public bool RenameFunctionTemplate(User user, FunctionTemplate template, string newName,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(template);
            error = null;
            if (string.IsNullOrWhiteSpace(newName))
            {
                error = new CommandError("A function template name must not be empty.");
                return false;
            }
            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var oldName = template.Name;
                if (!string.Equals(oldName, newName, StringComparison.Ordinal)
                    && ModelSystem.GlobalBoundary.ContainsFunctionTemplateName(newName))
                {
                    error = new CommandError($"A function template named '{newName}' already exists in the model system.");
                    return false;
                }
                template.Name = newName;
                Buffer.AddUndo(new Command(() =>
                {
                    template.Name = oldName;
                    return (true, null);
                }, () =>
                {
                    template.Name = newName;
                    return (true, null);
                }));
                return true;
            }
        }

        /// <summary>
        /// Moves or resizes a function template's canvas container box.
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="template">The function template whose location is being updated.</param>
        /// <param name="newLocation">The new canvas location rectangle.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns>True if the operation succeeds, false otherwise with an error message.</returns>
        public bool SetFunctionTemplateLocation(User user, FunctionTemplate template, Rectangle newLocation,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(template);
            error = null;
            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var oldLocation = template.Location;
                template.SetLocation(newLocation);
                Buffer.AddUndo(new Command(() =>
                {
                    template.SetLocation(oldLocation);
                    return (true, null);
                }, () =>
                {
                    template.SetLocation(newLocation);
                    return (true, null);
                }));
                return true;
            }
        }

        /// <summary>
        /// Moves <paramref name="template"/> from <paramref name="sourceBoundary"/> to
        /// <paramref name="destinationBoundary"/> with full undo/redo support.
        /// <para>
        /// The operation fails if any <see cref="FunctionInstance"/> that references this
        /// template would lose access to it after the move.  Access is lost when the template
        /// is relocated to a different scope than the instance: specifically when one party is
        /// inside a <see cref="FunctionTemplate.InternalModules"/> boundary that the other
        /// cannot reach through the regular <see cref="Boundary.Boundaries"/> hierarchy.
        /// </para>
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="template">The function template to move.</param>
        /// <param name="sourceBoundary">The boundary that currently owns <paramref name="template"/>.</param>
        /// <param name="destinationBoundary">The boundary to move <paramref name="template"/> into.</param>
        /// <param name="error">An error description when the method returns <c>false</c>.</param>
        /// <returns><c>true</c> on success; <c>false</c> with a populated <paramref name="error"/> on failure.</returns>
        public bool MoveFunctionTemplate(User user, FunctionTemplate template,
            Boundary sourceBoundary, Boundary destinationBoundary,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(template);
            ArgumentNullException.ThrowIfNull(sourceBoundary);
            ArgumentNullException.ThrowIfNull(destinationBoundary);
            error = null;

            if (sourceBoundary == destinationBoundary)
            {
                error = new CommandError("The source and destination boundaries are the same.");
                return false;
            }

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }

                // The template must currently live in sourceBoundary.
                if (!sourceBoundary.FunctionTemplates.Contains(template))
                {
                    error = new CommandError(
                        $"FunctionTemplate '{template.Name}' does not belong to the specified source boundary.");
                    return false;
                }

                // Determine the owning FunctionTemplate scope for the destination boundary.
                var destScope = FindOwningFunctionTemplate(ModelSystem.GlobalBoundary, destinationBoundary);

                // Check every existing FunctionInstance that references this template.
                var instances = GetAllFunctionInstancesOf(template);
                foreach (var fi in instances)
                {
                    var instScope = FindOwningFunctionTemplate(ModelSystem.GlobalBoundary, fi.ContainedWithin);
                    if (instScope != destScope)
                    {
                        string instanceDescription = instScope is null
                            ? "the global scope"
                            : $"FunctionTemplate '{instScope.Name}'";
                        string destDescription = destScope is null
                            ? "the global scope"
                            : $"FunctionTemplate '{destScope.Name}'";
                        error = new CommandError(
                            $"Cannot move FunctionTemplate '{template.Name}': " +
                            $"FunctionInstance '{fi.Name}' is in {instanceDescription} and would " +
                            $"no longer be able to reference the template after it is moved to {destDescription}.");
                        return false;
                    }
                }

                // Perform the move.
                if (!sourceBoundary.RemoveFunctionTemplate(template, out error))
                    return false;

                if (!destinationBoundary.AddFunctionTemplate(template, out error))
                {
                    // Roll back the removal if the add fails (e.g. name clash).
                    _ = sourceBoundary.AddFunctionTemplate(template, out _);
                    return false;
                }
                template.SetParent(destinationBoundary);

                Buffer.AddUndo(new Command(() =>
                {
                    // Undo: move back to sourceBoundary.
                    _ = destinationBoundary.RemoveFunctionTemplate(template, out _);
                    _ = sourceBoundary.AddFunctionTemplate(template, out _);
                    template.SetParent(sourceBoundary);
                    return (true, null);
                }, () =>
                {
                    // Redo: move to destinationBoundary again.
                    _ = sourceBoundary.RemoveFunctionTemplate(template, out _);
                    _ = destinationBoundary.AddFunctionTemplate(template, out _);
                    template.SetParent(destinationBoundary);
                    return (true, null);
                }));
                return true;
            }
        }

        /// <summary>
        /// Designates <paramref name="entryNode"/> as the <see cref="FunctionTemplate.EntryNode"/>
        /// of <paramref name="template"/>, or clears it when <paramref name="entryNode"/> is <c>null</c>.
        /// <para>
        /// The entry node must be a <see cref="Start"/> that belongs to
        /// <paramref name="template"/>'s <see cref="FunctionTemplate.InternalModules"/>.
        /// It defines the runtime type of the template so that
        /// <see cref="FunctionInstance"/> objects can participate in hook-compatibility checks.
        /// </para>
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="template">The function template to modify.</param>
        /// <param name="entryNode">The start node to use as the entry point, or <c>null</c> to clear.</param>
        /// <param name="error">An error description when the method returns <c>false</c>.</param>
        /// <returns><c>true</c> on success; <c>false</c> with a populated <paramref name="error"/> on failure.</returns>
        public bool SetFunctionTemplateEntryNode(User user, FunctionTemplate template, Node? entryNode,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(template);
            error = null;
            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                // Validate: the entry node (when non-null) must live in InternalModules.
                if (entryNode != null && entryNode.ContainedWithin != template.InternalModules)
                {
                    error = new CommandError(
                        $"The node '{entryNode.Name}' does not belong to the InternalModules of template '{template.Name}'.");
                    return false;
                }

                // Reject the change when it would break at least one existing link whose
                // destination is a FunctionInstance of this template.
                // FunctionInstance.Type returns entryNode.Type (or typeof(object) when null).
                var newType = entryNode?.Type;
                var instances = GetAllFunctionInstancesOf(template);
                foreach (var fi in instances)
                {
                    var incomingLinks = GetLinksGoingTo(fi);
                    foreach (var link in incomingLinks)
                    {
                        var hookType = link.OriginHook!.Type;
                        // For array hooks, check the element type.
                        var effectiveHookType = hookType.IsArray
                            ? hookType.GetElementType()!
                            : hookType;
                        // A null newType maps to typeof(object), which satisfies no typed hook.
                        bool compatible = newType is not null
                            && effectiveHookType.IsAssignableFrom(newType);
                        if (!compatible)
                        {
                            error = new CommandError(
                                $"Cannot change the entry node of template '{template.Name}': " +
                                $"FunctionInstance '{fi.Name}' is wired to hook '{link.OriginHook.Name}' " +
                                $"(type '{effectiveHookType.Name}') on node '{link.Origin?.Name}', " +
                                $"which is incompatible with the new entry-node type '{newType?.Name ?? "none"}'.");
                            return false;
                        }
                    }
                }

                var oldEntryNode = template.EntryNode;
                template.SetEntryNode(entryNode);
                Buffer.AddUndo(new Command(() =>
                {
                    template.SetEntryNode(oldEntryNode);
                    return (true, null);
                }, () =>
                {
                    template.SetEntryNode(entryNode);
                    return (true, null);
                }));
                return true;
            }
        }

        /// <summary>
        /// Adds a new <see cref="FunctionParameter"/> to <paramref name="template"/>.
        /// The parameter becomes a valid link destination inside the template and a hook
        /// on every <see cref="FunctionInstance"/> that references the template.
        /// </summary>
        public bool AddFunctionParameter(User user, FunctionTemplate template, string name, Type type,
            Rectangle location, out FunctionParameter? parameter,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(template);
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(type);
            parameter = null;
            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                if (!template.AddFunctionParameter(name, type, location, out parameter, out error))
                    return false;
                var captured = parameter!;
                var capturedIdx = template.FunctionParameters.Count - 1;
                Buffer.AddUndo(new Command(() =>
                {
                    template.RemoveFunctionParameter(captured, out var e);
                    return (true, e);
                }, () =>
                {
                    template.RestoreFunctionParameter(captured, capturedIdx);
                    return (true, null);
                }));
                return true;
            }
        }

        /// <summary>
        /// Replaces a BasicParameter inside a function template with a FunctionParameter.
        /// Existing internal links are redirected to the new FunctionParameter, and every
        /// FunctionInstance receives a BasicParameter provider containing the old value.
        /// </summary>
        public bool ConvertBasicParameterToFunctionParameter(User user, Node basicParameter,
            out FunctionParameter? parameter, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(basicParameter);
            parameter = null;

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }

                var type = basicParameter.Type;
                if (type is null || !type.IsGenericType
                    || type.GetGenericTypeDefinition() != typeof(RuntimeModules.BasicParameter<>))
                {
                    error = new CommandError("Only BasicParameter nodes can be converted to FunctionParameters.");
                    return false;
                }

                var internalBoundary = basicParameter.ContainedWithin;
                var template = internalBoundary?.OwningFunctionTemplate;
                if (internalBoundary is null || template is null)
                {
                    error = new CommandError("The BasicParameter must be inside a FunctionTemplate.");
                    return false;
                }

                var value = basicParameter.ParameterValue?.Representation;
                if (value is null)
                {
                    error = new CommandError($"BasicParameter '{basicParameter.Name}' has no value to expose.");
                    return false;
                }

                var linked = internalBoundary.Links
                    .Where(link => link.HasDestination(basicParameter))
                    .ToList();
                var parameterLocation = basicParameter.Location;
                var parameterBaseName = basicParameter.Name;
                if (parameterLocation.Equals(Rectangle.Hidden))
                {
                    parameterBaseName = linked.FirstOrDefault()?.OriginHook.Name ?? parameterBaseName;
                    var containingElement = linked
                        .Select(link => link.Origin)
                        .FirstOrDefault(origin => !origin.Location.Equals(Rectangle.Hidden));
                    parameterLocation = containingElement is not null
                        ? new Rectangle(containingElement.Location.X + containingElement.Location.Width + 30f,
                            containingElement.Location.Y, 250f, 50f)
                        : new Rectangle(40f, 40f, 250f, 50f);
                }
                    var parameterName = MakeUniqueFunctionParameterName(template, parameterBaseName);
                var instances = EnumerateBoundaries(ModelSystem.GlobalBoundary)
                    .SelectMany(boundary => boundary.FunctionInstances)
                    .Where(instance => ReferenceEquals(instance.Template, template))
                    .ToList();
                var providers = new List<(FunctionInstance Instance, Node Provider, Link Link)>();

                if (!template.AddFunctionParameter(parameterName, type, parameterLocation,
                        out parameter, out error))
                    return false;

                var capturedParameter = parameter;
                bool Apply()
                {
                    CommandError? localError;
                    foreach (var link in linked)
                        internalBoundary.RemoveLink(link, out localError);
                    internalBoundary.RemoveNode(basicParameter, out localError);

                    foreach (var link in linked)
                    {
                        if (link is SingleLink single)
                        {
                            single.SetDestination(capturedParameter, out localError);
                        }
                        else if (link is MultiLink multi)
                        {
                            for (int i = 0; i < multi.Destinations.Count; i++)
                            {
                                if (ReferenceEquals(multi.Destinations[i], basicParameter))
                                    multi.ReplaceDestination(i, capturedParameter, out localError);
                            }
                        }
                        internalBoundary.AddLink(link, out localError);
                    }

                    foreach (var instance in instances)
                    {
                        var providerName = MakeUniqueNodeName(instance.ContainedWithin!, parameterName);
                        if (!instance.ContainedWithin!.AddNode(GetModuleRepository(), providerName, type,
                                Rectangle.Hidden, out var provider, out localError))
                            return false;
                        if (!provider!.SetParameterValue(ParameterExpression.CreateParameter(value, type.GenericTypeArguments[0]),
                                out localError))
                            return false;

                        var hook = instance.Hooks.OfType<FunctionParameterHook>()
                            .FirstOrDefault(candidate => ReferenceEquals(candidate.Parameter, capturedParameter));
                        if (hook is null || !instance.ContainedWithin.AddLink(instance, hook, provider,
                                out var providerLink, out localError))
                            return false;
                        providers.Add((instance, provider, providerLink!));
                    }

                    return true;
                }

                void Restore()
                {
                    CommandError? localError;
                    foreach (var (_, provider, providerLink) in providers)
                    {
                        provider.ContainedWithin!.RemoveLink(providerLink, out localError);
                        provider.ContainedWithin.RemoveNode(provider, out localError);
                    }
                    providers.Clear();

                    foreach (var link in linked)
                        internalBoundary.RemoveLink(link, out localError);
                    foreach (var link in linked)
                    {
                        if (link is SingleLink single)
                            single.SetDestination(basicParameter, out localError);
                        else if (link is MultiLink multi)
                        {
                            for (int i = 0; i < multi.Destinations.Count; i++)
                            {
                                if (ReferenceEquals(multi.Destinations[i], capturedParameter))
                                    multi.ReplaceDestination(i, basicParameter, out localError);
                            }
                        }
                        internalBoundary.AddLink(link, out localError);
                    }
                    internalBoundary.AddNode(basicParameter, out localError);
                    template.RemoveFunctionParameter(capturedParameter, out localError);
                }

                if (!Apply())
                {
                    Restore();
                    error = new CommandError("Unable to create FunctionParameter providers for all FunctionInstances.");
                    parameter = null;
                    return false;
                }

                Buffer.AddUndo(new Command(
                    () => { Restore(); return (true, null); },
                    () => { return (Apply(), (CommandError?)null); }));
                error = null;
                return true;
            }
        }

        private static string MakeUniqueFunctionParameterName(FunctionTemplate template, string baseName)
        {
            var name = baseName;
            int index = 2;
            while (template.FunctionParameters.Any(parameter =>
                parameter.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                name = $"{baseName}{index++}";
            return name;
        }

        private static string MakeUniqueNodeName(Boundary boundary, string baseName)
        {
            var name = baseName;
            int index = 2;
            while (boundary.Modules.Any(node => node.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                || boundary.FunctionInstances.Any(instance => instance.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                name = $"{baseName}{index++}";
            return name;
        }

        private static IEnumerable<Boundary> EnumerateBoundaries(Boundary boundary)
        {
            yield return boundary;
            foreach (var child in boundary.Boundaries)
            {
                foreach (var nested in EnumerateBoundaries(child))
                    yield return nested;
            }
        }

        /// <summary>
        /// Removes a <see cref="FunctionParameter"/> from <paramref name="template"/>.
        /// Any links inside the template that point to the parameter are also removed.
        /// </summary>
        public bool RemoveFunctionParameter(User user, FunctionTemplate template, FunctionParameter parameter,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(template);
            ArgumentNullException.ThrowIfNull(parameter);
            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                // Refuse deletion if any FunctionInstance in the model has an active link
                // from the hook that corresponds to this FunctionParameter.
                if (HasActiveFunctionParameterLink(ModelSystem.GlobalBoundary, template, parameter))
                {
                    error = new CommandError(
                        $"Cannot remove FunctionParameter '{parameter.Name}': one or more FunctionInstances have a link connected to this parameter's hook.");
                    return false;
                }
                // Capture state before any mutations.
                var internalBoundary = template.InternalModules;
                var linksToRemove = internalBoundary.Links
                    .Where(l => l.HasDestination(parameter))
                    .ToList();
                int restoreIndex = template.FunctionParameters.IndexOf(parameter);

                // Remove links that reference this parameter as a destination.
                foreach (var link in linksToRemove)
                    internalBoundary.RemoveLink(link, out _);

                if (!template.RemoveFunctionParameter(parameter, out error))
                {
                    // Roll back link removals on failure.
                    foreach (var link in linksToRemove)
                        internalBoundary.AddLink(link, out _);
                    return false;
                }
                Buffer.AddUndo(new Command(() =>
                {
                    // Undo: restore parameter and its links.
                    template.RestoreFunctionParameter(parameter, restoreIndex);
                    foreach (var link in linksToRemove)
                        internalBoundary.AddLink(link, out _);
                    return (true, null);
                }, () =>
                {
                    // Redo: re-remove links then the parameter.
                    foreach (var link in linksToRemove)
                        internalBoundary.RemoveLink(link, out _);
                    return (template.RemoveFunctionParameter(parameter, out var e), e);
                }));
                return true;
            }
        }

        /// <summary>
        /// Renames a <see cref="FunctionParameter"/> within <paramref name="template">>.
        /// Also updates the corresponding <see cref="FunctionParameterHook"/> name on every
        /// live <see cref="FunctionInstance"/> because the hook derives its name from the parameter.
        /// </summary>
        public bool RenameFunctionParameter(User user, FunctionTemplate template, FunctionParameter parameter,
            string newName, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(template);
            ArgumentNullException.ThrowIfNull(parameter);
            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var oldName = parameter.Name;
                if (!template.RenameFunctionParameter(parameter, newName, out error))
                    return false;
                Buffer.AddUndo(new Command(() =>
                {
                    template.RenameFunctionParameter(parameter, oldName, out _);
                    return (true, null);
                }, () =>
                {
                    template.RenameFunctionParameter(parameter, newName, out _);
                    return (true, null);
                }));
                return true;
            }
        }

        // ── FunctionInstance ──────────────────────────────────────────────

        /// <summary>
        /// Places a new <see cref="FunctionInstance"/> of <paramref name="template"/> into
        /// <paramref name="boundary"/>.
        /// </summary>
        public bool AddFunctionInstance(User user, Boundary boundary, FunctionTemplate template,
            string name, Rectangle location,
            out FunctionInstance? instance, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(boundary);
            ArgumentNullException.ThrowIfNull(template);
            instance = null;
            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                if (!boundary.AddFunctionInstance(name, template, location, out instance, out error))
                    return false;
                var captured = instance!;
                Buffer.AddUndo(new Command(() =>
                {
                    return (boundary.RemoveFunctionInstance(captured, out var e), e);
                }, () =>
                {
                    return (boundary.AddFunctionInstance(captured, out var e), e);
                }));
                return true;
            }
        }

        private static readonly HashSet<Type> _simpleFunctionParamTypes = new()
        {
            typeof(int), typeof(bool), typeof(string), typeof(float)
        };

        private static string GetDefaultValueString(Type primitiveType)
        {
            if (primitiveType == typeof(bool))   return "false";
            if (primitiveType == typeof(string)) return "";
            return "0";
        }

        /// <summary>
        /// Creates a new <see cref="FunctionInstance"/> and automatically generates a hidden
        /// <see cref="RuntimeModules.BasicParameter{T}"/> node for each
        /// <see cref="NodeHook.FunctionParameterHook"/> whose type is <c>IFunction&lt;T&gt;</c>
        /// where <c>T</c> is one of <see cref="int"/>, <see cref="bool"/>,
        /// <see cref="string"/>, or <see cref="float"/>.
        /// </summary>
        public bool AddFunctionInstanceGenerateParameters(
            User user, Boundary boundary, FunctionTemplate template,
            string name, Rectangle location,
            out FunctionInstance? instance, out List<Node>? children,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(boundary);
            ArgumentNullException.ThrowIfNull(template);

            children = null;
            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    instance = null;
                    return false;
                }

                if (!boundary.AddFunctionInstance(name, template, location, out instance, out error))
                    return false;

                var fi = instance!;
                var nodes = new List<Node>();
                var links = new List<Link>();

                foreach (var hook in fi.Hooks.OfType<FunctionParameterHook>())
                {
                    var hookType = hook.Type;
                    if (!hookType.IsGenericType) continue;

                    var args = hookType.GetGenericArguments();
                    if (args.Length != 1) continue;

                    var argType = args[0];
                    if (!_simpleFunctionParamTypes.Contains(argType)) continue;

                    var basicParamType = typeof(RuntimeModules.BasicParameter<>).MakeGenericType(argType);
                    if (!hookType.IsAssignableFrom(basicParamType)) continue;

                    var defaultStr = GetDefaultValueString(argType);
                    var child = Node.Create(GetModuleRepository(), hook.Name, basicParamType, boundary, Rectangle.Hidden);
                    if (child?.SetParameterValue(ParameterExpression.CreateParameter(defaultStr, argType), out var _) == true)
                    {
                        nodes.Add(child);
                        links.Add(new SingleLink(fi, hook, child, false));
                    }
                }

                children = nodes;

                void Add()
                {
                    CommandError? e = null;
                    foreach (var child in nodes)
                        boundary.AddNode(child, out e);
                    foreach (var link in links)
                        boundary.AddLink(link, out e);
                }

                void Remove()
                {
                    CommandError? e = null;
                    foreach (var link in links)
                        boundary.RemoveLink(link, out e);
                    foreach (var child in nodes)
                        boundary.RemoveNode(child, out e);
                }

                Add();

                Buffer.AddUndo(new Command(() =>
                {
                    Remove();
                    return (boundary.RemoveFunctionInstance(fi, out var e), e);
                }, () =>
                {
                    if (boundary.AddFunctionInstance(fi, out var e))
                    {
                        Add();
                        return (true, null);
                    }
                    return (false, e);
                }));

                return true;
            }
        }

        /// <summary>
        /// Removes <paramref name="instance"/> from its containing boundary, and cleans up
        /// all links that reference it (both as origin and as destination) as well as any
        /// hidden/inlined embedded parameter nodes that were destinations of its outgoing links.
        /// The operation is fully undo/redo-able.
        /// </summary>
        public bool RemoveFunctionInstance(User user, FunctionInstance instance,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(instance);
            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var boundary = instance.ContainedWithin!;

                // Outgoing links: this FI is the origin (FunctionParameterHook connections to external nodes).
                var outgoingLinks = boundary.Links.Where(l => l.Origin == instance).ToList();

                // Incoming links: other nodes/starts/FIs have this FI as a destination.
                var incomingLinks = GetLinksGoingTo(instance);
                var multiLinkRestoreInfo = BuildMultiLinkRestoreInfo(incomingLinks, instance);

                // Hidden/inlined embedded parameter nodes: destinations of outgoing links whose
                // location is Rectangle.Hidden and that live in the same boundary.
                var hiddenNodes = outgoingLinks
                    .SelectMany<Link, Node>(l =>
                        l is SingleLink sl && sl.Destination is not null ? new[] { sl.Destination }
                        : l is MultiLink ml ? ml.Destinations.ToArray()
                        : Array.Empty<Node>())
                    .Where(n => n.Location.Equals(Rectangle.Hidden) && ReferenceEquals(n.ContainedWithin, boundary))
                    .Distinct()
                    .ToList();

                // For each hidden node, capture any OTHER incoming links plus its ghost cascade.
                var hiddenCascadeData = hiddenNodes
                    .Select(hn =>
                    {
                        var hnLinks     = GetLinksGoingTo(hn).Where(l => !outgoingLinks.Contains(l)).ToList();
                        var hnMulti     = BuildMultiLinkRestoreInfo(hnLinks, hn);
                        var hnGhosts    = GetAllGhostNodesOf(hn);
                        var hnGhostData = hnGhosts.Select(g =>
                        {
                            var gLinks = GetLinksGoingTo(g);
                            return (Ghost: g, Links: gLinks, MultiInfo: BuildMultiLinkRestoreInfo(gLinks, g));
                        }).ToList();
                        return (Node: hn, OtherIncoming: hnLinks, MultiInfo: hnMulti, GhostData: hnGhostData);
                    })
                    .ToList();

                void RemoveIncoming()
                    => RemoveIncomingLinks(incomingLinks, instance, multiLinkRestoreInfo);

                void RestoreIncoming()
                    => RestoreIncomingLinks(incomingLinks, instance, multiLinkRestoreInfo);

                void RemoveHiddenCascade()
                {
                    foreach (var (hn, hnLinks, hnMulti, hnGhostData) in hiddenCascadeData)
                    {
                        foreach (var (ghost, gLinks, gMulti) in hnGhostData)
                        {
                            RemoveIncomingLinks(gLinks, ghost, gMulti);
                            ghost.ContainedWithin!.RemoveGhostNode(ghost, out _);
                        }
                        RemoveIncomingLinks(hnLinks, hn, hnMulti);
                        boundary.RemoveNode(hn, out _);
                    }
                }

                void RestoreHiddenCascade()
                {
                    foreach (var (hn, hnLinks, hnMulti, hnGhostData) in hiddenCascadeData)
                    {
                        boundary.AddNode(hn, out _);
                        RestoreIncomingLinks(hnLinks, hn, hnMulti);
                        foreach (var (ghost, gLinks, gMulti) in hnGhostData)
                        {
                            ghost.ContainedWithin!.AddGhostNode(ghost, out _);
                            RestoreIncomingLinks(gLinks, ghost, gMulti);
                        }
                    }
                }

                // Remove incoming links, then outgoing links, then hidden embedded nodes.
                RemoveIncoming();
                foreach (var link in outgoingLinks)
                    boundary.RemoveLink(link, out _);
                RemoveHiddenCascade();

                if (boundary.RemoveFunctionInstance(instance, out error))
                {
                    Buffer.AddUndo(new Command(() =>
                    {
                        // Undo: restore the FI first, then hidden nodes, then outgoing links, then incoming.
                        if (boundary.AddFunctionInstance(instance, out var e))
                        {
                            RestoreHiddenCascade();
                            foreach (var link in outgoingLinks)
                                boundary.AddLink(link, out _);
                            RestoreIncoming();
                            return (true, null);
                        }
                        return (false, e);
                    }, () =>
                    {
                        // Redo: repeat the original removal sequence.
                        RemoveIncoming();
                        foreach (var link in outgoingLinks)
                            boundary.RemoveLink(link, out _);
                        RemoveHiddenCascade();
                        return (boundary.RemoveFunctionInstance(instance, out var e), e);
                    }));
                    return true;
                }
                else
                {
                    // FI removal failed; roll back all link removals.
                    RestoreHiddenCascade();
                    foreach (var link in outgoingLinks)
                        boundary.AddLink(link, out _);
                    RestoreIncoming();
                    return false;
                }
            }
        }

        /// <summary>
        /// Moves <paramref name="instance"/> from its current boundary to
        /// <paramref name="targetBoundary"/> with full undo/redo support.
        /// Outgoing <see cref="FunctionParameterHook"/> links and any inlined hidden
        /// parameter nodes travel with the instance.
        /// The operation fails when the destination boundary is in a different
        /// <see cref="FunctionTemplate"/> scope from the template's parent boundary.
        /// </summary>
        public bool MoveFunctionInstanceToBoundary(User user, FunctionInstance instance,
            Boundary targetBoundary, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(instance);
            ArgumentNullException.ThrowIfNull(targetBoundary);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }

                var oldBoundary = instance.ContainedWithin!;
                if (ReferenceEquals(oldBoundary, targetBoundary)) { error = null; return true; }

                // The destination must be in the same scope as the template.
                var destScope = FindOwningFunctionTemplate(ModelSystem.GlobalBoundary, targetBoundary);
                var tmplScope = FindOwningFunctionTemplate(ModelSystem.GlobalBoundary, instance.Template.Parent);
                if (!ReferenceEquals(destScope, tmplScope))
                {
                    string destDescription = destScope is null
                        ? "the global scope"
                        : $"FunctionTemplate '{destScope.Name}'";
                    string tmplDescription = tmplScope is null
                        ? "the global scope"
                        : $"FunctionTemplate '{tmplScope.Name}'";
                    error = new CommandError(
                        $"Cannot move FunctionInstance '{instance.Name}': " +
                        $"the destination is in {destDescription} but the template " +
                        $"'{instance.Template.Name}' lives in {tmplDescription}.");
                    return false;
                }

                // Outgoing links: the FI is the origin (FunctionParameterHook connections).
                // These are stored in the FI's boundary and must travel with it.
                var outgoingLinks = oldBoundary.Links.Where(l => l.Origin == instance).ToList();

                // Hidden inlined parameter nodes that are destinations of outgoing links.
                var hiddenNodes = outgoingLinks
                    .SelectMany<Link, Node>(l =>
                        l is SingleLink sl && sl.Destination is not null ? new[] { sl.Destination }
                        : l is MultiLink ml ? ml.Destinations.ToArray()
                        : Array.Empty<Node>())
                    .Where(n => n.Location.Equals(Rectangle.Hidden)
                             && ReferenceEquals(n.ContainedWithin, oldBoundary))
                    .Distinct()
                    .ToList();

                // Remove from old boundary.
                foreach (var link in outgoingLinks)
                    oldBoundary.RemoveLink(link, out _);
                foreach (var hidden in hiddenNodes)
                    oldBoundary.RemoveNode(hidden, out _);

                if (!oldBoundary.RemoveFunctionInstance(instance, out error))
                {
                    // Roll back.
                    foreach (var hidden in hiddenNodes)
                        oldBoundary.AddNode(hidden, out _);
                    foreach (var link in outgoingLinks)
                        oldBoundary.AddLink(link, out _);
                    return false;
                }

                // Update ContainedWithin and add to new boundary.
                instance.UpdateContainedWithin(targetBoundary);
                foreach (var hidden in hiddenNodes)
                    hidden.UpdateContainedWithin(targetBoundary);

                if (!targetBoundary.AddFunctionInstance(instance, out error))
                {
                    // Roll back.
                    instance.UpdateContainedWithin(oldBoundary);
                    foreach (var hidden in hiddenNodes)
                        hidden.UpdateContainedWithin(oldBoundary);
                    oldBoundary.AddFunctionInstance(instance, out _);
                    foreach (var hidden in hiddenNodes)
                        oldBoundary.AddNode(hidden, out _);
                    foreach (var link in outgoingLinks)
                        oldBoundary.AddLink(link, out _);
                    return false;
                }

                foreach (var hidden in hiddenNodes)
                    targetBoundary.AddNode(hidden, out _);
                foreach (var link in outgoingLinks)
                    targetBoundary.AddLink(link, out _);

                Buffer.AddUndo(new Command(() =>
                {
                    // Undo: move back to oldBoundary.
                    foreach (var link in outgoingLinks) targetBoundary.RemoveLink(link, out _);
                    foreach (var hidden in hiddenNodes)
                    {
                        targetBoundary.RemoveNode(hidden, out _);
                        hidden.UpdateContainedWithin(oldBoundary);
                    }
                    targetBoundary.RemoveFunctionInstance(instance, out _);
                    instance.UpdateContainedWithin(oldBoundary);
                    oldBoundary.AddFunctionInstance(instance, out _);
                    foreach (var hidden in hiddenNodes)
                        oldBoundary.AddNode(hidden, out _);
                    foreach (var link in outgoingLinks)
                        oldBoundary.AddLink(link, out _);
                    return (true, null);
                }, () =>
                {
                    // Redo: move to targetBoundary again.
                    foreach (var link in outgoingLinks) oldBoundary.RemoveLink(link, out _);
                    foreach (var hidden in hiddenNodes)
                    {
                        oldBoundary.RemoveNode(hidden, out _);
                        hidden.UpdateContainedWithin(targetBoundary);
                    }
                    oldBoundary.RemoveFunctionInstance(instance, out _);
                    instance.UpdateContainedWithin(targetBoundary);
                    targetBoundary.AddFunctionInstance(instance, out _);
                    foreach (var hidden in hiddenNodes)
                        targetBoundary.AddNode(hidden, out _);
                    foreach (var link in outgoingLinks)
                        targetBoundary.AddLink(link, out _);
                    return (true, null);
                }));

                error = null;
                return true;
            }
        }

        /// <summary>
        /// Renames <paramref name="instance"/>.
        /// </summary>
        public bool RenameFunctionInstance(User user, FunctionInstance instance, string newName,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(instance);
            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var oldName = instance.Name;
                if (!instance.SetName(newName, out error))
                    return false;
                Buffer.AddUndo(new Command(() =>
                {
                    instance.SetName(oldName, out _);
                    return (true, null);
                }, () =>
                {
                    instance.SetName(newName, out _);
                    return (true, null);
                }));
                return true;
            }
        }

        /// <summary>
        /// Moves / resizes a <see cref="FunctionInstance"/> on the canvas.
        /// </summary>
        public bool SetFunctionInstanceLocation(User user, FunctionInstance instance, Rectangle newLocation,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(instance);
            error = null;
            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var oldLocation = instance.Location;
                instance.SetLocation(newLocation);
                Buffer.AddUndo(new Command(() =>
                {
                    instance.SetLocation(oldLocation);
                    return (true, null);
                }, () =>
                {
                    instance.SetLocation(newLocation);
                    return (true, null);
                }));
                return true;
            }
        }

        /// <summary>
        /// Moves a heterogeneous collection of canvas elements as a single undoable operation.
        /// All supplied moves are applied and recorded in one <see cref="CommandBatch"/> so that
        /// a single undo reverses the entire group drag.
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="nodeMoves">Node / Start / GhostNode / FunctionParameter moves (all are <see cref="Node"/> subclasses).</param>
        /// <param name="commentMoves">Comment-block moves.</param>
        /// <param name="templateMoves">Function-template moves.</param>
        /// <param name="instanceMoves">Function-instance moves.</param>
        /// <param name="error">An error message if the operation fails.</param>
        /// <returns><c>true</c> on success; <c>false</c> with an error on failure.</returns>
        public bool MoveElements(
            User user,
            IReadOnlyList<(Node node, Rectangle newLocation)>? nodeMoves,
            IReadOnlyList<(CommentBlock block, Rectangle newLocation)>? commentMoves,
            IReadOnlyList<(FunctionTemplate template, Rectangle newLocation)>? templateMoves,
            IReadOnlyList<(FunctionInstance instance, Rectangle newLocation)>? instanceMoves,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            error = null;
            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }

                var batch = new CommandBatch();

                if (nodeMoves is not null)
                {
                    foreach (var (node, newLoc) in nodeMoves)
                    {
                        var oldLoc = node.Location;
                        node.SetLocation(newLoc);
                        var n = node; var o = oldLoc; var nl = newLoc;
                        batch.Add(new Command(
                            () => { n.SetLocation(o);  return (true, null); },
                            () => { n.SetLocation(nl); return (true, null); }));
                    }
                }

                if (commentMoves is not null)
                {
                    foreach (var (block, newLoc) in commentMoves)
                    {
                        var oldLoc = block.Location;
                        block.Location = newLoc;
                        var b = block; var o = oldLoc; var nl = newLoc;
                        batch.Add(new Command(
                            () => { b.Location = o;  return (true, null); },
                            () => { b.Location = nl; return (true, null); }));
                    }
                }

                if (templateMoves is not null)
                {
                    foreach (var (template, newLoc) in templateMoves)
                    {
                        var oldLoc = template.Location;
                        template.SetLocation(newLoc);
                        var t = template; var o = oldLoc; var nl = newLoc;
                        batch.Add(new Command(
                            () => { t.SetLocation(o);  return (true, null); },
                            () => { t.SetLocation(nl); return (true, null); }));
                    }
                }

                if (instanceMoves is not null)
                {
                    foreach (var (instance, newLoc) in instanceMoves)
                    {
                        var oldLoc = instance.Location;
                        instance.SetLocation(newLoc);
                        var inst = instance; var o = oldLoc; var nl = newLoc;
                        batch.Add(new Command(
                            () => { inst.SetLocation(o);  return (true, null); },
                            () => { inst.SetLocation(nl); return (true, null); }));
                    }
                }

                Buffer.AddUndo(batch);
                return true;
            }
        }

        /// <summary>
        /// Create a model system session to use for a run
        /// </summary>
        /// <param name="runtime">The XTMF runtime the run will occur in</param>
        /// <returns></returns>
        internal static ModelSystemSession CreateRunSession(ProjectSession session, ModelSystem modelSystem)        {
            return new ModelSystemSession(session, modelSystem);
        }

        /// <summary>
        /// Undo the previous command.
        /// </summary>
        /// <param name="user">The user requesting the undo.</param>
        /// <param name="error">An error message if the undo fails.</param>
        /// <returns>True if the undo succeeds, false otherwise with an error message.</returns>
        public bool Undo(User user, out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                return Buffer.UndoCommands(out error);
            }
        }

        /// <summary>
        /// Redo the previously undon command.
        /// </summary>
        /// <param name="user">The user requesting the redo.</param>
        /// <param name="error">An error message if the redo fails.</param>
        /// <returns>True if the redo succeeds, false otherwise with an error message.</returns>
        public bool Redo(User user, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);

            if (!_session.HasAccess(user))
            {
                error = new CommandError("The user does not have access to this project.", true);
                return false;
            }
            return Buffer.RedoCommands(out error);
        }

        /// <summary>True when there is at least one command that can be undone.</summary>
        public bool CanUndo => Buffer.CanUndo;

        /// <summary>Revision of the model edits made during this session.</summary>
        public long ChangeCount => Buffer.ChangeCount;

        /// <summary>True when there is at least one command that can be redone.</summary>
        public bool CanRedo => Buffer.CanRedo;

        /// <summary>
        /// Remove a single destination of a MultiLink
        /// </summary>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="multiLink">The multi link to operate on</param>
        /// <param name="index">The index to remove</param>
        /// <param name="error">The error message if the operation fails.</param>
        /// <returns>True if successful, false otherwise with error message.</returns>
        /// <summary>
        /// Moves a destination within a multi-link from one index to another.
        /// </summary>
        public bool MoveLinkDestination(User user, Link multiLink, int fromIndex, int toIndex, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(multiLink);
            error = null;

            if (multiLink is not MultiLink ml)
            {
                error = new CommandError("The link is not a multi-link!");
                return false;
            }

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }
                var count = ml.Destinations.Count;
                if (fromIndex < 0 || fromIndex >= count || toIndex < 0 || toIndex >= count)
                {
                    error = new CommandError("Index is out of bounds!");
                    return false;
                }
                ml.MoveDestination(fromIndex, toIndex);
                int _from = fromIndex, _to = toIndex;
                Buffer.AddUndo(new Command(
                    () => { ml.MoveDestination(_to, _from); return (true, null); },
                    () => { ml.MoveDestination(_from, _to); return (true, null); }));
                return true;
            }
        }

        public bool RemoveLinkDestination(User user, Link multiLink, int index, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(multiLink);
            error = null;

            if (multiLink is MultiLink ml)
            {
                lock (_sessionLock)
                {
                    if (!_session.HasAccess(user))
                    {
                        error = new CommandError("The user does not have access to this project.", true);
                        return false;
                    }
                    var dests = ml.Destinations;
                    if (index >= dests.Count || index < 0)
                    {
                        error = new CommandError("The index is out of bounds!");
                        return false;
                    }
                    var toRemove = dests[index];
                    bool wasHidden = ml.IsDestinationHidden(index);
                    ml.RemoveDestination(index);
                    Buffer.AddUndo(new Command(() =>
                    {
                        return (ml.AddDestination(toRemove, index, wasHidden, out var e), e);
                    }, () =>
                    {
                        ml.RemoveDestination(index);
                        return (true, null);
                    }));
                    return true;
                }
            }
            else
            {
                error = new CommandError("The link was not a multi-link!");
                return false;
            }
        }

        // ── ExtractToFunctionTemplate ─────────────────────────────────────

        /// <summary>
        /// Extracts a set of nodes from <paramref name="boundary"/> into a newly created
        /// <see cref="FunctionTemplate"/> and places a <see cref="FunctionInstance"/> of that
        /// template in the same boundary in-place.
        /// </summary>
        /// <remarks>
        /// <para>
        /// All nodes in <paramref name="selectedNodes"/> must reside in
        /// <paramref name="boundary"/>.  Exactly one link must come <em>from outside</em> the
        /// selection into the selection; that link's destination node becomes the template's
        /// <see cref="FunctionTemplate.EntryNode"/>.
        /// </para>
        /// <para>
        /// Every SingleLink whose origin is inside the selection and whose destination is
        /// <em>outside</em> the selection (and not a Rectangle.Hidden inline-parameter node)
        /// is converted into a <see cref="FunctionParameter"/> on the new template.
        /// The corresponding <see cref="FunctionInstance"/> hook is then wired to the original
        /// external destination.
        /// </para>
        /// <para>The entire operation is registered as a single undoable command.</para>
        /// </remarks>
        /// <param name="user">The user issuing the command.</param>
        /// <param name="boundary">The boundary that contains the selected nodes.</param>
        /// <param name="selectedNodes">The nodes to extract. Must all reside in <paramref name="boundary"/>.</param>
        /// <param name="functionTemplateName">Name for the new <see cref="FunctionTemplate"/>. Must be unique in the model system.</param>
        /// <param name="functionInstanceName">Name for the new <see cref="FunctionInstance"/>.</param>
        /// <param name="functionTemplateLocation">Canvas location for the function template box.</param>
        /// <param name="functionInstanceLocation">Canvas location for the function instance.</param>
        /// <param name="functionTemplate">The newly created template on success.</param>
        /// <param name="functionInstance">The newly created instance on success.</param>
        /// <param name="error">Error description when the method returns <c>false</c>.</param>
        /// <returns><c>true</c> on success; <c>false</c> with a populated <paramref name="error"/> on failure.</returns>
        public bool ExtractToFunctionTemplate(
            User user,
            Boundary boundary,
            IReadOnlyList<Node> selectedNodes,
            string functionTemplateName,
            string functionInstanceName,
            Rectangle functionTemplateLocation,
            Rectangle functionInstanceLocation,
            out FunctionTemplate? functionTemplate,
            out FunctionInstance? functionInstance,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(boundary);
            ArgumentNullException.ThrowIfNull(selectedNodes);
            functionTemplate = null;
            functionInstance = null;

            if (selectedNodes.Count == 0)
            {
                error = new CommandError("At least one node must be selected for extraction.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(functionTemplateName))
            {
                error = new CommandError("A function template name must not be empty.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(functionInstanceName))
            {
                error = new CommandError("A function instance name must not be empty.");
                return false;
            }

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }

                var selectedSet = new HashSet<Node>();
                foreach (var n in selectedNodes)
                    selectedSet.Add(n);

                // ── Validate all selected nodes live in the specified boundary ──
                foreach (var n in selectedNodes)
                {
                    if (!ReferenceEquals(n.ContainedWithin, boundary))
                    {
                        error = new CommandError(
                            $"Node '{n.Name}' does not reside in boundary '{boundary.Name}'.");
                        return false;
                    }
                    if (n is FunctionParameter)
                    {
                        error = new CommandError(
                            $"Node '{n.Name}' is a FunctionParameter and cannot be extracted.");
                        return false;
                    }
                }

                // ── Classify links ─────────────────────────────────────────────
                // External incoming: origin outside selection, destination inside selection.
                // Supports both SingleLink and MultiLink originating from outside the selection.
                // Each entry: (originalDestNode, redirectDelegate) where redirectDelegate(newDest)
                // redirects that particular connection to newDest.
                var incomingRedirects = new List<(Node OriginalDest, Action<Node> Redirect)>();

                foreach (var link in boundary.Links)
                {
                    if (selectedSet.Contains(link.Origin)) continue;

                    if (link is SingleLink inSl && selectedSet.Contains(inSl.Destination))
                    {
                        var capturedSl = inSl;
                        incomingRedirects.Add((inSl.Destination,
                            dest => capturedSl.SetDestination(dest, out _)));
                    }
                    else if (link is MultiLink inMl)
                    {
                        // Each destination inside the selection is an individual redirect.
                        // Replacing at the same index (remove + insert at i) doesn't shift other indices.
                        for (int mlDi = 0; mlDi < inMl.Destinations.Count; mlDi++)
                        {
                            if (!selectedSet.Contains(inMl.Destinations[mlDi])) continue;
                            var capturedMl   = inMl;
                            var capturedIdx  = mlDi;
                            var capturedDest = inMl.Destinations[mlDi];
                            incomingRedirects.Add((capturedDest, dest =>
                            {
                                capturedMl.RemoveDestination(capturedIdx);
                                capturedMl.AddDestination(dest, capturedIdx, out _);
                            }));
                        }
                    }
                }

                // All external incoming edges must lead to the same selected node (the entry node).
                var distinctEntries = incomingRedirects.Select(r => r.OriginalDest).Distinct().ToList();
                if (distinctEntries.Count == 0)
                {
                    error = new CommandError("Extraction requires at least one external incoming link.");
                    return false;
                }
                if (distinctEntries.Count > 1)
                {
                    error = new CommandError(
                        $"Extraction requires all external incoming links to target the same selected node, " +
                        $"but {distinctEntries.Count} different destination nodes were found.");
                    return false;
                }

                var entryNode = distinctEntries[0];

                // Reject mixed MultiLinks that cross the selection boundary (some dests in, some out).
                var ambiguousMulti = boundary.Links
                    .Where(l => l is MultiLink ml
                                && selectedSet.Contains(l.Origin)
                                && ml.Destinations.Any(d => selectedSet.Contains(d))
                                && ml.Destinations.Any(d => !selectedSet.Contains(d)))
                    .ToList();
                if (ambiguousMulti.Count > 0)
                {
                    error = new CommandError(
                        "Extraction is not supported when a MultiLink has destinations both " +
                        "inside and outside the selection.");
                    return false;
                }

                // Reject fully-external MultiLinks (all dests outside): they are ambiguous as FPs.
                var externalMultiLinks = boundary.Links
                    .Where(l => l is MultiLink ml2
                                && selectedSet.Contains(l.Origin)
                                && ml2.Destinations.All(d => !selectedSet.Contains(d)))
                    .ToList();
                if (externalMultiLinks.Count > 0)
                {
                    error = new CommandError(
                        "Extraction is not supported when a MultiLink exits the selection entirely. " +
                        "Replace it with individual SingleLinks before extracting.");
                    return false;
                }

                // External outgoing SingleLinks: origin inside, destination outside, not a hidden child.
                var externalOutgoing = boundary.Links
                    .Where(l => l is SingleLink sl2
                                && selectedSet.Contains(l.Origin)
                                && !selectedSet.Contains(sl2.Destination)
                                && !sl2.Destination.Location.Equals(Rectangle.Hidden))
                    .Cast<SingleLink>()
                    .ToList();

                // ── Validate FunctionTemplate name uniqueness ──────────────────
                if (ModelSystem.GlobalBoundary.ContainsFunctionTemplateName(functionTemplateName))
                {
                    error = new CommandError(
                        $"A FunctionTemplate named '{functionTemplateName}' already exists in the model system.");
                    return false;
                }

                // ══════════════════════════════════════════════════════════════
                // Perform the extraction (no more early returns with error after
                // this point -- all validation is done above).
                // ══════════════════════════════════════════════════════════════

                // Step 1 – Create FunctionTemplate.
                if (!boundary.AddFunctionTemplate(functionTemplateName, out var ft, out error))
                    return false;
                ft!.SetLocation(functionTemplateLocation);
                var internalBoundary = ft.InternalModules;

                    // Step 2 – Set entry node so the FunctionInstance has the correct type when added.
                    ft.SetEntryNode(entryNode);

                    // Step 3 – Create FunctionInstance and redirect incoming links BEFORE removing
                    //          any nodes from the boundary.  This ensures that when the incoming
                    //          MultiLink/SingleLink ObservableCollection events fire, both the old
                    //          destination (still in boundary) and the new one (fi) are resolvable
                    //          by the canvas VM, preventing a stale extra arrowhead.
                    boundary.AddFunctionInstance(functionInstanceName, ft, functionInstanceLocation,
                        out var fi, out _);
                    foreach (var (_, redirect) in incomingRedirects)
                        redirect(fi!);

                    // Step 4 – Remove external outgoing links from boundary so they don't follow
                    //          their origin nodes into InternalModules during the move.
                    foreach (var link in externalOutgoing)
                        boundary.RemoveLink(link, out _);

                    // Step 5 – Move each selected node (and its hidden inline children) to
                    //          InternalModules. Their remaining outgoing links (internal ones
                    //          plus links to hidden children) also move.
                var moveInfo = new List<(Node Node, List<Node> HiddenChildren, List<Link> MovedLinks)>();
                foreach (var node in selectedNodes)
                {
                    // Collect outgoing links still present in boundary (external ones were removed above).
                    var nodeOutgoing = boundary.Links.Where(l => l.Origin == node).ToList();

                    // Collect hidden inline-parameter children.
                    var hiddenChildrenSet = new HashSet<Node>();
                    foreach (var lk in nodeOutgoing)
                    {
                        IEnumerable<Node> dests = lk is SingleLink slH && slH.Destination is not null
                            ? new[] { slH.Destination }
                            : lk is MultiLink mlH
                                ? (IEnumerable<Node>)mlH.Destinations
                                : Array.Empty<Node>();
                        foreach (var dn in dests)
                        {
                            if (dn.Location.Equals(Rectangle.Hidden)
                                && ReferenceEquals(dn.ContainedWithin, boundary))
                                hiddenChildrenSet.Add(dn);
                        }
                    }
                    var hiddenChildren = hiddenChildrenSet.ToList();

                    // Remove links from boundary.
                    foreach (var lk in nodeOutgoing)
                        boundary.RemoveLink(lk, out _);

                    // Move node and hidden children.
                    boundary.RemoveNode(node, out _);
                    node.UpdateContainedWithin(internalBoundary);
                    internalBoundary.AddNode(node, out _);

                    foreach (var hc in hiddenChildren)
                    {
                        boundary.RemoveNode(hc, out _);
                        hc.UpdateContainedWithin(internalBoundary);
                        internalBoundary.AddNode(hc, out _);
                    }

                    // Re-add outgoing links to InternalModules.
                    foreach (var lk in nodeOutgoing)
                        internalBoundary.AddLink(lk, out _);

                    moveInfo.Add((node, hiddenChildren, nodeOutgoing));
                }

                 // Compute bounding box of selected nodes so we can place
                // FunctionParameters visibly above them, centred horizontally.
                const float FpWidth  = 120f;
                const float FpHeight =  50f;
                const float FpGap    =  20f;
                float bboxMinX = float.MaxValue, bboxMaxX = float.MinValue, bboxMinY = float.MaxValue;
                foreach (var node in selectedNodes)
                {
                    var loc = node.Location;
                    if (loc.Equals(Rectangle.Hidden)) continue;
                    if (loc.X         < bboxMinX) bboxMinX = loc.X;
                    if (loc.X + loc.Width  > bboxMaxX) bboxMaxX = loc.X + loc.Width;
                    if (loc.Y         < bboxMinY) bboxMinY = loc.Y;
                }
                // Fallback when every selected node is hidden / has no location.
                if (bboxMinX == float.MaxValue) { bboxMinX = 0f; bboxMaxX = 120f; bboxMinY = 100f; }
                int fpCount = externalOutgoing.Count;
                float totalFpWidth = fpCount * FpWidth + MathF.Max(0, fpCount - 1) * FpGap;
                float fpStartX     = (bboxMinX + bboxMaxX) / 2f - totalFpWidth / 2f;
                float fpY          = MathF.Max(0f, bboxMinY - FpHeight - 40f);

                // Step 6 – Create FunctionParameters and internal FP-destination links.
                var usedFpNames = new HashSet<string>(StringComparer.Ordinal);
                var fpData = new List<(FunctionParameter Fp, SingleLink ExternalLink, SingleLink InternalFpLink)>();
                int fpIndex = 0;
                foreach (var extLink in externalOutgoing)
                {
                    var hookType = extLink.OriginHook.Type;
                    var fpName   = extLink.OriginHook.Name;

                    // Ensure name uniqueness within the template.
                    if (usedFpNames.Contains(fpName))
                    {
                        int suffix = 1;
                        while (usedFpNames.Contains($"{fpName}_{suffix}")) suffix++;
                        fpName = $"{fpName}_{suffix}";
                    }
                    usedFpNames.Add(fpName);

                    var fpRect = new Rectangle(fpStartX + fpIndex * (FpWidth + FpGap), fpY, FpWidth, FpHeight);
                    ft.AddFunctionParameter(fpName, hookType, fpRect, out var fp, out _);
                    fpIndex++;
                    var internalFpLink = new SingleLink(extLink.Origin, extLink.OriginHook, fp!, false);
                    internalBoundary.AddLink(internalFpLink, out _);
                    fpData.Add((fp!, extLink, internalFpLink));
                }

                 // Step 7 – Create links from the FunctionInstance's hooks to the external destinations.
                var fiOutgoingLinks = new List<SingleLink>();
                foreach (var (fp, extLink, _) in fpData)
                {
                    var fiHook = fi!.Hooks
                        .OfType<FunctionParameterHook>()
                        .FirstOrDefault(h => ReferenceEquals(h.Parameter, fp));
                    if (fiHook is not null)
                    {
                        var fiLink = new SingleLink(fi, fiHook, extLink.Destination, false);
                        boundary.AddLink(fiLink, out _);
                        fiOutgoingLinks.Add(fiLink);
                    }
                }

                functionTemplate = ft;
                functionInstance = fi!;
                error = null;

                // ── Register a single undo/redo command ────────────────────────
                var capturedFt              = ft;
                var capturedFi              = fi!;
                var capturedEntryNode       = entryNode;
                var capturedIncomingRedirects = incomingRedirects;
                var capturedFpData          = fpData;
                var capturedMoveInfo        = moveInfo;
                var capturedExternalOut     = externalOutgoing;
                var capturedFiOutgoing      = fiOutgoingLinks;

                Buffer.AddUndo(new Command(
                    // ── Undo ──
                    () =>
                    {
                        // 1. Remove fi→external links.
                        foreach (var lk in capturedFiOutgoing)
                            boundary.RemoveLink(lk, out _);

                        // 2. Move nodes back from InternalModules to boundary FIRST so that
                        //    when SetDestination fires PropertyChanged below, the destination
                        //    node VM already exists in the boundary and the link can bind correctly.
                        //    Pass 1: move all nodes (and their hidden children) back first so that
                        //    every NodeViewModel exists before any link tries to resolve its destination.
                        foreach (var (node, hiddenChildren, movedLinks) in capturedMoveInfo)
                        {
                            // Remove outgoing links from InternalModules.
                            foreach (var lk in movedLinks)
                                internalBoundary.RemoveLink(lk, out _);

                            // Move node and hidden children back.
                            internalBoundary.RemoveNode(node, out _);
                            node.UpdateContainedWithin(boundary);
                            boundary.AddNode(node, out _);

                            foreach (var hc in hiddenChildren)
                            {
                                internalBoundary.RemoveNode(hc, out _);
                                hc.UpdateContainedWithin(boundary);
                                boundary.AddNode(hc, out _);
                            }
                        }

                        //    Pass 2: now that all destination NodeViewModels exist in the boundary,
                        //    re-add the links so TryAddLinkViewModel can resolve both endpoints.
                        foreach (var (_, _, movedLinks) in capturedMoveInfo)
                            foreach (var lk in movedLinks)
                                boundary.AddLink(lk, out _);

                        // 3. Re-add the original external outgoing links to boundary.
                        foreach (var lk in capturedExternalOut)
                            boundary.AddLink(lk, out _);

                        // 4. Restore external incoming link/destination (fires PropertyChanged;
                        //    capturedEntryNode is now in the boundary so the VM resolves correctly).
                        foreach (var (origDest, redirect) in capturedIncomingRedirects)
                            redirect(origDest);

                        // 5. Clear the entry node.
                        capturedFt.SetEntryNode(null);

                        // 6. Remove FunctionInstance.
                        boundary.RemoveFunctionInstance(capturedFi, out _);

                        // 7. Remove internal FP-destination links and FunctionParameters.
                        foreach (var (fp, _, internalFpLink) in capturedFpData)
                        {
                            internalBoundary.RemoveLink(internalFpLink, out _);
                            capturedFt.RemoveFunctionParameter(fp, out _);
                        }

                        // 8. Remove the FunctionTemplate.
                        boundary.RemoveFunctionTemplate(capturedFt, out _);

                        return (true, null);
                    },
                    // ── Redo ──
                    () =>
                    {
                        // Re-add FunctionTemplate.
                        boundary.AddFunctionTemplate(capturedFt, out _);
                        capturedFt.SetLocation(functionTemplateLocation);

                            // Re-add FunctionInstance and set entry node, then redirect incoming
                            // links BEFORE moving nodes (mirrors the corrected forward-direction order
                            // so that stale MultiLink arrowheads are properly cleaned up on redo too).
                            boundary.AddFunctionInstance(capturedFi, out _);
                            capturedFt.SetEntryNode(capturedEntryNode);
                            foreach (var (_, redirect) in capturedIncomingRedirects)
                                redirect(capturedFi);

                            // Remove external outgoing links.
                            foreach (var lk in capturedExternalOut)
                                boundary.RemoveLink(lk, out _);

                            // Move nodes to InternalModules.
                        foreach (var (node, hiddenChildren, movedLinks) in capturedMoveInfo)
                        {
                            foreach (var lk in movedLinks)
                                boundary.RemoveLink(lk, out _);

                            boundary.RemoveNode(node, out _);
                            node.UpdateContainedWithin(internalBoundary);
                            internalBoundary.AddNode(node, out _);

                            foreach (var hc in hiddenChildren)
                            {
                                boundary.RemoveNode(hc, out _);
                                hc.UpdateContainedWithin(internalBoundary);
                                internalBoundary.AddNode(hc, out _);
                            }

                            foreach (var lk in movedLinks)
                                internalBoundary.AddLink(lk, out _);
                        }

                        // Re-add FPs and internal FP links.
                        foreach (var (fp, _, internalFpLink) in capturedFpData)
                        {
                            capturedFt.RestoreFunctionParameter(fp, capturedFt.FunctionParameters.Count);
                            internalBoundary.AddLink(internalFpLink, out _);
                        }

                            // Re-add fi→external links.
                        foreach (var lk in capturedFiOutgoing)
                            boundary.AddLink(lk, out _);

                        return (true, null);
                    }
                ));

                return true;
            }
        }

        /// <summary>
        /// Export the model system to a file at the given path.
        /// </summary>
        /// <param name="user">The user performing the export.</param>
        /// <param name="exportPath">The path to export the model system to.</param>
        /// <param name="error">The error message if the export fails.</param>
        /// <returns>True if the export was successful, false otherwise with error message.</returns>
        /// <summary>
        /// Expands (inlines) a <see cref="FunctionInstance"/> back into the boundary it lives in.
        /// This is the inverse of <see cref="ExtractToFunctionTemplate"/>:
        /// internal nodes are moved out, links are reconnected, and the template + instance are removed.
        /// </summary>
        public bool ExpandFunctionInstance(
            User user,
            FunctionInstance instance,
            [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(instance);

            lock (_sessionLock)
            {
                if (!_session.HasAccess(user))
                {
                    error = new CommandError("The user does not have access to this project.", true);
                    return false;
                }

                var ft               = instance.Template;
                var boundary         = instance.ContainedWithin!;
                var internalBoundary = ft.InternalModules;

                if (ft.EntryNode is null)
                {
                    error = new CommandError(
                        $"Cannot expand '{instance.Name}': the template '{ft.Name}' has no entry node set.");
                    return false;
                }

                var allInstances = GetAllFunctionInstancesOf(ft);
                if (allInstances.Count > 1)
                {
                    error = new CommandError(
                        $"Cannot expand '{instance.Name}': template '{ft.Name}' has {allInstances.Count} instances. " +
                        $"Expansion is only supported when a single instance exists.");
                    return false;
                }

                var entryNode     = ft.EntryNode;
                var internalNodes = internalBoundary.Modules.ToList();

                // Partition internal links: those targeting a FunctionParameter vs. the rest.
                var allInternalLinks = internalBoundary.Links.ToList();
                var fpLinks  = allInternalLinks.OfType<SingleLink>()
                    .Where(sl => sl.Destination is FunctionParameter)
                    .ToList();
                var movedLinks = allInternalLinks.Except(fpLinks.Cast<Link>()).ToList();

                // FI outgoing links: fi → external via FunctionParameterHook.
                var fiOutgoingLinks = boundary.Links
                    .OfType<SingleLink>()
                    .Where(sl => ReferenceEquals(sl.Origin, instance))
                    .ToList();

                // Map each FunctionParameter to its external destination.
                var fpToExternal = new Dictionary<FunctionParameter, (NodeHook OriginHook, Node Dest)>();
                foreach (var sl in fiOutgoingLinks)
                {
                    if (sl.OriginHook is FunctionParameterHook fph)
                        fpToExternal[fph.Parameter] = (fph, sl.Destination);
                }

                // For each FP-internal link, build the replacement direct link.
                var newDirectLinks = new List<SingleLink>();
                foreach (var fpLink in fpLinks)
                {
                    if (fpLink.Destination is FunctionParameter fp
                        && fpToExternal.TryGetValue(fp, out var ext))
                    {
                        newDirectLinks.Add(new SingleLink(fpLink.Origin, fpLink.OriginHook, ext.Dest, false));
                    }
                }

                // Incoming links pointing at the FI (to be redirected to entryNode).
                var incomingToFi   = GetLinksGoingTo(instance);
                var multiLinkInfo  = BuildMultiLinkRestoreInfo(incomingToFi, instance);

                // ── Perform the expansion ─────────────────────────────────────────

                // 1. Remove fi→external (FP outgoing) links from boundary.
                foreach (var lk in fiOutgoingLinks)
                    boundary.RemoveLink(lk, out _);

                // 2. Remove FP-destination links from internalBoundary.
                foreach (var lk in fpLinks)
                    internalBoundary.RemoveLink(lk, out _);

                // 3. Remove non-FP links from internalBoundary (they move to boundary).
                foreach (var lk in movedLinks)
                    internalBoundary.RemoveLink(lk, out _);

                // 4. Move internal nodes to boundary.
                foreach (var node in internalNodes)
                {
                    internalBoundary.RemoveNode(node, out _);
                    node.UpdateContainedWithin(boundary);
                    boundary.AddNode(node, out _);
                }

                // 5. Re-add moved links to boundary.
                foreach (var lk in movedLinks)
                    boundary.AddLink(lk, out _);

                // 6. Add replacement direct links (bypass FP) to boundary.
                foreach (var lk in newDirectLinks)
                    boundary.AddLink(lk, out _);

                // 7. Redirect all incoming links from fi to entryNode.
                //    Do this only after entryNode is back in the boundary so the canvas
                //    can resolve both old and new destinations during link VM updates.
                foreach (var lk in incomingToFi)
                {
                    if (lk is SingleLink sl)
                    {
                        sl.SetDestination(entryNode, out _);
                    }
                    else if (lk is MultiLink ml && multiLinkInfo.TryGetValue(ml, out var entries))
                    {
                        foreach (var (idx, _) in entries)
                        {
                            ml.RemoveDestination(idx);
                            ml.AddDestination(entryNode, idx, out _);
                        }
                    }
                }

                // 8. Remove fi from boundary.
                boundary.RemoveFunctionInstance(instance, out _);

                // 9. Remove FunctionParameters from template, then remove template from boundary.
                var fpsToRestore = ft.FunctionParameters.ToList();
                foreach (var fp in fpsToRestore)
                    ft.RemoveFunctionParameter(fp, out _);
                boundary.RemoveFunctionTemplate(ft, out _);

                error = null;

                // ── Register undo/redo ─────────────────────────────────────────────
                var capturedFt            = ft;
                var capturedFi            = instance;
                var capturedEntryNode     = entryNode;
                var capturedInternalNodes = internalNodes;
                var capturedMovedLinks    = movedLinks;
                var capturedFpLinks       = fpLinks;
                var capturedNewDirect     = newDirectLinks;
                var capturedFiOutgoing    = fiOutgoingLinks;
                var capturedIncomingToFi  = incomingToFi;
                var capturedMultiInfo     = multiLinkInfo;
                var capturedFps           = fpsToRestore;

                Buffer.AddUndo(new Command(
                    // ── Undo: restore everything ──
                    () =>
                    {
                        // 1. Restore FunctionTemplate and FunctionParameters.
                        boundary.AddFunctionTemplate(capturedFt, out _);
                        foreach (var (fp, idx) in capturedFps.Select((fp, i) => (fp, i)))
                            capturedFt.RestoreFunctionParameter(fp, idx);

                        // 2. Re-add FunctionInstance to boundary.
                        boundary.AddFunctionInstance(capturedFi, out _);

                        // 3. Restore incoming links to point back at FI while entry is still
                        //    in the boundary so the canvas can resolve both endpoints.
                        RestoreIncomingLinks(capturedIncomingToFi, capturedFi, capturedMultiInfo);

                        // 4. Remove replacement direct links.
                        foreach (var lk in capturedNewDirect)
                            boundary.RemoveLink(lk, out _);

                        // 5. Remove moved links from boundary.
                        foreach (var lk in capturedMovedLinks)
                            boundary.RemoveLink(lk, out _);

                        // 6. Move nodes back to internalBoundary.
                        foreach (var node in capturedInternalNodes)
                        {
                            boundary.RemoveNode(node, out _);
                            node.UpdateContainedWithin(internalBoundary);
                            internalBoundary.AddNode(node, out _);
                        }

                        // 7. Re-add moved links to internalBoundary.
                        foreach (var lk in capturedMovedLinks)
                            internalBoundary.AddLink(lk, out _);

                        // 8. Re-add FP-destination links to internalBoundary.
                        foreach (var lk in capturedFpLinks)
                            internalBoundary.AddLink(lk, out _);

                        // 9. Re-add fi→external outgoing links.
                        foreach (var lk in capturedFiOutgoing)
                            boundary.AddLink(lk, out _);

                        return (true, null);
                    },
                    // ── Redo: re-apply expansion ──
                    () =>
                    {
                        // Mirror the forward direction exactly.

                        // 1. Remove fi→external links.
                        foreach (var lk in capturedFiOutgoing)
                            boundary.RemoveLink(lk, out _);

                        // 2. Move internals out to boundary first.
                        foreach (var lk in capturedFpLinks)
                            internalBoundary.RemoveLink(lk, out _);

                        foreach (var lk in capturedMovedLinks)
                            internalBoundary.RemoveLink(lk, out _);

                        foreach (var node in capturedInternalNodes)
                        {
                            internalBoundary.RemoveNode(node, out _);
                            node.UpdateContainedWithin(boundary);
                            boundary.AddNode(node, out _);
                        }

                        foreach (var lk in capturedMovedLinks)
                            boundary.AddLink(lk, out _);

                        foreach (var lk in capturedNewDirect)
                            boundary.AddLink(lk, out _);

                        // 3. Redirect incoming from FI to entry while both are in boundary.
                        foreach (var lk in capturedIncomingToFi)
                        {
                            if (lk is SingleLink sl)
                            {
                                sl.SetDestination(capturedEntryNode, out _);
                            }
                            else if (lk is MultiLink ml && capturedMultiInfo.TryGetValue(ml, out var entries))
                            {
                                foreach (var (idx, _) in entries)
                                {
                                    ml.RemoveDestination(idx);
                                    ml.AddDestination(capturedEntryNode, idx, out _);
                                }
                            }
                        }

                        // 4. Remove FI and then remove template metadata.
                        boundary.RemoveFunctionInstance(capturedFi, out _);

                        foreach (var fp in capturedFps)
                            capturedFt.RemoveFunctionParameter(fp, out _);
                        boundary.RemoveFunctionTemplate(capturedFt, out _);

                        return (true, null);
                    }
                ));

                return true;
            }
        }

        public bool ExportModelSystem(User user, string exportPath, [NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(exportPath);
            error = null;

            lock (_sessionLock)
            {
                return ModelSystemFile.ExportModelSystemFromSession(user, this, exportPath, out error);
            }
        }

        // ── Estimation group management ──────────────────────────────────────────

        /// <summary>Creates and adds a new estimation group with the given name.</summary>
        public bool AddEstimationGroup(User user, string name,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out EstimationGroup? group,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            group = null;
            if (!_session.HasAccess(user))
            { error = new CommandError("The user does not have access to this project.", true); return false; }
            lock (_sessionLock)
            {
                var newGroup = new EstimationGroup(name);
                ModelSystem.EstimationGroups.Add(newGroup);
                Buffer.AddUndo(new Command(
                    () => { ModelSystem.EstimationGroups.Remove(newGroup); return (true, null); },
                    () => { ModelSystem.EstimationGroups.Add(newGroup); return (true, null); }));
                group = newGroup;
                error = null;
                return true;
            }
        }

        /// <summary>Removes an estimation group (and all its parameters) from the model system.</summary>
        public bool RemoveEstimationGroup(User user, EstimationGroup group,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(group);
            if (!_session.HasAccess(user))
            { error = new CommandError("The user does not have access to this project.", true); return false; }
            lock (_sessionLock)
            {
                int idx = ModelSystem.EstimationGroups.IndexOf(group);
                if (idx < 0) { error = new CommandError("The estimation group was not found."); return false; }
                ModelSystem.EstimationGroups.RemoveAt(idx);
                Buffer.AddUndo(new Command(
                    () => { ModelSystem.EstimationGroups.Insert(idx, group); return (true, null); },
                    () => { ModelSystem.EstimationGroups.Remove(group); return (true, null); }));
                error = null;
                return true;
            }
        }

        /// <summary>Renames an estimation group.</summary>
        public bool RenameEstimationGroup(User user, EstimationGroup group, string newName,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(group);
            if (!_session.HasAccess(user))
            { error = new CommandError("The user does not have access to this project.", true); return false; }
            lock (_sessionLock)
            {
                var oldName = group.Name;
                group.Name = newName;
                Buffer.AddUndo(new Command(
                    () => { group.Name = oldName; return (true, null); },
                    () => { group.Name = newName; return (true, null); }));
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Sets (or clears) the single estimation fitness function node for the entire model system.
        /// <paramref name="fitnessNode"/> must implement
        /// <c>IFunction&lt;float&gt;</c> or <c>IFunction&lt;double&gt;</c>,
        /// or be <c>null</c> to clear the selection.
        /// </summary>
        public bool SetEstimationFitnessNode(User user, Node? fitnessNode,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            if (!_session.HasAccess(user))
            { error = new CommandError("The user does not have access to this project.", true); return false; }
            lock (_sessionLock)
            {
                var oldNode = ModelSystem.EstimationFitnessNode;
                ModelSystem.EstimationFitnessNode = fitnessNode;
                Buffer.AddUndo(new Command(
                    () => { ModelSystem.EstimationFitnessNode = oldNode; return (true, null); },
                    () => { ModelSystem.EstimationFitnessNode = fitnessNode; return (true, null); }));
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Sets the estimation algorithm configuration for the model system.
        /// This determines which search algorithm (Nelder-Mead, PSO, GA, etc.) is used
        /// and its hyperparameters during an estimation run.
        /// </summary>
        public bool SetEstimationAlgorithmConfig(User user, EstimationAlgorithmConfig config,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(config);
            if (!_session.HasAccess(user))
            { error = new CommandError("The user does not have access to this project.", true); return false; }
            lock (_sessionLock)
            {
                var oldConfig = ModelSystem.EstimationAlgorithmConfig;
                ModelSystem.EstimationAlgorithmConfig = config;
                Buffer.AddUndo(new Command(
                    () => { ModelSystem.EstimationAlgorithmConfig = oldConfig; return (true, null); },
                    () => { ModelSystem.EstimationAlgorithmConfig = config;    return (true, null); }));
                error = null;
                return true;
            }
        }

        /// <summary>Sets whether estimation minimises or maximises the fitness function.</summary>
        public bool SetEstimationObjective(User user, EstimationObjective objective,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            if (!_session.HasAccess(user))
            { error = new CommandError("The user does not have access to this project.", true); return false; }
            lock (_sessionLock)
            {
                var oldObjective = ModelSystem.EstimationObjective;
                ModelSystem.EstimationObjective = objective;
                Buffer.AddUndo(new Command(
                    () => { ModelSystem.EstimationObjective = oldObjective; return (true, null); },
                    () => { ModelSystem.EstimationObjective = objective;    return (true, null); }));
                error = null;
                return true;
            }
        }

        /// <summary>Enables or disables an estimation group.</summary>
        public bool SetEstimationGroupEnabled(User user, EstimationGroup group, bool isEnabled,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(group);
            if (!_session.HasAccess(user))
            { error = new CommandError("The user does not have access to this project.", true); return false; }
            lock (_sessionLock)
            {
                var oldValue = group.IsEnabled;
                group.IsEnabled = isEnabled;
                Buffer.AddUndo(new Command(
                    () => { group.IsEnabled = oldValue; return (true, null); },
                    () => { group.IsEnabled = isEnabled; return (true, null); }));
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Nominates a node for estimation within <paramref name="group"/>.
        /// </summary>
        public bool AddEstimationParameter(User user, EstimationGroup group,
            Node node, double min, double max, double nullHypothesis,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out EstimationEntry? entry,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(group);
            ArgumentNullException.ThrowIfNull(node);
            entry = null;
            if (!_session.HasAccess(user))
            { error = new CommandError("The user does not have access to this project.", true); return false; }
            lock (_sessionLock)
            {
                if (ModelSystem.EstimationGroups.Any(g => g.Parameters.Any(e => e.Node == node)))
                { error = new CommandError("This node is already nominated for estimation."); return false; }
                var newEntry = new EstimationEntry(node, min, max, nullHypothesis);
                group.Parameters.Add(newEntry);
                Buffer.AddUndo(new Command(
                    () => { group.Parameters.Remove(newEntry); return (true, null); },
                    () => { group.Parameters.Add(newEntry); return (true, null); }));
                entry = newEntry;
                error = null;
                return true;
            }
        }

        /// <summary>Removes an estimation entry from its group.</summary>
        public bool RemoveEstimationParameter(User user, EstimationGroup group, EstimationEntry entry,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(group);
            ArgumentNullException.ThrowIfNull(entry);
            if (!_session.HasAccess(user))
            { error = new CommandError("The user does not have access to this project.", true); return false; }
            lock (_sessionLock)
            {
                int idx = group.Parameters.IndexOf(entry);
                if (idx < 0) { error = new CommandError("The estimation entry was not found in the specified group."); return false; }
                group.Parameters.RemoveAt(idx);
                Buffer.AddUndo(new Command(
                    () => { group.Parameters.Insert(idx, entry); return (true, null); },
                    () => { group.Parameters.Remove(entry); return (true, null); }));
                error = null;
                return true;
            }
        }

        /// <summary>Updates the bounds and null-hypothesis of an existing estimation entry.</summary>
        public bool UpdateEstimationParameter(User user, EstimationEntry entry,
            double min, double max, double nullHypothesis, bool isEnabled,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(entry);
            if (!_session.HasAccess(user))
            { error = new CommandError("The user does not have access to this project.", true); return false; }
            lock (_sessionLock)
            {
                var oldMin = entry.Min; var oldMax = entry.Max;
                var oldNull = entry.NullHypothesis; var oldEnabled = entry.IsEnabled;
                entry.Min = min; entry.Max = max; entry.NullHypothesis = nullHypothesis; entry.IsEnabled = isEnabled;
                Buffer.AddUndo(new Command(
                    () => { entry.Min = oldMin; entry.Max = oldMax; entry.NullHypothesis = oldNull; entry.IsEnabled = oldEnabled; return (true, null); },
                    () => { entry.Min = min; entry.Max = max; entry.NullHypothesis = nullHypothesis; entry.IsEnabled = isEnabled; return (true, null); }));
                error = null;
                return true;
            }
        }

        // ── Calibration group management ─────────────────────────────────────────

        /// <summary>Creates and adds a new calibration group with the given name.</summary>
        public bool AddCalibrationGroup(User user, string name,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CalibrationGroup? group,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            group = null;
            if (!_session.HasAccess(user))
            { error = new CommandError("The user does not have access to this project.", true); return false; }
            lock (_sessionLock)
            {
                var newGroup = new CalibrationGroup(name);
                ModelSystem.CalibrationGroups.Add(newGroup);
                Buffer.AddUndo(new Command(
                    () => { ModelSystem.CalibrationGroups.Remove(newGroup); return (true, null); },
                    () => { ModelSystem.CalibrationGroups.Add(newGroup); return (true, null); }));
                group = newGroup;
                error = null;
                return true;
            }
        }

        /// <summary>Removes a calibration group (and all its parameters) from the model system.</summary>
        public bool RemoveCalibrationGroup(User user, CalibrationGroup group,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(group);
            if (!_session.HasAccess(user))
            { error = new CommandError("The user does not have access to this project.", true); return false; }
            lock (_sessionLock)
            {
                int idx = ModelSystem.CalibrationGroups.IndexOf(group);
                if (idx < 0) { error = new CommandError("The calibration group was not found."); return false; }
                ModelSystem.CalibrationGroups.RemoveAt(idx);
                Buffer.AddUndo(new Command(
                    () => { ModelSystem.CalibrationGroups.Insert(idx, group); return (true, null); },
                    () => { ModelSystem.CalibrationGroups.Remove(group); return (true, null); }));
                error = null;
                return true;
            }
        }

        /// <summary>Renames a calibration group.</summary>
        public bool RenameCalibrationGroup(User user, CalibrationGroup group, string newName,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(group);
            if (!_session.HasAccess(user))
            { error = new CommandError("The user does not have access to this project.", true); return false; }
            lock (_sessionLock)
            {
                var oldName = group.Name;
                group.Name = newName;
                Buffer.AddUndo(new Command(
                    () => { group.Name = oldName; return (true, null); },
                    () => { group.Name = newName; return (true, null); }));
                error = null;
                return true;
            }
        }

        /// <summary>Enables or disables a calibration group.</summary>
        public bool SetCalibrationGroupEnabled(User user, CalibrationGroup group, bool isEnabled,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(group);
            if (!_session.HasAccess(user))
            { error = new CommandError("The user does not have access to this project.", true); return false; }
            lock (_sessionLock)
            {
                var oldValue = group.IsEnabled;
                group.IsEnabled = isEnabled;
                Buffer.AddUndo(new Command(
                    () => { group.IsEnabled = oldValue; return (true, null); },
                    () => { group.IsEnabled = isEnabled; return (true, null); }));
                error = null;
                return true;
            }
        }

        /// <summary>Sets the default algorithm for new calibration entries in <paramref name="group"/>.</summary>
        public bool SetCalibrationGroupDefaultAlgorithm(User user, CalibrationGroup group,
            CalibrationAlgorithmBase algorithm,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(group);
            if (!_session.HasAccess(user))
            { error = new CommandError("The user does not have access to this project.", true); return false; }
            lock (_sessionLock)
            {
                var oldValue = group.DefaultAlgorithm;
                group.DefaultAlgorithm = algorithm;
                Buffer.AddUndo(new Command(
                    () => { group.DefaultAlgorithm = oldValue; return (true, null); },
                    () => { group.DefaultAlgorithm = algorithm; return (true, null); }));
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Nominates a node for calibration within <paramref name="group"/>.
        /// </summary>
        public bool AddCalibrationParameter(User user, CalibrationGroup group,
            Node node, double min, double max,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CalibrationEntry? entry,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out CommandError? error)
            => AddCalibrationParameter(user, group, node, min, max, 1.0, out entry, out error);

        /// <summary>
        /// Nominates a node for calibration within <paramref name="group"/>.
        /// </summary>
        public bool AddCalibrationParameter(User user, CalibrationGroup group,
            Node node, double min, double max, double stepSize,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CalibrationEntry? entry,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(group);
            ArgumentNullException.ThrowIfNull(node);
            entry = null;
            if (!_session.HasAccess(user))
            { error = new CommandError("The user does not have access to this project.", true); return false; }
            lock (_sessionLock)
            {
                if (ModelSystem.CalibrationGroups.Any(g => g.Parameters.Any(e => e.Node == node)))
                { error = new CommandError("This node is already nominated for calibration."); return false; }
                var newEntry = new CalibrationEntry(node, min, max, algorithm: group.DefaultAlgorithm, stepSize: stepSize);
                group.Parameters.Add(newEntry);
                Buffer.AddUndo(new Command(
                    () => { group.Parameters.Remove(newEntry); return (true, null); },
                    () => { group.Parameters.Add(newEntry); return (true, null); }));
                entry = newEntry;
                error = null;
                return true;
            }
        }

        /// <summary>Removes a calibration entry from its group.</summary>
        public bool RemoveCalibrationParameter(User user, CalibrationGroup group, CalibrationEntry entry,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(group);
            ArgumentNullException.ThrowIfNull(entry);
            if (!_session.HasAccess(user))
            { error = new CommandError("The user does not have access to this project.", true); return false; }
            lock (_sessionLock)
            {
                int idx = group.Parameters.IndexOf(entry);
                if (idx < 0) { error = new CommandError("The calibration entry was not found in the specified group."); return false; }
                group.Parameters.RemoveAt(idx);
                Buffer.AddUndo(new Command(
                    () => { group.Parameters.Insert(idx, entry); return (true, null); },
                    () => { group.Parameters.Remove(entry); return (true, null); }));
                error = null;
                return true;
            }
        }

        /// <summary>Updates the bounds, tolerance, and algorithm of an existing calibration entry.</summary>
        public bool UpdateCalibrationParameter(User user, CalibrationEntry entry,
            double min, double max, bool isEnabled, double errorTolerance, CalibrationAlgorithmBase algorithm,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out CommandError? error)
            => UpdateCalibrationParameter(user, entry, min, max, isEnabled, errorTolerance, entry.StepSize, algorithm, out error);

        /// <summary>Updates the bounds, tolerance, step size, and algorithm of an existing calibration entry.</summary>
        public bool UpdateCalibrationParameter(User user, CalibrationEntry entry,
            double min, double max, bool isEnabled, double errorTolerance, double stepSize, CalibrationAlgorithmBase algorithm,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(entry);
            if (!_session.HasAccess(user))
            { error = new CommandError("The user does not have access to this project.", true); return false; }
            lock (_sessionLock)
            {
                var oldMin = entry.Min; var oldMax = entry.Max;
                var oldEnabled = entry.IsEnabled;
                var oldTolerance = entry.ErrorTolerance;
                var oldStepSize = entry.StepSize;
                var oldAlgorithm = entry.Algorithm;
                entry.Min = min; entry.Max = max; entry.IsEnabled = isEnabled;
                entry.ErrorTolerance = errorTolerance; entry.StepSize = stepSize; entry.Algorithm = algorithm;
                Buffer.AddUndo(new Command(
                    () => { entry.Min = oldMin; entry.Max = oldMax; entry.IsEnabled = oldEnabled;
                            entry.ErrorTolerance = oldTolerance; entry.StepSize = oldStepSize; entry.Algorithm = oldAlgorithm;
                            return (true, null); },
                    () => { entry.Min = min; entry.Max = max; entry.IsEnabled = isEnabled;
                            entry.ErrorTolerance = errorTolerance; entry.StepSize = stepSize; entry.Algorithm = algorithm;
                            return (true, null); }));
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Sets (or clears) the model output node for a calibration entry.
        /// <paramref name="modelOutputNode"/> must implement
        /// <c>IFunction&lt;float&gt;</c> or <c>IFunction&lt;double&gt;</c>,
        /// or be <c>null</c> to clear the selection.
        /// </summary>
        public bool SetCalibrationEntryModelOutputNode(User user, CalibrationEntry entry, Node? modelOutputNode,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(entry);
            if (!_session.HasAccess(user))
            { error = new CommandError("The user does not have access to this project.", true); return false; }
            lock (_sessionLock)
            {
                var oldNode = entry.ModelOutputNode;
                entry.ModelOutputNode = modelOutputNode;
                Buffer.AddUndo(new Command(
                    () => { entry.ModelOutputNode = oldNode; return (true, null); },
                    () => { entry.ModelOutputNode = modelOutputNode; return (true, null); }));
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Sets (or clears) the target output node for a calibration entry.
        /// <paramref name="targetOutputNode"/> must implement
        /// <c>IFunction&lt;float&gt;</c> or <c>IFunction&lt;double&gt;</c>,
        /// or be <c>null</c> to clear the selection.
        /// </summary>
        public bool SetCalibrationEntryTargetOutputNode(User user, CalibrationEntry entry, Node? targetOutputNode,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out CommandError? error)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(entry);
            if (!_session.HasAccess(user))
            { error = new CommandError("The user does not have access to this project.", true); return false; }
            lock (_sessionLock)
            {
                var oldNode = entry.TargetOutputNode;
                entry.TargetOutputNode = targetOutputNode;
                Buffer.AddUndo(new Command(
                    () => { entry.TargetOutputNode = oldNode; return (true, null); },
                    () => { entry.TargetOutputNode = targetOutputNode; return (true, null); }));
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Gets the parameter node for a given node and parameter name.
        /// </summary>
        /// <param name="node"></param>
        /// <param name="parameterName"></param>
        /// <param name="parameterNode"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        internal bool GetParameterForNode(Node node, string parameterName,
         [NotNullWhen(true)] out Node? parameterNode, [NotNullWhen(false)] out string? error)
        {
            ArgumentNullException.ThrowIfNull(node);
            ArgumentNullException.ThrowIfNull(parameterName);
            lock (_sessionLock)
            {
                var containingBoundary = node.ContainedWithin ?? throw new InvalidOperationException("Node is not contained within a boundary.");
                var link = containingBoundary.Links.FirstOrDefault(lk => lk.Origin == node && lk.OriginHook?.Name == parameterName);
                if(link is null)
                {
                    parameterNode = null;
                    error = $"No parameter named '{parameterName}' was found for node '{node.Name}'.";
                    return false;
                }
                if(link.TryGetFirstDestination(out var dest))
                {
                    parameterNode = dest as Node;
                    if (parameterNode is null)
                    {
                        error = $"The parameter '{parameterName}' either has no destination or one that is not a node.";
                        return false;
                    }
                    error = null;
                    return true;
                }
                else
                {
                    parameterNode = null;
                    error = $"The parameter link for '{parameterName}' on node '{node.Name}' has no destination.";
                    return false;
                }
            }
        }
    }
}
