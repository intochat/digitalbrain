using System.Collections.Concurrent;
using DigitalBrain.Product.Conversation;
using DigitalBrain.Product.Presentation;
using DigitalBrain.Product.SalesInsights;
using DigitalBrain.Testing;

namespace DigitalBrain.Product.Tests.SalesInsights;

public sealed class SalesInsightsTests(DigitalBrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder composition)
        => composition
            .RegisterVocabulary(typeof(ChatSalesRequested).Assembly)
            .RegisterVocabulary(typeof(SalesInsightRequested).Assembly)
            .RegisterVocabulary(typeof(SalesInsightSurfaceRequested).Assembly)
            .RegisterIngress<ChatSalesRequested>()
            .RegisterWorkspaceService<ISalesRevenueReader>(workspace => Readers.For(workspace.Id))
            .RegisterNeuron<ConversationIngressNeuron>(ConversationIngressNeuron.Kind)
            .RegisterNeuron<SalesInsightNeuron>(SalesInsightNeuron.Kind)
            .RegisterNeuron<SalesInsightEffectNeuron>(SalesInsightEffectNeuron.Kind)
            .RegisterNeuron<SalesInsightProjectionNeuron>(SalesInsightProjectionNeuron.Kind);

    [Fact]
    public async Task ChatRequestCreatesOneDailyClosedWonRevenueSurface()
    {
        const string scope = "workspace/sales-insights";
        const string conversationId = "conversation/sales-acme";
        const string queryId = "sales-last-week";
        Readers.Reset(scope);
        Readers.For(scope).Records =
        [
            new SalesRevenueRecord(new DateOnly(2026, 8, 3), 100m, "USD"),
            new SalesRevenueRecord(new DateOnly(2026, 8, 3), 50m, "USD"),
            new SalesRevenueRecord(new DateOnly(2026, 8, 5), 75m, "USD"),
        ];
        var query = new SalesQuery(
            queryId,
            new SalesDateRange(new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 10)),
            "USD");
        var chat = OpenWorkspace(scope, conversationId, typeof(ChatSalesRequested));

        await chat.Publisher.PublishAsync(new ChatSalesRequested(query), Cancellation);

        var projection = new NeuronId(SalesInsightProjectionNeuron.Kind, queryId);
        var page = await WaitForJournalAsync(
            chat,
            projection,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(SalesInsightSurfaceRequested).FullName),
            "the semantic daily revenue surface",
            Cancellation);
        var surface = page.Records.Single(record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(SalesInsightSurfaceRequested).FullName);

        Assert.Equal(queryId, surface.Serialization.GetProperty("queryId").GetString());
        Assert.Equal("USD", surface.Serialization.GetProperty("currencyCode").GetString());
        Assert.Equal(225m, surface.Serialization.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(3, surface.Serialization.GetProperty("closedDealCount").GetInt32());
        Assert.Equal(
            conversationId,
            surface.Serialization.GetProperty("context").GetProperty("reference").GetString());
        Assert.Equal(
            [(int)SalesInsightDisplay.BarChart, (int)SalesInsightDisplay.Table],
            surface.Serialization.GetProperty("displays").EnumerateArray().Select(static display => display.GetInt32()));
        Assert.Equal(
            [(int)SalesInsightPlacement.Chat, (int)SalesInsightPlacement.ContextDrawer],
            surface.Serialization.GetProperty("placements").EnumerateArray().Select(static placement => placement.GetInt32()));

        var buckets = surface.Serialization.GetProperty("buckets").EnumerateArray().ToArray();
        Assert.Equal(7, buckets.Length);
        Assert.Equal("2026-08-03", buckets[0].GetProperty("date").GetString());
        Assert.Equal(150m, buckets[0].GetProperty("amount").GetDecimal());
        Assert.Equal(2, buckets[0].GetProperty("closedDealCount").GetInt32());
        Assert.Equal("2026-08-04", buckets[1].GetProperty("date").GetString());
        Assert.Equal(0m, buckets[1].GetProperty("amount").GetDecimal());
        Assert.Equal("2026-08-09", buckets[6].GetProperty("date").GetString());
        Assert.Equal(0m, buckets[6].GetProperty("amount").GetDecimal());

        var serialization = surface.Serialization.GetRawText();
        Assert.DoesNotContain("workspace", serialization, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("soql", serialization, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", serialization, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("action", serialization, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReaderFailureRendersRedactedUnavailableSurfaceInsteadOfAZeroChart()
    {
        const string scope = "workspace/sales-insights-unavailable";
        const string conversationId = "conversation/sales-unavailable";
        const string queryId = "sales-unavailable";
        Readers.Reset(scope);
        Readers.For(scope).FailRead = true;
        var chat = OpenWorkspace(scope, conversationId, typeof(ChatSalesRequested));
        var query = new SalesQuery(
            queryId,
            new SalesDateRange(new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 10)),
            "USD");

        await chat.Publisher.PublishAsync(new ChatSalesRequested(query), Cancellation);

        var projection = new NeuronId(SalesInsightProjectionNeuron.Kind, queryId);
        var page = await WaitForJournalAsync(
            chat,
            projection,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(SalesInsightUnavailableSurfaceRequested).FullName),
            "the redacted unavailable sales surface",
            Cancellation);
        var unavailable = page.Records.Single(record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(SalesInsightUnavailableSurfaceRequested).FullName);
        Assert.Equal(queryId, unavailable.Serialization.GetProperty("queryId").GetString());
        Assert.Equal(
            conversationId,
            unavailable.Serialization.GetProperty("context").GetProperty("reference").GetString());
        Assert.Equal(
            SalesInsightUnavailableReason.ReaderUnavailable,
            (SalesInsightUnavailableReason)unavailable.Serialization.GetProperty("reason").GetInt32());
        Assert.DoesNotContain(page.Records, record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(SalesInsightSurfaceRequested).FullName);
        Assert.DoesNotContain("provider-secret", unavailable.Serialization.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReactivatedQueryRetainsTheFirstCompletedSnapshotWhenChatDeliveryRepeats()
    {
        const string scope = "workspace/sales-insights-replay";
        const string conversationId = "conversation/sales-replay";
        const string queryId = "sales-replay";
        Readers.Reset(scope);
        Readers.For(scope).Records =
        [
            new SalesRevenueRecord(new DateOnly(2026, 8, 3), 100m, "USD"),
            new SalesRevenueRecord(new DateOnly(2026, 8, 5), 125m, "USD"),
        ];
        var query = new SalesQuery(
            queryId,
            new SalesDateRange(new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 10)),
            "USD");
        var chat = OpenWorkspace(scope, conversationId, typeof(ChatSalesRequested));
        var salesInsight = new NeuronId(SalesInsightNeuron.Kind, queryId);
        var conversation = new NeuronId(ConversationIngressNeuron.Kind, conversationId);

        await chat.Publisher.PublishAsync(new ChatSalesRequested(query), Cancellation);
        _ = await WaitForJournalAsync(
            chat,
            salesInsight,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(SalesInsightReady).FullName),
            "the first frozen sales insight",
            Cancellation);

        await DeactivateAsync(scope, [salesInsight], Cancellation);
        Readers.For(scope).Records =
        [
            new SalesRevenueRecord(new DateOnly(2026, 8, 3), 999m, "USD"),
        ];
        await chat.Publisher.PublishAsync(new ChatSalesRequested(query), Cancellation);

        _ = await WaitForJournalAsync(
            chat,
            conversation,
            observed => observed.Records.Count(record => record.Direction == JournalRecordDirection.Received
                && record.SynapseKind == typeof(ChatSalesRequested).FullName) == 2,
            "the repeated trusted chat request",
            Cancellation);
        await DrainAsync(scope, conversation, Cancellation);

        var page = await WaitForJournalAsync(
            chat,
            salesInsight,
            observed => observed.Records.Count(record => record.Direction == JournalRecordDirection.Received
                && record.SynapseKind == typeof(SalesInsightRequested).FullName) == 2
                && observed.Records.Count(record => record.Direction == JournalRecordDirection.Produced
                    && record.SynapseKind == typeof(SalesInsightReady).FullName) == 1,
            "the original completed insight after reactivation",
            Cancellation);
        var ready = page.Records.Single(record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(SalesInsightReady).FullName);
        Assert.Equal(225m, ready.Serialization.GetProperty("result").GetProperty("totalAmount").GetDecimal());
        Assert.Equal(2, ready.Serialization.GetProperty("result").GetProperty("closedDealCount").GetInt32());
    }

    [Fact]
    public void RejectsAReportingRangeLongerThanOneYearBeforeItCanReachAReader()
    {
        var failure = Assert.Throws<ArgumentOutOfRangeException>(() => new SalesDateRange(
            new DateOnly(2025, 1, 1),
            new DateOnly(2026, 1, 3)));

        Assert.Equal("toExclusive", failure.ParamName);
    }

    [Fact]
    public void RejectsANullTypedChatQueryAtTheIngressBoundary()
    {
        _ = Assert.Throws<ArgumentNullException>(() => new ChatSalesRequested(null!));
    }

    [Fact]
    public async Task OverflowingReaderAmountsRenderAnInvalidDataSurfaceInsteadOfRetryingTheQuery()
    {
        const string scope = "workspace/sales-insights-overflow";
        const string conversationId = "conversation/sales-overflow";
        const string queryId = "sales-overflow";
        Readers.Reset(scope);
        Readers.For(scope).Records =
        [
            new SalesRevenueRecord(new DateOnly(2026, 8, 3), decimal.MaxValue, "USD"),
            new SalesRevenueRecord(new DateOnly(2026, 8, 3), 1m, "USD"),
        ];
        var chat = OpenWorkspace(scope, conversationId, typeof(ChatSalesRequested));
        var query = new SalesQuery(
            queryId,
            new SalesDateRange(new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 10)),
            "USD");

        await chat.Publisher.PublishAsync(new ChatSalesRequested(query), Cancellation);

        var projection = new NeuronId(SalesInsightProjectionNeuron.Kind, queryId);
        var page = await WaitForJournalAsync(
            chat,
            projection,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(SalesInsightUnavailableSurfaceRequested).FullName),
            "the invalid-data sales surface after an aggregate overflow",
            Cancellation);
        var unavailable = page.Records.Single(record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(SalesInsightUnavailableSurfaceRequested).FullName);
        Assert.Equal(
            SalesInsightUnavailableReason.InvalidReaderData,
            (SalesInsightUnavailableReason)unavailable.Serialization.GetProperty("reason").GetInt32());
        Assert.DoesNotContain(page.Records, record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(SalesInsightSurfaceRequested).FullName);
    }

    [Fact]
    public async Task OversizedReaderResultIsRejectedBeforeItBecomesADurableStateMessage()
    {
        const string scope = "workspace/sales-insights-record-limit";
        const string conversationId = "conversation/sales-record-limit";
        const string queryId = "sales-record-limit";
        Readers.Reset(scope);
        Readers.For(scope).Records =
        [
            .. Enumerable
                .Range(0, 10_001)
                .Select(static _ => new SalesRevenueRecord(new DateOnly(2026, 8, 3), 1m, "USD")),
        ];
        var chat = OpenWorkspace(scope, conversationId, typeof(ChatSalesRequested));
        var query = new SalesQuery(
            queryId,
            new SalesDateRange(new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 10)),
            "USD");

        await chat.Publisher.PublishAsync(new ChatSalesRequested(query), Cancellation);

        var effect = new NeuronId(SalesInsightEffectNeuron.Kind, queryId);
        var page = await WaitForJournalAsync(
            chat,
            effect,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && (record.SynapseKind == typeof(SalesRevenueReadCompleted).FullName
                    || record.SynapseKind == typeof(SalesRevenueReadUnavailable).FullName)),
            "the bounded reader-result disposition",
            Cancellation);
        Assert.Contains(page.Records, record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(SalesRevenueReadUnavailable).FullName);
        Assert.DoesNotContain(page.Records, record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(SalesRevenueReadCompleted).FullName);
    }

    private static class Readers
    {
        private static readonly ConcurrentDictionary<string, ControlledSalesRevenueReader> ByWorkspace =
            new(StringComparer.Ordinal);

        internal static ControlledSalesRevenueReader For(string workspace)
            => ByWorkspace.GetOrAdd(workspace, static _ => new ControlledSalesRevenueReader());

        internal static void Reset(string workspace) => For(workspace).Reset();
    }

    private sealed class ControlledSalesRevenueReader : ISalesRevenueReader
    {
        internal IReadOnlyList<SalesRevenueRecord> Records { get; set; } = [];

        internal bool FailRead { get; set; }

        internal void Reset()
        {
            Records = [];
            FailRead = false;
        }

        public Task<IReadOnlyList<SalesRevenueRecord>> ReadClosedWonAsync(
            SalesQuery query,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            if (FailRead)
            {
                throw new InvalidOperationException("provider-secret");
            }

            return Task.FromResult(Records);
        }
    }
}
