using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using DigitalBrain.AI.OpenAI;
using DigitalBrain.Product.Identity;
using DigitalBrain.Product.Interactions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class GenAiTelemetryTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task ConfiguredPipelineCapturesModelAndToolContentOnlyWhenOptedIn(bool enabled, bool streaming)
    {
        using var provider = Services(new Dictionary<string, string?>
        {
            [AIClients.SensitiveTelemetryKey] = enabled ? "true" : "false",
        });
        using var client = AIClients.BuildChatPipeline(provider,
            LLMModel.FindByMarker(typeof(IGpt56Luna))!, new TelemetryModelClient());
        var spans = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("DigitalBrain.AI", StringComparison.Ordinal),
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => spans.Enqueue(activity),
        };
        ActivitySource.AddActivityListener(listener);
        var principal = new PrincipalId(Guid.NewGuid());
        var owner = new OwnerId("telemetry-owner");
        var chat = new NeuronId("chat", owner, PrincipalPartition.InstanceName(principal, "telemetry"));
        var command = CommandId.New();
        using var turn = AgentTurnContext.Enter(new AgentTurnContext(chat, command, new ActorContext(principal, "owner")));
        ActivityTraceId traceId;
        ActivitySpanId agentSpanId;
        var invoked = 0;
        var options = new ChatOptions
        {
            ModelId = "test-model",
            Tools =
            [
                AIFunctionFactory.Create((string repository) =>
                {
                    Assert.Equal("private-repository", repository);
                    invoked++;
                    return "private-tool-result";
                }, new AIFunctionFactoryOptions { Name = "review_diff" }),
            ],
        };
        ChatResponse response;
        using (var agent = AgentTelemetry.Start(new NeuronId("assistant", owner, "assistant"), "Ino",
            client.GetService<OpenTelemetryChatClient>()?.EnableSensitiveData is true))
        {
            Assert.NotNull(agent);
            traceId = agent.TraceId;
            agentSpanId = agent.SpanId;
            IReadOnlyList<ChatMessage> messages =
            [
                new(ChatRole.System, "private-system-instructions"),
                new(ChatRole.User, "private-user-request"),
            ];
            response = streaming
                ? await client.GetStreamingResponseAsync(messages, options, TestContext.Current.CancellationToken)
                    .ToChatResponseAsync(TestContext.Current.CancellationToken)
                : await client.GetResponseAsync(messages, options, TestContext.Current.CancellationToken);
        }

        Assert.Equal(1, invoked);
        Assert.Contains("private-assistant-reply", response.Text, StringComparison.Ordinal);
        var ownSpans = spans.Where(span => span.TraceId == traceId).ToArray();
        var agentSpan = Assert.Single(ownSpans, span => span.SpanId == agentSpanId);
        Assert.Equal("Ino", agentSpan.GetTagItem("gen_ai.agent.name"));
        Assert.Equal(chat.ToString(), agentSpan.GetTagItem("gen_ai.conversation.id"));
        Assert.Equal(command.ToString(), agentSpan.GetTagItem("db.command.id"));
        Assert.DoesNotContain(agentSpan.TagObjects, tag => tag.Key == "__EnableSensitiveData__");
        var modelSpans = ownSpans.Where(span => Equals(span.GetTagItem("gen_ai.operation.name"), "chat")).ToArray();
        Assert.Equal(2, modelSpans.Length);
        Assert.All(modelSpans, span =>
        {
            Assert.Equal("test-model", span.GetTagItem("gen_ai.request.model"));
            Assert.Equal("test-provider", span.GetTagItem("gen_ai.provider.name"));
            Assert.NotNull(span.GetTagItem("gen_ai.usage.input_tokens"));
            Assert.NotNull(span.GetTagItem("gen_ai.usage.output_tokens"));
        });
        var toolSpan = Assert.Single(ownSpans,
            span => Equals(span.GetTagItem("gen_ai.operation.name"), "execute_tool"));
        Assert.Equal(agentSpanId, toolSpan.ParentSpanId);
        Assert.Equal("review_diff", toolSpan.GetTagItem("gen_ai.tool.name"));
        Assert.Equal("call-1", toolSpan.GetTagItem("gen_ai.tool.call.id"));
        var recorded = string.Join('\n', ownSpans.SelectMany(span => span.TagObjects).Select(tag => $"{tag.Key}={tag.Value}"));
        foreach (var content in new[]
        {
            "private-system-instructions", "private-user-request", "private-repository",
            "private-tool-result", "private-assistant-reply",
        })
        {
            if (enabled) { Assert.Contains(content, recorded, StringComparison.Ordinal); }
            else { Assert.DoesNotContain(content, recorded, StringComparison.Ordinal); }
        }
        if (enabled)
        {
            Assert.NotNull(toolSpan.GetTagItem("gen_ai.tool.call.arguments"));
            Assert.NotNull(toolSpan.GetTagItem("gen_ai.tool.call.result"));
        }
        else
        {
            Assert.Null(toolSpan.GetTagItem("gen_ai.tool.call.arguments"));
            Assert.Null(toolSpan.GetTagItem("gen_ai.tool.call.result"));
            Assert.All(modelSpans, span =>
            {
                Assert.Null(span.GetTagItem("gen_ai.input.messages"));
                Assert.Null(span.GetTagItem("gen_ai.output.messages"));
            });
        }
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData(null, "true", true)]
    [InlineData("false", "true", false)]
    [InlineData("true", "false", true)]
    public void ExplicitModuleSettingOverridesStandardEnvironmentAndDefaultIsOff(string? module, string? standard, bool expected)
    {
        using var provider = Services(new Dictionary<string, string?>
        {
            [AIClients.SensitiveTelemetryKey] = module,
            ["OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT"] = standard,
        });
        using var client = AIClients.BuildChatPipeline(provider,
            LLMModel.FindByMarker(typeof(IGpt56Luna))!, new TelemetryModelClient());
        Assert.Equal(expected, client.GetService<OpenTelemetryChatClient>()!.EnableSensitiveData);
    }

    private static ServiceProvider Services(Dictionary<string, string?> configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build());
        // The real host defaults Microsoft categories to Warning. Content is recorded
        // on semantic spans without requiring broad Trace logging of payloads.
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        return services.BuildServiceProvider();
    }

    private sealed class TelemetryModelClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var completedTool = messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Any();
            var message = completedTool
                ? new ChatMessage(ChatRole.Assistant, "private-assistant-reply")
                : new ChatMessage(ChatRole.Assistant,
                    [new FunctionCallContent("call-1", "review_diff", new Dictionary<string, object?> { ["repository"] = "private-repository" })]);
            return Task.FromResult(new ChatResponse(message)
            {
                ModelId = "test-model", FinishReason = completedTool ? ChatFinishReason.Stop : ChatFinishReason.ToolCalls,
                Usage = new UsageDetails { InputTokenCount = 11, OutputTokenCount = 7 },
            });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            foreach (var update in response.ToChatResponseUpdates()) { yield return update; }
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is not null ? null
                : serviceType == typeof(ChatClientMetadata) ? new ChatClientMetadata("test-provider", new Uri("https://example.test"), "test-model")
                : serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() { }
    }
}
