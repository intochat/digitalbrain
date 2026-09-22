using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DigitalBrain.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Tests;

public sealed class InferenceTransportFacts
{
    [Fact]
    public async Task OllamaReasoningAndContextSettingsReachTheProvider()
    {
        var ct = TestContext.Current.CancellationToken;
        using var endpoint = new Loopback();
        using var services = new ServiceCollection().AddOptions().Configure<AIOptions>(options =>
        {
            options.Ollama.Endpoint = endpoint.Url;
            options.Default.Profile = "local";
            options.ModelProfiles["local"] = new()
            {
                Provider = "Ollama",
                Model = "fixture",
                Endpoint = endpoint.Url,
                AllowedReasoning = ["high"],
                ContextWindowTokens = 8192
            };
        }).AddSingleton<ModelProfiles>().AddSingleton<InferenceService>().BuildServiceProvider();
        var serve = endpoint.Reply("application/x-ndjson", """
            {"model":"fixture","created_at":"2026-09-22T00:00:00Z","message":{"role":"assistant","content":"done"},"done":true,"done_reason":"stop","prompt_eval_count":1,"eval_count":1}
            """ + "\n", ct);
        await services.GetRequiredService<InferenceService>().Generate(new([new("user", [new AiText("hi")])],
            Options: new(Reasoning: "high", Provider: new DigitalBrain.AI.Ollama.OllamaInferenceOptions(ContextWindowTokens: 6144))), cancellationToken: ct);
        using var request = JsonDocument.Parse(await serve);
        Assert.Equal("high", request.RootElement.GetProperty("think").GetString());
        Assert.Equal(6144, request.RootElement.GetProperty("options").GetProperty("num_ctx").GetInt32());
    }

    [Fact]
    public async Task RealOpenAIAdapterReturnsToolCallsWithoutExecution()
    {
        var ct = TestContext.Current.CancellationToken;
        using var endpoint = new Loopback();
        await using var brain = await UnitTest.Create().WithModule<AIModule>()
            .ConfigureSilo(silo => silo.Services.Configure<AIOptions>(options => Configure(options, endpoint.Url)))
            .StartAsync(ct);
        var serve = endpoint.Reply("application/json", """
            {"id":"response-1","object":"chat.completion","created":1,"model":"test-model","choices":[{"index":0,"message":{"role":"assistant","content":null,"tool_calls":[{"id":"call-1","type":"function","function":{"name":"lookup","arguments":"{\"id\":42}"}}]},"finish_reason":"tool_calls"}],"usage":{"prompt_tokens":10,"completion_tokens":5,"total_tokens":15}}
            """, ct);
        var llm = brain.Get<ILLM>("default");
        await using var started = await brain.Observe<InferenceStarted>(llm, ct);
        await using var completed = await brain.Observe<InferenceCompleted>(llm, ct);
        var result = await llm.Generate(new(
            [new("user", [new AiText("lookup")])],
            Options: new(MaxOutputTokens: 123),
            Tools: [new("lookup", "Lookup item", "{\"type\":\"object\",\"properties\":{\"id\":{\"type\":\"integer\"}}}")]), cancellationToken: ct);
        var request = JsonDocument.Parse(await serve);
        Assert.Equal("test-model", request.RootElement.GetProperty("model").GetString());
        Assert.True(request.RootElement.TryGetProperty("max_completion_tokens", out var tokens)
            || request.RootElement.TryGetProperty("max_tokens", out tokens));
        Assert.Equal(123, tokens.GetInt32());
        Assert.Single(request.RootElement.GetProperty("tools").EnumerateArray());
        Assert.Equal("call-1", Assert.IsType<AiToolCall>(Assert.Single(Assert.Single(result.Messages).Content)).CallId);
        Assert.Equal(15, result.Usage?.TotalTokens);
        Assert.Equal((await started.NextAsync(ct: ct)).OperationId, (await completed.NextAsync(ct: ct)).OperationId);
    }

    [Fact]
    public async Task RealStreamingAdapterPreservesTextFinishAndUsage()
    {
        var ct = TestContext.Current.CancellationToken;
        using var endpoint = new Loopback();
        using var services = Services(endpoint.Url);
        var serve = endpoint.Reply("text/event-stream", """
            data: {"id":"response-2","object":"chat.completion.chunk","created":1,"model":"test-model","choices":[{"index":0,"delta":{"role":"assistant","content":"hello"},"finish_reason":null}]}

            data: {"id":"response-2","object":"chat.completion.chunk","created":1,"model":"test-model","choices":[{"index":0,"delta":{},"finish_reason":"stop"}],"usage":{"prompt_tokens":2,"completion_tokens":1,"total_tokens":3}}

            data: [DONE]


            """, ct);
        var updates = new List<InferenceUpdate>();
        await foreach (var update in services.GetRequiredService<InferenceService>().GenerateStreaming(
            new([new("user", [new AiText("hello")])]), cancellationToken: ct)) { updates.Add(update); }
        await serve;
        Assert.Equal("hello", string.Concat(updates.SelectMany(u => u.Content).OfType<AiText>().Select(t => t.Text)));
        Assert.Contains(updates, u => u.FinishReason == "stop");
        Assert.Contains(updates, u => u.Usage?.TotalTokens == 3);
    }

    private static ServiceProvider Services(string endpoint)
        => new ServiceCollection().AddOptions().Configure<AIOptions>(options => Configure(options, endpoint))
            .AddSingleton<ModelProfiles>().AddSingleton<InferenceService>().BuildServiceProvider();

    private static void Configure(AIOptions options, string endpoint)
    {
        options.OpenAI.ApiKey = "test-only";
        options.OpenAI.Endpoint = endpoint;
        options.Default.Provider = "OpenAI";
        options.Default.Model = "test-model";
        options.Default.Capabilities = LlmCapabilities.Tools;
    }

    private sealed class Loopback : IDisposable
    {
        private readonly HttpListener _listener = new();
        public string Url { get; }
        public Loopback()
        {
            using var reservation = new TcpListener(IPAddress.Loopback, 0);
            reservation.Start();
            var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
            reservation.Stop();
            Url = $"http://localhost:{port}/";
            _listener.Prefixes.Add(Url);
            _listener.Start();
        }
        public async Task<string> Reply(string mediaType, string response, CancellationToken cancellationToken)
        {
            var context = await _listener.GetContextAsync().WaitAsync(cancellationToken);
            using var reader = new StreamReader(context.Request.InputStream);
            var body = await reader.ReadToEndAsync(cancellationToken);
            context.Response.ContentType = mediaType;
            var bytes = Encoding.UTF8.GetBytes(response);
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
            context.Response.Close();
            return body;
        }
        public void Dispose() => _listener.Close();
    }
}