using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.AI;

namespace XTMF2.UnitTests.AI;

[TestClass]
public sealed class TestAiControlServer
{
    [TestMethod]
    public async Task RequiresBearerAuthentication()
    {
        await using var fixture = await ControlServerFixture.CreateAsync();
        fixture.Client.DefaultRequestHeaders.Add("X-Request-ID", "request-1");

        using var response = await fixture.Client.GetAsync("v1/models?providerId=fake");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.AreEqual("request-1", response.Headers.GetValues("X-Request-ID").Single());
        Assert.AreEqual("request-1", fixture.AuditEvents[0].RequestId);
        Assert.AreEqual(401, fixture.AuditEvents[0].StatusCode);
    }

    [TestMethod]
    public async Task DelegatesModelsAndChatToAuthenticatedProvider()
    {
        await using var fixture = await ControlServerFixture.CreateAsync();
        fixture.Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", fixture.Token);

        using var modelsResponse = await fixture.Client.GetAsync("v1/models?providerId=fake");
        Assert.AreEqual(HttpStatusCode.OK, modelsResponse.StatusCode);
        var models = await modelsResponse.Content.ReadFromJsonAsync<AiModelInfo[]>();
        Assert.IsNotNull(models);
        Assert.AreEqual("fake-model", models[0].Id);

        using var chatResponse = await fixture.Client.PostAsync(
            "v1/chat",
            new StringContent(
                "{\"providerId\":\"fake\",\"modelId\":\"fake-model\",\"messages\":[{\"role\":\"User\",\"content\":\"Hello\"}]}",
                Encoding.UTF8,
                "application/json"));
        Assert.AreEqual(HttpStatusCode.OK, chatResponse.StatusCode);
        var chunks = await chatResponse.Content.ReadFromJsonAsync<AiResponseChunk[]>();
        Assert.IsNotNull(chunks);
        Assert.IsNotEmpty(chunks);
        Assert.AreEqual("Hello from fake provider.", chunks[0].Text);
    }

    [TestMethod]
    public async Task ActionEndpointHonorsApprovalPolicy()
    {
        await using var fixture = await ControlServerFixture.CreateAsync();
        fixture.Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", fixture.Token);
        const string body = """
            {"batch":{"id":"batch-1","summary":"Update","actions":[
              {"id":"action-1","kind":"UpdateNode","summary":"Update node","arguments":{},"isDestructive":false}
            ]},"autonomyPolicy":"ApproveBatch","approvalGranted":false}
            """;

        using var response = await fixture.Client.PostAsync(
            "v1/actions",
            new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.IsFalse(fixture.Applier.WasCalled);
    }

    [TestMethod]
    public async Task ChatSupportsAuthenticatedNdjsonStreaming()
    {
        await using var fixture = await ControlServerFixture.CreateAsync();
        fixture.Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", fixture.Token);
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/chat")
        {
            Content = new StringContent(
                "{\"providerId\":\"fake\",\"modelId\":\"fake-model\",\"messages\":[{\"role\":\"User\",\"content\":\"Hello\"}]}",
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Accept.ParseAdd("application/x-ndjson");

        using var response = await fixture.Client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("application/x-ndjson", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "Hello from fake provider.");
        StringAssert.Contains(body, "\n");
    }

    private sealed class ControlServerFixture : IAsyncDisposable
    {
        private readonly AiControlServer _server;

        private ControlServerFixture(
            AiControlServer server,
            HttpClient client,
            string token,
            FakeApplier applier,
            List<AiControlAuditEvent> auditEvents)
        {
            _server = server;
            Client = client;
            Token = token;
            Applier = applier;
            AuditEvents = auditEvents;
        }

        public HttpClient Client { get; }
        public string Token { get; }
        public FakeApplier Applier { get; }
        public List<AiControlAuditEvent> AuditEvents { get; }

        public static Task<ControlServerFixture> CreateAsync()
        {
            var port = GetFreePort();
            var token = "test-token";
            var registry = new AiProviderRegistry();
            registry.Register(new FakeProvider());
            var applier = new FakeApplier();
            var service = new AiAssistantService(registry, applier);
            var auditEvents = new List<AiControlAuditEvent>();
            var server = new AiControlServer(
                service,
                $"http://127.0.0.1:{port}/",
                token,
                auditEvents.Add);
            server.Start();
            var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };
            return Task.FromResult(new ControlServerFixture(server, client, token, applier, auditEvents));
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _server.DisposeAsync();
        }

        private static int GetFreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
    }

    private sealed class FakeProvider : IAiProvider
    {
        public AiProviderInfo Info { get; } = new("fake", "Fake", AiCapability.Streaming | AiCapability.ModelDiscovery);

        public Task<IReadOnlyList<AiModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AiModelInfo>>([new AiModelInfo("fake-model", "Fake Model", "fake")]);

        public async IAsyncEnumerable<AiResponseChunk> ChatAsync(
            AiChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new AiResponseChunk("Hello from fake provider.", [], IsComplete: true);
        }
    }

    private sealed class FakeApplier : IAiActionApplier
    {
        public bool WasCalled { get; private set; }

        public Task<AiActionExecutionResult> ApplyAsync(
            AiActionBatch batch,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult(AiActionExecutionResult.Success([]));
        }
    }
}
