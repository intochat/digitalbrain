using DigitalBrain.Assistant;
using DigitalBrain.AI.Metering;
using DigitalBrain.Compute;
using DigitalBrain;
using DigitalBrain.Contracts;
using Xunit;

namespace DigitalBrain.Modules.Assistant.Tests.Unit;

public sealed class ComputeUsageFacts
{
    [Fact]
    public void MixedPricedAndUnpricedModelsDoNotPresentAPartialSubtotalAsComplete()
    {
        var priced = new TokenUsageEntry(MeterKind.Chat, "OpenAI", "gpt-5.6-luna", 1000, 0, 0, 1000, 2000, true, DateTimeOffset.UtcNow);
        var unpriced = priced with { Model = "unknown-model" };
        var priceBook = new PriceBook();
        using var intent = IntentContext.Begin("mixed", "workspace");
        var receipt = AgentReceipts.Create(priceBook, intent, new IntentActivity(), AgentRunOutcome.Succeeded, [priced, unpriced]);
        Assert.True(receipt.Compute > 0);
        var row = AssistantUsage.Create("mixed", receipt, priceBook, [priced, unpriced]);
        Assert.Null(row.PreviewCompute);
    }

    [Fact]
    public void RetriedIntentPricesEarlierDurableProviderConsumption()
    {
        using var intent = IntentContext.Begin("retried", "workspace");
        var previous = new TokenUsageEntry(MeterKind.Chat, "OpenAI", "gpt-5.6-luna", 1000, 0, 0, 1000, 2000, true, DateTimeOffset.UtcNow);
        var current = previous with { InputTokens = 2000 };
        intent.AddUsage(current);
        var priceBook = new PriceBook();
        var currentReceipt = AgentReceipts.Create(priceBook, intent, new IntentActivity(), AgentRunOutcome.Succeeded);
        var cumulative = AgentReceipts.Create(priceBook, intent, new IntentActivity(), AgentRunOutcome.Succeeded, [previous, current]);
        Assert.Equal(2, cumulative.ModelCalls);
        Assert.True(cumulative.Compute > currentReceipt.Compute);
    }

    [Fact]
    public void ProjectionPreservesMissingTokensAndSeparatesPreviewFromActual()
    {
        var receipt = new AgentReceipt(AgentRunOutcome.Succeeded, "2 tool calls", [], [], 1, 12m);
        var row = AssistantUsage.Create("run", receipt, new PriceBook(), [new(MeterKind.Chat, "provider", "model", null, null, null, null, null, false, DateTimeOffset.UtcNow)]);
        Assert.Null(row.PreviewCompute);
        Assert.Null(row.ChargedCompute);
        Assert.Null(row.ReservedCompute);
        Assert.Null(row.SettledCompute);
        Assert.Equal("Assistant response", row.Title);
        Assert.Null(row.ModelUsage[0].InputTokens);
        Assert.False(row.ModelUsage[0].UsageReported);
        Assert.NotEqual(ComputeUsageEndpoints.IntentId("scope", "thread", "run"), ComputeUsageEndpoints.IntentId("other", "thread", "run"));
    }
}

