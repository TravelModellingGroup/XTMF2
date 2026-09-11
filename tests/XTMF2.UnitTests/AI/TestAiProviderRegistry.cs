using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.AI;

namespace XTMF2.UnitTests.AI;

[TestClass]
public sealed class TestAiProviderRegistry
{
    [TestMethod]
    public void ProvidersAreSortedForDisplay()
    {
        var registry = new AiProviderRegistry();
        registry.Register(new FakeProvider("z-provider", "Z Provider"));
        registry.Register(new FakeProvider("a-provider", "A Provider"));

        Assert.HasCount(2, registry.Providers);
        Assert.AreEqual("A Provider", registry.Providers[0].DisplayName);
    }

    [TestMethod]
    public void DuplicateProviderIdsAreRejected()
    {
        var registry = new AiProviderRegistry();
        registry.Register(new FakeProvider("provider", "First"));

        Assert.ThrowsExactly<InvalidOperationException>(
            () => registry.Register(new FakeProvider("PROVIDER", "Second")));
    }

    [TestMethod]
    public async Task ModelDiscoveryDelegatesToSelectedProvider()
    {
        var registry = new AiProviderRegistry();
        registry.Register(new FakeProvider("provider", "Provider"));

        var models = await registry.GetModelsAsync("provider");

        Assert.HasCount(1, models);
        Assert.AreEqual("model", models[0].Id);
    }

    [TestMethod]
    public void MissingProviderIsReported()
    {
        var registry = new AiProviderRegistry();

        Assert.ThrowsExactly<KeyNotFoundException>(() => registry.GetRequired("missing"));
    }

    private sealed class FakeProvider(string id, string displayName) : IAiProvider
    {
        public AiProviderInfo Info { get; } = new(id, displayName, AiCapability.ModelDiscovery);

        public Task<IReadOnlyList<AiModelInfo>> GetModelsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<AiModelInfo>>(
                [new AiModelInfo("model", "Model", Info.Id)]);
        }

        public async IAsyncEnumerable<AiResponseChunk> ChatAsync(
            AiChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return new AiResponseChunk("", [], IsComplete: true);
        }
    }
}