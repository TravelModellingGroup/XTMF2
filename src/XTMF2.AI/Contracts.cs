using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace XTMF2.AI;

[Flags]
public enum AiCapability
{
    None = 0,
    Streaming = 1,
    ModelDiscovery = 2,
    StructuredActions = 4
}

public enum AiRole
{
    System,
    User,
    Assistant,
    Tool
}

public enum AiActionKind
{
    CreateNode,
    UpdateNode,
    DeleteNode,
    CreateLink,
    AddLinkDestination,
    UpdateLink,
    DeleteLink,
    CreateBoundary,
    UpdateBoundary,
    DeleteBoundary,
    UpdateParameter,
    SetBasicParameter,
    SetScriptedParameter,
    ConvertBasicParameterToScriptedParameter,
    UpdateDescription,
    CreateCommentBlock,
    UpdateCommentBlock,
    DeleteCommentBlock
}

public sealed record AiProviderInfo(
    string Id,
    string DisplayName,
    AiCapability Capabilities);

public sealed record AcpPermissionOption(
    string OptionId,
    string Kind,
    string Name);

public sealed record AcpPermissionRequest(
    string? Title,
    IReadOnlyList<AcpPermissionOption> Options);

public sealed record AiModelInfo(
    string Id,
    string DisplayName,
    string ProviderId,
    bool IsLocal = false,
    string? Description = null);

public sealed record AiMessage(
    AiRole Role,
    string Content,
    IReadOnlyList<AiToolCall>? ToolCalls = null,
    string? ToolName = null);

public sealed record AiToolDefinition(
    string Name,
    string Description,
    JsonElement Parameters);

public sealed record AiToolCall(
    string Name,
    JsonElement Arguments,
    string? Id = null);

public sealed record AiContextSnapshot(
    string ModelSystemId,
    string ModelSystemName,
    string CurrentBoundaryId,
    string CurrentBoundaryName,
    IReadOnlyList<AiContextElement> Elements,
    IReadOnlyList<AiContextLink> Links,
    IReadOnlyDictionary<string, string> Variables,
    IReadOnlyList<AiModuleDescription>? AvailableModules = null,
    IReadOnlyList<string>? ReservedElementIds = null,
    IReadOnlyList<AiModuleDescription>? CurrentBoundaryModules = null,
    IReadOnlyList<AiContextCommentBlock>? CommentBlocks = null);

public sealed record AiModuleDescription(
    string TypeName,
    string Name,
    string Description,
    string? DocumentationLink,
    IReadOnlyList<AiModuleMemberDescription> Members,
    string? AiInstructions = null);

public sealed record AiModuleMemberDescription(
    string Name,
    bool IsParameter,
    string TypeName,
    string Description,
    bool Required,
    string? DefaultValue,
    bool PassesExecution,
    string Cardinality);

public sealed record AiContextHookState(
    string Name,
    bool IsParameter,
    bool Required,
    bool IsConnected,
    bool PassesExecution,
    string Cardinality);

public sealed record AiContextElement(
    string Id,
    string Kind,
    string Name,
    string? TypeName,
    string? Description,
    IReadOnlyDictionary<string, string> Parameters,
    IReadOnlyList<string>? AvailableParameters = null,
    IReadOnlyList<string>? AvailableHooks = null,
    bool IsProvisional = false,
    AiElementPosition? Position = null,
    IReadOnlyList<AiContextHookState>? AvailableHookStates = null);

public sealed record AiContextCommentBlock(
    string Id,
    string Header,
    string Comment,
    AiElementPosition? Position = null);

public sealed record AiBoundaryDescription(
    string Id,
    string Name,
    string FullPath,
    string Description,
    IReadOnlyList<AiContextElement> Elements,
    IReadOnlyList<AiContextCommentBlock> CommentBlocks);

public sealed record AiElementPosition(
    float X,
    float Y,
    float Width,
    float Height);

public sealed record AiContextLink(
    string Id,
    string OriginId,
    string? DestinationId,
    string HookName,
    bool IsDisabled);

public sealed record AiChatRequest(
    string ModelId,
    IReadOnlyList<AiMessage> Messages,
    AiContextSnapshot? Context = null,
    bool IsAgent = false,
    int MaxOutputTokens = 1024,
    AiGenerationOptions? GenerationOptions = null,
    IReadOnlyList<AiToolDefinition>? Tools = null);

public sealed record AiGenerationOptions(
    double? Temperature = null,
    double? TopP = null,
    double? RepeatPenalty = null,
    int? RepeatLastN = null);

public sealed record AiResponseChunk(
    string Text,
    IReadOnlyList<AiActionProposal> ProposedActions,
    bool IsComplete = false,
    string? Thinking = null,
    bool IsTruncated = false,
    string? ContinuationContext = null,
    AiPlan? Plan = null,
    IReadOnlyList<AiModuleMetadataRequest>? MetadataRequests = null,
    IReadOnlyList<AiNodeConnectionRequest>? ConnectionRequests = null,
    IReadOnlyList<AiCommentBlockRequest>? CommentBlockRequests = null,
    IReadOnlyList<AiBoundaryRequest>? BoundaryRequests = null,
    IReadOnlyList<AiToolCall>? ToolCalls = null);

public sealed record AiModuleMetadataRequest(string TypeName);

public sealed record AiNodeConnectionRequest(string FirstNodeId, string SecondNodeId);

public sealed record AiCommentBlockRequest(string? CommentBlockId = null, string? Query = null);

public sealed record AiBoundaryRequest(
    string? BoundaryId = null,
    string? Path = null,
    string? Query = null);

public enum AiPlanTaskStatus
{
    Pending,
    Ready,
    Running,
    Completed,
    Failed,
    Blocked,
    Skipped
}

public sealed record AiPlan(
    string Id,
    string Summary,
    IReadOnlyList<AiPlanTask> Tasks);

public sealed record AiPlanTask(
    string Id,
    string Title,
    string Description,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<string> ActionIds,
    AiPlanTaskStatus Status = AiPlanTaskStatus.Pending);

public sealed record AiActionProposal(
    string Id,
    AiActionKind Kind,
    string Summary,
    JsonElement Arguments,
    bool IsDestructive = false);

public sealed record AiActionBatch(
    string Id,
    string Summary,
    IReadOnlyList<AiActionProposal> Actions);

public sealed record AiActionValidationResult(
    bool IsValid,
    string? Error = null)
{
    public static AiActionValidationResult Valid { get; } = new(true);

    public static AiActionValidationResult Invalid(string error) => new(false, error);
}

public sealed record AiActionExecutionResult(
    bool IsSuccessful,
    string? Error = null,
    IReadOnlyList<string>? AffectedElementIds = null,
    string? FailedActionId = null,
    bool RequiresModelDecision = false)
{
    public static AiActionExecutionResult Success(IReadOnlyList<string> affectedElementIds) =>
        new(true, AffectedElementIds: affectedElementIds);

    public static AiActionExecutionResult Failure(
        string error,
        string? failedActionId = null,
        bool requiresModelDecision = false) =>
        new(false, error, FailedActionId: failedActionId, RequiresModelDecision: requiresModelDecision);
}

public interface IAiActionApplier
{
    Task<AiActionExecutionResult> ApplyAsync(
        AiActionBatch batch,
        CancellationToken cancellationToken = default);
}

public interface IAiActionValidator
{
    Task<AiActionExecutionResult> ValidateAsync(
        AiActionBatch batch,
        CancellationToken cancellationToken = default);
}

public static class AiActionValidation
{
    public static AiActionValidationResult ValidateForExecution(
        AiActionBatch batch,
        bool approvalGranted,
        bool destructiveApprovalGranted)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.Actions.Count == 0)
        {
            return AiActionValidationResult.Invalid("The action batch contains no actions.");
        }

        var duplicateId = batch.Actions
            .GroupBy(action => action.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateId is not null)
        {
            return AiActionValidationResult.Invalid(
                $"The action batch contains duplicate action id '{duplicateId.Key}'.");
        }

        if (!approvalGranted)
        {
            return AiActionValidationResult.Invalid(
                "The action batch requires explicit approval before execution.");
        }

        if (batch.Actions.Any(action => action.IsDestructive) && !destructiveApprovalGranted)
        {
            return AiActionValidationResult.Invalid(
                "Destructive actions require explicit destructive-action approval.");
        }

        return AiActionValidationResult.Valid;
    }
}

public sealed class AiProviderException : Exception
{
    public AiProviderException(string message)
        : base(message)
    {
    }

    public AiProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public interface IAiProvider
{
    AiProviderInfo Info { get; }

    Task<IReadOnlyList<AiModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default);

    IAsyncEnumerable<AiResponseChunk> ChatAsync(
        AiChatRequest request,
        CancellationToken cancellationToken = default);
}

public interface IAiModelContextInfo
{
    Task<int?> GetContextSizeAsync(
        string modelId,
        CancellationToken cancellationToken = default);
}