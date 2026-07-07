using DigitalBrain.TestKit;
using DigitalBrain.Core;
using DigitalBrain.Ino.Context;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Ino.Tests;

public class ContextNeuronTests : NeuronTestBase
{
    [Fact]
    public async Task Remember_Then_Recall_Finds_The_Text()
    {
        var context = Grain<IContextNeuron>("context-recall-test");
        await context.RememberAsync("the sky is blue today");
        var results = await context.RecallAsync("sky color");
        Assert.Contains("the sky is blue today", results);
    }

    [Fact]
    public async Task Signal_RecallRequested_Replies_With_Signal_RecallCompleted()
    {
        var context = Grain<IContextNeuron>("context-signal-test");
        await context.RememberAsync("the launch date is March 5th");

        await context.DeliverAsync(new Signal(
            ContextSignals.RecallRequested,
            new Dictionary<string, object?> { ["query"] = "launch date", ["chatId"] = 123L })
        { Receiver = new NeuronId("context-signal-test") });

        var outgoing = await context.GetTimelineAsync();
        Assert.Contains(outgoing, s => s is Signal reply
            && reply.Name == ContextSignals.RecallCompleted
            && reply.Props.TryGetValue("results", out var r)
            && r is string[] results && results.Contains("the launch date is March 5th")
            && reply.Props.TryGetValue("chatId", out var c) && Equals(c, 123L)
            && !reply.Props.ContainsKey("query"));
    }
}

internal sealed class TimingOutEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

public sealed class ContextNeuronEmbeddingUnavailableTests : NeuronTestBase
{
    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services =>
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>, TimingOutEmbeddingGenerator>());

    [Fact]
    public async Task Recall_Falls_Back_To_Keyword_Match_When_Embedding_Generator_Times_Out()
    {
        var context = Grain<IContextNeuron>("context-embed-timeout-test");
        await context.RememberAsync("the sky is blue today");

        var results = await context.RecallAsync("sky color");

        Assert.Contains("the sky is blue today", results);
    }
}
