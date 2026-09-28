using System.Runtime.CompilerServices;
using DigitalBrain.AI;
using DigitalBrain.AI.Metering;
using DigitalBrain.Compute;
using DigitalBrain.Contracts;
using Microsoft.Extensions.AI;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class MeteringFacts
{
    [Fact]
    public async Task ChatRecordsEveryReportedTokenClassWithProviderAndModelIdentity()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new RecordingSink();
        var usage = new UsageDetails
        {
            InputTokenCount = 10,
            CachedInputTokenCount = 3,
            ReasoningTokenCount = 2,
            OutputTokenCount = 5,
            TotalTokenCount = 15,
        };
        var client = new MeteringChatClient(new FixedChatClient(Response(usage, "gpt-5.6-luna")), sink, "OpenAI", "gpt-5.6-luna");

        using (IntentContext.Begin("intent-1", "scope"))
        {
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: ct);
        }

        var (intentId, entry) = Assert.Single(sink.Records);
        Assert.Equal("intent-1", intentId);
        Assert.Equal(MeterKind.Chat, entry.Meter);
        Assert.Equal("OpenAI", entry.Provider);
        Assert.Equal("gpt-5.6-luna", entry.Model);
        Assert.Equal(10, entry.InputTokens);
        Assert.Equal(3, entry.CachedInputTokens);
        Assert.Equal(2, entry.ReasoningTokens);
        Assert.Equal(5, entry.OutputTokens);
        Assert.Equal(15, entry.TotalTokens);
        Assert.True(entry.UsageReported);
    }

    [Fact]
    public async Task StreamingUsageIsRecordedOnceFromTheFinalChunk()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new RecordingSink();
        var usage = new UsageDetails { InputTokenCount = 7, OutputTokenCount = 4, TotalTokenCount = 11 };
        var client = new MeteringChatClient(new StreamingChatClient(usage), sink, "Anthropic", "claude");

        using (IntentContext.Begin("intent-stream"))
        {
            await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: ct)) { }
        }

        var entry = Assert.Single(sink.Records).Entry;
        Assert.Equal(7, entry.InputTokens);
        Assert.Equal(4, entry.OutputTokens);
        Assert.Equal(11, entry.TotalTokens);
        Assert.True(entry.UsageReported);
    }

    [Fact]
    public async Task MissingProviderUsageIsRecordedAsUnreportedNeverAsZero()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new RecordingSink();
        var client = new MeteringChatClient(new FixedChatClient(Response(null, "gpt-5.6-luna")), sink, "OpenAI", "gpt-5.6-luna");

        using (IntentContext.Begin("intent-nousage"))
        {
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: ct);
        }

        var entry = Assert.Single(sink.Records).Entry;
        Assert.False(entry.UsageReported);
        Assert.Null(entry.InputTokens);
        Assert.Null(entry.CachedInputTokens);
        Assert.Null(entry.ReasoningTokens);
        Assert.Null(entry.OutputTokens);
        Assert.Null(entry.TotalTokens);
    }

    [Fact]
    public async Task NoIntentContextRecordsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new RecordingSink();
        var client = new MeteringChatClient(new FixedChatClient(Response(new UsageDetails { InputTokenCount = 1 }, "gpt")), sink, "OpenAI", "gpt");

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: ct);

        Assert.Empty(sink.Records);
    }

    [Fact]
    public async Task EmbeddingUsageIsRecordedWithProviderAndModelIdentity()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new RecordingSink();
        var generator = new MeteringEmbeddingGenerator(new FixedEmbeddingGenerator(new UsageDetails { InputTokenCount = 9, TotalTokenCount = 9 }), sink, "OpenAI", "text-embedding-3-small");

        using (IntentContext.Begin("intent-embed"))
        {
            await generator.GenerateAsync(["one", "two"], cancellationToken: ct);
        }

        var entry = Assert.Single(sink.Records).Entry;
        Assert.Equal(MeterKind.Embedding, entry.Meter);
        Assert.Equal("OpenAI", entry.Provider);
        Assert.Equal("text-embedding-3-small", entry.Model);
        Assert.Equal(9, entry.InputTokens);
        Assert.Equal(9, entry.TotalTokens);
        Assert.True(entry.UsageReported);
    }

    [Fact]
    public async Task UsageSurvivesGrainReactivation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AIModule>().StartAsync(ct);
        var usage = brain.Get<IIntentUsage>("intent-durable");
        await usage.RecordAsync(new TokenUsageEntry(MeterKind.Chat, "OpenAI", "gpt-5.6-luna", 10, 3, 2, 5, 15, true, DateTimeOffset.UnixEpoch), ct);

        await brain.DeactivateAsync(usage, ct);

        var read = await usage.ReadAsync(ct);
        Assert.Equal("intent-durable", read.IntentId);
        var entry = Assert.Single(read.Entries);
        Assert.Equal(10, entry.InputTokens);
        Assert.Equal(3, entry.CachedInputTokens);
        Assert.Equal(2, entry.ReasoningTokens);
        Assert.Equal(5, entry.OutputTokens);
        Assert.Equal(15, entry.TotalTokens);
    }

    [Fact]
    public async Task IntentScopeBatchesEveryProviderCallIntoOneDurableWrite()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AIModule>().StartAsync(ct);
        var sink = new GrainIntentUsageSink(brain.Grains);
        var usage = brain.Get<IIntentUsage>("intent-batch");
        var chat = new TokenUsageEntry(MeterKind.Chat, "OpenAI", "gpt-5.6-luna", 10, 3, 2, 5, 15, true, DateTimeOffset.UnixEpoch);

        using (var intent = IntentContext.Begin("intent-batch", "scope"))
        {
            await sink.RecordAsync(intent.IntentId, chat, ct);
            await sink.RecordAsync(intent.IntentId, chat with { Meter = MeterKind.Embedding }, ct);
            // Provider calls accumulate in the intent scope; nothing is durable until the flush.
            Assert.Empty((await usage.ReadAsync(ct)).Entries);
            await sink.FlushAsync(intent, ct);
        }

        var recorded = await usage.ReadAsync(ct);
        Assert.Equal(2, recorded.Entries.Length);
        Assert.Equal(MeterKind.Chat, recorded.Entries[0].Meter);
        Assert.Equal(MeterKind.Embedding, recorded.Entries[1].Meter);
    }

    [Fact]
    public async Task IntentScopeFlushesEveryMeterEventAsOneBatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var meters = new RecordingBatchMeterSink();
        await using var brain = await UnitTest.Create().WithModule<AIModule>().StartAsync(ct);
        var sink = new GrainIntentUsageSink(brain.Grains, meters);
        var chat = new TokenUsageEntry(MeterKind.Chat, "OpenAI", "gpt-5.6-luna", 10, 3, 2, 5, 15, true, DateTimeOffset.UnixEpoch);

        using (var intent = IntentContext.Begin("intent-meter-batch", "scope"))
        {
            await sink.RecordAsync(intent.IntentId, chat, ct);
            await sink.RecordAsync(intent.IntentId, chat with { Meter = MeterKind.Embedding }, ct);
            await sink.FlushAsync(intent, ct);
        }

        var batch = Assert.Single(meters.Batches);
        // One chat entry emits five token classes; the batch carries both entries in one call.
        Assert.Equal(10, batch.Length);
        Assert.Equal(MeterSource.ChatClient, batch.First().Source);
    }

    [Fact]
    public void EveryReportedTokenClassBecomesItsOwnIdempotentMeterEvent()
    {
        var entry = new TokenUsageEntry(MeterKind.Chat, "OpenAI", "gpt-5.6-luna", 10, 3, 2, 5, 15, true, DateTimeOffset.UnixEpoch);

        var events = IntentMeterEvents.FromUsage("intent-1", "scope", entry).ToArray();

        Assert.Equal(
            ["input_tokens", "cached_input_tokens", "reasoning_tokens", "output_tokens", "total_tokens"],
            events.Select(meterEvent => meterEvent.Step));
        Assert.All(events, meterEvent => Assert.Equal($"llm.gpt-5.6-luna.{meterEvent.Step}", meterEvent.MeterId));
        Assert.All(events, meterEvent =>
        {
            Assert.Equal("intent-1", meterEvent.IntentId);
            Assert.Equal("scope", meterEvent.WorkspaceId);
            Assert.Equal(MeterSource.ChatClient, meterEvent.Source);
        });
    }

    private static ChatResponse Response(UsageDetails? usage, string modelId)
        => new(new ChatMessage(ChatRole.Assistant, "ok")) { Usage = usage, ModelId = modelId };

    private sealed class RecordingSink : IIntentUsageSink
    {
        public List<(string IntentId, TokenUsageEntry Entry)> Records { get; } = [];

        public Task RecordAsync(string intentId, TokenUsageEntry entry, CancellationToken cancellationToken = default)
        {
            Records.Add((intentId, entry));
            return Task.CompletedTask;
        }

        public Task FlushAsync(IntentContext intent, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingBatchMeterSink : IBatchMeterSink
    {
        public List<MeterEvent[]> Batches { get; } = [];

        public ValueTask RecordAsync(MeterEvent meterEvent, CancellationToken cancellationToken = default)
        {
            Batches.Add([meterEvent]);
            return ValueTask.CompletedTask;
        }

        public ValueTask<int> RecordBatchAsync(IReadOnlyList<MeterEvent> meterEvents, CancellationToken cancellationToken = default)
        {
            Batches.Add([.. meterEvents]);
            return ValueTask.FromResult(meterEvents.Count);
        }
    }

    private sealed class FixedChatClient(ChatResponse response) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(response);

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() { }
    }

    private sealed class StreamingChatClient(UsageDetails usage) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return new ChatResponseUpdate(ChatRole.Assistant, "par");
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(usage)]);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() { }
    }

    private sealed class FixedEmbeddingGenerator(UsageDetails usage) : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(values.Select(value => new Embedding<float>(new float[] { value.Length, 1 }))) { Usage = usage });

        public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() { }
    }
}