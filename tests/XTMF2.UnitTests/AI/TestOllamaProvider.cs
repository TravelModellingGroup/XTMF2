using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.AI;

namespace XTMF2.UnitTests.AI;

[TestClass]
public sealed class TestOllamaProvider
{
    [TestMethod]
    public void ParsesStructuredAgentResponse()
    {
        var chunk = OllamaProvider.ParseAgentResponse("""
                        {"text":"Rename the node.","plan":{"id":"p1","summary":"Rename safely","tasks":[
                            {"id":"t1","title":"Rename node","description":"Apply the name change","dependsOn":[],"actionIds":["a1"]}
                        ]},"proposedActions":[
              {"id":"a1","kind":"UpdateNode","summary":"Rename node","arguments":{"nodeId":"11111111-1111-1111-1111-111111111111","value":"New name"},"isDestructive":false}
            ]}
            """);

        Assert.IsNotNull(chunk);
        Assert.AreEqual("Rename the node.", chunk.Text);
        Assert.HasCount(1, chunk.ProposedActions);
        Assert.AreEqual(AiActionKind.UpdateNode, chunk.ProposedActions[0].Kind);
        Assert.IsNotNull(chunk.Plan);
        Assert.HasCount(1, chunk.Plan.Tasks);
        Assert.AreEqual("a1", chunk.Plan.Tasks[0].ActionIds[0]);
    }

    [TestMethod]
    public void ParsesModuleMetadataRequests()
    {
        var chunk = OllamaProvider.ParseAgentResponse(
            "{\"text\":\"I need the module composition details.\",\"metadataRequests\":[" +
            "{\"typeName\":\"XTMF2.RuntimeModules.WriteToLogA\"}]} ");

        Assert.IsNotNull(chunk);
        Assert.HasCount(1, chunk.MetadataRequests);
        Assert.AreEqual("XTMF2.RuntimeModules.WriteToLogA", chunk.MetadataRequests[0].TypeName);
    }

    [TestMethod]
    public void ParsesNodeConnectionRequests()
    {
        var chunk = OllamaProvider.ParseAgentResponse(
            "{\"text\":\"I need to check the link.\",\"connectionRequests\":[" +
            "{\"firstNodeId\":\"11111111-1111-1111-1111-111111111111\",\"secondNodeId\":\"22222222-2222-2222-2222-222222222222\"}]} ");

        Assert.IsNotNull(chunk);
        Assert.HasCount(1, chunk.ConnectionRequests);
        Assert.AreEqual("11111111-1111-1111-1111-111111111111",
            chunk.ConnectionRequests[0].FirstNodeId);
        Assert.AreEqual("22222222-2222-2222-2222-222222222222",
            chunk.ConnectionRequests[0].SecondNodeId);
    }

    [TestMethod]
    public void ParsesCommentBlockRequests()
    {
        var chunk = OllamaProvider.ParseAgentResponse(
            "{\"text\":\"I need the model note.\",\"commentBlockRequests\":[" +
            "{\"commentBlockId\":\"11111111-1111-1111-1111-111111111111\"}," +
            "{\"query\":\"peak period\"}]} ");

        Assert.IsNotNull(chunk);
        Assert.HasCount(2, chunk.CommentBlockRequests);
        Assert.AreEqual("11111111-1111-1111-1111-111111111111",
            chunk.CommentBlockRequests[0].CommentBlockId);
        Assert.AreEqual("peak period", chunk.CommentBlockRequests[1].Query);
    }

    [TestMethod]
    public void ParsesBoundaryRequests()
    {
        var chunk = OllamaProvider.ParseAgentResponse(
            "{\"text\":\"I need the nested boundary.\",\"boundaryRequests\":[" +
            "{\"boundaryId\":\"11111111-1111-1111-1111-111111111111\"}," +
            "{\"path\":\"Global.Nested\",\"query\":\"nested\"}]} ");

        Assert.IsNotNull(chunk);
        Assert.HasCount(2, chunk.BoundaryRequests);
        Assert.AreEqual("11111111-1111-1111-1111-111111111111",
            chunk.BoundaryRequests[0].BoundaryId);
        Assert.AreEqual("Global.Nested", chunk.BoundaryRequests[1].Path);
        Assert.AreEqual("nested", chunk.BoundaryRequests[1].Query);
    }

    [TestMethod]
    public void InvalidStructuredAgentResponseReturnsNull()
    {
        Assert.IsNull(OllamaProvider.ParseAgentResponse("plain text"));
    }

    [TestMethod]
    public void IncompleteStructuredAgentResponseReturnsNullWithoutDeserializing()
    {
        Assert.IsNull(OllamaProvider.ParseAgentResponse(
            "{\"text\":\"working\",\"proposedActions\":[{"));
    }

    [TestMethod]
    public void ExtractsCompleteActionsFromPartialStructuredResponse()
    {
        var actions = OllamaProvider.ExtractPartialAgentActions(
            "{\"text\":\"working\",\"proposedActions\":[" +
            "{\"id\":\"node-action\",\"kind\":\"CreateNode\",\"summary\":\"Create node\"," +
            "\"arguments\":{\"id\":\"11111111-1111-1111-1111-111111111111\",\"boundaryId\":\"22222222-2222-2222-2222-222222222222\",\"typeName\":\"XTMF2.RuntimeModules.If\",\"name\":\"Check\"},\"isDestructive\":false}," +
            "{\"id\":\"link-action\",\"kind\":\"CreateLink\",\"summary\":\"Connect\",\"arguments\":{");

        Assert.HasCount(1, actions);
        Assert.AreEqual("node-action", actions[0].Id);
        Assert.AreEqual(AiActionKind.CreateNode, actions[0].Kind);
    }
    [TestMethod]
    public async Task GetModelsReadsLocalModelNames()
    {
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"models\":[{\"name\":\"llama3.2\"},{\"name\":\"qwen2.5\"}]}")
        });
        var provider = new OllamaProvider(client, new Uri("http://localhost:11434"));

        var models = await provider.GetModelsAsync();

        Assert.HasCount(2, models);
        Assert.AreEqual("llama3.2", models[0].Id);
        Assert.IsTrue(models[0].IsLocal);
    }

    [TestMethod]
    public async Task ChatStreamsNdjsonMessagesAndSendsContext()
    {
        var requestBody = string.Empty;
        using var client = CreateClient(request =>
        {
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"message\":{\"thinking\":\"I should be concise.\",\"content\":\"Hello\"}}\n{\"message\":{\"content\":\" world\"},\"done\":true,\"done_reason\":\"length\"}\n",
                    Encoding.UTF8,
                    "application/x-ndjson")
            };
        });
        var provider = new OllamaProvider(client, new Uri("http://localhost:11434"));
        var request = new AiChatRequest(
            "llama3.2",
            [new AiMessage(AiRole.User, "Explain this")],
            new AiContextSnapshot("ms-1", "Demo", "boundary-1", "Root", [], [], new Dictionary<string, string>()));

        var chunks = new List<AiResponseChunk>();
        await foreach (var chunk in provider.ChatAsync(request))
        {
            chunks.Add(chunk);
        }

        Assert.HasCount(2, chunks);
        Assert.AreEqual("Hello", chunks[0].Text);
        Assert.AreEqual("I should be concise.", chunks[0].Thinking);
        Assert.IsTrue(chunks[1].IsComplete);
        Assert.IsTrue(chunks[1].IsTruncated);
        StringAssert.Contains(requestBody, "boundary-1");
        StringAssert.Contains(requestBody, "\"num_predict\":1024");
        Assert.IsFalse(requestBody.Contains("For this agent request", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task AgentRequestsUseRepetitionControls()
    {
        var requestBody = string.Empty;
        using var client = CreateClient(request =>
        {
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"message\":{\"content\":\"{\\\"text\\\":\\\"Done\\\",\\\"proposedActions\\\":[]}\"},\"done\":true}\n",
                    Encoding.UTF8,
                    "application/x-ndjson")
            };
        });
        var provider = new OllamaProvider(client, new Uri("http://localhost:11434"));
        var request = new AiChatRequest(
            "llama3.2",
            [new AiMessage(AiRole.User, "Create one node")],
            IsAgent: true);

        await foreach (var _ in provider.ChatAsync(request))
        {
        }

        StringAssert.Contains(requestBody, "\"temperature\":0.15");
        StringAssert.Contains(requestBody, "\"top_p\":0.9");
        StringAssert.Contains(requestBody, "\"repeat_penalty\":1.15");
        StringAssert.Contains(requestBody, "\"repeat_last_n\":256");
        StringAssert.Contains(requestBody, "\"think\":false");
    }

    [TestMethod]
    public async Task AgentStreamsActionOnlyChunkAsProposal()
    {
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"message\":{\"content\":\"{\\\"text\\\":\\\"\\\",\\\"proposedActions\\\":[{\\\"id\\\":\\\"a1\\\",\\\"kind\\\":\\\"UpdateNode\\\",\\\"summary\\\":\\\"Rename node\\\",\\\"arguments\\\":{\\\"nodeId\\\":\\\"11111111-1111-1111-1111-111111111111\\\",\\\"value\\\":\\\"New name\\\"},\\\"isDestructive\\\":false}]}\"}}\n" +
                "{\"message\":{\"content\":\"\"},\"done\":true}\n",
                Encoding.UTF8,
                "application/x-ndjson")
        });
        var provider = new OllamaProvider(client, new Uri("http://localhost:11434"));
        var request = new AiChatRequest(
            "llama3.2",
            [new AiMessage(AiRole.User, "Rename the node")],
            IsAgent: true);

        var chunks = new List<AiResponseChunk>();
        await foreach (var chunk in provider.ChatAsync(request))
        {
            chunks.Add(chunk);
        }

        Assert.HasCount(2, chunks);
        Assert.HasCount(1, chunks[0].ProposedActions);
        Assert.AreEqual("a1", chunks[0].ProposedActions[0].Id);
    }

    [TestMethod]
    public async Task AgentCanReturnPlainUserFacingAnswerWithoutJsonEnvelope()
    {
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"message\":{\"content\":\"The model system is ready.\"},\"done\":true}\n",
                Encoding.UTF8,
                "application/x-ndjson")
        });
        var provider = new OllamaProvider(client, new Uri("http://localhost:11434"));
        var request = new AiChatRequest(
            "llama3.2",
            [new AiMessage(AiRole.User, "What is the status?")],
            IsAgent: true);

        var chunks = new List<AiResponseChunk>();
        await foreach (var chunk in provider.ChatAsync(request))
        {
            chunks.Add(chunk);
        }

        Assert.HasCount(1, chunks);
        Assert.AreEqual("The model system is ready.", chunks[0].Text);
        Assert.IsTrue(chunks[0].IsComplete);
        Assert.IsFalse(chunks[0].IsTruncated);
        Assert.IsEmpty(chunks[0].ProposedActions);
    }

    [TestMethod]
    public async Task TruncatedAgentResponsePreservesContinuationContext()
    {
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"message\":{\"content\":\"{\\\"text\\\":\\\"partial\\\"\"},\"done\":false}\n" +
                "{\"message\":{\"content\":\"}\"},\"done\":true,\"done_reason\":\"length\"}\n",
                Encoding.UTF8,
                "application/x-ndjson")
        });
        var provider = new OllamaProvider(client, new Uri("http://localhost:11434"));
        var request = new AiChatRequest(
            "llama3.2",
            [new AiMessage(AiRole.User, "Continue")],
            IsAgent: true);

        AiResponseChunk finalChunk = null!;
        await foreach (var chunk in provider.ChatAsync(request))
        {
            finalChunk = chunk;
        }

        Assert.IsNotNull(finalChunk);
        Assert.IsTrue(finalChunk.IsTruncated);
        StringAssert.Contains(finalChunk.ContinuationContext, "partial");
    }

    [TestMethod]
    public async Task FailedRequestProducesProviderException()
    {
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            ReasonPhrase = "Unavailable",
            Content = new StringContent("offline")
        });
        var provider = new OllamaProvider(client, new Uri("http://localhost:11434"));

        await Assert.ThrowsExactlyAsync<AiProviderException>(() => provider.GetModelsAsync());
    }

    private static HttpClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        return new HttpClient(new DelegatingHandlerStub(handler));
    }

    private sealed class DelegatingHandlerStub(Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request));
        }
    }
}