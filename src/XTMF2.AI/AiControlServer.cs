using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace XTMF2.AI;

public sealed record AiControlChatRequest(
    string ProviderId,
    string ModelId,
    IReadOnlyList<AiMessage> Messages,
    AiContextSnapshot? Context = null,
    AiAutonomyPolicy AutonomyPolicy = AiAutonomyPolicy.SuggestOnly);

public sealed record AiControlActionRequest(
    AiActionBatch Batch,
    AiAutonomyPolicy AutonomyPolicy = AiAutonomyPolicy.SuggestOnly,
    bool ApprovalGranted = false,
    bool DestructiveApprovalGranted = false);

public sealed record AiControlAuditEvent(
    string RequestId,
    string Method,
    string Path,
    int StatusCode,
    TimeSpan Duration);

/// <summary>
/// Authenticated local HTTP control surface for the provider-neutral AI service.
/// </summary>
public sealed class AiControlServer : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly Func<AiAssistantService> _serviceFactory;
    private readonly HttpListener _listener = new();
    private readonly string _token;
    private readonly Action<AiControlAuditEvent>? _audit;
    private readonly SemaphoreSlim _requestGate;
    private readonly TimeSpan _requestTimeout;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _loop;

    public AiControlServer(
        AiAssistantService service,
        string prefix,
        string bearerToken,
        Action<AiControlAuditEvent>? audit = null,
        int maxConcurrentRequests = 4,
        TimeSpan? requestTimeout = null)
        : this(() => service, prefix, bearerToken, audit, maxConcurrentRequests, requestTimeout)
    {
    }

    public AiControlServer(
        Func<AiAssistantService> serviceFactory,
        string prefix,
        string bearerToken,
        Action<AiControlAuditEvent>? audit = null,
        int maxConcurrentRequests = 4,
        TimeSpan? requestTimeout = null)
    {
        _serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));
        if (string.IsNullOrWhiteSpace(prefix) || !prefix.EndsWith("/", StringComparison.Ordinal))
        {
            throw new ArgumentException("The HTTP listener prefix must be a non-empty URL ending with '/'.", nameof(prefix));
        }

        if (string.IsNullOrWhiteSpace(bearerToken))
        {
            throw new ArgumentException("A bearer token is required.", nameof(bearerToken));
        }

        if (maxConcurrentRequests <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentRequests));
        }

        if (requestTimeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        }

        _token = bearerToken;
        _audit = audit;
        _requestGate = new SemaphoreSlim(maxConcurrentRequests, maxConcurrentRequests);
        _requestTimeout = requestTimeout ?? TimeSpan.FromMinutes(5);
        _listener.Prefixes.Add(prefix);
    }

    public bool IsRunning => _listener.IsListening;

    public void Start()
    {
        if (_loop is not null)
        {
            throw new InvalidOperationException("The AI control server has already been started.");
        }

        _listener.Start();
        _loop = RunAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        _listener.Close();
        if (_loop is not null)
        {
            await _loop.ConfigureAwait(false);
        }

        _requestGate.Dispose();
        _shutdown.Dispose();
    }

    private async Task RunAsync()
    {
        var activeRequests = new List<Task>();
        while (!_shutdown.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException) when (_shutdown.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (_shutdown.IsCancellationRequested)
            {
                break;
            }

            activeRequests.RemoveAll(task => task.IsCompleted);
            activeRequests.Add(HandleContextAsync(context));
        }

        await Task.WhenAll(activeRequests).ConfigureAwait(false);
    }

    private async Task HandleContextAsync(HttpListenerContext context)
    {
        var acquired = false;
        using var timeout = new CancellationTokenSource(_requestTimeout);
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _shutdown.Token,
            timeout.Token);
        try
        {
            await _requestGate.WaitAsync(operationCancellation.Token).ConfigureAwait(false);
            acquired = true;
            await HandleAsync(context, operationCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            try
            {
                await WriteJsonAsync(context.Response, 500, new { error = exception.Message })
                    .ConfigureAwait(false);
            }
            catch
            {
            }
        }
        finally
        {
            if (acquired)
            {
                _requestGate.Release();
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        using (context.Response)
        {
            var requestId = context.Request.Headers["X-Request-ID"];
            if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > 128)
            {
                requestId = Guid.NewGuid().ToString("N");
            }

            context.Response.Headers["X-Request-ID"] = requestId;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                if (!IsAuthorized(context.Request))
                {
                    context.Response.AddHeader("WWW-Authenticate", "Bearer");
                    await WriteJsonAsync(context.Response, 401, new { error = "Authentication required." })
                        .ConfigureAwait(false);
                    return;
                }

                var path = context.Request.Url?.AbsolutePath.TrimEnd('/') ?? string.Empty;
                if (context.Request.HttpMethod == "GET" && path == "/v1/models")
                {
                    await HandleModelsAsync(context, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (context.Request.HttpMethod == "POST" && path == "/v1/chat")
                {
                    await HandleChatAsync(context, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (context.Request.HttpMethod == "POST" && path == "/v1/actions")
                {
                    await HandleActionsAsync(context, cancellationToken).ConfigureAwait(false);
                    return;
                }

                await WriteJsonAsync(context.Response, 404, new { error = "Endpoint not found." })
                    .ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    _audit?.Invoke(new AiControlAuditEvent(
                        requestId,
                        context.Request.HttpMethod,
                        context.Request.Url?.AbsolutePath ?? string.Empty,
                        context.Response.StatusCode,
                        stopwatch.Elapsed));
                }
                catch
                {
                }
            }
        }
    }

    private async Task HandleModelsAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var providerId = context.Request.QueryString["providerId"];
        if (string.IsNullOrWhiteSpace(providerId))
        {
            await WriteJsonAsync(context.Response, 400, new { error = "providerId is required." })
                .ConfigureAwait(false);
            return;
        }

        var models = await _serviceFactory().GetModelsAsync(providerId, cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(context.Response, 200, models).ConfigureAwait(false);
    }

    private async Task HandleChatAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = await JsonSerializer.DeserializeAsync<AiControlChatRequest>(
            context.Request.InputStream,
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
        if (request is null || string.IsNullOrWhiteSpace(request.ProviderId) || string.IsNullOrWhiteSpace(request.ModelId))
        {
            await WriteJsonAsync(context.Response, 400, new { error = "providerId, modelId, and messages are required." })
                .ConfigureAwait(false);
            return;
        }

        var streamType = context.Request.AcceptTypes?.FirstOrDefault(type =>
            type.Equals("text/event-stream", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("application/x-ndjson", StringComparison.OrdinalIgnoreCase));
        if (streamType is not null)
        {
            await StreamChatAsync(
                context.Response,
                request,
                streamType.Equals("text/event-stream", StringComparison.OrdinalIgnoreCase),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var chunks = new List<AiResponseChunk>();
        await foreach (var chunk in _serviceFactory().ChatAsync(
            request.ProviderId,
            new AiChatRequest(request.ModelId, request.Messages, request.Context, request.AutonomyPolicy),
            cancellationToken))
        {
            chunks.Add(chunk);
        }

        await WriteJsonAsync(context.Response, 200, chunks).ConfigureAwait(false);
    }

    private async Task StreamChatAsync(
        HttpListenerResponse response,
        AiControlChatRequest request,
        bool isEventStream,
        CancellationToken cancellationToken)
    {
        response.StatusCode = 200;
        response.ContentType = isEventStream
            ? "text/event-stream; charset=utf-8"
            : "application/x-ndjson; charset=utf-8";
        response.SendChunked = true;

        await foreach (var chunk in _serviceFactory().ChatAsync(
            request.ProviderId,
            new AiChatRequest(request.ModelId, request.Messages, request.Context, request.AutonomyPolicy),
            cancellationToken))
        {
            var json = JsonSerializer.Serialize(chunk, JsonOptions);
            var line = isEventStream ? $"data: {json}\n\n" : json + "\n";
            var bytes = Encoding.UTF8.GetBytes(line);
            await response.OutputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await response.OutputStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleActionsAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = await JsonSerializer.DeserializeAsync<AiControlActionRequest>(
            context.Request.InputStream,
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
        if (request is null || request.Batch is null)
        {
            await WriteJsonAsync(context.Response, 400, new { error = "batch is required." })
                .ConfigureAwait(false);
            return;
        }

        var result = await _serviceFactory().ExecuteAsync(
            request.Batch,
            request.AutonomyPolicy,
            request.ApprovalGranted,
            request.DestructiveApprovalGranted,
            cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(context.Response, result.IsSuccessful ? 200 : 400, result)
            .ConfigureAwait(false);
    }

    private bool IsAuthorized(HttpListenerRequest request)
    {
        var authorization = request.Headers["Authorization"];
        const string prefix = "Bearer ";
        if (authorization is null || !authorization.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var presented = Encoding.UTF8.GetBytes(authorization[prefix.Length..]);
        var expected = Encoding.UTF8.GetBytes(_token);
        return CryptographicOperations.FixedTimeEquals(presented, expected);
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, object value)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        response.ContentLength64 = payload.Length;
        await response.OutputStream.WriteAsync(payload).ConfigureAwait(false);
    }
}
