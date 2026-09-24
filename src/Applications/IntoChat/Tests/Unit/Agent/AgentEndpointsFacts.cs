using System.Runtime.CompilerServices;
using System.Text;
using DigitalBrain.AI.Agents;
using DigitalBrain.AI.Metering;
using DigitalBrain.Compute;
using DigitalBrain.Contracts;
using IntoChat.Agent;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace IntoChat.Tests;

public sealed class AgentEndpointsFacts
{
    // The /agent model input must carry the conversation summary once old turns are evicted.
    [Fact]
    public async Task RunModelFeedsTheConversationSummaryIntoTheSystemInstructions()
    {
        var runner = new RecordingRunner();
        var state = new AgentConversationState(3, null, [new("old-run", "earlier", "answer", [])], "the user asked about invoices");
        var text = new StringBuilder();
        var results = new List<string>();

        await AgentEndpoints.RunModel("scope", "run", "hello", developerMode: false, state, "run-reply",
            new ConfigurationBuilder().Build(), runner, new ToolSelection([], false), _ => Task.CompletedTask, text, results, new IntentActivity(), TestContext.Current.CancellationToken);

        Assert.NotNull(runner.Request);
        Assert.Contains("Earlier conversation summary: the user asked about invoices", runner.Request!.Instructions!, StringComparison.Ordinal);
    }

    // A stale open failure must not fail the run once a later live-table call succeeds.
    [Fact]
    public async Task SuccessfulTableReadClearsAnEarlierTableFailure()
    {
        var runner = new ToolEventRunner(
            new AgentTurnEvent.ToolCompleted("open", "show_supabase_query_table", """{"isError":true,"message":"PostgreSQL refused the query."}"""),
            new AgentTurnEvent.ToolCompleted("read", "table_read", """{"windowId":"table-x","rows":[],"rowsRead":0}"""));
        var results = new List<string>();

        var queryError = await AgentEndpoints.RunModel("scope", "run", "how many?", developerMode: false,
            EmptyState, "run-reply", new ConfigurationBuilder().Build(), runner, new ToolSelection([], false), _ => Task.CompletedTask, new StringBuilder(), results, new IntentActivity(), TestContext.Current.CancellationToken);

        Assert.Null(queryError);
        Assert.Empty(results);
    }

    [Fact]
    public async Task FailedTableReadAfterASuccessStillFailsTheRun()
    {
        var runner = new ToolEventRunner(
            new AgentTurnEvent.ToolCompleted("read", "table_read", """{"isError":true,"message":"No readable Public columns were requested."}"""));
        var results = new List<string>();

        var queryError = await AgentEndpoints.RunModel("scope", "run", "read it", developerMode: false,
            EmptyState, "run-reply", new ConfigurationBuilder().Build(), runner, new ToolSelection([], false), _ => Task.CompletedTask, new StringBuilder(), results, new IntentActivity(), TestContext.Current.CancellationToken);

        Assert.Equal("No readable Public columns were requested.", queryError);
    }

    private static AgentConversationState EmptyState => new(0, null, [], null);

    // The shadow Compute on a receipt is the intent's durable usage priced by the one price book.
    [Fact]
    public void ShadowPriceSumsEveryTokenClassForTheIntent()
    {
        using var intent = IntentContext.Begin("intent-shadow", "scope");
        intent.AddUsage(new TokenUsageEntry(MeterKind.Chat, "OpenAI", "gpt-5.6-luna",
            1_000_000, 0, null, 1_000_000, 2_000_000, true, DateTimeOffset.UtcNow));

        var (modelCalls, compute) = AgentReceipts.ShadowPrice(intent, new PriceBook());

        Assert.Equal(1, modelCalls);
        Assert.Equal(250m + 1000m, compute);
    }

    private sealed class ToolEventRunner(params AgentTurnEvent[] events) : IAgentTurnRunner
    {
        public async IAsyncEnumerable<AgentTurnEvent> RunAsync(AgentTurnRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            foreach (var item in events) { yield return item; }
            yield return new AgentTurnEvent.Finished();
        }
    }

    private sealed class RecordingRunner : IAgentTurnRunner
    {
        public AgentTurnRequest? Request { get; private set; }
        public async IAsyncEnumerable<AgentTurnEvent> RunAsync(AgentTurnRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            Request = request;
            await Task.Yield();
            yield return new AgentTurnEvent.Finished();
        }
    }
}