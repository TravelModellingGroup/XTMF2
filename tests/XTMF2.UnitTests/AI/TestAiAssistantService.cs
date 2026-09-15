using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.AI;

namespace XTMF2.UnitTests.AI;

[TestClass]
public sealed class TestAiAssistantService
{
    [TestMethod]
    public async Task ChatUsesTheSelectedProvider()
    {
        var provider = new FakeProvider();
        var registry = new AiProviderRegistry();
        registry.Register(provider);
        var service = new AiAssistantService(registry, new FakeActionApplier());

        var chunks = new List<AiResponseChunk>();
        await foreach (var chunk in service.ChatAsync(
            "fake",
            new AiChatRequest("model", [new AiMessage(AiRole.User, "Hello")])) )
        {
            chunks.Add(chunk);
        }

        Assert.HasCount(1, chunks);
        Assert.AreEqual("response", chunks[0].Text);
        Assert.AreEqual(1, provider.ChatCalls);
    }

    [TestMethod]
    public async Task RejectedPolicyDoesNotCallActionApplier()
    {
        var applier = new FakeActionApplier();
        var registry = new AiProviderRegistry();
        registry.Register(new FakeProvider());
        var service = new AiAssistantService(registry, applier);
        var batch = new AiActionBatch("batch", "Update", [new AiActionProposal(
            "action",
            AiActionKind.UpdateNode,
            "Update",
            System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone())]);

        var result = await service.ExecuteAsync(
            batch,
            approvalGranted: false,
            destructiveApprovalGranted: false);

        Assert.IsFalse(result.IsSuccessful);
        Assert.AreEqual(0, applier.ApplyCalls);
    }

    [TestMethod]
    public async Task ApprovedPolicyDelegatesActionBatch()
    {
        var applier = new FakeActionApplier();
        var registry = new AiProviderRegistry();
        registry.Register(new FakeProvider());
        var service = new AiAssistantService(registry, applier);
        var batch = new AiActionBatch("batch", "Update", [new AiActionProposal(
            "action",
            AiActionKind.UpdateNode,
            "Update",
            System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone())]);

        var result = await service.ExecuteAsync(
            batch,
            approvalGranted: true,
            destructiveApprovalGranted: false);

        Assert.IsTrue(result.IsSuccessful);
        Assert.AreEqual(1, applier.ApplyCalls);
    }

    private sealed class FakeProvider : IAiProvider
    {
        public AiProviderInfo Info { get; } = new("fake", "Fake", AiCapability.Streaming);

        public int ChatCalls { get; private set; }

        public Task<IReadOnlyList<AiModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<AiModelInfo>>([]);
        }

        public async IAsyncEnumerable<AiResponseChunk> ChatAsync(
            AiChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ChatCalls++;
            await Task.CompletedTask;
            yield return new AiResponseChunk("response", [], IsComplete: true);
        }
    }

    private sealed class FakeActionApplier : IAiActionApplier
    {
        public int ApplyCalls { get; private set; }

        public Task<AiActionExecutionResult> ApplyAsync(
            AiActionBatch batch,
            CancellationToken cancellationToken = default)
        {
            ApplyCalls++;
            return Task.FromResult(AiActionExecutionResult.Success(["element-1"]));
        }
    }
}