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
        using var endpoint = new LoopbackServer();
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
        }).AddSingleton<IAiCredentials>(new FixedAiCredentials())
            .AddSingleton<ModelProfiles>().AddSingleton<InferenceService>().BuildServiceProvider();
        var serve = endpoint.ReplyOnce("application/x-ndjson", """
            {"model":"fixture","created_at":"2026-09-22T00:00:00Z","message":{"role":"assistant","content":"done"},"done":true,"done_reason":"stop","prompt_eval_count":1,"eval_count":1}
            """ + "\n", ct);
        await services.GetRequiredService<InferenceService>().Generate(new([new("user", [new AiText("hi")])],
            Options: new(Reasoning: "high", Provider: new DigitalBrain.AI.Ollama.OllamaInferenceOptions(ContextWindowTokens: 6144))), cancellationToken: ct);
        using var request = JsonDocument.Parse((await serve).Body);
        Assert.Equal("high", request.RootElement.GetProperty("think").GetString());
        Assert.Equal(6144, request.RootElement.GetProperty("options").GetProperty("num_ctx").GetInt32());
    }

    [Fact]
    public async Task RealOpenAIAdapterReturnsToolCallsWithoutExecution()
    {
        var ct = TestContext.Current.CancellationToken;
        using var endpoint = new LoopbackServer();
        await using var brain = await UnitTest.Create().WithRegistrations(AiRegistrationSeeds.OpenAI(endpoint: endpoint.Url)).WithModule<AIModule>()
            .ConfigureSilo(silo => silo.Services.Configure<AIOptions>(Configure))
            .StartAsync(ct);
        var serve = endpoint.ReplyOnce("application/json", """
            {"id":"response-1","object":"chat.completion","created":1,"model":"test-model","choices":[{"index":0,"message":{"role":"assistant","content":null,"tool_calls":[{"id":"call-1","type":"function","function":{"name":"lookup","arguments":"{\"id\":42}"}}]},"finish_reason":"tool_calls"}],"usage":{"prompt_tokens":10,"completion_tokens":5,"total_tokens":15}}
            """, ct);
        var llm = brain.Get<ILLM>("default");
        await using var started = await brain.Observe<InferenceStarted>(llm, ct);
        await using var completed = await brain.Observe<InferenceCompleted>(llm, ct);
        var result = await llm.Generate(new(
            [new("user", [new AiText("lookup")])],
            Options: new(MaxOutputTokens: 123),
            Tools: [new("lookup", "Lookup item", "{\"type\":\"object\",\"properties\":{\"id\":{\"type\":\"integer\"}}}")]), cancellationToken: ct);
        var request = JsonDocument.Parse((await serve).Body);
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
        using var endpoint = new LoopbackServer();
        using var services = Services(endpoint.Url);
        var serve = endpoint.ReplyOnce("text/event-stream", """
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
        => new ServiceCollection().AddOptions().Configure<AIOptions>(Configure)
            .AddSingleton<IAiCredentials>(new FixedAiCredentials().Ready("openai", "test-only", endpoint))
            .AddSingleton<ModelProfiles>().AddSingleton<InferenceService>().BuildServiceProvider();

    private static void Configure(AIOptions options)
    {
        options.Default.Provider = "OpenAI";
        options.Default.Model = "test-model";
        options.Default.Capabilities = LlmCapabilities.Tools;
    }
}
