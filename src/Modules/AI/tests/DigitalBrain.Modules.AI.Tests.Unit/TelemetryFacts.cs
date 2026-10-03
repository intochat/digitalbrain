using System.Diagnostics;
using System.Runtime.CompilerServices;
using DigitalBrain.AI;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Modules.AI.Tests.Unit;

public sealed class TelemetryFacts
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task ConfigurationAloneControlsMessageCapture(bool enabled, bool streaming)
    {
        var name = "capture-" + Guid.NewGuid().ToString("N");
        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "DigitalBrain.AI." + name,
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(listener);
        using var services = new ServiceCollection()
            .AddSingleton(Options.Create(new AIOptions { Telemetry = new() { EnableSensitiveData = enabled } }))
            .BuildServiceProvider();
        // No request scope, identity, profile or classification: the host setting is sufficient,
        // including for a singleton client used repeatedly by background application neurons.
        using var client = AIClients.BuildChatPipeline(services, true, name, Client(), useFunctionInvocation: false);
        ChatMessage[] messages = [new(ChatRole.User, "capture-prompt-canary"),
            new(ChatRole.Tool, [new FunctionResultContent("call-1", "capture-tool-canary")])];
        for (var call = 0; call < 2; call++)
        {
            if (streaming)
            { await foreach (var _ in client.GetStreamingResponseAsync(messages, cancellationToken: TestContext.Current.CancellationToken)) { } }
            else { await client.GetResponseAsync(messages, cancellationToken: TestContext.Current.CancellationToken); }
        }
        Assert.Equal(2, spans.Count);
        foreach (var span in spans)
        {
            var recorded = string.Join("\n", span.TagObjects.Select(tag => tag.Value?.ToString())
                .Concat(span.Events.SelectMany(item => item.Tags.Select(tag => tag.Value?.ToString()))));
            Assert.Equal(enabled, recorded.Contains("capture-prompt-canary", StringComparison.Ordinal));
            Assert.Equal(enabled, recorded.Contains("capture-tool-canary", StringComparison.Ordinal));
            Assert.Equal(enabled, recorded.Contains("capture-answer-canary", StringComparison.Ordinal));
            Assert.Contains(span.TagObjects, tag => tag.Key == "gen_ai.operation.name");
        }
    }

    private static StubChatClient Client() => new(
        (_, _, _) => Task.FromResult(CannedResponse()),
        (_, _, _) => CannedResponse().ToChatResponseUpdates().ToAsyncEnumerable());

    private static ChatResponse CannedResponse() => new(new ChatMessage(ChatRole.Assistant, "capture-answer-canary")) { ModelId = "test-model" };
}
