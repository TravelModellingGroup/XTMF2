using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace XTMF2.AI;

public sealed class AiAssistantService
{
    private readonly AiProviderRegistry _providers;
    private readonly IAiActionApplier _actionApplier;

    public AiAssistantService(AiProviderRegistry providers, IAiActionApplier actionApplier)
    {
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _actionApplier = actionApplier ?? throw new ArgumentNullException(nameof(actionApplier));
    }

    public IAsyncEnumerable<AiResponseChunk> ChatAsync(
        string providerId,
        AiChatRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _providers.GetRequired(providerId).ChatAsync(request, cancellationToken);
    }

    public Task<IReadOnlyList<AiModelInfo>> GetModelsAsync(
        string providerId,
        CancellationToken cancellationToken = default)
    {
        return _providers.GetModelsAsync(providerId, cancellationToken);
    }

    public Task<int?> GetContextSizeAsync(
        string providerId,
        string modelId,
        CancellationToken cancellationToken = default)
    {
        var provider = _providers.GetRequired(providerId);
        return provider is IAiModelContextInfo contextInfo
            ? contextInfo.GetContextSizeAsync(modelId, cancellationToken)
            : Task.FromResult<int?>(null);
    }

    public async Task<AiActionExecutionResult> ExecuteAsync(
        AiActionBatch batch,
        bool approvalGranted,
        bool destructiveApprovalGranted,
        CancellationToken cancellationToken = default)
    {
        var validation = AiActionValidation.ValidateForExecution(
            batch,
            approvalGranted,
            destructiveApprovalGranted);
        if (!validation.IsValid)
        {
            return AiActionExecutionResult.Failure(validation.Error!);
        }

        return await _actionApplier.ApplyAsync(batch, cancellationToken).ConfigureAwait(false);
    }

    public Task<AiActionExecutionResult> ValidateAsync(
        AiActionBatch batch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        return _actionApplier is IAiActionValidator validator
            ? validator.ValidateAsync(batch, cancellationToken)
            : Task.FromResult(AiActionExecutionResult.Success(Array.Empty<string>()));
    }
}