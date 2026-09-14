using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace XTMF2.AI;

public sealed class OllamaProvider : IAiProvider, IAiModelContextInfo
{
    private static readonly JsonSerializerOptions ActionJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly HttpClient _httpClient;

    public OllamaProvider(HttpClient httpClient, Uri endpoint)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        Endpoint = new Uri(endpoint.ToString().TrimEnd('/') + "/", UriKind.Absolute);
    }

    public Uri Endpoint { get; }

    public AiProviderInfo Info { get; } = new(
        "ollama",
        "Ollama",
        AiCapability.Streaming | AiCapability.ModelDiscovery);

    public async Task<IReadOnlyList<AiModelInfo>> GetModelsAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            new Uri(Endpoint, "api/tags"),
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty("models", out var models) ||
            models.ValueKind != JsonValueKind.Array)
        {
            throw new AiProviderException("Ollama returned an invalid model list.");
        }

        var result = new List<AiModelInfo>();
        foreach (var model in models.EnumerateArray())
        {
            if (!model.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var modelId = name.GetString();
            if (!string.IsNullOrWhiteSpace(modelId))
            {
                result.Add(new AiModelInfo(modelId, modelId, Info.Id, IsLocal: true));
            }
        }

        return result;
    }

    public async Task<int?> GetContextSizeAsync(
        string modelId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        using var content = new StringContent(
            JsonSerializer.Serialize(new { model = modelId }),
            Encoding.UTF8,
            "application/json");
        using var response = await _httpClient.PostAsync(
            new Uri(Endpoint, "api/show"),
            content,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (document.RootElement.TryGetProperty("model_info", out var modelInfo) &&
            TryFindContextSize(modelInfo, out var contextSize))
        {
            return contextSize;
        }

        if (document.RootElement.TryGetProperty("parameters", out var parameters) &&
            parameters.ValueKind == JsonValueKind.String)
        {
            foreach (var line in parameters.GetString()!.Split('\n'))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && string.Equals(parts[0], "num_ctx", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(parts[1], out contextSize))
                {
                    return contextSize;
                }
            }
        }

        return null;
    }

    private static bool TryFindContextSize(JsonElement element, out int contextSize)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Contains("context_length", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.TryGetInt32(out contextSize))
                {
                    return true;
                }

                if (TryFindContextSize(property.Value, out contextSize))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryFindContextSize(item, out contextSize))
                {
                    return true;
                }
            }
        }

        contextSize = 0;
        return false;
    }

    public async IAsyncEnumerable<AiResponseChunk> ChatAsync(
        AiChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var messages = new List<object>();
        if (request.Context is not null)
        {
            messages.Add(new
            {
                role = "system",
                content = JsonSerializer.Serialize(request.Context, AiJson.Compact)
            });
        }

        foreach (var message in request.Messages)
        {
            messages.Add(new
            {
                role = message.Role.ToString().ToLowerInvariant(),
                content = message.Content
            });
        }

        if (request.AutonomyPolicy != AiAutonomyPolicy.SuggestOnly)
        {
            messages.Insert(0, new
            {
                role = "system",
                content = "For this agent request, return exactly one JSON object with this shape: " +
                    "When you need the host to execute a tool or apply proposed actions, return exactly one JSON " +
                    "object using this shape: " +
                    "{\"text\":\"brief explanation\",\"metadataRequests\":[{\"typeName\":\"exact registered module type name\"}],\"connectionRequests\":[{\"firstNodeId\":\"node GUID\",\"secondNodeId\":\"node GUID\"}],\"commentBlockRequests\":[{\"commentBlockId\":\"comment block GUID\",\"query\":\"text to find\"}],\"boundaryRequests\":[{\"boundaryId\":\"boundary GUID\",\"path\":\"boundary path\",\"query\":\"boundary name or text\"}],\"plan\":{\"id\":\"plan-id\",\"summary\":\"plan summary\",\"tasks\":[{" +
                    "\"id\":\"task-id\",\"title\":\"task title\",\"description\":\"task details\",\"dependsOn\":[],\"actionIds\":[\"stable-action-id\"]}]},\"proposedActions\":[{" +
                    "\"id\":\"stable-action-id\",\"kind\":\"CreateNode, CreateLink, AddLinkDestination, UpdateNode, UpdateParameter, SetBasicParameter, SetScriptedParameter, or ConvertBasicParameterToScriptedParameter\",\"summary\":\"what changes\",\"arguments\":{\"id\":\"UUID copied from context ReservedElementIds for the created node or link\",\"boundaryId\":\"boundary GUID for CreateNode\",\"typeName\":\"registered module type for CreateNode\",\"name\":\"node name for CreateNode\",\"x\":0,\"y\":0,\"originId\":\"origin node GUID for CreateLink or AddLinkDestination\",\"hookName\":\"origin hook name for CreateLink or AddLinkDestination\",\"destinationId\":\"destination node GUID for CreateLink or AddLinkDestination\",\"nodeId\":\"node GUID for parameter updates, or owning module GUID when parameterName is supplied\",\"parameterName\":\"generated parameter hook name when nodeId identifies the owning module\",\"value\":\"literal value for SetBasicParameter or expression for SetScriptedParameter or conversion\",\"isExpression\":false},\"isDestructive\":false}]} . " +
                    "Before proposing any CreateNode action, request metadata for every new module type through " +
                    "metadataRequests using the exact AvailableModules[].TypeName. When metadata is needed, " +
                    "return metadataRequests and an empty proposedActions array, then wait for the metadata " +
                    "results before returning CreateNode or dependent CreateLink actions. Never guess hooks, " +
                    "parameters, requiredness, cardinality, or AiInstructions from a compact module index. " +
                    "CurrentBoundaryModules contains detailed metadata, including AiInstructions, for every " +
                    "module type already present in the current boundary; use it as authoritative guidance " +
                    "when modifying or extending those existing modules. " +
                    "ScriptedParameter expressions use XTMF2 syntax: quoted strings, true/false, non-negative " +
                    "integer and floating-point literals, exact variable names, parentheses, unary !, arithmetic " +
                    "+ - * / ^, comparisons < <= > >= == !=, boolean && and ||, and condition ? whenTrue : whenFalse. " +
                    "Use the context Variables dictionary as the authoritative variable name-to-type lookup. " +
                    "Names must match exactly; local function variables shadow model-system variables. Do not " +
                    "invent variables or use C# syntax, explicit casts, method calls such as .ToString(), " +
                    "commas, or single-quoted strings. There are no methods or general-purpose casts in this " +
                    "language. Addition performs implicit string conversion: a string plus an integer or other " +
                    "value produces a string, so write \"Iteration: \" + CurrentIteration rather than " +
                    "CurrentIteration.ToString(). Numeric addition preserves numeric types when both operands " +
                    "are numeric; comparisons and conditional branches must use compatible types. " +
                    "Use an empty proposedActions array when no edit is needed. Plans must contain small, " +
                    "Use connectionRequests when you need to know whether two existing nodes are connected. " +
                    "Provide exact node IDs from Elements[].Id, return connectionRequests with no dependent " +
                    "actions, and wait for the tool result; the result reports both directions and the exact " +
                    "origin hook name for every matching link. " +
                    "Use commentBlockRequests when you need to retrieve documentation not sufficiently covered " +
                    "by CommentBlocks in the context. Match commentBlockId to CommentBlocks[].Id exactly or " +
                    "provide a short query for header/body text. Return the request without dependent actions " +
                    "and wait for the host result. Comment blocks are documentation only, never model elements, " +
                    "and their IDs must never be used as nodeId, originId, destinationId, or action arguments.id. " +
                    "Use boundaryRequests when you need to inspect elements or documentation in a boundary " +
                    "outside the current context. Match boundaryId or path exactly when known, or use a short " +
                    "query against boundary names, paths, or descriptions. Return boundaryRequests with no " +
                    "dependent actions and wait for the host result. Boundary lookup is read-only; use the " +
                    "returned boundary and element IDs only after they are provided by the host, and do not " +
                    "assume elements from another boundary are in Elements[]. " +
                    "independently executable tasks; each task actionIds entry must refer to a proposedActions id. " +
                    "Keep text to one brief sentence, do not include reasoning or repeat context, and do not use markdown fences. " +
                        "Every action has two different IDs: the top-level id is a stable action identifier, while " +
                        "arguments.id is the model-system element UUID. For CreateNode and CreateLink, arguments.id " +
                        "is required and must be copied from ReservedElementIds. The application, not the model, " +
                            "generates element UUIDs; never use the action identifier as arguments.id. " +
                            "For CreateNode, x and y are canvas coordinates in the same coordinate system as " +
                            "context Elements[].Position. Choose coordinates that keep the default 120 by 50 " +
                            "node rectangle in open space near related nodes; do not default every node to (0,0). " +
                            "A CreateLink action must contain all four non-empty arguments: id, originId, destinationId, " +
                            "and hookName. hookName is the exact non-parameter hook name on the origin node, copied " +
                            "from that origin element's AvailableHooks or from Links[].HookName. AvailableParameters " +
                            "and CurrentBoundaryModules members marked IsParameter are never valid hookName values. " +
                            "For If modules, Condition is a parameter and must be configured with a parameter action; " +
                            "only If True and If False are structural execution hooks. Never use an empty hookName, " +
                            "a destination name, or a parameter name. If no valid origin hook is available, " +
                            "do not propose the CreateLink action. An AddLinkDestination action must contain " +
                            "non-empty originId, destinationId, and hookName, must target an existing " +
                            "AtLeastOne or AnyNumber hook, and must not include arguments.id. Use it only to " +
                            "append a destination to an existing multi-cardinality origin hook such as " +
                            "Execute.To Execute. Never use AddLinkDestination for If True or If False: those " +
                            "hooks are single-cardinality branches. Use CreateLink for an unconnected single " +
                            "hook, and do not add a second destination when it is already occupied. Use SetBasicParameter " +
                            "for a BasicParameter " +
                            "node's literal value and SetScriptedParameter for a ScriptedParameter node's " +
                            "expression. Use ConvertBasicParameterToScriptedParameter to preserve a " +
                            "BasicParameter node while converting it to a compiled expression. ScriptedParameter " +
                            "expressions resolve function-local variables before model-system variables, must " +
                            "evaluate to the parameter's declared type, and use quoted literals for strings. " +
                            "For a generated parameter such as Message, either target the generated parameter " +
                            "node ID or target the owning module node ID with parameterName set to the exact " +
                            "hook name Message. " +
                            "Do not use the generic UpdateParameter action when a specific action applies. " +
                            "For a normal user-facing answer that needs no host tool or proposed action, respond " +
                            "directly in natural language or Markdown and do not wrap it in JSON. The text field is " +
                            "the user-facing explanation when a JSON tool/action envelope is required."
            });
        }

        var generationOptions = request.GenerationOptions ??
            (request.AutonomyPolicy == AiAutonomyPolicy.SuggestOnly
                ? null
                : new AiGenerationOptions(
                    Temperature: 0.15,
                    TopP: 0.9,
                    RepeatPenalty: 1.15,
                    RepeatLastN: 256));
        var options = new Dictionary<string, object>
        {
            ["num_predict"] = request.MaxOutputTokens > 0 ? request.MaxOutputTokens : 1024
        };
        if (generationOptions?.Temperature is { } temperature)
        {
            options["temperature"] = temperature;
        }

        if (generationOptions?.TopP is { } topP)
        {
            options["top_p"] = topP;
        }

        if (generationOptions?.RepeatPenalty is { } repeatPenalty)
        {
            options["repeat_penalty"] = repeatPenalty;
        }

        if (generationOptions?.RepeatLastN is { } repeatLastN)
        {
            options["repeat_last_n"] = repeatLastN;
        }

        var body = JsonSerializer.Serialize(new
        {
            model = request.ModelId,
            messages,
            options,
            think = request.AutonomyPolicy != AiAutonomyPolicy.SuggestOnly ? false : (bool?)null,
            stream = true
        });

        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(Endpoint, "api/chat"))
        {
            Content = content
        };
        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        var completeResponse = request.AutonomyPolicy == AiAutonomyPolicy.SuggestOnly
            ? null
            : new StringBuilder();
        var completeThinking = request.AutonomyPolicy == AiAutonomyPolicy.SuggestOnly
            ? null
            : new StringBuilder();
        var emittedAgentTextLength = 0;
        var emittedAgentThinkingLength = 0;
        var emittedAgentActionIds = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var text = string.Empty;
            if (root.TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var messageContent) &&
                messageContent.ValueKind == JsonValueKind.String)
            {
                text = messageContent.GetString() ?? string.Empty;
            }

            var thinking = message.TryGetProperty("thinking", out var thinkingProperty) &&
                           thinkingProperty.ValueKind == JsonValueKind.String
                ? thinkingProperty.GetString()
                : null;

            var isComplete = root.TryGetProperty("done", out var done) &&
                done.ValueKind == JsonValueKind.True;
            var isTruncated = root.TryGetProperty("done_reason", out var doneReason) &&
                doneReason.ValueKind == JsonValueKind.String &&
                string.Equals(doneReason.GetString(), "length", StringComparison.OrdinalIgnoreCase);
            if (completeResponse is not null)
            {
                completeResponse.Append(text);
                if (!string.IsNullOrEmpty(thinking))
                {
                    completeThinking!.Append(thinking);
                }
                var partialText = ExtractPartialText(completeResponse.ToString());
                var partialThinking = completeThinking!.ToString();
                var textDelta = TakeDelta(partialText, ref emittedAgentTextLength);
                var thinkingDelta = TakeDelta(partialThinking, ref emittedAgentThinkingLength);
                var partialActions = TakeNewActions(
                    ExtractPartialAgentActions(completeResponse.ToString()),
                    emittedAgentActionIds);
                if (isComplete)
                {
                    var structured = ParseAgentResponse(completeResponse.ToString());
                    if (structured is null && !StartsWithJsonObject(completeResponse.ToString()))
                    {
                        yield return new AiResponseChunk(
                            completeResponse.ToString(),
                            partialActions,
                            IsComplete: true,
                            Thinking: thinkingDelta);
                        yield break;
                    }

                    var actions = structured is null
                        ? partialActions
                        : TakeNewActions(
                            structured.ProposedActions ?? Array.Empty<AiActionProposal>(),
                            emittedAgentActionIds);
                    yield return structured is null
                        ? new AiResponseChunk(
                            textDelta,
                            actions,
                            IsComplete: true,
                            Thinking: thinkingDelta,
                            IsTruncated: true,
                            ContinuationContext: completeResponse.ToString())
                        : structured with
                        {
                            Text = textDelta,
                            Thinking = thinkingDelta,
                            ProposedActions = actions,
                            Plan = structured.Plan,
                            IsTruncated = isTruncated,
                            ContinuationContext = isTruncated ? completeResponse.ToString() : null
                        };
                    yield break;
                }

                if (textDelta.Length > 0 || thinkingDelta.Length > 0 || partialActions.Count > 0)
                {
                    yield return new AiResponseChunk(
                        textDelta,
                        partialActions,
                        Thinking: thinkingDelta,
                        ContinuationContext: completeResponse.ToString());
                }

                continue;
            }

            yield return new AiResponseChunk(text, [], isComplete, thinking, isTruncated);

            if (isComplete)
            {
                yield break;
            }
        }
    }

    internal static AiResponseChunk? ParseAgentResponse(string response)
    {
        if (!IsCompleteJsonObject(response))
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<AiResponseChunk>(response, ActionJsonOptions);
            return parsed is null
                ? null
                : parsed with { IsComplete = true };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsCompleteJsonObject(string value)
    {
        var objectDepth = 0;
        var started = false;
        var inString = false;
        var escaped = false;
        var objectEnded = false;

        foreach (var character in value)
        {
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                continue;
            }

            if (!started)
            {
                if (character != '{')
                {
                    return false;
                }

                started = true;
                objectDepth = 1;
                continue;
            }

            if (objectEnded)
            {
                return false;
            }

            if (character == '"')
            {
                inString = true;
            }
            else if (character == '{')
            {
                objectDepth++;
            }
            else if (character == '}' && --objectDepth == 0)
            {
                objectEnded = true;
            }
            else if (character == '}' && objectDepth < 0)
            {
                return false;
            }
        }

        return started && objectEnded && !inString && !escaped && objectDepth == 0;
    }

    private static bool StartsWithJsonObject(string value)
    {
        return value.TrimStart().StartsWith('{');
    }

    internal static IReadOnlyList<AiActionProposal> ExtractPartialAgentActions(string response)
    {
        var actionsPropertyIndex = response.IndexOf("\"proposedActions\"", StringComparison.Ordinal);
        if (actionsPropertyIndex < 0)
        {
            return Array.Empty<AiActionProposal>();
        }

        var arrayStart = response.IndexOf('[', actionsPropertyIndex);
        if (arrayStart < 0)
        {
            return Array.Empty<AiActionProposal>();
        }

        var actions = new List<AiActionProposal>();
        var index = arrayStart + 1;
        while (index < response.Length)
        {
            while (index < response.Length && char.IsWhiteSpace(response[index]))
            {
                index++;
            }

            if (index >= response.Length || response[index] != '{')
            {
                break;
            }

            var objectEnd = FindJsonObjectEnd(response, index);
            if (objectEnd < 0)
            {
                break;
            }

            try
            {
                var action = JsonSerializer.Deserialize<AiActionProposal>(
                    response[index..(objectEnd + 1)],
                    ActionJsonOptions);
                if (action is not null)
                {
                    actions.Add(action);
                }
            }
            catch (JsonException)
            {
                // Keep the incomplete or malformed action for the next continuation.
            }

            index = objectEnd + 1;
            while (index < response.Length && char.IsWhiteSpace(response[index]))
            {
                index++;
            }

            if (index < response.Length && response[index] == ',')
            {
                index++;
                continue;
            }

            break;
        }

        return actions;
    }

    private static IReadOnlyList<AiActionProposal> TakeNewActions(
        IReadOnlyList<AiActionProposal> actions,
        HashSet<string> emittedActionIds)
    {
        var newActions = new List<AiActionProposal>();
        foreach (var action in actions)
        {
            if (emittedActionIds.Add(action.Id))
            {
                newActions.Add(action);
            }
        }

        return newActions;
    }

    private static int FindJsonObjectEnd(string value, int objectStart)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var index = objectStart; index < value.Length; index++)
        {
            var character = value[index];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (character == '"')
            {
                inString = true;
            }
            else if (character == '{')
            {
                depth++;
            }
            else if (character == '}' && --depth == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static string ExtractPartialText(string response)
    {
        var propertyIndex = response.IndexOf("\"text\"", StringComparison.Ordinal);
        if (propertyIndex < 0)
        {
            return string.Empty;
        }

        var valueStart = response.IndexOf('"', propertyIndex + 6);
        if (valueStart < 0)
        {
            return string.Empty;
        }

        var escaped = false;
        var valueEnd = response.Length;
        for (var index = valueStart + 1; index < response.Length; index++)
        {
            var character = response[index];
            if (character == '"' && !escaped)
            {
                valueEnd = index;
                break;
            }

            escaped = character == '\\' && !escaped;
            if (character != '\\')
            {
                escaped = false;
            }
        }

        var rawValue = response[(valueStart + 1)..valueEnd];
        while (rawValue.Length > 0)
        {
            try
            {
                return JsonSerializer.Deserialize<string>("\"" + rawValue + "\"") ?? string.Empty;
            }
            catch (JsonException)
            {
                if (rawValue[^1] != '\\')
                {
                    return string.Empty;
                }

                rawValue = rawValue[..^1];
            }
        }

        return string.Empty;
    }

    private static string TakeDelta(string value, ref int emittedLength)
    {
        if (value.Length <= emittedLength)
        {
            return string.Empty;
        }

        var delta = value[emittedLength..];
        emittedLength = value.Length;
        return delta;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        throw new AiProviderException(
            $"Ollama request failed with {(int)response.StatusCode} ({response.ReasonPhrase}): {detail}");
    }
}