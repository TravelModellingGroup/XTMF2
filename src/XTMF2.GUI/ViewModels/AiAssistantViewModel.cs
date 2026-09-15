using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XTMF2.AI;
using XTMF2.GUI.AI;
using XTMF2.ModelSystemConstruct;

namespace XTMF2.GUI.ViewModels;

public sealed partial class AiAssistantViewModel : ObservableObject, IDisposable
{
    public const int MaximumAllowedCompactionCycles = 100;
    private const int MaxAutonomousApplyRetries = 2;
    private const int ReservedElementIdPoolSize = 32;
    private const int SummaryTurnThreshold = 5;
    private const int MaximumMetadataCycles = 3;

    private enum AiApplyOutcome
    {
        NotApplicable,
        Applied,
        Failed
    }
    private readonly AiAssistantService _service;
    private readonly ModelSystemContextProjector _contextProjector;
    private readonly Func<Boundary> _currentBoundary;
    private readonly int _maxCompactionCycles;
    private readonly List<string> _reservedElementIds = new();
    private readonly List<AiMessage> _askConversation = new();
    private CancellationTokenSource? _requestCancellation;
    private AiAssistantMessageViewModel? _activeAssistantMessage;
    private string _lastSubmittedPrompt = string.Empty;

    [ObservableProperty]
    private string _prompt = string.Empty;

    [ObservableProperty]
    private string _response = string.Empty;

    [ObservableProperty]
    private string _thinking = string.Empty;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private AiAssistantMode _mode = AiAssistantMode.Ask;

    private bool _userCancelledRequest;

    [ObservableProperty]
    private string _modelId = "llama3.2";

    [ObservableProperty]
    private bool _isDiscoveringModels;

    [ObservableProperty]
    private bool _isApplyingActions;

    [ObservableProperty]
    private bool _isToolActivityExpanded;

    [ObservableProperty]
    private string _modelContextSizeText = "Context size: unavailable";

    [ObservableProperty]
    private string _latestContextUsageText = "Latest request context: not sent";

    public ObservableCollection<string> AvailableModels { get; } = new();

    public IReadOnlyList<AiAssistantMode> AvailableModes { get; } =
        [AiAssistantMode.Ask, AiAssistantMode.Agent];

    public ObservableCollection<AiActionProposalViewModel> ProposedActions { get; } = new();

    public ObservableCollection<AiToolInvocationViewModel> ToolInvocations { get; } = new();

    public ObservableCollection<AiPlanTaskViewModel> PlanTasks { get; } = new();

    public ObservableCollection<AiAssistantMessageViewModel> Conversation { get; } = new();

    public IRelayCommand NewSessionCommand { get; }

    [ObservableProperty]
    private string _planSummary = string.Empty;

    public bool HasPlan => PlanTasks.Count > 0;

    public bool HasProposedActions => ProposedActions.Count > 0;

    public bool CanShowProposedActions => Mode == AiAssistantMode.Agent && !IsBusy && HasProposedActions;

    public bool CanApplyActions => Mode == AiAssistantMode.Agent &&
        !IsBusy && !IsApplyingActions && HasProposedActions;

    public bool IsAskMode => Mode == AiAssistantMode.Ask;

    public bool IsAgentMode => Mode == AiAssistantMode.Agent;

    public string ProviderId { get; }

    public AiAssistantService Service => _service;

    public AiAssistantViewModel(
        AiAssistantService service,
        ModelSystemContextProjector contextProjector,
        Func<Boundary> currentBoundary,
        string modelId = "llama3.2",
        string providerId = "ollama",
        int maxCompactionCycles = MaximumAllowedCompactionCycles)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _contextProjector = contextProjector ?? throw new ArgumentNullException(nameof(contextProjector));
        _currentBoundary = currentBoundary ?? throw new ArgumentNullException(nameof(currentBoundary));
        ModelId = string.IsNullOrWhiteSpace(modelId) ? "llama3.2" : modelId;
        ProviderId = string.IsNullOrWhiteSpace(providerId) ? "ollama" : providerId;
        _maxCompactionCycles = Math.Clamp(maxCompactionCycles, 1, MaximumAllowedCompactionCycles);
        ProposedActions.CollectionChanged += OnProposedActionsChanged;
        NewSessionCommand = new RelayCommand(NewSession);
    }

    private void OnProposedActionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasProposedActions));
        OnPropertyChanged(nameof(CanShowProposedActions));
        OnPropertyChanged(nameof(CanApplyActions));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanShowProposedActions));
        OnPropertyChanged(nameof(CanApplyActions));
    }

    partial void OnIsApplyingActionsChanged(bool value)
    {
        OnPropertyChanged(nameof(CanApplyActions));
    }

    partial void OnResponseChanged(string value)
    {
        if (_activeAssistantMessage is not null)
        {
            _activeAssistantMessage.Content = value;
        }
    }

    partial void OnThinkingChanged(string value)
    {
        if (_activeAssistantMessage is not null)
        {
            _activeAssistantMessage.Thinking = value;
        }
    }

    partial void OnModeChanged(AiAssistantMode value)
    {
        OnPropertyChanged(nameof(IsAskMode));
        OnPropertyChanged(nameof(IsAgentMode));
        OnPropertyChanged(nameof(CanShowProposedActions));
        OnPropertyChanged(nameof(CanApplyActions));

        if (value == AiAssistantMode.Ask)
        {
            ProposedActions.Clear();
            ToolInvocations.Clear();
            PlanTasks.Clear();
            PlanSummary = string.Empty;
            Error = null;
        }
    }

    [RelayCommand]
    private async Task RefreshModelsAsync()
    {
        if (IsBusy || IsDiscoveringModels)
        {
            return;
        }

        IsDiscoveringModels = true;
        Error = null;
        try
        {
            var models = await _service.GetModelsAsync(ProviderId);
            AvailableModels.Clear();
            foreach (var model in models)
            {
                AvailableModels.Add(model.Id);
            }

            var matchingModel = AvailableModels.FirstOrDefault(
                model => string.Equals(model, ModelId, StringComparison.OrdinalIgnoreCase));
            if (matchingModel is not null)
            {
                ModelId = matchingModel;
            }

            await UpdateModelContextSizeAsync();
        }
        catch (Exception exception)
        {
            Error = exception.Message;
        }
        finally
        {
            IsDiscoveringModels = false;
        }
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(Prompt))
        {
            return;
        }

        var prompt = Prompt.Trim();
        Prompt = string.Empty;
        _lastSubmittedPrompt = prompt;
        _requestCancellation?.Dispose();
        _requestCancellation = new CancellationTokenSource();
        IsBusy = true;
        IsToolActivityExpanded = true;
        StatusText = "Computing";
        var correctionFeedback = Error;
        Error = null;
        _activeAssistantMessage = null;
        Response = string.Empty;
        Thinking = string.Empty;
        _activeAssistantMessage = new AiAssistantMessageViewModel(false, string.Empty);
        _activeAssistantMessage.IsStreaming = true;
        Conversation.Add(new AiAssistantMessageViewModel(true, prompt));
        Conversation.Add(_activeAssistantMessage);
        _userCancelledRequest = false;
        ProposedActions.Clear();
        ToolInvocations.Clear();
        PlanTasks.Clear();
        PlanSummary = string.Empty;

        try
        {
            if (string.IsNullOrWhiteSpace(ModelId))
            {
                Error = "Choose an Ollama model before sending a request.";
                return;
            }

            await UpdateModelContextSizeAsync();

            if (Mode == AiAssistantMode.Ask)
            {
                await RunAskTurnAsync(prompt);
            }
            else
            {
                var attempt = 0;
                while (true)
                {
                    var outcome = await RunAgentTurnAsync(correctionFeedback, attempt > 0, prompt);
                    if (outcome != AiApplyOutcome.Failed ||
                        attempt >= MaxAutonomousApplyRetries ||
                        _userCancelledRequest)
                    {
                        break;
                    }

                    attempt++;
                    correctionFeedback = Error;
                    Response = BuildRetryNotice(correctionFeedback, attempt);
                    Error = null;
                    ProposedActions.Clear();
                    PlanTasks.Clear();
                    PlanSummary = string.Empty;
                    Thinking = string.Empty;
                    StatusText = $"Retrying after action failure ({attempt}/{MaxAutonomousApplyRetries})";
                }
            }
        }
        catch (OperationCanceledException)
        {
            var hasPartialOutput = !string.IsNullOrWhiteSpace(Response) ||
                                   !string.IsNullOrWhiteSpace(Thinking);
            Error = _userCancelledRequest
                ? hasPartialOutput
                    ? "The AI request was stopped before it finished. Partial response and thinking are shown above."
                    : "The AI request was stopped before any response was received."
                : hasPartialOutput
                    ? "The AI provider stopped responding before the request finished. Partial response and thinking are shown above."
                    : "The AI provider stopped responding before any response was received.";
            StatusText = "Stopped";
        }
        catch (Exception exception)
        {
            Error = exception.Message;
            StatusText = "Stopped";
        }
        finally
        {
            Thinking = string.Empty;
            if (_activeAssistantMessage is not null)
            {
                _activeAssistantMessage.IsStreaming = false;
            }
            IsBusy = false;
            IsToolActivityExpanded = false;
            if (string.IsNullOrWhiteSpace(Error))
            {
                StatusText = "Ready";
            }
        }
    }

    private async Task RunAskTurnAsync(string prompt)
    {
        var contextMessages = BuildAskContextMessages();
        var askMessages = new List<AiMessage>(contextMessages.Count + _askConversation.Count + 1);
        askMessages.AddRange(contextMessages);
        askMessages.AddRange(GetCompactedAskHistory(contextMessages));
        askMessages.Add(new AiMessage(AiRole.User, prompt));
        _askConversation.Add(new AiMessage(AiRole.User, prompt));

        await RunStreamingTurnAsync(
            askMessages,
            maxOutputTokens: 1024,
            statusLabel: "Answering",
            askMode: true);

        if (!string.IsNullOrWhiteSpace(Response))
        {
            _askConversation.Add(new AiMessage(AiRole.Assistant, Response));
        }
    }

    private List<AiMessage> BuildAskContextMessages()
    {
        return
        [
            new AiMessage(
                AiRole.System,
                "Answer the user's question using only the supplied XTMF2 model-system context. " +
                "You may explain existing elements, links, parameters, variables, hook states, and " +
                "available module definitions. Distinguish existing model elements from module types " +
                "that could be created. For a proposed solution, recommend suitable registered module " +
                "types and explain their relevant hooks or parameters. Do not create, modify, validate, " +
                "or apply model-system actions, and do not claim that any edit was made. If the context " +
                "mentions a registered module from AvailableModules, always format that module reference " +
                "as a Markdown link using its exact Name and DocumentationLink, such as " +
                "[Module Name](https://example.invalid/module). Never invent a documentation URL; if " +
                "the module has no DocumentationLink, use plain text instead. " +
                "When a name refers to an instantiated existing element in Elements, the element link " +
                "takes precedence over the module documentation link: format the exact element Name as " +
                "[exact element name](xtmf://element/<the exact Elements[].Id>). Every existing element " +
                "mentioned in the answer must use that internal link, including nodes, starts, function " +
                "instances, and parameter nodes. Never display an element ID, GUID, or shortened ID in " +
                "prose, code, or parentheses. Never use a module documentation URL for an instantiated " +
                "element. Use a module documentation link only when discussing the registered module type " +
                "itself rather than an instance in Elements. CommentBlocks are documentation notes with " +
                "stable GUIDs in CommentBlocks[].Id, not model elements; when referring to one, link its " +
                "descriptive text as [comment text](xtmf://comment/<the exact CommentBlocks[].Id>) and " +
                "never display its GUID. " +
                "does not contain enough information, say what is missing. Format the response as " +
                "Markdown when headings, lists, code, tables, or emphasis improve readability. Be " +
                "concise and direct.")
        ];
    }

    private IReadOnlyList<AiMessage> GetCompactedAskHistory(IReadOnlyList<AiMessage> contextMessages)
    {
        if (_askConversation.Count == 0 || ModelContextSize is not > 0)
        {
            return _askConversation;
        }

        var candidateMessages = new List<AiMessage>(contextMessages.Count + _askConversation.Count);
        candidateMessages.AddRange(contextMessages);
        candidateMessages.AddRange(_askConversation);
        var snapshot = _contextProjector.CreateSnapshot(_currentBoundary());
        var estimatedTokens = Math.Max(1,
            (JsonSerializer.Serialize(candidateMessages).Length +
             JsonSerializer.Serialize(snapshot).Length) / 4);
        if (estimatedTokens <= ModelContextSize * 0.6)
        {
            return _askConversation;
        }

        var history = string.Join("\n\n", _askConversation.Select(message =>
            $"{message.Role}: {message.Content}"));
        return
        [
            new AiMessage(
                AiRole.System,
                "Compacted Ask conversation history. Use it only for continuity; the current " +
                "model-system snapshot is authoritative:\n" + LimitContinuation(history))
        ];
    }

    private async Task<AiApplyOutcome> RunAgentTurnAsync(
        string? correctionFeedback,
        bool acceptIntentionalMissingRequiredHooks = false,
        string? prompt = null)
    {
        var contextMessages = BuildContextMessages(correctionFeedback);
        prompt ??= _lastSubmittedPrompt;

        StatusText = "Thinking";
        var agentMessages = new List<AiMessage>(contextMessages)
        {
            new AiMessage(
                AiRole.System,
                "When proposing model-system actions, use only IDs copied exactly from the supplied " +
                "ModelSystemContextSnapshot. Use CurrentBoundaryId for CreateNode boundaryId. " +
                "Use CurrentBoundaryModules for detailed metadata and AiInstructions for module types already " +
                "present in the active boundary; treat those instructions as authoritative. " +
                "For CreateNode, choose x and y in the same canvas coordinate system as Elements[].Position. " +
                "Use existing positions to place the full 120x50 node rectangle in open space near related nodes, " +
                "and avoid placing every new node at (0,0). " +
                "Use Elements[].Id for nodeId, originId, and destinationId. For CreateLink, use " +
                "Links[].OriginId, Links[].DestinationId, and Links[].HookName from the same context. " +
                "Use each element's AvailableParameters list to verify parameter edits and its " +
                "AvailableHooks list to verify link hooks; a hook being unconnected does not mean it " +
                "does not exist. Never invent parameter or hook names. Never create a link to a parameter " +
                "hook. In particular, If.Condition is a parameter and must be set with SetBasicParameter or " +
                "SetScriptedParameter; only If True and If False are structural execution link hooks. " +
                "Fail.Message and WriteToLog.Message are also generated parameter hooks, not execution " +
                "hooks; configure them with SetBasicParameter or SetScriptedParameter using the owning node " +
                "and parameterName Message, or use the generated parameter child node ID. " +
                "Every CreateNode and CreateLink must use one unused valid UUID copied exactly from the " +
                "ReservedElementIds list in the supplied context. The application generated those IDs; " +
                "never invent, regenerate, shorten, or wait for a replacement ID. Reuse the selected " +
                "reserved ID in later actions in this same proposal batch. " +
                "Elements with IsProvisional=true are reserved by an earlier proposal but are not committed " +
                "yet; their IDs are valid for later actions in this same batch and must be copied exactly. " +
                "The application creates all CreateNode actions before applying CreateLink actions, so a link " +
                "may reference a node created elsewhere in the same proposal batch. " +
                "For existing elements, never invent, shorten, regenerate, or infer their GUIDs. If either endpoint or the hook " +
                "cannot be found in the context, do not propose CreateLink and explain that more " +
                "context is required. Every action has a top-level id for tracking, but CreateNode and " +
                "CreateLink also require a separate arguments.id element UUID. Preserve the exact action " +
                "argument property names and values; never use the action tracking id as arguments.id. " +
                "A CreateLink must contain non-empty id, originId, destinationId, and hookName arguments. " +
                "hookName is the exact non-parameter hook on the origin node, copied from AvailableHooks or " +
                "Links[].HookName. AvailableParameters and CurrentBoundaryModules members marked IsParameter " +
                "are never valid hookName values. For If modules, Condition is a parameter; only If True and " +
                "If False are structural execution hooks. Never use a destination name or parameter name as " +
                "hookName; if no valid origin hook exists, do not propose the link. AddLinkDestination must " +
                "contain only non-empty originId, destinationId, and hookName, and is only for appending to an " +
                "existing AtLeastOne or AnyNumber origin hook, such as Execute.To Execute; do not include " +
                "arguments.id. Never use AddLinkDestination for If True or If False because those are single " +
                "cardinality branches. Use CreateLink for an unconnected single hook, and do not add a second " +
                "destination when it is already occupied."),
            new AiMessage(
                AiRole.System,
                "For multi-step requests, return a plan with small executable tasks. Each task must list " +
                "dependencies and actionIds that refer exactly to proposedActions ids. Do not put actions " +
                "inside the plan. A task should be independently applicable and verifiable; later tasks " +
                "must depend on earlier tasks when they use elements created by them."),
            new AiMessage(AiRole.User, prompt),
            new AiMessage(
                AiRole.System,
                "Reason about the request and implement it in this response using CreateNode, CreateLink, " +
                "AddLinkDestination, " +
                "UpdateNode, UpdateParameter, SetBasicParameter, SetScriptedParameter, and " +
                "ConvertBasicParameterToScriptedParameter actions. For documentation notes, use " +
                "CreateCommentBlock with the exact CurrentBoundaryId, comment text, optional header, and " +
                "positive x, y, width, and height; use UpdateCommentBlock with the exact CommentBlocks[].Id " +
                "and only the fields that should change; use DeleteCommentBlock with the exact comment ID. " +
                "Comment block IDs are generated by the host for new blocks and are never node or link IDs. " +
                "Use SetBasicParameter only for BasicParameter nodes and provide a literal value. Use " +
                "SetScriptedParameter only for ScriptedParameter nodes and provide a valid expression. " +
                "Use ConvertBasicParameterToScriptedParameter to preserve a BasicParameter node while " +
                "converting it to a ScriptedParameter. ScriptedParameter expressions resolve function-local " +
                "variables before model-system variables, must evaluate to the parameter's declared type, " +
                "and use quoted literals for strings. The expression language has no C# methods or explicit " +
                "casts: do not write CurrentIteration.ToString(). Addition implicitly converts to string when " +
                "either operand is a string, so \"Iteration: \" + CurrentIteration produces a string; numeric " +
                "addition remains numeric when both operands are numeric. For a generated parameter such as " +
                "Message, target either its generated child node ID or the owning module node ID with the exact " +
                "parameterName Message. " +
                "For generated parameter children, nodeId may be the owning module ID only when parameterName " +
                "is the exact parameter hook name; otherwise use the generated child node ID. " +
                "Return a concise explanation together with the " +
                "concrete proposedActions and a plan for multi-step requests. Do not defer the actions to a " +
                "later response.")
        };
            await RunStreamingTurnAsync(agentMessages, maxOutputTokens: 1024, "Thinking");

        if (HasProposedActions)
        {
            var validation = await _service.ValidateAsync(new AiActionBatch(
                Guid.NewGuid().ToString("N"),
                "Validate AI proposals",
                ProposedActions.Select(action => action.Proposal).ToArray()));
            if (!validation.IsSuccessful)
            {
                Error = FormatApplyError(validation, "The proposed actions failed model-system validation.");
                if (validation.RequiresModelDecision && acceptIntentionalMissingRequiredHooks)
                {
                    Error = null;
                    return AiApplyOutcome.NotApplicable;
                }

                ProposedActions.Clear();
                ToolInvocations.Clear();
                PlanTasks.Clear();
                PlanSummary = string.Empty;
                return AiApplyOutcome.Failed;
            }
        }

        return AiApplyOutcome.NotApplicable;
    }

    private List<AiMessage> BuildContextMessages(string? correctionFeedback)
    {
        var messages = new List<AiMessage>();
        messages.Add(new AiMessage(
            AiRole.System,
            "The ModelSystemContextSnapshot includes AvailableModules. Consult it before recommending or " +
            "creating a module. Match CreateNode typeName exactly to AvailableModules[].TypeName. " +
            "AvailableModules is a compact type index; its Members array is intentionally empty. If you need " +
            "hook, parameter, requiredness, cardinality, default, PassesExecution, or AiInstructions details, " +
            "you MUST request metadata for the exact typeName before proposing CreateNode. Return a " +
            "metadataRequests entry and no CreateNode actions in that response, then wait for the metadata " +
            "result before proposing CreateNode or dependent CreateLink actions. Never guess module hooks or " +
            "parameters from the compact index. " +
            "For elements in the current model, inspect AvailableHookStates: Required and IsConnected show which " +
            "hooks still need links. A required parameter hook is normally satisfied by its generated child; a " +
            "required non-parameter hook must have an explicit link. " +
            "RuntimeModules are included in this index with short descriptions and documentation links. " +
            "CommentBlocks is a separate documentation collection containing comment headers, body text, " +
            "stable GUIDs, and canvas positions. Use it for model-specific notes, but never treat comment blocks as " +
            "Nodes or links and never use CommentBlocks[].Id as a node or action ID. If the needed note is " +
            "not present or you need to search its text, use commentBlockRequests with an exact commentBlockId " +
            "or a short query, return no dependent actions, and wait for the host result. When referring to " +
            "a comment block, link its descriptive text with xtmf://comment/<the exact CommentBlocks[].Id>; " +
            "do not display the comment GUID itself. " +
            "The snapshot is focused on the current boundary. To inspect elements or documentation in another " +
            "boundary, use boundaryRequests with an exact boundaryId, exact path, or short name/description " +
            "query; return no dependent actions and wait for the host result. Boundary lookup is read-only, " +
            "and returned elements are not part of Elements[] until the host creates a new context. " +
            "When referring to an existing element from Elements, link its exact Name with the internal " +
            "Markdown URI xtmf://element/<the exact Elements[].Id>. Every existing element mentioned in " +
            "the response must use that link. Do not display element IDs, GUIDs, or shortened IDs, and do " +
            "not use a module documentation URL for an instantiated element; documentation links are only " +
            "for registered module types discussed independently of their existing instances. " +
            "Answer directly, avoid repeating the same reasoning, and do not invent missing context. " +
            "ScriptedParameter expressions use XTMF2's expression language: quoted string literals, " +
            "true/false booleans, non-negative integer and floating-point literals, exact variable names, " +
            "parentheses, unary !, arithmetic + - * / ^, comparisons < <= > >= == !=, boolean && and ||, " +
            "and the conditional form condition ? whenTrue : whenFalse. Use Variables in the context " +
            "snapshot as the authoritative name-to-type lookup. Variable names must match exactly; local " +
            "function variables shadow model-system variables with the same name. Do not invent variable " +
            "names or use C# syntax, method calls, commas, or single-quoted strings. " +
            "Important ID rule: a CreateNode id is a new reserved UUID, so never search Elements for it " +
            "and never reject it because it is not in the committed Elements list. Only existing node IDs " +
            "must be copied from Elements. If a later CreateLink targets a node created earlier in this " +
            "batch, copy that CreateNode id exactly; it may appear as an Elements entry with " +
            "IsProvisional=true."));
        messages.Add(new AiMessage(
            AiRole.System,
            "XTMF2 model-system terms: a Boundary groups model elements and links; the snapshot is focused " +
            "on the current boundary. A Node is a configured instance of one registered module type. " +
            "RuntimeModules are normal module types used as Nodes: configure their parameter Members and " +
            "connect their submodule Members with links to compose execution. A link starts at an origin " +
            "node's non-parameter hook and points to a destination node or FunctionInstance. " +
            "PassesExecution indicates that a submodule participates in the execution chain; use the " +
            "module description and hook metadata to determine how a RuntimeModule fits together rather " +
            "than guessing from its display name. A FunctionInstance is an instance of a reusable function " +
            "template and may expose template-derived parameters or destinations. In the context, " +
            "Elements[].Kind distinguishes Node from FunctionInstance, Links describes connections, and " +
            "AvailableModules describes registered module types but not their complete hook metadata. Do not " +
            "treat a module type description as an existing element: use AvailableModules for type selection, " +
            "metadataRequests for detailed type composition, and Elements for existing IDs."));
        messages.Add(new AiMessage(
            AiRole.System,
            "When you need to determine whether two existing nodes are connected, use the " +
            "connectionRequests tool with their exact Elements[].Id values. Return the request without " +
            "dependent actions, wait for the tool result, and use its originNodeId, destinationNodeId, " +
            "and hookName fields to decide whether a link already exists. Do not infer connectivity from " +
            "node names or type names."));
        if (!string.IsNullOrWhiteSpace(correctionFeedback))
        {
            messages.Add(new AiMessage(
                AiRole.System,
                "Correction feedback from the previous attempted action: " +
                LimitFeedback(correctionFeedback)));
        }

        return messages;
    }

    private async Task RunStreamingTurnAsync(
        IReadOnlyList<AiMessage> initialMessages,
        int maxOutputTokens,
        string statusLabel,
        bool askMode = false)
    {
        var conversationMessages = new List<AiMessage>(initialMessages);
        var messages = new List<AiMessage>(conversationMessages);
        var response = new StringBuilder(Response);
        var compactionCycle = 0;
        var turnCount = 0;
        var emittedActionIds = new HashSet<string>(StringComparer.Ordinal);
        var emittedCreateNodeKeys = new HashSet<string>(StringComparer.Ordinal);
        string? continuationContext = null;
        string? previousContinuationState = null;
        var repeatedContinuationStates = 0;
        var metadataCycle = 0;
        var connectionCycle = 0;
        var commentBlockCycle = 0;
        var boundaryCycle = 0;
        while (true)
        {
            EnsureReservedElementIds();
            messages = continuationContext is null
                ? new List<AiMessage>(conversationMessages)
                : BuildContinuationMessages(conversationMessages, continuationContext);
            turnCount++;
            if (turnCount > SummaryTurnThreshold ||
                (ModelContextSize is > 0 && LatestContextTokenEstimate > ModelContextSize * 0.6))
            {
                StatusText = "Summarizing context";
                continuationContext = askMode
                    ? BuildAskTurnSummary(_askConversation)
                    : BuildTurnSummary(Response, ProposedActions);
                messages = askMode
                    ? BuildAskContinuationMessages(conversationMessages, continuationContext)
                    : BuildContinuationMessages(conversationMessages, continuationContext);
                turnCount = 1;
            }

            var request = new AiChatRequest(
                ModelId.Trim(),
                messages,
                _contextProjector.CreateSnapshot(
                    _currentBoundary(),
                    pendingActions: ProposedActions.Select(action => action.Proposal).ToArray(),
                    reservedElementIds: _reservedElementIds),
                IsAgent: !askMode,
                MaxOutputTokens: maxOutputTokens);
            UpdateContextUsage(request);
            var wasTruncated = false;
            var metadataRequests = new List<AiModuleMetadataRequest>();
            var connectionRequests = new List<AiNodeConnectionRequest>();
            var commentBlockRequests = new List<AiCommentBlockRequest>();
            var boundaryRequests = new List<AiBoundaryRequest>();
            var turnToolCalls = new List<AiToolCall>();
            var turnResponse = new StringBuilder();
            StatusText = compactionCycle == 0
                ? statusLabel
                : $"{statusLabel} (continuation {compactionCycle}/{_maxCompactionCycles})";
            await foreach (var chunk in _service.ChatAsync(
                ProviderId,
                request,
                _requestCancellation!.Token))
            {
                turnResponse.Append(chunk.Text);
                response.Append(chunk.Text);
                Response = response.ToString();
                if (!string.IsNullOrWhiteSpace(chunk.Thinking))
                {
                    Thinking = string.IsNullOrWhiteSpace(Thinking)
                        ? chunk.Thinking
                        : LimitThinking($"{Thinking}{chunk.Thinking}");
                }
                wasTruncated |= chunk.IsTruncated;
                foreach (var action in askMode ? Array.Empty<AiActionProposal>() : chunk.ProposedActions)
                {
                    if (!emittedActionIds.Add(action.Id) ||
                        action.Kind == AiActionKind.CreateNode &&
                        !emittedCreateNodeKeys.Add(GetCreateNodeDeduplicationKey(action)))
                    {
                        continue;
                    }

                    ReserveActionIds(action);
                    ProposedActions.Add(new AiActionProposalViewModel(
                        action,
                        IsDestructiveAction(action, request.Context)));
                    ToolInvocations.Add(new AiToolInvocationViewModel(action));
                }
                if (!askMode && chunk.Plan is not null)
                {
                    SetPlan(chunk.Plan);
                }
                if (!askMode && chunk.MetadataRequests is not null)
                {
                    metadataRequests.AddRange(chunk.MetadataRequests);
                }
                if (!askMode && chunk.ConnectionRequests is not null)
                {
                    connectionRequests.AddRange(chunk.ConnectionRequests);
                }
                if (!askMode && chunk.CommentBlockRequests is not null)
                {
                    commentBlockRequests.AddRange(chunk.CommentBlockRequests);
                }
                if (!askMode && chunk.BoundaryRequests is not null)
                {
                    boundaryRequests.AddRange(chunk.BoundaryRequests);
                }
                if (!askMode && chunk.ToolCalls is not null)
                {
                    turnToolCalls.AddRange(chunk.ToolCalls);
                }
            }

            if (turnResponse.Length > 0 || turnToolCalls.Count > 0)
            {
                conversationMessages.Add(new AiMessage(
                    AiRole.Assistant,
                    turnResponse.ToString(),
                    turnToolCalls.Count == 0 ? null : turnToolCalls.ToArray()));
            }

            if (!wasTruncated)
            {
                if (askMode)
                {
                    break;
                }

                var connectionResult = BuildConnectionResult(connectionRequests);
                var metadataResult = BuildMetadataResult(metadataRequests);
                var commentBlockResult = BuildCommentBlockResult(commentBlockRequests);
                var boundaryResult = BuildBoundaryResult(boundaryRequests);
                var queuedToolResult = false;
                if (connectionResult is not null)
                {
                    if (connectionCycle >= MaximumMetadataCycles)
                    {
                        Error = $"The model requested node connection checks more than {MaximumMetadataCycles} times. " +
                            "Further requests were stopped.";
                        break;
                    }

                    connectionCycle++;
                    conversationMessages.Add(new AiMessage(
                        AiRole.Tool,
                        "The node connection request was resolved by the XTMF2 host. Use the following " +
                        "results and return one complete response. Each connection includes the origin " +
                        "hook name:\n" + connectionResult,
                        ToolName: "inspect_connections"));
                    StatusText = $"Checking node connections ({connectionCycle}/{MaximumMetadataCycles})";
                    queuedToolResult = true;
                }

                if (metadataResult is not null)
                {
                    if (metadataCycle >= MaximumMetadataCycles)
                    {
                        Error = $"The model requested module metadata more than {MaximumMetadataCycles} times. " +
                            "Further metadata requests were stopped.";
                        break;
                    }

                    metadataCycle++;
                    conversationMessages.Add(new AiMessage(
                        AiRole.Tool,
                        "The metadata request was resolved by the XTMF2 host. Use the following results and " +
                        "return one complete response. Do not request the same type again unless the result " +
                        "is missing:\n" + metadataResult,
                        ToolName: "request_module_metadata"));
                    StatusText = $"Loading module metadata ({metadataCycle}/{MaximumMetadataCycles})";
                    queuedToolResult = true;
                }

                if (commentBlockResult is not null)
                {
                    if (commentBlockCycle >= MaximumMetadataCycles)
                    {
                        Error = $"The model requested comment-block lookups more than {MaximumMetadataCycles} times. " +
                            "Further comment lookups were stopped.";
                        break;
                    }

                    commentBlockCycle++;
                    conversationMessages.Add(new AiMessage(
                        AiRole.Tool,
                        "The comment-block lookup was resolved by the XTMF2 host. Use the following " +
                        "documentation and return one complete response:\n" + commentBlockResult,
                        ToolName: "inspect_comment_blocks"));
                    StatusText = $"Looking up comment blocks ({commentBlockCycle}/{MaximumMetadataCycles})";
                    queuedToolResult = true;
                }

                if (boundaryResult is not null)
                {
                    if (boundaryCycle >= MaximumMetadataCycles)
                    {
                        Error = $"The model requested boundary lookups more than {MaximumMetadataCycles} times. " +
                            "Further boundary lookups were stopped.";
                        break;
                    }

                    boundaryCycle++;
                    conversationMessages.Add(new AiMessage(
                        AiRole.Tool,
                        "The boundary lookup was resolved by the XTMF2 host. The result is read-only; use " +
                        "the returned boundary and element IDs and return one complete response:\n" +
                        boundaryResult,
                        ToolName: "inspect_boundary"));
                    StatusText = $"Looking up boundaries ({boundaryCycle}/{MaximumMetadataCycles})";
                    queuedToolResult = true;
                }

                if (queuedToolResult)
                {
                    continuationContext = null;
                    continue;
                }

                break;
            }

            if (compactionCycle >= _maxCompactionCycles)
            {
                Error = $"The model reached the {_maxCompactionCycles}-cycle compact/continue limit. " +
                    "The partial response is shown above.";
                break;
            }

            compactionCycle++;
            StatusText = $"Compacting ({compactionCycle}/{_maxCompactionCycles})";
            continuationContext = BuildContinuationState(Response, ProposedActions);
            var normalizedContinuationState = NormalizeContinuationState(continuationContext);
            if (string.Equals(normalizedContinuationState, previousContinuationState, StringComparison.Ordinal))
            {
                repeatedContinuationStates++;
                if (repeatedContinuationStates >= 2)
                {
                    Error = "The model repeated the same continuation state twice. Further retries were stopped to avoid a loop.";
                    break;
                }
            }
            else
            {
                previousContinuationState = normalizedContinuationState;
                repeatedContinuationStates = 0;
            }
        }
    }

    private string? BuildMetadataResult(IEnumerable<AiModuleMetadataRequest> requests)
    {
        var uniqueTypeNames = requests
            .Select(request => request.TypeName?.Trim())
            .Where(typeName => !string.IsNullOrWhiteSpace(typeName))
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToArray();
        if (uniqueTypeNames.Length == 0)
        {
            return null;
        }

        var results = uniqueTypeNames.Select(typeName => new
        {
            typeName,
            module = _contextProjector.DescribeModuleType(typeName!)
        });
        return "Module metadata results. Use these exact type definitions before proposing dependent actions:\n" +
            LimitContinuation(JsonSerializer.Serialize(results));
    }

    private string? BuildConnectionResult(IEnumerable<AiNodeConnectionRequest> requests)
    {
        var uniqueRequests = requests
            .Where(request => Guid.TryParse(request.FirstNodeId, out _) &&
                              Guid.TryParse(request.SecondNodeId, out _))
            .Distinct()
            .Take(8)
            .ToArray();
        if (uniqueRequests.Length == 0)
        {
            return null;
        }

        var results = uniqueRequests.Select(request => new
        {
            firstNodeId = request.FirstNodeId,
            secondNodeId = request.SecondNodeId,
            connections = _contextProjector.DescribeConnections(request.FirstNodeId, request.SecondNodeId)
        });
        return "Node connection results. Each result reports the origin node, destination node, and origin hook name:\n" +
            LimitContinuation(JsonSerializer.Serialize(results));
    }

    private string? BuildCommentBlockResult(IEnumerable<AiCommentBlockRequest> requests)
    {
        var uniqueRequests = requests
            .Where(request => request is not null &&
                              (!string.IsNullOrWhiteSpace(request.CommentBlockId) ||
                               !string.IsNullOrWhiteSpace(request.Query)))
            .Distinct()
            .Take(8)
            .ToArray();
        if (uniqueRequests.Length == 0)
        {
            return null;
        }

        var results = _contextProjector.DescribeCommentBlocks(uniqueRequests);
        return "Comment-block documentation results. These are documentation only and are not model elements:\n" +
            LimitContinuation(JsonSerializer.Serialize(results));
    }

    private string? BuildBoundaryResult(IEnumerable<AiBoundaryRequest> requests)
    {
        var uniqueRequests = requests
            .Where(request => request is not null &&
                              (!string.IsNullOrWhiteSpace(request.BoundaryId) ||
                               !string.IsNullOrWhiteSpace(request.Path) ||
                               !string.IsNullOrWhiteSpace(request.Query)))
            .Distinct()
            .Take(8)
            .ToArray();
        if (uniqueRequests.Length == 0)
        {
            return null;
        }

        var results = _contextProjector.DescribeBoundaries(uniqueRequests);
        return "Boundary lookup results. These are read-only descriptions of boundaries outside or inside " +
            "the current context:\n" + LimitContinuation(JsonSerializer.Serialize(results));
    }

    [RelayCommand]
    private async Task ApplyActionsAsync()
    {
        if (Mode != AiAssistantMode.Agent || IsBusy || IsApplyingActions || ProposedActions.Count == 0)
        {
            return;
        }

        IsApplyingActions = true;
        Error = null;
        try
        {
            await ApplyActionsCoreAsync(
                approvalGranted: true,
                destructiveApprovalGranted: true,
                regenerateOnFailure: true);
        }
        catch (Exception exception)
        {
            Error = exception.Message;
        }
        finally
        {
            IsApplyingActions = false;
        }
    }

    [RelayCommand]
    private async Task ApplyPlanTaskAsync(AiPlanTaskViewModel? taskViewModel)
    {
        if (Mode != AiAssistantMode.Agent || taskViewModel is null || IsBusy || IsApplyingActions ||
            !taskViewModel.IsReady)
        {
            return;
        }

        var taskActions = ProposedActions
            .Where(action => (taskViewModel.Task.ActionIds ?? Array.Empty<string>())
                .Contains(action.Proposal.Id, StringComparer.Ordinal))
            .Select(action => action.Proposal)
            .ToArray();
        if (taskActions.Length == 0)
        {
            Error = $"Task '{taskViewModel.DisplayTitle}' has no pending actions.";
            return;
        }

        IsApplyingActions = true;
        taskViewModel.SetStatus(AiPlanTaskStatus.Running);
        Error = null;
        try
        {
            var result = await ExecuteActionSetAsync(taskActions);
            if (!result.IsSuccessful)
            {
                SetToolStatus(taskActions, result.FailedActionId, "Failed");
                taskViewModel.SetStatus(AiPlanTaskStatus.Failed);
                Error = FormatApplyError(result, $"Task '{taskViewModel.DisplayTitle}' could not be applied.");
                return;
            }

            SetToolStatus(taskActions, null, "Applied");

            taskViewModel.SetStatus(AiPlanTaskStatus.Completed);
            UpdatePlanReadiness();
            Response = string.IsNullOrWhiteSpace(Response)
                ? $"Completed task: {taskViewModel.DisplayTitle}."
                : $"{Response}\n\nCompleted task: {taskViewModel.DisplayTitle}.";
        }
        catch (Exception exception)
        {
            taskViewModel.SetStatus(AiPlanTaskStatus.Failed);
            Error = exception.Message;
        }
        finally
        {
            IsApplyingActions = false;
        }
    }

    private async Task ApplyActionsCoreAsync(
        bool approvalGranted,
        bool destructiveApprovalGranted,
        bool regenerateOnFailure = false)
    {
        var selectedActions = ProposedActions
            .Where(action => action.IsSelected)
            .Select(action => action.Proposal)
            .ToArray();
        if (selectedActions.Length == 0)
        {
            Error = "Select at least one proposed action to apply.";
            return;
        }

        selectedActions = IncludeRequiredCreateNodeDependencies(selectedActions);

        IsApplyingActions = true;
        try
        {
            var result = await ExecuteActionSetAsync(
                selectedActions,
                approvalGranted,
                destructiveApprovalGranted);
            if (!result.IsSuccessful)
            {
                SetToolStatus(selectedActions, result.FailedActionId, "Failed");
                Error = FormatApplyError(result, "The proposed actions could not be applied.");
                if (regenerateOnFailure)
                {
                    await RegenerateAfterApplyFailureAsync(Error);
                }
                return;
            }

            SetToolStatus(selectedActions, null, "Applied");

            PlanTasks.Clear();
            PlanSummary = string.Empty;
            Response = string.IsNullOrWhiteSpace(Response)
                ? "The proposed actions were applied."
                : $"{Response}\n\nThe proposed actions were applied.";
        }
        finally
        {
            IsApplyingActions = false;
        }
    }

    private async Task RegenerateAfterApplyFailureAsync(string correctionFeedback)
    {
        Response = BuildRetryNotice(correctionFeedback, 1);
        ProposedActions.Clear();
        ToolInvocations.Clear();
        PlanTasks.Clear();
        PlanSummary = string.Empty;
        Thinking = string.Empty;
        Error = null;
        StatusText = "Correcting failed actions";

        await RunAgentTurnAsync(correctionFeedback);
        if (ProposedActions.Count == 0 && string.IsNullOrWhiteSpace(Error))
        {
            Error = correctionFeedback + " The correction turn returned no actions.";
        }
    }

    private static string BuildRetryNotice(string? failure, int attempt)
    {
        var reason = string.IsNullOrWhiteSpace(failure)
            ? "The proposed changes could not be applied."
            : failure.Trim();
        return $"The proposed changes could not be applied. Reason: {reason}\n\n" +
            $"Retrying with a correction (attempt {attempt}).";
    }

    private async Task<AiActionExecutionResult> ExecuteActionSetAsync(
        IReadOnlyList<AiActionProposal> actions,
        bool approvalGranted = true,
        bool destructiveApprovalGranted = true)
    {
        var batch = new AiActionBatch(
            Guid.NewGuid().ToString("N"),
            "Apply AI plan task",
            actions);
        return await _service.ExecuteAsync(
            batch,
            approvalGranted,
            destructiveApprovalGranted);
    }

    private AiActionProposal[] IncludeRequiredCreateNodeDependencies(
        IReadOnlyList<AiActionProposal> selectedActions)
    {
        var actions = selectedActions.ToList();
        var includedActionIds = actions.Select(action => action.Id).ToHashSet(StringComparer.Ordinal);

        for (var index = 0; index < actions.Count; index++)
        {
            foreach (var nodeId in GetReferencedNodeIds(actions[index]))
            {
                if (!Guid.TryParse(nodeId, out var parsedNodeId) ||
                    ContainsNode(_currentBoundary(), parsedNodeId))
                {
                    continue;
                }

                var dependency = ProposedActions
                    .Select(action => action.Proposal)
                    .FirstOrDefault(action =>
                        action.Kind == AiActionKind.CreateNode &&
                        string.Equals(ReadActionId(action, "id"), nodeId, StringComparison.OrdinalIgnoreCase));
                if (dependency is not null && includedActionIds.Add(dependency.Id))
                {
                    actions.Add(dependency);
                }
            }
        }

        return actions.ToArray();
    }

    private static IEnumerable<string> GetReferencedNodeIds(AiActionProposal action)
    {
        if (action.Kind is AiActionKind.UpdateNode or AiActionKind.UpdateParameter or
            AiActionKind.SetBasicParameter or AiActionKind.SetScriptedParameter or
            AiActionKind.ConvertBasicParameterToScriptedParameter)
        {
            var nodeId = ReadActionId(action, "nodeId");
            if (!string.IsNullOrWhiteSpace(nodeId))
            {
                yield return nodeId;
            }
        }

        if (action.Kind is AiActionKind.CreateLink or AiActionKind.AddLinkDestination)
        {
            var originId = ReadActionId(action, "originId");
            var destinationId = ReadActionId(action, "destinationId");
            if (!string.IsNullOrWhiteSpace(originId))
            {
                yield return originId;
            }

            if (!string.IsNullOrWhiteSpace(destinationId))
            {
                yield return destinationId;
            }
        }
    }

    private static bool ContainsNode(Boundary boundary, Guid nodeId)
    {
        if (boundary.Modules.Any(node => node.Id == nodeId) ||
            boundary.FunctionInstances.Any(instance => instance.Id == nodeId))
        {
            return true;
        }

        return boundary.Boundaries.Any(child => ContainsNode(child, nodeId));
    }

    private string FormatApplyError(AiActionExecutionResult result, string fallback)
    {
        var error = result.Error ?? fallback;
        if (string.IsNullOrWhiteSpace(result.FailedActionId))
        {
            return error;
        }

        var failedAction = ProposedActions.FirstOrDefault(
            action => action.Proposal.Id == result.FailedActionId);
        var kindSuffix = failedAction is null ? string.Empty : $" ({failedAction.Proposal.Kind})";
                var decisionGuidance = result.RequiresModelDecision
                        ? " Review whether the required structural hook should receive a link. If it should, add the " +
                            "corresponding CreateLink action; if the omission is intentional, return the same action set " +
                            "without that link to confirm the choice."
                        : string.Empty;
                return $"Action '{result.FailedActionId}'{kindSuffix} failed: {error}{decisionGuidance} " +
                        (result.RequiresModelDecision
                                ? "Keep all existing action ids, kinds, and arguments unchanged unless the required link " +
                                    "decision requires adding a new CreateLink action."
                                : "Regenerate only that action with a corrected value; keep all other action ids, kinds, " +
                                    "and arguments exactly as previously supplied.");
    }

    private void SetToolStatus(
        IEnumerable<AiActionProposal> actions,
        string? failedActionId,
        string status)
    {
        var actionIds = actions.Select(action => action.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var invocation in ToolInvocations.Where(invocation => actionIds.Contains(invocation.Proposal.Id)))
        {
            invocation.SetStatus(failedActionId is not null &&
                                 string.Equals(invocation.Proposal.Id, failedActionId, StringComparison.Ordinal)
                ? "Failed"
                : status);
        }
    }

    private async Task ApplyReadyPlanTasksAsync()
    {
        foreach (var task in PlanTasks.Where(task => task.IsReady).ToArray())
        {
            var taskActions = ProposedActions
                .Where(action => (task.Task.ActionIds ?? Array.Empty<string>())
                    .Contains(action.Proposal.Id, StringComparer.Ordinal))
                .Select(action => action.Proposal)
                .ToArray();
            if (taskActions.Length == 0)
            {
                task.SetStatus(AiPlanTaskStatus.Failed);
                Error = $"Task '{task.DisplayTitle}' has no pending actions.";
                return;
            }

            task.SetStatus(AiPlanTaskStatus.Running);
            var result = await ExecuteActionSetAsync(taskActions);
            if (!result.IsSuccessful)
            {
                task.SetStatus(AiPlanTaskStatus.Failed);
                Error = FormatApplyError(result, $"Task '{task.DisplayTitle}' could not be applied.");
                return;
            }

            SetToolStatus(taskActions, null, "Applied");
            task.SetStatus(AiPlanTaskStatus.Completed);
            UpdatePlanReadiness();
        }
    }

    private void SetPlan(AiPlan plan)
    {
        PlanSummary = plan.Summary;
        PlanTasks.Clear();
        foreach (var task in plan.Tasks ?? Array.Empty<AiPlanTask>())
        {
            PlanTasks.Add(new AiPlanTaskViewModel(task));
        }

        UpdatePlanReadiness();
        OnPropertyChanged(nameof(HasPlan));
    }

    private void UpdatePlanReadiness()
    {
        foreach (var task in PlanTasks)
        {
            if (task.Status is AiPlanTaskStatus.Completed or AiPlanTaskStatus.Failed or AiPlanTaskStatus.Skipped)
            {
                continue;
            }

            var dependenciesComplete = (task.Task.DependsOn ?? Array.Empty<string>()).All(dependencyId =>
                PlanTasks.FirstOrDefault(candidate => candidate.Task.Id == dependencyId)?.Status ==
                AiPlanTaskStatus.Completed);
            task.SetStatus(dependenciesComplete ? AiPlanTaskStatus.Ready : AiPlanTaskStatus.Blocked);
        }
    }

    private static bool IsDestructiveAction(AiActionProposal action, AiContextSnapshot? context)
    {
        if (action.Kind is AiActionKind.DeleteNode or AiActionKind.DeleteLink or
            AiActionKind.DeleteBoundary or AiActionKind.DeleteCommentBlock)
        {
            return true;
        }

        if (context is null || action.Kind is not (AiActionKind.UpdateNode or
            AiActionKind.UpdateParameter or AiActionKind.SetBasicParameter or
            AiActionKind.SetScriptedParameter or AiActionKind.ConvertBasicParameterToScriptedParameter or
            AiActionKind.UpdateLink or
            AiActionKind.UpdateBoundary or AiActionKind.UpdateDescription or
            AiActionKind.UpdateCommentBlock))
        {
            return action.IsDestructive;
        }

        var targetId = ReadActionId(action, "nodeId") ?? ReadActionId(action, "id");
        if (targetId is null ||
            action.Kind != AiActionKind.UpdateCommentBlock && string.IsNullOrWhiteSpace(ReadActionValue(action)))
        {
            return action.IsDestructive;
        }

        return context.Elements.Any(element =>
                   string.Equals(element.Id, targetId, StringComparison.OrdinalIgnoreCase)) ||
               context.Links.Any(link =>
                   string.Equals(link.Id, targetId, StringComparison.OrdinalIgnoreCase)) ||
               (action.Kind == AiActionKind.UpdateCommentBlock &&
                context.CommentBlocks?.Any(comment =>
                    string.Equals(comment.Id, targetId, StringComparison.OrdinalIgnoreCase)) == true);
    }

    private static string? ReadActionId(AiActionProposal action, string propertyName)
    {
        return action.Arguments.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static string GetCreateNodeDeduplicationKey(AiActionProposal action)
    {
        return string.Join("|", action.Kind,
            ReadActionArgument(action, "boundaryId"),
            ReadActionArgument(action, "typeName"),
            ReadActionArgument(action, "name"),
            ReadActionArgument(action, "x"),
            ReadActionArgument(action, "y"));
    }

    private static string ReadActionArgument(AiActionProposal action, string propertyName)
    {
        return action.Arguments.TryGetProperty(propertyName, out var property)
            ? property.ToString()
            : string.Empty;
    }

    private static string? ReadActionValue(AiActionProposal action)
    {
        return action.Arguments.TryGetProperty("value", out var property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private void NewSession()
    {
        if (IsBusy)
        {
            return;
        }

        _askConversation.Clear();
        Conversation.Clear();
        _activeAssistantMessage = null;
        _lastSubmittedPrompt = string.Empty;
        Response = string.Empty;
        Thinking = string.Empty;
        Error = null;
        ProposedActions.Clear();
        ToolInvocations.Clear();
        PlanTasks.Clear();
        PlanSummary = string.Empty;
        LatestContextUsageText = "Latest request context: not sent";
        StatusText = "Ready";
    }

    [RelayCommand]
    private void Cancel()
    {
        _userCancelledRequest = true;
        _requestCancellation?.Cancel();
    }

    private static string LimitFeedback(string feedback)
    {
        const int maximumLength = 1200;
        return feedback.Length <= maximumLength
            ? feedback
            : feedback[..maximumLength] + "... [truncated]";
    }

    private async Task UpdateModelContextSizeAsync()
    {
        try
        {
            var contextSize = await _service.GetContextSizeAsync(ProviderId, ModelId.Trim());
            ModelContextSize = contextSize;
            ModelContextSizeText = contextSize is > 0
                ? $"Context size: {contextSize.Value:N0} tokens"
                : "Context size: unavailable";
        }
        catch
        {
            ModelContextSizeText = "Context size: unavailable";
        }
    }

    private void UpdateContextUsage(AiChatRequest request)
    {
        var messageText = JsonSerializer.Serialize(request.Messages);
        var contextText = request.Context is null ? string.Empty : JsonSerializer.Serialize(request.Context);
        var estimatedTokens = Math.Max(1, (messageText.Length + contextText.Length) / 4);
        LatestContextTokenEstimate = estimatedTokens;
        LatestContextUsageText = $"Latest request context: ~{estimatedTokens:N0} estimated tokens";
    }

    private int? ModelContextSize { get; set; }

    private int LatestContextTokenEstimate { get; set; }

    private void EnsureReservedElementIds()
    {
        while (_reservedElementIds.Count < ReservedElementIdPoolSize)
        {
            _reservedElementIds.Add(Guid.NewGuid().ToString());
        }
    }

    private void ReserveActionIds(AiActionProposal action)
    {
        if (action.Kind is not (AiActionKind.CreateNode or AiActionKind.CreateLink) ||
            !action.Arguments.TryGetProperty("id", out var idProperty) ||
            idProperty.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var id = idProperty.GetString();
        if (!string.IsNullOrWhiteSpace(id))
        {
            _reservedElementIds.Remove(id);
        }
    }

    private static string BuildTurnSummary(
        string response,
        IEnumerable<AiActionProposalViewModel> proposedActions)
    {
        var builder = new StringBuilder("Conversation summary:\n");
        builder.Append("Latest response: ");
        builder.AppendLine(LimitContinuation(response));
        builder.AppendLine("Reserved action IDs already emitted:");
        foreach (var action in proposedActions)
        {
            builder.Append("- ");
            builder.Append(action.Proposal.Id);
            builder.Append(" (");
            builder.Append(action.Proposal.Kind);
            builder.AppendLine(")");
        }

        return LimitContinuation(builder.ToString());
    }

    private static string BuildAskTurnSummary(IEnumerable<AiMessage> conversation)
    {
        var history = string.Join("\n\n", conversation.Select(message =>
            $"{message.Role}: {message.Content}"));
        return "Compacted Ask conversation history:\n" + LimitContinuation(history);
    }

    private static string LimitContinuation(string response)
    {
        const int maximumLength = 12000;
        return response.Length <= maximumLength
            ? response
            : response[^maximumLength..];
    }

    private static List<AiMessage> BuildContinuationMessages(
        IReadOnlyList<AiMessage> initialMessages,
        string continuationContext)
    {
        var messages = new List<AiMessage>(initialMessages.Count + 1);
        messages.AddRange(initialMessages);
        messages.Add(new AiMessage(
            AiRole.User,
            "The previous Agent response was truncated while producing a structured tool or action request. " +
            "Do not continue or repeat its partial JSON. Use this progress summary, then return one complete " +
            "JSON object using the required Agent schema only if a host tool request or proposed action is " +
            "still needed. Otherwise answer the user directly in natural language or Markdown. Preserve " +
            "already emitted action IDs and propose only actions that are not already listed. Keep text to one " +
            "brief sentence when using the JSON envelope.\n\n" +
            continuationContext));
        return messages;
    }

    private static List<AiMessage> BuildAskContinuationMessages(
        IReadOnlyList<AiMessage> initialMessages,
        string continuationContext)
    {
        var messages = new List<AiMessage>(initialMessages.Count + 1);
        messages.AddRange(initialMessages);
        messages.Add(new AiMessage(
            AiRole.System,
            "Use this compacted Ask history for continuity. The current model-system snapshot " +
            "is authoritative, and the response must remain read-only:\n" + continuationContext));
        return messages;
    }

    private static List<AiMessage> BuildMetadataMessages(
        IReadOnlyList<AiMessage> initialMessages,
        string metadataContext)
    {
        var messages = new List<AiMessage>(initialMessages.Count + 1);
        messages.AddRange(initialMessages);
        messages.Add(new AiMessage(
            AiRole.Tool,
            "The metadata request was resolved by the XTMF2 host. Use the following results and return " +
            "one complete response. Do not request the same type again unless the result is missing:\n" +
            metadataContext));
        return messages;
    }

    private static List<AiMessage> BuildConnectionMessages(
        IReadOnlyList<AiMessage> initialMessages,
        string connectionContext)
    {
        var messages = new List<AiMessage>(initialMessages.Count + 1);
        messages.AddRange(initialMessages);
        messages.Add(new AiMessage(
            AiRole.Tool,
            "The node connection request was resolved by the XTMF2 host. Use the following results and " +
            "return one complete response. Each connection includes the origin hook name:\n" +
            connectionContext));
        return messages;
    }

    private static string BuildContinuationState(
        string response,
        IEnumerable<AiActionProposalViewModel> proposedActions)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Progress summary:");
        builder.Append("Completed explanation: ");
        builder.AppendLine(LimitContinuation(response));
        builder.AppendLine("Already emitted action IDs:");
        foreach (var action in proposedActions)
        {
            builder.Append("- ");
            builder.Append(action.Proposal.Id);
            builder.Append(" (");
            builder.Append(action.Proposal.Kind);
            builder.AppendLine(")");
        }

        return LimitContinuation(builder.ToString());
    }

    private static string LimitThinking(string thinking)
    {
        const int maximumLength = 12000;
        return thinking.Length <= maximumLength
            ? thinking
            : thinking[^maximumLength..];
    }

    private static string NormalizeContinuationState(string state)
    {
        return string.Join(' ', state.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public void Dispose()
    {
        ProposedActions.CollectionChanged -= OnProposedActionsChanged;
        _requestCancellation?.Cancel();
        _requestCancellation?.Dispose();
        _requestCancellation = null;
    }
}