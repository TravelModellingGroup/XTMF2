using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.AI;
using XTMF2.GUI.AI;
using XTMF2.GUI.ViewModels;

namespace XTMF2.GUI.Tests.ViewModels;

[TestClass]
public sealed class AiAssistantViewModelTests
{
    [TestMethod]
    public async Task AskModeStreamsReadOnlyAnswerWithoutApplyingActions()
    {
        TestGuiHelper.RunInModelSystemContext(
            nameof(AskModeStreamsReadOnlyAnswerWithoutApplyingActions), (user, _, session) =>
        {
            var provider = new FakeProvider();
            provider.ContextSize = 100;
            var applier = new FakeActionApplier();
            var registry = new AiProviderRegistry();
            registry.Register(provider);
            var service = new AiAssistantService(registry, applier);
            using var viewModel = new AiAssistantViewModel(
                service,
                new ModelSystemContextProjector("model", "test", session),
                () => session.ModelSystem.GlobalBoundary,
                providerId: "fake");

            Assert.AreEqual(AiAssistantMode.Ask, viewModel.Mode);
            viewModel.Mode = AiAssistantMode.Ask;
            viewModel.Prompt = "What nodes are in this model system?";
            viewModel.SendCommand.ExecuteAsync(null).GetAwaiter().GetResult();

            Assert.IsNull(viewModel.Error, viewModel.Error);
            Assert.AreEqual("answer", viewModel.Response);
            Assert.IsEmpty(viewModel.Prompt);
            Assert.AreEqual(AiAutonomyPolicy.SuggestOnly, provider.Requests[0].AutonomyPolicy);
            Assert.HasCount(2, viewModel.Conversation);
            Assert.IsTrue(viewModel.Conversation[0].IsUser);
            Assert.AreEqual("What nodes are in this model system?", viewModel.Conversation[0].Content);
            Assert.IsTrue(viewModel.Conversation[1].IsAssistant);
            Assert.AreEqual("answer", viewModel.Conversation[1].Content);
            Assert.IsFalse(viewModel.Conversation[1].IsStreaming);
            Assert.IsTrue(viewModel.Conversation[1].IsMarkdownVisible);
            Assert.IsEmpty(viewModel.Thinking);
            Assert.IsEmpty(viewModel.Conversation[1].Thinking);
            StringAssert.Contains(
                string.Join("\n", provider.Requests[0].Messages),
                "Format the response as Markdown");
            StringAssert.Contains(
                string.Join("\n", provider.Requests[0].Messages),
                "always format that module reference as a Markdown link");
            StringAssert.Contains(
                string.Join("\n", provider.Requests[0].Messages),
                "xtmf://element/<the exact Elements[].Id>");
            StringAssert.Contains(
                string.Join("\n", provider.Requests[0].Messages),
                "xtmf://comment/<the exact CommentBlocks[].Id>");
            StringAssert.Contains(
                string.Join("\n", provider.Requests[0].Messages),
                "Never display an element ID, GUID, or shortened ID");
            Assert.AreEqual(0, applier.ApplyCalls);
            Assert.AreEqual(0, applier.ValidateCalls);
            Assert.IsFalse(viewModel.CanApplyActions);
            Assert.IsEmpty(viewModel.ProposedActions);

            viewModel.Prompt = "What did you just tell me?";
            viewModel.SendCommand.ExecuteAsync(null).GetAwaiter().GetResult();

            Assert.HasCount(2, provider.Requests);
            Assert.HasCount(4, viewModel.Conversation);
            Assert.AreEqual("answer", viewModel.Conversation[1].Content);
            Assert.IsEmpty(viewModel.Conversation[1].Thinking);
            Assert.IsTrue(viewModel.Conversation[2].IsUser);
            Assert.IsTrue(viewModel.Conversation[3].IsAssistant);
            StringAssert.Contains(
                string.Join("\n", provider.Requests[1].Messages),
                "Compacted Ask conversation history");

            viewModel.NewSessionCommand.Execute(null);
            Assert.IsEmpty(viewModel.Conversation);
            Assert.IsEmpty(viewModel.Response);
            Assert.IsEmpty(viewModel.Thinking);
            Assert.IsEmpty(viewModel.ProposedActions);
            Assert.IsEmpty(viewModel.ToolInvocations);
            Assert.IsEmpty(viewModel.PlanTasks);
            Assert.IsNull(viewModel.Error);
        });
    }

    private sealed class FakeProvider : IAiProvider, IAiModelContextInfo
    {
        public AiProviderInfo Info { get; } = new("fake", "Fake", AiCapability.Streaming);

        public List<AiChatRequest> Requests { get; } = new();

        public int? ContextSize { get; set; }

        public Task<IReadOnlyList<AiModelInfo>> GetModelsAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AiModelInfo>>([]);

        public Task<int?> GetContextSizeAsync(
            string modelId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ContextSize);

        public async IAsyncEnumerable<AiResponseChunk> ChatAsync(
            AiChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            await Task.CompletedTask;
            yield return new AiResponseChunk(
                "answer", [], IsComplete: true, Thinking: "temporary reasoning");
        }
    }

    private sealed class FakeActionApplier : IAiActionApplier, IAiActionValidator
    {
        public int ApplyCalls { get; private set; }

        public int ValidateCalls { get; private set; }

        public Task<AiActionExecutionResult> ApplyAsync(
            AiActionBatch batch,
            CancellationToken cancellationToken = default)
        {
            ApplyCalls++;
            return Task.FromResult(AiActionExecutionResult.Success([]));
        }

        public Task<AiActionExecutionResult> ValidateAsync(
            AiActionBatch batch,
            CancellationToken cancellationToken = default)
        {
            ValidateCalls++;
            return Task.FromResult(AiActionExecutionResult.Success([]));
        }
    }
}
