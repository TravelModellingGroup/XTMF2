using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using XTMF2.AI;
using XTMF2.Editing;
using XTMF2.ModelSystemConstruct;
using XTMF2.Configuration;

namespace XTMF2.GUI.AI;

public sealed class ModelSystemActionApplier : IAiActionApplier, IAiActionValidator
{
    private readonly ModelSystemSession _session;
    private readonly User _user;

    public ModelSystemActionApplier(ModelSystemSession session, User user)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _user = user ?? throw new ArgumentNullException(nameof(user));
    }

    public Task<AiActionExecutionResult> ApplyAsync(
        AiActionBatch batch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operations = batch.Actions
                .Select(ParseOperation)
                .OrderBy(GetOperationOrder)
                .ToArray();
                if (!Preflight(operations, enforceRequiredHooks: false,
                    out var preflightError, out var preflightActionId, out _))
            {
                return Task.FromResult(AiActionExecutionResult.Failure(
                    preflightError!, preflightActionId));
            }

            var affectedElementIds = new List<string>(operations.Length);

            _session.BeginBatch();
            try
            {
                foreach (var operation in operations)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!ApplyOperation(operation, out var affectedElementId, out var error))
                    {
                        _session.AbortBatch(out _);
                        return Task.FromResult(AiActionExecutionResult.Failure(error!, operation.ProposalId));
                    }

                    affectedElementIds.Add(affectedElementId.ToString());
                }

                _session.CommitBatch();
            }
            catch
            {
                _session.AbortBatch(out _);
                throw;
            }

            return Task.FromResult(AiActionExecutionResult.Success(affectedElementIds));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(AiActionExecutionResult.Failure("The AI action batch was cancelled."));
        }
        catch (AiActionInputException exception)
        {
            return Task.FromResult(AiActionExecutionResult.Failure(exception.Message));
        }
        catch (Exception exception)
        {
            return Task.FromResult(AiActionExecutionResult.Failure(
                $"The AI action batch could not be applied: {exception.Message}"));
        }
    }

    public Task<AiActionExecutionResult> ValidateAsync(
        AiActionBatch batch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operations = batch.Actions
                .Select(ParseOperation)
                .OrderBy(GetOperationOrder)
                .ToArray();
            return Preflight(operations, enforceRequiredHooks: true,
                    out var error, out var failedActionId, out var requiresModelDecision)
                ? Task.FromResult(AiActionExecutionResult.Success(Array.Empty<string>()))
                : Task.FromResult(AiActionExecutionResult.Failure(
                    error!, failedActionId, requiresModelDecision));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(AiActionExecutionResult.Failure("The AI action validation was cancelled."));
        }
        catch (AiActionInputException exception)
        {
            return Task.FromResult(AiActionExecutionResult.Failure(exception.Message));
        }
        catch (Exception exception)
        {
            return Task.FromResult(AiActionExecutionResult.Failure(
                $"The AI action batch could not be validated: {exception.Message}"));
        }
    }

    private bool Preflight(
        IReadOnlyList<ActionOperation> operations,
        bool enforceRequiredHooks,
        out string? error,
        out string? failedActionId,
        out bool requiresModelDecision)
    {
        error = null;
        failedActionId = null;
        requiresModelDecision = false;
        var plannedNodes = new Dictionary<Guid, PlannedNode>();
        var connectedHooks = new HashSet<(Guid NodeId, string HookName)>();

        foreach (var node in EnumerateNodes(_session.ModelSystem.GlobalBoundary))
        {
            foreach (var link in node.ContainedWithin.Links.Where(candidate => candidate.Origin == node))
            {
                connectedHooks.Add((node.Id, link.OriginHook.Name));
            }
        }

        foreach (var operation in operations.Where(operation => operation.Kind == AiActionKind.CreateNode))
        {
            if (FindNode(_session.ModelSystem.GlobalBoundary, operation.ElementId) is not null ||
                plannedNodes.ContainsKey(operation.ElementId))
            {
                error = $"A node with requested ID '{operation.ElementId}' already exists or is created more than once; " +
                    "this action is a duplicate. Use the existing node ID with UpdateNode or a parameter action, " +
                    "or choose a different unused reserved ID when a genuinely new node is intended.";
                failedActionId = operation.ProposalId;
                return false;
            }

            if (FindBoundary(_session.ModelSystem.GlobalBoundary, operation.BoundaryId) is null)
            {
                error = $"Boundary '{operation.BoundaryId}' was not found in the model system.";
                failedActionId = operation.ProposalId;
                return false;
            }

            var type = ResolveModuleType(operation.TypeName!);
            if (type is null)
            {
                error = $"Module type '{operation.TypeName}' was not found in the runtime.";
                failedActionId = operation.ProposalId;
                return false;
            }

            plannedNodes.Add(operation.ElementId, new PlannedNode(
                operation.ElementId,
                operation.Value,
                type,
                _session.GetModuleInfo(type).Hooks));
        }

        foreach (var operation in operations.Where(operation =>
                     operation.Kind is AiActionKind.CreateLink or AiActionKind.AddLinkDestination))
        {
            var origin = FindPlannedOrExistingNode(operation.OriginId, plannedNodes);
            var destination = FindPlannedOrExistingNode(operation.DestinationId, plannedNodes);
            if (origin is null || destination is null)
            {
                error = $"{operation.Kind} requires originId and destinationId nodes that exist or are created in the same batch.";
                failedActionId = operation.ProposalId;
                return false;
            }

            var hook = origin.Hooks.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, operation.HookName, StringComparison.OrdinalIgnoreCase));
            if (hook is null)
            {
                error = $"The origin node '{origin.Name}' does not have a hook named '{operation.HookName}'.";
                failedActionId = operation.ProposalId;
                return false;
            }

            var existingOrigin = FindNode(_session.ModelSystem.GlobalBoundary, origin.Id);
            var existingDestination = FindNode(_session.ModelSystem.GlobalBoundary, destination.Id);
            if (existingOrigin is not null && existingDestination is not null &&
                FindExistingConnection(existingOrigin, hook, existingDestination) is not null)
            {
                connectedHooks.Add((origin.Id, hook.Name));
                continue;
            }

            if (hook.IsParameter)
            {
                error = $"The hook '{operation.HookName}' is a parameter hook and cannot be connected with " +
                    $"{operation.Kind}. Do not regenerate this as a link. Configure the parameter with " +
                    "SetBasicParameter or SetScriptedParameter, targeting the owning node with the exact " +
                    $"parameterName '{operation.HookName}' or targeting its generated parameter child node ID. " +
                    "Only non-parameter structural hooks can be used with link actions.";
                failedActionId = operation.ProposalId;
                return false;
            }

            if (operation.Kind == AiActionKind.AddLinkDestination &&
                hook.Cardinality is HookCardinality.Single or HookCardinality.SingleOptional)
            {
                error = $"The hook '{operation.HookName}' has cardinality {hook.Cardinality} and does not accept " +
                    "multiple destinations. AddLinkDestination is only valid for AtLeastOne or AnyNumber hooks. " +
                    "Use CreateLink only when this single hook is currently unconnected; if it is already connected, " +
                    "do not add another destination to it.";
                failedActionId = operation.ProposalId;
                return false;
            }

            connectedHooks.Add((origin.Id, hook.Name));
        }

        if (!enforceRequiredHooks)
        {
            return true;
        }

        foreach (var plannedNode in plannedNodes)
        {
            foreach (var hook in plannedNode.Value.Hooks
                         .Where(hook => !hook.IsParameter &&
                                        hook.Cardinality is HookCardinality.Single or HookCardinality.AtLeastOne))
            {
                if (connectedHooks.Contains((plannedNode.Key, hook.Name)))
                {
                    continue;
                }

                error = $"CreateNode action for '{plannedNode.Value.Name}' creates a module with required hook " +
                    $"'{hook.Name}' unsatisfied. Add a CreateLink from node '{plannedNode.Key}' using hook " +
                    $"'{hook.Name}'. Required parameter hooks are generated automatically; required structural " +
                    "hooks must be connected explicitly.";
                failedActionId = operations.First(operation => operation.Kind == AiActionKind.CreateNode &&
                    operation.ElementId == plannedNode.Key).ProposalId;
                requiresModelDecision = true;
                return false;
            }
        }

        return true;
    }

    private PlannedNode? FindPlannedOrExistingNode(
        Guid nodeId,
        IReadOnlyDictionary<Guid, PlannedNode> plannedNodes)
    {
        if (plannedNodes.TryGetValue(nodeId, out var plannedNode))
        {
            return plannedNode;
        }

        var node = FindNode(_session.ModelSystem.GlobalBoundary, nodeId);
        return node is null
            ? null
            : new PlannedNode(node.Id, node.Name, node.Type, node.Hooks);
    }

    private static IEnumerable<Node> EnumerateNodes(Boundary boundary)
    {
        foreach (var node in boundary.Modules)
        {
            yield return node;
        }

        foreach (var functionInstance in boundary.FunctionInstances)
        {
            yield return functionInstance;
        }

        foreach (var child in boundary.Boundaries)
        {
            foreach (var node in EnumerateNodes(child))
            {
                yield return node;
            }
        }
    }

    private static int GetOperationOrder(ActionOperation operation)
    {
        return operation.Kind switch
        {
            AiActionKind.CreateNode => 0,
            AiActionKind.UpdateNode or AiActionKind.UpdateParameter or
                AiActionKind.SetBasicParameter or AiActionKind.SetScriptedParameter or
                AiActionKind.ConvertBasicParameterToScriptedParameter => 1,
            AiActionKind.CreateLink => 2,
            AiActionKind.AddLinkDestination => 3,
            _ => 3
        };
    }

    private bool ApplyOperation(ActionOperation operation, out Guid affectedElementId, out string? error)
    {
        affectedElementId = Guid.Empty;
        if (operation.Kind == AiActionKind.CreateNode)
        {
            if (FindNode(_session.ModelSystem.GlobalBoundary, operation.ElementId) is not null)
            {
                error = $"A node with requested ID '{operation.ElementId}' already exists. " +
                    "This action appears to duplicate a node already created or supplied by an earlier turn. " +
                    "Use the existing node ID with UpdateNode or a parameter action, or choose a different " +
                    "unused reserved ID only when a genuinely new node is intended.";
                return false;
            }

            var boundary = FindBoundary(_session.ModelSystem.GlobalBoundary, operation.BoundaryId);
            if (boundary is null)
            {
                error = $"Boundary '{operation.BoundaryId}' was not found in the model system.";
                return false;
            }

            var type = ResolveModuleType(operation.TypeName!);
            if (type is null)
            {
                error = $"Module type '{operation.TypeName}' was not found in the runtime.";
                return false;
            }

            var location = new Rectangle(operation.X, operation.Y, 120, 50);
            var created = IsParameterModuleType(type)
                ? _session.AddNode(_user, boundary, operation.Value!, type, location,
                    out var createdNode, out var createError, operation.ElementId)
                : _session.AddNodeGenerateParameters(
                    _user,
                    boundary,
                    operation.Value!,
                    type,
                    location,
                    out createdNode,
                    out _,
                    out createError,
                    operation.ElementId);
            if (!created)
            {
                error = createError?.Message ?? "The node could not be created.";
                return false;
            }

            affectedElementId = createdNode!.Id;
            error = null;
            return true;
        }

        if (operation.Kind == AiActionKind.CreateLink)
        {
            if (FindLink(_session.ModelSystem.GlobalBoundary, operation.ElementId) is not null)
            {
                error = $"A link with requested ID '{operation.ElementId}' already exists.";
                return false;
            }

            var origin = FindNode(_session.ModelSystem.GlobalBoundary, operation.OriginId);
            var destination = FindNode(_session.ModelSystem.GlobalBoundary, operation.DestinationId);
            if (origin is null || destination is null)
            {
                error = "CreateLink requires valid originId and destinationId nodes.";
                return false;
            }

            var hook = origin.Hooks.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, operation.HookName, StringComparison.OrdinalIgnoreCase));
            if (hook is null)
            {
                var availableHooks = origin.Hooks.Where(candidate => !candidate.IsParameter).ToArray();
                var availableHookNames = availableHooks.Length == 0
                    ? "none"
                    : string.Join(", ", availableHooks.Select(candidate => candidate.Name));
                var typeName = origin.Type.FullName ?? origin.Type.Name;
                error = $"The origin node '{origin.Name}' (type '{typeName}') does not have a hook " +
                    $"named '{operation.HookName}'. Available structural hooks: {availableHookNames}. " +
                    $"Node ID: {origin.Id}. CreateLink accepts only structural hooks; parameter hooks such as " +
                    $"'{operation.HookName}' must be configured with a parameter action. Use one of the listed " +
                    "structural hook names exactly.";
                return false;
            }

            var existingLink = FindExistingConnection(origin, hook, destination);
            if (existingLink is not null)
            {
                affectedElementId = existingLink.Id;
                error = null;
                return true;
            }

            if (hook.IsParameter)
            {
                error = $"The hook '{operation.HookName}' is a parameter hook and cannot be connected with " +
                    $"{operation.Kind}. Do not regenerate this as a link. Configure the parameter with " +
                    "SetBasicParameter or SetScriptedParameter, targeting the owning node with the exact " +
                    $"parameterName '{operation.HookName}' or targeting its generated parameter child node ID. " +
                    "Only non-parameter structural hooks can be used with link actions.";
                return false;
            }

            if (!_session.AddLink(_user, origin, hook, destination, out var link, out var linkError,
                operation.ElementId))
            {
                error = linkError?.Message ?? "The link could not be created.";
                return false;
            }

            affectedElementId = link!.Id;
            error = null;
            return true;
        }

        if (operation.Kind == AiActionKind.AddLinkDestination)
        {
            var origin = FindNode(_session.ModelSystem.GlobalBoundary, operation.OriginId);
            var destination = FindNode(_session.ModelSystem.GlobalBoundary, operation.DestinationId);
            if (origin is null || destination is null)
            {
                error = "AddLinkDestination requires valid originId and destinationId nodes.";
                return false;
            }

            var hook = origin.Hooks.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, operation.HookName, StringComparison.OrdinalIgnoreCase));
            if (hook is null)
            {
                error = $"The origin node '{origin.Name}' does not have a hook named '{operation.HookName}'.";
                return false;
            }

            var existingLink = FindExistingConnection(origin, hook, destination);
            if (existingLink is not null)
            {
                affectedElementId = existingLink.Id;
                error = null;
                return true;
            }

            if (hook.IsParameter || hook.Cardinality is HookCardinality.Single or HookCardinality.SingleOptional)
            {
                error = $"The hook '{operation.HookName}' on origin node '{origin.Name}' has cardinality " +
                    $"{hook.Cardinality} and does not accept multiple destinations. AddLinkDestination is only " +
                    "valid for AtLeastOne or AnyNumber hooks. Use CreateLink only when this single hook is " +
                    "currently unconnected; if it is already connected, do not add another destination to it.";
                return false;
            }

            if (!_session.AddLinks(_user, origin, hook, new[] { destination }, out var link, out var linkError))
            {
                error = linkError?.Message ?? "The destination could not be added to the link.";
                return false;
            }

            affectedElementId = link!.Id;
            error = null;
            return true;
        }

        var node = FindNode(_session.ModelSystem.GlobalBoundary, operation.NodeId);
        if (node is null)
        {
            error = $"Node '{operation.NodeId}' was not found in the model system. " +
                "If this action depends on a proposed CreateNode, select that creation step too. " +
                "For a generated parameter, target the owning module node and provide its exact " +
                "parameterName instead of inventing the hidden child node ID.";
            return false;
        }

        switch (operation.Kind)
        {
            case AiActionKind.UpdateNode:
                if (!_session.SetNodeName(_user, node, operation.Value!, out var nameError))
                {
                    error = nameError?.Message ?? "The node name could not be updated.";
                    return false;
                }

                break;
            case AiActionKind.UpdateParameter:
            case AiActionKind.SetBasicParameter:
            case AiActionKind.SetScriptedParameter:
            case AiActionKind.ConvertBasicParameterToScriptedParameter:
                node = ResolveParameterTarget(node, operation.ParameterName);
                if (node is null)
                {
                    error = $"The parameter target for node '{operation.NodeId}' was not found. " +
                        $"Specify the generated parameter node ID or its parameterName.";
                    return false;
                }

                var isBasicParameter = node.Type?.IsGenericType == true &&
                    node.Type.GetGenericTypeDefinition() == typeof(RuntimeModules.BasicParameter<>);
                var isScriptedParameter = node.Type?.IsGenericType == true &&
                    node.Type.GetGenericTypeDefinition() == typeof(RuntimeModules.ScriptedParameter<>);
                if (operation.Kind == AiActionKind.ConvertBasicParameterToScriptedParameter)
                {
                    if (!isBasicParameter)
                    {
                        error = $"Node '{node.Name}' is not a BasicParameter node.";
                        return false;
                    }

                    if (!_session.ConvertBasicParameterToScriptedParameter(
                        _user, node, operation.Value!, out var conversionError))
                    {
                        error = conversionError?.Message ??
                            "The BasicParameter could not be converted to a ScriptedParameter.";
                        return false;
                    }

                    break;
                }
                if (operation.Kind == AiActionKind.SetBasicParameter && !isBasicParameter)
                {
                    error = $"Node '{node.Name}' is not a BasicParameter node.";
                    return false;
                }

                if (operation.Kind == AiActionKind.SetScriptedParameter && isBasicParameter)
                {
                    if (!_session.ConvertBasicParameterToScriptedParameter(
                        _user, node, operation.Value!, out var conversionError))
                    {
                        error = conversionError?.Message ??
                            "The BasicParameter could not be converted to a ScriptedParameter.";
                        return false;
                    }

                    break;
                }

                if (operation.Kind == AiActionKind.SetScriptedParameter && !isScriptedParameter)
                {
                    error = $"Node '{node.Name}' is not a ScriptedParameter node.";
                    return false;
                }

                var useExpression = operation.Kind == AiActionKind.SetScriptedParameter ||
                    operation.IsExpression;
                var succeeded = useExpression
                    ? _session.SetParameterExpression(_user, node, operation.Value!, out var expressionError)
                    : _session.SetParameterValue(_user, node, operation.Value!, out expressionError);
                if (!succeeded)
                {
                    var isParameterNode = node.Type?.IsGenericType == true &&
                        (node.Type.GetGenericTypeDefinition() == typeof(RuntimeModules.BasicParameter<>) ||
                         node.Type.GetGenericTypeDefinition() == typeof(RuntimeModules.ScriptedParameter<>));
                    if (isParameterNode)
                    {
                        error = $"The parameter value for node '{node.Name}' could not be updated. " +
                            $"This is a {node.Type?.Name ?? "parameter"} node, so it has no child parameters. " +
                            (expressionError?.Message ?? "Check the value and expression syntax.") +
                            " ScriptedParameter expressions use exact variable names and do not support C# " +
                            "methods such as .ToString().";
                        return false;
                    }

                    var availableParameters = node.Hooks
                        .Where(candidate => candidate.IsParameter)
                        .Select(candidate => candidate.Name)
                        .ToArray();
                    var parameterNames = availableParameters.Length == 0
                        ? "none"
                        : string.Join(", ", availableParameters);
                    error = $"The parameter value for node '{node.Name}' could not be updated. " +
                        $"Available parameters: {parameterNames}. " +
                        $"Node type: {node.Type?.FullName ?? node.Type?.Name ?? "unknown"}. " +
                        (expressionError?.Message ?? "Check the value and expression syntax.");
                    return false;
                }

                break;
            default:
                error = $"Action kind '{operation.Kind}' is not supported by the GUI action applier yet.";
                return false;
        }

        affectedElementId = node.Id;
        error = null;
        return true;
    }

    private static ActionOperation ParseOperation(AiActionProposal proposal)
    {
        if (proposal.Kind == AiActionKind.CreateNode)
        {
            var elementId = ReadGuid(proposal, "id");
            var boundaryId = ReadGuid(proposal, "boundaryId");
            var typeName = ReadString(proposal, "typeName");
            var name = ReadString(proposal, "name");
            return new ActionOperation(proposal.Kind, elementId, Guid.Empty, boundaryId, Guid.Empty, Guid.Empty,
                string.Empty, string.Empty, name, typeName, ReadInt(proposal, "x", 0), ReadInt(proposal, "y", 0), false,
                proposal.Id);
        }

        if (proposal.Kind == AiActionKind.CreateLink)
        {
            var elementId = ReadGuid(proposal, "id");
            var originId = ReadGuid(proposal, "originId");
            var destinationId = ReadGuid(proposal, "destinationId");
            var hookName = ReadString(proposal, "hookName");
            return new ActionOperation(proposal.Kind, elementId, Guid.Empty, Guid.Empty, originId, destinationId,
                hookName, string.Empty, string.Empty, null, 0, 0, false, proposal.Id);
        }

        if (proposal.Kind == AiActionKind.AddLinkDestination)
        {
            var originId = ReadGuid(proposal, "originId");
            var destinationId = ReadGuid(proposal, "destinationId");
            var hookName = ReadString(proposal, "hookName");
            return new ActionOperation(proposal.Kind, Guid.Empty, Guid.Empty, Guid.Empty, originId, destinationId,
                hookName, string.Empty, string.Empty, null, 0, 0, false, proposal.Id);
        }

        if (proposal.Kind is not AiActionKind.UpdateNode and not AiActionKind.UpdateParameter and
            not AiActionKind.SetBasicParameter and not AiActionKind.SetScriptedParameter and
            not AiActionKind.ConvertBasicParameterToScriptedParameter)
        {
            throw new AiActionInputException(
                $"Action '{proposal.Id}' uses unsupported action kind '{proposal.Kind}'.");
        }

        var nodeId = ReadGuid(proposal, "nodeId");
        var value = ReadString(proposal, "value");
        var parameterName = proposal.Arguments.TryGetProperty("parameterName", out var parameterProperty) &&
            parameterProperty.ValueKind == JsonValueKind.String
            ? parameterProperty.GetString()
            : null;

        var isExpression = proposal.Arguments.TryGetProperty("isExpression", out var expressionProperty) &&
            expressionProperty.ValueKind == JsonValueKind.True;
        return new ActionOperation(proposal.Kind, nodeId, nodeId, Guid.Empty, Guid.Empty, Guid.Empty,
            string.Empty, parameterName ?? string.Empty, value, null, 0, 0, isExpression, proposal.Id);
    }

    private static Guid ReadGuid(AiActionProposal proposal, string name)
    {
        if (!proposal.Arguments.TryGetProperty(name, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            !Guid.TryParse(property.GetString(), out var value))
        {
            throw new AiActionInputException($"Action '{proposal.Id}' must contain a valid {name}.");
        }

        return value;
    }

    private static string ReadString(AiActionProposal proposal, string name)
    {
        if (!proposal.Arguments.TryGetProperty(name, out var property) ||
            property.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new AiActionInputException($"Action '{proposal.Id}' must contain a non-empty string {name}.");
        }

        return property.GetString()!;
    }

    private static int ReadInt(AiActionProposal proposal, string name, int fallback)
    {
        return proposal.Arguments.TryGetProperty(name, out var property) && property.TryGetInt32(out var value)
            ? value
            : fallback;
    }

    private static Node? ResolveParameterTarget(Node node, string parameterName)
    {
        var isParameterNode = node.Type?.IsGenericType == true &&
            (node.Type.GetGenericTypeDefinition() == typeof(RuntimeModules.BasicParameter<>) ||
             node.Type.GetGenericTypeDefinition() == typeof(RuntimeModules.ScriptedParameter<>));
        if (isParameterNode)
        {
            return node;
        }

        if (string.IsNullOrWhiteSpace(parameterName) || node.ContainedWithin is null)
        {
            return null;
        }

        var parameterLink = node.ContainedWithin.Links.FirstOrDefault(link =>
            link.Origin == node && link.OriginHook.IsParameter &&
            string.Equals(link.OriginHook.Name, parameterName, StringComparison.OrdinalIgnoreCase));
        if (parameterLink is null)
        {
            return null;
        }

        return parameterLink switch
        {
            SingleLink singleLink => singleLink.Destination,
            MultiLink multiLink => multiLink.Destinations.FirstOrDefault(),
            _ => null
        };
    }

    private Type? ResolveModuleType(string typeName)
    {
        var loadedType = _session.LoadedModuleTypes.FirstOrDefault(type =>
            string.Equals(type.AssemblyQualifiedName, typeName, StringComparison.Ordinal) ||
            string.Equals(type.FullName, typeName, StringComparison.Ordinal) ||
            string.Equals(type.Name, typeName, StringComparison.Ordinal));
        if (loadedType is not null)
        {
            return loadedType;
        }

        var resolvedType = Type.GetType(
            typeName,
            assemblyName => AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly =>
                string.Equals(assembly.GetName().Name, assemblyName?.Name, StringComparison.Ordinal)),
            (assembly, name, ignoreCase) => assembly?.GetType(name, throwOnError: false, ignoreCase),
            throwOnError: false);
        if (resolvedType is not null)
        {
            return resolvedType;
        }

        var genericStart = typeName.IndexOf('[', StringComparison.Ordinal);
        if (genericStart <= 0)
        {
            return null;
        }

        var genericDefinitionName = typeName[..genericStart];
        var openGeneric = _session.OpenGenericModuleTypes.FirstOrDefault(type =>
            string.Equals(type.FullName, genericDefinitionName, StringComparison.Ordinal));
        if (openGeneric is null || openGeneric.GetGenericArguments().Length != 1)
        {
            return null;
        }

        var argumentStart = typeName.IndexOf("[[", genericStart, StringComparison.Ordinal);
        var argumentEnd = argumentStart < 0
            ? typeName.IndexOf(']', genericStart)
            : typeName.IndexOf(',', argumentStart + 2);
        if (argumentStart < 0 || argumentEnd <= argumentStart + 2)
        {
            return null;
        }

        var argumentName = typeName[(argumentStart + 2)..argumentEnd];
        var argumentType = Type.GetType(argumentName) ??
            _session.AllAvailableTypes.FirstOrDefault(type =>
                string.Equals(type.FullName, argumentName, StringComparison.Ordinal));
        return argumentType is null
            ? null
            : openGeneric.MakeGenericType(argumentType);
    }

    private static bool IsParameterModuleType(Type type)
    {
        return type.IsGenericType &&
            (type.GetGenericTypeDefinition() == typeof(RuntimeModules.BasicParameter<>) ||
             type.GetGenericTypeDefinition() == typeof(RuntimeModules.ScriptedParameter<>));
    }

    private static Node? FindNode(Boundary boundary, Guid nodeId)
    {
        foreach (var node in boundary.Modules)
        {
            if (node.Id == nodeId)
            {
                return node;
            }
        }

        foreach (var functionInstance in boundary.FunctionInstances)
        {
            if (functionInstance.Id == nodeId)
            {
                return functionInstance;
            }
        }

        foreach (var child in boundary.Boundaries)
        {
            var result = FindNode(child, nodeId);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    private static Boundary? FindBoundary(Boundary boundary, Guid boundaryId)
    {
        if (boundary.Id == boundaryId)
            return boundary;

        foreach (var child in boundary.Boundaries)
        {
            var result = FindBoundary(child, boundaryId);
            if (result is not null)
                return result;
        }

        return null;
    }

    private static Link? FindLink(Boundary boundary, Guid linkId)
    {
        var link = boundary.Links.FirstOrDefault(candidate => candidate.Id == linkId);
        if (link is not null)
            return link;

        foreach (var child in boundary.Boundaries)
        {
            var result = FindLink(child, linkId);
            if (result is not null)
                return result;
        }

        return null;
    }

    private static Link? FindExistingConnection(Node origin, NodeHook hook, Node destination)
    {
        var link = origin.ContainedWithin?.Links.FirstOrDefault(candidate =>
            candidate.Origin == origin && candidate.OriginHook == hook);
        if (link is SingleLink singleLink)
        {
            return singleLink.Destination == destination ? singleLink : null;
        }

        if (link is MultiLink multiLink && multiLink.Destinations.Contains(destination))
        {
            return multiLink;
        }

        return null;
    }

    private sealed record ActionOperation(
        AiActionKind Kind,
        Guid ElementId,
        Guid NodeId,
        Guid BoundaryId,
        Guid OriginId,
        Guid DestinationId,
        string HookName,
        string ParameterName,
        string Value,
        string? TypeName,
        int X,
        int Y,
        bool IsExpression,
        string ProposalId);

    private sealed record PlannedNode(
        Guid Id,
        string Name,
        Type Type,
        IReadOnlyList<NodeHook> Hooks);

    private sealed class AiActionInputException(string message) : Exception(message);
}