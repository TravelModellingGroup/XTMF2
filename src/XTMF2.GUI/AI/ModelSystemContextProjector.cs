using System;
using System.Collections.Generic;
using System.Linq;
using XTMF2.AI;
using XTMF2.Editing;
using XTMF2.ModelSystemConstruct;

namespace XTMF2.GUI.AI;

public sealed class ModelSystemContextProjector
{
    private const int MaxDescriptionLength = 1000;
    private const int MaxModuleIndexDescriptionLength = 240;
    private const int MaxParameterLength = 2000;
    private const int MaxBoundaryResults = 8;
    private const int MaxBoundaryElements = 32;
    private const int MaxBoundaryComments = 16;
    private readonly string _modelSystemId;
    private readonly string _modelSystemName;
    private readonly ModelSystemSession? _session;
    private Boundary? _currentBoundaryForLookup;

    public ModelSystemContextProjector(
        string modelSystemId,
        string modelSystemName,
        ModelSystemSession? session = null)
    {
        _modelSystemId = modelSystemId ?? throw new ArgumentNullException(nameof(modelSystemId));
        _modelSystemName = modelSystemName ?? throw new ArgumentNullException(nameof(modelSystemName));
        _session = session;
    }

    public AiContextSnapshot CreateSnapshot(
        Boundary currentBoundary,
        IReadOnlySet<Guid>? selectedElementIds = null,
        IReadOnlyCollection<AiActionProposal>? pendingActions = null,
        IReadOnlyList<string>? reservedElementIds = null)
    {
        ArgumentNullException.ThrowIfNull(currentBoundary);
        _currentBoundaryForLookup = currentBoundary;

        var elements = EnumerateElements(currentBoundary)
            .Where(element => selectedElementIds is null || selectedElementIds.Contains(element.Id))
            .Select(CreateElement)
            .ToList();
        var elementIds = elements.Select(element => element.Id).ToHashSet(StringComparer.Ordinal);
        var links = currentBoundary.Links
            .Where(link => elementIds.Contains(link.Origin.Id.ToString()))
            .SelectMany(CreateLinks)
            .ToList();
        var moduleDescriptions = CreateModuleDescriptions();
        var currentBoundaryModules = CreateCurrentBoundaryModuleDescriptions(currentBoundary, moduleDescriptions);
        AddPendingElements(elements, links, elementIds, pendingActions, moduleDescriptions);
        elements = elements
            .Select(element => element with
            {
                AvailableHookStates = CreateHookStates(element, links, moduleDescriptions)
            })
            .ToList();
        var moduleIndex = CreateModuleIndex(moduleDescriptions);
        var variables = CreateVariableDescriptions(currentBoundary);
        var commentBlocks = currentBoundary.CommentBlocks
            .Select(CreateCommentBlock)
            .ToArray();

        return new AiContextSnapshot(
            _modelSystemId,
            _modelSystemName,
            currentBoundary.Id.ToString(),
            currentBoundary.Name,
            elements,
            links,
            variables,
            moduleIndex,
            reservedElementIds,
            currentBoundaryModules,
            commentBlocks);
    }

    private IReadOnlyDictionary<string, string> CreateVariableDescriptions(Boundary currentBoundary)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        var localVariables = currentBoundary.OwningFunctionTemplate?.LocalVariables;
        if (localVariables is not null)
        {
            foreach (var variable in localVariables)
            {
                AddVariableDescription(variables, variable);
            }
        }

        var modelVariables = _session?.ModelSystem.Variables;
        if (modelVariables is not null)
        {
            foreach (var variable in modelVariables)
            {
                if (!variables.ContainsKey(variable.Name))
                {
                    AddVariableDescription(variables, variable);
                }
            }
        }

        return variables;
    }

    private static void AddVariableDescription(
        IDictionary<string, string> variables,
        Node variable)
    {
        if (string.IsNullOrWhiteSpace(variable.Name))
        {
            return;
        }

        var valueType = variable.ParameterValue?.Type;
        if (valueType is null && variable.Type?.IsGenericType == true)
        {
            var genericDefinition = variable.Type.GetGenericTypeDefinition();
            if (genericDefinition == typeof(RuntimeModules.BasicParameter<>) ||
                genericDefinition == typeof(RuntimeModules.ScriptedParameter<>))
            {
                valueType = variable.Type.GetGenericArguments()[0];
            }
        }

        if (valueType is not null)
        {
            variables[variable.Name] = valueType.FullName ?? valueType.Name;
        }
    }

    public IReadOnlyList<object> DescribeConnections(string firstNodeId, string secondNodeId)
    {
        if (!Guid.TryParse(firstNodeId, out var firstId) ||
            !Guid.TryParse(secondNodeId, out var secondId) ||
            _session?.ModelSystem.GlobalBoundary is not { } boundary)
        {
            return Array.Empty<object>();
        }

        return EnumerateBoundaries(boundary)
            .SelectMany(current => current.Links)
            .Where(link => (link.Origin.Id == firstId && DestinationIds(link).Contains(secondId)) ||
                          (link.Origin.Id == secondId && DestinationIds(link).Contains(firstId)))
            .SelectMany(link => DestinationIds(link)
                .Where(destinationId => destinationId == firstId || destinationId == secondId)
                .Select(destinationId => (object)new
                {
                    originNodeId = link.Origin.Id,
                    destinationNodeId = destinationId,
                    hookName = link.OriginHook.Name,
                    isDisabled = link.IsDisabled
                }))
            .ToArray();
    }

    private static IEnumerable<Boundary> EnumerateBoundaries(Boundary boundary)
    {
        yield return boundary;
        foreach (var child in boundary.Boundaries)
        foreach (var nested in EnumerateBoundaries(child))
            yield return nested;
    }

    private static IEnumerable<Guid> DestinationIds(Link link)
    {
        return link switch
        {
            SingleLink single when single.Destination is not null => [single.Destination.Id],
            MultiLink multi => multi.Destinations.Select(destination => destination.Id),
            _ => []
        };
    }

    private static void AddPendingElements(
        List<AiContextElement> elements,
        List<AiContextLink> links,
        HashSet<string> existingElementIds,
        IReadOnlyCollection<AiActionProposal>? pendingActions,
        IReadOnlyList<AiModuleDescription> moduleDescriptions)
    {
        if (pendingActions is null)
        {
            return;
        }

        foreach (var action in pendingActions)
        {
            if (action.Kind == AiActionKind.CreateNode &&
                TryReadGuid(action.Arguments, "id", out var nodeId) &&
                existingElementIds.Add(nodeId))
            {
                var typeName = ReadString(action.Arguments, "typeName");
                var module = moduleDescriptions.FirstOrDefault(candidate =>
                    string.Equals(candidate.TypeName, typeName, StringComparison.Ordinal));
                elements.Add(new AiContextElement(
                    nodeId,
                    "Node",
                    ReadString(action.Arguments, "name") ?? "Proposed node",
                    typeName,
                    "Provisional node from an unapplied AI proposal.",
                    new Dictionary<string, string>(StringComparer.Ordinal),
                    module?.Members.Where(member => member.IsParameter).Select(member => member.Name).ToArray(),
                    module?.Members.Where(member => !member.IsParameter).Select(member => member.Name).ToArray(),
                    IsProvisional: true,
                    Position: new AiElementPosition(
                        ReadFloat(action.Arguments, "x", 0),
                        ReadFloat(action.Arguments, "y", 0),
                        120,
                        50)));
            }
        }

        foreach (var action in pendingActions)
        {
            if (action.Kind == AiActionKind.CreateLink &&
                TryReadGuid(action.Arguments, "id", out var linkId) &&
                TryReadGuid(action.Arguments, "originId", out var originId) &&
                TryReadGuid(action.Arguments, "destinationId", out var destinationId))
            {
                links.Add(new AiContextLink(
                    linkId,
                    originId,
                    destinationId,
                    ReadString(action.Arguments, "hookName") ?? string.Empty,
                    IsDisabled: false));
            }
        }
    }

    private static IReadOnlyList<AiContextHookState> CreateHookStates(
        AiContextElement element,
        IReadOnlyList<AiContextLink> links,
        IReadOnlyList<AiModuleDescription> moduleDescriptions)
    {
        if (string.IsNullOrWhiteSpace(element.TypeName))
        {
            return Array.Empty<AiContextHookState>();
        }

        var module = moduleDescriptions.FirstOrDefault(candidate =>
            string.Equals(candidate.TypeName, element.TypeName, StringComparison.Ordinal));
        if (module is null)
        {
            return Array.Empty<AiContextHookState>();
        }

        return module.Members
            .Select(member => new AiContextHookState(
                member.Name,
                member.IsParameter,
                member.Required,
                links.Any(link => !link.IsDisabled &&
                    string.Equals(link.OriginId, element.Id, StringComparison.Ordinal) &&
                    string.Equals(link.HookName, member.Name, StringComparison.OrdinalIgnoreCase)),
                member.PassesExecution,
                member.Cardinality))
            .ToArray();
    }

    private static bool TryReadGuid(System.Text.Json.JsonElement arguments, string name, out string value)
    {
        value = string.Empty;
        if (!arguments.TryGetProperty(name, out var property) ||
            property.ValueKind != System.Text.Json.JsonValueKind.String ||
            !Guid.TryParse(property.GetString(), out var guid))
        {
            return false;
        }

        value = guid.ToString();
        return true;
    }

    private static string? ReadString(System.Text.Json.JsonElement arguments, string name)
    {
        return arguments.TryGetProperty(name, out var property) &&
               property.ValueKind == System.Text.Json.JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static float ReadFloat(System.Text.Json.JsonElement arguments, string name, float fallback)
    {
        return arguments.TryGetProperty(name, out var property) && property.TryGetSingle(out var value)
            ? value
            : fallback;
    }

    private IReadOnlyList<AiModuleDescription> CreateModuleDescriptions()
    {
        if (_session is null)
        {
            return Array.Empty<AiModuleDescription>();
        }

        return _session.LoadedModuleTypes
            .Select(CreateModuleDescription)
            .Where(description => description is not null)
            .Cast<AiModuleDescription>()
            .OrderBy(description => description.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<AiModuleDescription> CreateModuleIndex(
        IReadOnlyList<AiModuleDescription> moduleDescriptions)
    {
        return moduleDescriptions
            .Select(description => new AiModuleDescription(
                description.TypeName,
                description.Name,
                Limit(description.Description, MaxModuleIndexDescriptionLength),
                description.DocumentationLink,
                Array.Empty<AiModuleMemberDescription>()))
            .ToArray();
    }

    private static IReadOnlyList<AiModuleDescription> CreateCurrentBoundaryModuleDescriptions(
        Boundary currentBoundary,
        IReadOnlyList<AiModuleDescription> moduleDescriptions)
    {
        var currentTypes = EnumerateElements(currentBoundary)
            .Select(element => element.Type.FullName ?? element.Type.Name)
            .ToHashSet(StringComparer.Ordinal);
        return moduleDescriptions
            .Where(description => currentTypes.Contains(description.TypeName))
            .ToArray();
    }

    public AiModuleDescription? DescribeModuleType(string typeName)
    {
        if (_session is null || string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        var type = _session.LoadedModuleTypes.FirstOrDefault(candidate =>
            string.Equals(candidate.FullName, typeName, StringComparison.Ordinal) ||
            string.Equals(candidate.Name, typeName, StringComparison.Ordinal));
        return type is null ? null : CreateModuleDescription(type);
    }

    public IReadOnlyList<AiContextCommentBlock> DescribeCommentBlocks(
        IEnumerable<AiCommentBlockRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);

        var commentBlocks = _currentBoundaryForLookup is null
            ? Array.Empty<AiContextCommentBlock>()
            : _currentBoundaryForLookup.CommentBlocks.Select(CreateCommentBlock).ToArray();
        var normalizedRequests = requests
            .Where(request => request is not null)
            .Select(request => new
            {
                Id = request.CommentBlockId?.Trim(),
                Query = request.Query?.Trim()
            })
            .Where(request => !string.IsNullOrWhiteSpace(request.Id) ||
                              !string.IsNullOrWhiteSpace(request.Query))
            .Take(8)
            .ToArray();

        if (normalizedRequests.Length == 0)
        {
            return Array.Empty<AiContextCommentBlock>();
        }

        return commentBlocks
            .Where(comment => normalizedRequests.Any(request =>
                !string.IsNullOrWhiteSpace(request.Id) &&
                string.Equals(comment.Id, request.Id, StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrWhiteSpace(request.Query) &&
                (comment.Header.Contains(request.Query, StringComparison.OrdinalIgnoreCase) ||
                 comment.Comment.Contains(request.Query, StringComparison.OrdinalIgnoreCase))))
            .Take(8)
            .ToArray();
    }

    public IReadOnlyList<AiBoundaryDescription> DescribeBoundaries(
        IEnumerable<AiBoundaryRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);

        var boundaries = _session is null
            ? Array.Empty<Boundary>()
            : EnumerateBoundaries(_session.ModelSystem.GlobalBoundary).ToArray();
        var normalizedRequests = requests
            .Where(request => request is not null)
            .Select(request => new
            {
                Id = request.BoundaryId?.Trim(),
                Path = request.Path?.Trim(),
                Query = request.Query?.Trim()
            })
            .Where(request => !string.IsNullOrWhiteSpace(request.Id) ||
                              !string.IsNullOrWhiteSpace(request.Path) ||
                              !string.IsNullOrWhiteSpace(request.Query))
            .Take(MaxBoundaryResults)
            .ToArray();

        if (normalizedRequests.Length == 0)
        {
            return Array.Empty<AiBoundaryDescription>();
        }

        return boundaries
            .Where(boundary => normalizedRequests.Any(request =>
                !string.IsNullOrWhiteSpace(request.Id) &&
                string.Equals(boundary.Id.ToString(), request.Id, StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrWhiteSpace(request.Path) &&
                string.Equals(boundary.FullPath, request.Path, StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrWhiteSpace(request.Query) &&
                (boundary.Name.Contains(request.Query, StringComparison.OrdinalIgnoreCase) ||
                 boundary.FullPath.Contains(request.Query, StringComparison.OrdinalIgnoreCase) ||
                 boundary.Description.Contains(request.Query, StringComparison.OrdinalIgnoreCase))))
            .Take(MaxBoundaryResults)
            .Select(CreateBoundaryDescription)
            .ToArray();
    }

    private AiBoundaryDescription CreateBoundaryDescription(Boundary boundary)
    {
        var elements = EnumerateElements(boundary)
            .Take(MaxBoundaryElements)
            .Select(CreateElement)
            .ToArray();
        var comments = boundary.CommentBlocks
            .Take(MaxBoundaryComments)
            .Select(CreateCommentBlock)
            .ToArray();
        return new AiBoundaryDescription(
            boundary.Id.ToString(),
            boundary.Name,
            boundary.FullPath,
            Limit(boundary.Description, MaxDescriptionLength),
            elements,
            comments);
    }

    private AiModuleDescription? CreateModuleDescription(Type type)
    {
        try
        {
            var metadata = _session!.GetModuleInfo(type);
            return new AiModuleDescription(
                type.FullName ?? type.Name,
                metadata.Description.Name ?? type.Name,
                Limit(metadata.Description.Description ?? string.Empty, MaxDescriptionLength),
                metadata.Description.DocumentationLink,
                metadata.Hooks.Select(hook => new AiModuleMemberDescription(
                    hook.Name,
                    hook.IsParameter,
                    hook.Type.FullName ?? hook.Type.Name,
                    Limit(hook.Description, MaxDescriptionLength),
                    hook.Cardinality is HookCardinality.Single or HookCardinality.AtLeastOne,
                    hook.DefaultValue,
                    hook.PassesExecution,
                    hook.Cardinality.ToString())).ToArray(),
                ReadAiInstructions(type));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? ReadAiInstructions(Type type)
    {
        var instructions = type.GetCustomAttributes(typeof(AiModuleInstructionsAttribute), false)
            .OfType<AiModuleInstructionsAttribute>()
            .Select(attribute => attribute.Instructions)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return instructions is null ? null : Limit(instructions, MaxDescriptionLength);
    }

    private static IEnumerable<Node> EnumerateElements(Boundary boundary)
    {
        foreach (var start in boundary.Starts)
        {
            yield return start;
        }

        foreach (var node in boundary.Modules)
        {
            yield return node;
        }

        foreach (var functionInstance in boundary.FunctionInstances)
        {
            yield return functionInstance;
        }
    }

    private static AiContextElement CreateElement(Node node)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        if (node.ParameterValue is not null)
        {
            parameters["value"] = Limit(node.ParameterValue.Representation, MaxParameterLength);
        }

        return new AiContextElement(
            node.Id.ToString(),
            node is Start ? "Start" : node is FunctionInstance ? "FunctionInstance" : "Node",
            node.Name,
            node.Type.FullName ?? node.Type.Name,
                string.IsNullOrWhiteSpace(node.Description) ? null : Limit(node.Description, MaxDescriptionLength),
            parameters,
            node.Hooks.Where(hook => hook.IsParameter).Select(hook => hook.Name).ToArray(),
            node.Hooks.Where(hook => !hook.IsParameter).Select(hook => hook.Name).ToArray(),
            Position: new AiElementPosition(
                node.Location.X,
                node.Location.Y,
                node.Location.Width,
                node.Location.Height));
    }

    private static AiContextCommentBlock CreateCommentBlock(CommentBlock commentBlock)
    {
        return new AiContextCommentBlock(
            commentBlock.Id.ToString(),
            Limit(commentBlock.Header, MaxDescriptionLength),
            Limit(commentBlock.Comment, MaxDescriptionLength),
            new AiElementPosition(
                commentBlock.Location.X,
                commentBlock.Location.Y,
                commentBlock.Location.Width,
                commentBlock.Location.Height));
    }

            private static string Limit(string value, int maximumLength)
            {
            return value.Length <= maximumLength
                ? value
                : value[..maximumLength] + "... [truncated]";
            }

    private static IEnumerable<AiContextLink> CreateLinks(Link link)
    {
        if (link is SingleLink singleLink)
        {
            yield return CreateLink(link, singleLink.Destination);
            yield break;
        }

        if (link is MultiLink multiLink)
        {
            foreach (var destination in multiLink.Destinations)
            {
                yield return CreateLink(link, destination);
            }
        }
    }

    private static AiContextLink CreateLink(Link link, Node destination)
    {
        return new AiContextLink(
            link.Id.ToString(),
            link.Origin.Id.ToString(),
            destination.Id.ToString(),
            link.OriginHook.Name,
            link.IsDisabled);
    }
}