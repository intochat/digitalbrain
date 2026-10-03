using System.Runtime.CompilerServices;
using System.Text;
using DigitalBrain;
using DigitalBrain.AI;
using DigitalBrain.AI.Agents;
using DigitalBrain.AI.Metering;
using DigitalBrain.Assistant;
using DigitalBrain.Compute;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel;
using Xunit;

namespace DigitalBrain.Modules.Assistant.Tests.Unit;

public sealed class AgentTurnFacts
{
    [Fact]
    public async Task RunModelForwardsTheSelectedProfileInsteadOfTheServerDefault()
    {
        var runner = new RecordingRunner();
        var selected = new AgentModelSelection(Profile: "work");
        var configuration = new AssistantOptions { Model = "IGpt56Luna" };

        await AssistantTurnExecution.RunModel("scope", "run", "hello", AssistantDefinition.Product, EmptyState, "reply",
            configuration, runner.RunAsync, _ => Task.CompletedTask,
            new StringBuilder(), [], new IntentActivity(), TestContext.Current.CancellationToken, selected);

        Assert.Equal(selected, runner.Request!.Model);
    }

    // The HTTP adapter must forward the application neuron's instructions unchanged.
    [Fact]
    public async Task RunModelPreservesTheApplicationDefinitionWithoutRecomposingIt()
    {
        var runner = new RecordingRunner();
        var state = new AgentConversationState(3, null, [new("old-run", "earlier", "answer", [])], "the user asked about invoices");
        var definition = new AgentDefinition { Instructions = "Application-owned instructions and summary", Tools = ["custom_tool"] };
        var text = new StringBuilder();
        var results = new List<string>();

        await AssistantTurnExecution.RunModel("scope", "run", "hello", definition, state, "run-reply",
            new AssistantOptions(), runner.RunAsync, _ => Task.CompletedTask, text, results, new IntentActivity(), TestContext.Current.CancellationToken);

        Assert.NotNull(runner.Request);
        Assert.Equal(definition.Instructions, runner.Request!.Instructions);
        Assert.Equal(definition.Tools, runner.Request.ToolNames);
    }

    // A stale open failure must not fail the run once a later live-table call succeeds.
    [Fact]
    public async Task SuccessfulTableReadClearsAnEarlierTableFailure()
    {
        var runner = new ToolEventRunner(
            new AgentTurnEvent.ToolCompleted("open", "show_supabase_query_table", """{"isError":true,"message":"PostgreSQL refused the query."}"""),
            new AgentTurnEvent.ToolCompleted("read", "table_read", """{"windowId":"table-x","rows":[],"rowsRead":0}"""));
        var results = new List<string>();

        var queryError = await AssistantTurnExecution.RunModel("scope", "run", "how many?", AssistantDefinition.Product,
            EmptyState, "run-reply", new AssistantOptions(), runner.RunAsync, _ => Task.CompletedTask, new StringBuilder(), results, new IntentActivity(), TestContext.Current.CancellationToken);

        Assert.Null(queryError);
        Assert.Empty(results);
    }

    [Fact]
    public async Task FailedTableReadAfterASuccessStillFailsTheRun()
    {
        var runner = new ToolEventRunner(
            new AgentTurnEvent.ToolCompleted("read", "table_read", """{"isError":true,"message":"No readable Public columns were requested."}"""));
        var results = new List<string>();

        var queryError = await AssistantTurnExecution.RunModel("scope", "run", "read it", AssistantDefinition.Product,
            EmptyState, "run-reply", new AssistantOptions(), runner.RunAsync, _ => Task.CompletedTask, new StringBuilder(), results, new IntentActivity(), TestContext.Current.CancellationToken);

        Assert.Equal("No readable Public columns were requested.", queryError);
    }

    [Fact]
    public async Task PostgresWindowResultIsRecorded()
    {
        var results = new List<string>();
        var runner = new ToolEventRunner(new AgentTurnEvent.ToolCompleted("open", "show_postgres_query_table", """{"windowId":"pg-table","source":"postgres"}"""));
        var error = await AssistantTurnExecution.RunModel("scope", "run", "show postgres", AssistantDefinition.Product,
            EmptyState, "reply", new AssistantOptions(), runner.RunAsync, _ => Task.CompletedTask,
            new StringBuilder(), results, new IntentActivity(), TestContext.Current.CancellationToken);
        Assert.Null(error);
        Assert.Equal("pg-table", Assert.Single(results));
    }
    [Fact]
    public async Task SchemaRetryDoesNotEraseFailedPostgresWindow()
    {
        var runner = new ToolEventRunner(
            new AgentTurnEvent.ToolCompleted("open", "show_postgres_query_table", """{"isError":true,"message":"Query refused"}"""),
            new AgentTurnEvent.ToolCompleted("schema", "postgres_schema", """{"database":"research","tables":[]}"""));
        var error = await AssistantTurnExecution.RunModel("scope", "run", "show postgres", AssistantDefinition.Product,
            EmptyState, "reply", new AssistantOptions(), runner.RunAsync, _ => Task.CompletedTask,
            new StringBuilder(), [], new IntentActivity(), TestContext.Current.CancellationToken);
        Assert.Equal("Query refused", error);
    }
    private static AgentConversationState EmptyState => new(0, null, [], null);

    // The shadow Compute on a receipt is the intent's durable usage priced by the one price book.
    [Fact]
    public void ShadowPriceSumsEveryTokenClassForTheIntent()
    {
        using var intent = IntentContext.Begin("intent-shadow", "scope");
        intent.AddUsage(new TokenUsageEntry(MeterKind.Chat, "OpenAI", "gpt-5.6-luna",
            1_000_000, 0, null, 1_000_000, 2_000_000, true, DateTimeOffset.UtcNow));

        var (modelCalls, compute) = AgentReceipts.ShadowPrice(intent.Usage.OfType<TokenUsageEntry>(), new PriceBook());

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
