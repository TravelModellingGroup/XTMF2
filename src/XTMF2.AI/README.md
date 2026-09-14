# XTMF2.AI

`XTMF2.AI` contains the provider-neutral contracts and adapters used to add AI assistance to XTMF2.GUI.

## Current scope

- `IAiProvider` supports model discovery and streaming chat.
- `OllamaProvider` connects to a local Ollama server through its HTTP API.
- `AiContextSnapshot` is the deliberately limited context shape sent to providers.
- `AiActionProposal` and `AiActionBatch` represent structured model-system edits.
- `AiPlan` and `AiPlanTask` represent dependency-ordered work that can be executed in small batches.
- `AiActionExecutionResult.FailedActionId` identifies exactly which proposed action failed validation, so correction feedback can target one action instead of an entire batch.
- `AiActionValidation` enforces explicit approval and destructive-action confirmation before edits are applied.
- `AiProviderRegistry` provides deterministic provider lookup and model discovery.
- `AiAssistantService` connects provider selection, streaming chat, policy validation, and action application.

Ask mode returns ordinary responses. Agent mode returns structured proposals for explicit review and application; destructive actions require a separate explicit approval.

## Architecture boundary

This project does not reference Avalonia, `ModelSystemSession`, `HostBus`, or `RunBus`. Providers return text and structured proposals; the GUI layer is responsible for projecting model-system context, validating supported operations, and applying approved operations through `ModelSystemSession` so edits remain undoable.

Providers must not receive credentials in `AiContextSnapshot`, and this project does not persist credentials. Provider-specific authentication belongs in the adapter or host application's credential-store integration.

## Agent orchestration

XTMF2 has an implemented orchestrator, but it is currently host-owned rather than a standalone class in `XTMF2.AI`. `AiAssistantViewModel` in `XTMF2.GUI` owns the turn state machine and user-facing state; `AiAssistantService` dispatches provider calls and validates approved actions; `ModelSystemContextProjector` supplies the current model-system snapshot; and `ModelSystemActionApplier` translates accepted actions into undoable `ModelSystemSession` commands. The provider remains responsible for inference and structured response parsing, not for mutating the model system.

### Turn algorithm

An `Ask` turn follows this path:

1. Validate that a prompt and model are selected, clear the prior turn state, and create a cancellation token.
2. Project the current boundary into an `AiContextSnapshot` containing existing elements, links, parameters, hooks, variables, and the available module catalog.
3. Send the prompt to the selected provider as a streaming request using `SuggestOnly` policy and the Ask output budget.
4. Append response and thinking chunks as they arrive. Structured proposals and plans are collected for display, but Ask mode never applies actions.

An `Agent` turn adds a two-phase workflow:

1. **Design:** send the prompt with the current context and force `SuggestOnly`. The model returns concise prose naming exact module types, node names, and hook names; it must not return actions or a plan.
2. **Build:** send the prompt again with the same context plus the design summary. The model returns one structured response containing concise text, optional `proposedActions`, and an optional dependency-aware `plan`.
3. Validate action requests through `AiAssistantService` before exposing proposals for review.
4. Validate the complete proposed action set through the host's non-mutating model-system validator. If a required structural hook is unconnected, ask the model whether it intends to add the link; an unchanged action set confirms that the omission is intentional. Other validation failures are fed back as corrections before exposing proposals.
5. In `SuggestOnly` or `ApproveBatch`, leave validated proposals visible for review. The user can select individual actions, approve the batch, and apply it through the GUI.
6. In `Autonomous`, apply non-destructive proposals automatically. If a plan is present, execute only ready tasks in dependency order; otherwise execute the proposed actions as one batch. Stop at the first failed task or action.

The three policies have distinct meanings:

- `SuggestOnly` permits inference and proposal display but no action execution.
- `ApproveBatch` requires explicit approval before a selected batch is executed.
- `Autonomous` permits automatic execution, but destructive actions still require separate destructive-action approval. The current GUI action applier supports `CreateNode`, `CreateLink`, `AddLinkDestination`, `UpdateNode`, and `UpdateParameter`; other action kinds are rejected as unsupported.

### Agent tools

The model does not receive arbitrary code execution or direct access to the runtime. Its tool surface is the structured action schema:

Agent responses may also include bounded `metadataRequests` and `connectionRequests` arrays. `metadataRequests` contains exact registered module type names; `connectionRequests` contains two exact existing node IDs. The GUI resolves these requests through `ModelSystemContextProjector`, returns module descriptions or matching links as tool-style follow-up messages, and asks the provider to produce one complete response using the result. Connection results report both endpoint IDs, the direction, disabled state, and the origin hook name for each matching link. At most eight requests are resolved per turn and the assistant stops after three request cycles. Unknown type names or invalid node IDs return empty/null results rather than exposing arbitrary reflection or runtime objects.

- `CreateNode`: create a configured module instance in the current boundary using a registered module type, name, position, and caller-supplied reserved UUID.
- `CreateLink`: connect an origin node hook to a destination node using a caller-supplied reserved UUID.
- `AddLinkDestination`: append a destination to an existing multi-cardinality origin hook using the origin node ID, hook name, and destination node ID. It does not allocate a new link ID.
- `UpdateNode`: rename an existing node by stable node ID.
- `UpdateParameter`: set a value or expression on an existing node by stable node ID and parameter value.
- `SetBasicParameter`: set a literal value on a BasicParameter node.
- `SetScriptedParameter`: set a compiled expression on a ScriptedParameter node.
- `ConvertBasicParameterToScriptedParameter`: preserve a BasicParameter node and its links while converting it to a ScriptedParameter with a compiled expression. Expressions resolve function-local variables before model-system variables, must evaluate to the declared parameter type, and use quoted literals for strings.

ScriptedParameter expressions use the built-in expression language: quoted string literals, `true`/`false`, non-negative integer and floating-point literals, exact variable names, parentheses, unary `!`, arithmetic `+ - * / ^`, comparisons `< <= > >= == !=`, boolean `&&` and `||`, and conditional `condition ? whenTrue : whenFalse`. The context snapshot's `Variables` dictionary is the authoritative name-to-type lookup. Local function variables shadow model-system variables with the same name. C# syntax, method calls, commas, and single-quoted strings are not supported.

Each action has an ID, kind, summary, JSON arguments, and destructive flag. The applier validates IDs, module types, hooks, parameters, and argument shapes before applying anything. Create-node operations run before updates, create-link operations run after creation, and destination-append operations run after link creation. The whole accepted batch is committed through the model-system command buffer and can be undone as one operation.

For multi-step work, the model can return an `AiPlan`. Each `AiPlanTask` names its dependencies and the action IDs it owns. Tasks become `Ready` only when all referenced dependencies are completed, and each task is applied as a separate action batch. Completed task actions are removed from the pending proposal set; a failed task stops autonomous execution and reports the failed action when available.

### Bounded recovery

The orchestrator handles two different incomplete-response cases:

- If Ollama stops at its output limit, the assistant extracts complete proposals already present, discards the incomplete JSON envelope, and asks for one fresh complete response using a bounded progress summary. The summary preserves the response tail and emitted action IDs, while the next context snapshot preserves provisional IDs for newly proposed nodes and links. Continuation cycles are capped at the configured limit, defaulting to 100 and constrained to 1-100. Repeated normalized continuation state is detected and stops the loop early.
- If an autonomous action batch fails, the failed action ID and error are converted into targeted correction feedback. Pending proposals and plans are cleared, the current model-system context is re-read, and the original request is retried up to two times. The correction asks the model to regenerate only the failed action while preserving the other action IDs, kinds, and arguments.
- If an explicitly applied selection fails, the same targeted correction feedback starts one fresh provider turn. Stale proposals are cleared and the returned actions remain available for review; if the correction turn returns no actions, the original failure remains visible.

Cancellation stops the provider stream and action execution through the request cancellation token. Partial response and thinking text remain visible when a request is stopped or the provider fails.

## Ollama

```csharp
using var httpClient = new HttpClient();
var provider = new OllamaProvider(
    httpClient,
    new Uri("http://localhost:11434"));

var models = await provider.GetModelsAsync();
```

The adapter uses `GET /api/tags` for model discovery and `POST /api/chat` with streaming enabled. Responses are read with `ResponseHeadersRead` so Ollama's first token or thinking fragment is available immediately instead of waiting for the complete response. Requests also send a bounded `num_predict` output budget (768 tokens for Agent mode and 1024 for Ask mode). Agent requests use conservative Ollama generation settings (`temperature`, `top_p`, `repeat_penalty`, and `repeat_last_n`) to reduce repetitive output. Ollama is optional; connection failures are returned as `AiProviderException` and must not prevent XTMF2.GUI from starting.

The GUI assistant uses `llama3.2` as its default editable model. The GUI settings persist the provider ID, model ID, Ollama endpoint, and action policy (`SuggestOnly`, `ApproveBatch`, or `Autonomous`) as non-secret preferences. The endpoint is validated when the application starts; invalid or unsupported values fall back to `http://localhost:11434`. Credentials are not written to these settings. Ollama Ask requests stream ordinary text. Ollama Agent requests receive an explicit structured-output instruction and are parsed as one JSON object containing `text`, an optional `plan`, and `proposedActions`; supported proposals are then shown for review and application in the assistant window. Planned tasks reference proposal IDs and can be run individually after their dependencies complete. Autonomous mode executes ready planned tasks as separate action batches, stopping at the first failure.

When applying a batch fails, `ModelSystemActionApplier` reports which proposed action id caused the failure. The assistant turns that into a targeted correction message ("Action 'a3' (CreateLink) failed: ... Regenerate only that action ...") instead of a generic error, so the next attempt only needs to fix the one broken action. In Autonomous mode, this correction is fed back automatically: `AiAssistantViewModel` retries up to `MaxAutonomousApplyRetries` times, clearing stale proposals and re-reading the current model-system snapshot before each retry, without requiring the user to resend the prompt.

Agent mode splits each turn into a Design phase and a Build phase. The Design phase requests plain text only (forcing `AiAutonomyPolicy.SuggestOnly` for that request regardless of the configured policy) asking the model to name the exact module types, node names, and hook names it intends to use, without emitting any actions or a plan. The Build phase then asks the model to implement exactly that confirmed design using the normal structured-action schema. `StatusText` reflects the active phase ("Designing", then "Building"; "Computing" in Ask mode, which does not use phases), and `Thinking` is cleared when moving from Design to Build so reasoning from one phase does not linger under the other. `Response` accumulates across both phases so the design explanation stays visible above the build result.

Model discovery is opt-in from the assistant pane. Refreshing the model list calls the selected provider's `GetModelsAsync` implementation, so an unavailable Ollama server is reported in the assistant pane rather than blocking application startup.

## Provider registration

Hosts register provider adapters once during application composition:

```csharp
var providers = new AiProviderRegistry();
providers.Register(ollamaProvider);
var models = await providers.GetModelsAsync("ollama");
```

Provider IDs are case-insensitive and must be unique. The registry intentionally does not create providers or persist selections; those responsibilities belong to the host application's composition and settings layers.

## External control API

`AiControlServer` provides an opt-in authenticated HTTP API around `AiAssistantService`. Hosts should bind it to loopback unless they deliberately provide a separately secured network boundary:

```csharp
var controlServer = new AiControlServer(
    assistantService,
    "http://127.0.0.1:45678/",
    bearerToken);
controlServer.Start();
```

Every request must include `Authorization: Bearer <token>`. The API exposes `GET /v1/models?providerId=...`, `POST /v1/chat`, and `POST /v1/actions`. Chat responses use the same `AiResponseChunk` records as the GUI; request `Accept: application/x-ndjson` for newline-delimited streaming or `Accept: text/event-stream` for SSE. Responses include `X-Request-ID`; callers may provide that header to correlate retries. Hosts may receive non-sensitive `AiControlAuditEvent` records through the optional audit callback. Concurrent requests are bounded to four by default and can be changed with the `maxConcurrentRequests` constructor argument. Requests are cancelled after five minutes by default; hosts can change that with `requestTimeout`. Action requests still pass through `AiActionPolicy` and the configured `IAiActionApplier`; callers must explicitly provide approval flags, and destructive actions require both approvals. The server is host-owned and must be disposed with `DisposeAsync()` during shutdown. Tokens should be generated or retrieved through a host secret mechanism rather than stored in source control or ordinary settings.

## Assistant service

The host supplies an `IAiActionApplier` implementation. The GUI implementation should translate each approved `AiActionBatch` into `ModelSystemSession` calls and return the affected element IDs for navigation. Provider calls remain outside the session and can be cancelled independently.

The current GUI applier supports non-destructive `CreateNode`, `CreateLink`, `UpdateNode`, and `UpdateParameter` actions. Creation and update argument shapes are:

```json
{
    "id": "reserved-node-guid",
    "boundaryId": "stable-boundary-guid",
    "typeName": "registered module type name",
    "name": "new node name",
    "x": 100,
    "y": 100
}
```

```json
{
    "id": "reserved-link-guid",
    "originId": "origin-node-guid",
    "hookName": "origin hook name",
    "destinationId": "destination-node-guid"
}
```

These are the arguments for `CreateNode` and `CreateLink`, respectively. The `id` is required and is committed as the new element's stable GUID. The orchestrator generates a pool of reserved element UUIDs and places them in `AiContextSnapshot.ReservedElementIds`; the model must copy an unused value from that list and reuse it for later actions in the same batch. The model must not invent or regenerate element UUIDs. The type name must match a registered module type, and the link hook must exist on the origin node. A batch can create a node and then link to that node using its reserved `id` before the batch is committed.

```json
{
    "nodeId": "stable-node-guid",
    "value": "new value or expression",
    "isExpression": false
}
```

The GUI applies a batch through the session command buffer, so the accepted batch can be undone as one operation. Unsupported or malformed actions are rejected before they are applied.

The GUI opens the assistant in a separate modeless window from the model-system editor header. `Ask` mode always sends suggest-only requests and does not allow applying proposed edits. `Agent` mode enables the configured autonomy policy and exposes the existing reviewed action-application flow, including undo through `ModelSystemSession`.

The assistant pane displays streamed proposals for review. Each proposal can be individually selected or rejected before `Apply actions` submits the selected proposals as one batch; suggest-only mode rejects execution, while approve-batch permits explicit application. Autonomous mode applies streamed non-destructive proposals automatically, but destructive actions still require the explicit `Allow destructive actions` confirmation in the pane.

When Ollama reports that a response stopped at its output-length limit, the GUI automatically asks the model to compact the response and continue. The maximum number of continuation cycles is configurable in the GUI settings and defaults to 100, with values constrained to 1-100 per request. The assistant shows `Computing`, `Compacting`, and continuation status while this happens, and preserves partial response and thinking text if the provider stops before completion.

Continuation retries use a rolling state rather than appending every previous retry to the conversation. The original instructions and user request are retained, while a bounded progress summary is supplied in a new user message. The summary contains the completed explanation and already emitted action IDs; the incomplete JSON envelope is discarded, and the model is asked to return one fresh complete structured response. The current context snapshot supplies reserved IDs and proposed action details again. The assistant also limits accumulated thinking text and instructs the model to continue without restarting or repeating its reasoning.

Agent responses are deliberately concise: the structured explanation is limited to one brief sentence and the provider instructs the model not to repeat context or expose unnecessary reasoning. Continuation states are normalized and tracked; if the model emits the same state again, the assistant stops early with a loop warning instead of consuming the remaining continuation budget.

`ModelSystemContextProjector` creates a compact provider context from the current boundary. It includes stable element IDs, names, short type names, descriptions, parameter representations, each element's `AvailableParameters`, `AvailableHooks`, and `AvailableHookStates`, links, and an `AvailableModules` type index. `AvailableHookStates` identifies each projected hook as a parameter or structural hook and reports `Required`, `IsConnected`, `Cardinality`, and `PassesExecution`, so the model can see which required links are still missing. Each index entry includes the exact module `TypeName`, display name, short description, and documentation link; its `Members` array is intentionally empty so the full registered module catalog does not consume every request's context window. The model can return a bounded `metadataRequests` array with exact type names, and the GUI responds with the detailed module description containing `AiInstructions` and member metadata for parameters and submodule hooks: `IsParameter`, type name, description, requiredness, cardinality, default value, and `PassesExecution`. This includes the built-in `XTMF2.RuntimeModules` and loaded extension modules. Empty/default fields are omitted during provider serialization; index descriptions are limited to 240 characters, detailed descriptions and AI instructions to 1,000 characters, and parameter representations to 2,000 characters, with a truncation marker. Runtime module authors can use the provider-neutral `AiModuleInstructionsAttribute` for concise composition rules, such as explaining that `WriteToLogA.Message` uses a generated parameter child or that `Execute.To Execute` is a multi-destination execution-flow hook. It can restrict the snapshot to selected elements and does not include credentials, filesystem data, or direct model objects.

The assistant also receives focused model-system guidance: a boundary groups elements and links; a `Node` is a configured instance of a registered module type; RuntimeModules are ordinary module types used as nodes, with parameters configured from parameter members and behavior composed by linking submodule members. `PassesExecution` identifies hooks that participate in the execution chain. A `FunctionInstance` instantiates a reusable function template and may expose template-derived parameters or destinations. `Elements[].Kind`, `Links`, and `AvailableModules` distinguish existing elements, connections, and available module definitions; module definitions must never be confused with existing element IDs.

Agent requests also receive an ID-resolution instruction. Models must copy existing IDs exactly from the context: use `CurrentBoundaryId` for `CreateNode.boundaryId`, `Elements[].Id` for existing node IDs, and `Links[].OriginId`, `Links[].DestinationId`, and `Links[].HookName` for `CreateLink`. For new nodes and links, they must copy IDs from `ReservedElementIds`, then reuse those reserved IDs throughout the uncommitted proposal batch. They must not generate UUIDs themselves and must decline to propose a link when either existing endpoint or its hook is absent from the supplied context.

When earlier proposals in the same request reserve new elements, the next context snapshot includes them in `Elements` with `IsProvisional: true`. These are valid unapplied IDs for later actions in the same batch, including `CreateLink`; they are not yet committed model elements and must not be treated as existing IDs in a separate request.

During a truncated Ollama Agent response, complete action objects are extracted from the partial `proposedActions` array and emitted before the outer JSON envelope finishes. This allows newly reserved node IDs to enter the provisional context before a continuation generates links that reference them. Incomplete action objects are discarded for the retry, while the next context snapshot reconstructs the valid provisional state from actions already emitted.

The assistant performs a local context-summary checkpoint after five streaming request turns, or sooner when the estimated serialized request context exceeds 60% of the discovered model context size. The checkpoint keeps the original instructions, replaces accumulated progress with a bounded summary of the latest response and emitted action IDs, and sends the reduced context on the next request. Request usage is estimated locally; exact tokenization remains provider/model-specific.

## Testing

Provider tests should use an injected fake `HttpMessageHandler` or equivalent transport. Do not require a running Ollama server in automated tests.