using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.AI;

namespace XTMF2.UnitTests.AI;

[TestClass]
public sealed class TestAiContracts
{
    [TestMethod]
    public void ChatRequestDefaultsToSuggestOnly()
    {
        var request = new AiChatRequest("model", [new AiMessage(AiRole.User, "Inspect this")]);

        Assert.AreEqual(AiAutonomyPolicy.SuggestOnly, request.AutonomyPolicy);
        Assert.IsNull(request.Context);
    }

    [TestMethod]
    public void ProviderCapabilitiesCanRepresentLocalStreamingProvider()
    {
        var provider = new AiProviderInfo(
            "ollama",
            "Ollama",
            AiCapability.Streaming | AiCapability.ModelDiscovery | AiCapability.StructuredActions);

        Assert.IsTrue(provider.Capabilities.HasFlag(AiCapability.Streaming));
        Assert.IsTrue(provider.Capabilities.HasFlag(AiCapability.ModelDiscovery));
        Assert.IsTrue(provider.Capabilities.HasFlag(AiCapability.StructuredActions));
    }

    [TestMethod]
    public void DestructiveActionIsExplicit()
    {
        using var document = JsonDocument.Parse("{\"nodeId\":\"node-1\"}");
        var action = new AiActionProposal(
            "delete-node-1",
            AiActionKind.DeleteNode,
            "Delete the selected node",
            document.RootElement.Clone(),
            IsDestructive: true);

        Assert.IsTrue(action.IsDestructive);
        Assert.AreEqual("node-1", action.Arguments.GetProperty("nodeId").GetString());
    }
}